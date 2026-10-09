#!/usr/bin/env python3
"""Leakage-safe first-Touch Zone Behavior descriptive study (V4 stage 3)."""

from __future__ import annotations

import argparse
import csv
import json
import math
import statistics
from collections import Counter, defaultdict
from datetime import datetime
from pathlib import Path


H1_MONTHS = ("202601", "202602", "202603", "202604", "202605", "202606")
JULY = "202607"
FEATURES = ("SignedDeltaRatio", "ZoneVolumeShare", "FavorableAdvancePoints", "AdversePenetrationPoints")


def number(row: dict[str, str], name: str) -> float:
    return float(row[name])


def truth(row: dict[str, str], name: str) -> bool:
    return row.get(name, "").strip().lower() == "true"


def trading_date(rows: list[dict[str, str]]) -> str:
    dates = Counter(row["Time"][:10] for row in rows)
    return max(dates.items(), key=lambda item: (item[1], item[0]))[0]


def response(direction: str, low: float, high: float, window: list[dict[str, str]]) -> tuple[float, float]:
    future_high = max(number(row, "High") for row in window)
    future_low = min(number(row, "Low") for row in window)
    if direction == "Bull":
        return max(0.0, future_high - high), max(0.0, low - future_low)
    if direction == "Bear":
        return max(0.0, low - future_low), max(0.0, future_high - high)
    raise ValueError(f"Unsupported Zone direction: {direction}")


def build_first_touch_rows(events: list[dict[str, str]], bars: list[dict[str, str]], planned_dates: set[str]) -> list[dict[str, object]]:
    """Return exactly one descriptive row for each planned first Touch."""
    events_by_zone: dict[tuple[str, str], list[dict[str, str]]] = defaultdict(list)
    bars_by_zone: dict[tuple[str, str], list[dict[str, str]]] = defaultdict(list)
    snapshot_bars: dict[str, list[dict[str, str]]] = defaultdict(list)
    for row in events:
        events_by_zone[(row["SnapshotID"], row["ZoneID"])].append(row)
    for row in bars:
        bars_by_zone[(row["SnapshotID"], row["ZoneID"])].append(row)
        snapshot_bars[row["SnapshotID"]].append(row)
    snapshot_dates = {snapshot: trading_date(rows) for snapshot, rows in snapshot_bars.items()}

    output: list[dict[str, object]] = []
    for key, zone_events in events_by_zone.items():
        snapshot, zone = key
        date = snapshot_dates.get(snapshot)
        if date not in planned_dates:
            continue
        ordered_events = sorted(zone_events, key=lambda row: (int(row["Bar"]), row["EventType"]))
        births = [row for row in ordered_events if row["EventType"] == "Birth"]
        touches = [row for row in ordered_events if row["EventType"] == "Touch" and int(row["TouchOrdinal"]) == 1]
        if len(births) != 1 or len(touches) != 1:
            continue
        birth, touch = births[0], touches[0]
        direction = touch["Direction"]
        low, high = number(touch, "ZoneLow"), number(touch, "ZoneHigh")
        ordered_bars = sorted(bars_by_zone[key], key=lambda row: int(row["Bar"]))
        touch_bar = int(touch["Bar"])
        touch_rows = [row for row in ordered_bars if int(row["Bar"]) == touch_bar]
        if len(touch_rows) != 1:
            continue
        pre_touch = [row for row in ordered_bars if int(birth["Bar"]) <= int(row["Bar"]) <= touch_bar]
        post_touch = [row for row in ordered_bars if int(row["Bar"]) > touch_bar]
        window = post_touch[:12]
        touch_row = touch_rows[0]
        all_volume = number(touch_row, "CumulativeBuyVolume") + number(touch_row, "CumulativeSellVolume")
        zone_volume = number(touch_row, "CumulativeZoneBuyVolume") + number(touch_row, "CumulativeZoneSellVolume")
        delta = number(touch_row, "CumulativeBuyVolume") - number(touch_row, "CumulativeSellVolume")
        signed_delta = delta if direction == "Bull" else -delta
        terminal_events = [row for row in ordered_events if row["EventType"] in {"Invalidated", "Expired", "SnapshotEnd"}]
        terminal = terminal_events[0] if terminal_events else None
        last_window_bar = int(window[-1]["Bar"]) if window else touch_bar
        terminal_inside_window = bool(terminal and touch_bar < int(terminal["Bar"]) <= last_window_bar)
        complete = len(window) == 12 and not terminal_inside_window
        favorable, adverse = response(direction, low, high, window) if complete else (None, None)
        invalidated = bool(terminal and terminal["EventType"] == "Invalidated" and terminal_inside_window)
        output.append({
            "SnapshotID": snapshot, "TradingDate": date, "Month": date[:7].replace("-", ""), "ZoneID": zone,
            "Direction": direction, "ZoneLow": low, "ZoneHigh": high, "BirthBar": int(birth["Bar"]), "TouchBar": touch_bar,
            "PreTouchBars": len(pre_touch), "PostTouchBarsObserved": len(post_touch),
            "LabelStatus": "Complete" if complete else "Censored", "InvalidatedWithin12Bars": str(invalidated).lower(),
            "SignedDeltaRatio": signed_delta / all_volume if all_volume else None,
            "ZoneVolumeShare": zone_volume / all_volume if all_volume else None,
            "FavorableAdvancePoints": number(touch_row, "MaxFavorableExcursion"),
            "AdversePenetrationPoints": number(touch_row, "MaxAdverseExcursion"),
            "FavorableResponsePoints": favorable, "AdverseResponsePoints": adverse,
            "NetResponsePoints": favorable - adverse if complete else None,
        })
    return sorted(output, key=lambda row: (row["TradingDate"], row["SnapshotID"], row["ZoneID"]))


