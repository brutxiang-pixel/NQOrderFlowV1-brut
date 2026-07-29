#!/usr/bin/env python3
"""Zero-Replay Gate 0 for the V3.0 aggressor-pressure/price-response family."""

from __future__ import annotations

import argparse
import csv
import json
import re
from collections import Counter
from pathlib import Path


DELTA_SHARE_FLOOR = 0.10  # Deliberately permissive density floor, not an alpha parameter.
CLOSE_UPPER = 2.0 / 3.0
CLOSE_LOWER = 1.0 / 3.0
MIN_H1_RESPONSE_EVENTS = 90
MIN_JULY_RESPONSE_EVENTS = 20
MIN_EVENTS_PER_H1_DAY = 8
MIN_EVENTS_PER_JULY_DAY = 5
DIRECTION_RE = re.compile(r"Buy:(\d+)/(\d+)\|Sell:(\d+)/(\d+)")


def read_csv(path: Path) -> list[dict[str, str]]:
    with path.open(encoding="utf-8-sig", newline="") as handle:
        return list(csv.DictReader(handle))


def write_csv(path: Path, rows: list[dict[str, object]]) -> None:
    if not rows:
        return
    with path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--archive-dir", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    args = parser.parse_args()

    all_rows: list[dict[str, object]] = []
    day_summary: list[dict[str, object]] = []
    for audit_path in sorted(args.archive_dir.glob("*_microstructure_audit_bars.csv")):
        rich_path = next(args.archive_dir.glob(audit_path.name.replace("microstructure_audit_bars", "rich_bar_features")))
        rich_by_bar = {row["Bar"]: row for row in read_csv(rich_path)}
        tick_rows = [row for row in read_csv(audit_path) if row["Source"] == "OnNewTrade"]
        counts: Counter[str] = Counter()
        joined = 0
        parsed = 0
        date = tick_rows[0]["FirstTime"][:10]
        for tick in tick_rows:
            rich = rich_by_bar.get(tick["Bar"])
            match = DIRECTION_RE.fullmatch(tick["DirectionSummary"])
            if rich is None or match is None:
                continue
            joined += 1
            buy_volume, sell_volume = int(match.group(2)), int(match.group(4))
            total = buy_volume + sell_volume
            high, low, close = float(rich["High"]), float(rich["Low"]), float(rich["Close"])
            if total <= 0 or high <= low:
                continue
            parsed += 1
            delta_share = (buy_volume - sell_volume) / total
            close_location = (close - low) / (high - low)
            pressure = "None"
            if delta_share >= DELTA_SHARE_FLOOR and close_location >= CLOSE_UPPER:
                pressure = "BuyPressureUpClose"
            elif delta_share >= DELTA_SHARE_FLOOR and close_location <= CLOSE_LOWER:
                pressure = "BuyPressureDownClose"
            elif delta_share <= -DELTA_SHARE_FLOOR and close_location <= CLOSE_LOWER:
                pressure = "SellPressureDownClose"
            elif delta_share <= -DELTA_SHARE_FLOOR and close_location >= CLOSE_UPPER:
                pressure = "SellPressureUpClose"
            counts[pressure] += 1
            if pressure != "None":
                all_rows.append({
                    "Date": date,
                    "Month": date.replace("-", "")[:6],
                    "Bar": tick["Bar"],
                    "Time": rich["Time"],
                    "ResponseClass": pressure,
                    "BuyVolume": buy_volume,
                    "SellVolume": sell_volume,
                    "DeltaShare": round(delta_share, 6),
                    "CloseLocation": round(close_location, 6),
                })
        response = counts["BuyPressureDownClose"] + counts["SellPressureUpClose"]
        day_summary.append({
            "Date": date,
            "Month": date.replace("-", "")[:6],
            "TickBars": len(tick_rows),
            "JoinedBars": joined,
            "ParsedBars": parsed,
            "BuyPressureUpClose": counts["BuyPressureUpClose"],
            "SellPressureDownClose": counts["SellPressureDownClose"],
            "BuyPressureDownClose": counts["BuyPressureDownClose"],
            "SellPressureUpClose": counts["SellPressureUpClose"],
            "OpposingPressureResponse": response,
        })

    h1 = [row for row in day_summary if str(row["Month"]) < "202607"]
    july = [row for row in day_summary if str(row["Month"]) == "202607"]
    h1_events = sum(int(row["OpposingPressureResponse"]) for row in h1)
    july_events = sum(int(row["OpposingPressureResponse"]) for row in july)
    failed = []
    if h1_events < MIN_H1_RESPONSE_EVENTS:
        failed.append("H1 response density")
    if july_events < MIN_JULY_RESPONSE_EVENTS:
        failed.append("July response density")
    if any(int(row["OpposingPressureResponse"]) < MIN_EVENTS_PER_H1_DAY for row in h1):
        failed.append("H1 day coverage")
    if any(int(row["OpposingPressureResponse"]) < MIN_EVENTS_PER_JULY_DAY for row in july):
        failed.append("July day coverage")

    args.output_dir.mkdir(parents=True, exist_ok=True)
    write_csv(args.output_dir / "response_events.csv", all_rows)
    write_csv(args.output_dir / "day_summary.csv", day_summary)
    summary = {
        "Gate": "V3.0 aggressor pressure-price response Gate 0",
        "Data": "OnNewTrade raw Buy/Sell volume joined to same-bar Rich OHLC; DOM excluded",
        "Proxy": "abs(delta share)>=10% with close in opposite outer third",
        "ImportantLimitation": "bar aggregate distinguishes pressure response only; it cannot distinguish absorption from intrabar exhaustion",
        "H1OpposingPressureResponse": h1_events,
        "JulyOpposingPressureResponse": july_events,
        "Thresholds": {"H1": MIN_H1_RESPONSE_EVENTS, "July": MIN_JULY_RESPONSE_EVENTS, "H1Day": MIN_EVENTS_PER_H1_DAY, "JulyDay": MIN_EVENTS_PER_JULY_DAY},
        "Passed": not failed,
        "FailedThresholds": failed,
        "NoPnlConclusion": True,
    }
    (args.output_dir / "summary.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(summary, ensure_ascii=False))


if __name__ == "__main__":
    main()
