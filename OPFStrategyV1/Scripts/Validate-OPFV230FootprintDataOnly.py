#!/usr/bin/env python3
"""Validate v2.30 Footprint Data Only snapshots without interpreting alpha."""

from __future__ import annotations

import argparse
import csv
import json
from collections import Counter
from pathlib import Path


def truth(value: str) -> bool:
    return value.strip().lower() == "true"


def decimal(value: str) -> float:
    return float(value or 0)


def keys(rows: list[dict[str, str]], fields: tuple[str, ...]) -> set[tuple[str, ...]]:
    return {tuple(row.get(field, "") for field in fields) for row in rows}


def read_csv(path: Path) -> list[dict[str, str]]:
    with path.open("r", newline="", encoding="utf-8-sig") as handle:
        return list(csv.DictReader(handle))


def validate_snapshot(log_dir: Path, feature_path: Path) -> dict[str, object]:
    snapshot = feature_path.name.removesuffix("_footprint_candidate_features.csv")
    config = json.loads((log_dir / f"{snapshot}_ConfigSnapshot.json").read_text(encoding="utf-8-sig"))
    research_log = (log_dir / f"{snapshot}_research.log").read_text(encoding="utf-8-sig")
    rows = read_csv(feature_path)
    risk_rows = read_csv(log_dir / f"{snapshot}_risk_evaluations.csv")
    order_files = [
        path for path in log_dir.glob(f"{snapshot}_*")
        if path.name.endswith(("_execution_trades.csv", "_live_account_pnl.csv", "_trades.csv"))
    ]
    expected = {
        "SignalID", "DecisionTime", "DecisionBar", "Lane", "ResearchPath",
        "TickSequenceBoundary", "ReferenceTickTime", "Tick60WindowComplete",
        "FootprintHistory15mComplete", "PocM5_1", "PocM5_2", "PocM5_3",
    }
    header_missing = expected - set(rows[0]) if rows else expected
    malformed = [
        row for row in rows
        if row["Lane"] != "ResearchCandidate"
        or int(row["TickSequenceBoundary"] or 0) <= 0
        or not row["ReferenceTickTime"]
        or min(decimal(row[name]) for name in ("BuyVolume30", "SellVolume30", "UnknownVolume30", "BuyVolume60", "SellVolume60", "UnknownVolume60")) < 0
    ]
    window_violation = [
        row for row in rows
        if sum(decimal(row[name]) for name in ("BuyVolume30", "SellVolume30", "UnknownVolume30"))
        > sum(decimal(row[name]) for name in ("BuyVolume60", "SellVolume60", "UnknownVolume60"))
    ]
    warm_rows = [row for row in rows if truth(row["Tick60WindowComplete"]) and truth(row["FootprintHistory15mComplete"])]
    warm_poc_invalid = [
        row for row in warm_rows
        if min(decimal(row[name]) for name in ("PocM5_1", "PocM5_2", "PocM5_3")) <= 0
    ]
    feature_keys = keys(rows, ("SignalID", "DecisionTime", "DecisionBar", "ResearchPath"))
    risk_keys = keys(risk_rows, ("SignalID", "Time", "Bar", "ResearchPath"))
    missing_risk = feature_keys - risk_keys
    passed = (
        config.get("ResearchSchemaVersion") == "OPF_RESEARCH_2.30"
        and config.get("ActualExecutionSettings", {}).get("Version") == "ACTUAL_EXEC_2.47"
        and "FOOTPRINT_DATA_COLLECTION_ONLY enabled=true actualOrders=false" in research_log
        and bool(rows)
        and not header_missing
        and not malformed
        and not window_violation
        and not warm_poc_invalid
        and not missing_risk
        and not order_files
    )
    return {
        "Snapshot": snapshot,
        "Passed": passed,
        "Rows": len(rows),
        "WarmRows": len(warm_rows),
        "ResearchSchema": config.get("ResearchSchemaVersion", ""),
        "ActualConfig": config.get("ActualExecutionSettings", {}).get("Version", ""),
        "OrderFiles": len(order_files),
        "HeaderMissing": len(header_missing),
        "MalformedRows": len(malformed),
        "WindowViolations": len(window_violation),
        "WarmPocInvalid": len(warm_poc_invalid),
        "RiskJoinMissing": len(missing_risk),
        "TouchStates": "|".join(f"{name}:{count}" for name, count in sorted(Counter(row["PriceDeltaDivergence"] for row in rows).items())),
    }


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--log-dir", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    summaries = [validate_snapshot(args.log_dir, path) for path in sorted(args.log_dir.glob("*_footprint_candidate_features.csv"))]
    if not summaries:
        raise SystemExit("No footprint_candidate_features.csv found")
    args.output.parent.mkdir(parents=True, exist_ok=True)
    with args.output.open("w", newline="", encoding="utf-8") as handle:
        writer = csv.DictWriter(handle, fieldnames=list(summaries[0]))
        writer.writeheader()
        writer.writerows(summaries)
    if not all(item["Passed"] for item in summaries):
        raise SystemExit("v2.30 validation failed")


if __name__ == "__main__":
    main()
