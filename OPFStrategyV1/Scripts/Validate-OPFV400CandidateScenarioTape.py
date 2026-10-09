#!/usr/bin/env python3
"""Validate V4 broad Candidate Scenario Tape Data Only snapshots."""

from __future__ import annotations

import argparse
import csv
import json
from pathlib import Path


PROFILE = "V400_CANDIDATE_SCENARIO_TAPE_DATA_ONLY"
MODE = "CandidateScenarioTapeDataOnly"
RESEARCH_SCHEMA = "OPF_RESEARCH_2.33"
ACTUAL_VERSION = "ACTUAL_EXEC_2.50"
SCENARIO_REQUIRED = (
    "CandidateID", "SignalID", "DecisionTime", "DecisionBar", "ResearchPath",
    "PlannedEntry", "PlannedStop", "PlannedTarget", "PlannedRiskPoints",
    "EntryBid", "EntryAsk", "MarketSequence", "ZoneID", "Decision", "Reason",
)
FORBIDDEN_OUTPUT_SUFFIXES = (
    "_market_execution_ticks.csv",
    "_zone_behavior_events.csv",
    "_zone_behavior_bars.csv",
    "_zone_behavior_price_levels.csv",
)
ORDER_OUTPUT_SUFFIXES = ("_execution_trades.csv", "_live_account_pnl.csv", "_trades.csv")


def read_csv(path: Path) -> list[dict[str, str]]:
    with path.open("r", newline="", encoding="utf-8-sig") as handle:
        return list(csv.DictReader(handle))


def decimal(value: str) -> float:
    return float(value or 0)


def scenario_key(row: dict[str, str]) -> tuple[str, str, str, str]:
    return row["SignalID"], row["DecisionTime"], row["DecisionBar"], row["ResearchPath"]


def validate_snapshot(log_dir: Path, snapshot: str) -> dict[str, object]:
    config = json.loads((log_dir / f"{snapshot}_ConfigSnapshot.json").read_text(encoding="utf-8-sig"))
    settings = config.get("ActualExecutionSettings", {})
    research_log = (log_dir / f"{snapshot}_research.log").read_text(encoding="utf-8-sig")
    scenarios = read_csv(log_dir / f"{snapshot}_market_execution_scenarios.csv")
    risks = read_csv(log_dir / f"{snapshot}_risk_evaluations.csv")
    events = read_csv(log_dir / f"{snapshot}_execution_events.csv")
    scenario_headers = set(scenarios[0]) if scenarios else set()
    header_missing = sum(field not in scenario_headers for field in SCENARIO_REQUIRED)
    ids = [row.get("CandidateID", "") for row in scenarios]
    duplicate_candidates = len(ids) - len(set(ids))
    scenario_keys = {scenario_key(row) for row in scenarios}
    risk_keys = {
        (row.get("SignalID", ""), row.get("Time", ""), row.get("Bar", ""), row.get("ResearchPath", ""))
        for row in risks
    }
    invalid = sum(
        not row.get("CandidateID")
        or decimal(row.get("PlannedEntry", "")) <= 0
        or decimal(row.get("PlannedStop", "")) <= 0
        or decimal(row.get("PlannedTarget", "")) <= 0
        or decimal(row.get("PlannedRiskPoints", "")) <= 0
        or decimal(row.get("EntryBid", "")) <= 0
        or decimal(row.get("EntryAsk", "")) <= 0
        or int(row.get("MarketSequence") or 0) <= 0
        or not row.get("ZoneID")
        or row.get("Decision") != "DataOnlyObserved"
        or row.get("Reason") != "ActualOrdersDisabled"
        for row in scenarios
    )
    existing = {path.name for path in log_dir.glob(f"{snapshot}_*")}
    forbidden_outputs = sum(any(name.endswith(suffix) for name in existing) for suffix in FORBIDDEN_OUTPUT_SUFFIXES)
    order_outputs = sum(any(name.endswith(suffix) for name in existing) for suffix in ORDER_OUTPUT_SUFFIXES)
    entry_sends = sum("ENTRY_SEND" in row.values() for row in events)
    passed = (
        config.get("ResearchSchemaVersion") == RESEARCH_SCHEMA
        and settings.get("Version") == ACTUAL_VERSION
        and settings.get("RunProfileId") == PROFILE
        and settings.get("RunMode") == MODE
        and settings.get("EnableActualOrders") is False
        and "CANDIDATE_SCENARIO_TAPE_DATA_ONLY enabled=true actualOrders=false" in research_log
        and bool(scenarios)
        and header_missing == 0
        and duplicate_candidates == 0
        and not (scenario_keys - risk_keys)
        and invalid == 0
        and forbidden_outputs == 0
        and order_outputs == 0
        and entry_sends == 0
    )
    return {
        "Snapshot": snapshot,
        "Passed": passed,
        "ScenarioRows": len(scenarios),
        "DuplicateCandidateIDs": duplicate_candidates,
        "ScenarioRiskCoverageMissing": len(scenario_keys - risk_keys),
        "RiskRowsOutsideScenario": len(risk_keys - scenario_keys),
        "ScenarioInvalidRows": invalid,
        "ForbiddenOutputFiles": forbidden_outputs,
        "ActualOrderFiles": order_outputs,
        "EntrySend": entry_sends,
    }


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--log-dir", type=Path, required=True)
    parser.add_argument("--summary-output", type=Path, required=True)
    args = parser.parse_args()
    snapshots = sorted(path.name.removesuffix("_ConfigSnapshot.json") for path in args.log_dir.glob("*_ConfigSnapshot.json"))
    if not snapshots:
        raise SystemExit("No ConfigSnapshot files found")
    summaries = [validate_snapshot(args.log_dir, snapshot) for snapshot in snapshots]
    args.summary_output.parent.mkdir(parents=True, exist_ok=True)
    with args.summary_output.open("w", newline="", encoding="utf-8") as handle:
        writer = csv.DictWriter(handle, fieldnames=list(summaries[0]))
        writer.writeheader()
        writer.writerows(summaries)
    if not all(row["Passed"] for row in summaries):
        raise SystemExit("V4 Candidate Scenario Tape validation failed")


if __name__ == "__main__":
    main()
