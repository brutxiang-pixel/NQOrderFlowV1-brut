import argparse
import importlib.util
from pathlib import Path

import numpy as np
import pandas as pd


LOMO_POLICIES = {}


def load_module(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def base_config(prepared, overrides, delay):
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


def secondary_allowed(row, primary, policy):
    if policy.startswith("Same+"):
        if primary is None or row.Side != primary["Side"]:
            return False
        policy = policy.split("+", 1)[1]
    is_expansion = bool(getattr(row, "IsExpansion", False))
    if policy.startswith("ExpansionOnly:"):
        enabled = set(policy.split(":", 1)[1].split("+"))
        return is_expansion and row.ExpansionFamily in enabled
    if is_expansion:
        return False
    if policy == "StaticPathRiskSameG2OppG0":
        threshold = 2.0 if primary is not None and row.Side == primary["Side"] else 0.0
        return float(row.StaticPathRiskA50) >= threshold
    if policy in LOMO_POLICIES:
        column, threshold = LOMO_POLICIES[policy]
        return float(getattr(row, column)) >= threshold
    if policy == "All":
        return True
    if policy == "Opposite":
        return primary is not None and row.Side != primary["Side"]
    if policy == "LongOnly":
        return row.Side == "Long"
    if policy == "ShortOnly":
        return row.Side == "Short"
    if policy == "SQ50":
        return float(row.SetupQualityScore) >= 50
    if policy == "SQ60":
        return float(row.SetupQualityScore) >= 60
    if policy == "Regime40":
        return float(row.RegimeScore) >= 40
    if policy == "Risk20":
        return float(row.PolicyFilledRisk) <= 20
    if policy == "Risk25":
        return float(row.PolicyFilledRisk) <= 25
    if policy == "RR2":
        return float(row.EstimatedRR) >= 2.0
    raise ValueError(f"Unknown secondary policy: {policy}")


def add_lomo_scores(tape, config, actual_keys, bias_map, global_bias,
                    calibration_error):
    work = tape.copy()
    values = []
    for row in work.itertuples(index=False):
        gross3, _, _, source = outcome(row, config)
        key = (row.SnapshotID, row.SignalID, row.ResearchPath, row.EventTime)
        reserve3 = 0.0 if key in actual_keys else bias_map.get(
            (row.Side, row.ResearchPath), global_bias
        )
        if str(source).startswith("RichExit:"):
            reserve3 += calibration_error
        values.append((gross3 - reserve3) / 3.0 - 1.2)
    work["ConservativeNet1"] = values
    hour = work["EventTime"].dt.hour
    work["Session"] = pd.cut(
        hour, [-1, 5, 12, 19, 23], labels=["Asia", "Europe", "US", "Late"]
    ).astype(str)
    work["QualityBand"] = pd.cut(
        work["SetupQualityScore"], [-np.inf, 39.999, 49.999, 59.999, np.inf],
        labels=["Q0", "Q40", "Q50", "Q60"],
    ).astype(str)
    work["RiskBand"] = pd.cut(
        work["PolicyFilledRisk"], [-np.inf, 10, 20, 30, np.inf],
        labels=["R10", "R20", "R30", "RHigh"],
    ).astype(str)
    specifications = {
        "Path": ["Side", "ResearchPath"],
        "PathSession": ["Side", "ResearchPath", "Session"],
        "PathQuality": ["Side", "ResearchPath", "QualityBand"],
        "PathRisk": ["Side", "ResearchPath", "RiskBand"],
    }
    months = sorted(work["Month"].unique())
    for name, columns in specifications.items():
        for alpha in (10, 20, 50):
            score_column = f"Lomo{name}A{alpha}"
            work[score_column] = np.nan
            for month in months:
                train = work[work["Month"].ne(month)]
                test = work[work["Month"].eq(month)]
                global_mean = float(train["ConservativeNet1"].mean())
                grouped = train.groupby(columns)["ConservativeNet1"].agg(["sum", "count"])
                grouped["Score"] = (
                    grouped["sum"] + alpha * global_mean
                ) / (grouped["count"] + alpha)
                score_map = grouped["Score"].to_dict()
                keys = list(zip(*(test[column] for column in columns)))
                work.loc[test.index, score_column] = [
                    score_map.get(key, global_mean) for key in keys
                ]
            for threshold in (0.0, 1.0, 2.0, 4.0, 6.0):
                policy = f"{name}A{alpha}G{str(threshold).replace('.', '_')}"
                LOMO_POLICIES[policy] = (score_column, threshold)
    static_columns = specifications["PathRisk"]
    static_global_mean = float(work["ConservativeNet1"].mean())
    static_grouped = work.groupby(static_columns)["ConservativeNet1"].agg(["sum", "count"])
    static_grouped["Score"] = (
        static_grouped["sum"] + 50 * static_global_mean
    ) / (static_grouped["count"] + 50)
    static_score_map = static_grouped["Score"].to_dict()
    static_keys = list(zip(*(work[column] for column in static_columns)))
    work["StaticPathRiskA50"] = [
        static_score_map.get(key, static_global_mean) for key in static_keys
    ]
    for threshold in (0.0, 1.0, 2.0, 4.0, 6.0):
        policy = f"StaticPathRiskA50G{str(threshold).replace('.', '_')}"
        LOMO_POLICIES[policy] = ("StaticPathRiskA50", threshold)
    return work


def outcome(row, config):
    override = config["OutcomeOverrides"].get(
        (row.SnapshotID, row.SignalID, row.ResearchPath, row.EventTime)
    )
    if override is None:
        return (
            float(row.DirectGross3),
            row.DirectExitTime,
            row.DirectExitReason,
            row.OutcomeSource,
        )
    return (
        float(override["Gross3"]),
        override["ExitTime"],
        override["ExitReason"],
        override["OutcomeSource"],
    )


def simulate_concurrent(engine, prepared, config, primary_quantity, secondary_quantity,
                        secondary_policy, risk_cap):
    accepted = []
    diagnostics = {
        "PrimaryAccepted": 0,
        "SecondaryAccepted": 0,
        "ActiveBlocked": 0,
        "SecondaryGateBlocked": 0,
        "RiskBlocked": 0,
        "StaticBlocked": 0,
        "CapBlocked": 0,
        "LossBlocked": 0,
        "DailyLossTriggeredDays": 0,
        "SameDirectionOverlap": 0,
        "PeakConcurrentRisk": 0.0,
        "PeakConcurrentContracts": 0,
        "PrimaryIneligible": 0,
    }
    for snapshot_id, _, clock, candidates_by_time in prepared:
        active = {"Primary": None, "Secondary": None}
        normal_count = 0
        realized = 0.0
        loss_triggered = False

        for time in clock:
            for lane in ("Primary", "Secondary"):
                trade = active[lane]
                if trade is not None and not trade["PnLRealized"] and trade["ExitTime"] < time:
                    realized += trade["Net3"]
                    trade["PnLRealized"] = True
                if trade is not None and trade["ActiveUntil"] < time:
                    if not trade["PnLRealized"]:
                        realized += trade["Net3"]
                    active[lane] = None

            candidates = candidates_by_time.get(time)
            if candidates is None:
                continue
            for row in candidates:
                is_expansion = bool(getattr(row, "IsExpansion", False))
                if not is_expansion and not engine.policy_allowed(row, config):
                    diagnostics["StaticBlocked"] += 1
                    continue
                if normal_count >= config["DailyCap"]:
                    diagnostics["CapBlocked"] += 1
                    continue
                if realized <= -config["DailyLoss"]:
                    diagnostics["LossBlocked"] += 1
                    loss_triggered = True
                    continue

                if active["Primary"] is None and is_expansion:
                    diagnostics["PrimaryIneligible"] += 1
                    continue
                if active["Primary"] is None:
                    lane = "Primary"
                    quantity = primary_quantity
                elif secondary_quantity > 0 and active["Secondary"] is None:
                    if not secondary_allowed(row, active["Primary"], secondary_policy):
                        diagnostics["SecondaryGateBlocked"] += 1
                        continue
                    lane = "Secondary"
                    quantity = secondary_quantity
                else:
                    diagnostics["ActiveBlocked"] += 1
                    continue

                filled_risk = float(row.PolicyFilledRisk)
                candidate_risk = filled_risk * 2.0 * quantity
                concurrent_risk = candidate_risk + sum(
                    item["RiskDollars"] for item in active.values() if item is not None
                )
                if concurrent_risk > risk_cap:
                    diagnostics["RiskBlocked"] += 1
                    continue

                gross3, exit_time, exit_reason, source = outcome(row, config)
                gross = gross3 * quantity / 3.0
                record = {
                    "SnapshotID": snapshot_id,
                    "TradingDate": row.TradingDate,
                    "Month": row.Month,
                    "SignalID": row.SignalID,
                    "TradeID": row.ResolvedTradeID
                    if pd.notna(row.ResolvedTradeID) and str(row.ResolvedTradeID)
                    else f"{lane}|{row.SignalID}|{row.ResearchPath}",
                    "EntryTime": time,
                    "ExitTime": exit_time,
                    "ActiveUntil": engine.policy_active_until(exit_time, config),
                    "PnLRealized": False,
                    "Side": row.Side,
                    "ResearchPath": row.ResearchPath,
                    "InitialRiskPoints": float(row.InitialRiskPoints),
                    "Ambiguous": bool(row.Ambiguous),
                    "Lane": lane,
                    "Quantity": quantity,
                    "RiskDollars": candidate_risk,
                    "Gross3": gross,
                    "Net3": gross - 1.2 * quantity,
                    "ExitReason": exit_reason,
                    "OutcomeSource": source,
                    "CountsNormal": bool(getattr(row, "CountsNormal", True)),
                    "EconomicTrade": bool(getattr(row, "EconomicTrade", True)),
                }
                accepted.append(record)
                active[lane] = record
                if record["CountsNormal"]:
                    normal_count += 1
                diagnostics[f"{lane}Accepted"] += 1
                diagnostics["PeakConcurrentRisk"] = max(
                    diagnostics["PeakConcurrentRisk"], concurrent_risk
                )
                contracts = sum(
                    item["Quantity"] for item in active.values() if item is not None
                )
                diagnostics["PeakConcurrentContracts"] = max(
                    diagnostics["PeakConcurrentContracts"], contracts
                )
                if (
                    lane == "Secondary"
                    and active["Primary"] is not None
                    and active["Primary"]["Side"] == row.Side
                ):
                    diagnostics["SameDirectionOverlap"] += 1

        for trade in active.values():
            if trade is not None and not trade["PnLRealized"]:
                realized += trade["Net3"]
        if loss_triggered:
            diagnostics["DailyLossTriggeredDays"] += 1

    trades = pd.DataFrame(accepted)
    if diagnostics["SecondaryAccepted"]:
        diagnostics["SameDirectionOverlapPct"] = round(
            diagnostics["SameDirectionOverlap"] / diagnostics["SecondaryAccepted"] * 100, 2
        )
    else:
        diagnostics["SameDirectionOverlapPct"] = 0.0
    return trades, diagnostics


def conservative_metrics(trades, dates, actual_keys, bias_map, global_bias,
                         calibration_error):
    work = trades.copy()
    work["CandidateKey"] = list(zip(
        work["SnapshotID"], work["SignalID"], work["ResearchPath"], work["EntryTime"]
    ))
    scale = work["Quantity"].astype(float) / 3.0
    synthetic = work["CountsNormal"] & ~work["CandidateKey"].isin(actual_keys)
    work["ExecutionRiskReserve"] = 0.0
    work.loc[synthetic, "ExecutionRiskReserve"] = [
        bias_map.get((side, path), global_bias) * factor
        for side, path, factor in zip(
            work.loc[synthetic, "Side"], work.loc[synthetic, "ResearchPath"], scale[synthetic]
        )
    ]
    rich = work["CountsNormal"] & work["OutcomeSource"].str.startswith("RichExit:")
    work["CalibrationErrorReserve"] = 0.0
    work.loc[rich, "CalibrationErrorReserve"] = calibration_error * scale[rich]
    work["ConservativeGross"] = (
        work["Gross3"] - work["ExecutionRiskReserve"] - work["CalibrationErrorReserve"]
    )
    work["ConservativeNet"] = work["ConservativeGross"] - 1.2 * work["Quantity"]
    normal = work[work["CountsNormal"]]
    positive = work.loc[work["ConservativeNet"] > 0, "ConservativeNet"].sum()
    negative = -work.loc[work["ConservativeNet"] < 0, "ConservativeNet"].sum()
    date_index = pd.to_datetime(pd.Series(dates))
    all_weeks = sorted(set(
        date_index.dt.isocalendar().year.astype(str)
        + "-W" + date_index.dt.isocalendar().week.astype(str).str.zfill(2)
    ))
    iso = pd.to_datetime(work["TradingDate"]).dt.isocalendar()
    work["Week"] = iso.year.astype(str) + "-W" + iso.week.astype(str).str.zfill(2)
    weekly = work.groupby("Week")["ConservativeNet"].sum().reindex(all_weeks, fill_value=0)
    daily = work.groupby("TradingDate")["ConservativeNet"].sum().reindex(dates, fill_value=0)
    cumulative = daily.cumsum()
    secondary = work[work["Lane"].eq("Secondary") & work["CountsNormal"]]
    result = {
        "ConservativeGrossLower": round(float(normal["ConservativeGross"].sum()), 2),
        "ConservativePF": round(float(positive / negative), 4) if negative else float("inf"),
        "ConservativePositiveWeekPct": round(float((weekly > 0).mean() * 100), 2),
        "ConservativeMaxDD": round(float((cumulative.cummax() - cumulative).max()), 2),
        "ConservativeWorstWeek": round(float(weekly.min()), 2),
        "ConservativeJanFebGross": round(float(normal.loc[normal["Month"].isin(["2026-01", "2026-02"]), "ConservativeGross"].sum()), 2),
        "ConservativeMarAprGross": round(float(normal.loc[normal["Month"].isin(["2026-03", "2026-04"]), "ConservativeGross"].sum()), 2),
        "ConservativeMayJunGross": round(float(normal.loc[normal["Month"].isin(["2026-05", "2026-06"]), "ConservativeGross"].sum()), 2),
        "SecondaryConservativeGross": round(float(secondary["ConservativeGross"].sum()), 2),
        "SecondaryConservativeNet": round(float(secondary["ConservativeNet"].sum()), 2),
        "ExecutionRiskReserve": round(float(work["ExecutionRiskReserve"].sum()), 2),
        "CalibrationErrorReserve": round(float(work["CalibrationErrorReserve"].sum()), 2),
    }
    result["SecondaryConservativeExpectancy"] = round(
        result["SecondaryConservativeNet"] / len(secondary), 2
    ) if len(secondary) else 0.0
    return result


def run_arm(engine, prepared, config, dates, conservative_inputs, calibration_error,
            arm, primary_quantity, secondary_quantity, policy, risk_cap):
    trades, diagnostics = simulate_concurrent(
        engine, prepared, config, primary_quantity, secondary_quantity, policy, risk_cap
    )
    actual_keys, bias_map, global_bias = conservative_inputs
    lower = conservative_metrics(
        trades, dates, actual_keys, bias_map, global_bias, calibration_error
    )
    return trades, {
        "Arm": arm,
        "PrimaryQuantity": primary_quantity,
        "SecondaryQuantity": secondary_quantity,
        "SecondaryPolicy": policy,
        "RiskCap": risk_cap,
        "DelayMinutes": config["ActiveReleaseDelayMinutes"],
        "NormalTrades": int(trades["CountsNormal"].sum()),
        "ObservedGross": round(float(trades.loc[trades["CountsNormal"], "Gross3"].sum()), 2),
        **lower,
        **diagnostics,
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--rich", type=Path, required=True)
    parser.add_argument("--m5-calibration", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--dynamic-exit", action="store_true")
    parser.add_argument("--static-dynamic-exit", action="store_true")
    parser.add_argument("--secondary-policy")
    parser.add_argument("--arm")
    parser.add_argument("--risk-cap", type=float)
    parser.add_argument("--delay", type=float)
    parser.add_argument("--daily-cap", type=int, default=18)
    parser.add_argument("--daily-loss", type=float, default=450.0)
    parser.add_argument("--actual-zonebirth-split", action="store_true")
    args = parser.parse_args()

    here = Path(__file__).parent
    engine = load_module("opf_v214_concurrency_engine", here / "Analyze-OPFDecisionTapePortfolio.py")
    conservative = load_module(
        "opf_v214_concurrency_conservative", here / "Analyze-OPFDecisionTapeH1ExitCombination.py"
    )
    rich_module = load_module(
        "opf_v214_concurrency_rich", here / "Analyze-OPFV209H1ExecutionCalibratedPortfolio.py"
    )
    coarse = load_module(
        "opf_v214_concurrency_coarse", here / "Analyze-OPFV214H1ProfitCoarseScreen.py"
    )
    long_module = load_module(
        "opf_v214_concurrency_long", here / "Analyze-OPFV214H1LongExpansion.py"
    )
    exit_module = load_module(
        "opf_v214_concurrency_exit", here / "Analyze-OPFDecisionTapeH1ExitScreen.py"
    )
    dynamic_module = load_module(
        "opf_v214_concurrency_dynamic", here / "Analyze-OPFV214H1DynamicExitLomo.py"
    )

    raw = engine.load_decision_tape(args.evidence, "H1")
    bars = rich_module.bars_by_trading_date(args.rich)
    tape = engine.prepare_policy_tape(raw, rich_module, bars)
    tape["IsExpansion"] = False
    tape["ExpansionFamily"] = ""
    trading_dates = tape.groupby("SnapshotID")["TradingDate"].first().to_dict()
    expansions = long_module.load_expansions(engine, args.evidence, trading_dates)
    tape = pd.concat([tape, expansions], ignore_index=True, sort=False).sort_values(
        ["TradingDate", "EventTime", "Bar", "SourceSequence"]
    ).reset_index(drop=True)
    conservative_inputs = conservative.build_conservative_inputs(raw)
    calibration = pd.read_csv(args.m5_calibration)
    calibration_error = max(
        0.0,
        float(calibration["Bias"].mean())
        + 1.645 * float(calibration["Bias"].std()) / np.sqrt(len(calibration)),
    )
    overrides = coarse.current_policy_overrides(tape, bars, rich_module, exit_module)
    if args.dynamic_exit or args.static_dynamic_exit:
        tape["Index"] = tape.index
        hour = tape["EventTime"].dt.hour
        tape["Session"] = pd.cut(
            hour, [-1, 5, 12, 19, 23], labels=["Asia", "Europe", "US", "Late"]
        ).astype(str)
        tape["RiskBand"] = pd.cut(
            tape["PolicyFilledRisk"], [-np.inf, 10, 20, 30, np.inf],
            labels=["R10", "R20", "R30", "RHigh"],
        ).astype(str)
        base_only = tape[~tape["IsExpansion"].astype(bool)].copy()
        dynamic_outcomes = dynamic_module.build_outcomes(
            base_only, bars, rich_module, exit_module, overrides,
            *conservative_inputs, calibration_error,
        )
        group_columns = ["Side", "ResearchPath", "RiskBand"]
        if args.static_dynamic_exit:
            dynamic_selection = dynamic_module.select_static(
                dynamic_outcomes, group_columns, 60, 1.645, 4.0
            )
            overrides = dynamic_module.make_static_overrides(
                base_only, dynamic_outcomes, dynamic_selection, group_columns, overrides
            )
            if args.actual_zonebirth_split:
                zonebirth = base_only[
                    base_only["Side"].eq("Short")
                    & base_only["ResearchPath"].eq("ZoneBirthResearch")
                ]
                for row in zonebirth.itertuples(index=False):
                    overrides.pop(
                        (row.SnapshotID, row.SignalID, row.ResearchPath, row.EventTime),
                        None,
                    )
        else:
            dynamic_selection = dynamic_module.select_lomo(
                dynamic_outcomes, group_columns, 60, 1.645, 4.0
            )
            overrides = dynamic_module.make_overrides(
                base_only, dynamic_outcomes, dynamic_selection, group_columns, overrides
            )
    score_config = base_config(None, overrides, 5.0)
    tape = add_lomo_scores(
        tape, score_config, *conservative_inputs, calibration_error
    )
    dates = sorted(tape["TradingDate"].unique())
    prepared = engine.prepare_simulation_days(tape, rich_module, bars)

    arms = [
        ("A_3x1", 3, 0),
        ("B_2plus1", 2, 1),
        ("C_3plus1", 3, 1),
        ("D_3plus3", 3, 3),
    ]
    policies = [
        "All", "Opposite", "LongOnly", "ShortOnly", "SQ50", "SQ60",
        "Regime40", "Risk20", "Risk25", "RR2", *LOMO_POLICIES,
    ]
    families = list(long_module.FAMILIES)
    for family in families:
        policies.append(f"ExpansionOnly:{family}")
    policies.extend([
        f"ExpansionOnly:{families[0]}+{families[1]}",
        f"ExpansionOnly:{families[0]}+{families[2]}",
        f"ExpansionOnly:{families[1]}+{families[2]}",
        f"ExpansionOnly:{'+'.join(families)}",
    ])
    if args.secondary_policy:
        policies = [args.secondary_policy]
    if args.arm:
        arms = [arm for arm in arms if arm[0] == args.arm]
        if not arms:
            raise RuntimeError(f"Unknown arm: {args.arm}")
    rows = []
    all_trades = []
    delays = (args.delay,) if args.delay is not None else (5.0, 10.0)
    for delay in delays:
        config = base_config(prepared, overrides, delay)
        config["DailyCap"] = args.daily_cap
        config["DailyLoss"] = args.daily_loss
        for arm, primary_quantity, secondary_quantity in arms:
            arm_policies = ["All"] if secondary_quantity == 0 else policies
            for policy in arm_policies:
                risk_caps = [9999.0] if secondary_quantity == 0 else [300.0, 450.0, 600.0, 9999.0]
                if args.risk_cap is not None:
                    risk_caps = [args.risk_cap]
                for risk_cap in risk_caps:
                    trades, result = run_arm(
                        engine, prepared, config, dates, conservative_inputs,
                        calibration_error, arm, primary_quantity, secondary_quantity,
                        policy, risk_cap,
                    )
                    rows.append(result)
                    trades = trades.copy()
                    trades["Arm"] = arm
                    trades["SecondaryPolicy"] = policy
                    trades["RiskCap"] = risk_cap
                    trades["DelayMinutes"] = delay
                    all_trades.append(trades)

    raw_results = pd.DataFrame(rows)
    robust_rows = []
    group_columns = ["Arm", "PrimaryQuantity", "SecondaryQuantity", "SecondaryPolicy", "RiskCap"]
    for key, group in raw_results.groupby(group_columns, sort=False):
        worst = group.sort_values("ConservativeGrossLower").iloc[0]
        row = {column: value for column, value in zip(group_columns, key)}
        row.update({
            "ConservativeGross5m": float(group.loc[group["DelayMinutes"].eq(5), "ConservativeGrossLower"].iloc[0]),
            "ConservativeGross10m": float(group.loc[group["DelayMinutes"].eq(10), "ConservativeGrossLower"].iloc[0]),
            "RobustConservativeGross": worst["ConservativeGrossLower"],
            "RobustPF": worst["ConservativePF"],
            "RobustPositiveWeekPct": worst["ConservativePositiveWeekPct"],
            "RobustMaxDD": worst["ConservativeMaxDD"],
            "RobustWorstWeek": worst["ConservativeWorstWeek"],
            "RobustTrades": int(worst["NormalTrades"]),
            "RobustJanFebGross": worst["ConservativeJanFebGross"],
            "RobustMarAprGross": worst["ConservativeMarAprGross"],
            "RobustMayJunGross": worst["ConservativeMayJunGross"],
            "RobustSecondaryGross": worst["SecondaryConservativeGross"],
            "RobustSecondaryExpectancy": worst["SecondaryConservativeExpectancy"],
            "SecondaryAccepted": int(worst["SecondaryAccepted"]),
            "SameDirectionOverlapPct": worst["SameDirectionOverlapPct"],
            "PeakConcurrentRisk": worst["PeakConcurrentRisk"],
            "DailyLossTriggeredDays": int(worst["DailyLossTriggeredDays"]),
        })
        row["TargetMet"] = (
            row["RobustConservativeGross"] >= 24000
            and row["RobustPF"] >= 1.35
            and row["RobustPositiveWeekPct"] >= 75
            and min(row["RobustJanFebGross"], row["RobustMarAprGross"], row["RobustMayJunGross"]) > 0
        )
        robust_rows.append(row)
    robust = pd.DataFrame(robust_rows).sort_values(
        ["TargetMet", "RobustConservativeGross", "RobustPF"], ascending=[False, False, False]
    )

    baseline_rows = robust[robust["Arm"].eq("A_3x1")]
    if (
        not baseline_rows.empty
        and args.daily_cap == 18
        and args.daily_loss == 450.0
        and not args.actual_zonebirth_split
    ):
        baseline = baseline_rows.iloc[0]
        expected_baseline = 21712.59 if args.dynamic_exit else 19221.73
        if abs(float(baseline["RobustConservativeGross"]) - expected_baseline) > 0.01:
            raise RuntimeError(
                f"A arm failed baseline replication: {baseline['RobustConservativeGross']} != {expected_baseline}"
            )

    args.output_dir.mkdir(parents=True, exist_ok=True)
    raw_results.to_csv(args.output_dir / "concurrency_raw.csv", index=False)
    robust.to_csv(args.output_dir / "concurrency_robust.csv", index=False)
    pd.concat(all_trades, ignore_index=True).to_csv(
        args.output_dir / "concurrency_trades.csv", index=False
    )
    print(robust.head(30).to_string(index=False))


if __name__ == "__main__":
    main()
