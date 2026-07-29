import argparse
import importlib.util
from pathlib import Path

import numpy as np
import pandas as pd


CURRENT_POLICIES = {
    "BreakawayShort4R_BE1_5_36": ("Short", "BreakawayFvg", 4.0, 1.5, 36),
    "ObservationLong4R_NoBE_36": ("Long", "ObservationConfirm", 4.0, 0.0, 36),
    "ObservationWideLong3R_NoBE_12": (
        "Long", "ObservationConfirm_WideStop1_5R", 3.0, 0.0, 12
    ),
    "ObservationStrictOtherLong3R_NoBE_12": (
        "Long", "ObservationStrict_Other", 3.0, 0.0, 12
    ),
    "AlmostLong4R_NoBE_36": ("Long", "AlmostConfirmed", 4.0, 0.0, 36),
    "FailureObservationWideShort4R_NoBE_36": (
        "Short", "FailureReverse_ObservationInvalidated_WideStop1_5R", 4.0, 0.0, 36
    ),
}


def load_module(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def add_rich_features(tape: pd.DataFrame, rich_root: Path, strict_module) -> pd.DataFrame:
    rich = strict_module.load_rich(rich_root)
    output = tape.merge(
        rich,
        left_on=["EventTime", "Bar"],
        right_on=["EntryTime", "EntryBar"],
        how="left",
        validate="many_to_one",
    )
    is_long = output["Side"].eq("Long")
    output["RichAlignedScore"] = np.where(
        is_long, output["RichBullScore"], output["RichBearScore"]
    )
    output["RichOpposingScore"] = np.where(
        is_long, output["RichBearScore"], output["RichBullScore"]
    )
    output["RichScoreDelta"] = output["RichAlignedScore"] - output["RichOpposingScore"]
    output["RichAlignedVwapDistanceAtr"] = np.where(
        is_long, output["RichVwapDistanceAtr"], -output["RichVwapDistanceAtr"]
    )
    output["RichAbsoluteVwapDistanceAtr"] = output["RichVwapDistanceAtr"].abs()
    hour = output["EventTime"].dt.hour
    output["Session"] = pd.cut(
        hour, [-1, 5, 12, 19, 23], labels=["Asia", "Europe", "US", "Late"]
    ).astype(str)
    numeric = [
        "SetupQualityScore", "RegimeScore", "InitialRiskPoints",
        "RichRegimeBars", "RichRelativeVolume20", "RichAlignedScore",
        "RichScoreDelta", "RichAlignedVwapDistanceAtr", "RichAbsoluteVwapDistanceAtr",
    ]
    for column in numeric:
        output[column] = pd.to_numeric(output[column], errors="coerce")
        output[column] = output[column].fillna(output[column].median())
    return output


def current_policy_overrides(tape, bars, rich_module, exit_module):
    overrides = {}
    for name, (side, path, target_r, be_trigger_r, max_bars) in CURRENT_POLICIES.items():
        selected = tape[tape["Side"].eq(side) & tape["ResearchPath"].eq(path)]
        for row in selected.itertuples(index=False):
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
            overrides[(row.SnapshotID, row.SignalID, row.ResearchPath, row.EventTime)] = {
                "Gross3": gross,
                "ExitTime": exit_time,
                "ExitReason": reason,
                "OutcomeSource": f"RichExit:{name}",
            }
    return overrides


def fixed_rules(tape: pd.DataFrame):
    rules = [("Baseline", "Baseline", lambda row: True)]
    families = {
        "SetupQualityMin": [(value, lambda row, v=value: row.SetupQualityScore >= v)
                            for value in (50, 60)],
        "RegimeScoreMin": [(value, lambda row, v=value: row.RegimeScore >= v)
                           for value in (40, 60)],
        "RiskMax": [(value, lambda row, v=value: row.InitialRiskPoints <= v)
                    for value in (20, 25)],
        "AlignedTrendMin": [(value, lambda row, v=value: row.RichAlignedScore >= v)
                            for value in (40, 60)],
        "TrendDeltaMin": [(value, lambda row, v=value: row.RichScoreDelta >= v)
                          for value in (0, 20)],
        "RelativeVolumeMin": [(value, lambda row, v=value: row.RichRelativeVolume20 >= v)
                              for value in (1.0, 1.25)],
        "VwapAbsMax": [(value, lambda row, v=value: row.RichAbsoluteVwapDistanceAtr <= v)
                       for value in (1.0, 1.5)],
        "VwapAlignedMin": [(value, lambda row, v=value: row.RichAlignedVwapDistanceAtr >= v)
                           for value in (0.0,)],
        "RegimeBarsMin": [(value, lambda row, v=value: row.RichRegimeBars >= v)
                          for value in (4, 8)],
    }
    for family, variants in families.items():
        for value, predicate in variants:
            rules.append((f"{family}:{value}", family, predicate))

    return rules


def base_config(engine, tape, rich_module, bars, overrides, delay, gate):
    return {
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
        "ActiveReleaseDelayMinutes": delay,
        "PreparedDays": engine.prepare_simulation_days(tape, rich_module, bars),
        "OutcomeOverrides": overrides,
        "CandidateGate": gate,
    }


def block_gross(trades, months):
    normal = trades[trades["CountsNormal"]]
    return round(float(normal.loc[normal["Month"].isin(months), "Gross3"].sum()), 2)


def evaluate_rule(name, family, gate, engine, tape, rich_module, bars, overrides,
                  dates, actual_keys, bias_map, global_bias, calibration_error,
                  prepared_by_delay):
    results = []
    for delay in (5.0, 10.0):
        config = base_config(engine, tape, rich_module, bars, overrides, delay, gate)
        config["PreparedDays"] = prepared_by_delay[delay]
        trades, diagnostics = engine.simulate_policy(tape, rich_module, bars, config)
        metrics = engine.policy_metrics(trades, dates)
        conservative = conservative_module.conservative_metrics(
            trades, dates, actual_keys, bias_map, global_bias, calibration_error
        )
        results.append({
            "Delay": delay,
            "TradesData": trades,
            **metrics,
            **conservative,
            "JanFebGross": block_gross(trades, ("2026-01", "2026-02")),
            "MarAprGross": block_gross(trades, ("2026-03", "2026-04")),
            "MayJunGross": block_gross(trades, ("2026-05", "2026-06")),
            **diagnostics,
        })
    robust = min(results, key=lambda item: item["ConservativeGrossLower"])
    output = {
        "Rule": name,
        "Family": family,
        "Gross5m": results[0]["NormalGross"],
        "Gross10m": results[1]["NormalGross"],
        "ConservativeGross5m": results[0]["ConservativeGrossLower"],
        "ConservativeGross10m": results[1]["ConservativeGrossLower"],
        "RobustConservativeGross": robust["ConservativeGrossLower"],
        "RobustPF": robust["ConservativePF"],
        "RobustPositiveWeekPct": robust["ConservativePositiveWeekPct"],
        "RobustMaxDD": robust["ConservativeMaxDD"],
        "RobustWorstWeek": robust["ConservativeWorstWeek"],
        "RobustTrades": robust["NormalTrades"],
        "RobustJanFebGross": robust["ConservativeJanFebGross"],
        "RobustMarAprGross": robust["ConservativeMarAprGross"],
        "RobustMayJunGross": robust["ConservativeMayJunGross"],
        "TargetMet": (
            robust["ConservativeGrossLower"] >= 24000
            and robust["ConservativePF"] >= 1.35
            and robust["ConservativePositiveWeekPct"] >= 75
            and min(robust["ConservativeJanFebGross"], robust["ConservativeMarAprGross"],
                    robust["ConservativeMayJunGross"]) > 0
        ),
    }
    return output


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--rich", type=Path, required=True)
    parser.add_argument("--m5-calibration", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--include-pairs", action="store_true")
    parser.add_argument("--pairs-only", action="store_true")
    args = parser.parse_args()

    here = Path(__file__).parent
    engine = load_module("opf_v214_coarse_engine", here / "Analyze-OPFDecisionTapePortfolio.py")
    global conservative_module
    conservative_module = load_module(
        "opf_v214_coarse_conservative", here / "Analyze-OPFDecisionTapeH1ExitCombination.py"
    )
    rich_module = load_module(
        "opf_v214_coarse_rich", here / "Analyze-OPFV209H1ExecutionCalibratedPortfolio.py"
    )
    strict_module = load_module(
        "opf_v214_coarse_strict", here / "Analyze-OPFV208H1StrictEntryTree.py"
    )
    exit_module = load_module(
        "opf_v214_coarse_exit", here / "Analyze-OPFDecisionTapeH1ExitScreen.py"
    )

    raw = engine.load_decision_tape(args.evidence, "H1")
    bars = rich_module.bars_by_trading_date(args.rich)
    tape = engine.prepare_policy_tape(raw, rich_module, bars)
    tape = add_rich_features(tape, args.rich, strict_module)
    overrides = current_policy_overrides(tape, bars, rich_module, exit_module)
    actual_keys, bias_map, global_bias = conservative_module.build_conservative_inputs(raw)
    calibration = pd.read_csv(args.m5_calibration)
    calibration_error = max(
        0.0,
        float(calibration["Bias"].mean())
        + 1.645 * float(calibration["Bias"].std()) / np.sqrt(len(calibration)),
    )
    dates = sorted(tape["TradingDate"].unique())
    prepared = {delay: engine.prepare_simulation_days(tape, rich_module, bars)
                for delay in (5.0, 10.0)}

    single_rules = fixed_rules(tape)
    if args.pairs_only:
        singles = pd.read_csv(args.output_dir / "single_rule_screen.csv")
    else:
        rows = [
            evaluate_rule(name, family, gate, engine, tape, rich_module, bars, overrides,
                          dates, actual_keys, bias_map, global_bias, calibration_error, prepared)
            for name, family, gate in single_rules
        ]
        singles = pd.DataFrame(rows).sort_values(
            ["RobustConservativeGross", "RobustPF"], ascending=False
        )

    top = []
    for family, group in singles[~singles["Family"].eq("Baseline")].groupby("Family"):
        rule_name = group.iloc[0]["Rule"]
        top.append(next(rule for rule in single_rules if rule[0] == rule_name))
    top_by_family = {rule[1]: rule for rule in top}
    allowed_pairs = [
        ("SetupQualityMin", "RiskMax"),
        ("RegimeScoreMin", "RiskMax"),
        ("AlignedTrendMin", "RiskMax"),
        ("TrendDeltaMin", "RiskMax"),
        ("RelativeVolumeMin", "VwapAbsMax"),
        ("RelativeVolumeMin", "VwapAlignedMin"),
        ("RegimeBarsMin", "AlignedTrendMin"),
        ("SetupQualityMin", "AlignedTrendMin"),
        ("RegimeScoreMin", "AlignedTrendMin"),
        ("SetupQualityMin", "RelativeVolumeMin"),
    ]
    pair_rules = []
    for left_family, right_family in allowed_pairs:
        left = top_by_family.get(left_family)
        right = top_by_family.get(right_family)
        if left is None or right is None:
            continue
        pair_rules.append((
            f"{left[0]} & {right[0]}",
            f"{left[1]}+{right[1]}",
            lambda row, a=left[2], b=right[2]: a(row) and b(row),
        ))
    pair_rows = []
    if args.include_pairs or args.pairs_only:
        pair_rows = [
            evaluate_rule(name, family, gate, engine, tape, rich_module, bars, overrides,
                          dates, actual_keys, bias_map, global_bias, calibration_error, prepared)
            for name, family, gate in pair_rules
        ]
    pairs = pd.DataFrame(pair_rows)
    combined = pd.concat([singles, pairs], ignore_index=True).sort_values(
        ["TargetMet", "RobustConservativeGross", "RobustPF"],
        ascending=[False, False, False],
    )
    args.output_dir.mkdir(parents=True, exist_ok=True)
    if not args.pairs_only:
        singles.to_csv(args.output_dir / "single_rule_screen.csv", index=False)
    if not pairs.empty:
        pairs.sort_values(
            ["RobustConservativeGross", "RobustPF"], ascending=False
        ).to_csv(args.output_dir / "pair_rule_screen.csv", index=False)
    combined.to_csv(args.output_dir / "coarse_screen_summary.csv", index=False)
    print(combined.head(30).to_string(index=False))


if __name__ == "__main__":
    main()
