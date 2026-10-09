#!/usr/bin/env python3
"""Build explicitly counterfactual static outcomes from planned geometry and M5 bars."""

from __future__ import annotations

import argparse
from pathlib import Path

import pandas as pd


M5_COLUMNS = ("Time", "Open", "High", "Low", "Close")
WINDOW_BARS = 12
POINT_DOLLARS_3_CONTRACTS = 6.0


def _gross(side: str, entry: float, exit_price: float) -> float:
    direction = 1.0 if side == "Long" else -1.0
    return (exit_price - entry) * direction * POINT_DOLLARS_3_CONTRACTS


def label_candidate(candidate: pd.Series, bars: pd.DataFrame) -> dict:
    decision_time = pd.Timestamp(candidate["DecisionTime"])
    expected_times = pd.date_range(decision_time + pd.Timedelta(minutes=5), periods=WINDOW_BARS, freq="5min")
    if "Time" in bars.columns:
        indexed = bars.copy()
        indexed["Time"] = pd.to_datetime(indexed["Time"])
        indexed = indexed.set_index("Time")
    else:
        indexed = bars
    future = indexed.reindex(expected_times)
    base = {
        "CandidateID": candidate["CandidateID"],
        "DecisionTime": decision_time,
        "DecisionBar": candidate["DecisionBar"],
        "Side": candidate["Side"],
        "PlannedEntry": float(candidate["PlannedEntry"]),
        "PlannedStop": float(candidate["PlannedStop"]),
        "PlannedTarget": float(candidate["PlannedTarget"]),
        "WindowBars": WINDOW_BARS,
        "OutcomeSource": "StaticPlanCounterfactual",
    }
    if future.loc[:, ["Open", "High", "Low", "Close"]].isna().any().any():
        return {
            **base, "TerminalLabel": "Censored", "OutcomeTime": pd.NaT, "StaticExitPrice": pd.NA,
            "StrictGrossDollars": pd.NA, "LowerBoundGrossDollars": pd.NA, "UpperBoundGrossDollars": pd.NA,
        }

    side = base["Side"]
    stop = base["PlannedStop"]
    target = base["PlannedTarget"]
    for timestamp, bar in future.iterrows():
        hit_stop = float(bar["Low"]) <= stop if side == "Long" else float(bar["High"]) >= stop
        hit_target = float(bar["High"]) >= target if side == "Long" else float(bar["Low"]) <= target
        if hit_stop and hit_target:
            return {
                **base, "TerminalLabel": "Ambiguous", "OutcomeTime": timestamp, "StaticExitPrice": pd.NA,
                "StrictGrossDollars": pd.NA,
                "LowerBoundGrossDollars": _gross(side, base["PlannedEntry"], stop),
                "UpperBoundGrossDollars": _gross(side, base["PlannedEntry"], target),
            }
        if hit_stop:
            value = _gross(side, base["PlannedEntry"], stop)
            return {
                **base, "TerminalLabel": "SL", "OutcomeTime": timestamp, "StaticExitPrice": stop,
                "StrictGrossDollars": value, "LowerBoundGrossDollars": value, "UpperBoundGrossDollars": value,
            }
        if hit_target:
            value = _gross(side, base["PlannedEntry"], target)
            return {
                **base, "TerminalLabel": "TP", "OutcomeTime": timestamp, "StaticExitPrice": target,
                "StrictGrossDollars": value, "LowerBoundGrossDollars": value, "UpperBoundGrossDollars": value,
            }
    exit_price = float(future.iloc[-1]["Close"])
    value = _gross(side, base["PlannedEntry"], exit_price)
    return {
        **base, "TerminalLabel": "TimeStop", "OutcomeTime": future.index[-1], "StaticExitPrice": exit_price,
        "StrictGrossDollars": value, "LowerBoundGrossDollars": value, "UpperBoundGrossDollars": value,
    }


