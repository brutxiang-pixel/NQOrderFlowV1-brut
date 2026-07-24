import argparse
import json
from pathlib import Path

import pandas as pd


COMPONENT_NAMES = (
    "SwingProgression",
    "VWAPSide",
    "OpeningRangeSide",
    "VwapCrossCount",
    "DirectionalDisplacement",
    "AtrExpansion",
)
RICH_VALUE_COLUMNS = (
    "Regime",
    "RegimeBars",
    "Open",
    "High",
    "Low",
    "Close",
    "Volume",
    "Vwap",
    "AverageVolume20",
    "RelativeVolume20",
    "CloseMinusVwap",
    "Atr14",
    "VwapDistanceAtr",
    "BullScore",
    "BearScore",
)
RICH_METADATA_COLUMNS = (
    "SnapshotID",
    "StrategyVersion",
    "ResearchSchemaVersion",
    "InstrumentProfileName",
    "ExecutionProfileName",
    "ProfileCatalogVersion",
)
JOIN_COLUMNS = ["RichTime", "RichBar"]


def read_csvs(root: Path, pattern: str) -> pd.DataFrame:
    files = sorted(root.rglob(pattern))
    if not files:
        raise ValueError(f"No files matched {pattern!r} under {root}")
    return pd.concat((pd.read_csv(path, low_memory=False) for path in files), ignore_index=True)


def _expand_components(value: str, prefix: str) -> dict:
    components = json.loads(value)
    by_name = {component["Name"]: component for component in components}
    if tuple(by_name) != COMPONENT_NAMES:
        raise ValueError(f"Unexpected {prefix} component names: {tuple(by_name)}")
    expanded = {}
    for name in COMPONENT_NAMES:
        component = by_name[name]
        expanded[f"Rich{prefix}{name}RawValue"] = str(component["RawValue"])
        expanded[f"Rich{prefix}{name}Passed"] = int(bool(component["Passed"]))
        expanded[f"Rich{prefix}{name}Contribution"] = float(component["Contribution"])
    return expanded


def load_rich_features(root: Path) -> tuple[pd.DataFrame, pd.DataFrame]:
    selected_path = root / "selected_snapshots.csv"
    selected = pd.read_csv(selected_path, dtype={"Date": str, "SnapshotId": str})
    if len(selected) != 184 or selected["Date"].nunique() != 184 or selected["SnapshotId"].nunique() != 184:
        raise ValueError("selected_snapshots.csv must contain 184 unique dates and SnapshotIds")

    parts = []
    for row in selected.itertuples(index=False):
        path = root / f"{row.SnapshotId}_rich_bar_features.csv"
        if not path.is_file():
            raise ValueError(f"Missing selected Rich file: {path.name}")
        part = pd.read_csv(path, low_memory=False)
        if len(part) != int(row.Rows):
            raise ValueError(f"Row count mismatch for {path.name}: {len(part)} != {row.Rows}")
        if not part["SnapshotID"].astype(str).eq(str(row.SnapshotId)).all():
            raise ValueError(f"SnapshotID mismatch in {path.name}")
        part["RichTradingDate"] = str(row.Date)
        parts.append(part)

    rich = pd.concat(parts, ignore_index=True)
    rich.rename(columns={column: f"Rich{column}" for column in RICH_VALUE_COLUMNS}, inplace=True)
    rich.rename(columns={column: f"Rich{column}" for column in RICH_METADATA_COLUMNS}, inplace=True)
    rich.rename(columns={"Time": "RichTime", "Bar": "RichBar"}, inplace=True)
    rich["RichTime"] = pd.to_datetime(rich["RichTime"])
    rich["RichBar"] = rich["RichBar"].astype(int)
    if rich.duplicated(JOIN_COLUMNS).any():
        duplicates = int(rich.duplicated(JOIN_COLUMNS, keep=False).sum())
        raise ValueError(f"Rich Time+Bar keys are not unique: {duplicates} duplicate rows")

    bull = pd.DataFrame(
        (_expand_components(value, "Bull") for value in rich.pop("BullComponents")),
        index=rich.index,
    )
    bear = pd.DataFrame(
        (_expand_components(value, "Bear") for value in rich.pop("BearComponents")),
        index=rich.index,
    )
    rich = pd.concat([rich, bull, bear], axis=1)
    return rich, selected


def join_rich_features(rows: pd.DataFrame, rich: pd.DataFrame) -> pd.DataFrame:
    joined = rows.copy()
    joined["RichTime"] = pd.to_datetime(joined["EntryTime"])
    joined["RichBar"] = joined["EntryBar"].astype(int)
    joined = joined.merge(rich, on=JOIN_COLUMNS, how="left", validate="many_to_one")
    missing = int(joined["RichTradingDate"].isna().sum())
    if missing:
        raise ValueError(f"Rich feature join missed {missing}/{len(joined)} candidate-policy rows")
    return joined


