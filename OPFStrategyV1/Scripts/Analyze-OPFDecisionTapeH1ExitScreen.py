import argparse
import importlib.util
import itertools
from pathlib import Path

import pandas as pd


POINT_VALUE = 2.0
QUANTITY = 3


def load_module(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def fixed_exit(
    bars: pd.DataFrame,
    entry_time: pd.Timestamp,
    entry: float,
    stop: float,
    side: str,
    target_r: float,
    be_trigger_r: float,
    max_bars: int,
) -> tuple[float, pd.Timestamp, str]:
    direction = 1 if side == "Long" else -1
    risk = abs(entry - stop)
    target = entry + direction * risk * target_r
    active_stop = stop
    if not bars.empty and entry_time < bars["Time"].min():
        return -risk * POINT_VALUE * QUANTITY, entry_time, "CoverageGapStop"
    scan = bars[bars["Time"].ge(entry_time)].head(max_bars)
    if scan.empty:
        return 0.0, entry_time, "NoBars"
    for bar in scan.itertuples(index=False):
        stop_hit = bar.Low <= active_stop if direction == 1 else bar.High >= active_stop
        target_hit = bar.High >= target if direction == 1 else bar.Low <= target
        if stop_hit:
            return (
                direction * (active_stop - entry) * POINT_VALUE * QUANTITY,
                bar.Time,
                "StopCollision" if target_hit else ("ProtectBE" if active_stop == entry else "Stop"),
            )
        if target_hit:
            return risk * target_r * POINT_VALUE * QUANTITY, bar.Time, "Target"
        if be_trigger_r > 0:
            trigger = entry + direction * risk * be_trigger_r
            reached = bar.High >= trigger if direction == 1 else bar.Low <= trigger
            if reached:
                active_stop = entry
    last = scan.iloc[-1]
    gross = direction * (float(last["Close"]) - entry) * POINT_VALUE * QUANTITY
    return gross, last["Time"], "TimeStop"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--rich", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    args = parser.parse_args()

    engine = load_module(
        "opf_decision_tape_engine_exit_screen",
        Path(__file__).with_name("Analyze-OPFDecisionTapePortfolio.py"),
    )
    rich_module = load_module(
        "opf_v209_execution_calibrated_exit_screen",
        Path(__file__).with_name("Analyze-OPFV209H1ExecutionCalibratedPortfolio.py"),
    )
    raw = engine.load_decision_tape(args.evidence, "H1")
    bars = rich_module.bars_by_trading_date(args.rich)
    tape = engine.prepare_policy_tape(raw, rich_module, bars)
    prepared = engine.prepare_simulation_days(tape, rich_module, bars)
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
        "PreparedDays": prepared,
    }
    trades, _ = engine.simulate_policy(tape, rich_module, bars, config)
    normal = trades[trades["CountsNormal"]].copy()
    normal["Week"] = (
        pd.to_datetime(normal["TradingDate"]).dt.isocalendar().year.astype(str)
        + "-W"
        + pd.to_datetime(normal["TradingDate"]).dt.isocalendar().week.astype(str).str.zfill(2)
    )
    base_gross = float(normal["Gross3"].sum())
    groups = (
        normal.groupby(["Side", "ResearchPath"]).size().loc[lambda x: x.ge(20)].index
    )
    outcomes = []
    for row in normal.itertuples(index=False):
        if row.ResearchPath == "ZoneBirthResearch":
            continue
        day_bars = bars[rich_module.trading_date_key(row.TradingDate)]
        for target_r, be_trigger_r, max_bars in itertools.product(
            [1.5, 2.0, 2.5, 3.0, 4.0], [0.0, 1.0, 1.5], [12, 18, 24, 36]
        ):
            gross, exit_time, reason = fixed_exit(
                day_bars,
                pd.Timestamp(row.EntryTime),
                float(row.EntryPrice),
                float(row.StopPrice),
                row.Side,
                target_r,
                be_trigger_r,
                max_bars,
            )
            outcomes.append(
                {
                    "SignalID": row.SignalID,
                    "Side": row.Side,
                    "ResearchPath": row.ResearchPath,
                    "Month": row.Month,
                    "Week": row.Week,
                    "BaseGross": float(row.Gross3),
                    "TargetR": target_r,
                    "BETriggerR": be_trigger_r,
                    "MaxBars": max_bars,
                    "AltGross": gross,
                    "AltExitTime": exit_time,
                    "AltExitReason": reason,
                }
            )
    outcome = pd.DataFrame(outcomes)
    rows = []
    for side, path in groups:
        group = outcome[(outcome["Side"].eq(side)) & (outcome["ResearchPath"].eq(path))]
        if group.empty:
            continue
        for (target_r, be_trigger_r, max_bars), policy in group.groupby(
            ["TargetR", "BETriggerR", "MaxBars"]
        ):
            delta = policy["AltGross"] - policy["BaseGross"]
            weekly_delta = policy.groupby("Week").apply(
                lambda x: float((x["AltGross"] - x["BaseGross"]).sum()),
                include_groups=False,
            )
            rows.append(
                {
                    "Side": side,
                    "ResearchPath": path,
                    "Trades": len(policy),
                    "TargetR": target_r,
                    "BETriggerR": be_trigger_r,
                    "MaxBars": max_bars,
                    "DirectDelta": round(float(delta.sum()), 2),
                    "ProjectedGross": round(base_gross + float(delta.sum()), 2),
                    "JanFebDelta": round(float(delta[policy["Month"].isin(["2026-01", "2026-02"])].sum()), 2),
                    "MarAprDelta": round(float(delta[policy["Month"].isin(["2026-03", "2026-04"])].sum()), 2),
                    "MayJunDelta": round(float(delta[policy["Month"].isin(["2026-05", "2026-06"])].sum()), 2),
                    "PositiveDeltaWeeksPct": round(float((weekly_delta > 0).mean() * 100), 2),
                    "WorstWeekDelta": round(float(weekly_delta.min()), 2),
                }
            )
    summary = pd.DataFrame(rows).sort_values("DirectDelta", ascending=False)
    args.output_dir.mkdir(parents=True, exist_ok=True)
    summary.to_csv(args.output_dir / "exit_policy_direct_screen.csv", index=False)
    normal.to_csv(args.output_dir / "best_path_baseline_trades.csv", index=False)
    print(summary.head(40).to_string(index=False))


if __name__ == "__main__":
    main()
