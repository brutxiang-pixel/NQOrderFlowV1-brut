#!/usr/bin/env python3
"""Audit whether the existing evidence can justify a G2 shadow-opportunity collection.

This is Gate 0 only.  It never estimates PnL and never creates a trading rule.
"""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
from collections import Counter, defaultdict
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path


H1_MONTHS = ("202601", "202602", "202603", "202604", "202605", "202606")
JULY = "202607"
MIN_H1_TOTAL = 120
MIN_MONTHLY = 12
MIN_JULY = 25
MIN_LABEL_RATE = 0.80


def parse_time(value: str) -> datetime:
    return datetime.fromisoformat(value)


def read_archive_rows(root: Path, suffix: str) -> list[dict[str, str]]:
    rows: list[dict[str, str]] = []
    for path in sorted(root.glob(f"*_{suffix}.csv")):
        with path.open(encoding="utf-8-sig", newline="") as handle:
            rows.extend(csv.DictReader(handle))
    return rows


def truth(value: str) -> bool:
    return value.strip().lower() == "true"


@dataclass(frozen=True)
class ProtectedWindow:
    snapshot_id: str
    trade_id: str
    side: str
    ready_time: datetime
    exit_time: datetime


def label_key(row: dict[str, str]) -> tuple[str, str, str, str]:
    # Data Only and Actual snapshots intentionally have distinct SnapshotID values.
    # The v2.30 outcome-label gate froze this four-part identity across the pair.
    return (row["SignalID"], row["DecisionTime"], row["DecisionBar"], row["ResearchPath"])


def decision_key(row: dict[str, str]) -> tuple[str, str, str, str]:
    return (row["SignalID"], row["Time"], row["Bar"], row["ResearchPath"])


def reason_prefix(value: str) -> str:
    return value.split(":", 1)[0]


def is_secondary_shell(row: dict[str, str]) -> bool:
    """Reached the secondary decision branch; earlier signal/path/risk gates passed."""
    return row["Decision"] == "Execute" or reason_prefix(row["Reason"]) in {
        "SecondaryStaticG2Blocked",
        "SecondaryRiskCap",
        "SecondaryDirectionMismatch",
        "PrimaryNotProtectedDeferredV216",
    }


def choose_dates(day_rows: dict[str, list[dict[str, str]]]) -> list[dict[str, str]]:
    """Deterministic coverage-based plan; called only after every Gate 0 threshold passes."""
    by_month: dict[str, list[tuple[str, int, str]]] = defaultdict(list)
    for day, rows in day_rows.items():
        digest = hashlib.sha256(f"v300-g2-shadow-gate0|{day}".encode()).hexdigest()
        by_month[day.replace("-", "")[:6]].append((day, len(rows), digest))

    selected: list[dict[str, str]] = []
    for month in H1_MONTHS:
        ranked = sorted(by_month[month], key=lambda item: (-item[1], item[2]))[:2]
        selected.extend({"Date": day, "Period": month, "Reason": "H1 coverage rank", "OpportunityCount": str(count)} for day, count, _ in ranked)
    for day, count, _ in sorted(by_month[JULY], key=lambda item: (-item[1], item[2])):
        selected.append({"Date": day, "Period": JULY, "Reason": "July full available pool", "OpportunityCount": str(count)})
    return sorted(selected, key=lambda item: item["Date"])


