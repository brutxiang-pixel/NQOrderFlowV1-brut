import argparse
import importlib.util
from pathlib import Path

import numpy as np
import pandas as pd


def load_module(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def config(prepared, overrides, delay):
    return {
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
    }


def options():
    values = [("Current", None, None, None)]
    for target in (1.5, 2.0, 2.5, 3.0, 4.0, 5.0):
        for bars in (12, 24, 36):
            values.append((f"F{target:g}_T{bars}", target, 0.0, bars))
    for trigger in (0.75, 1.0, 1.5):
        for target in (2.5, 3.0, 4.0, 5.0):
            for bars in (24, 36):
                values.append((f"BE{trigger:g}_F{target:g}_T{bars}", target, trigger, bars))
    return values


def build_outcomes(tape, bars, rich_module, exit_module, current_overrides,
                   actual_keys, bias_map, global_bias, calibration_error):
    rows = []
    cache = {}
    for row in tape.itertuples(index=False):
        key = (row.SnapshotID, row.SignalID, row.ResearchPath, row.EventTime)
        current = current_overrides.get(key)
        if current is None:
            current = {
                "Gross3": float(row.DirectGross3),
                "ExitTime": row.DirectExitTime,
                "ExitReason": row.DirectExitReason,
                "OutcomeSource": row.OutcomeSource,
            }
        for name, target, trigger, max_bars in options():
            if name == "Current":
                result = current
            else:
                outcome_key = (
                    row.TradingDate, row.EventTime, row.Side,
                    float(row.PolicyEntryPrice), float(row.PolicyStopPrice),
                    target, trigger, max_bars,
                )
                result = cache.get(outcome_key)
                if result is None:
                    gross, exit_time, reason = exit_module.fixed_exit(
                        bars[rich_module.trading_date_key(row.TradingDate)],
                        pd.Timestamp(row.EventTime),
                        float(row.PolicyEntryPrice),
                        float(row.PolicyStopPrice),
                        row.Side, target, trigger, max_bars,
                    )
                    result = {
                        "Gross3": gross,
                        "ExitTime": exit_time,
                        "ExitReason": reason,
                        "OutcomeSource": f"RichExit:Dynamic:{name}",
                    }
                    cache[outcome_key] = result
            reserve = 0.0 if key in actual_keys else bias_map.get(
                (row.Side, row.ResearchPath), global_bias
            )
            if str(result["OutcomeSource"]).startswith("RichExit:"):
                reserve += calibration_error
            rows.append({
                "RowIndex": row.Index,
                "SnapshotID": row.SnapshotID,
                "SignalID": row.SignalID,
                "ResearchPath": row.ResearchPath,
                "EventTime": row.EventTime,
                "Month": row.Month,
                "Side": row.Side,
                "Session": row.Session,
                "RiskBand": row.RiskBand,
                "Option": name,
                "Gross3": float(result["Gross3"]),
                "ExitTime": result["ExitTime"],
                "ExitReason": result["ExitReason"],
                "OutcomeSource": result["OutcomeSource"],
                "ConservativeNet3": float(result["Gross3"]) - reserve - 3.6,
            })
    return pd.DataFrame(rows)


def select_lomo(outcomes, group_columns, min_trades, z_score, margin):
    selections = []
    for month in sorted(outcomes["Month"].unique()):
        train = outcomes[outcomes["Month"].ne(month)]
        stats = train.groupby([*group_columns, "Option"])["ConservativeNet3"].agg(
            ["count", "mean", "std"]
        ).reset_index()
        stats["LowerMean"] = stats["mean"] - z_score * stats["std"].fillna(0) / np.sqrt(stats["count"])
        for group_key, part in stats.groupby(group_columns, sort=False):
            if not isinstance(group_key, tuple):
                group_key = (group_key,)
            current = part[part["Option"].eq("Current")]
            if current.empty or int(current.iloc[0]["count"]) < min_trades:
                chosen = "Current"
                gain = 0.0
            else:
                eligible = part[part["count"].ge(min_trades)].sort_values(
                    ["LowerMean", "Option"], ascending=[False, True]
                )
                best = eligible.iloc[0]
                gain = float(best["LowerMean"] - current.iloc[0]["LowerMean"])
                chosen = best["Option"] if gain >= margin else "Current"
            row = {column: value for column, value in zip(group_columns, group_key)}
            row.update({"TestMonth": month, "ChosenOption": chosen, "LowerMeanGain": gain})
            selections.append(row)
    return pd.DataFrame(selections)


def select_static(outcomes, group_columns, min_trades, z_score, margin):
    stats = outcomes.groupby([*group_columns, "Option"])["ConservativeNet3"].agg(
        ["count", "mean", "std"]
    ).reset_index()
    stats["LowerMean"] = (
        stats["mean"]
        - z_score * stats["std"].fillna(0) / np.sqrt(stats["count"])
    )
    selections = []
    for group_key, part in stats.groupby(group_columns, sort=False):
        if not isinstance(group_key, tuple):
            group_key = (group_key,)
        current = part[part["Option"].eq("Current")]
        if current.empty or int(current.iloc[0]["count"]) < min_trades:
            chosen = "Current"
            gain = 0.0
        else:
            eligible = part[part["count"].ge(min_trades)].sort_values(
                ["LowerMean", "Option"], ascending=[False, True]
            )
            best = eligible.iloc[0]
            gain = float(best["LowerMean"] - current.iloc[0]["LowerMean"])
            chosen = best["Option"] if gain >= margin else "Current"
        row = {column: value for column, value in zip(group_columns, group_key)}
        row.update({"ChosenOption": chosen, "LowerMeanGain": gain})
        selections.append(row)
    return pd.DataFrame(selections)


def make_overrides(tape, outcomes, selections, group_columns, current_overrides):
    choice = selections.set_index(["TestMonth", *group_columns])["ChosenOption"].to_dict()
    chosen_rows = []
    for row in tape.itertuples(index=False):
        group_key = tuple(getattr(row, column) for column in group_columns)
        option = choice.get((row.Month, *group_key), "Current")
        chosen_rows.append((row.Index, option))
    chosen = pd.DataFrame(chosen_rows, columns=["RowIndex", "Option"])
    selected = outcomes.merge(chosen, on=["RowIndex", "Option"], how="inner", validate="one_to_one")
    overrides = dict(current_overrides)
    for row in selected.itertuples(index=False):
        if row.Option == "Current":
            continue
        overrides[(row.SnapshotID, row.SignalID, row.ResearchPath, row.EventTime)] = {
            "Gross3": row.Gross3,
            "ExitTime": row.ExitTime,
            "ExitReason": row.ExitReason,
            "OutcomeSource": row.OutcomeSource,
        }
    return overrides


def make_static_overrides(tape, outcomes, selections, group_columns, current_overrides):
    choice = selections.set_index(group_columns)["ChosenOption"].to_dict()
    chosen_rows = []
    for row in tape.itertuples(index=False):
        group_key = tuple(getattr(row, column) for column in group_columns)
        option = choice.get(group_key, "Current")
        chosen_rows.append((row.Index, option))
    chosen = pd.DataFrame(chosen_rows, columns=["RowIndex", "Option"])
    selected = outcomes.merge(
        chosen, on=["RowIndex", "Option"], how="inner", validate="one_to_one"
    )
    overrides = dict(current_overrides)
    for row in selected.itertuples(index=False):
        if row.Option == "Current":
            continue
        overrides[(row.SnapshotID, row.SignalID, row.ResearchPath, row.EventTime)] = {
            "Gross3": row.Gross3,
            "ExitTime": row.ExitTime,
            "ExitReason": row.ExitReason,
            "OutcomeSource": row.OutcomeSource,
        }
    return overrides


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--rich", type=Path, required=True)
    parser.add_argument("--m5-calibration", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    args = parser.parse_args()

    here = Path(__file__).parent
    engine = load_module("opf_v214_dynamic_engine", here / "Analyze-OPFDecisionTapePortfolio.py")
    conservative = load_module("opf_v214_dynamic_cons", here / "Analyze-OPFDecisionTapeH1ExitCombination.py")
    rich_module = load_module("opf_v214_dynamic_rich", here / "Analyze-OPFV209H1ExecutionCalibratedPortfolio.py")
    coarse = load_module("opf_v214_dynamic_coarse", here / "Analyze-OPFV214H1ProfitCoarseScreen.py")
    exit_module = load_module("opf_v214_dynamic_exit", here / "Analyze-OPFDecisionTapeH1ExitScreen.py")

    raw = engine.load_decision_tape(args.evidence, "H1")
    bars = rich_module.bars_by_trading_date(args.rich)
    tape = engine.prepare_policy_tape(raw, rich_module, bars).reset_index(drop=True)
    tape["Index"] = tape.index
    hour = tape["EventTime"].dt.hour
    tape["Session"] = pd.cut(hour, [-1, 5, 12, 19, 23], labels=["Asia", "Europe", "US", "Late"]).astype(str)
    tape["RiskBand"] = pd.cut(
        tape["PolicyFilledRisk"], [-np.inf, 10, 20, 30, np.inf],
        labels=["R10", "R20", "R30", "RHigh"],
    ).astype(str)
    current = coarse.current_policy_overrides(tape, bars, rich_module, exit_module)
    actual_keys, bias_map, global_bias = conservative.build_conservative_inputs(raw)
    calibration = pd.read_csv(args.m5_calibration)
    calibration_error = max(
        0.0, float(calibration["Bias"].mean())
        + 1.645 * float(calibration["Bias"].std()) / np.sqrt(len(calibration))
    )
    outcomes = build_outcomes(
        tape, bars, rich_module, exit_module, current,
        actual_keys, bias_map, global_bias, calibration_error,
    )
    dates = sorted(tape["TradingDate"].unique())
    prepared = engine.prepare_simulation_days(tape, rich_module, bars)
    rows = []
    audits = []
    groupings = {
        "Path": ["Side", "ResearchPath"],
        "PathSession": ["Side", "ResearchPath", "Session"],
        "PathRisk": ["Side", "ResearchPath", "RiskBand"],
    }
    for grouping, columns in groupings.items():
        for min_trades in (20, 40, 60):
            for z_score in (0.0, 0.5, 1.0, 1.645):
                for margin in (0.0, 1.0, 2.0, 4.0):
                    selections = select_lomo(outcomes, columns, min_trades, z_score, margin)
                    overrides = make_overrides(tape, outcomes, selections, columns, current)
                    name = f"{grouping}_N{min_trades}_Z{z_score:g}_M{margin:g}"
                    changed = int(selections["ChosenOption"].ne("Current").sum())
                    for delay in (5.0, 10.0):
                        trades, diagnostics = engine.simulate_policy(
                            tape, rich_module, bars, config(prepared, overrides, delay)
                        )
                        metrics = engine.policy_metrics(trades, dates)
                        lower = conservative.conservative_metrics(
                            trades, dates, actual_keys, bias_map, global_bias, calibration_error
                        )
                        rows.append({
                            "Name": name, "Grouping": grouping, "MinTrades": min_trades,
                            "ZScore": z_score, "Margin": margin, "ChangedMonthGroups": changed,
                            "DelayMinutes": delay, **metrics, **lower, **diagnostics,
                        })
                    audit = selections.copy()
                    audit["Name"] = name
                    audits.append(audit)

    raw_results = pd.DataFrame(rows)
    robust_rows = []
    for name, group in raw_results.groupby("Name", sort=False):
        worst = group.sort_values("ConservativeGrossLower").iloc[0]
        robust_rows.append({
            "Name": name,
            "Grouping": worst["Grouping"],
            "MinTrades": int(worst["MinTrades"]),
            "ZScore": worst["ZScore"],
            "Margin": worst["Margin"],
            "ChangedMonthGroups": int(worst["ChangedMonthGroups"]),
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
        })
    robust = pd.DataFrame(robust_rows)
    robust["TargetMet"] = (
        robust["RobustConservativeGross"].ge(24000)
        & robust["RobustPF"].ge(1.35)
        & robust["RobustPositiveWeekPct"].ge(75)
        & robust[["RobustJanFebGross", "RobustMarAprGross", "RobustMayJunGross"]].min(axis=1).gt(0)
    )
    robust.sort_values(["TargetMet", "RobustConservativeGross", "RobustPF"], ascending=[False, False, False], inplace=True)
    args.output_dir.mkdir(parents=True, exist_ok=True)
    outcomes.to_csv(args.output_dir / "dynamic_exit_outcomes.csv", index=False)
    raw_results.to_csv(args.output_dir / "dynamic_exit_raw.csv", index=False)
    robust.to_csv(args.output_dir / "dynamic_exit_robust.csv", index=False)
    pd.concat(audits, ignore_index=True).to_csv(args.output_dir / "dynamic_exit_selection_audit.csv", index=False)
    print(robust.head(30).to_string(index=False))


if __name__ == "__main__":
    main()
