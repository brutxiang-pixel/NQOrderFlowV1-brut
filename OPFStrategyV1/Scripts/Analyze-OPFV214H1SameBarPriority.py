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


def run_priority(name, sort_key, engine, conservative, tape, rich_module, bars,
                 prepared, overrides, dates, actual_keys, bias_map, global_bias,
                 calibration_error, delay):
    config = {
        "DailyCap": 18,
        "DailyLoss": 450.0,
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
        "CandidateSortKey": sort_key,
    }
    trades, _ = engine.simulate_policy(tape, rich_module, bars, config)
    metrics = engine.policy_metrics(trades, dates)
    lower = conservative.conservative_metrics(
        trades, dates, actual_keys, bias_map, global_bias, calibration_error
    )
    return {"Priority": name, "DelayMinutes": delay, **metrics, **lower}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--rich", type=Path, required=True)
    parser.add_argument("--m5-calibration", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    args = parser.parse_args()

    here = Path(__file__).parent
    engine = load_module("opf_v214_priority_engine", here / "Analyze-OPFDecisionTapePortfolio.py")
    conservative = load_module(
        "opf_v214_priority_conservative", here / "Analyze-OPFDecisionTapeH1ExitCombination.py"
    )
    rich_module = load_module(
        "opf_v214_priority_rich", here / "Analyze-OPFV209H1ExecutionCalibratedPortfolio.py"
    )
    strict_module = load_module(
        "opf_v214_priority_strict", here / "Analyze-OPFV208H1StrictEntryTree.py"
    )
    coarse = load_module(
        "opf_v214_priority_coarse", here / "Analyze-OPFV214H1ProfitCoarseScreen.py"
    )
    exit_module = load_module(
        "opf_v214_priority_exit", here / "Analyze-OPFDecisionTapeH1ExitScreen.py"
    )

    raw = engine.load_decision_tape(args.evidence, "H1")
    bars = rich_module.bars_by_trading_date(args.rich)
    tape = engine.prepare_policy_tape(raw, rich_module, bars)
    tape = coarse.add_rich_features(tape, args.rich, strict_module)
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
    priorities = [
        ("Baseline", None),
        ("SetupQuality", lambda row: (row.SetupQualityScore, row.RegimeScore, -row.InitialRiskPoints)),
        ("RegimeScore", lambda row: (row.RegimeScore, row.SetupQualityScore, -row.InitialRiskPoints)),
        ("LowerRisk", lambda row: (-row.InitialRiskPoints, row.SetupQualityScore, row.RegimeScore)),
        ("AlignedTrend", lambda row: (row.RichAlignedScore, row.SetupQualityScore, -row.InitialRiskPoints)),
        ("TrendDelta", lambda row: (row.RichScoreDelta, row.SetupQualityScore, -row.InitialRiskPoints)),
        ("EstimatedRR", lambda row: (row.EstimatedRR, row.SetupQualityScore, row.RegimeScore)),
        ("QualityTrendRisk", lambda row: (
            row.SetupQualityScore, row.RichAlignedScore, row.RegimeScore, -row.InitialRiskPoints
        )),
    ]
    rows = []
    for name, sort_key in priorities:
        for delay in (5.0, 10.0):
            rows.append(
                run_priority(
                    name, sort_key, engine, conservative, tape, rich_module, bars,
                    prepared, overrides, dates, actual_keys, bias_map, global_bias,
                    calibration_error, delay,
                )
            )
    raw_results = pd.DataFrame(rows)
    robust_rows = []
    for name, group in raw_results.groupby("Priority", sort=False):
        worst = group.sort_values("ConservativeGrossLower").iloc[0]
        robust_rows.append({
            "Priority": name,
            "ConservativeGross5m": group.loc[group["DelayMinutes"].eq(5), "ConservativeGrossLower"].iloc[0],
            "ConservativeGross10m": group.loc[group["DelayMinutes"].eq(10), "ConservativeGrossLower"].iloc[0],
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
    raw_results.to_csv(args.output_dir / "same_bar_priority_raw.csv", index=False)
    robust.to_csv(args.output_dir / "same_bar_priority_robust.csv", index=False)
    print(robust.to_string(index=False))


if __name__ == "__main__":
    main()
