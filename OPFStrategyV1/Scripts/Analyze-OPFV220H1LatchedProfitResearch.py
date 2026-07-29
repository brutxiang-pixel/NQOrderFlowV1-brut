import argparse
import importlib.util
from dataclasses import dataclass
from pathlib import Path

import pandas as pd


COMMISSION = 3.6
CURRENT_GROSS = 26210.73
CURRENT_NET = 22826.73
CURRENT_PF = 1.6569
CURRENT_POSITIVE_WEEKS = 23
CURRENT_WORST_WEEK = -1023.60
CURRENT_MAX_DD = 2034.29
CURRENT_JULY_COMBINED_NET = -38.98 + 184.67


def load_module(path: Path, name: str):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


@dataclass(frozen=True)
class Policy:
    daily_cap: int = 15
    daily_loss: float = 250.0
    long_drawdown: float | None = None
    short_metric: str = "Off"
    short_threshold: float | None = None
    primary_block: str = "Off"
    zone_birth_min_quality: float | None = None
    primary_short_min_quality: float | None = None
    secondary_week_loss: float | None = None
    secondary_week_drawdown: float | None = None
    secondary_day_loss: float | None = None
    secondary_day_cap: int | None = None
    secondary_week_cap: int | None = None
    weekly_account_loss: float | None = None
    weekly_account_drawdown: float | None = None

    @property
    def name(self) -> str:
        long_dd = "Off" if self.long_drawdown is None else f"{self.long_drawdown:g}"
        short = (
            "Off"
            if self.short_threshold is None
            else f"{self.short_metric}{self.short_threshold:g}"
        )
        zone_quality = (
            "Off" if self.zone_birth_min_quality is None else f"{self.zone_birth_min_quality:g}"
        )
        short_quality = (
            "Off" if self.primary_short_min_quality is None else f"{self.primary_short_min_quality:g}"
        )
        return (
            f"Cap{self.daily_cap}_Loss{self.daily_loss:g}_"
            f"LongDD{long_dd}_Short{short}_Primary{self.primary_block}_"
            f"ZoneQ{zone_quality}_PrimaryShortQ{short_quality}"
        )


def is_breakaway_short(row) -> bool:
    return row.Side == "Short" and str(row.ResearchPath).startswith("Breakaway")


def short_metric_value(state: dict[str, float], metric: str) -> float:
    if metric == "ShortNet":
        return state["ShortNet"]
    if metric == "NonBreakawayShortNet":
        return state["NonBreakawayShortNet"]
    if metric == "ShortDrawdown":
        return state["ShortNet"] - state["ShortPeak"]
    if metric == "NonBreakawayShortDrawdown":
        return state["NonBreakawayShortNet"] - state["NonBreakawayShortPeak"]
    raise ValueError(f"Unknown short metric: {metric}")


