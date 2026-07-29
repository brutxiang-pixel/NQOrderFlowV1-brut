import argparse
import importlib.util
from pathlib import Path

import pandas as pd


def load_module(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def read_actual_runs(roots: list[Path]) -> pd.DataFrame:
    parts = []
    for root in roots:
        for path in sorted(root.glob("*_execution_trades.csv")):
            rows = pd.read_csv(path, low_memory=False)
            if rows.empty:
                continue
            rows["EntryTime"] = pd.to_datetime(rows["EntryTime"])
            rows["ExitTime"] = pd.to_datetime(rows["ExitTime"])
            rows["TradingDate"] = rows["EntryTime"].max().normalize()
            rows["Run"] = path.name.removesuffix("_execution_trades.csv")
            rows["EvidenceRoot"] = root.name
            parts.append(rows)
    if not parts:
        raise FileNotFoundError("No execution trade files found in actual roots")
    return pd.concat(parts, ignore_index=True)


def cleanup_delays(roots: list[Path]) -> pd.DataFrame:
    parts = []
    for root in roots:
        event_by_run = {
            path.name.removesuffix("_execution_events.csv"): path
            for path in root.glob("*_execution_events.csv")
        }
        for trade_path in sorted(root.glob("*_execution_trades.csv")):
            run = trade_path.name.removesuffix("_execution_trades.csv")
            event_path = event_by_run.get(run)
            if event_path is None:
                continue
            trades = pd.read_csv(
                trade_path,
                usecols=["SnapshotID", "TradeID", "EntryTime", "ExitTime"],
                low_memory=False,
            )
            events = pd.read_csv(
                event_path,
                usecols=["SnapshotID", "TradeID", "Time", "Event"],
                low_memory=False,
            )
            trades["EntryTime"] = pd.to_datetime(trades["EntryTime"])
            trades["ExitTime"] = pd.to_datetime(trades["ExitTime"])
            events["Time"] = pd.to_datetime(events["Time"])
            cleanup = (
                events[events["Event"].eq("PROTECTION_CLEANUP_DONE")]
                .groupby(["SnapshotID", "TradeID"], as_index=False)["Time"]
                .max()
                .rename(columns={"Time": "CleanupDoneTime"})
            )
            joined = trades.merge(
                cleanup, on=["SnapshotID", "TradeID"], how="left", validate="many_to_one"
            )
            joined["CleanupDelayMinutes"] = (
                joined["CleanupDoneTime"] - joined["ExitTime"]
            ).dt.total_seconds() / 60.0
            joined["Run"] = run
            joined["EvidenceRoot"] = root.name
            parts.append(joined)
    return pd.concat(parts, ignore_index=True) if parts else pd.DataFrame()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--tape-evidence", type=Path, required=True)
    parser.add_argument("--sim-trades", type=Path, required=True)
    parser.add_argument("--actual-root", type=Path, action="append", required=True)
    parser.add_argument("--rich", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    args = parser.parse_args()

    scripts = Path(__file__).parent
    engine = load_module(
        "opf_v209_calibration_audit_engine",
        scripts / "Analyze-OPFDecisionTapePortfolio.py",
    )
    rich_module = load_module(
        "opf_v209_calibration_audit_rich",
        scripts / "Analyze-OPFV209H1ExecutionCalibratedPortfolio.py",
    )

    tape = engine.load_decision_tape(args.tape_evidence, "H1")
    snapshot_dates = (
        tape.groupby("SnapshotID")["TradingDate"]
        .max()
        .dt.strftime("%Y-%m-%d")
        .to_dict()
    )
    tape_keys = set(
        zip(
            pd.to_datetime(tape["TradingDate"]).dt.strftime("%Y-%m-%d"),
            tape["SignalID"],
            tape["ResearchPath"],
        )
    )
    raw_decisions = engine.read_snapshot_csvs(args.tape_evidence, "execution_decisions")
    raw_decisions["TradingDateText"] = raw_decisions["SnapshotID"].map(snapshot_dates)
    raw_decisions = raw_decisions[raw_decisions["TradingDateText"].notna()].copy()
    raw_reason_map = (
        raw_decisions.groupby(["TradingDateText", "SignalID", "ResearchPath"])["Reason"]
        .apply(lambda values: " || ".join(sorted(set(values.fillna("").astype(str)))))
        .to_dict()
    )
    raw_decision_keys = set(raw_reason_map)
    shadows = engine.read_snapshot_csvs(args.tape_evidence, "shadow_trades")
    shadow_dates = shadows["SnapshotID"].map(snapshot_dates)
    shadow_keys = set(zip(shadow_dates, shadows["SignalID"], shadows["ResearchPath"]))
    sim = pd.read_csv(args.sim_trades, low_memory=False)
    sim["TradingDate"] = pd.to_datetime(sim["TradingDate"])
    sim["EntryTime"] = pd.to_datetime(sim["EntryTime"])
    actual = read_actual_runs(args.actual_root)

    identity_rows = []
    run_rows = []
    for run, run_trades in actual.groupby("Run", sort=True):
        date = run_trades["TradingDate"].iloc[0]
        day_sim = sim[sim["TradingDate"].eq(date)]
        sim_keys = set(zip(day_sim["SignalID"], day_sim["ResearchPath"]))
        actual_keys = set(zip(run_trades["SignalID"], run_trades["ResearchPath"]))
        matched = actual_keys & sim_keys
        date_text = date.strftime("%Y-%m-%d")
        missing_source = 0
        for row in run_trades.itertuples(index=False):
            selected = (row.SignalID, row.ResearchPath) in sim_keys
            source_key = (date_text, row.SignalID, row.ResearchPath)
            source_covered = source_key in tape_keys
            raw_decision_present = source_key in raw_decision_keys
            shadow_available = source_key in shadow_keys
            missing_source += int(not source_covered)
            identity_rows.append(
                {
                    "Run": run,
                    "TradingDate": date_text,
                    "SignalID": row.SignalID,
                    "TradeID": row.TradeID,
                    "Side": row.Side,
                    "ResearchPath": row.ResearchPath,
                    "ActualGross": float(row.Dollars),
                    "SelectedBySimulation": selected,
                    "CoveredByOldTape": source_covered,
                    "RawDecisionPresent": raw_decision_present,
                    "ShadowOutcomeAvailable": shadow_available,
                    "OldDecisionReasons": raw_reason_map.get(source_key, ""),
                }
            )
        sim_gross = float(day_sim.loc[day_sim["CountsNormal"].astype(str).eq("True"), "Gross3"].sum())
        actual_gross = float(run_trades["Dollars"].sum())
        run_rows.append(
            {
                "Run": run,
                "TradingDate": date_text,
                "SimulationTrades": int(day_sim["CountsNormal"].astype(str).eq("True").sum()),
                "ActualNormalTrades": len(run_trades),
                "IdentityMatched": len(matched),
                "IdentityMatchPct": round(100.0 * len(matched) / max(1, len(actual_keys)), 2),
                "OldTapeMissingActualCandidates": missing_source,
                "SimulationGross": round(sim_gross, 2),
                "ActualGross": round(actual_gross, 2),
                "GrossDelta": round(actual_gross - sim_gross, 2),
            }
        )

    bars = rich_module.bars_by_trading_date(args.rich)
    first_bar = {key: value["Time"].min() for key, value in bars.items()}
    sim["FirstRichBar"] = sim["TradingDate"].dt.strftime("%Y-%m-%d").map(first_bar)
    coverage = sim[sim["EntryTime"].lt(sim["FirstRichBar"])].copy()
    coverage = coverage[
        [
            "TradingDate",
            "EntryTime",
            "FirstRichBar",
            "SignalID",
            "Side",
            "ResearchPath",
            "Gross3",
            "OutcomeSource",
        ]
    ]

    run_summary = pd.DataFrame(run_rows)
    identity = pd.DataFrame(identity_rows)
    delays = cleanup_delays(args.actual_root)
    gate = pd.DataFrame(
        [
            {
                "ActualRuns": len(run_summary),
                "UniqueTradingDates": run_summary["TradingDate"].nunique(),
                "ActualNormalTrades": int(run_summary["ActualNormalTrades"].sum()),
                "IdentityMatched": int(run_summary["IdentityMatched"].sum()),
                "OldTapeMissingActualCandidates": int(
                    run_summary["OldTapeMissingActualCandidates"].sum()
                ),
                "RawDecisionOnlyActualCandidates": int(
                    (
                        ~identity["CoveredByOldTape"]
                        & identity["RawDecisionPresent"]
                        & ~identity["ShadowOutcomeAvailable"]
                    ).sum()
                ),
                "SimulationGross": round(float(run_summary["SimulationGross"].sum()), 2),
                "ActualGross": round(float(run_summary["ActualGross"].sum()), 2),
                "GrossDelta": round(float(run_summary["GrossDelta"].sum()), 2),
                "RichCoverageGapTrades": len(coverage),
                "CalibrationGatePassed": bool(
                    run_summary["OldTapeMissingActualCandidates"].sum() == 0
                    and abs(run_summary["GrossDelta"].sum())
                    <= max(100.0, abs(run_summary["SimulationGross"].sum()) * 0.10)
                ),
            }
        ]
    )

    args.output_dir.mkdir(parents=True, exist_ok=True)
    run_summary.to_csv(args.output_dir / "actual_run_calibration.csv", index=False)
    identity.to_csv(args.output_dir / "actual_identity_coverage.csv", index=False)
    coverage.to_csv(args.output_dir / "rich_coverage_gaps.csv", index=False)
    delays.to_csv(args.output_dir / "cleanup_delays.csv", index=False)
    gate.to_csv(args.output_dir / "calibration_gate.csv", index=False)
    print(run_summary.to_string(index=False))
    print("\n", gate.to_string(index=False))


if __name__ == "__main__":
    main()
