#!/usr/bin/env python3
"""Audit v2.77 family marginal contribution from the existing StaticPlan tape.

This is an offline counterfactual audit only.  It must not be read as Actual
Replay, Smoke, or live-trading evidence.
"""

from __future__ import annotations

import argparse
import importlib.util
from pathlib import Path

import pandas as pd


VIEWS = ("Strict", "LowerBound")


def family_for(path: str) -> str:
    if path.startswith("ObservationConfirm"):
        return "ObservationConfirm"
    if path.startswith("FailureReverse_ObservationInvalidated"):
        return "FailureReverseInvalidated"
    if path.startswith("FailureReverse_RetestFailed"):
        return "FailureReverseRetestFailed"
    if path.startswith("ObservationStrict_BullFresh"):
        return "ObservationStrictBullFresh"
    if path.startswith("ObservationStrict_Other"):
        return "ObservationStrictOther"
    if path.startswith("BreakawayFvg"):
        return "BreakawayFvg"
    if path.startswith("StructureConfirmShadow"):
        return "StructureConfirmShadow"
    return path


def _month_net(trades: pd.DataFrame, months: list[str]) -> pd.Series:
    if trades.empty:
        return pd.Series(0.0, index=months)
    net = trades.assign(Month=trades["TradingDate"].dt.strftime("%Y-%m")).groupby("Month")["NetDollars"].sum()
    return net.reindex(months, fill_value=0.0)


def _worst_week(trades: pd.DataFrame) -> float:
    if trades.empty:
        return 0.0
    return float(trades.groupby("TradingWeek")["NetDollars"].sum().min())


def evaluate_stability(monthly: dict[str, list[float]], accepted: dict[str, int], worst_week_delta: dict[str, float]) -> dict:
    totals = {view: round(sum(values), 2) for view, values in monthly.items()}
    joint_nonnegative = sum(
        strict >= 0 and lower >= 0
        for strict, lower in zip(monthly["Strict"], monthly["LowerBound"])
    )
    concentration = {}
    for view, values in monthly.items():
        positive_total = sum(value for value in values if value > 0)
        concentration[view] = 0.0 if positive_total == 0 else max(values) / positive_total
    rules = {
        "BothViewsPositive": all(totals[view] > 0 for view in VIEWS),
        "FourJointNonnegativeMonths": joint_nonnegative >= 4,
        "NoMonthOverHalfPositiveContribution": all(concentration[view] <= 0.5 for view in VIEWS),
        "StrictAcceptedFamilyTradesAtLeast20": accepted["Strict"] >= 20,
        "WorstWeekNotWorseByMoreThan250": all(worst_week_delta[view] >= -250.0 for view in VIEWS),
    }
    return {
        **rules,
        "StrictTotalContribution": totals["Strict"],
        "LowerBoundTotalContribution": totals["LowerBound"],
        "JointNonnegativeMonths": joint_nonnegative,
        "StrictPositiveMonthConcentration": round(concentration["Strict"], 4),
        "LowerBoundPositiveMonthConcentration": round(concentration["LowerBound"], 4),
        "Pass": all(rules.values()),
    }


