import argparse
import importlib.util
from pathlib import Path

import numpy as np
import pandas as pd


priority_path = Path(__file__).with_name("Analyze-OPFDynamicPolicyPriority.py")
priority_spec = importlib.util.spec_from_file_location("opf_dynamic_priority", priority_path)
priority_module = importlib.util.module_from_spec(priority_spec)
priority_spec.loader.exec_module(priority_module)
load_rows = priority_module.load_rows
choose_policy = priority_module.choose_policy
simulate = priority_module.simulate


GROUP = ["Side", "ResearchPath", "ExitPolicy"]
NUMERIC = [
    "RegimeScore",
    "SetupQualityScore",
    "InitialRiskPoints",
    "EstimatedRR",
    "ATR14",
    "ZoneWidth",
    "ZoneTouchCount",
    "PullbackCountInRegime",
    "CandleRange",
    "BodyRatio",
    "CloseLocation",
    "ZoneAge",
    "BullScore",
    "BearScore",
    "TrendStrength",
    "Alignment",
    "ConfirmRange",
    "ConfirmBodyRatio",
    "ConfirmCloseLocation",
    "ConfirmAlignment",
    "ConfirmPrevBreak",
    "ConfirmZoneReclaim",
    "HourSin",
    "HourCos",
    "DayOfWeek",
]
CATEGORICAL = [
    "Season",
    "Session",
    "ZoneType",
    "ZoneFreshness",
    "RegimeBucket",
    "SetupQualityBucket",
    "RiskBucket",
    "EstimatedRRBucket",
    "TimeBucket",
    "SkipPattern",
    "HasZoneBirthResearch",
    "HasObservationConfirmResearch",
    "HasObservationStrictResearch",
    "HasFailureReverseResearch",
    "HasFailureRetestTrigger",
    "HasBreakawayQualified",
    "ConfirmAvailable",
    "ConfirmReason",
]
TARGETS = ["OutcomeR", "OutcomeClipped", "OutcomePositive"]


def add_features(rows: pd.DataFrame) -> pd.DataFrame:
    rows = rows.copy()
    hour = rows["EntryTime"].dt.hour + rows["EntryTime"].dt.minute / 60
    rows["HourSin"] = np.sin(2 * np.pi * hour / 24)
    rows["HourCos"] = np.cos(2 * np.pi * hour / 24)
    rows["DayOfWeek"] = rows["EntryTime"].dt.dayofweek
    risk = rows["InitialRiskPoints_Policy"].fillna(rows["InitialRiskPoints"]).clip(lower=0.25)
    rows["OutcomeR"] = rows["OutcomeNet"] / (4 * risk)
    rows["OutcomeClipped"] = rows["OutcomeNet"].clip(lower=-100, upper=150)
    rows["OutcomePositive"] = rows["OutcomeNet"].gt(0).astype(float)
    return rows


def design(train: pd.DataFrame, test: pd.DataFrame):
    combined = pd.concat([train, test], ignore_index=True)
    numeric = combined[NUMERIC].fillna(0).astype(float)
    train_numeric = numeric.iloc[: len(train)]
    numeric = ((numeric - train_numeric.mean()) / train_numeric.std().replace(0, 1)).clip(-5, 5)
    categorical = pd.get_dummies(
        combined[CATEGORICAL].fillna("Unknown").astype(str), dtype=float
    )
    numeric_matrix = numeric.to_numpy()
    categorical_matrix = categorical.to_numpy()
    return (
        numeric_matrix[: len(train)],
        numeric_matrix[len(train) :],
        categorical_matrix[: len(train)],
        categorical_matrix[len(train) :],
    )


