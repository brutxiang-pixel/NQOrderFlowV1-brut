#!/usr/bin/env python3
"""Connect V4 deep and broad Candidate Scenario tapes for PortfolioEngine input."""

from __future__ import annotations

import argparse
from pathlib import Path

import pandas as pd


REQUIRED_COLUMNS = (
    "SnapshotID", "CandidateID", "SignalID", "DecisionTime", "DecisionBar", "Side", "SetupType", "ResearchPath",
    "PlannedEntry", "PlannedStop", "PlannedTarget", "PlannedRiskPoints", "PlannedTargetR", "EntryBid", "EntryAsk",
    "MarketSequence", "DailyTradeCount", "DailyGrossDollars", "DailyAccountNetDollars", "WeeklyLongNetDollars",
    "ActiveTradeCount", "SecondarySlotOccupied", "GlobexLocked", "GlobexReason", "UsOpenBlackout", "UsOpenReason",
    "LatencyGateActive", "DailyTradeLimitReached", "DailyLossReached", "WeeklyLongGateActive", "ZoneID", "ZoneType",
    "ZoneDirection", "ZoneLow", "ZoneHigh", "ZoneCreatedTime", "ZoneCreatedBar", "ZoneTouchOrdinal", "ZoneLastTouchTime",
    "ZoneLastTouchBar", "ZoneLastTouchMarketSequence", "ZoneCumulativeBuy", "ZoneCumulativeSell", "ZoneCumulativeDelta",
    "ZoneCumulativeInZoneBuy", "ZoneCumulativeInZoneSell", "ZoneCumulativeInZoneDelta", "Decision", "Reason",
)
KEY_COLUMNS = ("SignalID", "DecisionTime", "DecisionBar", "ResearchPath")
DYNAMIC_AUDIT_COLUMNS = (
    "EntryBid", "EntryAsk", "MarketSequence", "ZoneLastTouchMarketSequence", "ZoneCumulativeBuy",
    "ZoneCumulativeSell", "ZoneCumulativeDelta", "ZoneCumulativeInZoneBuy", "ZoneCumulativeInZoneSell",
    "ZoneCumulativeInZoneDelta",
)
STABLE_COLUMNS = tuple(column for column in REQUIRED_COLUMNS if column not in {"SnapshotID", *DYNAMIC_AUDIT_COLUMNS})


def load_scenarios(roots: Path | list[Path]) -> pd.DataFrame:
    if isinstance(roots, Path):
        roots = [roots]
    parts = []
    for root in roots:
        for path in sorted(root.glob("*_market_execution_scenarios.csv")):
            frame = pd.read_csv(path, dtype=str, keep_default_na=False)
            missing = set(REQUIRED_COLUMNS) - set(frame.columns)
            if missing:
                raise ValueError(f"Missing scenario columns in {path.name}: {sorted(missing)}")
            frame = frame.loc[:, REQUIRED_COLUMNS].copy()
            frame["SourceFile"] = path.name
            frame["SessionID"] = path.name.removesuffix("_market_execution_scenarios.csv")
            parts.append(frame)
    if not parts:
        raise FileNotFoundError(f"No market_execution_scenarios.csv files in {root}")
    output = pd.concat(parts, ignore_index=True)
    duplicates = output["CandidateID"].duplicated(keep=False)
    if duplicates.any():
        raise ValueError(f"Duplicate CandidateID: {sorted(output.loc[duplicates, 'CandidateID'].unique())[:3]}")
    stable_duplicates = output.duplicated(list(KEY_COLUMNS), keep=False)
    if stable_duplicates.any():
        raise ValueError("Duplicate stable candidate key")
    if (~output["Decision"].eq("DataOnlyObserved") | ~output["Reason"].eq("ActualOrdersDisabled")).any():
        raise ValueError("Candidate tape contains a non-DataOnly scenario")
    return output


def connect(deep_root: Path, broad_roots: Path | list[Path]) -> tuple[dict[str, int], pd.DataFrame]:
    deep = load_scenarios(deep_root).set_index(list(KEY_COLUMNS), drop=False)
    broad = load_scenarios(broad_roots).set_index(list(KEY_COLUMNS), drop=False)
    deep_keys, broad_keys = set(deep.index), set(broad.index)
    missing = deep_keys - broad_keys
    if missing:
        raise ValueError(f"Candidate identity mismatch: missing={len(missing)}")
    stable_mismatches = []
    dynamic_mismatches = 0
    for key in sorted(deep_keys):
        left, right = deep.loc[key], broad.loc[key]
        fields = [column for column in STABLE_COLUMNS if left[column] != right[column]]
        if fields:
            stable_mismatches.append((key, fields))
        if any(left[column] != right[column] for column in DYNAMIC_AUDIT_COLUMNS):
            dynamic_mismatches += 1
    if stable_mismatches:
        key, fields = stable_mismatches[0]
        raise ValueError(f"Stable scenario mismatch for {key}: {fields}")
    portfolio = broad.reset_index(drop=True).copy()
    portfolio["PortfolioKey"] = portfolio.apply(lambda row: "|".join(row[column] for column in KEY_COLUMNS), axis=1)
    portfolio["OutcomeSource"] = "CounterfactualPending"
    portfolio["PricePathSource"] = "RichM5Required"
    portfolio["DynamicAuditState"] = "ReplayLocalOnly"
    return {
        "DeepCandidates": len(deep),
        "BroadCandidates": len(broad),
        "StableMatches": len(deep),
        "DynamicMismatches": dynamic_mismatches,
    }, portfolio


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--deep-root", type=Path, required=True)
    parser.add_argument("--broad-root", type=Path, action="append", required=True)
    parser.add_argument("--output-csv", type=Path, required=True)
    parser.add_argument("--summary-csv", type=Path, required=True)
    args = parser.parse_args()
    summary, portfolio = connect(args.deep_root, args.broad_root)
    args.output_csv.parent.mkdir(parents=True, exist_ok=True)
    portfolio.to_csv(args.output_csv, index=False, encoding="utf-8")
    pd.DataFrame([summary]).to_csv(args.summary_csv, index=False, encoding="utf-8")


if __name__ == "__main__":
    main()