def _load_portfolio_module(path: Path):
    spec = importlib.util.spec_from_file_location("portfolio", path)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Unable to load {path}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def research_quality(outcomes_dir: Path) -> tuple[pd.DataFrame, pd.DataFrame]:
    columns = [
        "SignalID", "EntryTime", "Side", "ResearchPath", "MFE_R", "MAE_R", "Hit1R", "Hit1_5R",
        "StopHitBefore1R", "OutcomeSource", "WouldTradeLive", "ResearchOnlySignal", "ActualVerified",
    ]
    frames = []
    for path in outcomes_dir.glob("*_research_outcomes.csv"):
        frames.append(pd.read_csv(path, usecols=lambda name: name in columns))
    if not frames:
        raise ValueError(f"No research outcomes under {outcomes_dir}")
    rows = pd.concat(frames, ignore_index=True)
    rows = rows.loc[rows["OutcomeSource"].eq("ResearchOHLC")].copy()
    rows = rows.drop_duplicates(["SignalID", "ResearchPath", "EntryTime", "Side"])
    rows["EntryTime"] = pd.to_datetime(rows["EntryTime"])
    rows["Family"] = rows["ResearchPath"].map(family_for)
    rows["FamilySide"] = rows["Family"] + " " + rows["Side"]
    rows["Period"] = pd.cut(
        rows["EntryTime"],
        bins=pd.to_datetime(
            ["2025-03-31", "2025-07-31 23:59:59", "2025-09-30 23:59:59", "2025-12-31 23:59:59", "2026-04-30 23:59:59", "2026-07-31 23:59:59"],
            format="mixed",
        ),
        labels=["2025-04~07", "2025-08~09", "2025-10~12", "2026-01~04", "2026-05~07"],
        include_lowest=True,
    )
    rows["PreStop1_5R"] = rows["Hit1_5R"].astype(bool) & ~rows["StopHitBefore1R"].astype(bool)
    summary = rows.groupby(["ResearchPath", "Side"], dropna=False).agg(
        Proposals=("SignalID", "size"),
        Hit1RRate=("Hit1R", "mean"),
        Hit1_5RRate=("Hit1_5R", "mean"),
        StopBefore1RRate=("StopHitBefore1R", "mean"),
        PreStop1_5RRate=("PreStop1_5R", "mean"),
        MedianMfeR=("MFE_R", "median"),
        MedianMaeR=("MAE_R", "median"),
    ).reset_index()
    for column in ["Hit1RRate", "Hit1_5RRate", "StopBefore1RRate", "PreStop1_5RRate", "MedianMfeR", "MedianMaeR"]:
        summary[column] = summary[column].round(4)
    period = rows.groupby(["ResearchPath", "Side", "Period"], observed=True).agg(
        Proposals=("SignalID", "size"),
        Hit1_5RRate=("Hit1_5R", "mean"),
        StopBefore1RRate=("StopHitBefore1R", "mean"),
        PreStop1_5RRate=("PreStop1_5R", "mean"),
    ).reset_index()
    for column in ["Hit1_5RRate", "StopBefore1RRate", "PreStop1_5RRate"]:
        period[column] = period[column].round(4)
    return summary, period


