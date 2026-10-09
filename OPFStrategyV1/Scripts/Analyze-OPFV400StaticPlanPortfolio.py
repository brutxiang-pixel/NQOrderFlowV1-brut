#!/usr/bin/env python3
"""Run strictly separated StaticPlanPortfolio research views."""

from __future__ import annotations

import argparse
from pathlib import Path

import pandas as pd


COMMISSION = 3.60
DAILY_CAP = 15
DAILY_LOSS = 250.0
WEEKLY_LONG_LOSS = 500.0


def resolve_outcome(label: pd.Series, view: str) -> float | None:
    terminal = label["TerminalLabel"]
    if terminal == "Censored":
        return None
    column = f"{view}GrossDollars"
    value = label[column]
    return None if pd.isna(value) else float(value)


def _enabled(row, name: str) -> bool:
    value = getattr(row, name, False)
    return str(value).lower() == "true"


def _prepare(candidates: pd.DataFrame) -> pd.DataFrame:
    required = {
        "CandidateID", "SnapshotID", "DecisionTime", "Side", "ResearchPath", "TerminalLabel", "OutcomeTime",
        "StrictGrossDollars", "LowerBoundGrossDollars", "UpperBoundGrossDollars",
    }
    missing = sorted(required - set(candidates.columns))
    if missing:
        raise ValueError(f"Missing StaticPlan input columns: {missing}")
    if candidates["CandidateID"].duplicated().any():
        raise ValueError("Duplicate CandidateID")
    output = candidates.copy()
    output["DecisionTime"] = pd.to_datetime(output["DecisionTime"])
    output["OutcomeTime"] = pd.to_datetime(output["OutcomeTime"])
    output["TradingDate"] = output.groupby("SnapshotID")["DecisionTime"].transform("max").dt.normalize()
    iso = output["TradingDate"].dt.isocalendar()
    output["TradingWeek"] = iso.year.astype(str) + "-W" + iso.week.astype(str).str.zfill(2)
    return output.sort_values(["DecisionTime", "CandidateID"]).reset_index(drop=True)


def simulate(candidates: pd.DataFrame, view: str) -> tuple[pd.DataFrame, pd.DataFrame]:
    if view not in {"Strict", "LowerBound", "UpperBound"}:
        raise ValueError(f"Unsupported view: {view}")
    candidates = _prepare(candidates)
    active: list[dict] = []
    daily_count: dict[str, int] = {}
    daily_net: dict[str, float] = {}
    weekly_long_net: dict[str, float] = {}
    trades: list[dict] = []
    decisions: list[dict] = []

    def realize_before(time: pd.Timestamp) -> None:
        nonlocal active
        still_active = []
        for trade in active:
            if trade["OutcomeTime"] <= time:
                daily_net[trade["SnapshotID"]] = daily_net.get(trade["SnapshotID"], 0.0) + trade["NetDollars"]
                if trade["Side"] == "Long":
                    weekly_long_net[trade["TradingWeek"]] = (
                        weekly_long_net.get(trade["TradingWeek"], 0.0) + trade["NetDollars"]
                    )
            else:
                still_active.append(trade)
        active = still_active

    for row in candidates.itertuples(index=False):
        realize_before(row.DecisionTime)
        gross = resolve_outcome(pd.Series(row._asdict()), view)
        reason = "Accepted"
        if gross is None:
            reason = row.TerminalLabel
        elif _enabled(row, "GlobexLocked"):
            reason = "GlobexLocked"
        elif _enabled(row, "UsOpenBlackout"):
            reason = "UsOpenBlackout"
        elif _enabled(row, "LatencyGateActive"):
            reason = "LatencyGateActive"
        elif active and any(trade["Side"] != row.Side for trade in active):
            reason = "OppositeDirectionActive"
        elif len(active) >= 2:
            reason = "SameDirectionSlotLimit"
        elif daily_count.get(row.SnapshotID, 0) >= DAILY_CAP:
            reason = "DailyTradeLimit"
        elif daily_net.get(row.SnapshotID, 0.0) <= -DAILY_LOSS:
            reason = "DailyLossLimit"
        elif row.Side == "Long" and weekly_long_net.get(row.TradingWeek, 0.0) <= -WEEKLY_LONG_LOSS:
            reason = "WeeklyLongLossLimit"

        decision = {
            "CandidateID": row.CandidateID, "SnapshotID": row.SnapshotID, "DecisionTime": row.DecisionTime,
            "Side": row.Side, "ResearchPath": row.ResearchPath, "PortfolioView": view,
            "TerminalLabel": row.TerminalLabel, "Decision": "Accept" if reason == "Accepted" else "Block",
            "BlockReason": "" if reason == "Accepted" else reason,
            "EvidenceType": "StaticPlanPortfolio", "OutcomeSource": "Counterfactual",
        }
        decisions.append(decision)
        if reason != "Accepted":
            continue
        trade = {
            **decision, "TradingDate": row.TradingDate, "TradingWeek": row.TradingWeek,
            "OutcomeTime": row.OutcomeTime, "GrossDollars": gross, "CommissionDollars": COMMISSION,
            "NetDollars": gross - COMMISSION,
        }
        trades.append(trade)
        active.append(trade)
        daily_count[row.SnapshotID] = daily_count.get(row.SnapshotID, 0) + 1
    return pd.DataFrame(trades), pd.DataFrame(decisions)