def simulate(
    tape: pd.DataFrame,
    connector,
    policy: Policy,
    actual_abort_keys: frozenset[str],
    candidate_gate=None,
    trade_adjuster=None,
) -> tuple[pd.DataFrame, dict]:
    accepted = []
    blocked = {
        "WeeklyLong": 0,
        "WeeklyLongDrawdown": 0,
        "WeeklyShort": 0,
        "SecondaryGate": 0,
        "Active": 0,
        "DailyCap": 0,
        "DailyLoss": 0,
        "CombinedRisk": 0,
        "PreflightAborted": 0,
    }
    week_state: dict[tuple[int, int], dict[str, float | bool]] = {}

    def realize(state, trade):
        state["AccountNet"] += trade["Net"]
        state["AccountPeak"] = max(state["AccountPeak"], state["AccountNet"])
        if (
            policy.weekly_account_loss is not None
            and state["AccountNet"] <= -policy.weekly_account_loss
        ):
            state["AccountLatched"] = True
        if (
            policy.weekly_account_drawdown is not None
            and state["AccountNet"] - state["AccountPeak"]
            <= -policy.weekly_account_drawdown
        ):
            state["AccountLatched"] = True
        if trade["Lane"] == "Secondary":
            state["SecondaryNet"] += trade["Net"]
            state["SecondaryPeak"] = max(state["SecondaryPeak"], state["SecondaryNet"])
            if (
                policy.secondary_week_loss is not None
                and state["SecondaryNet"] <= -policy.secondary_week_loss
            ):
                state["SecondaryLatched"] = True
            if (
                policy.secondary_week_drawdown is not None
                and state["SecondaryNet"] - state["SecondaryPeak"]
                <= -policy.secondary_week_drawdown
            ):
                state["SecondaryLatched"] = True
        if trade["Side"] == "Long":
            state["LongNet"] += trade["Net"]
            state["LongPeak"] = max(state["LongPeak"], state["LongNet"])
        else:
            state["ShortNet"] += trade["Net"]
            state["ShortPeak"] = max(state["ShortPeak"], state["ShortNet"])
            if not trade["IsBreakawayShort"]:
                state["NonBreakawayShortNet"] += trade["Net"]
                state["NonBreakawayShortPeak"] = max(
                    state["NonBreakawayShortPeak"], state["NonBreakawayShortNet"]
                )

        if min(state["AccountNet"], state["LongNet"]) <= -500.0:
            state["LongLatched"] = True
        if (
            policy.long_drawdown is not None
            and state["LongNet"] - state["LongPeak"] <= -policy.long_drawdown
        ):
            state["LongDrawdownLatched"] = True
        if (
            policy.short_threshold is not None
            and short_metric_value(state, policy.short_metric) <= -policy.short_threshold
        ):
            state["ShortLatched"] = True

    ordered = tape.sort_values(["TradingDate", "EntryTime", "SourceSequence"])
    for snapshot_id, day in ordered.groupby("SnapshotID", sort=False):
        active = []
        normal_count = 0
        realized_net = 0.0
        secondary_day_net = 0.0
        secondary_day_count = 0
        trading_date = pd.Timestamp(day.iloc[0]["TradingDate"])
        iso = trading_date.isocalendar()
        week_key = (int(iso.year), int(iso.week))
        state = week_state.setdefault(
            week_key,
            {
                "AccountNet": 0.0,
                "AccountPeak": 0.0,
                "AccountLatched": False,
                "LongNet": 0.0,
                "LongPeak": 0.0,
                "ShortNet": 0.0,
                "ShortPeak": 0.0,
                "NonBreakawayShortNet": 0.0,
                "NonBreakawayShortPeak": 0.0,
                "LongLatched": False,
                "LongDrawdownLatched": False,
                "ShortLatched": False,
                "SecondaryNet": 0.0,
                "SecondaryPeak": 0.0,
                "SecondaryLatched": False,
                "SecondaryCount": 0,
            },
        )

        for row in day.sort_values(["EntryTime", "SourceSequence"]).itertuples(index=False):
            retained = []
            for trade in active:
                if trade["ExitTime"] < row.EntryTime:
                    if not trade["Realized"]:
                        realized_net += trade["Net"]
                        realize(state, trade)
                        if trade["Lane"] == "Secondary":
                            secondary_day_net += trade["Net"]
                        trade["Realized"] = True
                    continue
                retained.append(trade)
            active = retained

            if state["AccountLatched"]:
                blocked["WeeklyShort"] += 1
                continue

            if normal_count >= policy.daily_cap:
                blocked["DailyCap"] += 1
                continue
            if realized_net <= -policy.daily_loss:
                blocked["DailyLoss"] += 1
                continue
            if len(active) >= 2:
                blocked["Active"] += 1
                continue
            if row.Side == "Long" and state["LongLatched"]:
                blocked["WeeklyLong"] += 1
                continue
            if row.Side == "Long" and state["LongDrawdownLatched"]:
                blocked["WeeklyLongDrawdown"] += 1
                continue
            if (
                row.Side == "Short"
                and not is_breakaway_short(row)
                and state["ShortLatched"]
            ):
                blocked["WeeklyShort"] += 1
                continue

            lane = "Primary" if not active else "Secondary"
            if candidate_gate is not None and candidate_gate(row, lane):
                blocked["PreflightAborted"] += 1
                continue
            if lane == "Primary":
                if policy.primary_block in {"ZoneBirth", "ZoneBirthUnknown"} and row.ResearchPath == "ZoneBirthResearch":
                    blocked["PreflightAborted"] += 1
                    continue
                if policy.primary_block in {"Unknown", "ZoneBirthUnknown"} and row.ResearchPath == "UnknownRegimeZoneTouch":
                    blocked["PreflightAborted"] += 1
                    continue
                if (
                    policy.zone_birth_min_quality is not None
                    and row.ResearchPath == "ZoneBirthResearch"
                    and float(row.SetupQualityScore) < policy.zone_birth_min_quality
                ):
                    blocked["PreflightAborted"] += 1
                    continue
                if (
                    policy.primary_short_min_quality is not None
                    and row.Side == "Short"
                    and not is_breakaway_short(row)
                    and float(row.SetupQualityScore) < policy.primary_short_min_quality
                ):
                    blocked["PreflightAborted"] += 1
                    continue
            if lane == "Secondary":
                if row.Side != active[0]["Side"] or not connector.actual_g2_eligible(row):
                    blocked["SecondaryGate"] += 1
                    continue
                if state["SecondaryLatched"]:
                    blocked["SecondaryGate"] += 1
                    continue
                if (
                    policy.secondary_day_loss is not None
                    and secondary_day_net <= -policy.secondary_day_loss
                ):
                    blocked["SecondaryGate"] += 1
                    continue
                if (
                    policy.secondary_day_cap is not None
                    and secondary_day_count >= policy.secondary_day_cap
                ):
                    blocked["SecondaryGate"] += 1
                    continue
                if (
                    policy.secondary_week_cap is not None
                    and state["SecondaryCount"] >= policy.secondary_week_cap
                ):
                    blocked["SecondaryGate"] += 1
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
                "IsBreakawayShort": is_breakaway_short(row),
                "OutcomeClassification": row.OutcomeClassification,
                "Realized": False,
            }
            if trade_adjuster is not None:
                trade_adjuster(row, lane, trade)
            accepted.append(trade)
            active.append(trade)
            if lane == "Secondary":
                secondary_day_count += 1
                state["SecondaryCount"] += 1
            if row.OutcomeClassification in {"Normal", "Predicted"}:
                normal_count += 1

        for trade in active:
            if not trade["Realized"]:
                realize(state, trade)
                trade["Realized"] = True

    return pd.DataFrame(accepted), blocked