def audit(candidates: pd.DataFrame, labels: pd.DataFrame, portfolio, group_column: str) -> tuple[pd.DataFrame, pd.DataFrame, pd.DataFrame]:
    label_columns = [
        "CandidateID", "TerminalLabel", "OutcomeTime", "StrictGrossDollars", "LowerBoundGrossDollars",
        "UpperBoundGrossDollars",
    ]
    joined = candidates.merge(labels.loc[:, label_columns], on="CandidateID", how="inner", validate="one_to_one")
    if len(joined) != len(candidates):
        raise ValueError("Candidate without a StaticPlan label")
    joined["Family"] = joined["ResearchPath"].map(family_for)
    joined["FamilySide"] = joined["Family"] + " " + joined["Side"]
    prepared = portfolio._prepare(joined)
    months = sorted(prepared["TradingDate"].dt.strftime("%Y-%m").unique())
    baseline: dict[str, pd.DataFrame] = {}
    for view in VIEWS:
        trades, _ = portfolio.simulate(joined, view)
        baseline[view] = trades
    groups = sorted(joined[group_column].unique())
    rows: list[dict] = []
    month_rows: list[dict] = []
    for group in groups:
        removed = joined.loc[joined[group_column].ne(group)].copy()
        monthly_values: dict[str, list[float]] = {}
        accepted: dict[str, int] = {}
        risk_delta: dict[str, float] = {}
        candidate_count = int(joined[group_column].eq(group).sum())
        for view in VIEWS:
            excluded, _ = portfolio.simulate(removed, view)
            base_trades = baseline[view]
            base_month = _month_net(base_trades, months)
            excluded_month = _month_net(excluded, months)
            contribution = base_month - excluded_month
            monthly_values[view] = [float(contribution.loc[month]) for month in months]
            accepted[view] = int(base_trades.assign(Family=base_trades["ResearchPath"].map(family_for)).assign(
                FamilySide=lambda rows: rows["Family"] + " " + rows["Side"]
            )[group_column].eq(group).sum())
            risk_delta[view] = _worst_week(base_trades) - _worst_week(excluded)
            for month in months:
                month_rows.append({
                    "Group": group, "PortfolioView": view, "Month": month,
                    "BaselineNetDollars": round(float(base_month.loc[month]), 2),
                    "RemovedNetDollars": round(float(excluded_month.loc[month]), 2),
                    "ContributionNetDollars": round(float(contribution.loc[month]), 2),
                })
        gate = evaluate_stability(monthly_values, accepted, risk_delta)
        rows.append({
            "Group": group,
            "CandidateCount": candidate_count,
            "StrictAcceptedFamilyTrades": accepted["Strict"],
            "LowerBoundAcceptedFamilyTrades": accepted["LowerBound"],
            "StrictWorstWeekDelta": round(risk_delta["Strict"], 2),
            "LowerBoundWorstWeekDelta": round(risk_delta["LowerBound"], 2),
            **gate,
        })
    baseline_rows = []
    for view, trades in baseline.items():
        summary = portfolio.summarize(trades)
        baseline_rows.append({"PortfolioView": view, **summary})
    return pd.DataFrame(rows), pd.DataFrame(month_rows), pd.DataFrame(baseline_rows)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--candidate-csv", type=Path, required=True)
    parser.add_argument("--label-csv", type=Path, required=True)
    parser.add_argument("--portfolio-script", type=Path, required=True)
    parser.add_argument("--research-outcomes-dir", type=Path)
    parser.add_argument("--output-dir", type=Path, required=True)
    args = parser.parse_args()
    args.output_dir.mkdir(parents=True, exist_ok=True)
    candidates = pd.read_csv(args.candidate_csv)
    labels = pd.read_csv(args.label_csv)
    portfolio = _load_portfolio_module(args.portfolio_script)
    summary, monthly, baseline = audit(candidates, labels, portfolio, "Family")
    side_summary, side_monthly, _ = audit(candidates, labels, portfolio, "FamilySide")
    summary.sort_values("StrictTotalContribution", ascending=False).to_csv(
        args.output_dir / "family_stability_summary.csv", index=False, encoding="utf-8-sig"
    )
    monthly.sort_values(["Group", "PortfolioView", "Month"]).to_csv(
        args.output_dir / "family_monthly_marginal_contribution.csv", index=False, encoding="utf-8-sig"
    )
    side_summary.sort_values("StrictTotalContribution", ascending=False).to_csv(
        args.output_dir / "family_side_stability_summary.csv", index=False, encoding="utf-8-sig"
    )
    side_monthly.sort_values(["Group", "PortfolioView", "Month"]).to_csv(
        args.output_dir / "family_side_monthly_marginal_contribution.csv", index=False, encoding="utf-8-sig"
    )
    if args.research_outcomes_dir:
        quality, period_quality = research_quality(args.research_outcomes_dir)
        quality.to_csv(args.output_dir / "research_path_direction_quality.csv", index=False, encoding="utf-8-sig")
        period_quality.to_csv(args.output_dir / "research_path_direction_period_quality.csv", index=False, encoding="utf-8-sig")
    baseline.to_csv(args.output_dir / "portfolio_baseline.csv", index=False, encoding="utf-8-sig")
    strict = baseline.loc[baseline["PortfolioView"].eq("Strict")].iloc[0]
    lower = baseline.loc[baseline["PortfolioView"].eq("LowerBound")].iloc[0]
    assert (int(strict["Trades"]), round(float(strict["NetDollars"]), 2)) == (1148, 1597.77)
    assert (int(lower["Trades"]), round(float(lower["NetDollars"]), 2)) == (1166, 1943.49)
    print(summary.sort_values("StrictTotalContribution", ascending=False).to_string(index=False))


if __name__ == "__main__":
    main()
