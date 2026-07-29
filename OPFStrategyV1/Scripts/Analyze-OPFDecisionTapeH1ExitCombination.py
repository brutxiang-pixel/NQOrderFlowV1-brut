import argparse
import importlib.util
import itertools
from pathlib import Path

import numpy as np
import pandas as pd


def load_module(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def segment(trades: pd.DataFrame, months: tuple[str, str]) -> float:
    normal = trades[trades["CountsNormal"]]
    return round(float(normal.loc[normal["Month"].isin(months), "Gross3"].sum()), 2)


def build_conservative_inputs(raw: pd.DataFrame):
    matched = raw[
        raw["Decision"].eq("Execute")
        & raw["Classification"].eq("Normal")
        & raw["AccountGross"].notna()
    ].copy()
    matched["Bias3"] = matched["GrossDollars"].astype(float) * 1.5 - matched[
        "AccountGross"
    ].astype(float) * 1.5
    global_upper = max(
        0.0,
        float(matched["Bias3"].mean())
        + 1.645 * float(matched["Bias3"].std()) / np.sqrt(len(matched)),
    )
    grouped = matched.groupby(["Side", "ResearchPath"])["Bias3"].agg(
        ["count", "mean", "std"]
    )
    grouped["UpperBias3"] = np.where(
        grouped["count"].ge(20),
        (
            grouped["mean"]
            + 1.645 * grouped["std"].fillna(0) / np.sqrt(grouped["count"])
        ).clip(lower=0),
        global_upper,
    )
    actual_keys = set(
        zip(
            matched["SnapshotID"],
            matched["SignalID"],
            matched["ResearchPath"],
            matched["EventTime"],
        )
    )
    return actual_keys, grouped["UpperBias3"].to_dict(), global_upper


def conservative_metrics(
    trades: pd.DataFrame,
    dates: list,
    actual_keys: set,
    bias_map: dict,
    global_bias: float,
    calibration_per_rich_exit: float,
) -> dict:
    work = trades.copy()
    work["CandidateKey"] = list(
        zip(work["SnapshotID"], work["SignalID"], work["ResearchPath"], work["EntryTime"])
    )
    work["ExecutionRiskReserve"] = 0.0
    synthetic_normal = work["CountsNormal"] & ~work["CandidateKey"].isin(actual_keys)
    work.loc[synthetic_normal, "ExecutionRiskReserve"] = [
        bias_map.get((side, path), global_bias)
        for side, path in zip(
            work.loc[synthetic_normal, "Side"],
            work.loc[synthetic_normal, "ResearchPath"],
        )
    ]
    work["CalibrationErrorReserve"] = 0.0
    rich_exit = work["CountsNormal"] & work["OutcomeSource"].str.startswith("RichExit:")
    work.loc[rich_exit, "CalibrationErrorReserve"] = calibration_per_rich_exit
    work["ConservativeGross3"] = (
        work["Gross3"]
        - work["ExecutionRiskReserve"]
        - work["CalibrationErrorReserve"]
    )
    work["ConservativeNet3"] = work["Net3"]
    work.loc[work["CountsNormal"], "ConservativeNet3"] = (
        work.loc[work["CountsNormal"], "ConservativeGross3"] - 3.6
    )
    normal = work[work["CountsNormal"]]
    positive = work.loc[work["ConservativeNet3"] > 0, "ConservativeNet3"].sum()
    negative = -work.loc[work["ConservativeNet3"] < 0, "ConservativeNet3"].sum()
    date_index = pd.to_datetime(pd.Series(dates))
    all_weeks = sorted(
        set(
            date_index.dt.isocalendar().year.astype(str)
            + "-W"
            + date_index.dt.isocalendar().week.astype(str).str.zfill(2)
        )
    )
    iso = pd.to_datetime(work["TradingDate"]).dt.isocalendar()
    work["Week"] = iso.year.astype(str) + "-W" + iso.week.astype(str).str.zfill(2)
    weekly = work.groupby("Week")["ConservativeNet3"].sum().reindex(all_weeks, fill_value=0)
    daily = (
        work.groupby("TradingDate")["ConservativeNet3"]
        .sum()
        .reindex(dates, fill_value=0)
    )
    cumulative = daily.cumsum()
    return {
        "ExecutionRiskReserve": round(float(work["ExecutionRiskReserve"].sum()), 2),
        "CalibrationErrorReserve": round(float(work["CalibrationErrorReserve"].sum()), 2),
        "ConservativeGrossLower": round(float(normal["ConservativeGross3"].sum()), 2),
        "ConservativePF": round(float(positive / negative), 4) if negative else float("inf"),
        "ConservativeMaxDD": round(float((cumulative.cummax() - cumulative).max()), 2),
        "ConservativePositiveWeekPct": round(float((weekly > 0).mean() * 100), 2),
        "ConservativeWorstWeek": round(float(weekly.min()), 2),
        "ConservativeJanFebGross": round(
            float(normal.loc[normal["Month"].isin(["2026-01", "2026-02"]), "ConservativeGross3"].sum()), 2
        ),
        "ConservativeMarAprGross": round(
            float(normal.loc[normal["Month"].isin(["2026-03", "2026-04"]), "ConservativeGross3"].sum()), 2
        ),
        "ConservativeMayJunGross": round(
            float(normal.loc[normal["Month"].isin(["2026-05", "2026-06"]), "ConservativeGross3"].sum()), 2
        ),
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--rich", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--m5-calibration", type=Path, required=True)
    parser.add_argument("--active-release-delay-minutes", type=float, default=0.0)
    args = parser.parse_args()

    engine = load_module(
        "opf_decision_tape_engine_exit_combo",
        Path(__file__).with_name("Analyze-OPFDecisionTapePortfolio.py"),
    )
    rich_module = load_module(
        "opf_v209_execution_calibrated_exit_combo",
        Path(__file__).with_name("Analyze-OPFV209H1ExecutionCalibratedPortfolio.py"),
    )
    exit_module = load_module(
        "opf_exit_screen_helpers",
        Path(__file__).with_name("Analyze-OPFDecisionTapeH1ExitScreen.py"),
    )
    raw = engine.load_decision_tape(args.evidence, "H1")
    bars = rich_module.bars_by_trading_date(args.rich)
    base_tape = engine.prepare_policy_tape(raw, rich_module, bars)
    actual_keys, bias_map, global_bias = build_conservative_inputs(raw)
    calibration = pd.read_csv(args.m5_calibration)
    calibration_per_rich_exit = max(
        0.0,
        float(calibration["Bias"].mean())
        + 1.645 * float(calibration["Bias"].std()) / np.sqrt(len(calibration)),
    )
    policies = {
        "BreakawayShort4R_BE1_5_36": ("Short", "BreakawayFvg", 4.0, 1.5, 36),
        "BreakawayShort4R_BE1_0_36": ("Short", "BreakawayFvg", 4.0, 1.0, 36),
        "BreakawayShort4R_NoBE_36": ("Short", "BreakawayFvg", 4.0, 0.0, 36),
        "ObservationLong4R_NoBE_36": ("Long", "ObservationConfirm", 4.0, 0.0, 36),
        "ObservationWideLong3R_NoBE_12": ("Long", "ObservationConfirm_WideStop1_5R", 3.0, 0.0, 12),
        "ObservationStrictOtherLong3R_NoBE_12": ("Long", "ObservationStrict_Other", 3.0, 0.0, 12),
        "AlmostLong4R_NoBE_36": ("Long", "AlmostConfirmed", 4.0, 0.0, 36),
        "FailureObservationWideShort4R_NoBE_36": (
            "Short",
            "FailureReverse_ObservationInvalidated_WideStop1_5R",
            4.0,
            0.0,
            36,
        ),
    }
    alternatives = {}
    for name, (side, path, target_r, be_trigger_r, max_bars) in policies.items():
        values = {}
        selected = base_tape[base_tape["Side"].eq(side) & base_tape["ResearchPath"].eq(path)]
        for index, row in selected.iterrows():
            gross, exit_time, reason = exit_module.fixed_exit(
                bars[rich_module.trading_date_key(row.TradingDate)],
                pd.Timestamp(row.EventTime),
                float(row.PolicyEntryPrice),
                float(row.PolicyStopPrice),
                row.Side,
                target_r,
                be_trigger_r,
                max_bars,
            )
            row_key = (row.SnapshotID, row.SignalID, row.ResearchPath, row.EventTime)
            values[row_key] = {
                "Gross3": gross,
                "ExitTime": exit_time,
                "ExitReason": reason,
                "OutcomeSource": f"RichExit:{name}",
            }
        alternatives[name] = values

    rows = []
    best_trades = None
    best_gross = float("-inf")
    names = list(policies)
    dates = sorted(base_tape["TradingDate"].unique())
    prepared = engine.prepare_simulation_days(base_tape, rich_module, bars)
    for size in range(len(names) + 1):
        for enabled in itertools.combinations(names, size):
            if sum(name.startswith("BreakawayShort") for name in enabled) > 1:
                continue
            overrides = {}
            for name in enabled:
                overrides.update(alternatives[name])
            config = {
                "DailyCap": 10,
                "DailyLoss": 600.0,
                "DeferredEnabled": False,
                "DeferredBars": 18,
                "DeferredTargetR": 3.0,
                "DropLongPaths": {
                    "FailureReverse_RetestFailed",
                    "ObservationStrict_BullFresh_WideStop1_5R",
                },
                "DropShortPaths": {
                    "FailureReverse_ObservationInvalidated",
                    "FailureReverse_RetestFailed_WideStop1_5R",
                    "ShadowCandidate",
                },
                "ExcludedRiskBands": [(13.25, 16.0)],
                "MinSetupQuality": 0.0,
                "MinEstimatedRR": 0.0,
                "BaselineMode": False,
                "ReleaseOnEqual": False,
                "ActiveReleaseDelayMinutes": args.active_release_delay_minutes,
                "PreparedDays": prepared,
                "OutcomeOverrides": overrides,
            }
            trades, diagnostics = engine.simulate_policy(base_tape, rich_module, bars, config)
            result = engine.policy_metrics(trades, dates)
            conservative = conservative_metrics(
                trades,
                dates,
                actual_keys,
                bias_map,
                global_bias,
                calibration_per_rich_exit,
            )
            rows.append(
                {
                    "Enabled": ";".join(enabled) or "None",
                    "EnabledCount": len(enabled),
                    **result,
                    "JanFebGross": segment(trades, ("2026-01", "2026-02")),
                    "MarAprGross": segment(trades, ("2026-03", "2026-04")),
                    "MayJunGross": segment(trades, ("2026-05", "2026-06")),
                    **conservative,
                    **diagnostics,
                }
            )
            if conservative["ConservativeGrossLower"] > best_gross:
                best_gross = conservative["ConservativeGrossLower"]
                best_trades = trades.copy()
    summary = pd.DataFrame(rows).sort_values(
        ["ConservativeGrossLower", "ConservativePF"], ascending=False
    )
    args.output_dir.mkdir(parents=True, exist_ok=True)
    summary.to_csv(args.output_dir / "exit_combination_summary.csv", index=False)
    best_trades.to_csv(args.output_dir / "exit_combination_best_trades.csv", index=False)
    print(summary.head(32).to_string(index=False))


if __name__ == "__main__":
    main()
