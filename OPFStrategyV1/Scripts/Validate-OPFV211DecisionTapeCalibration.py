import argparse
from pathlib import Path

import pandas as pd


DECISION_KEY = ["SnapshotID", "SignalID", "ResearchPath", "Time"]
TRACKER_KEY = ["SnapshotID", "SignalID", "ResearchPath", "EntryBar"]
BAR_KEY = [*TRACKER_KEY, "Bar"]
PRE_CAPTURE_REASON_HEADS = {
    "StrategyStopping",
    "GlobexCloseoutLock",
    "UsCashOpenBlackout",
    "LiveLatencyBlocked",
    "AggressiveExpansionWait1ExpiredV165",
    "AggressiveExpansionWait1LongFollowThroughFailedV165",
    "AggressiveExpansionWait1ShortFollowThroughFailedV165",
    "AggressiveExpansionWait1ConfirmBarStopInvalidV166",
    "AggressiveExpansionWait1InvalidConfirmBarRiskV166",
    "AggressiveExpansionWait1InvalidStopV166",
}


def read_csvs(root: Path, suffix: str) -> pd.DataFrame:
    files = sorted(root.glob(f"*_{suffix}.csv"))
    if not files:
        raise FileNotFoundError(f"No *_{suffix}.csv files in {root}")
    return pd.concat((pd.read_csv(path, low_memory=False) for path in files), ignore_index=True)


