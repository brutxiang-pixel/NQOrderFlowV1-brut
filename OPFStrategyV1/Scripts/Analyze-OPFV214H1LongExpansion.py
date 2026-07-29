import argparse
import importlib.util
import itertools
from pathlib import Path

import numpy as np
import pandas as pd


FAMILIES = {
    "LongFailureRetestQuality": {
        "Side": "Long",
        "ResearchPath": "FailureReverse_RetestFailed",
        "ReasonHead": "SetupQualityTooLow",
        "ExitPolicy": "Fixed3R",
    },
    "LongOcWideLowRR": {
        "Side": "Long",
        "ResearchPath": "ObservationConfirm_WideStop1_5R",
        "ReasonHead": "EstimatedRRTooLow",
        "ExitPolicy": "Fixed3R",
    },
    "LongFailureObservationLowRR": {
        "Side": "Long",
        "ResearchPath": "FailureReverse_ObservationInvalidated",
        "ReasonHead": "EstimatedRRTooLow",
        "ExitPolicy": "ProtectBE1R_Then3R",
    },
}


def load_module(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def load_expansions(engine, evidence: Path, trading_dates: dict) -> pd.DataFrame:
    policies = engine.read_snapshot_csvs(evidence, "exit_policy_evaluations")
    decisions = engine.read_snapshot_csvs(evidence, "execution_decisions")
    policies["EntryTime"] = pd.to_datetime(policies["EntryTime"])
    policies["ExitTime"] = pd.to_datetime(policies["ExitTime"])
    decisions["Time"] = pd.to_datetime(decisions["Time"])
    decisions["ReasonHead"] = decisions["Reason"].fillna("").str.extract(
        r"^([^:|]+)", expand=False
    )
    keys = ["SnapshotID", "SignalID", "ResearchPath"]
    decision_columns = [
        *keys, "Time", "Bar", "Decision", "Reason", "ReasonHead", "Side",
        "SetupQualityScore", "RegimeScore", "InitialRiskPoints", "EstimatedRR",
        "SourceSequence",
    ]
    joined = policies.merge(
        decisions[decision_columns].drop_duplicates(keys),
        on=keys,
        how="inner",
        suffixes=("", "Decision"),
        validate="many_to_one",
    )
    joined["TradingDate"] = joined["SnapshotID"].map(trading_dates)
    joined = joined[joined["TradingDate"].notna()].copy()
    joined = joined[joined["TradingDate"].dt.year.eq(2026)].copy()
    parts = []
    for name, spec in FAMILIES.items():
        selected = joined[
            joined["Side"].eq(spec["Side"])
            & joined["ResearchPath"].eq(spec["ResearchPath"])
            & joined["ReasonHead"].eq(spec["ReasonHead"])
            & joined["ExitPolicy"].eq(spec["ExitPolicy"])
            & joined["Decision"].ne("Execute")
        ].copy()
        selected["ExpansionFamily"] = name
        parts.append(selected)
    output = pd.concat(parts, ignore_index=True)
    output["EventTime"] = output["EntryTime"]
    output["SourceSequence"] = output["SourceSequenceDecision"]
    output["Month"] = output["TradingDate"].dt.to_period("M").astype(str)
    output["ResolvedTradeID"] = ""
    output["ExpectedParentTradeID"] = pd.NA
    output["Classification"] = "Normal"
    output["DirectGross3"] = output["PnLDollars"].astype(float) * 1.5
    output["DirectNet3"] = output["DirectGross3"] - 3.6
    output["DirectExitTime"] = output["ExitTime"]
    output["DirectExitReason"] = output["ExitReason"]
    output["PolicyFilledRisk"] = output["InitialRiskPoints"].astype(float)
    output["PolicyEntryPrice"] = output["Entry"].astype(float)
    output["PolicyStopPrice"] = output["Stop"].astype(float)
    output["OutcomeSource"] = "RichExit:" + output["ExpansionFamily"]
    output["ShadowLiveUntil"] = output["ExitTime"]
    output["RollbackCountOnExit"] = False
    output["CountsNormal"] = True
    output["OccupiesActive"] = True
    output["EconomicTrade"] = True
    output["RecordedActiveObservationBlock"] = False
    output["RecordedFutureActiveBlock"] = False
    output["Ambiguous"] = output["AmbiguousStopAndTargetSameBar"].astype(bool)
    output["IsExpansion"] = True
    return output.sort_values(
        ["TradingDate", "EventTime", "Bar", "SourceSequence"]
    ).drop_duplicates(["SnapshotID", "SignalID", "ResearchPath", "ExpansionFamily"])


def direct_summary(expansions: pd.DataFrame) -> pd.DataFrame:
    work = expansions.copy()
    work["Block"] = pd.cut(
        work["TradingDate"].dt.month, [0, 2, 4, 6], labels=["JanFeb", "MarApr", "MayJun"]
    )
    rows = []
    for family, group in work.groupby("ExpansionFamily", sort=False):
        rows.append({
            "ExpansionFamily": family,
            "Trades": len(group),
            "DirectGross3": round(float(group["DirectGross3"].sum()), 2),
            "DirectMean3": round(float(group["DirectGross3"].mean()), 2),
            "JanFebGross3": round(float(group.loc[group["Block"].eq("JanFeb"), "DirectGross3"].sum()), 2),
            "MarAprGross3": round(float(group.loc[group["Block"].eq("MarApr"), "DirectGross3"].sum()), 2),
            "MayJunGross3": round(float(group.loc[group["Block"].eq("MayJun"), "DirectGross3"].sum()), 2),
        })
    return pd.DataFrame(rows).sort_values("DirectGross3", ascending=False)


def run_combo(enabled, engine, conservative, tape, rich_module, bars, prepared,
              overrides, dates, actual_keys, bias_map, global_bias,
              calibration_error, delay):
    enabled = set(enabled)

    def gate(row):
        is_expansion = bool(getattr(row, "IsExpansion", False))
        if is_expansion:
            return row.ExpansionFamily in enabled
        if row.Side == "Long" and row.ResearchPath == "FailureReverse_RetestFailed":
            return False
        return True

    config = {
        "DailyCap": 18,
        "DailyLoss": 450.0,
        "DeferredEnabled": False,
        "DeferredBars": 18,
        "DeferredTargetR": 3.0,
        "DropLongPaths": {"ObservationStrict_BullFresh_WideStop1_5R"},
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
        "CandidateGate": gate,
    }
    trades, diagnostics = engine.simulate_policy(tape, rich_module, bars, config)
    metrics = engine.policy_metrics(trades, dates)
    lower = conservative.conservative_metrics(
        trades, dates, actual_keys, bias_map, global_bias, calibration_error
    )
    return {"Enabled": ";".join(sorted(enabled)) or "None", "DelayMinutes": delay,
            **metrics, **lower, **diagnostics}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--rich", type=Path, required=True)
    parser.add_argument("--m5-calibration", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    args = parser.parse_args()

    here = Path(__file__).parent
    engine = load_module("opf_v214_long_engine", here / "Analyze-OPFDecisionTapePortfolio.py")
    conservative = load_module(
        "opf_v214_long_conservative", here / "Analyze-OPFDecisionTapeH1ExitCombination.py"
    )
    rich_module = load_module(
        "opf_v214_long_rich", here / "Analyze-OPFV209H1ExecutionCalibratedPortfolio.py"
    )
    coarse = load_module(
        "opf_v214_long_coarse", here / "Analyze-OPFV214H1ProfitCoarseScreen.py"
    )
    exit_module = load_module(
        "opf_v214_long_exit", here / "Analyze-OPFDecisionTapeH1ExitScreen.py"
    )

    raw = engine.load_decision_tape(args.evidence, "H1")
    bars = rich_module.bars_by_trading_date(args.rich)
    baseline = engine.prepare_policy_tape(raw, rich_module, bars)
    baseline["IsExpansion"] = False
    baseline["ExpansionFamily"] = ""
    trading_dates = baseline.groupby("SnapshotID")["TradingDate"].first().to_dict()
    expansions = load_expansions(engine, args.evidence, trading_dates)
    tape = pd.concat([baseline, expansions], ignore_index=True, sort=False).sort_values(
        ["TradingDate", "EventTime", "Bar", "SourceSequence"]
    ).reset_index(drop=True)
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

    rows = []
    names = list(FAMILIES)
    for size in range(len(names) + 1):
        for enabled in itertools.combinations(names, size):
            for delay in (5.0, 10.0):
                rows.append(
                    run_combo(
                        enabled, engine, conservative, tape, rich_module, bars, prepared,
                        overrides, dates, actual_keys, bias_map, global_bias,
                        calibration_error, delay,
                    )
                )
    raw_results = pd.DataFrame(rows)
    robust_rows = []
    for enabled, group in raw_results.groupby("Enabled", sort=False):
        worst = group.sort_values("ConservativeGrossLower").iloc[0]
        robust_rows.append({
            "Enabled": enabled,
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
    direct_summary(expansions).to_csv(args.output_dir / "long_expansion_direct_summary.csv", index=False)
    raw_results.to_csv(args.output_dir / "long_expansion_raw.csv", index=False)
    robust.to_csv(args.output_dir / "long_expansion_robust.csv", index=False)
    print(direct_summary(expansions).to_string(index=False))
    print()
    print(robust.to_string(index=False))


if __name__ == "__main__":
    main()
