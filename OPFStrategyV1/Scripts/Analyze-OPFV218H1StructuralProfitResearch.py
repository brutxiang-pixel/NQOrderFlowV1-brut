import argparse
import importlib.util
from dataclasses import dataclass
from pathlib import Path

import pandas as pd


COMMISSION = 3.6
BASELINE_GROSS = 24239.98
BASELINE_JULY_1_NET = -872.30
BASELINE_JULY_2_NET = -689.10


def load_connector(path: Path):
    spec = importlib.util.spec_from_file_location("opf_v218_connector", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


@dataclass(frozen=True)
class Policy:
    progress: str
    breakaway_override: bool
    weekly_long_wide_floor: float | None
    weekly_action: str
    weekly_metric: str
    daily_long_wide_losses: int | None

    @property
    def name(self) -> str:
        weekly = "Off" if self.weekly_long_wide_floor is None else f"{abs(self.weekly_long_wide_floor):g}"
        daily = "Off" if self.daily_long_wide_losses is None else str(self.daily_long_wide_losses)
        return (
            f"Progress{self.progress}_Breakaway{int(self.breakaway_override)}_"
            f"Weekly{self.weekly_metric}{self.weekly_action}{weekly}_DailyLWLoss{daily}"
        )


def threshold_time(row, progress: str):
    if progress == "None":
        return row.EntryTime
    column = {
        "075R": "First0_75RBar",
        "1R": "First1RBar",
        "15R": "First1_5RBar",
    }[progress]
    value = getattr(row, column)
    if pd.isna(value):
        return pd.NaT
    bars = max(0, int(round(float(value) - float(row.EntryBar))))
    return row.EntryTime + pd.Timedelta(minutes=5 * bars)


def is_long_wide(row) -> bool:
    return row.Side == "Long" and row.ResearchPath == "ObservationConfirm_WideStop1_5R"


def is_breakaway_short(row) -> bool:
    return row.Side == "Short" and str(row.ResearchPath).startswith("Breakaway")


def weekly_action_blocks(row, lane: str, policy: Policy) -> bool:
    if policy.weekly_action == "LongWide":
        return is_long_wide(row)
    if policy.weekly_action == "AllLong":
        return row.Side == "Long"
    if policy.weekly_action == "Secondary":
        return lane == "Secondary"
    if policy.weekly_action == "LongWideSecondary":
        return is_long_wide(row) or lane == "Secondary"
    if policy.weekly_action == "ExpansionLongWide":
        return is_long_wide(row) and "OCWideStopLongExpansionV157" in str(row.OriginalReason)
    raise ValueError(f"Unknown weekly action: {policy.weekly_action}")


def weekly_metric_value(state: dict[str, float], metric: str) -> float:
    if metric == "AccountOrLong":
        return min(state["AccountNet"], state["LongNet"])
    if metric == "AccountAndLong":
        return max(state["AccountNet"], state["LongNet"])
    return state[metric]


def simulate(
    tape: pd.DataFrame,
    connector,
    policy: Policy,
    actual_abort_keys: frozenset[str],
) -> tuple[pd.DataFrame, dict]:
    accepted = []
    blocked = {
        "Progress": 0,
        "WeeklyLongWide": 0,
        "DailyLongWide": 0,
        "SecondaryGate": 0,
        "Active": 0,
        "DailyCap": 0,
        "DailyLoss": 0,
        "CombinedRisk": 0,
        "PreflightAborted": 0,
    }
    week_state: dict[tuple[int, int], dict[str, float]] = {}

    def realize(week_key, trade):
        state = week_state[week_key]
        state["AccountNet"] += trade["Net"]
        if trade["Side"] == "Long":
            state["LongNet"] += trade["Net"]
        if trade["IsLongWide"]:
            state["LongWideNet"] += trade["Net"]

    ordered = tape.sort_values(["TradingDate", "EntryTime", "SourceSequence"])
    for snapshot_id, day in ordered.groupby("SnapshotID", sort=False):
        active = []
        normal_count = 0
        realized_net = 0.0
        daily_long_wide_loss_count = 0
        trading_date = pd.Timestamp(day.iloc[0]["TradingDate"])
        iso = trading_date.isocalendar()
        week_key = (int(iso.year), int(iso.week))
        week_state.setdefault(
            week_key, {"AccountNet": 0.0, "LongNet": 0.0, "LongWideNet": 0.0}
        )

        for row in day.sort_values(["EntryTime", "SourceSequence"]).itertuples(index=False):
            retained = []
            for trade in active:
                if trade["ExitTime"] < row.EntryTime:
                    if not trade["Realized"]:
                        realized_net += trade["Net"]
                        realize(week_key, trade)
                        if trade["IsLongWide"] and trade["Gross"] < 0:
                            daily_long_wide_loss_count += 1
                    continue
                retained.append(trade)
            active = retained

            if normal_count >= 15:
                blocked["DailyCap"] += 1
                continue
            if realized_net <= -250.0:
                blocked["DailyLoss"] += 1
                continue
            if len(active) >= 2:
                blocked["Active"] += 1
                continue
            if (
                is_long_wide(row)
                and policy.daily_long_wide_losses is not None
                and daily_long_wide_loss_count >= policy.daily_long_wide_losses
            ):
                blocked["DailyLongWide"] += 1
                continue

            lane = "Primary" if not active else "Secondary"
            if (
                policy.weekly_long_wide_floor is not None
                and weekly_metric_value(week_state[week_key], policy.weekly_metric)
                <= policy.weekly_long_wide_floor
                and weekly_action_blocks(row, lane, policy)
            ):
                blocked["WeeklyLongWide"] += 1
                continue
            if lane == "Secondary":
                if row.Side != active[0]["Side"]:
                    blocked["SecondaryGate"] += 1
                    continue
                eligible = connector.actual_g2_eligible(row)
                if policy.breakaway_override and is_breakaway_short(row):
                    eligible = True
                if not eligible:
                    blocked["SecondaryGate"] += 1
                    continue
                if policy.progress != "None":
                    progress_time = active[0]["ProgressTime"]
                    if pd.isna(progress_time) or progress_time > row.EntryTime:
                        blocked["Progress"] += 1
                        continue

            risk = float(row.ExactRisk)
            planned_risk = float(row.InitialRiskPoints)
            combined_risk = planned_risk * 6.0 + sum(item["RiskDollars"] for item in active)
            if combined_risk > 300.0:
                blocked["CombinedRisk"] += 1
                continue

            abort_reason = connector.preflight_abort_reason(row)
            identity = f"{row.SignalID}|{row.ResearchPath}|{lane}"
            if identity in actual_abort_keys:
                abort_reason = "ActualCurrentPolicyAbort"
            if abort_reason:
                blocked["PreflightAborted"] += 1
                continue

            trade = {
                "SnapshotID": snapshot_id,
                "TradingDate": trading_date,
                "SignalID": row.SignalID,
                "ResearchPath": row.ResearchPath,
                "Side": row.Side,
                "Lane": lane,
                "EntryTime": row.EntryTime,
                "ExitTime": row.PredictedExitTime,
                "ExitRole": row.PredictedExitRole,
                "Gross": float(row.PredictedGross),
                "Net": float(row.PredictedGross) - COMMISSION,
                "RiskDollars": risk * 6.0,
                "ProgressTime": threshold_time(row, policy.progress),
                "IsLongWide": is_long_wide(row),
                "OutcomeClassification": row.OutcomeClassification,
                "Realized": False,
            }
            accepted.append(trade)
            active.append(trade)
            if row.OutcomeClassification in {"Normal", "Predicted"}:
                normal_count += 1

        for trade in active:
            if not trade["Realized"]:
                realize(week_key, trade)

    return pd.DataFrame(accepted), blocked


def profit_factor(values: pd.Series) -> float:
    wins = float(values[values > 0].sum())
    losses = -float(values[values < 0].sum())
    return wins / losses if losses > 0 else float("inf")


def summarize(sample: str, policy: Policy, trades: pd.DataFrame, blocked: dict) -> dict:
    account = trades.copy()
    dates = pd.to_datetime(account["TradingDate"])
    daily = account.assign(Date=dates.dt.strftime("%Y-%m-%d")).groupby("Date")["Net"].sum()
    weeks = account.assign(Week=dates.dt.to_period("W-SUN").astype(str)).groupby("Week")["Net"].sum()
    cumulative = daily.cumsum()
    drawdown = cumulative.cummax() - cumulative
    secondary = account[account["Lane"].eq("Secondary")]
    breakaway = account[
        account["Side"].eq("Short") & account["ResearchPath"].str.startswith("Breakaway")
    ]
    long_wide = account[
        account["Side"].eq("Long")
        & account["ResearchPath"].eq("ObservationConfirm_WideStop1_5R")
    ]
    return {
        "Sample": sample,
        "Policy": policy.name,
        "Progress": policy.progress,
        "BreakawayOverride": policy.breakaway_override,
        "WeeklyLongWideFloor": policy.weekly_long_wide_floor,
        "WeeklyAction": policy.weekly_action,
        "WeeklyMetric": policy.weekly_metric,
        "DailyLongWideLosses": policy.daily_long_wide_losses,
        "Trades": len(account),
        "SecondaryTrades": len(secondary),
        "Gross": round(float(account["Gross"].sum()), 2),
        "Net": round(float(account["Net"].sum()), 2),
        "PF": round(profit_factor(account["Gross"]), 4),
        "PositiveWeeks": int(weeks.gt(0).sum()),
        "WeekCount": len(weeks),
        "WorstWeek": round(float(weeks.min()), 2),
        "MaxDD": round(float(drawdown.max()), 2),
        "SecondaryGross": round(float(secondary["Gross"].sum()), 2),
        "LongWideGross": round(float(long_wide["Gross"].sum()), 2),
        "BreakawayShortGross": round(float(breakaway["Gross"].sum()), 2),
        **blocked,
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--h1-tape", type=Path, required=True)
    parser.add_argument("--july1-tape", type=Path, required=True)
    parser.add_argument("--july2-tape", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()

    connector = load_connector(Path(__file__).with_name("Analyze-OPFV217DualSlotCalibration.py"))
    samples = {
        "H1": args.h1_tape,
        "July1": args.july1_tape,
        "July2": args.july2_tape,
    }
    tapes = {}
    abort_keys = {}
    for sample, path in samples.items():
        tape = pd.read_csv(path, low_memory=False)
        for column in ("EntryTime", "PredictedExitTime", "TradingDate"):
            tape[column] = pd.to_datetime(tape[column])
        tapes[sample] = tape
        submissions = pd.read_csv(path.with_name("submission_trace.csv"), low_memory=False)
        aborted = submissions[submissions["SubmissionStatus"].eq("PreflightAborted")]
        abort_keys[sample] = frozenset(
            aborted["SignalID"] + "|" + aborted["ResearchPath"] + "|" + aborted["Lane"]
        )

    policies = [
        Policy(progress, override, weekly, "LongWide", "AccountNet", daily)
        for progress in ("None", "075R", "1R", "15R")
        for override in (False, True)
        for weekly in (None, -150.0, -250.0, -400.0)
        for daily in (None, 1, 2)
    ]
    policies.extend(
        Policy("None", False, weekly, action, "AccountNet", None)
        for weekly in (-300.0, -350.0, -400.0, -450.0, -500.0, -600.0, -700.0)
        for action in (
            "LongWide",
            "AllLong",
            "Secondary",
            "LongWideSecondary",
            "ExpansionLongWide",
        )
    )
    policies.extend(
        Policy("None", False, weekly, action, metric, None)
        for weekly in (-250.0, -350.0, -500.0, -700.0)
        for action in ("AllLong", "LongWide")
        for metric in ("LongNet", "LongWideNet")
    )
    policies.extend(
        Policy("None", False, weekly, "AllLong", metric, None)
        for weekly in (-350.0, -500.0, -600.0, -700.0)
        for metric in ("AccountOrLong", "AccountAndLong")
    )
    policies = list(dict.fromkeys(policies))
    rows = []
    for policy in policies:
        for sample, tape in tapes.items():
            trades, blocked = simulate(tape, connector, policy, abort_keys[sample])
            rows.append(summarize(sample, policy, trades, blocked))

    results = pd.DataFrame(rows)
    wide = results.pivot(index="Policy", columns="Sample")
    policy_rows = []
    for policy in policies:
        name = policy.name
        h1 = results[(results["Policy"].eq(name)) & (results["Sample"].eq("H1"))].iloc[0]
        j1 = results[(results["Policy"].eq(name)) & (results["Sample"].eq("July1"))].iloc[0]
        j2 = results[(results["Policy"].eq(name)) & (results["Sample"].eq("July2"))].iloc[0]
        policy_rows.append(
            {
                "Policy": name,
                "H1Gross": h1.Gross,
                "H1GrossDelta": round(h1.Gross - BASELINE_GROSS, 2),
                "H1Net": h1.Net,
                "H1PF": h1.PF,
                "H1PositiveWeeks": h1.PositiveWeeks,
                "H1WorstWeek": h1.WorstWeek,
                "H1MaxDD": h1.MaxDD,
                "H1SecondaryGross": h1.SecondaryGross,
                "H1LongWideGross": h1.LongWideGross,
                "H1BreakawayShortGross": h1.BreakawayShortGross,
                "July1Net": j1.Net,
                "July1NetDelta": round(j1.Net - BASELINE_JULY_1_NET, 2),
                "July2Net": j2.Net,
                "July2NetDelta": round(j2.Net - BASELINE_JULY_2_NET, 2),
                "July1BreakawayShortGross": j1.BreakawayShortGross,
                "July2BreakawayShortGross": j2.BreakawayShortGross,
            }
        )
    ranking = pd.DataFrame(policy_rows)
    ranking["CrossPeriodPass"] = (
        ranking["H1Gross"].ge(BASELINE_GROSS)
        & ranking["H1PF"].ge(1.58)
        & ranking["H1PositiveWeeks"].ge(20)
        & ranking["July1NetDelta"].ge(0)
        & ranking["July2NetDelta"].ge(0)
        & ranking["H1BreakawayShortGross"].ge(3900.0)
        & ranking["July1BreakawayShortGross"].ge(700.0)
        & ranking["July2BreakawayShortGross"].ge(700.0)
    )
    ranking = ranking.sort_values(
        ["CrossPeriodPass", "H1Gross", "July2Net", "July1Net"],
        ascending=[False, False, False, False],
    )

    args.output.parent.mkdir(parents=True, exist_ok=True)
    results.to_csv(args.output.with_name(args.output.stem + "_detail.csv"), index=False)
    ranking.to_csv(args.output, index=False)

    diagnostic_policies = {
        "Baseline": Policy("None", False, None, "LongWide", "AccountNet", None),
        "WeeklyAllLong500": Policy("None", False, -500.0, "AllLong", "AccountNet", None),
        "WeeklyAllLong600": Policy("None", False, -600.0, "AllLong", "AccountNet", None),
        "WeeklyEitherLossAllLong500": Policy(
            "None", False, -500.0, "AllLong", "AccountOrLong", None
        ),
    }
    diagnostic_trades = []
    diagnostic_blocks = []
    for label, policy in diagnostic_policies.items():
        for sample, tape in tapes.items():
            trades, blocked = simulate(tape, connector, policy, abort_keys[sample])
            trades = trades.copy()
            trades["PolicyLabel"] = label
            trades["Sample"] = sample
            diagnostic_trades.append(trades)
            diagnostic_blocks.append({"PolicyLabel": label, "Sample": sample, **blocked})
    selected = pd.concat(diagnostic_trades, ignore_index=True)
    selected.to_csv(args.output.with_name(args.output.stem + "_selected_trades.csv"), index=False)
    pd.DataFrame(diagnostic_blocks).to_csv(
        args.output.with_name(args.output.stem + "_selected_blocks.csv"), index=False
    )
    selected["Month"] = pd.to_datetime(selected["TradingDate"]).dt.strftime("%Y-%m")
    selected["Week"] = pd.to_datetime(selected["TradingDate"]).dt.to_period("W-SUN").astype(str)
    selected.groupby(["Sample", "PolicyLabel", "Month"], as_index=False).agg(
        Trades=("Gross", "size"), Gross=("Gross", "sum"), Net=("Net", "sum")
    ).to_csv(args.output.with_name(args.output.stem + "_selected_monthly.csv"), index=False)
    selected.groupby(["Sample", "PolicyLabel", "Week"], as_index=False).agg(
        Trades=("Gross", "size"), Gross=("Gross", "sum"), Net=("Net", "sum")
    ).to_csv(args.output.with_name(args.output.stem + "_selected_weekly.csv"), index=False)
    print(ranking.head(25).to_string(index=False))


if __name__ == "__main__":
    main()