def label_candidate_ticks(candidate: pd.Series, ticks: pd.DataFrame) -> dict:
    """Four-day diagnostic only; it never alters the full-sample M5 label."""
    decision_time = pd.Timestamp(candidate["DecisionTime"])
    window_end = decision_time + pd.Timedelta(minutes=WINDOW_BARS * 5)
    if isinstance(ticks, tuple):
        sequences, m5_times, prices = ticks
    else:
        ordered = ticks.loc[:, ["Sequence", "M5Time", "Price"]].copy()
        ordered["M5Time"] = pd.to_datetime(ordered["M5Time"])
        ordered = ordered.sort_values("Sequence")
        sequences = ordered["Sequence"].to_numpy()
        m5_times = ordered["M5Time"].to_numpy(dtype="datetime64[ns]")
        prices = ordered["Price"].to_numpy(dtype=float)
    start = sequences.searchsorted(int(candidate["MarketSequence"]), side="right")
    end = m5_times.searchsorted(window_end.to_datetime64(), side="right")
    if start >= end:
        return {"TickTerminalLabel": "Censored", "TickOutcomeTime": pd.NaT}
    side = candidate["Side"]
    stop = float(candidate["PlannedStop"])
    target = float(candidate["PlannedTarget"])
    for index in range(start, end):
        price = prices[index]
        hit_stop = price <= stop if side == "Long" else price >= stop
        hit_target = price >= target if side == "Long" else price <= target
        if hit_stop:
            return {"TickTerminalLabel": "SL", "TickOutcomeTime": m5_times[index]}
        if hit_target:
            return {"TickTerminalLabel": "TP", "TickOutcomeTime": m5_times[index]}
    return {"TickTerminalLabel": "TimeStop", "TickOutcomeTime": m5_times[end - 1]}


def load_m5(rich_root: Path, deep_m5_csv: Path | None) -> pd.DataFrame:
    parts = []
    for path in sorted(rich_root.glob("*_rich_bar_features.csv")):
        frame = pd.read_csv(path, usecols=["Time", "Open", "High", "Low", "Close"])
        frame["M5Source"] = "RichM5"
        parts.append(frame)
    if deep_m5_csv is not None:
        deep = pd.read_csv(deep_m5_csv, usecols=["M5Time", "Open", "High", "Low", "Close"])
        deep = deep.rename(columns={"M5Time": "Time"})
        deep["M5Source"] = "DeepMarketTape"
        parts.append(deep)
    if not parts:
        raise FileNotFoundError("No Rich M5 input")
    bars = pd.concat(parts, ignore_index=True)
    bars["Time"] = pd.to_datetime(bars["Time"])
    for column in M5_COLUMNS[1:]:
        bars[column] = pd.to_numeric(bars[column], errors="raise")
    duplicate_time = bars.duplicated(["Time"], keep=False)
    if duplicate_time.any():
        comparable = ["Open", "High", "Low", "Close"]
        mismatch = bars.loc[duplicate_time].groupby("Time")[comparable].nunique().gt(1).any(axis=1)
        if mismatch.any():
            raise ValueError("Conflicting M5 values at the same time")
        bars = bars.drop_duplicates(["Time"], keep="first")
    return bars.sort_values("Time").reset_index(drop=True)


def build_labels(candidates: pd.DataFrame, bars: pd.DataFrame) -> pd.DataFrame:
    required = {
        "CandidateID", "DecisionTime", "DecisionBar", "Side", "PlannedEntry", "PlannedStop", "PlannedTarget",
    }
    missing = sorted(required - set(candidates.columns))
    if missing:
        raise ValueError(f"Missing Candidate columns: {missing}")
    if candidates["CandidateID"].duplicated().any():
        raise ValueError("Duplicate CandidateID")
    indexed_bars = bars.set_index("Time")
    labels = pd.DataFrame([label_candidate(row, indexed_bars) for _, row in candidates.iterrows()])
    if len(labels) != len(candidates) or labels["CandidateID"].duplicated().any():
        raise ValueError("Static label identity failure")
    return labels


