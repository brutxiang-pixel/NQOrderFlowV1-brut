import argparse
import math
from pathlib import Path

import pandas as pd


DYNAMIC_REASON_HEADS = {"ActiveTrade", "DailyTradeLimit", "LiveDailyLoss"}
KEY = ["SnapshotID", "SignalID", "ResearchPath"]
POINT_VALUE = 2.0
TICK_SIZE = 0.25
QUANTITY = 3
COMMISSION = 3.6
DAILY_CAP = 10
DAILY_LOSS = 600.0
STAGE_C_IDENTITY_MIN = 0.90
STAGE_C_DAILY_COUNT_ERROR_MAX = 1
STAGE_C_GROSS_ABS_ERROR_MAX = 150.0
STAGE_C_GROSS_REL_ERROR_MAX = 0.15
DEVELOPMENT_IDENTITY_MIN = 0.95
DEVELOPMENT_ROLE_MIN = 0.95
DEVELOPMENT_GROSS_ABS_ERROR_MAX = 100.0
DEVELOPMENT_GROSS_REL_ERROR_MAX = 0.10


def align_to_tick(value: float) -> float:
    return math.floor(value / TICK_SIZE + 0.5) * TICK_SIZE


def read_csvs(root: Path, suffix: str, sequence: bool = False) -> pd.DataFrame:
    parts = []
    for path in sorted(root.glob(f"*_{suffix}.csv")):
        part = pd.read_csv(path, low_memory=False)
        if sequence:
            part["SourceSequence"] = range(len(part))
        parts.append(part)
    if not parts:
        raise FileNotFoundError(f"No *_{suffix}.csv files in {root}")
    return pd.concat(parts, ignore_index=True)


def reason_head(value) -> str:
    text = "" if pd.isna(value) else str(value)
    return text.split(":", 1)[0].split("|", 1)[0]


def load_evidence(root: Path, require_turns: bool = False):
    calibration = read_csvs(root, "decision_tape_calibration")
    bars = read_csvs(root, "decision_tape_calibration_bars")
    decisions = read_csvs(root, "execution_decisions", sequence=True)
    trades = read_csvs(root, "execution_trades")
    pnl = read_csvs(root, "live_account_pnl")
    events = read_csvs(root, "execution_events")
    try:
        turns = read_csvs(root, "decision_tape_market_turns")
    except FileNotFoundError:
        if require_turns:
            raise
        turns = pd.DataFrame()
    for frame, columns in (
        (calibration, ("EntryTime", "ResolveTime")),
        (bars, ("Time",)),
        (decisions, ("Time",)),
        (trades, ("EntryTime", "ExitTime")),
        (pnl, ("EntryTime", "ExitTime")),
        (events, ("Time",)),
    ):
        for column in columns:
            frame[column] = pd.to_datetime(frame[column])
    decisions["ReasonHead"] = decisions["Reason"].map(reason_head)
    return calibration, bars, turns, decisions, trades, pnl, events


def policy_parameters(row):
    path = row.ResearchPath
    if row.Side == "Long" and path in {"ObservationConfirm", "AlmostConfirmed"}:
        return float(row.TargetR), 0.0, 36
    if row.Side == "Long" and path in {
        "ObservationConfirm_WideStop1_5R",
        "ObservationStrict_Other",
    }:
        return float(row.TargetR), 0.0, 12
    if row.Side == "Short" and path == "BreakawayFvg":
        return float(row.TargetR), 1.5, 36
    if row.Side == "Short" and path == "FailureReverse_ObservationInvalidated_WideStop1_5R":
        return float(row.TargetR), 0.0, 36
    return float(row.TargetR), 0.0, None


def dynamic_policy_parameters(option: str):
    if option.startswith("F"):
        target, bars = option[1:].split("_T", 1)
        return float(target), 0.0, int(bars)
    if option.startswith("BE"):
        trigger, remainder = option[2:].split("_F", 1)
        target, bars = remainder.split("_T", 1)
        return float(target), float(trigger), int(bars)
    raise ValueError(f"Unsupported dynamic exit option: {option}")


