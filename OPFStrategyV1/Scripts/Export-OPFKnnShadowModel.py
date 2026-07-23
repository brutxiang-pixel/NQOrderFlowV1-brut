import argparse
import gzip
import hashlib
import importlib.util
import json
from pathlib import Path

import numpy as np
import pandas as pd


script_path = Path(__file__).with_name("Analyze-OPFDynamicPolicyKnn.py")
spec = importlib.util.spec_from_file_location("opf_knn", script_path)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--h1", type=Path, required=True)
    parser.add_argument("--q4", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()

    rows = module.add_features(
        pd.concat(
            [module.load_rows(args.h1, False), module.load_rows(args.q4, False)],
            ignore_index=True,
        )
    )
    policy_risk = rows["InitialRiskPoints_Policy"].fillna(rows["InitialRiskPoints"]).clip(lower=0.25)
    rows["ObservedR"] = rows["OutcomeNet"] / (4 * policy_risk)
    conservative_net = rows["OutcomeNet"].copy()
    ambiguous = rows["AmbiguousStopAndTargetSameBar"].astype(str).eq("True")
    conservative_net.loc[ambiguous] = -4 * policy_risk.loc[ambiguous] - 2.4
    rows["ConservativeR"] = conservative_net / (4 * policy_risk)

    categories = {}
    category_codes = {}
    for feature in module.CATEGORICAL:
        values = rows[feature].fillna("Unknown").astype(str)
        ordered = sorted(values.unique().tolist())
        categories[feature] = ordered
        mapping = {value: index for index, value in enumerate(ordered)}
        category_codes[feature] = values.map(mapping).astype(int)

    rows = rows.copy()
    for feature in module.CATEGORICAL:
        rows[f"Category_{feature}"] = category_codes[feature]

    groups = []
    for (side, research_path, exit_policy), group in rows.groupby(module.GROUP, sort=True):
        numeric = group[module.NUMERIC].fillna(0).astype(float)
        means = numeric.mean()
        stds = numeric.std().replace(0, 1).fillna(1)
        standardized = ((numeric - means) / stds).clip(-5, 5)
        category_matrix = group[[f"Category_{feature}" for feature in module.CATEGORICAL]].to_numpy()
        target_matrix = group[["ObservedR", "ConservativeR"]].to_numpy()
        references = np.column_stack([standardized.to_numpy(), category_matrix, target_matrix])
        groups.append(
            {
                "side": side,
                "researchPath": research_path,
                "exitPolicy": exit_policy,
                "means": [round(float(value), 10) for value in means],
                "stds": [round(float(value), 10) for value in stds],
                "references": [[round(float(value), 10) for value in row] for row in references],
            }
        )

    model = {
        "modelVersion": "OPF_KNN_SHADOW_1.97",
        "sourceResearchSchema": "OPF_RESEARCH_1.94",
        "trainingSnapshots": {"q4": 62, "h1": 122},
        "k": 40,
        "categoryWeight": 0.75,
        "gateThresholdR": 0.075,
        "observedShrinkage": 0.0,
        "conservativeShrinkage": 15.0,
        "observedGlobalMeanR": round(float(rows["ObservedR"].mean()), 10),
        "conservativeGlobalMeanR": round(float(rows["ConservativeR"].mean()), 10),
        "numericFeatures": module.NUMERIC,
        "categoricalFeatures": module.CATEGORICAL,
        "categories": categories,
        "groups": groups,
    }

    args.output.parent.mkdir(parents=True, exist_ok=True)
    payload = json.dumps(model, separators=(",", ":"), ensure_ascii=True).encode("utf-8")
    with args.output.open("wb") as output_stream:
        with gzip.GzipFile(fileobj=output_stream, mode="wb", compresslevel=9, mtime=0) as stream:
            stream.write(payload)
    digest = hashlib.sha256(args.output.read_bytes()).hexdigest().upper()
    print(f"groups={len(groups)} references={len(rows)} jsonBytes={len(payload)} gzipBytes={args.output.stat().st_size}")
    print(f"sha256={digest}")


if __name__ == "__main__":
    main()
