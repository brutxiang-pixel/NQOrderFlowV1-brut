import argparse
import importlib.util
from pathlib import Path

import pandas as pd


MID_RISK_LOW = 13.25
MID_RISK_HIGH = 16.0
POINT_VALUE = 2.0
COMMISSION3 = 3.6


def load_module(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def read_csvs(root: Path, pattern: str) -> pd.DataFrame:
    files = sorted(root.glob(pattern))
    if not files:
        raise FileNotFoundError(f"No files matched {pattern} in {root}")
    return pd.concat((pd.read_csv(path, low_memory=False) for path in files), ignore_index=True)


def load_execution_ledger(root: Path) -> pd.DataFrame:
    ledger = read_csvs(root, "*_execution_trades.csv")
    for column in ("EntryTime", "ExitTime"):
        ledger[column] = pd.to_datetime(ledger[column])
    for column in ("EntryPrice", "StopPrice", "FilledRiskPoints"):
        ledger[column] = pd.to_numeric(ledger[column], errors="coerce")
    ledger.sort_values(["SnapshotID", "TradeID", "ExitTime"], inplace=True)
    return ledger.drop_duplicates(["SnapshotID", "TradeID"], keep="last")


def bars_by_trading_date(rich_root: Path) -> dict[str, pd.DataFrame]:
    selected = pd.read_csv(
        rich_root / "selected_snapshots.csv", dtype={"Date": str, "SnapshotId": str}
    )
    output = {}
    for row in selected.itertuples(index=False):
        path = rich_root / f"{row.SnapshotId}_rich_bar_features.csv"
        bars = pd.read_csv(
            path,
            usecols=["Time", "Bar", "Open", "High", "Low", "Close"],
            low_memory=False,
        )
        bars["Time"] = pd.to_datetime(bars["Time"])
        bars["Bar"] = pd.to_numeric(bars["Bar"], errors="raise").astype(int)
        for column in ("Open", "High", "Low", "Close"):
            bars[column] = pd.to_numeric(bars[column], errors="raise")
        date_key = pd.Timestamp(row.Date).strftime("%Y-%m-%d")
        output[date_key] = bars.sort_values(["Time", "Bar"]).reset_index(drop=True)
    return output


def trading_date_key(value) -> str:
    return pd.Timestamp(value).strftime("%Y-%m-%d")


def in_mid_risk_band(value: float) -> bool:
    return MID_RISK_LOW < float(value) <= MID_RISK_HIGH


def zonebirth_outcome(
    bars: pd.DataFrame,
    entry_time: pd.Timestamp,
    entry_price: float,
    stop_price: float,
    side: str,
) -> dict:
    direction = 1 if side == "Long" else -1
    risk = direction * (entry_price - stop_price)
    if risk <= 0:
        raise ValueError(
            f"Invalid ZoneBirth risk: side={side}, entry={entry_price}, stop={stop_price}"
        )

    base_target = entry_price + direction * risk * 2.5
    runner_target = entry_price + direction * risk * 4.0
    later = bars[bars["Time"].gt(entry_time)]
    if later.empty:
        return {
            "ExitTime": entry_time,
            "ExitReason": "NoLaterRichBar",
            "Gross3": 0.0,
            "FilledRiskPoints": risk,
        }

    base_filled = False
    base_fill_bar = None
    for row in later.itertuples(index=False):
        stop_hit = row.Low <= stop_price if direction == 1 else row.High >= stop_price
        base_hit = row.High >= base_target if direction == 1 else row.Low <= base_target
        runner_hit = row.High >= runner_target if direction == 1 else row.Low <= runner_target

        if not base_filled:
            if stop_hit:
                return {
                    "ExitTime": row.Time,
                    "ExitReason": "Base:Stop|Runner:Stop",
                    "Gross3": -risk * POINT_VALUE * 3,
                    "FilledRiskPoints": risk,
                }
            if base_hit:
                base_filled = True
                base_fill_bar = row.Bar
                if runner_hit:
                    return {
                        "ExitTime": row.Time,
                        "ExitReason": "Base:Target|Runner:Target",
                        "Gross3": risk * 18.0,
                        "FilledRiskPoints": risk,
                    }
                continue
            continue

        if runner_hit:
            return {
                "ExitTime": row.Time,
                "ExitReason": "Base:Target|Runner:Target",
                "Gross3": risk * 18.0,
                "FilledRiskPoints": risk,
            }
        if row.Bar > base_fill_bar:
            break_even_hit = row.Low <= entry_price if direction == 1 else row.High >= entry_price
            if break_even_hit:
                return {
                    "ExitTime": row.Time,
                    "ExitReason": "Base:Target|Runner:ProtectBE",
                    "Gross3": risk * 10.0,
                    "FilledRiskPoints": risk,
                }

    last = later.iloc[-1]
    runner_points = direction * (float(last["Close"]) - entry_price)
    gross = (
        risk * 10.0 + runner_points * POINT_VALUE
        if base_filled
        else runner_points * POINT_VALUE * 3
    )
    return {
        "ExitTime": last["Time"],
        "ExitReason": "SnapshotEndAfterBase" if base_filled else "SnapshotEnd",
        "Gross3": gross,
        "FilledRiskPoints": risk,
    }


def calibrated_core(core: pd.DataFrame, ledger: pd.DataFrame, bars: dict) -> tuple[pd.DataFrame, dict]:
    ledger_columns = [
        "SnapshotID",
        "TradeID",
        "EntryTime",
        "ExitTime",
        "EntryPrice",
        "StopPrice",
        "FilledRiskPoints",
    ]
    source = core.merge(
        ledger[ledger_columns],
        left_on=["SnapshotID", "OriginalTradeID"],
        right_on=["SnapshotID", "TradeID"],
        how="left",
        suffixes=("", "Ledger"),
        validate="one_to_one",
    )
    output = []
    collapsed_exit_fallbacks = 0
    missing_ledger = 0
    zonebirth_rebuilt = 0
    for row in source.itertuples(index=False):
        ledger_entry = getattr(row, "EntryTimeLedger")
        ledger_exit = getattr(row, "ExitTimeLedger")
        entry_time = ledger_entry if pd.notna(ledger_entry) else row.EntryTime
        if pd.isna(ledger_entry):
            missing_ledger += 1
        if pd.isna(ledger_exit) or ledger_exit <= entry_time:
            exit_time = row.ExitTime
            collapsed_exit_fallbacks += 1
        else:
            exit_time = ledger_exit

        entry_price = (
            float(row.EntryPrice) if pd.notna(row.EntryPrice) else float(row.ActualEntryPrice)
        )
        stop_price = float(row.StopPrice) if pd.notna(row.StopPrice) else float(row.Stop)
        filled_risk = (
            float(row.FilledRiskPoints)
            if pd.notna(row.FilledRiskPoints)
            else abs(entry_price - stop_price)
        )
        gross = float(row.ActualGross2) * 1.5
        exit_reason = row.ExitReason
        if row.ResearchPath == "ZoneBirthResearch":
            outcome = zonebirth_outcome(
                bars[trading_date_key(row.TradingDate)],
                entry_time,
                entry_price,
                stop_price,
                row.Side,
            )
            exit_time = outcome["ExitTime"]
            exit_reason = outcome["ExitReason"]
            gross = outcome["Gross3"]
            filled_risk = outcome["FilledRiskPoints"]
            zonebirth_rebuilt += 1

        strategy_normal = row.Classification == "Normal"
        output.append(
            {
                "SnapshotID": row.SnapshotID,
                "TradeID": row.OriginalTradeID,
                "SignalID": row.SignalID,
                "EntryTime": entry_time,
                "ExitTime": exit_time,
                "TradingDate": row.TradingDate,
                "Month": row.Month,
                "Side": row.Side,
                "ResearchPath": row.ResearchPath,
                "InitialRiskPoints": float(row.InitialRiskPoints),
                "FilledRiskPoints": filled_risk,
                "RiskGateBlocked": in_mid_risk_band(row.InitialRiskPoints)
                or in_mid_risk_band(filled_risk),
                "DeferredGateBlocked": False,
                "Source": "Core",
                "Priority": 0,
                "Classification": row.Classification,
                "CountsTowardLimit": strategy_normal,
                "CountsTowardGross": strategy_normal,
                "Gross3": gross,
                "Net3": gross - COMMISSION3,
                "ParentTradeID": None,
                "ExitReason": exit_reason,
            }
        )
    diagnostics = {
        "CoreRows": len(output),
        "MissingExecutionLedger": missing_ledger,
        "CollapsedExitFallbacks": collapsed_exit_fallbacks,
        "ZoneBirthCoreRebuilt": zonebirth_rebuilt,
    }
    return pd.DataFrame(output), diagnostics


def shadow_gross3(row, mode: str) -> float:
    net2 = float(row.ObservedNet2 if mode == "Observed" else row.ConservativeNet2)
    return (net2 + 2.4) * 1.5


def calibrated_released(
    rows: pd.DataFrame,
    bars: dict,
    mode: str,
    reason_heads=("DailyTradeLimit", "LiveDailyLoss"),
) -> pd.DataFrame:
    source = rows[
        ~rows["IsCore"]
        & rows["Period"].eq("H1")
        & rows["OriginalDecision"].eq("Skip")
        & rows["ReasonHead"].isin(reason_heads)
    ].copy()
    output = []
    for row in source.itertuples(index=False):
        filled_risk = float(row.InitialRiskPoints)
        gross = shadow_gross3(row, mode)
        exit_time = row.ExitTime
        exit_reason = row.ExitReason
        if row.ResearchPath == "ZoneBirthResearch":
            outcome = zonebirth_outcome(
                bars[trading_date_key(row.TradingDate)],
                row.EntryTime,
                float(row.Entry),
                float(row.Stop),
                row.Side,
            )
            exit_time = outcome["ExitTime"]
            exit_reason = outcome["ExitReason"]
            gross = outcome["Gross3"]
            filled_risk = outcome["FilledRiskPoints"]
        output.append(
            {
                "SnapshotID": row.SnapshotID,
                "TradeID": f"RELEASED|{row.SignalID}|{row.ResearchPath}",
                "SignalID": row.SignalID,
                "EntryTime": row.EntryTime,
                "ExitTime": exit_time,
                "TradingDate": row.TradingDate,
                "Month": row.Month,
                "Side": row.Side,
                "ResearchPath": row.ResearchPath,
                "InitialRiskPoints": float(row.InitialRiskPoints),
                "FilledRiskPoints": filled_risk,
                "RiskGateBlocked": in_mid_risk_band(row.InitialRiskPoints)
                or in_mid_risk_band(filled_risk),
                "DeferredGateBlocked": False,
                "Source": row.ReasonHead,
                "Priority": 1,
                "Classification": "Normal",
                "CountsTowardLimit": True,
                "CountsTowardGross": True,
                "Gross3": gross,
                "Net3": gross - COMMISSION3,
                "ParentTradeID": None,
                "ExitReason": exit_reason,
            }
        )
    return pd.DataFrame(output)


def deferred_outcome(
    bars: pd.DataFrame,
    ready_time: pd.Timestamp,
    side: str,
    risk: float,
    max_bars: int = 18,
    target_r: float = 3.0,
) -> dict | None:
    later = bars[bars["Time"].gt(ready_time)]
    if later.empty:
        return None
    entry_index = int(later.index[0])
    entry_time = bars.loc[entry_index, "Time"]
    entry_price = float(bars.loc[entry_index, "Open"])
    direction = 1 if side == "Long" else -1
    stop = entry_price - direction * risk
    target = entry_price + direction * risk * target_r
    be_armed = False
    scan = bars.loc[entry_index : entry_index + max_bars - 1]
    for row in scan.itertuples(index=False):
        stop_hit = row.Low <= stop if direction == 1 else row.High >= stop
        target_hit = row.High >= target if direction == 1 else row.Low <= target
        if stop_hit:
            return {
                "EntryTime": entry_time,
                "EntryPrice": entry_price,
                "ExitTime": row.Time,
                "ExitReason": "StopCollision" if target_hit else "Stop",
                "Gross3": direction * (stop - entry_price) * POINT_VALUE * 3,
            }
        if target_hit:
            return {
                "EntryTime": entry_time,
                "EntryPrice": entry_price,
                "ExitTime": row.Time,
                "ExitReason": "Target",
                "Gross3": risk * target_r * POINT_VALUE * 3,
            }
        if not be_armed:
            reached_one_r = (
                row.High >= entry_price + risk
                if direction == 1
                else row.Low <= entry_price - risk
            )
            if reached_one_r:
                be_armed = True
                stop = entry_price
    last = scan.iloc[-1]
    return {
        "EntryTime": entry_time,
        "EntryPrice": entry_price,
        "ExitTime": last["Time"],
        "ExitReason": "TimeStop",
        "Gross3": direction * (float(last["Close"]) - entry_price) * POINT_VALUE * 3,
    }


def deferred_rr_recheck_allowed(
    bars: pd.DataFrame,
    entry_time: pd.Timestamp,
    entry_price: float,
    side: str,
    path: str,
    risk: float,
) -> bool:
    fixed_reward_paths = {
        "ObservationConfirm",
        "BreakawayFvg",
        "BreakawayFvg_Qualified",
        "BreakawayRetest",
        "ShadowCandidate",
        "TrendPullbackConfirmed",
        "FailureReverse_RetestFailed",
    }
    if path in fixed_reward_paths:
        return True

    min_rr = {
        ("Long", "ObservationStrict_Other"): 0.25,
        ("Long", "ObservationStrict_Other_WideStop1_5R"): 0.25,
        ("Long", "ObservationStrict_BullFresh_WideStop1_5R"): 0.5,
        ("Short", "FailureReverse_RetestFailed_WideStop1_5R"): 0.25,
        ("Short", "FailureReverse_ObservationInvalidated_WideStop1_5R"): 0.25,
        ("Long", "FailureReverse_ObservationInvalidated_WideStop1_5R"): 0.5,
        ("Short", "FailureReverse_ObservationInvalidated"): 0.25,
        ("Long", "FailureReverse_ObservationInvalidated"): 0.25,
        ("Short", "UnknownRegimeZoneTouch"): 0.25,
        ("Short", "ZoneBirthResearch"): 0.25,
        ("Long", "AlmostConfirmed"): 0.25,
        ("Long", "ObservationConfirm_WideStop1_5R"): 0.5,
        ("Short", "ObservationConfirm_WideStop1_5R"): 0.5,
    }.get((side, path), 1.0)
    recent = bars[bars["Time"].lt(entry_time)].tail(20)
    if side == "Long":
        structures = recent.loc[recent["High"].gt(entry_price), "High"]
        reward = 0.0 if structures.empty else float(structures.min()) - entry_price
    else:
        structures = recent.loc[recent["Low"].lt(entry_price), "Low"]
        reward = 0.0 if structures.empty else entry_price - float(structures.max())
    estimated_rr = reward / risk if risk > 0 else 0.0
    return estimated_rr >= min_rr


def calibrated_deferred(rows: pd.DataFrame, core: pd.DataFrame, bars: dict) -> tuple[pd.DataFrame, dict]:
    source = rows[
        ~rows["IsCore"]
        & rows["Period"].eq("H1")
        & rows["OriginalDecision"].eq("Skip")
        & rows["ReasonHead"].eq("ActiveTrade")
    ].copy()
    source["ParentTradeID"] = source["OriginalReason"].str.extract(
        r"^ActiveTrade:([^|]+)", expand=False
    )
    parents = core[
        ["SnapshotID", "TradeID", "ExitTime"]
    ].rename(columns={"TradeID": "ParentTradeID", "ExitTime": "ParentExitTime"})
    source = source.merge(
        parents, on=["SnapshotID", "ParentTradeID"], how="inner", validate="many_to_one"
    )
    source = source[source["ExitTime"].gt(source["ParentExitTime"])].copy()
    source["ReadyTime"] = source[["EntryTime", "ParentExitTime"]].max(axis=1)
    source.sort_values(
        ["SnapshotID", "ParentTradeID", "ReadyTime", "EntryTime", "EntryBar"],
        inplace=True,
    )
    selected = source.groupby(["SnapshotID", "ParentTradeID"], sort=False).tail(1)

    output = []
    no_later_bar = 0
    for row in selected.itertuples(index=False):
        outcome = deferred_outcome(
            bars[trading_date_key(row.TradingDate)],
            row.ReadyTime,
            row.Side,
            float(row.InitialRiskPoints),
        )
        if outcome is None:
            no_later_bar += 1
            continue
        rr_recheck_allowed = deferred_rr_recheck_allowed(
            bars[trading_date_key(row.TradingDate)],
            outcome["EntryTime"],
            outcome["EntryPrice"],
            row.Side,
            row.ResearchPath,
            float(row.InitialRiskPoints),
        )
        gross = outcome["Gross3"]
        output.append(
            {
                "SnapshotID": row.SnapshotID,
                "TradeID": f"DEFERRED|{row.SignalID}|{row.ResearchPath}",
                "SignalID": row.SignalID,
                "EntryTime": outcome["EntryTime"],
                "ExitTime": outcome["ExitTime"],
                "TradingDate": row.TradingDate,
                "Month": row.Month,
                "Side": row.Side,
                "ResearchPath": row.ResearchPath,
                "InitialRiskPoints": float(row.InitialRiskPoints),
                "FilledRiskPoints": float(row.InitialRiskPoints),
                "RiskGateBlocked": in_mid_risk_band(row.InitialRiskPoints),
                "DeferredGateBlocked": not rr_recheck_allowed,
                "Source": "DeferredContinuation",
                "Priority": 2,
                "Classification": "Normal",
                "CountsTowardLimit": True,
                "CountsTowardGross": True,
                "Gross3": gross,
                "Net3": gross - COMMISSION3,
                "ParentTradeID": row.ParentTradeID,
                "ExitReason": outcome["ExitReason"],
            }
        )
    diagnostics = {
        "DeferredBlockedCandidates": len(source),
        "DeferredParentsWithLiveCandidate": len(selected),
        "DeferredNoLaterBar": no_later_bar,
        "DeferredRrRecheckBlocked": sum(
            1 for row in output if row["DeferredGateBlocked"]
        ),
    }
    return pd.DataFrame(output), diagnostics


def simulate(
    core: pd.DataFrame,
    released: pd.DataFrame,
    active_candidates: pd.DataFrame,
    deferred: pd.DataFrame,
):
    events = pd.concat([core, released, active_candidates, deferred], ignore_index=True)
    events = events[
        ~(
            events["Side"].eq("Long")
            & events["ResearchPath"].eq("FailureReverse_RetestFailed")
        )
    ].copy()
    accepted = []
    diagnostics = {
        "CoreAccepted": 0,
        "ReleasedAccepted": 0,
        "DeferredAccepted": 0,
        "ActiveBlocked": 0,
        "CapBlocked": 0,
        "LossBlocked": 0,
        "RiskGateBlocked": 0,
        "DeferredGateBlocked": 0,
        "StaleParentBlocked": 0,
        "DuplicateCandidateBlocked": 0,
    }
    for _, day in events.groupby("SnapshotID", sort=False):
        day = day.sort_values(["EntryTime", "Priority", "TradeID"])
        active = None
        realized = 0.0
        normal_count = 0
        accepted_core_ids = set()
        accepted_candidate_keys = set()
        for row in day.itertuples(index=False):
            if active is not None and active["ExitTime"] <= row.EntryTime:
                realized += active["Net3"]
                active = None
            if active is not None:
                diagnostics["ActiveBlocked"] += 1
                continue
            if row.Source == "DeferredContinuation" and row.ParentTradeID not in accepted_core_ids:
                diagnostics["StaleParentBlocked"] += 1
                continue
            candidate_key = (row.SignalID, row.ResearchPath)
            if candidate_key in accepted_candidate_keys:
                diagnostics["DuplicateCandidateBlocked"] += 1
                continue
            if row.CountsTowardLimit:
                if row.DeferredGateBlocked:
                    diagnostics["DeferredGateBlocked"] += 1
                    continue
                if row.RiskGateBlocked:
                    diagnostics["RiskGateBlocked"] += 1
                    continue
                if normal_count >= 10:
                    diagnostics["CapBlocked"] += 1
                    continue
                if realized <= -300:
                    diagnostics["LossBlocked"] += 1
                    continue

            record = row._asdict()
            iso = row.TradingDate.isocalendar()
            record["Week"] = f"{iso.year}-W{iso.week:02d}"
            accepted.append(record)
            accepted_candidate_keys.add(candidate_key)
            active = record
            if row.CountsTowardLimit:
                normal_count += 1
            if row.Source == "Core":
                diagnostics["CoreAccepted"] += 1
                accepted_core_ids.add(row.TradeID)
            elif row.Source == "DeferredContinuation":
                diagnostics["DeferredAccepted"] += 1
            else:
                diagnostics["ReleasedAccepted"] += 1
        if active is not None:
            realized += active["Net3"]
    return pd.DataFrame(accepted), diagnostics


def summarize(trades: pd.DataFrame, dates: list, weeks: list) -> dict:
    normal = trades[trades["CountsTowardGross"]]
    daily = trades.groupby("TradingDate")["Net3"].sum().reindex(dates, fill_value=0)
    weekly = trades.groupby("Week")["Net3"].sum().reindex(weeks, fill_value=0)
    positive = trades.loc[trades["Net3"] > 0, "Net3"].sum()
    negative = -trades.loc[trades["Net3"] < 0, "Net3"].sum()
    cumulative = daily.cumsum()
    gross = float(normal["Gross3"].sum())
    return {
        "Trades": len(trades),
        "NormalTrades": len(normal),
        "Gross": round(gross, 2),
        "AccountNet": round(float(trades["Net3"].sum()), 2),
        "PF": round(float(positive / negative), 4) if negative else float("inf"),
        "MaxDD": round(float((cumulative.cummax() - cumulative).max()), 2),
        "PositiveWeekPct": round(float((weekly > 0).mean() * 100), 2),
        "WorstWeek": round(float(weekly.min()), 2),
        "JanFebGross": round(
            float(normal[normal["Month"].isin(["2026-01", "2026-02"])]["Gross3"].sum()), 2
        ),
        "MarAprGross": round(
            float(normal[normal["Month"].isin(["2026-03", "2026-04"])]["Gross3"].sum()), 2
        ),
        "MayJunGross": round(
            float(normal[normal["Month"].isin(["2026-05", "2026-06"])]["Gross3"].sum()), 2
        ),
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--rich", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    args = parser.parse_args()

    portfolio = load_module(
        "opf_v207_portfolio", Path(__file__).with_name("Analyze-OPFV207ProfitRiskPortfolio.py")
    )
    rows, core_source, _, _, _ = portfolio.load_candidates(args.evidence)
    bars = bars_by_trading_date(args.rich)
    ledger = load_execution_ledger(args.evidence)
    core, core_diagnostics = calibrated_core(
        core_source[core_source["Period"].eq("H1")], ledger, bars
    )
    deferred, deferred_diagnostics = calibrated_deferred(rows, core, bars)
    dates = sorted(core["TradingDate"].unique())
    weeks = sorted(
        {f"{date.isocalendar().year}-W{date.isocalendar().week:02d}" for date in dates}
    )

    args.output_dir.mkdir(parents=True, exist_ok=True)
    summaries = []
    traces = {}
    for mode in ("Observed", "Conservative"):
        released = calibrated_released(rows, bars, mode)
        active_candidates = calibrated_released(rows, bars, mode, ("ActiveTrade",))
        trades, simulation_diagnostics = simulate(
            core, released, active_candidates, deferred
        )
        summary = {
            "Mode": mode,
            **summarize(trades, dates, weeks),
            **core_diagnostics,
            **deferred_diagnostics,
            **simulation_diagnostics,
        }
        summary["TargetMet"] = summary["Gross"] >= 20000
        summaries.append(summary)
        traces[mode] = trades

    summary_frame = pd.DataFrame(summaries)
    summary_frame.to_csv(args.output_dir / "v209_h1_execution_calibrated_summary.csv", index=False)
    pd.concat(
        [trace.assign(Mode=mode) for mode, trace in traces.items()], ignore_index=True
    ).to_csv(args.output_dir / "v209_h1_execution_calibrated_trades.csv", index=False)

    path_parts = []
    monthly_parts = []
    for mode, trace in traces.items():
        path = (
            trace.groupby(["Source", "Side", "ResearchPath"])[["Gross3", "Net3"]]
            .agg(["count", "sum", "mean"])
            .reset_index()
        )
        path.insert(0, "Mode", mode)
        path_parts.append(path)
        monthly = trace.groupby(["Month", "Source"])[["Gross3", "Net3"]].agg(
            ["count", "sum"]
        ).reset_index()
        monthly.insert(0, "Mode", mode)
        monthly_parts.append(monthly)
    pd.concat(path_parts, ignore_index=True).to_csv(
        args.output_dir / "v209_h1_execution_calibrated_paths.csv", index=False
    )
    pd.concat(monthly_parts, ignore_index=True).to_csv(
        args.output_dir / "v209_h1_execution_calibrated_monthly.csv", index=False
    )

    print(summary_frame.to_string(index=False))


if __name__ == "__main__":
    main()