def regular_outcome(row, path_bars: pd.DataFrame) -> dict:
    direction = 1 if row.Side == "Long" else -1
    entry = execution_entry(row)
    stop = float(row.Stop)
    risk = abs(entry - stop)
    target_r, be_trigger_r, time_stop_bars = policy_parameters(row)
    target = align_to_tick(entry + direction * risk * target_r)
    break_even_armed = False
    break_even_trigger_bar = None

    for bar in path_bars[path_bars["RelativeBar"].ge(1)].itertuples(index=False):
        stop_hit = bar.Low <= stop if direction == 1 else bar.High >= stop
        target_hit = bar.High >= target if direction == 1 else bar.Low <= target
        if stop_hit:
            return outcome(row, bar.Time, "SL", -risk * POINT_VALUE * QUANTITY, bar.RelativeBar, False)
        if target_hit:
            gross = risk * target_r * POINT_VALUE * QUANTITY
            return outcome(row, bar.Time, "TP", gross, bar.RelativeBar, False)

        if be_trigger_r > 0:
            trigger = entry + direction * risk * be_trigger_r
            trigger_hit = bar.High >= trigger if direction == 1 else bar.Low <= trigger
            break_even_hit = bar.Low <= entry if direction == 1 else bar.High >= entry
            if not break_even_armed and trigger_hit:
                break_even_armed = True
                break_even_trigger_bar = bar.RelativeBar
            elif (
                break_even_armed
                and bar.RelativeBar > break_even_trigger_bar
                and break_even_hit
            ):
                return outcome(row, bar.Time, "BE", 0.0, bar.RelativeBar, False)

        if time_stop_bars is not None and bar.RelativeBar >= time_stop_bars - 1:
            points = direction * (float(bar.Close) - entry)
            return outcome(
                row,
                bar.Time,
                "TIME_STOP",
                points * POINT_VALUE * QUANTITY,
                bar.RelativeBar,
                False,
            )

    last = path_bars.sort_values("RelativeBar").iloc[-1]
    points = direction * (float(last["Close"]) - entry)
    return outcome(
        row,
        last["Time"],
        "PATH_END",
        points * POINT_VALUE * QUANTITY,
        int(last["RelativeBar"]),
        True,
    )


def zonebirth_outcome(row, path_bars: pd.DataFrame) -> dict:
    entry = execution_entry(row)
    stop = float(row.Stop)
    risk = abs(entry - stop)
    base_target = align_to_tick(entry - risk * 2.5)
    runner_target = align_to_tick(entry - risk * 4.0)
    base_filled = False
    base_fill_bar = None

    for bar in path_bars[path_bars["RelativeBar"].ge(1)].itertuples(index=False):
        if not base_filled:
            if bar.High >= stop:
                return outcome(
                    row,
                    bar.Time,
                    "SPLIT_BASE_SL_RUNNER_SL",
                    -risk * POINT_VALUE * QUANTITY,
                    bar.RelativeBar,
                    False,
                )
            if bar.Low <= base_target:
                base_filled = True
                base_fill_bar = bar.RelativeBar
                if bar.Low <= runner_target:
                    return outcome(
                        row,
                        bar.Time,
                        "SPLIT_BASE_TP_RUNNER_TP",
                        risk * 18.0,
                        bar.RelativeBar,
                        False,
                    )
            continue

        if bar.Low <= runner_target:
            return outcome(
                row,
                bar.Time,
                "SPLIT_BASE_TP_RUNNER_TP",
                risk * 18.0,
                bar.RelativeBar,
                False,
            )
        if bar.RelativeBar > base_fill_bar and bar.High >= entry:
            return outcome(
                row,
                bar.Time,
                "SPLIT_BASE_TP_RUNNER_BE",
                risk * 10.0,
                bar.RelativeBar,
                False,
            )

    last = path_bars.sort_values("RelativeBar").iloc[-1]
    runner_points = entry - float(last["Close"])
    gross = risk * 10.0 + runner_points * POINT_VALUE if base_filled else runner_points * POINT_VALUE * QUANTITY
    return outcome(row, last["Time"], "PATH_END", gross, int(last["RelativeBar"]), True)