def profit_factor(values: pd.Series) -> float:
    wins = float(values[values > 0].sum())
    losses = -float(values[values < 0].sum())
    return wins / losses if losses > 0 else float("inf")


def summarize(sample: str, policy: Policy, trades: pd.DataFrame, blocked: dict) -> dict:
    dates = pd.to_datetime(trades["TradingDate"])
    daily = trades.assign(Date=dates.dt.strftime("%Y-%m-%d")).groupby("Date")["Net"].sum()
    weeks = trades.assign(Week=dates.dt.to_period("W-SUN").astype(str)).groupby("Week")["Net"].sum()
    months = trades.assign(Month=dates.dt.strftime("%Y-%m")).groupby("Month")["Net"].sum()
    cumulative = daily.cumsum()
    drawdown = cumulative.cummax() - cumulative
    secondary = trades[trades["Lane"].eq("Secondary")]
    breakaway = trades[
        trades["Side"].eq("Short") & trades["ResearchPath"].str.startswith("Breakaway")
    ]
    return {
        "Sample": sample,
        "Policy": policy.name,
        "DailyCap": policy.daily_cap,
        "DailyLoss": policy.daily_loss,
        "LongDrawdown": policy.long_drawdown,
        "ShortMetric": policy.short_metric,
        "ShortThreshold": policy.short_threshold,
        "Trades": len(trades),
        "Gross": round(float(trades["Gross"].sum()), 2),
        "Net": round(float(trades["Net"].sum()), 2),
        "PF": round(profit_factor(trades["Gross"]), 4),
        "PositiveWeeks": int(weeks.gt(0).sum()),
        "WeekCount": len(weeks),
        "WorstWeek": round(float(weeks.min()), 2),
        "MaxDD": round(float(drawdown.max()), 2),
        "SecondaryGross": round(float(secondary["Gross"].sum()), 2),
        "BreakawayShortGross": round(float(breakaway["Gross"].sum()), 2),
        "AllMonthsPositive": bool(months.gt(0).all()),
        **blocked,
    }