def write_csv(path: Path, rows: list[dict[str, object]]) -> None:
    if not rows:
        return
    with path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--archive-opf-dir", type=Path, required=True)
    parser.add_argument("--labels", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    args = parser.parse_args()

    events = read_archive_rows(args.archive_opf_dir, "execution_events")
    trades = read_archive_rows(args.archive_opf_dir, "execution_trades")
    decisions = read_archive_rows(args.archive_opf_dir, "execution_decisions")
    with args.labels.open(encoding="utf-8-sig", newline="") as handle:
        labels = {label_key(row): row for row in csv.DictReader(handle)}

    stop_sent: dict[tuple[str, str], datetime] = {}
    for event in events:
        if event["Event"] == "SL_SENT" and event["TradeID"]:
            stop_sent.setdefault((event["SnapshotID"], event["TradeID"]), parse_time(event["Time"]))

    windows: list[ProtectedWindow] = []
    for trade in trades:
        key = (trade["SnapshotID"], trade["TradeID"])
        if truth(trade["IsAbnormalExecution"]) or key not in stop_sent:
            continue
        windows.append(ProtectedWindow(trade["SnapshotID"], trade["TradeID"], trade["Side"], stop_sent[key], parse_time(trade["ExitTime"])))
    windows_by_snapshot: dict[str, list[ProtectedWindow]] = defaultdict(list)
    for window in windows:
        windows_by_snapshot[window.snapshot_id].append(window)

    opportunities: list[dict[str, object]] = []
    reason_counts: Counter[str] = Counter()
    for decision in decisions:
        if not is_secondary_shell(decision):
            continue
        moment = parse_time(decision["Time"])
        matches = [
            window for window in windows_by_snapshot[decision["SnapshotID"]]
            if window.ready_time <= moment < window.exit_time
            and window.side == decision["Side"]
            and window.trade_id != decision["TradeID"]
        ]
        if len(matches) != 1:
            continue
        label = labels.get(decision_key(decision))
        label_ready = label is not None and truth(label["Tick60WindowComplete"]) and truth(label["FootprintHistory15mComplete"])
        prefix = reason_prefix(decision["Reason"])
        reason_counts[prefix] += 1
        opportunities.append({
            "Date": decision["Time"][:10],
            "Month": decision["Time"][:7].replace("-", ""),
            "SnapshotID": decision["SnapshotID"],
            "SignalID": decision["SignalID"],
            "DecisionTime": decision["Time"],
            "DecisionBar": decision["Bar"],
            "Side": decision["Side"],
            "ResearchPath": decision["ResearchPath"],
            "Decision": decision["Decision"],
            "ReasonPrefix": prefix,
            "CandidateTradeID": decision["TradeID"],
            "PrimaryTradeID": matches[0].trade_id,
            "PrimaryProtectionReadyTime": matches[0].ready_time.isoformat(),
            "PrimaryExitTime": matches[0].exit_time.isoformat(),
            "ExistingOutcomeLabelReady": label_ready,
            "HardBlock": "None" if decision["Decision"] == "Execute" else prefix,
        })

    by_month: Counter[str] = Counter(str(row["Month"]) for row in opportunities)
    day_rows: dict[str, list[dict[str, str]]] = defaultdict(list)
    for row in opportunities:
        day_rows[str(row["Date"])].append({key: str(value) for key, value in row.items()})
    label_rate = sum(bool(row["ExistingOutcomeLabelReady"]) for row in opportunities) / len(opportunities) if opportunities else 0.0
    failed = []
    if sum(by_month[month] for month in H1_MONTHS) < MIN_H1_TOTAL:
        failed.append("H1Total")
    if any(by_month[month] < MIN_MONTHLY for month in H1_MONTHS):
        failed.append("MonthlyCoverage")
    if by_month[JULY] < MIN_JULY:
        failed.append("JulyCoverage")
    if label_rate < MIN_LABEL_RATE:
        failed.append("EstimatedLabelRate")
    passed = not failed

    args.output_dir.mkdir(parents=True, exist_ok=True)
    write_csv(args.output_dir / "opportunities.csv", opportunities)
    write_csv(args.output_dir / "day_coverage.csv", [
        {"Date": day, "Month": day.replace("-", "")[:6], "OpportunityCount": len(rows), "LabelReadyCount": sum(row["ExistingOutcomeLabelReady"] == "True" for row in rows)}
        for day, rows in sorted(day_rows.items())
    ])
    write_csv(args.output_dir / "reason_counts.csv", [{"ReasonPrefix": key, "Count": value} for key, value in sorted(reason_counts.items())])
    if passed:
        write_csv(args.output_dir / "frozen_17day_collection_plan.csv", choose_dates(day_rows))

    summary = {
        "Gate": "V3.0 G2 shadow opportunity Gate 0",
        "Definition": "same-direction decision reached secondary branch during another normal trade's SL_SENT-to-exit window; self trade excluded",
        "ProtectedWindows": len(windows),
        "Opportunities": len(opportunities),
        "H1Opportunities": sum(by_month[month] for month in H1_MONTHS),
        "MonthlyOpportunities": {month: by_month[month] for month in (*H1_MONTHS, JULY)},
        "JulyOpportunities": by_month[JULY],
        "EstimatedLabelReadyRate": round(label_rate, 4),
        "Thresholds": {"H1Total": MIN_H1_TOTAL, "Monthly": MIN_MONTHLY, "July": MIN_JULY, "LabelRate": MIN_LABEL_RATE},
        "Passed": passed,
        "FailedThresholds": failed,
        "NoPnlConclusion": True,
    }
    (args.output_dir / "summary.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(summary, ensure_ascii=False))


if __name__ == "__main__":
    main()
