#!/usr/bin/env python3
"""Build auditable v2.30 Actual outcome labels without interpreting alpha."""

from __future__ import annotations

import argparse
import csv
import json
from collections import Counter, defaultdict
from pathlib import Path


KEY = ("SignalID", "DecisionTime", "DecisionBar", "ResearchPath")


def read_rows(paths: list[Path], pattern: str) -> list[dict[str, str]]:
    rows: list[dict[str, str]] = []
    for directory in paths:
        for path in sorted(directory.glob(pattern)):
            with path.open(encoding="utf-8-sig", newline="") as handle:
                rows.extend(csv.DictReader(handle))
    return rows


def truth(value: str) -> bool:
    return value.lower() == "true"


def feature_key(row: dict[str, str]) -> tuple[str, ...]:
    return tuple(row[name] for name in KEY)


def decision_key(row: dict[str, str]) -> tuple[str, ...]:
    return (row["SignalID"], row["Time"], row["Bar"], row["ResearchPath"])


def unique_map(rows: list[dict[str, str]], key_builder, name: str) -> dict[tuple[str, ...], dict[str, str]]:
    result: dict[tuple[str, ...], dict[str, str]] = {}
    duplicates = 0
    for row in rows:
        key = key_builder(row)
        if key in result:
            duplicates += 1
        result[key] = row
    if duplicates:
        raise SystemExit(f"Duplicate {name} keys: {duplicates}")
    return result


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--footprint-dir", type=Path, action="append", required=True)
    parser.add_argument("--actual-dir", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    args = parser.parse_args()

    features = read_rows(args.footprint_dir, "*_footprint_candidate_features.csv")
    warm_features = [
        row for row in features
        if truth(row["Tick60WindowComplete"]) and truth(row["FootprintHistory15mComplete"])
    ]
    decisions = read_rows([args.actual_dir], "*_execution_decisions.csv")
    trades = read_rows([args.actual_dir], "*_execution_trades.csv")
    pnls = read_rows([args.actual_dir], "*_live_account_pnl.csv")
    events = read_rows([args.actual_dir], "*_execution_events.csv")

    decision_by_key = unique_map(decisions, decision_key, "decision")
    trade_by_id = unique_map(trades, lambda row: (row["TradeID"],), "trade")
    pnl_by_id = unique_map(pnls, lambda row: (row["TradeID"],), "pnl")
    events_by_id: dict[str, list[dict[str, str]]] = defaultdict(list)
    for event in events:
        events_by_id[event["TradeID"]].append(event)

    labels: list[dict[str, str]] = []
    unmatched: list[dict[str, str]] = []
    for feature in warm_features:
        key = feature_key(feature)
        decision = decision_by_key.get(key)
        if decision is None:
            unmatched.append({name: feature[name] for name in KEY})
            continue

        label = {
            **feature,
            "ActualDecision": decision["Decision"],
            "TradeID": decision["TradeID"],
            "OutcomeClass": "",
            "ExitRole": "",
            "NetPnLDollars": "",
            "IsSecondary": str(decision["TradeID"].endswith("-S2")).lower(),
        }
        if decision["Decision"] != "Execute":
            label["OutcomeClass"] = "NoEntry"
        elif (decision["TradeID"],) in trade_by_id:
            trade = trade_by_id[(decision["TradeID"],)]
            pnl = pnl_by_id.get((decision["TradeID"],))
            if pnl is None or pnl["Classification"] != "Normal":
                raise SystemExit(f"Normal trade missing normal account PnL: {decision['TradeID']}")
            label["OutcomeClass"] = "NormalOutcome"
            label["ExitRole"] = trade["ExitRole"]
            label["NetPnLDollars"] = pnl["NetPnLDollars"]
        elif (decision["TradeID"],) in pnl_by_id:
            pnl = pnl_by_id[(decision["TradeID"],)]
            if pnl["Classification"] not in {"Abnormal", "Quarantine"}:
                raise SystemExit(f"Unhandled non-trade PnL classification: {decision['TradeID']}")
            label["OutcomeClass"] = "ExcludedAbnormal"
            label["NetPnLDollars"] = pnl["NetPnLDollars"]
        else:
            event_names = {event["Event"] for event in events_by_id[decision["TradeID"]]}
            if "ENTRY_SUBMISSION_ABORTED_V178" not in event_names:
                raise SystemExit(f"Executed decision lacks terminal outcome: {decision['TradeID']}")
            label["OutcomeClass"] = "EntryAborted"
        labels.append(label)

    outcomes = Counter(row["OutcomeClass"] for row in labels)
    run_profiles = set()
    for config_path in args.actual_dir.glob("*_ConfigSnapshot.json"):
        config = json.loads(config_path.read_text(encoding="utf-8-sig"))
        settings = config["ActualExecutionSettings"]
        run_profiles.add((config["ResearchSchemaVersion"], settings["Version"], settings.get("RunProfileId"), settings.get("RunMode")))
    if run_profiles != {("OPF_RESEARCH_2.30", "ACTUAL_EXEC_2.47", "V300_OUTCOME_LABEL_24DAY", "ActualExecution")}:
        raise SystemExit(f"Unexpected Actual run profile: {run_profiles}")

    args.output_dir.mkdir(parents=True, exist_ok=True)
    label_path = args.output_dir / "outcome_labels.csv"
    with label_path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=list(labels[0]))
        writer.writeheader()
        writer.writerows(labels)
    unmatched_path = args.output_dir / "unmatched_warm_features.csv"
    with unmatched_path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=list(KEY))
        writer.writeheader()
        writer.writerows(unmatched)
    summary = {
        "WarmFootprintRows": len(warm_features),
        "StrictMatchedRows": len(labels),
        "UnmatchedWarmRows": len(unmatched),
        "StrictMatchRate": round(len(labels) / len(warm_features), 6),
        "OutcomeClasses": outcomes,
        "SecondaryNormalOutcomes": sum(row["OutcomeClass"] == "NormalOutcome" and row["IsSecondary"] == "true" for row in labels),
        "RunProfile": "V300_OUTCOME_LABEL_24DAY",
        "Passed": len(labels) / len(warm_features) >= 0.999 and not outcomes.get("", 0),
    }
    (args.output_dir / "summary.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")


if __name__ == "__main__":
    main()