def execution_entry(row) -> float:
    if bool(getattr(row, "EntryQuoteValid", False)):
        return float(row.EntryAsk if row.Side == "Long" else row.EntryBid)
    return float(row.Entry)


def entry_and_stop(row, fill_anchors: dict) -> tuple[float, float, bool]:
    key = (row.SnapshotID, row.SignalID, row.ResearchPath)
    anchor = fill_anchors.get(key)
    if anchor is not None:
        return anchor[0], anchor[1], True
    return execution_entry(row), float(row.Stop), False


def bar_lookup(path_bars: pd.DataFrame) -> dict:
    return {
        int(row.Bar): (row.Time, int(row.RelativeBar), float(row.Close))
        for row in path_bars.itertuples(index=False)
    }


def turn_slice(row, turns_by_snapshot: dict) -> pd.DataFrame:
    turns = turns_by_snapshot[row.SnapshotID]
    return turns[
        turns["Sequence"].gt(int(row.EntryMarketSequence))
        & turns["Sequence"].le(int(row.ResolveMarketSequence))
    ]


def crossed_levels(start: float, end: float, levels: list[tuple[str, float]]):
    if end > start:
        return sorted(
            ((name, level) for name, level in levels if start < level <= end),
            key=lambda item: item[1],
        )
    if end < start:
        return sorted(
            ((name, level) for name, level in levels if end <= level < start),
            key=lambda item: item[1],
            reverse=True,
        )
    return []


def turn_regular_outcome(
    row,
    path_bars: pd.DataFrame,
    market_turns: pd.DataFrame,
    fill_anchors: dict,
    policy_override=None,
) -> dict:
    direction = 1 if row.Side == "Long" else -1
    entry, stop, fill_anchor_used = entry_and_stop(row, fill_anchors)
    risk = abs(entry - stop)
    target_r, be_trigger_r, time_stop_bars = (
        policy_override if policy_override is not None else policy_parameters(row)
    )
    target = align_to_tick(entry + direction * risk * target_r)
    trigger = entry + direction * risk * be_trigger_r
    break_even_stop = align_to_tick(entry)
    break_even_armed = False
    break_even_trigger_bar = None
    break_even_requires_next_bar = row.Side == "Short" and row.ResearchPath == "BreakawayFvg"
    time_stop_bar = (
        int(row.EntryBar) + time_stop_bars - 1
        if time_stop_bars is not None
        else None
    )
    turns_by_bar = {
        int(bar): group.sort_values("Sequence")
        for bar, group in market_turns.groupby("Bar", sort=False)
    }
    current_price = entry

    for candle in path_bars.sort_values("RelativeBar").itertuples(index=False):
        bar = int(candle.Bar)
        if time_stop_bar is not None and bar > time_stop_bar:
            break
        for turn in turns_by_bar.get(bar, pd.DataFrame()).itertuples(index=False):
            levels = [("TARGET", target)]
            break_even_active = break_even_armed and (
                not break_even_requires_next_bar or bar > break_even_trigger_bar
            )
            if break_even_active:
                levels.append(("BE", break_even_stop))
            else:
                levels.append(("STOP", stop))
                if be_trigger_r > 0 and not break_even_armed:
                    levels.append(("BE_TRIGGER", trigger))
            for name, _ in crossed_levels(current_price, float(turn.Price), levels):
                if name == "BE_TRIGGER":
                    break_even_armed = True
                    break_even_trigger_bar = bar
                    continue
                if name == "TARGET":
                    result = outcome(
                        row,
                        candle.Time,
                        "TP",
                        risk * target_r * POINT_VALUE * QUANTITY,
                        candle.RelativeBar,
                        False,
                    )
                elif name == "BE":
                    result = outcome(
                        row, candle.Time, "BE", 0.0, candle.RelativeBar, False
                    )
                else:
                    result = outcome(
                        row,
                        candle.Time,
                        "SL",
                        -risk * POINT_VALUE * QUANTITY,
                        candle.RelativeBar,
                        False,
                    )
                result["FillAnchorUsed"] = fill_anchor_used
                return result
            current_price = float(turn.Price)

        break_even_active = break_even_armed and (
            not break_even_requires_next_bar or bar > break_even_trigger_bar
        )
        stop_level = break_even_stop if break_even_active else stop
        stop_hit = candle.Low <= stop_level if direction == 1 else candle.High >= stop_level
        target_hit = candle.High >= target if direction == 1 else candle.Low <= target
        if stop_hit or target_hit:
            if stop_hit:
                role = "BE" if break_even_active else "SL"
                gross = 0.0 if break_even_active else -risk * POINT_VALUE * QUANTITY
            else:
                role = "TP"
                gross = risk * target_r * POINT_VALUE * QUANTITY
            result = outcome(
                row, candle.Time, role, gross, candle.RelativeBar, False
            )
            result["FillAnchorUsed"] = fill_anchor_used
            return result

        if not break_even_armed and be_trigger_r > 0:
            trigger_hit = candle.High >= trigger if direction == 1 else candle.Low <= trigger
            if trigger_hit:
                break_even_armed = True
                break_even_trigger_bar = bar

        if time_stop_bar is not None and bar >= time_stop_bar:
            result = outcome(
                row,
                candle.Time,
                "TIME_STOP",
                direction * (float(candle.Close) - entry) * POINT_VALUE * QUANTITY,
                candle.RelativeBar,
                False,
            )
            result["FillAnchorUsed"] = fill_anchor_used
            return result

    last = path_bars.sort_values("RelativeBar").iloc[-1]
    result = outcome(
        row,
        last["Time"],
        "PATH_END",
        direction * (float(last["Close"]) - entry) * POINT_VALUE * QUANTITY,
        int(last["RelativeBar"]),
        True,
    )
    result["FillAnchorUsed"] = fill_anchor_used
    return result