def _match_count(rows: pd.DataFrame, time_column: str, bar_column: str, rich: pd.DataFrame) -> int:
    keys = rows[[time_column, bar_column]].copy()
    keys.columns = JOIN_COLUMNS
    keys["RichTime"] = pd.to_datetime(keys["RichTime"])
    keys["RichBar"] = keys["RichBar"].astype(int)
    matched = keys.merge(rich[JOIN_COLUMNS], on=JOIN_COLUMNS, how="left", indicator=True)
    return int(matched["_merge"].eq("both").sum())


def validate_archives(
    h1: Path,
    q4: Path,
    rich: pd.DataFrame,
    selected: pd.DataFrame,
    require_zero_initialization_matches: bool = True,
) -> dict:
    decisions = pd.concat(
        [read_csvs(q4, "*_execution_decisions.csv"), read_csvs(h1, "*_execution_decisions.csv")],
        ignore_index=True,
    )
    policies = pd.concat(
        [read_csvs(q4, "*_exit_policy_evaluations.csv"), read_csvs(h1, "*_exit_policy_evaluations.csv")],
        ignore_index=True,
    )
    shadows = read_csvs(h1, "*_shadow_trades.csv")
    decision_matched = _match_count(decisions, "Time", "Bar", rich)
    policy_matched = _match_count(policies, "EntryTime", "EntryBar", rich)
    shadow_matched = _match_count(shadows, "EntryTime", "EntryBar", rich)

    decision_keys = decisions[["Time", "Bar"]].copy()
    decision_keys.columns = JOIN_COLUMNS
    decision_keys["RichTime"] = pd.to_datetime(decision_keys["RichTime"])
    decision_keys["RichBar"] = decision_keys["RichBar"].astype(int)
    matched_decisions = decision_keys.merge(rich, on=JOIN_COLUMNS, how="left", validate="many_to_one")
    initialization_zero = (
        matched_decisions["RichAverageVolume20"].fillna(0).eq(0)
        | matched_decisions["RichRelativeVolume20"].fillna(0).eq(0)
        | matched_decisions["RichAtr14"].fillna(0).eq(0)
    )

    report = {
        "TradingDays": int(selected["Date"].nunique()),
        "Q4Days": int(selected["Date"].str.startswith("2025-").sum()),
        "H1Days": int(selected["Date"].str.startswith("2026-").sum()),
        "RichRows": int(len(rich)),
        "RichDuplicateKeys": int(rich.duplicated(JOIN_COLUMNS).sum()),
        "ExecutionDecisionMatched": decision_matched,
        "ExecutionDecisionTotal": int(len(decisions)),
        "ExitPolicyMatched": policy_matched,
        "ExitPolicyTotal": int(len(policies)),
        "AvailableShadowMatched": shadow_matched,
        "AvailableShadowTotal": int(len(shadows)),
        "MatchedCandidateInitializationZeros": int(initialization_zero.sum()),
        "ComponentJsonErrors": 0,
    }
    expected = {
        "TradingDays": 184,
        "Q4Days": 62,
        "H1Days": 122,
        "RichDuplicateKeys": 0,
        "ComponentJsonErrors": 0,
    }
    if require_zero_initialization_matches:
        expected["MatchedCandidateInitializationZeros"] = 0
    failures = {key: (report[key], value) for key, value in expected.items() if report[key] != value}
    for matched, total in (
        ("ExecutionDecisionMatched", "ExecutionDecisionTotal"),
        ("ExitPolicyMatched", "ExitPolicyTotal"),
        ("AvailableShadowMatched", "AvailableShadowTotal"),
    ):
        if report[matched] != report[total]:
            failures[matched] = (report[matched], report[total])
    if failures:
        raise ValueError(f"Rich archive validation failed: {failures}")
    return report


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--h1", type=Path, required=True)
    parser.add_argument("--q4", type=Path, required=True)
    parser.add_argument("--rich", type=Path, required=True)
    parser.add_argument("--validation-output", type=Path, required=True)
    args = parser.parse_args()

    rich, selected = load_rich_features(args.rich)
    report = validate_archives(args.h1, args.q4, rich, selected)
    args.validation_output.parent.mkdir(parents=True, exist_ok=True)
    pd.DataFrame([report]).to_csv(args.validation_output, index=False)
    print(pd.DataFrame([report]).to_string(index=False))


if __name__ == "__main__":
    main()