def predict_fold(
    train: pd.DataFrame,
    test: pd.DataFrame,
    ks: tuple[int, ...],
    category_weights: tuple[float, ...],
    shrinkages: tuple[float, ...],
):
    predictions = {
        (target, k, category_weight, shrinkage): np.zeros(len(test))
        for target in TARGETS
        for k in ks
        for category_weight in category_weights
        for shrinkage in shrinkages
    }
    global_means = train[TARGETS].mean()
    for group_key, test_group in test.groupby(GROUP, sort=False):
        mask = np.logical_and.reduce(
            [train[column].eq(value).to_numpy() for column, value in zip(GROUP, group_key)]
        )
        train_group = train.loc[mask]
        test_positions = test.index.get_indexer(test_group.index)
        if train_group.empty:
            for target in TARGETS:
                for k in ks:
                    for category_weight in category_weights:
                        for shrinkage in shrinkages:
                            predictions[(target, k, category_weight, shrinkage)][test_positions] = global_means[target]
            continue

        train_numeric, test_numeric, train_categorical, test_categorical = design(train_group, test_group)
        target_values = train_group[TARGETS].to_numpy()
        max_k = min(max(ks), len(train_group))
        train_numeric_norm = np.sum(train_numeric * train_numeric, axis=1)
        train_categorical_norm = np.sum(train_categorical * train_categorical, axis=1)
        for start in range(0, len(test_group), 256):
            stop = min(start + 256, len(test_group))
            numeric_block = test_numeric[start:stop]
            categorical_block = test_categorical[start:stop]
            numeric_distances = np.maximum(
                np.sum(numeric_block * numeric_block, axis=1)[:, None]
                + train_numeric_norm[None, :]
                - 2 * numeric_block @ train_numeric.T,
                0,
            )
            categorical_distances = np.maximum(
                np.sum(categorical_block * categorical_block, axis=1)[:, None]
                + train_categorical_norm[None, :]
                - 2 * categorical_block @ train_categorical.T,
                0,
            )
            for category_weight in category_weights:
                distances = numeric_distances + category_weight * category_weight * categorical_distances
                neighbor_indices = np.argpartition(distances, max_k - 1, axis=1)[:, :max_k]
                neighbor_distances = np.take_along_axis(distances, neighbor_indices, axis=1)
                order = np.argsort(neighbor_distances, axis=1)
                neighbor_indices = np.take_along_axis(neighbor_indices, order, axis=1)
                neighbor_distances = np.take_along_axis(neighbor_distances, order, axis=1)
                for k in ks:
                    actual_k = min(k, max_k)
                    indices = neighbor_indices[:, :actual_k]
                    weights = 1 / (np.sqrt(neighbor_distances[:, :actual_k]) + 0.5)
                    values = target_values[indices]
                    weighted = np.sum(values * weights[:, :, None], axis=1)
                    denominator = np.sum(weights, axis=1)[:, None]
                    for shrinkage in shrinkages:
                        estimates = (
                            weighted + shrinkage * global_means.to_numpy()
                        ) / (denominator + shrinkage)
                        for target_index, target in enumerate(TARGETS):
                            predictions[(target, k, category_weight, shrinkage)][test_positions[start:stop]] = estimates[:, target_index]
    return predictions


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--h1", type=Path, required=True)
    parser.add_argument("--q4", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()

    ks = (40,)
    category_weights = (0.75,)
    shrinkages = (0.0, 5.0, 15.0)
    thresholds = {
        "OutcomeR": (-999, -0.25, 0, 0.05, 0.075, 0.1, 0.125, 0.15, 0.2, 0.3),
    }
    results = []
    observed = pd.concat(
        [load_rows(args.h1, False), load_rows(args.q4, False)], ignore_index=True
    )
    for conservative in (False, True):
        combined = observed.copy()
        if conservative:
            ambiguous = combined["AmbiguousStopAndTargetSameBar"].astype(str).eq("True")
            risk = combined["InitialRiskPoints_Policy"].fillna(combined["InitialRiskPoints"])
            combined.loc[ambiguous, "OutcomeNet"] = -4 * risk[ambiguous] - 2.4
        combined = add_features(combined)
        scored = {
            (target, k, category_weight, shrinkage): []
            for target in thresholds
            for k in ks
            for category_weight in category_weights
            for shrinkage in shrinkages
        }
        for month in sorted(combined["Month"].unique()):
            train = combined[combined["Month"].ne(month)].copy().reset_index(drop=True)
            test = combined[combined["Month"].eq(month)].copy().reset_index(drop=True)
            predictions = predict_fold(train, test, ks, category_weights, shrinkages)
            for k in ks:
                for category_weight in category_weights:
                    for shrinkage in shrinkages:
                        for target in thresholds:
                            if target == "OutcomeRScaled":
                                risk = test["InitialRiskPoints_Policy"].fillna(test["InitialRiskPoints"]).clip(lower=0.25)
                                score = predictions[("OutcomeR", k, category_weight, shrinkage)] * 4 * risk
                            else:
                                score = predictions[(target, k, category_weight, shrinkage)]
                            scored[(target, k, category_weight, shrinkage)].append(test.assign(Score=score))

        for (target, k, category_weight, shrinkage), parts in scored.items():
            selected = choose_policy(pd.concat(parts, ignore_index=True))
            for threshold in thresholds[target]:
                for limit in (15, 0):
                    trades, net, periods = simulate(selected, threshold, limit)
                    results.append(
                        {
                            "Mode": "Conservative" if conservative else "Observed",
                            "TargetModel": target,
                            "K": k,
                            "CategoryWeight": category_weight,
                            "Shrinkage": shrinkage,
                            "Threshold": threshold,
                            "DailyLimit": limit,
                            "H1Trades": periods["H1"][0],
                            "H1Net": round(periods["H1"][1], 2),
                            "Q4Trades": periods["Q4"][0],
                            "Q4Net": round(periods["Q4"][1], 2),
                            "CombinedTrades": trades,
                            "CombinedNet": net,
                        }
                    )

    output = pd.DataFrame(results).sort_values(["Mode", "CombinedNet"], ascending=[True, False])
    args.output.parent.mkdir(parents=True, exist_ok=True)
    output.to_csv(args.output, index=False)
    print(output.groupby("Mode").head(15).to_string(index=False))


if __name__ == "__main__":
    main()
