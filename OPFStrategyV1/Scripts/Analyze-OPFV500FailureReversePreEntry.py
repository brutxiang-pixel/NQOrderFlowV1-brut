#!/usr/bin/env python3
"""Read-only pre-entry screen for immediate FailureReverse Actual trades."""

from __future__ import annotations

import argparse
from pathlib import Path

import pandas as pd


IMMEDIATE = {
    "FailureReverse_ObservationInvalidated",
    "FailureReverse_ObservationInvalidated_WideStop1_5R",
}


def load_tape(directory: Path) -> tuple[pd.DataFrame, pd.DataFrame]:
    scenarios = pd.concat(
        (pd.read_csv(path) for path in directory.glob("*_market_execution_scenarios.csv")),
        ignore_index=True,
    )
    bars = pd.concat(
        (pd.read_csv(path) for path in directory.glob("*_rich_bar_features.csv")),
        ignore_index=True,
    )
    scenarios["DecisionTime"] = pd.to_datetime(scenarios["DecisionTime"])
    bars["Time"] = pd.to_datetime(bars["Time"])
    scenarios = scenarios.loc[scenarios["ResearchPath"].isin(IMMEDIATE)].copy()
    scenarios = scenarios.drop_duplicates(["SignalID", "ResearchPath"], keep="last")
    return scenarios, bars


def screen(actual: pd.DataFrame, scenarios: pd.DataFrame, bars: pd.DataFrame) -> pd.DataFrame:
    actual = actual.loc[actual["ResearchPath"].isin(IMMEDIATE)].copy()
    actual["EntryTime"] = pd.to_datetime(actual["EntryTime"])
    merged = actual.merge(
        scenarios[
            ["SignalID", "ResearchPath", "DecisionTime", "Side", "PlannedEntry", "PlannedRiskPoints", "ZoneLow", "ZoneHigh"]
        ],
        on=["SignalID", "ResearchPath", "Side"],
        how="left",
        validate="many_to_one",
    )
    next_bars = bars[["Time", "High", "Low", "Close"]].copy()
    next_bars = next_bars.rename(columns={"Time": "NextBarTime", "High": "NextHigh", "Low": "NextLow", "Close": "NextClose"})
    next_bars = next_bars.drop_duplicates(["NextBarTime"], keep="last")
    merged["NextBarTime"] = merged["DecisionTime"] + pd.Timedelta(minutes=5)
    merged = merged.merge(next_bars, on="NextBarTime", how="left", validate="many_to_one")
    short = merged["Side"].eq("Short")
    merged["PenetrationR"] = 0.0
    merged.loc[short, "PenetrationR"] = (merged.loc[short, "ZoneLow"] - merged.loc[short, "PlannedEntry"]) / merged.loc[short, "PlannedRiskPoints"]
    merged.loc[~short, "PenetrationR"] = (merged.loc[~short, "PlannedEntry"] - merged.loc[~short, "ZoneHigh"]) / merged.loc[~short, "PlannedRiskPoints"]
    merged["Depth025"] = merged["PenetrationR"].ge(0.25)
    merged["Wait1CloseOutside"] = ((short & merged["NextClose"].lt(merged["ZoneLow"])) | (~short & merged["NextClose"].gt(merged["ZoneHigh"])))
    merged["Wait1NeverReenter"] = ((short & merged["NextHigh"].lt(merged["ZoneLow"])) | (~short & merged["NextLow"].gt(merged["ZoneHigh"])))
    merged["Candidate"] = merged["Depth025"] & merged["Wait1CloseOutside"] & merged["Wait1NeverReenter"]
    return merged


def summarize(rows: pd.DataFrame, name: str) -> dict[str, object]:
    gross_profit = rows.loc[rows["GrossPnLDollars"] > 0, "GrossPnLDollars"].sum()
    gross_loss = -rows.loc[rows["GrossPnLDollars"] < 0, "GrossPnLDollars"].sum()
    return {
        "Screen": name,
        "Trades": len(rows),
        "NormalNet": round(rows["NetPnLDollars"].sum(), 2),
        "GrossPF": round(gross_profit / gross_loss, 4) if gross_loss else None,
    }


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--actual", type=Path, required=True)
    parser.add_argument("--tape", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    actual = pd.read_csv(args.actual)
    scenarios, bars = load_tape(args.tape)
    result = screen(actual, scenarios, bars)
    args.output.mkdir(parents=True, exist_ok=True)
    result.to_csv(args.output / "v500_failure_reverse_pre_entry_matches.csv", index=False)
    matched = result.loc[result["DecisionTime"].notna() & result["NextClose"].notna()].copy()
    reports = pd.DataFrame([
        summarize(matched, "MatchedImmediateActual"),
        summarize(matched.loc[matched["Depth025"]], "PenetrationR_GE_025"),
        summarize(matched.loc[matched["Candidate"]], "Depth025_Wait1Outside_NoReentry"),
    ])
    reports.to_csv(args.output / "v500_failure_reverse_pre_entry_summary.csv", index=False)
    by_segment = []
    for segment, group in matched.groupby("TrainingSegment", sort=True):
        by_segment.append(summarize(group, f"{segment}:ImmediateActual"))
        by_segment.append(summarize(group.loc[group["Depth025"]], f"{segment}:PenetrationR_GE_025"))
        by_segment.append(summarize(group.loc[group["Candidate"]], f"{segment}:Depth025_Wait1Outside_NoReentry"))
    pd.DataFrame(by_segment).to_csv(args.output / "v500_failure_reverse_pre_entry_segments.csv", index=False)
    print(reports.to_string(index=False))


if __name__ == "__main__":
    main()
