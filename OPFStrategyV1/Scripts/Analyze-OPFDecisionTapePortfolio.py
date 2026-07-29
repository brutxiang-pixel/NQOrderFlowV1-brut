import argparse
import importlib.util
import re
from pathlib import Path

import pandas as pd


DYNAMIC_REASON_HEADS = {"ActiveTrade", "DailyTradeLimit", "LiveDailyLoss"}
KEY = ["SnapshotID", "SignalID", "ResearchPath"]


def load_module(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def read_snapshot_csvs(root: Path, suffix: str, usecols=None) -> pd.DataFrame:
    parts = []
    for path in sorted(root.glob(f"*_{suffix}.csv")):
        part = pd.read_csv(path, usecols=usecols, low_memory=False)
        part["SourceFile"] = path.name
        part["SourceSequence"] = range(len(part))
        parts.append(part)
    if not parts:
        raise FileNotFoundError(f"No *_{suffix}.csv files in {root}")
    return pd.concat(parts, ignore_index=True)


def load_decision_tape(root: Path, period: str) -> pd.DataFrame:
    shadows = read_snapshot_csvs(root, "shadow_trades")
    decisions = read_snapshot_csvs(root, "execution_decisions")
    pnl = read_snapshot_csvs(root, "live_account_pnl")
    executions = read_snapshot_csvs(root, "execution_trades")

    for frame, columns in (
        (shadows, ("EntryTime", "ExitTime")),
        (decisions, ("Time",)),
        (pnl, ("EntryTime", "ExitTime")),
        (executions, ("EntryTime", "ExitTime")),
    ):
        for column in columns:
            frame[column] = pd.to_datetime(frame[column])

    trading_date = shadows.groupby("SnapshotID")["EntryTime"].max().dt.normalize()
    shadows["TradingDate"] = shadows["SnapshotID"].map(trading_date)
    decisions["TradingDate"] = decisions["SnapshotID"].map(trading_date)
    wanted_year = 2026 if period == "H1" else 2025
    shadows = shadows[shadows["TradingDate"].dt.year.eq(wanted_year)].copy()
    decisions = decisions[decisions["TradingDate"].dt.year.eq(wanted_year)].copy()
    decisions["ReasonHead"] = decisions["Reason"].fillna("").str.extract(
        r"^([^:|]+)", expand=False
    )
    relevant = decisions[
        decisions["Decision"].eq("Execute")
        | decisions["ReasonHead"].isin(DYNAMIC_REASON_HEADS)
    ].copy()

    shadow_columns = [
        *KEY,
        "EntryTime",
        "EntryBar",
        "ExitTime",
        "ExitBar",
        "Side",
        "Entry",
        "Stop",
        "InitialRiskPoints",
        "ExitReason",
        "GrossDollars",
        "NetDollars",
        "Ambiguous",
        "OriginalTradeID",
    ]
    tape = relevant.merge(
        shadows[shadow_columns],
        on=KEY,
        how="left",
        suffixes=("", "Shadow"),
        validate="many_to_one",
        indicator="ShadowJoin",
    )

    actual_columns = [
        "SnapshotID",
        "TradeID",
        "EntryTime",
        "ExitTime",
        "Classification",
        "EntryPrice",
        "ExitPrice",
        "GrossPnLDollars",
        "CommissionDollars",
        "NetPnLDollars",
    ]
    actual = pnl[actual_columns].rename(
        columns={
            "EntryTime": "AccountEntryTime",
            "ExitTime": "AccountExitTime",
            "EntryPrice": "AccountEntryPrice",
            "ExitPrice": "AccountExitPrice",
            "GrossPnLDollars": "AccountGross",
            "CommissionDollars": "AccountCommission",
            "NetPnLDollars": "AccountNet",
        }
    )
    tape["ResolvedTradeID"] = tape["TradeID"].fillna("")
    tape.loc[tape["ResolvedTradeID"].eq(""), "ResolvedTradeID"] = tape.loc[
        tape["ResolvedTradeID"].eq(""), "OriginalTradeID"
    ]
    tape = tape.merge(
        actual,
        left_on=["SnapshotID", "ResolvedTradeID"],
        right_on=["SnapshotID", "TradeID"],
        how="left",
        suffixes=("", "Account"),
        validate="many_to_one",
    )

    execution_columns = [
        "SnapshotID",
        "TradeID",
        "EntryTime",
        "ExitTime",
        "EntryPrice",
        "StopPrice",
        "FilledRiskPoints",
        "IsAbnormalExecution",
        "AbnormalReason",
    ]
    execution = executions[execution_columns].sort_values(
        ["SnapshotID", "TradeID", "ExitTime"]
    ).drop_duplicates(["SnapshotID", "TradeID"], keep="last")
    execution = execution.rename(
        columns={
            "EntryTime": "ExecutionEntryTime",
            "ExitTime": "ExecutionExitTime",
            "EntryPrice": "ExecutionEntryPrice",
        }
    )
    tape = tape.merge(
        execution,
        left_on=["SnapshotID", "ResolvedTradeID"],
        right_on=["SnapshotID", "TradeID"],
        how="left",
        suffixes=("", "Execution"),
        validate="many_to_one",
    )

    tape["EventTime"] = tape["Time"]
    tape["LifecycleEntryTime"] = tape["ExecutionEntryTime"].fillna(tape["EntryTime"])
    has_execution_exit = tape["ExecutionExitTime"].notna()
    has_account_exit = tape["AccountExitTime"].notna()
    tape["BaselineExitTime"] = tape["ExitTime"]
    tape.loc[has_account_exit, "BaselineExitTime"] = tape.loc[
        has_account_exit, "AccountExitTime"
    ]
    tape.loc[has_execution_exit, "BaselineExitTime"] = tape.loc[
        has_execution_exit, "ExecutionExitTime"
    ]
    valid_execution_exit = tape["ExecutionExitTime"].gt(tape["LifecycleEntryTime"])
    valid_account_exit = tape["AccountExitTime"].gt(tape["LifecycleEntryTime"])
    tape["CounterfactualExitTime"] = tape["ExitTime"]
    tape.loc[valid_account_exit, "CounterfactualExitTime"] = tape.loc[
        valid_account_exit, "AccountExitTime"
    ]
    tape.loc[valid_execution_exit, "CounterfactualExitTime"] = tape.loc[
        valid_execution_exit, "ExecutionExitTime"
    ]
    tape["ExitTimeFallback"] = tape["Decision"].eq("Execute") & ~(
        valid_execution_exit | valid_account_exit
    )
    tape["ExpectedParentTradeID"] = tape["Reason"].fillna("").str.extract(
        r"^ActiveTrade:([^|]+)", expand=False
    )
    referenced_until = (
        tape.dropna(subset=["ExpectedParentTradeID"])
        .groupby(["SnapshotID", "ExpectedParentTradeID"])["EventTime"]
        .max()
        .to_dict()
    )
    tape["ObservedActiveUntil"] = [
        max(
            baseline_exit,
            referenced_until.get((snapshot_id, trade_id), baseline_exit),
        )
        for snapshot_id, trade_id, baseline_exit in zip(
            tape["SnapshotID"], tape["ResolvedTradeID"], tape["BaselineExitTime"]
        )
    ]
    tape["Month"] = tape["TradingDate"].dt.to_period("M").astype(str)
    tape.sort_values(
        ["TradingDate", "SnapshotID", "EventTime", "Bar", "SourceSequence"],
        inplace=True,
    )
    return tape.reset_index(drop=True)


def audit_original_state(tape: pd.DataFrame) -> tuple[pd.DataFrame, pd.DataFrame]:
    detail = []
    daily = []
    for snapshot_id, day in tape.groupby("SnapshotID", sort=False):
        execute_rows = day[day["Decision"].eq("Execute")].copy()
        intervals = {
            row.ResolvedTradeID: (
                row.LifecycleEntryTime,
                row.ObservedActiveUntil,
                row.CounterfactualExitTime,
            )
            for row in execute_rows.itertuples(index=False)
        }
        active = None
        normal_count = 0
        realized = 0.0
        for row in day.itertuples(index=False):
            if active is not None and active["ExitTime"] <= row.EventTime:
                realized += active["Gross"]
                active = None

            active_id = None if active is None else active["TradeID"]
            expected_parent = row.ExpectedParentTradeID if pd.notna(row.ExpectedParentTradeID) else None
            active_match = True
            if row.ReasonHead == "ActiveTrade":
                active_match = active_id == expected_parent
            elif row.Decision == "Execute":
                active_match = active is None

            lifecycle_compatible = True
            if row.ReasonHead == "ActiveTrade":
                parent_interval = intervals.get(expected_parent)
                lifecycle_compatible = parent_interval is not None and (
                    parent_interval[0] <= row.EventTime <= parent_interval[1]
                    or parent_interval[0] <= row.EventTime <= parent_interval[2]
                )
            elif row.Decision == "Execute":
                lifecycle_compatible = not any(
                    trade_id != row.ResolvedTradeID
                    and entry_time < row.EventTime < exit_time
                    for trade_id, (entry_time, exit_time, _) in intervals.items()
                )

            count_match = int(row.DailyTradeCount) == normal_count
            pnl_match = abs(float(row.DailyPnlDollars) - realized) <= 0.011
            detail.append(
                {
                    "SnapshotID": snapshot_id,
                    "TradingDate": row.TradingDate,
                    "SignalID": row.SignalID,
                    "ResearchPath": row.ResearchPath,
                    "EventTime": row.EventTime,
                    "Decision": row.Decision,
                    "ReasonHead": row.ReasonHead,
                    "ExpectedParentTradeID": expected_parent,
                    "SimActiveTradeID": active_id,
                    "ActiveStateMatch": active_match,
                    "LifecycleCompatible": lifecycle_compatible,
                    "LoggedDailyTradeCount": int(row.DailyTradeCount),
                    "SimDailyTradeCount": normal_count,
                    "DailyTradeCountMatch": count_match,
                    "LoggedDailyPnl": float(row.DailyPnlDollars),
                    "SimRealizedPnl": realized,
                    "DailyPnlMatch": pnl_match,
                    "ExitTimeFallback": bool(row.ExitTimeFallback),
                }
            )

            if row.Decision == "Execute":
                classification = row.Classification if pd.notna(row.Classification) else "Missing"
                counts_normal = classification == "Normal"
                if counts_normal:
                    normal_count += 1
                gross = float(row.AccountGross) if pd.notna(row.AccountGross) else 0.0
                active = {
                    "TradeID": row.ResolvedTradeID,
                    "ExitTime": row.ObservedActiveUntil,
                    "Gross": gross,
                }
        if active is not None:
            realized += active["Gross"]
        daily.append(
            {
                "SnapshotID": snapshot_id,
                "TradingDate": day["TradingDate"].iloc[0],
                "NormalExecuteCount": normal_count,
                "FinalSimRealizedPnl": realized,
                "ExpectedExecuteCount": int(day["Decision"].eq("Execute").sum()),
            }
        )
    return pd.DataFrame(detail), pd.DataFrame(daily)


def summarize_tape(tape: pd.DataFrame, audit: pd.DataFrame) -> pd.DataFrame:
    return pd.DataFrame(
        [
            {
                "Candidates": len(tape),
                "UniqueCandidateKeys": len(tape[KEY].drop_duplicates()),
                "DuplicateDecisionEvents": int(tape.duplicated(KEY).sum()),
                "ShadowJoinPct": round(float(tape["ShadowJoin"].eq("both").mean() * 100), 4),
                "Execute": int(tape["Decision"].eq("Execute").sum()),
                "ActiveTrade": int(tape["ReasonHead"].eq("ActiveTrade").sum()),
                "DailyTradeLimit": int(tape["ReasonHead"].eq("DailyTradeLimit").sum()),
                "LiveDailyLoss": int(tape["ReasonHead"].eq("LiveDailyLoss").sum()),
                "ExitTimeFallbacks": int(tape["ExitTimeFallback"].sum()),
                "ActiveStateMatchPct": round(float(audit["ActiveStateMatch"].mean() * 100), 4),
                "LifecycleCompatiblePct": round(
                    float(audit["LifecycleCompatible"].mean() * 100), 4
                ),
                "DailyTradeCountMatchPct": round(
                    float(audit["DailyTradeCountMatch"].mean() * 100), 4
                ),
                "DailyPnlMatchPct": round(float(audit["DailyPnlMatch"].mean() * 100), 4),
            }
        ]
    )


def prepare_policy_tape(tape: pd.DataFrame, rich_module, bars: dict) -> pd.DataFrame:
    output = tape.copy()
    gross = []
    exit_times = []
    exit_reasons = []
    filled_risks = []
    outcome_sources = []
    entry_prices = []
    stop_prices = []
    for row in output.itertuples(index=False):
        original_normal = row.Decision == "Execute" and row.Classification == "Normal"
        original_account = row.Decision == "Execute" and pd.notna(row.AccountGross)
        entry_price = (
            float(row.ExecutionEntryPrice)
            if original_normal and pd.notna(row.ExecutionEntryPrice)
            else float(row.Entry)
        )
        stop_price = (
            float(row.StopPrice)
            if original_normal and pd.notna(row.StopPrice)
            else float(row.Stop)
        )
        filled_risk = (
            float(row.FilledRiskPoints)
            if original_normal and pd.notna(row.FilledRiskPoints)
            else float(row.InitialRiskPoints)
        )
        if original_account and not original_normal:
            actual_quantity = max(1.0, float(row.AccountCommission) / 1.2)
            trade_gross = float(row.AccountGross) * 3.0 / actual_quantity
            exit_time = row.CounterfactualExitTime
            exit_reason = row.ExitReason
            outcome_source = "ActualNonNormalControl"
        elif row.ResearchPath == "ZoneBirthResearch":
            result = rich_module.zonebirth_outcome(
                bars[rich_module.trading_date_key(row.TradingDate)],
                row.EventTime,
                entry_price,
                stop_price,
                row.Side,
            )
            trade_gross = float(result["Gross3"])
            exit_time = result["ExitTime"]
            exit_reason = result["ExitReason"]
            filled_risk = float(result["FilledRiskPoints"])
            outcome_source = "RichZoneBirth"
        elif original_normal and pd.notna(row.AccountGross):
            actual_quantity = max(1.0, float(row.AccountCommission) / 1.2)
            trade_gross = float(row.AccountGross) * 3.0 / actual_quantity
            exit_time = row.CounterfactualExitTime
            exit_reason = row.ExitReason
            outcome_source = "ActualScaled3"
        else:
            trade_gross = float(row.GrossDollars) * 1.5
            exit_time = row.ExitTime
            exit_reason = row.ExitReason
            outcome_source = "ShadowScaled3"
        gross.append(trade_gross)
        exit_times.append(exit_time)
        exit_reasons.append(exit_reason)
        filled_risks.append(filled_risk)
        outcome_sources.append(outcome_source)
        entry_prices.append(entry_price)
        stop_prices.append(stop_price)
    output["DirectGross3"] = gross
    output["DirectNet3"] = output["DirectGross3"] - 3.6
    output["DirectExitTime"] = exit_times
    output["DirectExitReason"] = exit_reasons
    output["PolicyFilledRisk"] = filled_risks
    output["OutcomeSource"] = outcome_sources
    output["PolicyEntryPrice"] = entry_prices
    output["PolicyStopPrice"] = stop_prices
    output["ShadowLiveUntil"] = output["ExitTime"]
    output["RollbackCountOnExit"] = False
    output["CountsNormal"] = ~output["Decision"].eq("Execute") | output[
        "Classification"
    ].eq("Normal")
    output["EconomicTrade"] = ~(
        output["Decision"].eq("Execute") & output["AccountGross"].isna()
    )
    output["OccupiesActive"] = output["EconomicTrade"]
    output = add_recorded_active_observation_flags(output)
    return (
        output.sort_values(["TradingDate", "EventTime", "Bar", "SourceSequence"])
        .drop_duplicates(KEY, keep="first")
        .reset_index(drop=True)
    )


def prepare_baseline_tape(tape: pd.DataFrame) -> pd.DataFrame:
    output = tape.copy()
    original_execute = output["Decision"].eq("Execute")
    has_actual = original_execute & output["AccountGross"].notna()
    output["DirectGross3"] = output["GrossDollars"].astype(float)
    output["DirectNet3"] = output["NetDollars"].astype(float)
    output.loc[has_actual, "DirectGross3"] = output.loc[has_actual, "AccountGross"]
    output.loc[has_actual, "DirectNet3"] = output.loc[has_actual, "AccountNet"]
    aborted = original_execute & ~has_actual
    output.loc[aborted, "DirectGross3"] = 0.0
    output.loc[aborted, "DirectNet3"] = 0.0
    output["DirectExitTime"] = output["ExitTime"]
    output.loc[original_execute, "DirectExitTime"] = output.loc[
        original_execute, "ObservedActiveUntil"
    ]
    output.loc[aborted, "DirectExitTime"] = output.loc[aborted, "EventTime"]
    output["DirectExitReason"] = output["ExitReason"]
    output["PolicyFilledRisk"] = output["InitialRiskPoints"].astype(float)
    output["PolicyEntryPrice"] = output["Entry"].astype(float)
    output["PolicyStopPrice"] = output["Stop"].astype(float)
    has_execution_entry = original_execute & output["ExecutionEntryPrice"].notna()
    output.loc[has_execution_entry, "PolicyEntryPrice"] = output.loc[
        has_execution_entry, "ExecutionEntryPrice"
    ]
    output.loc[has_execution_entry & output["StopPrice"].notna(), "PolicyStopPrice"] = output.loc[
        has_execution_entry & output["StopPrice"].notna(), "StopPrice"
    ]
    output["OutcomeSource"] = "ShadowBaseline"
    output.loc[has_actual, "OutcomeSource"] = "ActualBaseline"
    output.loc[aborted, "OutcomeSource"] = "AbortBaseline"
    output["ShadowLiveUntil"] = output["ExitTime"]
    output["CountsNormal"] = ~original_execute | output["Classification"].eq("Normal")
    output["OccupiesActive"] = ~aborted
    output["EconomicTrade"] = ~aborted
    return add_recorded_active_observation_flags(output)


def add_recorded_active_observation_flags(output: pd.DataFrame) -> pd.DataFrame:
    original_execute = output["Decision"].eq("Execute")
    execute_lifecycle_by_trade = (
        output.loc[original_execute & output["ResolvedTradeID"].notna()]
        .groupby(["SnapshotID", "ResolvedTradeID"])
        .agg(
            EntryTime=("LifecycleEntryTime", "min"),
            ActiveUntil=("ObservedActiveUntil", "max"),
        )
    )
    output["RecordedActiveObservationBlock"] = [
        pd.notna(parent_id)
        and (snapshot_id, parent_id) in execute_lifecycle_by_trade.index
        and event_time
        <= execute_lifecycle_by_trade.loc[(snapshot_id, parent_id), "ActiveUntil"]
        for snapshot_id, parent_id, event_time in zip(
            output["SnapshotID"], output["ExpectedParentTradeID"], output["EventTime"]
        )
    ]
    output["RecordedFutureActiveBlock"] = [
        pd.notna(parent_id)
        and (snapshot_id, parent_id) in execute_lifecycle_by_trade.index
        and execute_lifecycle_by_trade.loc[(snapshot_id, parent_id), "EntryTime"] > event_time
        for snapshot_id, parent_id, event_time in zip(
            output["SnapshotID"], output["ExpectedParentTradeID"], output["EventTime"]
        )
    ]
    return output


def infer_daily_cap(tape: pd.DataFrame) -> int:
    values = {
        int(value)
        for reason in tape["Reason"].fillna("")
        for value in re.findall(r"(?:dailyCap=|DailyTradeLimit:max=)(\d+)", reason)
    }
    if len(values) != 1:
        raise ValueError(f"Expected one recorded daily cap, found {sorted(values)}")
    return values.pop()


def load_v208_control(root: Path, tape: pd.DataFrame) -> tuple[set, dict]:
    events = read_snapshot_csvs(root, "execution_events")
    events["Time"] = pd.to_datetime(events["Time"])
    deferred = events[events["Event"].isin(
        ["DEFERRED_CAPTURED_V208", "DEFERRED_REPLACED_V208", "DEFERRED_ACTIVATED_V208"]
    )].copy()
    forced_blocks = set(
        zip(
            deferred.loc[deferred["Event"].ne("DEFERRED_ACTIVATED_V208"), "SnapshotID"],
            deferred.loc[deferred["Event"].ne("DEFERRED_ACTIVATED_V208"), "SignalID"],
            deferred.loc[deferred["Event"].ne("DEFERRED_ACTIVATED_V208"), "ResearchPath"],
            deferred.loc[deferred["Event"].ne("DEFERRED_ACTIVATED_V208"), "Time"],
        )
    )
    activated = deferred[deferred["Event"].eq("DEFERRED_ACTIVATED_V208")]
    forced_activations = {}
    for event in activated.itertuples(index=False):
        matches = tape[
            tape["SnapshotID"].eq(event.SnapshotID)
            & tape["SignalID"].eq(event.SignalID)
            & tape["ResearchPath"].eq(event.ResearchPath)
            & tape["EventTime"].eq(event.Time)
            & tape["Decision"].eq("Execute")
            & tape["Classification"].eq("Normal")
        ]
        if len(matches) != 1:
            raise ValueError(
                f"Expected one v2.08 activated trade for {event.SnapshotID}/{event.SignalID}/{event.Time}, found {len(matches)}"
            )
        row = matches.iloc[0]
        forced_activations[(event.SnapshotID, event.Time)] = {
            "SnapshotID": event.SnapshotID,
            "TradingDate": row.TradingDate,
            "Month": row.Month,
            "SignalID": row.SignalID,
            "TradeID": row.ResolvedTradeID,
            "EntryTime": event.Time,
            "ExitTime": row.CounterfactualExitTime,
            "Side": row.Side,
            "ResearchPath": row.ResearchPath,
            "Source": "Deferred",
            "Gross3": float(row.AccountGross),
            "Net3": float(row.AccountNet),
            "ExitReason": row.ExitReason,
            "OutcomeSource": "ActualV208DeferredControl",
            "IsDeferred": True,
            "CountsNormal": True,
            "OccupiesActive": True,
            "EconomicTrade": True,
        }
    return forced_blocks, forced_activations


def policy_allowed(row, config: dict) -> bool:
    candidate_gate = config.get("CandidateGate")
    if candidate_gate is not None and not candidate_gate(row):
        return False
    if row.Side == "Long" and row.ResearchPath in config["DropLongPaths"]:
        return False
    if row.Side == "Short" and row.ResearchPath in config["DropShortPaths"]:
        return False
    if float(row.SetupQualityScore) < config["MinSetupQuality"]:
        return False
    if float(row.EstimatedRR) < config["MinEstimatedRR"]:
        return False
    planned = float(row.InitialRiskPoints)
    filled = float(row.PolicyFilledRisk)
    for low, high in config["ExcludedRiskBands"]:
        if low < planned <= high or low < filled <= high:
            return False
    return True


def prepare_simulation_days(tape: pd.DataFrame, rich_module, bars: dict) -> list:
    prepared = []
    for snapshot_id, day in tape.groupby("SnapshotID", sort=False):
        if rich_module is None:
            day_bars = pd.DataFrame({"Time": pd.Series(dtype="datetime64[ns]")})
        else:
            date_key = rich_module.trading_date_key(day["TradingDate"].iloc[0])
            day_bars = bars[date_key]
        candidates_by_time = {
            time: list(part.sort_values(["Bar", "SourceSequence"]).itertuples(index=False))
            for time, part in day.groupby("EventTime", sort=False)
        }
        clock = sorted(set(candidates_by_time) | set(day_bars["Time"]))
        prepared.append((snapshot_id, day_bars, clock, candidates_by_time))
    return prepared


def policy_active_until(exit_time, config: dict):
    delay_minutes = float(config.get("ActiveReleaseDelayMinutes", 0.0))
    return pd.Timestamp(exit_time) + pd.Timedelta(minutes=delay_minutes)


def simulate_policy(tape: pd.DataFrame, rich_module, bars: dict, config: dict):
    accepted = []
    diagnostics = {
        "DirectAccepted": 0,
        "DeferredAccepted": 0,
        "ActiveBlocked": 0,
        "StaticBlocked": 0,
        "CapBlocked": 0,
        "LossBlocked": 0,
        "DeferredExpired": 0,
        "DeferredGateBlocked": 0,
        "RecordedActiveObservationBlocked": 0,
        "RecordedFutureActiveBlocked": 0,
        "ForcedControlBlocked": 0,
        "ForcedControlActivated": 0,
        "ForcedControlStateMismatch": 0,
    }
    prepared_days = config.get("PreparedDays") or prepare_simulation_days(
        tape, rich_module, bars
    )
    for snapshot_id, day_bars, clock, candidates_by_time in prepared_days:
        active = None
        pending = {}
        parent_release = {}
        capture_sequence = 0
        normal_count = 0
        realized = 0.0

        for time in clock:
            if (
                active is not None
                and not active["PnLRealized"]
                and active["ExitTime"] < time
            ):
                realized += active["Net3"]
                active["PnLRealized"] = True
            if active is not None and active["ActiveUntil"] < time:
                if not active["PnLRealized"]:
                    realized += active["Net3"]
                if active.get("RollbackCountOnExit", False):
                    normal_count = max(0, normal_count - 1)
                parent_release[active["TradeID"]] = active["ActiveUntil"]
                active = None

            forced = config.get("ForcedActivations", {}).get((snapshot_id, time))
            if forced is not None:
                if active is not None or normal_count >= config["DailyCap"] or realized <= -config["DailyLoss"]:
                    diagnostics["ForcedControlStateMismatch"] += 1
                else:
                    forced = dict(forced)
                    forced.setdefault(
                        "ActiveUntil", policy_active_until(forced["ExitTime"], config)
                    )
                    forced.setdefault("PnLRealized", False)
                    accepted.append(forced)
                    active = forced
                    normal_count += 1
                    diagnostics["ForcedControlActivated"] += 1

            candidates = candidates_by_time.get(time)
            if candidates is not None:
                candidate_sort_key = config.get("CandidateSortKey")
                if candidate_sort_key is not None:
                    candidates = sorted(
                        candidates,
                        key=candidate_sort_key,
                        reverse=config.get("CandidateSortReverse", True),
                    )
                for row in candidates:
                    if (
                        active is not None
                        and active["ActiveUntil"] == time
                        and (
                            config.get("ReleaseOnEqual", False)
                            or (config.get("BaselineMode", False) and row.Decision == "Execute")
                        )
                    ):
                        if not active["PnLRealized"]:
                            realized += active["Net3"]
                        if active.get("RollbackCountOnExit", False):
                            normal_count = max(0, normal_count - 1)
                        parent_release[active["TradeID"]] = active["ActiveUntil"]
                        active = None
                    if not policy_allowed(row, config):
                        diagnostics["StaticBlocked"] += 1
                        continue
                    if (
                        row.SnapshotID,
                        row.SignalID,
                        row.ResearchPath,
                        row.EventTime,
                    ) in config.get("ForcedBlocks", set()):
                        diagnostics["ForcedControlBlocked"] += 1
                        continue
                    if active is not None:
                        diagnostics["ActiveBlocked"] += 1
                        if config["DeferredEnabled"] and not active["IsDeferred"]:
                            capture_sequence += 1
                            pending.setdefault(active["TradeID"], []).append(
                                {
                                    "Row": row,
                                    "Sequence": capture_sequence,
                                }
                            )
                        continue
                    if (
                        config.get("BaselineMode", False)
                        or config.get("HonorRecordedActiveObservations", False)
                    ) and bool(
                        getattr(row, "RecordedActiveObservationBlock", False)
                    ):
                        diagnostics["RecordedActiveObservationBlocked"] += 1
                        if bool(getattr(row, "RecordedFutureActiveBlock", False)):
                            diagnostics["RecordedFutureActiveBlocked"] += 1
                        continue
                    if normal_count >= config["DailyCap"]:
                        diagnostics["CapBlocked"] += 1
                        continue
                    if realized <= -config["DailyLoss"]:
                        diagnostics["LossBlocked"] += 1
                        continue
                    override = config.get("OutcomeOverrides", {}).get(
                        (row.SnapshotID, row.SignalID, row.ResearchPath, row.EventTime)
                    )
                    direct_gross = (
                        float(override["Gross3"])
                        if override is not None
                        else float(row.DirectGross3)
                    )
                    direct_net = (
                        direct_gross - 3.6
                        if override is not None
                        else float(row.DirectNet3)
                    )
                    direct_exit_time = (
                        override["ExitTime"] if override is not None else row.DirectExitTime
                    )
                    direct_exit_reason = (
                        override["ExitReason"]
                        if override is not None
                        else row.DirectExitReason
                    )
                    outcome_source = (
                        override["OutcomeSource"]
                        if override is not None
                        else row.OutcomeSource
                    )
                    record = {
                        "SnapshotID": snapshot_id,
                        "TradingDate": row.TradingDate,
                        "Month": row.Month,
                        "SignalID": row.SignalID,
                        "TradeID": row.ResolvedTradeID
                        if pd.notna(row.ResolvedTradeID) and str(row.ResolvedTradeID)
                        else f"DIRECT|{row.SignalID}|{row.ResearchPath}",
                        "EntryTime": time,
                        "ExitTime": direct_exit_time,
                        "ActiveUntil": policy_active_until(direct_exit_time, config),
                        "PnLRealized": False,
                        "Side": row.Side,
                        "ResearchPath": row.ResearchPath,
                        "InitialRiskPoints": float(row.InitialRiskPoints),
                        "EntryPrice": float(row.PolicyEntryPrice),
                        "StopPrice": float(row.PolicyStopPrice),
                        "Ambiguous": bool(row.Ambiguous),
                        "Source": "Direct",
                        "Gross3": direct_gross,
                        "Net3": direct_net,
                        "ExitReason": direct_exit_reason,
                        "OutcomeSource": outcome_source,
                        "IsDeferred": False,
                        "CountsNormal": bool(getattr(row, "CountsNormal", True)),
                        "OccupiesActive": bool(getattr(row, "OccupiesActive", True)),
                        "EconomicTrade": bool(getattr(row, "EconomicTrade", True)),
                    }
                    accepted.append(record)
                    if record["OccupiesActive"]:
                        active = record
                    if record["CountsNormal"]:
                        normal_count += 1
                    diagnostics["DirectAccepted"] += 1

            if active is not None or not config["DeferredEnabled"] or not pending:
                continue

            parent_id = max(
                pending,
                key=lambda key: max(item["Sequence"] for item in pending[key]),
            )
            release_time = parent_release.get(parent_id)
            if release_time is None or time <= release_time:
                continue
            candidates = pending.pop(parent_id)
            live = [item for item in candidates if item["Row"].ShadowLiveUntil >= time]
            if not live:
                diagnostics["DeferredExpired"] += len(candidates)
                continue
            selected = max(
                live,
                key=lambda item: (item["Row"].EventTime, item["Sequence"]),
            )
            row = selected["Row"]
            outcome_key = (
                snapshot_id,
                time,
                row.Side,
                float(row.InitialRiskPoints),
                config["DeferredBars"],
                config.get("DeferredTargetR", 3.0),
            )
            outcome_cache = config.get("DeferredOutcomeCache")
            if outcome_cache is not None and outcome_key in outcome_cache:
                outcome = outcome_cache[outcome_key]
            else:
                outcome = rich_module.deferred_outcome(
                    day_bars,
                    time - pd.Timedelta(microseconds=1),
                    row.Side,
                    float(row.InitialRiskPoints),
                    config["DeferredBars"],
                    config.get("DeferredTargetR", 3.0),
                )
                if outcome_cache is not None:
                    outcome_cache[outcome_key] = outcome
            if outcome is None:
                diagnostics["DeferredExpired"] += 1
                continue
            rr_key = (
                snapshot_id,
                outcome["EntryTime"],
                outcome["EntryPrice"],
                row.Side,
                row.ResearchPath,
                float(row.InitialRiskPoints),
            )
            rr_cache = config.get("DeferredRRCache")
            if rr_cache is not None and rr_key in rr_cache:
                rr_allowed = rr_cache[rr_key]
            else:
                rr_allowed = rich_module.deferred_rr_recheck_allowed(
                    day_bars,
                    outcome["EntryTime"],
                    outcome["EntryPrice"],
                    row.Side,
                    row.ResearchPath,
                    float(row.InitialRiskPoints),
                )
                if rr_cache is not None:
                    rr_cache[rr_key] = rr_allowed
            if not rr_allowed or not policy_allowed(row, config):
                diagnostics["DeferredGateBlocked"] += 1
                continue
            if normal_count >= config["DailyCap"]:
                diagnostics["CapBlocked"] += 1
                continue
            if realized <= -config["DailyLoss"]:
                diagnostics["LossBlocked"] += 1
                continue
            gross = float(outcome["Gross3"])
            record = {
                "SnapshotID": snapshot_id,
                "TradingDate": row.TradingDate,
                "Month": row.Month,
                "SignalID": row.SignalID,
                "TradeID": f"DEFERRED|{row.SignalID}|{row.ResearchPath}",
                "EntryTime": outcome["EntryTime"],
                "ExitTime": outcome["ExitTime"],
                "ActiveUntil": policy_active_until(outcome["ExitTime"], config),
                "PnLRealized": False,
                "Side": row.Side,
                "ResearchPath": row.ResearchPath,
                "InitialRiskPoints": float(row.InitialRiskPoints),
                "EntryPrice": float(outcome["EntryPrice"]),
                "StopPrice": float(outcome["EntryPrice"])
                - (1 if row.Side == "Long" else -1) * float(row.InitialRiskPoints),
                "Ambiguous": False,
                "Source": "Deferred",
                "Gross3": gross,
                "Net3": gross - 3.6,
                "ExitReason": outcome["ExitReason"],
                "OutcomeSource": "CounterfactualRepriced",
                "IsDeferred": True,
                "CountsNormal": True,
                "OccupiesActive": True,
                "EconomicTrade": True,
            }
            accepted.append(record)
            active = record
            normal_count += 1
            diagnostics["DeferredAccepted"] += 1

        if active is not None:
            if not active["PnLRealized"]:
                realized += active["Net3"]
    return pd.DataFrame(accepted), diagnostics


def policy_metrics(trades: pd.DataFrame, all_dates: list) -> dict:
    if trades.empty:
        return {"Trades": 0, "Gross": 0.0, "AccountNet": 0.0, "PF": 0.0, "MaxDD": 0.0}
    positive = trades.loc[trades["Net3"] > 0, "Net3"].sum()
    negative = -trades.loc[trades["Net3"] < 0, "Net3"].sum()
    daily = trades.groupby("TradingDate")["Net3"].sum().reindex(all_dates, fill_value=0)
    cumulative = daily.cumsum()
    iso = pd.to_datetime(trades["TradingDate"]).dt.isocalendar()
    trades = trades.copy()
    trades["Week"] = iso.year.astype(str) + "-W" + iso.week.astype(str).str.zfill(2)
    weekly = trades.groupby("Week")["Net3"].sum()
    normal = trades[trades.get("CountsNormal", True)]
    non_normal = trades[~trades.get("CountsNormal", True)]
    return {
        "Trades": len(trades),
        "NormalTrades": int(trades.get("CountsNormal", True).sum()),
        "EconomicTrades": int(trades.get("EconomicTrade", True).sum()),
        "Gross": round(float(trades["Gross3"].sum()), 2),
        "AccountNet": round(float(trades["Net3"].sum()), 2),
        "NormalGross": round(float(normal["Gross3"].sum()), 2),
        "NormalAccountNet": round(float(normal["Net3"].sum()), 2),
        "NonNormalAccountNet": round(float(non_normal["Net3"].sum()), 2),
        "PF": round(float(positive / negative), 4) if negative else float("inf"),
        "MaxDD": round(float((cumulative.cummax() - cumulative).max()), 2),
        "PositiveWeekPct": round(float((weekly > 0).mean() * 100), 2),
        "WorstWeek": round(float(weekly.min()), 2),
        "SyntheticTrades": int((~trades["OutcomeSource"].str.startswith("Actual")).sum()),
        "CounterfactualGross": round(
            float(trades.loc[~trades["OutcomeSource"].str.startswith("Actual"), "Gross3"].sum()), 2
        ),
    }


def identity_comparison(tape: pd.DataFrame, trades: pd.DataFrame) -> dict:
    expected = set(
        zip(
            tape.loc[tape["Decision"].eq("Execute"), "SignalID"],
            tape.loc[tape["Decision"].eq("Execute"), "ResearchPath"],
            tape.loc[tape["Decision"].eq("Execute"), "EventTime"],
        )
    )
    actual = set(zip(trades["SignalID"], trades["ResearchPath"], trades["EntryTime"]))
    matched = expected & actual
    return {
        "ExpectedExecuteIdentities": len(expected),
        "SimulatedIdentities": len(actual),
        "MatchedIdentities": len(matched),
        "IdentityMatchPct": round(len(matched) / len(expected) * 100, 4) if expected else 100.0,
        "MissingExpected": len(expected - actual),
        "SimulationOnly": len(actual - expected),
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--period", choices=("H1", "Q4"), default="H1")
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--rich", type=Path)
    parser.add_argument("--run-policy", action="store_true")
    parser.add_argument("--run-v208-control", action="store_true")
    parser.add_argument("--run-baseline", action="store_true")
    args = parser.parse_args()

    tape = load_decision_tape(args.evidence, args.period)
    audit, daily = audit_original_state(tape)
    summary = summarize_tape(tape, audit)
    args.output_dir.mkdir(parents=True, exist_ok=True)
    tape.to_csv(args.output_dir / "decision_tape.csv", index=False)
    audit.to_csv(args.output_dir / "decision_tape_control_audit.csv", index=False)
    daily.to_csv(args.output_dir / "decision_tape_daily_control.csv", index=False)
    summary.to_csv(args.output_dir / "decision_tape_summary.csv", index=False)
    print(summary.to_string(index=False))

    if args.run_baseline:
        baseline_tape = prepare_baseline_tape(tape)
        baseline_config = {
            "DailyCap": infer_daily_cap(baseline_tape),
            "DailyLoss": 300.0,
            "DeferredEnabled": False,
            "DeferredBars": 18,
            "DropLongPaths": set(),
            "DropShortPaths": set(),
            "ExcludedRiskBands": [],
            "MinSetupQuality": 0.0,
            "MinEstimatedRR": 0.0,
            "BaselineMode": True,
            "ReleaseOnEqual": False,
        }
        baseline_trades, baseline_diagnostics = simulate_policy(
            baseline_tape, None, {}, baseline_config
        )
        baseline_result = {
            **policy_metrics(baseline_trades, sorted(baseline_tape["TradingDate"].unique())),
            **identity_comparison(baseline_tape, baseline_trades),
            **baseline_diagnostics,
        }
        pd.DataFrame([baseline_result]).to_csv(
            args.output_dir / "decision_tape_baseline_summary.csv", index=False
        )
        baseline_trades.to_csv(
            args.output_dir / "decision_tape_baseline_trades.csv", index=False
        )
        print(pd.Series(baseline_result).to_string())

    if args.run_policy:
        if args.rich is None:
            parser.error("--rich is required with --run-policy")
        rich_module = load_module(
            "opf_v209_execution_calibrated",
            Path(__file__).with_name("Analyze-OPFV209H1ExecutionCalibratedPortfolio.py"),
        )
        bars = rich_module.bars_by_trading_date(args.rich)
        policy_tape = prepare_policy_tape(tape, rich_module, bars)
        config = {
            "DailyCap": 10,
            "DailyLoss": 300.0,
            "DeferredEnabled": True,
            "DeferredBars": 18,
            "DropLongPaths": {"FailureReverse_RetestFailed"},
            "DropShortPaths": set(),
            "ExcludedRiskBands": [(13.25, 16.0)],
            "MinSetupQuality": 0.0,
            "MinEstimatedRR": 0.0,
            "BaselineMode": False,
            "ReleaseOnEqual": False,
        }
        trades, diagnostics = simulate_policy(policy_tape, rich_module, bars, config)
        metrics = {
            **config,
            **policy_metrics(trades, sorted(policy_tape["TradingDate"].unique())),
            **diagnostics,
        }
        pd.DataFrame([metrics]).to_csv(
            args.output_dir / "decision_tape_policy_summary.csv", index=False
        )
        trades.to_csv(args.output_dir / "decision_tape_policy_trades.csv", index=False)
        print(pd.Series(metrics).to_string())

    if args.run_v208_control:
        if args.rich is None:
            parser.error("--rich is required with --run-v208-control")
        rich_module = load_module(
            "opf_v209_execution_calibrated_control",
            Path(__file__).with_name("Analyze-OPFV209H1ExecutionCalibratedPortfolio.py"),
        )
        bars = rich_module.bars_by_trading_date(args.rich)
        policy_tape = prepare_policy_tape(tape, rich_module, bars)
        original_account = policy_tape["Decision"].eq("Execute") & policy_tape[
            "AccountGross"
        ].notna()
        policy_tape.loc[original_account, "DirectGross3"] = policy_tape.loc[
            original_account, "AccountGross"
        ]
        policy_tape.loc[original_account, "DirectNet3"] = policy_tape.loc[
            original_account, "AccountNet"
        ]
        policy_tape.loc[original_account, "DirectExitTime"] = policy_tape.loc[
            original_account, "CounterfactualExitTime"
        ]
        policy_tape.loc[original_account, "OutcomeSource"] = "ActualV208DirectControl"
        forced_blocks, forced_activations = load_v208_control(args.evidence, tape)
        config = {
            "DailyCap": 10,
            "DailyLoss": 300.0,
            "DeferredEnabled": False,
            "DeferredBars": 18,
            "DropLongPaths": {"FailureReverse_RetestFailed"},
            "DropShortPaths": set(),
            "ExcludedRiskBands": [(13.25, 16.0)],
            "MinSetupQuality": 0.0,
            "MinEstimatedRR": 0.0,
            "BaselineMode": False,
            "ReleaseOnEqual": False,
            "ForcedBlocks": forced_blocks,
            "ForcedActivations": forced_activations,
            "HonorRecordedActiveObservations": True,
        }
        trades, diagnostics = simulate_policy(policy_tape, rich_module, bars, config)
        metrics = {
            **policy_metrics(trades, sorted(policy_tape["TradingDate"].unique())),
            **identity_comparison(
                tape[tape["Classification"].eq("Normal") & tape["Decision"].eq("Execute")],
                trades[trades["CountsNormal"]],
            ),
            **diagnostics,
        }
        pd.DataFrame([metrics]).to_csv(
            args.output_dir / "decision_tape_v208_control_summary.csv", index=False
        )
        trades.to_csv(
            args.output_dir / "decision_tape_v208_control_trades.csv", index=False
        )
        print(pd.Series(metrics).to_string())


if __name__ == "__main__":
    main()