def turn_zonebirth_outcome(
    row, path_bars: pd.DataFrame, market_turns: pd.DataFrame, fill_anchors: dict
) -> dict:
    entry, stop, fill_anchor_used = entry_and_stop(row, fill_anchors)
    risk = abs(entry - stop)
    base_target = align_to_tick(entry - risk * 2.5)
    runner_target = align_to_tick(entry - risk * 4.0)
    break_even_stop = align_to_tick(entry)
    base_filled = False
    base_target_enabled = True
    runner_target_enabled = True
    runner_break_even_applied = False
    candles = {
        int(candle.Bar): candle
        for candle in path_bars.sort_values("RelativeBar").itertuples(index=False)
    }
    current_price = entry

    for turn in market_turns.sort_values("Sequence").itertuples(index=False):
        bar = int(turn.Bar)
        candle = candles.get(bar)
        if candle is None:
            current_price = float(turn.Price)
            continue
        reference = candles.get(bar - 1, candle)
        if not base_filled:
            levels = [("STOP", stop)]
            if base_target_enabled:
                levels.append(("BASE", base_target))
        else:
            runner_stop = break_even_stop if runner_break_even_applied else stop
            levels = [("RUNNER_STOP", runner_stop)]
            if runner_target_enabled:
                levels.append(("RUNNER_TARGET", runner_target))

        for name, _ in crossed_levels(current_price, float(turn.Price), levels):
            if name == "STOP":
                result = outcome(
                    row,
                    candle.Time,
                    "SPLIT_BASE_SL_RUNNER_SL",
                    -risk * POINT_VALUE * QUANTITY,
                    candle.RelativeBar,
                    False,
                )
                result["FillAnchorUsed"] = fill_anchor_used
                return result
            if name == "BASE":
                if float(reference.High) >= stop:
                    base_target_enabled = False
                    continue
                base_filled = True
                runner_break_even_applied = (
                    entry - float(reference.Close) >= TICK_SIZE
                )
                if float(turn.Price) <= runner_target:
                    runner_stop = break_even_stop if runner_break_even_applied else stop
                    if float(reference.High) >= runner_stop:
                        runner_target_enabled = False
                        continue
                    result = outcome(
                        row,
                        candle.Time,
                        "SPLIT_BASE_TP_RUNNER_TP",
                        risk * 18.0,
                        candle.RelativeBar,
                        False,
                    )
                    result["FillAnchorUsed"] = fill_anchor_used
                    return result
                continue
            if name == "RUNNER_TARGET":
                runner_stop = break_even_stop if runner_break_even_applied else stop
                if float(reference.High) >= runner_stop:
                    runner_target_enabled = False
                    continue
                result = outcome(
                    row,
                    candle.Time,
                    "SPLIT_BASE_TP_RUNNER_TP",
                    risk * 18.0,
                    candle.RelativeBar,
                    False,
                )
                result["FillAnchorUsed"] = fill_anchor_used
                return result

            role = (
                "SPLIT_BASE_TP_RUNNER_BE"
                if runner_break_even_applied
                else "SPLIT_BASE_TP_RUNNER_SL"
            )
            gross = risk * (10.0 if runner_break_even_applied else 8.0)
            result = outcome(
                row, candle.Time, role, gross, candle.RelativeBar, False
            )
            result["FillAnchorUsed"] = fill_anchor_used
            return result
        current_price = float(turn.Price)

    last = path_bars.sort_values("RelativeBar").iloc[-1]
    runner_points = entry - float(last["Close"])
    gross = (
        risk * 10.0 + runner_points * POINT_VALUE
        if base_filled
        else runner_points * POINT_VALUE * QUANTITY
    )
    result = outcome(
        row,
        last["Time"],
        "PATH_END",
        gross,
        int(last["RelativeBar"]),
        True,
    )
    result["FillAnchorUsed"] = fill_anchor_used
    return result


