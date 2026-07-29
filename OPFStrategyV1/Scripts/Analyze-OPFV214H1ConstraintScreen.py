import argparse
import importlib.util
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


def run_config(engine, conservative, tape, rich_module, bars, prepared, overrides,
               dates, actual_keys, bias_map, global_bias, calibration_error,
               cap: int, loss: float, delay: float):
    config = {
        "DailyCap": cap,
        "DailyLoss": loss,
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
        "ActiveReleaseDelayMinutes": delay,
        "PreparedDays": prepared,
        "OutcomeOverrides": overrides,
    }
    trades, diagnostics = engine.simulate_policy(tape, rich_module, bars, config)
    metrics = engine.policy_metrics(trades, dates)
    lower = conservative.conservative_metrics(
        trades, dates, actual_keys, bias_map, global_bias, calibration_error
    )
    return {
        "DailyCap": cap,
        "DailyLoss": loss,
        "DelayMinutes": delay,
        **metrics,
        **lower,
        "JanFebGross": segment(trades, ("2026-01", "2026-02")),
        "MarAprGross": segment(trades, ("2026-03", "2026-04")),
        "MayJunGross": segment(trades, ("2026-05", "2026-06")),
        **diagnostics,
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--rich", type=Path, required=True)
    parser.add_argument("--m5-calibration", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    args = parser.parse_args()

    here = Path(__file__).parent
    engine = load_module("opf_v214_constraint_engine", here / "Analyze-OPFDecisionTapePortfolio.py")
    conservative = load_module(
        "opf_v214_constraint_conservative", here / "Analyze-OPFDecisionTapeH1ExitCombination.py"
    )
    rich_module = load_module(
        "opf_v214_constraint_rich", here / "Analyze-OPFV209H1ExecutionCalibratedPortfolio.py"
    )
    coarse = load_module(
        "opf_v214_constraint_coarse", here / "Analyze-OPFV214H1ProfitCoarseScreen.py"
    )
    exit_module = load_module(
        "opf_v214_constraint_exit", here / "Analyze-OPFDecisionTapeH1ExitScreen.py"
    )

    raw = engine.load_decision_tape(args.evidence, "H1")
    bars = rich_module.bars_by_trading_date(args.rich)
    tape = engine.prepare_policy_tape(raw, rich_module, bars)
    overrides = coarse.current_policy_overrides(tape, bars, rich_module, exit_module)
    actual_keys, bias_map, global_bias = conservative.build_conservative_inputs(raw)
    calibration = pd.read_csv(args.m5_calibration)
    calibration_error = max(
        0.0,
        float(calibration["Bias"].mean())
        + 1.645 * float(calibration["Bias"].std()) / np.sqrt(len(calibration)),
    )
    dates = sorted(tape["TradingDate"].unique())
    prepared = engine.prepare_simulation_days(tape, rich_module, bars)

    first_pass = []
    for cap in (10, 12, 15, 18, 21, 999):
        for loss in (300.0, 450.0, 600.0, 750.0):
            first_pass.append(
                run_config(
                    engine, conservative, tape, rich_module, bars, prepared, overrides,
                    dates, actual_keys, bias_map, global_bias, calibration_error,
                    cap, loss, 5.0,
                )
            )
    five = pd.DataFrame(first_pass).sort_values(
        ["ConservativeGrossLower", "ConservativePF"], ascending=False
    )
    top_keys = list(zip(five.head(8)["DailyCap"], five.head(8)["DailyLoss"]))
    second_pass = [
        run_config(
            engine, conservative, tape, rich_module, bars, prepared, overrides,
            dates, actual_keys, bias_map, global_bias, calibration_error,
            int(cap), float(loss), 10.0,
        )
        for cap, loss in top_keys
    ]
    ten = pd.DataFrame(second_pass)

    robust_rows = []
    for cap, loss in top_keys:
        left = five[(five["DailyCap"].eq(cap)) & (five["DailyLoss"].eq(loss))].iloc[0]
        right = ten[(ten["DailyCap"].eq(cap)) & (ten["DailyLoss"].eq(loss))].iloc[0]
        worst = left if left["ConservativeGrossLower"] <= right["ConservativeGrossLower"] else right
        robust_rows.append({
            "DailyCap": int(cap),
            "DailyLoss": float(loss),
            "ConservativeGross5m": left["ConservativeGrossLower"],
            "ConservativeGross10m": right["ConservativeGrossLower"],
            "RobustConservativeGross": worst["ConservativeGrossLower"],
            "RobustPF": worst["ConservativePF"],
            "RobustPositiveWeekPct": worst["ConservativePositiveWeekPct"],
            "RobustMaxDD": worst["ConservativeMaxDD"],
            "RobustWorstWeek": worst["ConservativeWorstWeek"],
            "RobustTrades": int(worst["NormalTrades"]),
            "RobustJanFebGross": worst["ConservativeJanFebGross"],
            "RobustMarAprGross": worst["ConservativeMarAprGross"],
            "RobustMayJunGross": worst["ConservativeMayJunGross"],
            "TargetMet": (
                worst["ConservativeGrossLower"] >= 24000
                and worst["ConservativePF"] >= 1.35
                and worst["ConservativePositiveWeekPct"] >= 75
                and min(worst["ConservativeJanFebGross"], worst["ConservativeMarAprGross"],
                        worst["ConservativeMayJunGross"]) > 0
            ),
        })
    robust = pd.DataFrame(robust_rows).sort_values(
        ["TargetMet", "RobustConservativeGross", "RobustPF"],
        ascending=[False, False, False],
    )
    args.output_dir.mkdir(parents=True, exist_ok=True)
    five.to_csv(args.output_dir / "constraint_grid_5m.csv", index=False)
    ten.to_csv(args.output_dir / "constraint_grid_10m_top8.csv", index=False)
    robust.to_csv(args.output_dir / "constraint_grid_robust_top8.csv", index=False)
    print(robust.to_string(index=False))


if __name__ == "__main__":
    main()
