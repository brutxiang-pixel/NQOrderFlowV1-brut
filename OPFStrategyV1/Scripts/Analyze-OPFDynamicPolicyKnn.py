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

rich_path = Path(__file__).with_name("Connect-OPFRichFeatures.py")
rich_spec = importlib.util.spec_from_file_location("opf_rich_connector", rich_path)
rich_module = importlib.util.module_from_spec(rich_spec)
rich_spec.loader.exec_module(rich_module)


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
    "ConfirmAvailable",
    "ConfirmReason",
]
TARGETS = ["OutcomeR", "OutcomeClipped", "OutcomePositive"]
RICH_COMPONENTS = [
    "SwingProgression",
    "VWAPSide",
    "OpeningRangeSide",
    "VwapCrossCount",
    "DirectionalDisplacement",
    "AtrExpansion",
]
RICH_TREND_NUMERIC = [
    "RichAlignedScore",
    "RichOpposingScore",
    "RichScoreDelta",
    *[f"RichAligned{component}Contribution" for component in RICH_COMPONENTS],
    *[f"RichOpposing{component}Contribution" for component in RICH_COMPONENTS],
    "RichAlignedVWAPSideCount",
    "RichAlignedOpeningRangeCount",
    "RichVwapCrossCount",
    "RichDirectionalAtr14",
    "RichAtrExpansionRatio",
]
RICH_TREND_CATEGORICAL = [
    "RichAvailable",
    "RichAlignedSwingRaw",
    "RichOpposingSwingRaw",
    "RichAlignedVWAPSideRaw",
    "RichAlignedOpeningRangeRaw",
    "RichVwapCrossRaw",
    "RichDirectionalState",
    "RichAtrExpansionState",
]
RICH_VOLUME_VWAP_NUMERIC = [
    "RichLogVolume",
    "RichRelativeVolume20",
    "RichAlignedVwapDistanceAtr",
    "RichAbsoluteVwapDistanceAtr",
]
RICH_REGIME_NUMERIC = ["RichRegimeBarsLog"]
RICH_REGIME_CATEGORICAL = ["RichRegime"]


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


def add_rich_features(rows: pd.DataFrame) -> pd.DataFrame:
    rows = rows.copy()
    available = ~(
        rows["RichAverageVolume20"].fillna(0).eq(0)
        | rows["RichRelativeVolume20"].fillna(0).eq(0)
        | rows["RichAtr14"].fillna(0).eq(0)
    )
    is_long = rows["Side"].eq("Long")
    rows["RichAvailable"] = np.where(available, "True", "False")
    rows["RichAlignedScore"] = np.where(is_long, rows["RichBullScore"], rows["RichBearScore"])
    rows["RichOpposingScore"] = np.where(is_long, rows["RichBearScore"], rows["RichBullScore"])
    rows["RichScoreDelta"] = rows["RichAlignedScore"] - rows["RichOpposingScore"]
    for component in RICH_COMPONENTS:
        rows[f"RichAligned{component}Contribution"] = np.where(
            is_long,
            rows[f"RichBull{component}Contribution"],
            rows[f"RichBear{component}Contribution"],
        )
        rows[f"RichOpposing{component}Contribution"] = np.where(
            is_long,
            rows[f"RichBear{component}Contribution"],
            rows[f"RichBull{component}Contribution"],
        )

    rows["RichAlignedSwingRaw"] = np.where(
        is_long,
        rows["RichBullSwingProgressionRawValue"],
        rows["RichBearSwingProgressionRawValue"],
    )
    rows["RichOpposingSwingRaw"] = np.where(
        is_long,
        rows["RichBearSwingProgressionRawValue"],
        rows["RichBullSwingProgressionRawValue"],
    )
    rows["RichAlignedVWAPSideRaw"] = np.where(
        is_long, rows["RichBullVWAPSideRawValue"], rows["RichBearVWAPSideRawValue"]
    )
    rows["RichAlignedOpeningRangeRaw"] = np.where(
        is_long,
        rows["RichBullOpeningRangeSideRawValue"],
        rows["RichBearOpeningRangeSideRawValue"],
    )
    rows["RichVwapCrossRaw"] = rows["RichBullVwapCrossCountRawValue"]
    rows["RichDirectionalState"] = np.where(
        rows["RichBullDirectionalDisplacementRawValue"].eq("AtrUnavailable"),
        "Unavailable",
        "Available",
    )
    rows["RichAtrExpansionState"] = np.where(
        rows["RichBullAtrExpansionRawValue"].eq("InsufficientBars"),
        "InsufficientBars",
        "Available",
    )
    rows["RichAlignedVWAPSideCount"] = np.where(
        is_long,
        rows["RichBullVWAPSideRawValue"].str.extract(r"Count=(\d+)/5", expand=False),
        rows["RichBearVWAPSideRawValue"].str.extract(r"Count=(\d+)/5", expand=False),
    )
    rows["RichAlignedOpeningRangeCount"] = np.where(
        is_long,
        rows["RichBullOpeningRangeSideRawValue"].str.extract(r"Count=(\d+)/5", expand=False),
        rows["RichBearOpeningRangeSideRawValue"].str.extract(r"Count=(\d+)/5", expand=False),
    )
    rows["RichVwapCrossCount"] = rows["RichBullVwapCrossCountRawValue"].str.extract(
        r"Crosses=(\d+)", expand=False
    )
    rows["RichDirectionalAtr14"] = rows["RichBullDirectionalDisplacementRawValue"].str.extract(
        r"ATR14=([0-9.]+)", expand=False
    )
    expansion = rows["RichBullAtrExpansionRawValue"].str.extract(
        r"ATR14=([0-9.]+);Avg50=([0-9.]+)", expand=True
    ).astype(float)
    rows["RichAtrExpansionRatio"] = expansion[0] / expansion[1].replace(0, np.nan)
    for column in (
        "RichAlignedVWAPSideCount",
        "RichAlignedOpeningRangeCount",
        "RichVwapCrossCount",
        "RichDirectionalAtr14",
    ):
        rows[column] = pd.to_numeric(rows[column], errors="coerce")

    rows["RichLogVolume"] = np.log1p(rows["RichVolume"].clip(lower=0))
    rows["RichAlignedVwapDistanceAtr"] = np.where(
        is_long, rows["RichVwapDistanceAtr"], -rows["RichVwapDistanceAtr"]
    )
    rows["RichAbsoluteVwapDistanceAtr"] = rows["RichVwapDistanceAtr"].abs()
    rows["RichRegimeBarsLog"] = np.log1p(rows["RichRegimeBars"].clip(lower=0))

    rich_numeric = RICH_TREND_NUMERIC + RICH_VOLUME_VWAP_NUMERIC + RICH_REGIME_NUMERIC
    rows.loc[~available, rich_numeric] = np.nan
    rows.loc[~available, RICH_TREND_CATEGORICAL + RICH_REGIME_CATEGORICAL] = "Unavailable"
    return rows


