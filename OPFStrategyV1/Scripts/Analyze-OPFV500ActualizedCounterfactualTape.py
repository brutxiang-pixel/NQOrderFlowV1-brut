#!/usr/bin/env python3
"""Create a conservative counterfactual tape from Actual outcomes only."""

from __future__ import annotations

import argparse
import importlib.util
from pathlib import Path

import pandas as pd


BASELINE_SCRIPT = Path(__file__).with_name("Analyze-OPFV500CrossYearBaseline.py")
BASELINE_SPEC = importlib.util.spec_from_file_location("v500_baseline", BASELINE_SCRIPT)
BASELINE = importlib.util.module_from_spec(BASELINE_SPEC)
BASELINE_SPEC.loader.exec_module(BASELINE)

TARGET_PATH = "ZoneBirthResearch"
TARGET_SIDE = "Short"
TARGET_SETUP_QUALITY = 45.0
GATE_REASONS = r"^(DailyTradeLimit|LiveDailyLoss|DailyLoss):"


def target_trades(baseline: pd.DataFrame) -> pd.DataFrame:
    return baseline.loc[
        baseline["ResearchPath"].eq(TARGET_PATH)
        & baseline["Side"].eq(TARGET_SIDE)
        & baseline["SetupQualityScore"].eq(TARGET_SETUP_QUALITY)
    ].copy()


def build_exposure_tape(baseline: pd.DataFrame, decisions: pd.DataFrame) -> pd.DataFrame:
    targets = target_trades(baseline).loc[:, ["SnapshotID", "TradeID", "EntryTime", "TrainingSegment"]].copy()
    targets["EntryTime"] = pd.to_datetime(targets["EntryTime"])
    decisions = decisions.copy()
    decisions["Time"] = pd.to_datetime(decisions["Time"])
    rows = []
    for decision in decisions.itertuples(index=False):
        prior_targets = targets.loc[
            targets["SnapshotID"].eq(decision.SnapshotID) & targets["EntryTime"].lt(decision.Time)
        ]
        if prior_targets.empty:
            continue
        reason = str(decision.Reason)
        active_trade_id = reason.split(":", 1)[1].split("|", 1)[0] if reason.startswith("ActiveTrade:") else ""
        if active_trade_id in set(prior_targets["TradeID"]):
            exposure = "ActiveTradeExposure"
        elif pd.Series([reason]).str.match(GATE_REASONS, na=False).iloc[0]:
            exposure = "GateExposure"
        else:
            continue
        rows.append({
            "SnapshotID": decision.SnapshotID,
            "SignalID": decision.SignalID,
            "DecisionTime": decision.Time,
            "Decision": decision.Decision,
            "Reason": reason,
            "ResearchPath": decision.ResearchPath,
            "Side": decision.Side,
            "ExposureClass": exposure,
            "PriorDisabledTargetCount": int(len(prior_targets)),
            "TrainingSegment": prior_targets.iloc[0]["TrainingSegment"],
        })
    return pd.DataFrame(rows)


def simulate_frozen_gates(baseline: pd.DataFrame) -> dict:
    targets = target_trades(baseline)
    baseline_net = float(baseline["NetPnLDollars"].sum())
    removed_net = float(targets["NetPnLDollars"].sum())
    return {
        "Scenario": "FrozenGatesNoReplacement",
        "BaselineTrades": int(len(baseline)),
        "RemovedTrades": int(len(targets)),
        "BaselineNetDollars": round(baseline_net, 2),
        "RemovedNetDollars": round(removed_net, 2),
        "FrozenGateNetDollars": round(baseline_net - removed_net, 2),
        "DirectNetDelta": round(-removed_net, 2),
    }


def _read_decisions(archive_root: Path) -> pd.DataFrame:
    parts = []
    columns = ["SnapshotID", "TradeID", "SignalID", "Time", "Decision", "Reason", "ResearchPath", "Side", "SetupQualityScore"]
    for archive in BASELINE.DEFAULT_ARCHIVES:
        directory = archive_root / archive
        for path in sorted(directory.rglob("*_execution_decisions.csv")):
            frame = pd.read_csv(path, usecols=columns)
            frame["ArchiveName"] = archive
            parts.append(frame)
    if not parts:
        raise FileNotFoundError("No execution-decision files found")
    decisions = pd.concat(parts, ignore_index=True)
    decisions["EntryTime"] = decisions["Time"]
    decisions["Classification"] = "Normal"
    decisions = BASELINE.filter_training_rows(decisions).drop(columns=["EntryTime", "Classification"])
    return decisions.drop_duplicates(["SnapshotID", "SignalID", "Time", "ResearchPath", "Side"], keep="last")


def summarize_exposures(tape: pd.DataFrame) -> pd.DataFrame:
    if tape.empty:
        return pd.DataFrame(columns=["TrainingSegment", "ExposureClass", "Candidates"])
    return (
        tape.groupby(["TrainingSegment", "ExposureClass"], sort=True)
        .size().rename("Candidates").reset_index()
    )


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--archive-root", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    args = parser.parse_args()
    decisions = _read_decisions(args.archive_root)
    baseline = BASELINE.load_baseline(args.archive_root)
    actual_features = decisions.loc[decisions["TradeID"].notna() & decisions["TradeID"].ne("")].drop_duplicates(
        ["SnapshotID", "TradeID"], keep="last"
    )
    baseline = baseline.merge(
        actual_features.loc[:, ["SnapshotID", "TradeID", "SetupQualityScore"]],
        on=["SnapshotID", "TradeID"], how="left", validate="one_to_one",
    )
    if baseline["SetupQualityScore"].isna().any():
        raise ValueError("Actual baseline trade missing setup-quality join")
    tape = build_exposure_tape(baseline, decisions)
    scenario = simulate_frozen_gates(baseline)
    args.output_dir.mkdir(parents=True, exist_ok=True)
    baseline.to_csv(args.output_dir / "v500_actualized_baseline_trades.csv", index=False, encoding="utf-8")
    tape.to_csv(args.output_dir / "v500_zonebirth_short_exposure_tape.csv", index=False, encoding="utf-8")
    pd.DataFrame([scenario]).to_csv(args.output_dir / "v500_zonebirth_short_frozen_gate_scenario.csv", index=False, encoding="utf-8")
    summarize_exposures(tape).to_csv(args.output_dir / "v500_zonebirth_short_exposure_summary.csv", index=False, encoding="utf-8")
    print(pd.DataFrame([scenario]).to_string(index=False))
    print(summarize_exposures(tape).to_string(index=False))


if __name__ == "__main__":
    main()