def terciles(values: list[float]) -> tuple[float, float]:
    ordered = sorted(values)
    return (statistics.quantiles(ordered, n=3, method="inclusive")[0], statistics.quantiles(ordered, n=3, method="inclusive")[1])


def bucket(value: float, boundaries: tuple[float, float]) -> str:
    return "Low" if value <= boundaries[0] else "Mid" if value <= boundaries[1] else "High"


def median(rows: list[dict[str, object]], field: str) -> float | None:
    values = [float(row[field]) for row in rows if row[field] is not None]
    return round(statistics.median(values), 4) if values else None


def summarize(rows: list[dict[str, object]]) -> dict[str, object]:
    complete = [row for row in rows if row["LabelStatus"] == "Complete"]
    return {
        "Samples": len(rows), "CompleteSamples": len(complete),
        "CompleteRate": round(len(complete) / len(rows), 4) if rows else None,
        "MedianFavorable": median(complete, "FavorableResponsePoints"),
        "MedianAdverse": median(complete, "AdverseResponsePoints"),
        "MedianNetResponse": median(complete, "NetResponsePoints"),
        "InvalidationRate": round(sum(row["InvalidatedWithin12Bars"] == "true" for row in complete) / len(complete), 4) if complete else None,
    }


def feature_lomo(rows: list[dict[str, object]]) -> list[dict[str, object]]:
    complete = [row for row in rows if row["LabelStatus"] == "Complete"]
    results: list[dict[str, object]] = []
    for feature in FEATURES:
        feature_rows = [row for row in complete if row[feature] is not None]
        for holdout in H1_MONTHS:
            train = [float(row[feature]) for row in feature_rows if row["Month"] in H1_MONTHS and row["Month"] != holdout]
            test = [row for row in feature_rows if row["Month"] == holdout]
            if len(train) < 3:
                continue
            boundaries = terciles(train)
            for name in ("Low", "Mid", "High"):
                subset = [row for row in test if bucket(float(row[feature]), boundaries) == name]
                results.append({"Feature": feature, "Evaluation": f"LOMO_{holdout}", "Bin": name, "BoundaryLow": boundaries[0], "BoundaryHigh": boundaries[1], **summarize(subset)})
        h1_values = [float(row[feature]) for row in feature_rows if row["Month"] in H1_MONTHS]
        if len(h1_values) >= 3:
            boundaries = terciles(h1_values)
            for name in ("Low", "Mid", "High"):
                subset = [row for row in feature_rows if row["Month"] == JULY and bucket(float(row[feature]), boundaries) == name]
                results.append({"Feature": feature, "Evaluation": "JulyStress", "Bin": name, "BoundaryLow": boundaries[0], "BoundaryHigh": boundaries[1], **summarize(subset)})
    return results


