#!/usr/bin/env python3
"""Audit V4 Zone Touch labels against independent global Rich M5 bars."""

from __future__ import annotations

import argparse
import csv
import json
from collections import defaultdict, Counter
from pathlib import Path


def number(row: dict[str, str], name: str) -> float:
    return float(row[name])


def label_touch(touch: dict[str, str], market: list[dict[str, str]], terminal_bar: int | None) -> dict[str, object]:
    ordered = sorted((row for row in market if row["SnapshotID"] == touch["SnapshotID"]), key=lambda row: int(row["Bar"]))
    touch_bar = int(touch["Bar"])
    future = [row for row in ordered if int(row["Bar"]) > touch_bar][:6]
    complete = len(future) == 6
    low, high = number(touch, "ZoneLow"), number(touch, "ZoneHigh")
    direction = touch["Direction"]
    if complete:
        future_high, future_low = max(number(row, "High") for row in future), min(number(row, "Low") for row in future)
        favorable, adverse = ((max(0.0, future_high-high), max(0.0, low-future_low)) if direction == "Bull" else (max(0.0, low-future_low), max(0.0, future_high-high)))
    else:
        favorable = adverse = None
    terminal_inside = terminal_bar is not None and touch_bar < terminal_bar <= (int(future[-1]["Bar"]) if future else touch_bar)
    return {"LabelStatus": "Complete" if complete else "BoundaryCensored", "FavorableResponsePoints": favorable, "AdverseResponsePoints": adverse, "InvalidatedWithin6Bars": terminal_inside}


def read_all(directory: Path, suffix: str) -> list[dict[str, str]]:
    rows=[]
    for path in directory.glob(f"*_{suffix}.csv"):
        with path.open(encoding="utf-8-sig", newline="") as handle: rows.extend(csv.DictReader(handle))
    return rows


def main() -> None:
    parser=argparse.ArgumentParser(); parser.add_argument("--log-dir", type=Path, required=True); parser.add_argument("--plan", type=Path); parser.add_argument("--output", type=Path, required=True); args=parser.parse_args()
    events=read_all(args.log_dir,"zone_behavior_events"); market=read_all(args.log_dir,"rich_bar_features")
    market_by_snapshot=defaultdict(list)
    for row in market: market_by_snapshot[row["SnapshotID"]].append(row)
    planned=None
    if args.plan:
        with args.plan.open(encoding="utf-8-sig",newline="") as handle: planned={x["TradingDate"] for x in csv.DictReader(handle)}
        market_by_snapshot={key:value for key,value in market_by_snapshot.items() if max(Counter(x["Time"][:10] for x in value).items(), key=lambda x:x[1])[0] in planned}
    grouped=defaultdict(list)
    for row in events: grouped[(row["SnapshotID"],row["ZoneID"])].append(row)
    rows=[]
    for (snapshot,zone), values in grouped.items():
        touches=[x for x in values if x["EventType"]=="Touch" and x["TouchOrdinal"]=="1"]
        terminals=[x for x in values if x["EventType"]=="Invalidated"]
        if len(touches)!=1 or snapshot not in market_by_snapshot: continue
        row=label_touch(touches[0],market_by_snapshot[snapshot],int(terminals[0]["Bar"]) if terminals else None)
        row.update({"SnapshotID":snapshot,"ZoneID":zone,"TouchTime":touches[0]["Time"],"TouchBar":touches[0]["Bar"]}); rows.append(row)
    args.output.parent.mkdir(parents=True,exist_ok=True)
    with args.output.open("w",encoding="utf-8",newline="") as handle:
        writer=csv.DictWriter(handle,fieldnames=list(rows[0]) if rows else ["SnapshotID"]); writer.writeheader(); writer.writerows(rows)
    print(json.dumps({"FirstTouches":len(rows),"Complete":sum(x["LabelStatus"]=="Complete" for x in rows),"BoundaryCensored":sum(x["LabelStatus"]!="Complete" for x in rows)}))

if __name__=="__main__": main()