def feature_set(name: str) -> tuple[list[str], list[str]]:
    rich_numeric = []
    rich_categorical = []
    if name == "trend":
        rich_numeric += RICH_TREND_NUMERIC
        rich_categorical += RICH_TREND_CATEGORICAL
    if name == "volume_vwap":
        rich_numeric += RICH_VOLUME_VWAP_NUMERIC
        rich_categorical += ["RichAvailable"]
    if name in {"regime", "regime_bars"}:
        rich_numeric += RICH_REGIME_NUMERIC
        rich_categorical += ["RichAvailable"]
    if name == "regime":
        rich_categorical += RICH_REGIME_CATEGORICAL
    return rich_numeric, list(dict.fromkeys(rich_categorical))


def squared_distances(test: np.ndarray, train: np.ndarray) -> np.ndarray:
    if train.shape[1] == 0:
        return np.zeros((len(test), len(train)))
    return np.maximum(
        np.sum(test * test, axis=1)[:, None]
        + np.sum(train * train, axis=1)[None, :]
        - 2 * test @ train.T,
        0,
    )


def design(
    train: pd.DataFrame,
    test: pd.DataFrame,
    rich_numeric: list[str],
    rich_categorical: list[str],
):
    combined = pd.concat([train, test], ignore_index=True)
    numeric_features = NUMERIC + rich_numeric
    numeric = combined[numeric_features].astype(float)
    numeric[NUMERIC] = numeric[NUMERIC].fillna(0)
    for column in rich_numeric:
        training_median = numeric[column].iloc[: len(train)].median()
        numeric[column] = numeric[column].fillna(0 if pd.isna(training_median) else training_median)
    train_numeric = numeric.iloc[: len(train)]
    numeric = ((numeric - train_numeric.mean()) / train_numeric.std().replace(0, 1)).clip(-5, 5)
    legacy_categorical = pd.get_dummies(
        combined[CATEGORICAL].fillna("Unknown").astype(str), dtype=float
    ).to_numpy()
    rich_categorical_matrix = pd.get_dummies(
        combined[rich_categorical].fillna("Unknown").astype(str), dtype=float
    ).to_numpy() if rich_categorical else np.empty((len(combined), 0))
    numeric_matrix = numeric.to_numpy()
    legacy_numeric_count = len(NUMERIC)
    return (
        numeric_matrix[: len(train), :legacy_numeric_count],
        numeric_matrix[len(train) :, :legacy_numeric_count],
        numeric_matrix[: len(train), legacy_numeric_count:],
        numeric_matrix[len(train) :, legacy_numeric_count:],
        legacy_categorical[: len(train)],
        legacy_categorical[len(train) :],
        rich_categorical_matrix[: len(train)],
        rich_categorical_matrix[len(train) :],
    )


