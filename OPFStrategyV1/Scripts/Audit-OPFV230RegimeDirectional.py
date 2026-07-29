#!/usr/bin/env python3
"""Audit decision-time regime availability and direct direction attribution."""

from __future__ import annotations

import argparse
import csv
from bisect import bisect_right
from collections import defaultdict
from pathlib import Path


def read_rows(directory: Path, pattern: str) -> list[dict[str, str]]:
    rows: list[dict[str, str]] = []
    for path in sorted(directory.glob(pattern)):
        with path.open(encoding="utf-8-sig", newline="") as handle:
            rows.extend(csv.DictReader(handle))
    return rows


def key_from_label(row: dict[str, str]) -> tuple[str, str, str, str]:
    return (row["SignalID"], row["DecisionTime"], row["DecisionBar"], row["ResearchPath"])


def key_from_decision(row: dict[str, str]) -> tuple[str, str, str, str]:
    return (row["SignalID"], row["Time"], row["Bar"], row["ResearchPath"])


def net(rows: list[dict[str, str]]) -> float:
    return round(sum(float(row["NetPnLDollars"]) for row in rows), 2)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--labels", type=Path, required=True)
    parser.add_argument("--actual-dir", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    args = parser.parse_args()

    with args.labels.open(encoding="utf-8-sig", newline="") as handle:
        labels = [row for row in csv.DictReader(handle) if row["OutcomeClass"] == "NormalOutcome"]
    decisions = read_rows(args.actual_dir, "*_execution_decisions.csv")
    decision_by_key = {key_from_decision(row): row for row in decisions}
    changes_by_snapshot: dict[str, list[dict[str, str]]] = defaultdict(list)
    for row in read_rows(args.actual_dir, "*_regime_changes.csv"):
        changes_by_snapshot[row["SnapshotID"]].append(row)
    for changes in changes_by_snapshot.values():
        changes.sort(key=lambda row: (row["Time"], int(row["Bar"])))

    enriched: list[dict[str, str]] = []
    for label in labels:
        decision = decision_by_key.get(key_from_label(label))
        if decision is None:
            raise SystemExit(f"Missing decision for label: {label['SignalID']}")
        changes = changes_by_snapshot[decision["SnapshotID"]]
        times = [row["Time"] for row in changes]
        index = bisect_right(times, label["DecisionTime"]) - 1
        if index < 0:
            raise SystemExit(f"No prior regime state: {label['SignalID']}")
        raw_regime = changes[index]["Regime"]
        state = raw_regime if raw_regime in {"BullTrend", "BearTrend"} else "ChopRange"
        enriched.append({**label, "ActualSnapshotID": decision["SnapshotID"], "DecisionRegime": raw_regime, "ThreeStateRegime": state})

    summary: list[dict[str, object]] = []
    for month_group, subset in (("H1", [r for r in enriched if r["SignalID"][:6] < "202607"]), ("July", [r for r in enriched if r["SignalID"][:6] == "202607"])):
        for regime in ("BullTrend", "BearTrend", "ChopRange"):
            for side in ("Long", "Short"):
                rows = [r for r in subset if r["ThreeStateRegime"] == regime and r["Side"] == side]
                summary.append({"Period": month_group, "Regime": regime, "Side": side, "Trades": len(rows), "Net": net(rows), "PerTrade": round(net(rows) / len(rows), 2) if rows else 0.0, "G2Trades": sum(r["IsSecondary"] == "true" for r in rows)})
        shorts = [r for r in subset if r["Side"] == "Short"]
        longs = [r for r in subset if r["Side"] == "Long"]
        summary.append({"Period": month_group, "Regime": "DirectShortOnly", "Side": "All", "Trades": len(shorts), "Net": net(shorts), "PerTrade": round(net(shorts) / len(shorts), 2) if shorts else 0.0, "G2Trades": sum(r["IsSecondary"] == "true" for r in shorts)})
        summary.append({"Period": month_group, "Regime": "DirectRemovedLong", "Side": "All", "Trades": len(longs), "Net": net(longs), "PerTrade": round(net(longs) / len(longs), 2) if longs else 0.0, "G2Trades": sum(r["IsSecondary"] == "true" for r in longs)})

    args.output_dir.mkdir(parents=True, exist_ok=True)
    with (args.output_dir / "normal_outcomes_with_regime.csv").open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=list(enriched[0]))
        writer.writeheader()
        writer.writerows(enriched)
    with (args.output_dir / "summary.csv").open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=list(summary[0]))
        writer.writeheader()
        writer.writerows(summary)


if __name__ == "__main__":
    main()