def eligible(lomo: list[dict[str, object]], feature: str) -> dict[str, object]:
    h1_pairs = []
    for month in H1_MONTHS:
        rows = [row for row in lomo if row["Feature"] == feature and row["Evaluation"] == f"LOMO_{month}"]
        lookup = {row["Bin"]: row for row in rows}
        if "Low" in lookup and "High" in lookup and lookup["Low"]["MedianNetResponse"] is not None and lookup["High"]["MedianNetResponse"] is not None:
            h1_pairs.append(float(lookup["High"]["MedianNetResponse"]) - float(lookup["Low"]["MedianNetResponse"]))
    july = {row["Bin"]: row for row in lomo if row["Feature"] == feature and row["Evaluation"] == "JulyStress"}
    july_diff = None if "Low" not in july or "High" not in july or july["Low"]["MedianNetResponse"] is None or july["High"]["MedianNetResponse"] is None else float(july["High"]["MedianNetResponse"]) - float(july["Low"]["MedianNetResponse"])
    combined = statistics.median(h1_pairs) if h1_pairs else 0.0
    same_direction = sum((value > 0) == (combined > 0) and value != 0 for value in h1_pairs)
    july_ok = july_diff is not None and july_diff != 0 and (july_diff > 0) == (combined > 0)
    enough = all(row["Samples"] >= 30 for row in july.values()) if july else False
    return {"Feature": feature, "LomoComparableMonths": len(h1_pairs), "SameDirectionMonths": same_direction,
            "MedianHighMinusLowNetResponse": round(combined, 4), "JulyHighMinusLowNetResponse": july_diff,
            "JulyBinsAtLeast30": enough, "FollowUpEligible": len(h1_pairs) == 6 and same_direction >= 5 and abs(combined) >= 1.0 and july_ok and enough}


def read_csvs(log_dir: Path, suffix: str) -> list[dict[str, str]]:
    rows = []
    for path in sorted(log_dir.glob(f"*_{suffix}.csv")):
        with path.open(encoding="utf-8-sig", newline="") as handle:
            rows.extend(csv.DictReader(handle))
    return rows


def write_csv(path: Path, rows: list[dict[str, object]]) -> None:
    if not rows:
        path.write_text("", encoding="utf-8")
        return
    with path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--log-dir", type=Path, required=True)
    parser.add_argument("--plan", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    args = parser.parse_args()
    with args.plan.open(encoding="utf-8-sig", newline="") as handle:
        planned_dates = {row["TradingDate"] for row in csv.DictReader(handle)}
    rows = build_first_touch_rows(read_csvs(args.log_dir, "zone_behavior_events"), read_csvs(args.log_dir, "zone_behavior_bars"), planned_dates)
    lomo = feature_lomo(rows)
    coverage = [{"Month": month, **summarize([row for row in rows if row["Month"] == month])} for month in (*H1_MONTHS, JULY)]
    summary = {"Rows": len(rows), "CompleteRows": sum(row["LabelStatus"] == "Complete" for row in rows), "Features": [eligible(lomo, feature) for feature in FEATURES]}
    args.output_dir.mkdir(parents=True, exist_ok=True)
    write_csv(args.output_dir / "first_touch_labels.csv", rows)
    write_csv(args.output_dir / "feature_lomo.csv", lomo)
    write_csv(args.output_dir / "monthly_coverage.csv", coverage)
    (args.output_dir / "summary.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")


if __name__ == "__main__":
    main()