def outcome(row, exit_time, exit_role, gross, relative_bar, path_end):
    return {
        "SnapshotID": row.SnapshotID,
        "SignalID": row.SignalID,
        "ResearchPath": row.ResearchPath,
        "PredictedExitTime": exit_time,
        "PredictedExitRole": exit_role,
        "PredictedGross": round(float(gross), 2),
        "PredictedNet": round(float(gross) - COMMISSION, 2),
        "PredictedExitRelativeBar": int(relative_bar),
        "PathEndFallback": path_end,
    }


def predict_outcomes(
    calibration: pd.DataFrame,
    bars: pd.DataFrame,
    turns: pd.DataFrame | None = None,
    trades: pd.DataFrame | None = None,
    exit_options: dict | None = None,
) -> pd.DataFrame:
    grouped_bars = {
        key: group.sort_values("RelativeBar")
        for key, group in bars.groupby(KEY, sort=False)
    }
    turns_by_snapshot = None
    if turns is not None and not turns.empty:
        turns_by_snapshot = {
            snapshot_id: group.sort_values("Sequence")
            for snapshot_id, group in turns.groupby("SnapshotID", sort=False)
        }
    fill_anchors = {}
    if trades is not None and not trades.empty:
        fill_anchors = {
            (row.SnapshotID, row.SignalID, row.ResearchPath): (
                float(row.EntryPrice),
                float(row.EntryPrice) - float(row.InitialRiskPoints)
                if row.Side == "Long"
                else float(row.EntryPrice) + float(row.InitialRiskPoints),
            )
            for row in trades.itertuples(index=False)
        }
    rows = []
    for row in calibration.itertuples(index=False):
        path_bars = grouped_bars[(row.SnapshotID, row.SignalID, row.ResearchPath)]
        option = "Current" if exit_options is None else exit_options.get(
            (row.SnapshotID, row.SignalID, row.ResearchPath), "Current"
        )
        policy_override = (
            None if option == "Current" else dynamic_policy_parameters(option)
        )
        if turns_by_snapshot is not None:
            market_turns = turn_slice(row, turns_by_snapshot)
            if (
                option == "Current"
                and row.Side == "Short"
                and row.ResearchPath == "ZoneBirthResearch"
            ):
                rows.append(
                    turn_zonebirth_outcome(
                        row, path_bars, market_turns, fill_anchors
                    )
                )
            else:
                rows.append(
                    turn_regular_outcome(
                        row,
                        path_bars,
                        market_turns,
                        fill_anchors,
                        policy_override,
                    )
                )
        elif row.Side == "Short" and row.ResearchPath == "ZoneBirthResearch":
            result = zonebirth_outcome(row, path_bars)
            result["FillAnchorUsed"] = False
            rows.append(result)
        else:
            result = regular_outcome(row, path_bars)
            result["FillAnchorUsed"] = False
            rows.append(result)
    return pd.DataFrame(rows)


