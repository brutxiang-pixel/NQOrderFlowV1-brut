#!/usr/bin/env python3
"""Build a read-only, homogeneous Actual baseline for V5 cross-year research."""

from __future__ import annotations

import argparse
from pathlib import Path

import pandas as pd


EXCLUDED_SNAPSHOTS = {"OPF-20260728-233338"}
TRAINING_MONTHS = {
    *( (2025, month) for month in (4, 5, 6, 7, 10, 11, 12) ),
    *( (2026, month) for month in range(1, 8) ),
}
DEFAULT_ARCHIVES = (
    "opf_v500_pre_2025q4_frozen_baseline_active_logs_20260731",
    "opf_v300_live_gate_2025_06_complete_20snapshots_20260731",
    "opf_v300_live_gate_2025_07_rerun_complete_21snapshots_20260730",
    "opf_v500_frozen_baseline_2025q4_complete_62snapshots_plus_1_out_of_scope_20260731",
    "opf_v300_full_actual_replay_202601_to_20260728_20260729",
)


def filter_training_rows(rows: pd.DataFrame) -> pd.DataFrame:
    output = rows.copy()
    output["EntryTime"] = pd.to_datetime(output["EntryTime"])
    months = list(zip(output["EntryTime"].dt.year, output["EntryTime"].dt.month))
    return output.loc[
        output["Classification"].eq("Normal")
        & ~output["SnapshotID"].isin(EXCLUDED_SNAPSHOTS)
        & pd.Series(months, index=output.index).isin(TRAINING_MONTHS)
    ].copy()


def add_time_dimensions(rows: pd.DataFrame) -> pd.DataFrame:
    output = rows.copy()
    output["EntryTime"] = pd.to_datetime(output["EntryTime"])
    output["TradingDate"] = output["EntryTime"].dt.normalize()
    output["Month"] = output["EntryTime"].dt.strftime("%Y-%m")
    if "ArchiveName" in output:
        output.loc[
            output["ArchiveName"].eq("opf_v300_live_gate_2025_07_rerun_complete_21snapshots_20260730"),
            "Month",
        ] = "2025-07"
    output["WeekStart"] = output["TradingDate"] - pd.to_timedelta(output["TradingDate"].dt.weekday, unit="D")
    output["TrainingSegment"] = ""
    output.loc[output["Month"].isin(["2025-04", "2025-05", "2025-06", "2025-07"]), "TrainingSegment"] = "2025-SpringSummer"
    output.loc[output["Month"].isin(["2025-10", "2025-11", "2025-12"]), "TrainingSegment"] = "2025-Q4"
    output.loc[output["Month"].isin(["2026-01", "2026-02", "2026-03", "2026-04", "2026-05", "2026-06"]), "TrainingSegment"] = "2026-H1"
    output.loc[output["Month"].eq("2026-07"), "TrainingSegment"] = "2026-JulyStress"
    return output


def summarize(rows: pd.DataFrame) -> dict:
    if rows.empty:
        return {"Trades": 0, "GrossDollars": 0.0, "CommissionDollars": 0.0, "NetDollars": 0.0, "GrossPF": 0.0, "MaxDrawdownNet": 0.0}
    ordered = rows.sort_values("EntryTime")
    net_curve = ordered["NetPnLDollars"].cumsum()
    drawdown = net_curve - net_curve.cummax()
    gross_profit = ordered.loc[ordered["GrossPnLDollars"] > 0, "GrossPnLDollars"].sum()
    gross_loss = -ordered.loc[ordered["GrossPnLDollars"] < 0, "GrossPnLDollars"].sum()
    return {
        "Trades": int(len(ordered)),
        "GrossDollars": round(float(ordered["GrossPnLDollars"].sum()), 2),
        "CommissionDollars": round(float(ordered["CommissionDollars"].sum()), 2),
        "NetDollars": round(float(ordered["NetPnLDollars"].sum()), 2),
        "GrossPF": round(float(gross_profit / gross_loss), 4) if gross_loss else float("inf"),
        "MaxDrawdownNet": round(float(drawdown.min()), 2),
    }


def _read_csvs(directory: Path, suffix: str, columns: list[str]) -> pd.DataFrame:
    parts = [pd.read_csv(path, usecols=columns) for path in sorted(directory.rglob(f"*_{suffix}.csv"))]
    if not parts:
        raise FileNotFoundError(f"No *_{suffix}.csv files in {directory}")
    return pd.concat(parts, ignore_index=True)


def load_baseline(archive_root: Path) -> pd.DataFrame:
    account_columns = ["SnapshotID", "TradeID", "EntryTime", "ExitTime", "Side", "Classification", "Quantity", "GrossPnLDollars", "CommissionDollars", "NetPnLDollars"]
    execution_columns = ["SnapshotID", "TradeID", "SignalID", "ResearchPath", "ExitRole", "ActualMFE_R", "ActualMAE_R", "FilledRiskPoints"]
    account_parts = []
    execution_parts = []
    for archive in DEFAULT_ARCHIVES:
        directory = archive_root / archive
        if not directory.exists():
            raise FileNotFoundError(f"Required archive missing: {directory}")
        account = _read_csvs(directory, "live_account_pnl", account_columns)
        account["ArchiveName"] = archive
        account_parts.append(account)
        execution = _read_csvs(directory, "execution_trades", execution_columns)
        execution["ArchiveName"] = archive
        execution_parts.append(execution)
    account = filter_training_rows(pd.concat(account_parts, ignore_index=True))
    execution = pd.concat(execution_parts, ignore_index=True).drop_duplicates(["SnapshotID", "TradeID"], keep="last")
    baseline = account.merge(
        execution.drop(columns="ArchiveName"), on=["SnapshotID", "TradeID"], how="left", validate="many_to_one"
    )
    if baseline["ResearchPath"].isna().any():
        missing = int(baseline["ResearchPath"].isna().sum())
        raise ValueError(f"Normal account trade missing execution-path join: {missing}")
    return add_time_dimensions(baseline)


def summarize_by(rows: pd.DataFrame, columns: list[str]) -> pd.DataFrame:
    records = []
    for keys, group in rows.groupby(columns, dropna=False, sort=True):
        if not isinstance(keys, tuple):
            keys = (keys,)
        records.append(dict(zip(columns, keys)) | summarize(group))
    return pd.DataFrame(records)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--archive-root", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    args = parser.parse_args()
    baseline = load_baseline(args.archive_root)
    args.output_dir.mkdir(parents=True, exist_ok=True)
    baseline.to_csv(args.output_dir / "v500_cross_year_baseline_trades.csv", index=False, encoding="utf-8")
    summaries = {
        "v500_cross_year_baseline_segments.csv": summarize_by(baseline, ["TrainingSegment"]),
        "v500_cross_year_baseline_months.csv": summarize_by(baseline, ["TrainingSegment", "Month"]),
        "v500_cross_year_baseline_weeks.csv": summarize_by(baseline, ["TrainingSegment", "WeekStart"]),
        "v500_cross_year_baseline_paths.csv": summarize_by(baseline, ["TrainingSegment", "ResearchPath", "Side"]),
        "v500_cross_year_baseline_exits.csv": summarize_by(baseline, ["TrainingSegment", "ResearchPath", "ExitRole"]),
    }
    for name, report in summaries.items():
        report.to_csv(args.output_dir / name, index=False, encoding="utf-8")
    print(summarize_by(baseline, ["TrainingSegment"]).to_string(index=False))


if __name__ == "__main__":
    main()
