#!/usr/bin/env python3
"""Validate and normalize V4 Market Execution Tape Data Only snapshots."""

from __future__ import annotations

import argparse
import csv
import json
from collections import defaultdict
from pathlib import Path


PROFILE = "V400_MARKET_EXECUTION_TAPE_4DAY_DATA_ONLY"
MODE = "MarketExecutionTapeDataOnly"
RESEARCH_SCHEMA = "OPF_RESEARCH_2.33"
ACTUAL_VERSION = "ACTUAL_EXEC_2.50"
VOLUME_ERROR_LIMIT_PCT = 0.20

SCENARIO_REQUIRED = [
    "CandidateID", "SignalID", "DecisionTime", "DecisionBar", "ResearchPath",
    "PlannedEntry", "PlannedStop", "PlannedTarget", "PlannedRiskPoints",
    "EntryBid", "EntryAsk", "MarketSequence", "ZoneID", "Decision", "Reason",
]


def read_csv(path: Path) -> list[dict[str, str]]:
    with path.open("r", newline="", encoding="utf-8-sig") as handle:
        return list(csv.DictReader(handle))


def decimal(value: str) -> float:
    return float(value or 0)


def scenario_key(row: dict[str, str]) -> tuple[str, str, str, str]:
    return row["SignalID"], row["DecisionTime"], row["DecisionBar"], row["ResearchPath"]


def tick_m5_rows(ticks: list[dict[str, str]]) -> tuple[list[dict[str, object]], int]:
    by_m5: dict[str, list[dict[str, str]]] = defaultdict(list)
    expected = 1
    errors = 0
    for tick in ticks:
        if int(tick["Sequence"]) != expected:
            errors += 1
        expected = int(tick["Sequence"]) + 1
        by_m5[tick["M5Time"]].append(tick)

    output: list[dict[str, object]] = []
    for time, rows in sorted(by_m5.items()):
        output.append({
            "M5Time": time,
            "FirstSequence": int(rows[0]["Sequence"]),
            "LastSequence": int(rows[-1]["Sequence"]),
            "Open": decimal(rows[0]["Price"]),
            "High": max(decimal(row["Price"]) for row in rows),
            "Low": min(decimal(row["Price"]) for row in rows),
            "Close": decimal(rows[-1]["Price"]),
            "Volume": sum(decimal(row["Volume"]) for row in rows),
            "BuyVolume": sum(decimal(row["Volume"]) for row in rows if row["Direction"] == "Buy"),
            "SellVolume": sum(decimal(row["Volume"]) for row in rows if row["Direction"] == "Sell"),
            "UnknownVolume": sum(decimal(row["Volume"]) for row in rows if row["Direction"] not in {"Buy", "Sell"}),
        })
    return output, errors