def cleanup_delay_table(
    trades: pd.DataFrame, events: pd.DataFrame, outcomes: pd.DataFrame
):
    cleanup = (
        events[events["Event"].eq("PROTECTION_CLEANUP_DONE")]
        .groupby(["SnapshotID", "TradeID"], as_index=False)["Time"]
        .max()
        .rename(columns={"Time": "CleanupTime"})
    )
    joined = trades.merge(cleanup, on=["SnapshotID", "TradeID"], how="left")
    joined = joined.merge(
        outcomes[[*KEY, "PredictedExitTime", "PredictedExitRole"]],
        on=KEY,
        how="left",
        validate="one_to_one",
    )
    joined["CleanupDelayMinutes"] = (
        joined["CleanupTime"] - joined["ExitTime"]
    ).dt.total_seconds().div(60).clip(lower=0)
    joined["OccupancyAdjustmentMinutes"] = (
        joined["CleanupTime"] - joined["PredictedExitTime"]
    ).dt.total_seconds().div(60).clip(lower=0)
    medians = (
        joined.groupby(["ResearchPath", "PredictedExitRole"])["OccupancyAdjustmentMinutes"]
        .median()
        .to_dict()
    )
    path_medians = (
        joined.groupby("ResearchPath")["OccupancyAdjustmentMinutes"].median().to_dict()
    )
    return joined, medians, path_medians


def abnormal_occupancy(events: pd.DataFrame) -> dict:
    abnormal = events[
        events["Event"].isin(
            {
                "ENTRY_FILLED_RISK_EXCEEDED",
                "ENTRY_FILLED_STRICT_RISK_BAND_EXCLUDED_V208",
            }
        )
    ][[*KEY, "TradeID"]].drop_duplicates()
    cleanup = (
        events[events["Event"].eq("PROTECTION_CLEANUP_DONE")]
        .groupby(["SnapshotID", "TradeID"], as_index=False)["Time"]
        .max()
        .rename(columns={"Time": "CleanupTime"})
    )
    joined = abnormal.merge(cleanup, on=["SnapshotID", "TradeID"], how="left")
    return {
        (row.SnapshotID, row.SignalID, row.ResearchPath): row.CleanupTime
        for row in joined.itertuples(index=False)
        if not pd.isna(row.CleanupTime)
    }