def write_reports(labels: pd.DataFrame, output_dir: Path) -> None:
    output_dir.mkdir(parents=True, exist_ok=True)
    labels.to_csv(output_dir / "static_path_labels.csv", index=False, encoding="utf-8")
    summary = labels.groupby(["Side", "TerminalLabel"], dropna=False).size().rename("Candidates").reset_index()
    summary.to_csv(output_dir / "static_path_label_summary.csv", index=False, encoding="utf-8")


def write_four_day_tick_comparison(
    deep_root: Path, candidates: pd.DataFrame, labels: pd.DataFrame, output_dir: Path
) -> None:
    key = ["SignalID", "DecisionTime", "DecisionBar", "ResearchPath"]
    label_input = candidates.loc[:, ["CandidateID", *key]].rename(columns={"CandidateID": "StaticCandidateID"})
    label_input = label_input.merge(
        labels.loc[:, ["CandidateID", "TerminalLabel"]].rename(columns={"CandidateID": "StaticCandidateID"}),
        on="StaticCandidateID", validate="one_to_one"
    )
    label_input["DecisionTime"] = pd.to_datetime(label_input["DecisionTime"])
    scenarios = pd.concat(
        (pd.read_csv(path) for path in sorted(deep_root.glob("*_market_execution_scenarios.csv"))), ignore_index=True
    )
    scenarios["DecisionTime"] = pd.to_datetime(scenarios["DecisionTime"])
    deep = scenarios.merge(label_input, on=key, how="left", validate="one_to_one", indicator="StaticLabelJoin")
    if deep["StaticLabelJoin"].ne("both").any():
        raise ValueError("Deep Tick Scenario missing static M5 label")
    rows = []
    for snapshot_id, group in deep.groupby("SnapshotID", sort=False):
        tick_path = deep_root / f"{snapshot_id}_market_execution_ticks.csv"
        if not tick_path.exists():
            raise FileNotFoundError(f"Missing ticks for {snapshot_id}")
        ticks = pd.read_csv(tick_path, usecols=["Sequence", "M5Time", "Price"])
        ticks["M5Time"] = pd.to_datetime(ticks["M5Time"])
        ticks = ticks.sort_values("Sequence")
        tick_arrays = (
            ticks["Sequence"].to_numpy(), ticks["M5Time"].to_numpy(dtype="datetime64[ns]"),
            ticks["Price"].to_numpy(dtype=float),
        )
        for _, candidate in group.iterrows():
            tick = label_candidate_ticks(candidate, tick_arrays)
            rows.append({
                "StaticCandidateID": candidate["StaticCandidateID"], "DeepCandidateID": candidate["CandidateID"],
                "SignalID": candidate["SignalID"],
                "ResearchPath": candidate["ResearchPath"], "M5TerminalLabel": candidate["TerminalLabel"], **tick,
                "CalibrationScope": "FourDayDeepTickOnly",
            })
    detail = pd.DataFrame(rows)
    if len(detail) != len(deep) or detail["StaticCandidateID"].duplicated().any():
        raise ValueError("Tick calibration identity failure")
    detail.to_csv(output_dir / "four_day_tick_m5_comparison.csv", index=False, encoding="utf-8")
    matrix = pd.crosstab(detail["M5TerminalLabel"], detail["TickTerminalLabel"]).reset_index()
    matrix.to_csv(output_dir / "four_day_tick_m5_confusion_matrix.csv", index=False, encoding="utf-8")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--candidate-csv", type=Path, required=True)
    parser.add_argument("--rich-root", type=Path, required=True)
    parser.add_argument("--deep-m5-csv", type=Path)
    parser.add_argument("--deep-root", type=Path)
    parser.add_argument("--output-dir", type=Path, required=True)
    args = parser.parse_args()
    labels = build_labels(pd.read_csv(args.candidate_csv), load_m5(args.rich_root, args.deep_m5_csv))
    write_reports(labels, args.output_dir)
    if args.deep_root is not None:
        write_four_day_tick_comparison(args.deep_root, pd.read_csv(args.candidate_csv), labels, args.output_dir)
    print(labels["TerminalLabel"].value_counts().to_string())


if __name__ == "__main__":
    main()
