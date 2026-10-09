#!/usr/bin/env python3
"""Calibrate frozen Actual outcomes against V4 candidate plans."""

from __future__ import annotations

import argparse
from pathlib import Path

import pandas as pd


KEY = ["SignalID", "ResearchPath"]
EXECUTION_COLUMNS = (
    "SnapshotID", "SignalID", "ResearchPath", "TradeID", "Side", "EntryPrice", "ExitPrice",
    "StopPrice", "FilledRiskPoints", "ActualMFEPoints", "ActualMAEPoints", "ExitRole", "RawDollars",
    "InitialRiskPoints", "PlannedRiskPoints", "PlannedTargetR", "TargetR", "IsAbnormalExecution",
)
ACCOUNT_COLUMNS = (
    "SnapshotID", "TradeID", "Classification", "GrossPnLDollars", "CommissionDollars", "NetPnLDollars",
)
CANDIDATE_COLUMNS = (
    "CandidateID", "SignalID", "ResearchPath", "Side", "PlannedEntry", "PlannedStop", "PlannedTarget",
    "PlannedRiskPoints", "PlannedTargetR",
)


def require_columns(frame: pd.DataFrame, columns: tuple[str, ...], source: str) -> None:
    missing = sorted(set(columns) - set(frame.columns))
    if missing:
        raise ValueError(f"Missing {source} columns: {missing}")


def load_archive(root: Path) -> tuple[pd.DataFrame, pd.DataFrame]:
    execution_paths = sorted(root.glob("*_execution_trades.csv"))
    account_paths = sorted(root.glob("*_live_account_pnl.csv"))
    if not execution_paths or not account_paths:
        raise FileNotFoundError("Frozen archive requires execution_trades and live_account_pnl files")
    executions = pd.concat((pd.read_csv(path) for path in execution_paths), ignore_index=True)
    accounts = pd.concat((pd.read_csv(path) for path in account_paths), ignore_index=True)
    return executions, accounts