def validate_snapshot(log_dir: Path, snapshot: str) -> tuple[dict[str, object], list[dict[str, object]]]:
    config = json.loads((log_dir / f"{snapshot}_ConfigSnapshot.json").read_text(encoding="utf-8-sig"))
    settings = config.get("ActualExecutionSettings", {})
    research_log = (log_dir / f"{snapshot}_research.log").read_text(encoding="utf-8-sig")
    ticks = read_csv(log_dir / f"{snapshot}_market_execution_ticks.csv")
    scenarios = read_csv(log_dir / f"{snapshot}_market_execution_scenarios.csv")
    risks = read_csv(log_dir / f"{snapshot}_risk_evaluations.csv")
    rich = read_csv(log_dir / f"{snapshot}_rich_bar_features.csv")
    events = read_csv(log_dir / f"{snapshot}_execution_events.csv")
    m5_rows, sequence_errors = tick_m5_rows(ticks)
    tick_volume = {row["M5Time"]: float(row["Volume"]) for row in m5_rows}
    rich_volume = {row["Time"]: decimal(row["Volume"]) for row in rich}
    common = set(tick_volume) & set(rich_volume)
    missing_tick_bars = set(rich_volume) - set(tick_volume)
    volume_error = sum(tick_volume[time] - rich_volume[time] for time in common)
    rich_common_volume = sum(rich_volume[time] for time in common)
    volume_error_pct = 0 if rich_common_volume == 0 else abs(100 * volume_error / rich_common_volume)
    first_tick_time = min(tick_volume) if tick_volume else ""
    missing_non_warmup = sum(1 for time in missing_tick_bars if time >= first_tick_time)
    scenario_header = set(scenarios[0]) if scenarios else set()
    scenario_header_missing = len(set(SCENARIO_REQUIRED) - scenario_header)
    scenario_ids = [row["CandidateID"] for row in scenarios]
    duplicate_candidates = len(scenario_ids) - len(set(scenario_ids))
    risk_keys = {(row["SignalID"], row["Time"], row["Bar"], row["ResearchPath"]) for row in risks}
    scenario_keys = {scenario_key(row) for row in scenarios}
    coverage_missing = len(risk_keys - scenario_keys)
    coverage_extra = len(scenario_keys - risk_keys)
    scenario_invalid = sum(bool(
        decimal(row["PlannedEntry"]) <= 0 or decimal(row["PlannedStop"]) <= 0 or decimal(row["PlannedTarget"]) <= 0
        or decimal(row["PlannedRiskPoints"]) <= 0 or decimal(row["EntryBid"]) <= 0 or decimal(row["EntryAsk"]) <= 0
        or int(row["MarketSequence"] or 0) <= 0 or not row["ZoneID"]
        or row["Decision"] != "DataOnlyObserved" or row["Reason"] != "ActualOrdersDisabled"
        or (bool(row.get("ZoneLastTouchMarketSequence", "")) and int(row["ZoneLastTouchMarketSequence"]) > int(row["MarketSequence"]))
    ) for row in scenarios)
    actual_order_files = [
        path for path in log_dir.glob(f"{snapshot}_*")
        if path.name.endswith(("_execution_trades.csv", "_live_account_pnl.csv", "_trades.csv"))
    ]
    entry_sends = sum("ENTRY_SEND" in row.values() for row in events)
    passed = (
        config.get("ResearchSchemaVersion") == RESEARCH_SCHEMA
        and settings.get("Version") == ACTUAL_VERSION
        and settings.get("RunProfileId") == PROFILE
        and settings.get("RunMode") == MODE
        and settings.get("EnableActualOrders") is False
        and "MARKET_EXECUTION_TAPE_DATA_ONLY enabled=true actualOrders=false" in research_log
        and bool(ticks) and bool(scenarios) and sequence_errors == 0
        and scenario_header_missing == 0 and duplicate_candidates == 0
        and coverage_missing == 0 and coverage_extra == 0 and scenario_invalid == 0
        and not actual_order_files and entry_sends == 0
        and missing_non_warmup == 0 and volume_error_pct <= VOLUME_ERROR_LIMIT_PCT
    )
    summary = {
        "Snapshot": snapshot, "Passed": passed, "TickRows": len(ticks), "M5Rows": len(m5_rows),
        "RichM5Rows": len(rich), "TickSequenceErrors": sequence_errors, "MissingTickWarmupBars": len(missing_tick_bars),
        "MissingTickNonWarmupBars": missing_non_warmup, "TickRichVolumeErrorPct": round(volume_error_pct, 6),
        "ScenarioRows": len(scenarios), "DuplicateCandidateIDs": duplicate_candidates,
        "ScenarioRiskCoverageMissing": coverage_missing, "ScenarioRiskCoverageExtra": coverage_extra,
        "ScenarioInvalidRows": scenario_invalid, "ActualOrderFiles": len(actual_order_files), "EntrySend": entry_sends,
    }
    return summary, m5_rows


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--log-dir", type=Path, required=True)
    parser.add_argument("--summary-output", type=Path, required=True)
    parser.add_argument("--m5-output", type=Path, required=True)
    args = parser.parse_args()
    snapshots = sorted(path.name.removesuffix("_ConfigSnapshot.json") for path in args.log_dir.glob("*_ConfigSnapshot.json"))
    if not snapshots:
        raise SystemExit("No ConfigSnapshot files found")
    summaries: list[dict[str, object]] = []
    m5_output: list[dict[str, object]] = []
    for snapshot in snapshots:
        summary, m5_rows = validate_snapshot(args.log_dir, snapshot)
        summaries.append(summary)
        for row in m5_rows:
            m5_output.append({"SnapshotID": snapshot, **row})
    args.summary_output.parent.mkdir(parents=True, exist_ok=True)
    with args.summary_output.open("w", newline="", encoding="utf-8") as handle:
        writer = csv.DictWriter(handle, fieldnames=list(summaries[0]))
        writer.writeheader()
        writer.writerows(summaries)
    with args.m5_output.open("w", newline="", encoding="utf-8") as handle:
        writer = csv.DictWriter(handle, fieldnames=["SnapshotID", "M5Time", "FirstSequence", "LastSequence", "Open", "High", "Low", "Close", "Volume", "BuyVolume", "SellVolume", "UnknownVolume"])
        writer.writeheader()
        writer.writerows(m5_output)
    if not all(row["Passed"] for row in summaries):
        raise SystemExit("V4 Market Execution Tape validation failed")


if __name__ == "__main__":
    main()
