import argparse
import csv
import gzip
import hashlib
import json
import math
from pathlib import Path


def parse_pairs(value):
    pairs = {}
    if not value:
        return pairs
    for item in value.split(";"):
        name, raw = item.split("=", 1)
        pairs[name] = raw
    return pairs


def parse_bool(value):
    return value.strip().lower() == "true"


def group_key(side, research_path):
    return f"{side}|{research_path}"


def score_group(model, group, numeric, categorical_codes):
    standardized = []
    for value, mean, std in zip(numeric, group["means"], group["stds"]):
        scale = std if std != 0 else 1
        standardized.append(max(-5, min(5, (value - mean) / scale)))

    numeric_count = len(model["numericFeatures"])
    categorical_count = len(model["categoricalFeatures"])
    distances = []
    for reference in group["references"]:
        distance = sum(
            (value - reference[index]) ** 2
            for index, value in enumerate(standardized)
        )
        mismatches = sum(
            code != int(reference[numeric_count + index])
            for index, code in enumerate(categorical_codes)
        )
        distance += model["categoryWeight"] ** 2 * 2 * mismatches
        distances.append(
            (
                distance,
                reference[numeric_count + categorical_count],
                reference[numeric_count + categorical_count + 1],
            )
        )

    neighbors = sorted(distances, key=lambda item: item[0])[: min(model["k"], len(distances))]
    weights = [1 / (math.sqrt(item[0]) + 0.5) for item in neighbors]
    weight_sum = sum(weights)
    observed_weighted = sum(weight * item[1] for weight, item in zip(weights, neighbors))
    conservative_weighted = sum(weight * item[2] for weight, item in zip(weights, neighbors))
    observed = (
        observed_weighted + model["observedShrinkage"] * model["observedGlobalMeanR"]
    ) / (weight_sum + model["observedShrinkage"])
    conservative = (
        conservative_weighted
        + model["conservativeShrinkage"] * model["conservativeGlobalMeanR"]
    ) / (weight_sum + model["conservativeShrinkage"])
    return observed, conservative, len(neighbors)


def score(model, groups_by_path, side, research_path, numeric, categorical):
    groups = groups_by_path.get(group_key(side, research_path), [])
    if not groups:
        return None

    categorical_codes = []
    for feature, value in zip(model["categoricalFeatures"], categorical):
        values = model["categories"][feature]
        categorical_codes.append(values.index(value) if value in values else -1)

    scored = []
    for group in groups:
        observed, conservative, reference_count = score_group(
            model, group, numeric, categorical_codes
        )
        scored.append((group["exitPolicy"], observed, conservative, reference_count))
    observed_best = max(scored, key=lambda item: item[1])
    conservative_best = max(scored, key=lambda item: item[2])
    return observed_best, conservative_best


def compare_row(row, model, model_sha256, groups_by_path, tolerance):
    numeric_pairs = parse_pairs(row["NumericFeatures"])
    categorical_pairs = parse_pairs(row["CategoricalFeatures"])
    numeric = [float(numeric_pairs[name]) for name in model["numericFeatures"]]
    categorical = [categorical_pairs[name] for name in model["categoricalFeatures"]]
    result = score(model, groups_by_path, row["Side"], row["ResearchPath"], numeric, categorical)

    errors = []
    if row["ModelVersion"] != model["modelVersion"]:
        errors.append("ModelVersion")
    if row["ModelSHA256"].upper() != model_sha256:
        errors.append("ModelSHA256")
    if result is None:
        if row["UnavailableReason"] != "PathMissing":
            errors.append("UnavailableReason")
        return errors

    observed, conservative = result
    expected = {
        "ObservedPolicy": observed[0],
        "ObservedScoreR": observed[1],
        "ObservedGatePassed": observed[1] >= model["gateThresholdR"],
        "ObservedReferenceCount": observed[3],
        "ConservativePolicy": conservative[0],
        "ConservativeScoreR": conservative[2],
        "ConservativeGatePassed": conservative[2] >= model["gateThresholdR"],
        "ConservativeReferenceCount": conservative[3],
    }
    for name in ("ObservedPolicy", "ConservativePolicy"):
        if row[name] != expected[name]:
            errors.append(name)
    for name in ("ObservedScoreR", "ConservativeScoreR"):
        if abs(float(row[name]) - expected[name]) > tolerance:
            errors.append(name)
    for name in ("ObservedGatePassed", "ConservativeGatePassed"):
        if parse_bool(row[name]) != expected[name]:
            errors.append(name)
    for name in ("ObservedReferenceCount", "ConservativeReferenceCount"):
        if int(row[name]) != expected[name]:
            errors.append(name)
    if row["UnavailableReason"]:
        errors.append("UnavailableReason")
    return errors


def main():
    parser = argparse.ArgumentParser(
        description="Verify Python/C# parity for OPF v1.97 KNN shadow decision logs."
    )
    parser.add_argument("--model", type=Path, required=True)
    parser.add_argument("--logs", type=Path, required=True)
    parser.add_argument("--tolerance", type=float, default=1e-7)
    args = parser.parse_args()

    compressed = args.model.read_bytes()
    model_sha256 = hashlib.sha256(compressed).hexdigest().upper()
    model = json.loads(gzip.decompress(compressed))
    groups_by_path = {}
    for group in model["groups"]:
        groups_by_path.setdefault(group_key(group["side"], group["researchPath"]), []).append(group)

    files = sorted(args.logs.glob("*_knn_shadow_decisions.csv"))
    if not files:
        raise SystemExit(f"No *_knn_shadow_decisions.csv files found under {args.logs}")

    checked = 0
    mismatches = []
    for path in files:
        with path.open("r", encoding="utf-8-sig", newline="") as stream:
            for row_number, row in enumerate(csv.DictReader(stream), start=2):
                checked += 1
                try:
                    errors = compare_row(row, model, model_sha256, groups_by_path, args.tolerance)
                except (KeyError, ValueError) as error:
                    errors = [f"ParseError:{error}"]
                if errors:
                    mismatches.append((path.name, row_number, row.get("SignalID", ""), errors))

    print(
        f"model={model['modelVersion']} sha256={model_sha256} "
        f"files={len(files)} rows={checked} mismatches={len(mismatches)}"
    )
    for path, row_number, signal_id, errors in mismatches[:20]:
        print(f"MISMATCH file={path} row={row_number} signal={signal_id} fields={'|'.join(errors)}")
    if mismatches:
        raise SystemExit(1)


if __name__ == "__main__":
    main()