def predict_fold(
    train: pd.DataFrame,
    test: pd.DataFrame,
    ks: tuple[int, ...],
    category_weights: tuple[float, ...],
    rich_block_weights: tuple[float, ...],
    shrinkages: tuple[float, ...],
    rich_numeric: list[str],
    rich_categorical: list[str],
    target_columns: tuple[str, ...],
):
    predictions = {
        (target, k, category_weight, rich_block_weight, shrinkage): np.zeros(len(test))
        for target in target_columns
        for k in ks
        for category_weight in category_weights
        for rich_block_weight in rich_block_weights
        for shrinkage in shrinkages
    }
    global_means = train[list(target_columns)].mean()
    for group_key, test_group in test.groupby(GROUP, sort=False):
        mask = np.logical_and.reduce(
            [train[column].eq(value).to_numpy() for column, value in zip(GROUP, group_key)]
        )
        train_group = train.loc[mask]
        test_positions = test.index.get_indexer(test_group.index)
        if train_group.empty:
            for target in target_columns:
                for k in ks:
                    for category_weight in category_weights:
                        for rich_block_weight in rich_block_weights:
                            for shrinkage in shrinkages:
                                predictions[(target, k, category_weight, rich_block_weight, shrinkage)][test_positions] = global_means[target]
            continue

        (
            train_legacy_numeric,
            test_legacy_numeric,
            train_rich_numeric,
            test_rich_numeric,
            train_legacy_categorical,
            test_legacy_categorical,
            train_rich_categorical,
            test_rich_categorical,
        ) = design(
            train_group,
            test_group,
            rich_numeric,
            rich_categorical,
        )
        target_values = train_group[list(target_columns)].to_numpy()
        max_k = min(max(ks), len(train_group))
        for start in range(0, len(test_group), 256):
            stop = min(start + 256, len(test_group))
            legacy_numeric_distances = squared_distances(
                test_legacy_numeric[start:stop], train_legacy_numeric
            )
            rich_numeric_distances = squared_distances(
                test_rich_numeric[start:stop], train_rich_numeric
            )
            legacy_categorical_distances = squared_distances(
                test_legacy_categorical[start:stop], train_legacy_categorical
            )
            rich_categorical_distances = squared_distances(
                test_rich_categorical[start:stop], train_rich_categorical
            )
            for category_weight in category_weights:
                legacy_distances = (
                    legacy_numeric_distances
                    + category_weight * category_weight * legacy_categorical_distances
                )
                rich_distances = (
                    rich_numeric_distances
                    + category_weight * category_weight * rich_categorical_distances
                )
                for rich_block_weight in rich_block_weights:
                    distances = legacy_distances + rich_block_weight * rich_block_weight * rich_distances
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
                            for target_index, target in enumerate(target_columns):
                                predictions[(target, k, category_weight, rich_block_weight, shrinkage)][test_positions[start:stop]] = estimates[:, target_index]
    return predictions


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--h1", type=Path, required=True)
    parser.add_argument("--q4", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--rich", type=Path)
    parser.add_argument("--rich-validation-output", type=Path)
    parser.add_argument(
        "--rich-feature-set",
        choices=("none", "trend", "volume_vwap", "regime", "regime_bars"),
        default="none",
    )
    parser.add_argument("--rich-block-weights", default="1.0")
    args = parser.parse_args()

    ks = (40,)
    category_weights = (0.75,)
    rich_block_weights = tuple(
        dict.fromkeys(float(value.strip()) for value in args.rich_block_weights.split(","))
    )
    if any(weight < 0 for weight in rich_block_weights):
        parser.error("--rich-block-weights must contain non-negative values")
    if args.rich_feature_set == "none":
        rich_block_weights = (0.0,)
    shrinkages = (0.0, 5.0, 15.0)
    thresholds = {
        "OutcomeR": (-999, -0.25, 0, 0.05, 0.075, 0.1, 0.125, 0.15, 0.2, 0.3),
    }
    results = []
    observed = pd.concat(
        [load_rows(args.h1, False), load_rows(args.q4, False)], ignore_index=True
    )
    if args.rich:
        legacy_columns = list(observed.columns)
        rich, selected = rich_module.load_rich_features(args.rich)
        rich_report = rich_module.validate_archives(
            args.h1,
            args.q4,
            rich,
            selected,
            require_zero_initialization_matches=False,
        )
        observed = rich_module.join_rich_features(observed, rich)
        candidate_initialization_zero = (
            observed["RichAverageVolume20"].fillna(0).eq(0)
            | observed["RichRelativeVolume20"].fillna(0).eq(0)
            | observed["RichAtr14"].fillna(0).eq(0)
        )
        rich_report["ModelCandidateInitializationZeroPolicyRows"] = int(candidate_initialization_zero.sum())
        rich_report["ModelCandidateInitializationZeroCandidates"] = int(
            observed.loc[candidate_initialization_zero]
            .drop_duplicates(["SnapshotID", "SignalID", "ResearchPath", "EntryBar", "EntryTime"])
            .shape[0]
        )
        if args.rich_feature_set == "none":
            observed = observed[legacy_columns]
        else:
            observed = add_rich_features(observed)
        if args.rich_validation_output:
            args.rich_validation_output.parent.mkdir(parents=True, exist_ok=True)
            pd.DataFrame([rich_report]).to_csv(args.rich_validation_output, index=False)
    elif args.rich_feature_set != "none":
        parser.error("--rich is required when --rich-feature-set is not 'none'")
    rich_numeric, rich_categorical = feature_set(args.rich_feature_set)
    observed_combined = add_features(observed.copy())
    conservative_combined = observed.copy()
    ambiguous = conservative_combined["AmbiguousStopAndTargetSameBar"].astype(str).eq("True")
    risk = conservative_combined["InitialRiskPoints_Policy"].fillna(
        conservative_combined["InitialRiskPoints"]
    )
    conservative_combined.loc[ambiguous, "OutcomeNet"] = -4 * risk[ambiguous] - 2.4
    conservative_combined = add_features(conservative_combined)
    combined = observed_combined.copy()
    combined["ObservedOutcomeR"] = observed_combined["OutcomeR"]
    combined["ConservativeOutcomeR"] = conservative_combined["OutcomeR"]
    combined["ConservativeOutcomeNet"] = conservative_combined["OutcomeNet"]
    target_columns = ("ObservedOutcomeR", "ConservativeOutcomeR")
    scored = {
        (mode, target, k, category_weight, rich_block_weight, shrinkage): []
        for mode in ("Observed", "Conservative")
        for target in thresholds
        for k in ks
        for category_weight in category_weights
        for rich_block_weight in rich_block_weights
        for shrinkage in shrinkages
    }
    for month in sorted(combined["Month"].unique()):
        train = combined[combined["Month"].ne(month)].copy().reset_index(drop=True)
        test = combined[combined["Month"].eq(month)].copy().reset_index(drop=True)
        predictions = predict_fold(
            train,
            test,
            ks,
            category_weights,
            rich_block_weights,
            shrinkages,
            rich_numeric,
            rich_categorical,
            target_columns,
        )
        for mode in ("Observed", "Conservative"):
            prediction_target = f"{mode}OutcomeR"
            for k in ks:
                for category_weight in category_weights:
                    for rich_block_weight in rich_block_weights:
                        for shrinkage in shrinkages:
                            for target in thresholds:
                                if target == "OutcomeRScaled":
                                    risk = test["InitialRiskPoints_Policy"].fillna(test["InitialRiskPoints"]).clip(lower=0.25)
                                    score = predictions[(prediction_target, k, category_weight, rich_block_weight, shrinkage)] * 4 * risk
                                else:
                                    score = predictions[(prediction_target, k, category_weight, rich_block_weight, shrinkage)]
                                scored_test = test.assign(Score=score)
                                if mode == "Conservative":
                                    scored_test = scored_test.assign(
                                        OutcomeNet=scored_test["ConservativeOutcomeNet"]
                                    )
                                scored[(mode, target, k, category_weight, rich_block_weight, shrinkage)].append(scored_test)

    for (mode, target, k, category_weight, rich_block_weight, shrinkage), parts in scored.items():
        selected = choose_policy(pd.concat(parts, ignore_index=True))
        for threshold in thresholds[target]:
            for limit in (15, 0):
                trades, net, periods = simulate(selected, threshold, limit)
                results.append(
                    {
                        "FeatureSet": args.rich_feature_set,
                        "Mode": mode,
                        "TargetModel": target,
                        "K": k,
                        "CategoryWeight": category_weight,
                        "RichBlockWeight": rich_block_weight,
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
    if args.rich_feature_set == "none":
        output.drop(columns=["FeatureSet", "RichBlockWeight"], inplace=True)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    output.to_csv(args.output, index=False)
    print(output.groupby("Mode").head(15).to_string(index=False))


if __name__ == "__main__":
    main()
