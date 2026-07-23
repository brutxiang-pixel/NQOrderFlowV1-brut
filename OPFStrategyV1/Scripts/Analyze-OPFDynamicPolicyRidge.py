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


CATEGORICAL = [
    "Side",
    "ResearchPath",
    "ExitPolicy",
    "Season",
    "RegimeScore",
    "QualityBin",
    "RiskBin",
    "Session",
    "ZoneType",
    "ZoneFreshness",
    "TouchBin",
    "AtrBin",
    "RegimeBucket",
    "SetupQualityBucket",
    "RiskBucket",
    "EstimatedRRBucket",
    "TimeBucket",
    "RangeBin",
    "BodyRatioBin",
    "CloseLocationBin",
    "ZoneWidthBin",
    "ZoneAgeBin",
    "PullbackBin",
    "TrendStrengthBin",
    "AlignmentBin",
    "SkipPattern",
    "HasZoneBirthResearch",
    "HasObservationConfirmResearch",
    "HasObservationStrictResearch",
    "HasFailureReverseResearch",
    "HasFailureRetestTrigger",
    "HasBreakawayQualified",
    "ConfirmAvailable",
    "ConfirmReason",
    "ConfirmRangeBin",
    "ConfirmBodyRatioBin",
    "ConfirmCloseLocationBin",
    "ConfirmAlignmentBin",
    "ConfirmPrevBreakBin",
    "ConfirmZoneReclaimBin",
]

NUMERIC = [
    "SetupQualityScore",
    "InitialRiskPoints",
    "EstimatedRR",
    "ATR14",
    "ZoneWidth",
    "PullbackCountInRegime",
    "BullScore",
    "BearScore",
]


def add_interactions(rows: pd.DataFrame) -> pd.DataFrame:
    rows = rows.copy()
    path = rows["Side"].astype(str) + "|" + rows["ResearchPath"].astype(str)
    policy = rows["ExitPolicy"].astype(str)
    rows["PathPolicy"] = path + "|" + policy
    rows["PathRegimePolicy"] = rows["PathPolicy"] + "|" + rows["RegimeScore"].astype(str)
    rows["PathSessionPolicy"] = rows["PathPolicy"] + "|" + rows["Session"].astype(str)
    rows["PathQualityPolicy"] = rows["PathPolicy"] + "|" + rows["QualityBin"].astype(str)
    rows["PathRiskPolicy"] = rows["PathPolicy"] + "|" + rows["RiskBin"].astype(str)
    rows["PathConfirmPolicy"] = rows["PathPolicy"] + "|" + rows["ConfirmReason"].astype(str)
    return rows


def ridge_design(train: pd.DataFrame, test: pd.DataFrame):
    combined = add_interactions(pd.concat([train, test], ignore_index=True))
    interaction_columns = [
        "PathPolicy",
        "PathRegimePolicy",
        "PathSessionPolicy",
        "PathQualityPolicy",
        "PathRiskPolicy",
        "PathConfirmPolicy",
    ]
    categorical = pd.get_dummies(
        combined[CATEGORICAL + interaction_columns].fillna("Unknown").astype(str),
        dtype=float,
    )
    numeric = combined[NUMERIC].fillna(0).astype(float)
    train_numeric = numeric.iloc[: len(train)]
    numeric = (numeric - train_numeric.mean()) / train_numeric.std().replace(0, 1)
    matrix = np.column_stack([np.ones(len(combined)), numeric.to_numpy(), categorical.to_numpy()])
    return matrix[: len(train)], matrix[len(train) :]


def ridge_predictions(train: pd.DataFrame, test: pd.DataFrame, alphas: tuple[float, ...]):
    train_x, test_x = ridge_design(train, test)
    xtx = train_x.T @ train_x
    target_names = ["OutcomeNet", "OutcomeClipped", "OutcomeR", "OutcomePositive"]
    xty = train_x.T @ train[target_names].to_numpy()
    predictions = {}
    for alpha in alphas:
        penalty = np.eye(train_x.shape[1]) * alpha
        penalty[0, 0] = 0
        beta = np.linalg.solve(xtx + penalty, xty)
        values = test_x @ beta
        for index, target_name in enumerate(target_names):
            predictions[(alpha, target_name)] = values[:, index]
    return predictions


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--h1", type=Path, required=True)
    parser.add_argument("--q4", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()

    alphas = (100.0, 1000.0, 10000.0)
    results = []
    for conservative in (False, True):
        combined = pd.concat(
            [load_rows(args.h1, conservative), load_rows(args.q4, conservative)],
            ignore_index=True,
        )
        policy_risk = combined["InitialRiskPoints_Policy"].fillna(combined["InitialRiskPoints"]).clip(lower=0.25)
        combined["OutcomeClipped"] = combined["OutcomeNet"].clip(lower=-100, upper=150)
        combined["OutcomeR"] = combined["OutcomeNet"] / (4 * policy_risk)
        combined["OutcomePositive"] = combined["OutcomeNet"].gt(0).astype(float)
        target_thresholds = {
            "OutcomeNet": (-999, -10, -5, 0, 5, 10),
            "OutcomeClipped": (-999, -10, -5, 0, 5, 10),
            "OutcomeR": (-999, -0.25, 0, 0.1, 0.2, 0.3),
            "OutcomePositive": (-999, 0.4, 0.45, 0.5, 0.55, 0.6),
            "OutcomeRScaled": (-999, -10, -5, 0, 5, 10),
        }
        scored = {(alpha, target): [] for alpha in alphas for target in target_thresholds}
        for month in sorted(combined["Month"].unique()):
            train = combined[combined["Month"].ne(month)].copy()
            test = combined[combined["Month"].eq(month)].copy()
            predictions = ridge_predictions(train, test, alphas)
            for alpha in alphas:
                for target_name in target_thresholds:
                    if target_name == "OutcomeRScaled":
                        test_risk = test["InitialRiskPoints_Policy"].fillna(test["InitialRiskPoints"]).clip(lower=0.25)
                        score_values = predictions[(alpha, "OutcomeR")] * 4 * test_risk
                    else:
                        score_values = predictions[(alpha, target_name)]
                    scored[(alpha, target_name)].append(test.assign(Score=score_values))

        for (alpha, target_name), parts in scored.items():
            selected = choose_policy(pd.concat(parts, ignore_index=True))
            for threshold in target_thresholds[target_name]:
                for limit in (15, 0):
                    trades, net, periods = simulate(selected, threshold, limit)
                    results.append(
                        {
                            "Mode": "Conservative" if conservative else "Observed",
                            "TargetModel": target_name,
                            "Alpha": alpha,
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