def simulate_constraints(
    candidates: pd.DataFrame,
    outcomes: pd.DataFrame,
    cleanup_medians: dict,
    path_cleanup_medians: dict,
    abnormal_until: dict,
):
    tape = candidates.merge(outcomes, on=KEY, validate="one_to_one")
    accepted = []
    for snapshot_id, day in tape.groupby("SnapshotID", sort=False):
        active_until = None
        active_net = 0.0
        active_realized = True
        normal_count = 0
        realized_net = 0.0
        for row in day.sort_values(["EntryTime", "SourceSequence"]).itertuples(index=False):
            same_time_released = (
                row.EntryTime == active_until and row.ReasonHead != "ActiveTrade"
            )
            if active_until is not None and (
                row.EntryTime > active_until or same_time_released
            ):
                if not active_realized:
                    realized_net += active_net
                active_until = None
                active_net = 0.0
                active_realized = True

            if active_until is not None or normal_count >= DAILY_CAP or realized_net <= -DAILY_LOSS:
                continue

            key = (row.SnapshotID, row.SignalID, row.ResearchPath)
            if key in abnormal_until:
                active_until = abnormal_until[key]
                active_net = 0.0
                active_realized = True
                continue

            trade_id = "" if pd.isna(row.OriginalTradeID) else str(row.OriginalTradeID)
            delay = cleanup_medians.get(
                (row.ResearchPath, row.PredictedExitRole),
                path_cleanup_medians.get(row.ResearchPath, 0.0),
            )
            active_until = row.PredictedExitTime + pd.Timedelta(minutes=float(delay))
            active_net = float(row.PredictedNet)
            active_realized = False
            normal_count += 1
            accepted.append(
                {
                    "SnapshotID": snapshot_id,
                    "SignalID": row.SignalID,
                    "ResearchPath": row.ResearchPath,
                    "TradeID": trade_id,
                    "Classification": "Normal",
                    "PredictedExitRole": row.PredictedExitRole,
                    "PredictedGross": row.PredictedGross,
                    "PredictedNet": row.PredictedNet,
                }
            )
    return pd.DataFrame(accepted)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--calibration-evidence", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument(
        "--gate",
        choices=("development", "validation"),
        default="development",
    )
    args = parser.parse_args()

    calibration, bars, turns, decisions, trades, _, events = load_evidence(
        args.evidence, require_turns=True
    )
    joined = calibration.merge(
        decisions[
            [
                "SnapshotID",
                "SignalID",
                "ResearchPath",
                "Time",
                "Decision",
                "ReasonHead",
                "SourceSequence",
            ]
        ],
        left_on=[*KEY, "EntryTime"],
        right_on=[*KEY, "Time"],
        validate="one_to_one",
    )
    candidates = joined[
        joined["Decision"].eq("Execute")
        | joined["ReasonHead"].isin(DYNAMIC_REASON_HEADS)
    ].copy()
    outcomes = predict_outcomes(calibration, bars, turns, trades)
    (
        calibration_source,
        calibration_bars,
        calibration_turns,
        _,
        calibration_trades,
        _,
        calibration_events,
    ) = load_evidence(args.calibration_evidence)
    calibration_outcomes = predict_outcomes(
        calibration_source,
        calibration_bars,
        calibration_turns,
        calibration_trades,
    )
    cleanup, cleanup_medians, path_cleanup_medians = cleanup_delay_table(
        calibration_trades, calibration_events, calibration_outcomes
    )
    simulated = simulate_constraints(
        candidates,
        outcomes,
        cleanup_medians,
        path_cleanup_medians,
        abnormal_occupancy(events),
    )

    actual_keys = set(zip(trades["SnapshotID"], trades["SignalID"], trades["ResearchPath"]))
    simulated_normal = simulated[simulated["Classification"].eq("Normal")]
    simulated_keys = set(
        zip(
            simulated_normal["SnapshotID"],
            simulated_normal["SignalID"],
            simulated_normal["ResearchPath"],
        )
    )
    identity_matched = len(actual_keys & simulated_keys)

    actual_daily_counts = trades.groupby("SnapshotID").size()
    simulated_daily_counts = simulated_normal.groupby("SnapshotID").size()
    daily_counts = pd.DataFrame(
        {
            "ActualTrades": actual_daily_counts,
            "SimulatedTrades": simulated_daily_counts,
        }
    ).fillna(0).astype(int)
    daily_counts["TradeCountError"] = (
        daily_counts["SimulatedTrades"] - daily_counts["ActualTrades"]
    )
    max_daily_count_error = int(daily_counts["TradeCountError"].abs().max())

    blind_actual = trades.merge(outcomes, on=KEY, validate="one_to_one")
    role_match = blind_actual["ExitRole"].eq(blind_actual["PredictedExitRole"])
    actual_gross = float(blind_actual["Dollars"].sum())
    predicted_gross = float(blind_actual["PredictedGross"].sum())
    gross_delta = predicted_gross - actual_gross
    relative_gross_error = abs(gross_delta) / max(1.0, abs(actual_gross))
    gross_scale_denominator = float(
        blind_actual.groupby("SnapshotID")["Dollars"].sum().abs().sum()
    )
    scale_relative_gross_error = abs(gross_delta) / max(
        1.0, gross_scale_denominator
    )
    daily_gross = blind_actual.groupby("SnapshotID").agg(
        ActualGross=("Dollars", "sum"),
        PredictedGross=("PredictedGross", "sum"),
    )
    daily_gross["AbsGrossError"] = (
        daily_gross["PredictedGross"] - daily_gross["ActualGross"]
    ).abs()
    snapshot_count = max(1, len(daily_gross))
    gross_abs_error_per_day = abs(gross_delta) / snapshot_count
    median_abs_daily_gross_error = float(daily_gross["AbsGrossError"].median())
    max_abs_daily_gross_error = float(daily_gross["AbsGrossError"].max())

    entry_trade_ids = set(
        events.loc[events["Event"].eq("ENTRY_SEND"), "TradeID"].dropna().astype(str)
    )
    cleanup_trade_ids = set(
        events.loc[
            events["Event"].eq("PROTECTION_CLEANUP_DONE"), "TradeID"
        ].dropna().astype(str)
    )
    actual_trade_ids = set(trades["TradeID"].dropna().astype(str))
    lifecycle_defects = len(actual_trade_ids - entry_trade_ids) + len(
        actual_trade_ids - cleanup_trade_ids
    )

    if args.gate == "development":
        identity_min = DEVELOPMENT_IDENTITY_MIN
        role_min = DEVELOPMENT_ROLE_MIN
        gross_abs_max = DEVELOPMENT_GROSS_ABS_ERROR_MAX
        gross_rel_max = DEVELOPMENT_GROSS_REL_ERROR_MAX
    else:
        identity_min = STAGE_C_IDENTITY_MIN
        role_min = STAGE_C_IDENTITY_MIN
        gross_abs_max = STAGE_C_GROSS_ABS_ERROR_MAX
        gross_rel_max = STAGE_C_GROSS_REL_ERROR_MAX
    gross_abs_gate_value = (
        abs(gross_delta)
        if args.gate == "development"
        else gross_abs_error_per_day
    )

    summary = pd.DataFrame(
        [
            {
                "ActualNormalTrades": len(actual_keys),
                "SimulatedNormalTrades": len(simulated_keys),
                "IdentityMatched": identity_matched,
                "IdentityMatchPct": round(100.0 * identity_matched / max(1, len(actual_keys)), 2),
                "BlindExitRoleMatchPct": round(float(role_match.mean() * 100), 2),
                "ActualGross": round(actual_gross, 2),
                "BlindPredictedGross": round(predicted_gross, 2),
                "GrossDelta": round(gross_delta, 2),
                "GrossAbsErrorPerDay": round(gross_abs_error_per_day, 2),
                "MedianAbsDailyGrossError": round(median_abs_daily_gross_error, 2),
                "MaxAbsDailyGrossError": round(max_abs_daily_gross_error, 2),
                "RelativeGrossErrorPct": round(relative_gross_error * 100, 2),
                "GrossScaleDenominator": round(gross_scale_denominator, 2),
                "ScaleRelativeGrossErrorPct": round(
                    scale_relative_gross_error * 100, 2
                ),
                "MaxDailyTradeCountError": max_daily_count_error,
                "PathEndFallbacks": int(outcomes["PathEndFallback"].sum()),
                "FillAnchoredCandidates": int(outcomes["FillAnchorUsed"].sum()),
                "LifecycleDefects": lifecycle_defects,
                "GateProfile": args.gate,
                "ConnectorGatePassed": bool(
                    identity_matched / max(1, len(actual_keys)) >= identity_min
                    and float(role_match.mean()) >= role_min
                    and max_daily_count_error <= STAGE_C_DAILY_COUNT_ERROR_MAX
                    and gross_abs_gate_value <= gross_abs_max
                    and scale_relative_gross_error <= gross_rel_max
                    and lifecycle_defects == 0
                ),
            }
        ]
    )

    args.output_dir.mkdir(parents=True, exist_ok=True)
    outcomes.to_csv(args.output_dir / "candidate_blind_outcomes.csv", index=False)
    blind_actual.to_csv(args.output_dir / "actual_blind_outcome_comparison.csv", index=False)
    cleanup.to_csv(args.output_dir / "calibration_cleanup_delays.csv", index=False)
    simulated.to_csv(args.output_dir / "constraint_simulated_trades.csv", index=False)
    daily_counts.reset_index().to_csv(
        args.output_dir / "daily_trade_count_validation.csv", index=False
    )
    summary.to_csv(args.output_dir / "connector_gate.csv", index=False)
    print(summary.to_string(index=False))


if __name__ == "__main__":
    main()
