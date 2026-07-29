import argparse
from pathlib import Path

import pandas as pd


KEY = ["SnapshotID", "SignalID", "ResearchPath", "Time"]
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
    args = parser.parse_args()

    decisions = read_csvs(args.evidence, "execution_decisions")
    calibration = read_csvs(args.evidence, "decision_tape_calibration")
    events = read_csvs(args.evidence, "execution_events")
    trades = read_csvs(args.evidence, "execution_trades")

    decisions["Time"] = pd.to_datetime(decisions["Time"])
    calibration["Time"] = pd.to_datetime(calibration["EntryTime"])
    calibration["ResolveTime"] = pd.to_datetime(calibration["ResolveTime"])
    calibration["ReasonHead"] = calibration["OriginalReason"].map(reason_head)
    decisions["ReasonHead"] = decisions["Reason"].map(reason_head)

    expected = decisions[~decisions["ReasonHead"].isin(PRE_CAPTURE_REASON_HEADS)].copy()
    expected = expected.drop_duplicates(KEY, keep="last")
    calibration_unique = calibration.drop_duplicates(KEY, keep="last")
    duplicate_rows = len(calibration) - len(calibration_unique)

    joined = expected.merge(
        calibration_unique,
        on=KEY,
        how="outer",
        suffixes=("Decision", "Calibration"),
        indicator=True,
    )
    matched = joined[joined["_merge"].eq("both")].copy()
    decision_match = matched["Decision"].fillna("").eq(
        matched["OriginalDecision"].fillna("")
    )
    reason_match = matched["Reason"].fillna("").eq(
        matched["OriginalReason"].fillna("")
    )

    execute = expected[expected["Decision"].eq("Execute")].copy()
    execute_join = execute.merge(
        calibration_unique,
        on=KEY,
        how="left",
        suffixes=("Decision", "Calibration"),
        indicator=True,
    )
    execute_covered = execute_join["_merge"].eq("both")
    trade_id_match = execute_join["TradeID"].fillna("").eq(
        execute_join["OriginalTradeID"].fillna("")
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

    missing_original = calibration_unique[
        calibration_unique["OriginalDecision"].fillna("").eq("")
    ]
    entry_bar_stop = calibration_unique[
        pd.to_numeric(calibration_unique["FirstStopBar"], errors="coerce").eq(
            pd.to_numeric(calibration_unique["EntryBar"], errors="coerce")
        )
    ]
    over_max_bars = calibration_unique[
        pd.to_numeric(calibration_unique["BarsTracked"], errors="coerce").gt(36)
    ]

    summary = {
        "Snapshots": calibration_unique["SnapshotID"].nunique(),
        "ResearchVersions": ";".join(sorted(calibration_unique["ResearchSchemaVersion"].astype(str).unique())),
        "ExpectedDecisionKeys": len(expected),
        "CalibrationRows": len(calibration_unique),
        "DuplicateCalibrationRows": duplicate_rows,
        "MatchedDecisionKeys": int(joined["_merge"].eq("both").sum()),
        "MissingCalibrationKeys": int(joined["_merge"].eq("left_only").sum()),
        "CalibrationOnlyKeys": int(joined["_merge"].eq("right_only").sum()),
        "DecisionMatchPct": round(float(decision_match.mean() * 100), 4) if len(matched) else 0.0,
        "ReasonMatchPct": round(float(reason_match.mean() * 100), 4) if len(matched) else 0.0,
        "ExecuteRows": len(execute),
        "ExecuteCalibrationCoveragePct": round(float(execute_covered.mean() * 100), 4) if len(execute) else 100.0,
        "ExecuteTradeIdMatchPct": round(float(trade_id_match.mean() * 100), 4) if len(execute) else 100.0,
        "MissingOriginalDecisionRows": len(missing_original),
        "EntryBarStopRows": len(entry_bar_stop),
        "Over36BarRows": len(over_max_bars),
        "ExecutionTrades": len(trades),
        "EntrySend": entry_send,
        "CleanupDone": cleanup_done,
        "DangerEvents": len(danger),
    }
    summary["CollectionGatePassed"] = bool(
        summary["ResearchVersions"] == "OPF_RESEARCH_2.10"
        and summary["DuplicateCalibrationRows"] == 0
        and summary["MissingCalibrationKeys"] == 0
        and summary["CalibrationOnlyKeys"] == 0
        and summary["DecisionMatchPct"] == 100.0
        and summary["ReasonMatchPct"] == 100.0
        and summary["ExecuteCalibrationCoveragePct"] == 100.0
        and summary["ExecuteTradeIdMatchPct"] == 100.0
        and summary["MissingOriginalDecisionRows"] == 0
        and summary["Over36BarRows"] == 0
        and entry_send == cleanup_done
        and len(danger) == 0
    )

    args.output_dir.mkdir(parents=True, exist_ok=True)
    pd.DataFrame([summary]).to_csv(
        args.output_dir / "decision_tape_calibration_summary.csv", index=False
    )
    joined.to_csv(args.output_dir / "decision_tape_calibration_join.csv", index=False)
    entry_bar_stop.to_csv(args.output_dir / "decision_tape_entry_bar_stops.csv", index=False)
    danger.to_csv(args.output_dir / "decision_tape_danger_events.csv", index=False)
    print(pd.Series(summary).to_string())


if __name__ == "__main__":
    main()
