#!/usr/bin/env python3
"""Run the pre-registered v3.0 G2 microstructure elimination screen."""

from __future__ import annotations

import argparse
import csv
import json
import statistics
from collections import defaultdict
from pathlib import Path


H1_MONTHS = ("202601", "202602", "202603", "202604", "202605", "202606")
JULY = "202607"


def side_sign(row: dict[str, str]) -> float:
    return 1.0 if row["Side"] == "Long" else -1.0


def numeric(row: dict[str, str], name: str) -> float:
    return float(row[name])


def candidate(row: dict[str, str], name: str, threshold: float) -> bool:
    sign = side_sign(row)
    if name == "Aggressor60Aligned":
        return sign * (numeric(row, "BuyVolume60") - numeric(row, "SellVolume60")) >= 0
    if name == "ZoneTouchDeltaAligned":
        return sign * numeric(row, "ZoneTouchDelta") >= 0
    if name == "PocMigrationAligned":
        return sign * (numeric(row, "PocMigration1") + numeric(row, "PocMigration2")) >= 0
    if name == "ZoneOccupancyMedian":
        return numeric(row, "ZoneOccupiedLevelRatio") >= threshold
    raise ValueError(name)


def net(rows: list[dict[str, str]]) -> float:
    return round(sum(numeric(row, "NetPnLDollars") for row in rows), 2)


def metrics(rows: list[dict[str, str]]) -> dict[str, float | int]:
    value = net(rows)
    return {"Trades": len(rows), "Net": value, "PerTrade": round(value / len(rows), 2) if rows else 0.0}


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--labels", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    args = parser.parse_args()

    with args.labels.open(encoding="utf-8-sig", newline="") as handle:
        rows = list(csv.DictReader(handle))
    g2 = [row for row in rows if row["OutcomeClass"] == "NormalOutcome" and row["IsSecondary"] == "true"]
    by_month: dict[str, list[dict[str, str]]] = defaultdict(list)
    for row in g2:
        by_month[row["SignalID"][:6]].append(row)
    if sum(len(by_month[month]) for month in H1_MONTHS) < 20:
        raise SystemExit("Insufficient H1 G2 sample")

    names = ("Aggressor60Aligned", "ZoneTouchDeltaAligned", "PocMigrationAligned", "ZoneOccupancyMedian")
    monthly: list[dict[str, object]] = []
    summary: list[dict[str, object]] = []
    for name in names:
        h1_rows: list[dict[str, str]] = []
        h1_kept: list[dict[str, str]] = []
        for holdout in H1_MONTHS:
            train = [row for month in H1_MONTHS if month != holdout for row in by_month[month]]
            threshold = statistics.median(numeric(row, "ZoneOccupiedLevelRatio") for row in train) if name == "ZoneOccupancyMedian" else 0.0
            test = by_month[holdout]
            kept = [row for row in test if candidate(row, name, threshold)]
            excluded = [row for row in test if not candidate(row, name, threshold)]
            h1_rows.extend(test)
            h1_kept.extend(kept)
            monthly.append({"Candidate": name, "Period": holdout, "Threshold": threshold, **{f"Baseline{k}": v for k, v in metrics(test).items()}, **{f"Kept{k}": v for k, v in metrics(kept).items()}, **{f"Excluded{k}": v for k, v in metrics(excluded).items()}})

        h1_excluded = [row for row in h1_rows if row not in h1_kept]
        h1_threshold = statistics.median(numeric(row, "ZoneOccupiedLevelRatio") for row in h1_rows) if name == "ZoneOccupancyMedian" else 0.0
        july_rows = by_month[JULY]
        july_kept = [row for row in july_rows if candidate(row, name, h1_threshold)]
        july_excluded = [row for row in july_rows if not candidate(row, name, h1_threshold)]
        summary.append({"Candidate": name, "H1ThresholdForJuly": h1_threshold, **{f"H1Baseline{k}": v for k, v in metrics(h1_rows).items()}, **{f"H1Kept{k}": v for k, v in metrics(h1_kept).items()}, **{f"H1Excluded{k}": v for k, v in metrics(h1_excluded).items()}, **{f"JulyBaseline{k}": v for k, v in metrics(july_rows).items()}, **{f"JulyKept{k}": v for k, v in metrics(july_kept).items()}, **{f"JulyExcluded{k}": v for k, v in metrics(july_excluded).items()}})

    args.output_dir.mkdir(parents=True, exist_ok=True)
    for name, data in (("monthly_lomo.csv", monthly), ("summary.csv", summary)):
        with (args.output_dir / name).open("w", encoding="utf-8", newline="") as handle:
            writer = csv.DictWriter(handle, fieldnames=list(data[0]))
            writer.writeheader()
            writer.writerows(data)
    (args.output_dir / "metadata.json").write_text(json.dumps({"InputG2NormalOutcomes": len(g2), "H1Months": H1_MONTHS, "JulyStressMonth": JULY, "Candidates": names}, indent=2), encoding="utf-8")


if __name__ == "__main__":
    main()