def calibrate(
    executions: pd.DataFrame, accounts: pd.DataFrame, candidates: pd.DataFrame
) -> tuple[pd.DataFrame, dict[str, int]]:
    require_columns(executions, EXECUTION_COLUMNS, "execution")
    require_columns(accounts, ACCOUNT_COLUMNS, "account")
    require_columns(candidates, CANDIDATE_COLUMNS, "candidate")

    normal_execution = executions.loc[
        executions["IsAbnormalExecution"].astype(str).str.lower().eq("false"), EXECUTION_COLUMNS
    ].copy()
    normal_account = accounts.loc[
        accounts["Classification"].eq("Normal"), ACCOUNT_COLUMNS
    ].copy()
    if normal_execution.duplicated(["SnapshotID", "TradeID"]).any():
        raise ValueError("Duplicate normal execution trade identity")
    if normal_account.duplicated(["SnapshotID", "TradeID"]).any():
        raise ValueError("Duplicate normal account trade identity")
    if candidates.duplicated(KEY).any():
        raise ValueError("Duplicate Candidate key")

    actual = normal_execution.merge(
        normal_account,
        on=["SnapshotID", "TradeID"],
        how="left",
        validate="one_to_one",
        indicator="AccountJoin",
    )
    if actual["AccountJoin"].ne("both").any():
        raise ValueError("Normal Actual missing account PnL")
    replay_identity_columns = [column for column in actual.columns if column not in {"SnapshotID", "AccountJoin"}]
    duplicate_replay_rows = actual.duplicated(replay_identity_columns, keep="first")
    duplicate_replay_count = int(duplicate_replay_rows.sum())
    actual = actual.loc[~duplicate_replay_rows].copy()
    candidate_for_join = candidates.loc[:, CANDIDATE_COLUMNS].rename(columns={
        "Side": "CandidateSide", "PlannedEntry": "CandidatePlannedEntry",
        "PlannedStop": "CandidatePlannedStop", "PlannedTarget": "CandidatePlannedTarget",
        "PlannedRiskPoints": "CandidatePlannedRiskPoints", "PlannedTargetR": "CandidatePlannedTargetR",
    })
    detail = actual.merge(
        candidate_for_join,
        on=KEY,
        how="left",
        validate="one_to_one",
        indicator="CandidateJoin",
    )
    if detail["CandidateJoin"].ne("both").any():
        raise ValueError("Normal Actual missing Candidate")
    if detail["CandidateID"].duplicated().any():
        raise ValueError("Candidate joins more than one normal Actual")
    if detail["Side"].ne(detail["CandidateSide"]).any():
        raise ValueError("Actual and Candidate side mismatch")

    detail = detail.rename(columns={
        "EntryPrice": "ActualEntryPrice", "ExitPrice": "ActualExitPrice", "StopPrice": "ActualStopPrice",
        "RawDollars": "ActualRawDollars", "GrossPnLDollars": "ActualGrossPnLDollars",
        "CommissionDollars": "ActualCommissionDollars", "NetPnLDollars": "ActualNetPnLDollars",
        "InitialRiskPoints": "ActualInitialRiskPoints", "PlannedRiskPoints": "ActualPlannedRiskPoints",
        "PlannedTargetR": "ActualPlannedTargetR", "TargetR": "ActualConfiguredTargetR",
    })
    numeric = (
        "ActualEntryPrice", "ActualExitPrice", "ActualStopPrice", "FilledRiskPoints", "ActualMFEPoints",
        "ActualMAEPoints", "ActualRawDollars", "ActualGrossPnLDollars", "ActualCommissionDollars",
        "ActualNetPnLDollars", "ActualInitialRiskPoints", "ActualPlannedRiskPoints",
        "ActualPlannedTargetR", "ActualConfiguredTargetR", "CandidatePlannedEntry", "CandidatePlannedStop",
        "CandidatePlannedTarget", "CandidatePlannedRiskPoints", "CandidatePlannedTargetR",
    )
    for column in numeric:
        detail[column] = pd.to_numeric(detail[column], errors="raise")
    detail["EntryDeltaPoints"] = detail["ActualEntryPrice"] - detail["CandidatePlannedEntry"]
    detail["CandidatePlanActualPlanRiskDeltaPoints"] = (
        detail["CandidatePlannedRiskPoints"] - detail["ActualPlannedRiskPoints"]
    )
    detail["ActualPlanFilledRiskDeltaPoints"] = (
        detail["FilledRiskPoints"] - detail["ActualPlannedRiskPoints"]
    )
    detail["CandidatePlanFilledRiskDeltaPoints"] = (
        detail["FilledRiskPoints"] - detail["CandidatePlannedRiskPoints"]
    )
    detail["CandidateTargetRActualPlanDelta"] = (
        detail["CandidatePlannedTargetR"] - detail["ActualPlannedTargetR"]
    )
    detail["CandidateTargetAtActualFillR"] = (
        detail["CandidatePlannedTarget"] - detail["ActualEntryPrice"]
    ).abs() / detail["FilledRiskPoints"]
    detail["PlanGeometrySource"] = "CandidateScenario"
    detail["ActualOutcomeSource"] = "FrozenActual"
    detail = detail.drop(columns=["AccountJoin", "CandidateJoin"])
    summary = {
        "NormalActualInputTrades": len(normal_execution),
        "DuplicateReplayRowsExcluded": duplicate_replay_count,
        "NormalActualTrades": len(actual),
        "NormalAccountTrades": len(normal_account),
        "CandidateKeyCount": len(candidates),
        "ExactCandidateJoins": len(detail),
        "MissingCandidateJoins": 0,
        "MissingAccountPnL": 0,
        "CandidatePlanActualPlanRiskMatches": int(
            detail["CandidatePlanActualPlanRiskDeltaPoints"].abs().lt(1e-9).sum()
        ),
        "ActualPlanFilledRiskMatches": int(
            detail["ActualPlanFilledRiskDeltaPoints"].abs().lt(1e-9).sum()
        ),
        "CandidateTargetRActualPlanMatches": int(
            detail["CandidateTargetRActualPlanDelta"].abs().lt(1e-9).sum()
        ),
        "ActualEntryCandidatePlanMatches": int(detail["EntryDeltaPoints"].abs().lt(1e-9).sum()),
    }
    return detail.sort_values(["SnapshotID", "TradeID"]).reset_index(drop=True), summary


def write_reports(detail: pd.DataFrame, summary: dict[str, int], output_dir: Path) -> None:
    output_dir.mkdir(parents=True, exist_ok=True)
    detail.to_csv(output_dir / "actual_candidate_calibration_detail.csv", index=False, encoding="utf-8")
    pd.DataFrame([summary]).to_csv(
        output_dir / "actual_candidate_calibration_summary.csv", index=False, encoding="utf-8"
    )
    distributions = detail.groupby(["Side", "ResearchPath", "CandidatePlannedTargetR"], dropna=False).agg(
        Trades=("CandidateID", "size"),
        MeanEntryDeltaPoints=("EntryDeltaPoints", "mean"),
        MedianEntryDeltaPoints=("EntryDeltaPoints", "median"),
        MeanActualPlanFilledRiskDeltaPoints=("ActualPlanFilledRiskDeltaPoints", "mean"),
        MedianActualPlanFilledRiskDeltaPoints=("ActualPlanFilledRiskDeltaPoints", "median"),
        ActualNetDollars=("ActualNetPnLDollars", "sum"),
    ).reset_index()
    distributions.to_csv(
        output_dir / "actual_candidate_calibration_distributions.csv", index=False, encoding="utf-8"
    )


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--actual-root", type=Path, required=True)
    parser.add_argument("--candidate-csv", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    args = parser.parse_args()
    executions, accounts = load_archive(args.actual_root)
    candidates = pd.read_csv(args.candidate_csv)
    detail, summary = calibrate(executions, accounts, candidates)
    write_reports(detail, summary, args.output_dir)
    print(pd.Series(summary).to_string())


if __name__ == "__main__":
    main()