def reason_head(value) -> str:
    text = "" if pd.isna(value) else str(value)
    return text.split(":", 1)[0].split("|", 1)[0]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--expected-research-version", default="OPF_RESEARCH_2.11")
    parser.add_argument("--require-entry-quotes", action="store_true")
    parser.add_argument("--require-market-turns", action="store_true")
    args = parser.parse_args()

    decisions = read_csvs(args.evidence, "execution_decisions")
    calibration = read_csvs(args.evidence, "decision_tape_calibration")
    bars = read_csvs(args.evidence, "decision_tape_calibration_bars")
    events = read_csvs(args.evidence, "execution_events")
    trades = read_csvs(args.evidence, "execution_trades")
    turns = (
        read_csvs(args.evidence, "decision_tape_market_turns")
        if args.require_market_turns
        else pd.DataFrame()
    )

    decisions["Time"] = pd.to_datetime(decisions["Time"])
    calibration["Time"] = pd.to_datetime(calibration["EntryTime"])
    calibration["ResolveTime"] = pd.to_datetime(calibration["ResolveTime"])
    bars["Time"] = pd.to_datetime(bars["Time"])
    decisions["ReasonHead"] = decisions["Reason"].map(reason_head)

    expected = decisions[~decisions["ReasonHead"].isin(PRE_CAPTURE_REASON_HEADS)].copy()
    expected = expected.drop_duplicates(DECISION_KEY, keep="last")
    calibration_unique = calibration.drop_duplicates(DECISION_KEY, keep="last")
    duplicate_calibration = len(calibration) - len(calibration_unique)

    joined = expected.merge(
        calibration_unique,
        on=DECISION_KEY,
        how="outer",
        suffixes=("Decision", "Calibration"),
        indicator=True,
    )
    matched = joined[joined["_merge"].eq("both")]
    decision_match = matched["Decision"].fillna("").eq(matched["OriginalDecision"].fillna(""))
    reason_match = matched["Reason"].fillna("").eq(matched["OriginalReason"].fillna(""))

    execute = expected[expected["Decision"].eq("Execute")].copy()
    execute_join = execute.merge(
        calibration_unique,
        on=DECISION_KEY,
        how="left",
        suffixes=("Decision", "Calibration"),
        indicator=True,
    )
    execute_covered = execute_join["_merge"].eq("both")
    trade_id_match = execute_join["TradeID"].fillna("").eq(
        execute_join["OriginalTradeID"].fillna("")
    )

    quote_valid = calibration_unique.get(
        "EntryQuoteValid", pd.Series(False, index=calibration_unique.index)
    ).astype(str).str.lower().eq("true")
    valid_quote_prices = (
        pd.to_numeric(calibration_unique.get("EntryBid"), errors="coerce").gt(0)
        & pd.to_numeric(calibration_unique.get("EntryAsk"), errors="coerce").gt(0)
    ) if "EntryBid" in calibration_unique and "EntryAsk" in calibration_unique else pd.Series(False, index=calibration_unique.index)

    preflight = events[events["Event"].eq("ENTRY_QUOTE_PREFLIGHT_OK_V178")].copy()
    extracted = preflight["Message"].fillna("").str.extract(
        r"bid=([0-9.]+)\|ask=([0-9.]+)"
    )
    preflight["PreflightBid"] = pd.to_numeric(extracted[0], errors="coerce")
    preflight["PreflightAsk"] = pd.to_numeric(extracted[1], errors="coerce")
    preflight["PreflightMarketSequence"] = pd.to_numeric(
        preflight["Message"].fillna("").str.extract(r"marketSequence=([0-9]+)")[0],
        errors="coerce",
    )
    preflight["Time"] = pd.to_datetime(preflight["Time"])
    execute_quotes = execute_join.merge(
        preflight[["SnapshotID", "SignalID", "ResearchPath", "Time", "PreflightBid", "PreflightAsk", "PreflightMarketSequence"]],
        on=DECISION_KEY,
        how="left",
        validate="one_to_one",
    )
    execute_quote_covered = execute_quotes["PreflightBid"].notna() & execute_quotes["PreflightAsk"].notna()
    execute_bid = (
        pd.to_numeric(execute_quotes["EntryBid"], errors="coerce")
        if "EntryBid" in execute_quotes
        else pd.Series(float("nan"), index=execute_quotes.index)
    )
    execute_ask = (
        pd.to_numeric(execute_quotes["EntryAsk"], errors="coerce")
        if "EntryAsk" in execute_quotes
        else pd.Series(float("nan"), index=execute_quotes.index)
    )
    execute_quote_match = (
        execute_bid.eq(execute_quotes["PreflightBid"])
        & execute_ask.eq(execute_quotes["PreflightAsk"])
    )
    execute_sequence_match = (
        pd.to_numeric(execute_quotes.get("EntryMarketSequence"), errors="coerce")
        .eq(execute_quotes["PreflightMarketSequence"])
        if "EntryMarketSequence" in execute_quotes
        else pd.Series(False, index=execute_quotes.index)
    )

    entry_sequence = pd.to_numeric(
        calibration_unique.get("EntryMarketSequence"), errors="coerce"
    ) if "EntryMarketSequence" in calibration_unique else pd.Series(float("nan"), index=calibration_unique.index)
    resolve_sequence = pd.to_numeric(
        calibration_unique.get("ResolveMarketSequence"), errors="coerce"
    ) if "ResolveMarketSequence" in calibration_unique else pd.Series(float("nan"), index=calibration_unique.index)
    candidate_sequence_valid = entry_sequence.gt(0) & resolve_sequence.ge(entry_sequence)

    turn_rows = len(turns)
    duplicate_turn_sequences = 0
    non_increasing_turn_sequences = 0
    invalid_turn_prices = 0
    invalid_turn_kinds = 0
    candidate_turn_bounds_missing = len(calibration_unique) if args.require_market_turns else 0
    if not turns.empty:
        turns["Sequence"] = pd.to_numeric(turns["Sequence"], errors="coerce")
        turns["Price"] = pd.to_numeric(turns["Price"], errors="coerce")
        duplicate_turn_sequences = int(turns.duplicated(["SnapshotID", "Sequence"], keep=False).sum())
        non_increasing_turn_sequences = int(
            turns.groupby("SnapshotID")["Sequence"].apply(
                lambda values: (~values.diff().dropna().gt(0)).sum()
            ).sum()
        )
        invalid_turn_prices = int((~turns["Price"].gt(0)).sum())
        invalid_turn_kinds = int(
            (~turns["Kind"].isin({"Start", "BarStart", "BarEnd", "TurnHigh", "TurnLow", "End"})).sum()
        )
        bounds = turns.groupby("SnapshotID", as_index=False)["Sequence"].agg(
            MinTurnSequence="min", MaxTurnSequence="max"
        )
        candidate_bounds = calibration_unique.merge(bounds, on="SnapshotID", how="left")
        candidate_turn_bounds_missing = int(
            (
                candidate_bounds["MinTurnSequence"].isna()
                | pd.to_numeric(candidate_bounds["EntryMarketSequence"], errors="coerce").lt(candidate_bounds["MinTurnSequence"])
                | pd.to_numeric(candidate_bounds["ResolveMarketSequence"], errors="coerce").gt(candidate_bounds["MaxTurnSequence"])
            ).sum()
        )

    duplicate_bars = int(bars.duplicated(BAR_KEY, keep=False).sum())
    bars["ExpectedRelativeBar"] = bars["Bar"] - bars["EntryBar"]
    relative_bar_mismatch = int(
        pd.to_numeric(bars["RelativeBar"], errors="coerce")
        .ne(pd.to_numeric(bars["ExpectedRelativeBar"], errors="coerce"))
        .sum()
    )

    bar_groups = []
    for key, group in bars.groupby(TRACKER_KEY, sort=False):
        ordered = group.sort_values("Bar")
        relative = pd.to_numeric(ordered["RelativeBar"], errors="coerce")
        time_gaps = ordered["Time"].diff().dropna().dt.total_seconds().div(60)
        bar_groups.append(
            {
                **dict(zip(TRACKER_KEY, key)),
                "BarRows": len(ordered),
                "MinRelativeBar": relative.min(),
                "MaxRelativeBar": relative.max(),
                "RelativeBarContinuous": bool(relative.tolist() == list(range(len(ordered)))),
                "FiveMinuteContinuous": bool(time_gaps.eq(5).all()),
            }
        )
    bar_group_summary = pd.DataFrame(bar_groups)
    tracker_join = calibration_unique.merge(
        bar_group_summary,
        on=TRACKER_KEY,
        how="left",
        validate="one_to_one",
    )
    missing_bar_trackers = int(tracker_join["BarRows"].isna().sum())
    bar_count_mismatch = int(
        tracker_join["BarRows"].fillna(-1).ne(tracker_join["BarsTracked"] + 1).sum()
    )
    non_continuous_bars = int(
        (~tracker_join["RelativeBarContinuous"].fillna(False)).sum()
    )
    non_five_minute_paths = int(
        (~tracker_join["FiveMinuteContinuous"].fillna(False)).sum()
    )
    over_max_relative_bar = int(
        pd.to_numeric(bars["RelativeBar"], errors="coerce").gt(36).sum()
    )

    event_counts = events["Event"].value_counts()
    entry_send = int(event_counts.get("ENTRY_SEND", 0))
    cleanup_done = int(event_counts.get("PROTECTION_CLEANUP_DONE", 0))
    danger = events[
        events["Event"].str.contains(
            "ORDER_REGISTER_FAILED|ORDER_CANCEL_FAILED|ORDER_MODIFY_FAILED|DUPLICATE_EXIT_FILL|ORPHAN_POSITION|TRADE_ORDER_MISMATCH|PROTECTIVE_EXIT_CALLBACK_TIMEOUT",
            regex=True,
            na=False,
        )
    ]

    summary = {
        "Snapshots": calibration_unique["SnapshotID"].nunique(),
        "ResearchVersions": ";".join(sorted(calibration_unique["ResearchSchemaVersion"].astype(str).unique())),
        "ExpectedDecisionKeys": len(expected),
        "CalibrationRows": len(calibration_unique),
        "DuplicateCalibrationRows": duplicate_calibration,
        "MissingCalibrationKeys": int(joined["_merge"].eq("left_only").sum()),
        "CalibrationOnlyKeys": int(joined["_merge"].eq("right_only").sum()),
        "DecisionMatchPct": round(float(decision_match.mean() * 100), 4) if len(matched) else 0.0,
        "ReasonMatchPct": round(float(reason_match.mean() * 100), 4) if len(matched) else 0.0,
        "ExecuteRows": len(execute),
        "ExecuteCalibrationCoveragePct": round(float(execute_covered.mean() * 100), 4) if len(execute) else 100.0,
        "ExecuteTradeIdMatchPct": round(float(trade_id_match.mean() * 100), 4) if len(execute) else 100.0,
        "CalibrationQuoteValidPct": round(float((quote_valid & valid_quote_prices).mean() * 100), 4) if len(calibration_unique) else 100.0,
        "ExecutePreflightQuoteCoveragePct": round(float(execute_quote_covered.mean() * 100), 4) if len(execute) else 100.0,
        "ExecutePreflightQuoteMatchPct": round(float(execute_quote_match.mean() * 100), 4) if len(execute) else 100.0,
        "CandidateMarketSequenceValidPct": round(float(candidate_sequence_valid.mean() * 100), 4) if len(calibration_unique) else 100.0,
        "ExecutePreflightSequenceMatchPct": round(float(execute_sequence_match.mean() * 100), 4) if len(execute) else 100.0,
        "MarketTurnRows": turn_rows,
        "DuplicateMarketTurnSequences": duplicate_turn_sequences,
        "NonIncreasingMarketTurnSequences": non_increasing_turn_sequences,
        "InvalidMarketTurnPrices": invalid_turn_prices,
        "InvalidMarketTurnKinds": invalid_turn_kinds,
        "CandidateTurnBoundsMissing": candidate_turn_bounds_missing,
        "CalibrationBarRows": len(bars),
        "DuplicateCalibrationBarRows": duplicate_bars,
        "MissingBarTrackers": missing_bar_trackers,
        "BarCountMismatchTrackers": bar_count_mismatch,
        "RelativeBarMismatchRows": relative_bar_mismatch,
        "NonContinuousBarTrackers": non_continuous_bars,
        "NonFiveMinutePathTrackers": non_five_minute_paths,
        "Over36RelativeBarRows": over_max_relative_bar,
        "ExecutionTrades": len(trades),
        "EntrySend": entry_send,
        "CleanupDone": cleanup_done,
        "DangerEvents": len(danger),
    }
    summary["CollectionGatePassed"] = bool(
        summary["ResearchVersions"] == args.expected_research_version
        and summary["DuplicateCalibrationRows"] == 0
        and summary["MissingCalibrationKeys"] == 0
        and summary["CalibrationOnlyKeys"] == 0
        and summary["DecisionMatchPct"] == 100.0
        and summary["ReasonMatchPct"] == 100.0
        and summary["ExecuteCalibrationCoveragePct"] == 100.0
        and summary["ExecuteTradeIdMatchPct"] == 100.0
        and (
            not args.require_entry_quotes
            or (
                summary["CalibrationQuoteValidPct"] == 100.0
                and summary["ExecutePreflightQuoteCoveragePct"] == 100.0
                and summary["ExecutePreflightQuoteMatchPct"] == 100.0
            )
        )
        and (
            not args.require_market_turns
            or (
                summary["CandidateMarketSequenceValidPct"] == 100.0
                and summary["ExecutePreflightSequenceMatchPct"] == 100.0
                and summary["MarketTurnRows"] > 0
                and summary["DuplicateMarketTurnSequences"] == 0
                and summary["NonIncreasingMarketTurnSequences"] == 0
                and summary["InvalidMarketTurnPrices"] == 0
                and summary["InvalidMarketTurnKinds"] == 0
                and summary["CandidateTurnBoundsMissing"] == 0
            )
        )
        and summary["DuplicateCalibrationBarRows"] == 0
        and summary["MissingBarTrackers"] == 0
        and summary["BarCountMismatchTrackers"] == 0
        and summary["RelativeBarMismatchRows"] == 0
        and summary["NonContinuousBarTrackers"] == 0
        and summary["NonFiveMinutePathTrackers"] == 0
        and summary["Over36RelativeBarRows"] == 0
        and entry_send == cleanup_done
        and len(danger) == 0
    )

    args.output_dir.mkdir(parents=True, exist_ok=True)
    pd.DataFrame([summary]).to_csv(args.output_dir / "decision_tape_calibration_summary.csv", index=False)
    tracker_join.to_csv(args.output_dir / "decision_tape_calibration_bar_coverage.csv", index=False)
    joined.to_csv(args.output_dir / "decision_tape_calibration_join.csv", index=False)
    danger.to_csv(args.output_dir / "decision_tape_danger_events.csv", index=False)
    print(pd.Series(summary).to_string())


if __name__ == "__main__":
    main()