def policies() -> list[Policy]:
    result = [Policy()]
    result.extend(
        Policy(daily_cap=cap, daily_loss=loss)
        for cap in (10, 12, 15, 18, 20)
        for loss in (200.0, 250.0, 300.0, 400.0, 500.0)
    )
    result.extend(Policy(primary_block=value) for value in ("ZoneBirth", "Unknown", "ZoneBirthUnknown"))
    result.extend(Policy(zone_birth_min_quality=value) for value in (42.0, 44.0, 45.0, 50.0, 55.0, 60.0, 65.0))
    result.extend(Policy(primary_short_min_quality=value) for value in (45.0, 50.0, 55.0, 60.0))
    for cap in (15, 18):
        for zone_quality in (50.0, 55.0, 60.0, 65.0):
            for short_quality in (None, 45.0, 50.0):
                result.append(
                    Policy(
                        daily_cap=cap,
                        zone_birth_min_quality=zone_quality,
                        primary_short_min_quality=short_quality,
                    )
                )
    for cap in (12, 15, 18, 20):
        for loss in (200.0, 250.0, 300.0, 400.0, 500.0):
            for zone_quality in (42.0, 44.0, 45.0):
                result.append(
                    Policy(
                        daily_cap=cap,
                        daily_loss=loss,
                        zone_birth_min_quality=zone_quality,
                    )
                )
                for short_threshold in (350.0, 500.0, 650.0):
                    result.append(
                        Policy(
                            daily_cap=cap,
                            daily_loss=loss,
                            short_metric="NonBreakawayShortNet",
                            short_threshold=short_threshold,
                            zone_birth_min_quality=zone_quality,
                        )
                    )
                result.append(
                    Policy(
                        daily_cap=cap,
                        short_metric="NonBreakawayShortNet",
                        short_threshold=500.0,
                        zone_birth_min_quality=zone_quality,
                        primary_short_min_quality=short_quality,
                    )
                )
    result.extend(Policy(long_drawdown=value) for value in (300.0, 400.0, 500.0, 600.0, 750.0))
    result.extend(
        Policy(short_metric=metric, short_threshold=value)
        for metric in (
            "ShortNet",
            "NonBreakawayShortNet",
            "ShortDrawdown",
            "NonBreakawayShortDrawdown",
        )
        for value in (250.0, 350.0, 500.0, 650.0, 800.0)
    )
    for cap in (12, 15, 18):
        for loss in (200.0, 250.0, 300.0, 400.0):
            for long_dd in (None, 400.0, 600.0):
                result.append(Policy(cap, loss, long_dd))
                for metric in ("NonBreakawayShortNet", "NonBreakawayShortDrawdown"):
                    for threshold in (350.0, 500.0, 650.0):
                        result.append(Policy(cap, loss, long_dd, metric, threshold))
    return list(dict.fromkeys(result))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--h1-tape", type=Path, required=True)
    parser.add_argument("--july1-tape", type=Path, required=True)
    parser.add_argument("--july2-tape", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    args = parser.parse_args()

    connector = load_module(
        Path(__file__).with_name("Analyze-OPFV217DualSlotCalibration.py"),
        "opf_v217_connector",
    )
    sample_paths = {
        "H1": args.h1_tape,
        "July1": args.july1_tape,
        "July2": args.july2_tape,
    }
    tapes = {}
    abort_keys = {}
    for sample, path in sample_paths.items():
        tape = pd.read_csv(path, low_memory=False)
        for column in ("EntryTime", "PredictedExitTime", "TradingDate"):
            tape[column] = pd.to_datetime(tape[column])
        tapes[sample] = tape
        submissions = pd.read_csv(path.with_name("submission_trace.csv"), low_memory=False)
        aborted = submissions[submissions["SubmissionStatus"].eq("PreflightAborted")]
        abort_keys[sample] = frozenset(
            aborted["SignalID"] + "|" + aborted["ResearchPath"] + "|" + aborted["Lane"]
        )

    rows = []
    trade_sets = {}
    for policy in policies():
        for sample, tape in tapes.items():
            trades, blocked = simulate(tape, connector, policy, abort_keys[sample])
            rows.append(summarize(sample, policy, trades, blocked))
            if sample == "H1":
                trade_sets[policy.name] = trades

    detail = pd.DataFrame(rows)
    ranking_rows = []
    for policy in policies():
        selected = detail[detail["Policy"].eq(policy.name)].set_index("Sample")
        h1 = selected.loc["H1"]
        j1 = selected.loc["July1"]
        j2 = selected.loc["July2"]
        ranking_rows.append(
            {
                "Policy": policy.name,
                "DailyCap": policy.daily_cap,
                "DailyLoss": policy.daily_loss,
                "LongDrawdown": policy.long_drawdown,
                "ShortMetric": policy.short_metric,
                "ShortThreshold": policy.short_threshold,
                "PrimaryBlock": policy.primary_block,
                "ZoneBirthMinQuality": policy.zone_birth_min_quality,
                "PrimaryShortMinQuality": policy.primary_short_min_quality,
                "H1Trades": h1.Trades,
                "H1Gross": h1.Gross,
                "H1GrossDeltaVsPublished": round(h1.Gross - CURRENT_GROSS, 2),
                "H1Net": h1.Net,
                "H1NetDeltaVsPublished": round(h1.Net - CURRENT_NET, 2),
                "H1PF": h1.PF,
                "H1PositiveWeeks": h1.PositiveWeeks,
                "H1WorstWeek": h1.WorstWeek,
                "H1MaxDD": h1.MaxDD,
                "H1SecondaryGross": h1.SecondaryGross,
                "H1BreakawayShortGross": h1.BreakawayShortGross,
                "H1AllMonthsPositive": h1.AllMonthsPositive,
                "July1Net": j1.Net,
                "July2Net": j2.Net,
                "JulyCombinedNet": round(j1.Net + j2.Net, 2),
            }
        )
    ranking = pd.DataFrame(ranking_rows)
    baseline = ranking[ranking["Policy"].eq(Policy().name)].iloc[0]
    ranking["GrossDeltaVsLatched"] = (ranking["H1Gross"] - baseline.H1Gross).round(2)
    ranking["NetDeltaVsLatched"] = (ranking["H1Net"] - baseline.H1Net).round(2)
    ranking["SmokeGate"] = (
        ranking["GrossDeltaVsLatched"].ge(1500.0)
        & ranking["NetDeltaVsLatched"].ge(1500.0)
        & ranking["H1PF"].ge(baseline.H1PF)
        & ranking["H1PositiveWeeks"].ge(baseline.H1PositiveWeeks)
        & ranking["H1WorstWeek"].ge(baseline.H1WorstWeek)
        & ranking["H1MaxDD"].le(baseline.H1MaxDD)
        & ranking["H1AllMonthsPositive"]
        & ranking["H1BreakawayShortGross"].ge(baseline.H1BreakawayShortGross - 1.0)
        & ranking["JulyCombinedNet"].ge(CURRENT_JULY_COMBINED_NET - 250.0)
    )
    ranking = ranking.sort_values(
        ["SmokeGate", "H1Net", "H1PF", "H1MaxDD"],
        ascending=[False, False, False, True],
    )

    args.output_dir.mkdir(parents=True, exist_ok=True)
    detail.to_csv(args.output_dir / "detail.csv", index=False)
    ranking.to_csv(args.output_dir / "ranking.csv", index=False)
    baseline_policy = Policy().name
    top_policy = ranking.iloc[0].Policy
    selected = []
    for label, name in (("LatchedBaseline", baseline_policy), ("Top", top_policy)):
        trades = trade_sets[name].copy()
        trades["Selection"] = label
        selected.append(trades)
    selected = pd.concat(selected, ignore_index=True)
    selected.to_csv(args.output_dir / "selected_trades.csv", index=False)
    selected["Month"] = pd.to_datetime(selected["TradingDate"]).dt.strftime("%Y-%m")
    selected["Week"] = pd.to_datetime(selected["TradingDate"]).dt.to_period("W-SUN").astype(str)
    selected.groupby(["Selection", "Month"], as_index=False).agg(
        Trades=("Net", "size"), Gross=("Gross", "sum"), Net=("Net", "sum")
    ).to_csv(args.output_dir / "selected_monthly.csv", index=False)
    selected.groupby(["Selection", "Week"], as_index=False).agg(
        Trades=("Net", "size"), Gross=("Gross", "sum"), Net=("Net", "sum")
    ).to_csv(args.output_dir / "selected_weekly.csv", index=False)

    print("Published dynamic baseline:", CURRENT_GROSS, CURRENT_NET)
    print("Actual-latched control:", baseline.H1Gross, baseline.H1Net, baseline.H1PF)
    print(ranking.head(20).to_string(index=False))


if __name__ == "__main__":
    main()