def summarize(trades: pd.DataFrame) -> dict:
    if trades.empty:
        return {"Trades": 0, "GrossDollars": 0.0, "NetDollars": 0.0, "PF": 0.0, "WorstWeekNet": 0.0}
    positive = trades.loc[trades["NetDollars"] > 0, "NetDollars"].sum()
    negative = -trades.loc[trades["NetDollars"] < 0, "NetDollars"].sum()
    weekly = trades.groupby("TradingWeek")["NetDollars"].sum()
    return {
        "Trades": len(trades), "GrossDollars": round(float(trades["GrossDollars"].sum()), 2),
        "NetDollars": round(float(trades["NetDollars"].sum()), 2),
        "PF": round(float(positive / negative), 4) if negative else float("inf"),
        "WorstWeekNet": round(float(weekly.min()), 2),
    }


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--candidate-csv", type=Path, required=True)
    parser.add_argument("--label-csv", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--include-research-path")
    args = parser.parse_args()
    candidates = pd.read_csv(args.candidate_csv)
    labels = pd.read_csv(args.label_csv)
    label_columns = [
        "CandidateID", "TerminalLabel", "OutcomeTime", "StrictGrossDollars", "LowerBoundGrossDollars",
        "UpperBoundGrossDollars",
    ]
    joined = candidates.merge(labels.loc[:, label_columns], on="CandidateID", how="inner", validate="one_to_one")
    if len(joined) != len(candidates):
        raise ValueError("Candidate without a static label")
    if args.include_research_path:
        joined = joined.loc[joined["ResearchPath"].eq(args.include_research_path)].copy()
    args.output_dir.mkdir(parents=True, exist_ok=True)
    summaries = []
    for view in ("Strict", "LowerBound", "UpperBound"):
        trades, decisions = simulate(joined, view)
        trades.to_csv(args.output_dir / f"static_plan_{view.lower()}_trades.csv", index=False, encoding="utf-8")
        decisions.to_csv(args.output_dir / f"static_plan_{view.lower()}_decisions.csv", index=False, encoding="utf-8")
        summaries.append({"PortfolioView": view, **summarize(trades), "EvidenceType": "StaticPlanPortfolio"})
    pd.DataFrame(summaries).to_csv(args.output_dir / "static_plan_summary.csv", index=False, encoding="utf-8")
    print(pd.DataFrame(summaries).to_string(index=False))


if __name__ == "__main__":
    main()
