import argparse
import importlib.util
from pathlib import Path

import pandas as pd


def load_module(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def zonebirth_gross3(gross2: float, exit_reason: str) -> float:
    if exit_reason == "Base:Target|Runner:ProtectBE":
        return gross2 * 2
    if exit_reason == "Base:Target|Runner:Target":
        return gross2 * 18 / 13
    return gross2 * 1.5


def gross3(gross2: float, path: str, exit_reason: str) -> float:
    if path == "ZoneBirthResearch":
        return zonebirth_gross3(gross2, exit_reason)
    return gross2 * 1.5


def strict_entry_allowed(risk_points: float, strict_gate: str) -> bool:
    if strict_gate == "None":
        return True
    return not (13.25 < float(risk_points) <= 16.0)


def prepare_core(core: pd.DataFrame, period="H1") -> pd.DataFrame:
    rows = []
    for row in core[core["Period"].eq(period)].itertuples(index=False):
        strategy_normal = row.Classification == "Normal"
        trade_gross = gross3(float(row.ActualGross2), row.ResearchPath, row.ExitReason)
        rows.append(
            {
                "SnapshotID": row.SnapshotID,
                "TradeID": row.OriginalTradeID,
                "EntryTime": row.ActualEntryTime,
                "ExitTime": row.ActualExitTime,
                "TradingDate": row.TradingDate,
                "Month": row.Month,
                "Side": row.Side,
                "ResearchPath": row.ResearchPath,
                "InitialRiskPoints": float(row.InitialRiskPoints),
                "Source": "Core",
                "Priority": 0,
                "Classification": row.Classification,
                "CountsTowardLimit": strategy_normal,
                "CountsTowardGross": strategy_normal,
                "Gross3": trade_gross,
                "Net3": trade_gross - 3.6,
                "ParentTradeID": None,
            }
        )
    return pd.DataFrame(rows)


def prepare_released(rows: pd.DataFrame, mode: str, period="H1") -> pd.DataFrame:
    source = rows[
        ~rows["IsCore"]
        & rows["Period"].eq(period)
        & rows["OriginalDecision"].eq("Skip")
        & rows["ReasonHead"].isin(["DailyTradeLimit", "LiveDailyLoss"])
    ].copy()
    output = []
    for row in source.itertuples(index=False):
        net2 = float(row.ObservedNet2 if mode == "Observed" else row.ConservativeNet2)
        trade_gross = gross3(net2 + 2.4, row.ResearchPath, row.ExitReason)
        output.append(
            {
                "SnapshotID": row.SnapshotID,
                "TradeID": f"RELEASED|{row.SignalID}",
                "EntryTime": row.EntryTime,
                "ExitTime": row.ExitTime,
                "TradingDate": row.TradingDate,
                "Month": row.Month,
                "Side": row.Side,
                "ResearchPath": row.ResearchPath,
                "InitialRiskPoints": float(row.InitialRiskPoints),
                "Source": row.ReasonHead,
                "Priority": 1,
                "Classification": "Normal",
                "CountsTowardLimit": True,
                "CountsTowardGross": True,
                "Gross3": trade_gross,
                "Net3": trade_gross - 3.6,
                "ParentTradeID": None,
            }
        )
    return pd.DataFrame(output)


def prepare_deferred(
    deferred_module, rows, core, bars, target_r=2.0, max_bars=12, period="H1"
) -> pd.DataFrame:
    candidates = deferred_module.attach_parent(core, rows)
    candidates = candidates[candidates["Period"].eq(period)]
    outcomes = deferred_module.build_outcomes(
        candidates, bars, target_r, max_bars, "StopFirst"
    )
    output = []
    for row in outcomes.itertuples(index=False):
        trade_gross = (float(row.DeferredNet) + 2.4) * 1.5
        output.append(
            {
                "SnapshotID": row.SnapshotID,
                "TradeID": f"DEFERRED|{row.SignalID}",
                "EntryTime": row.DeferredEntryTime,
                "ExitTime": row.DeferredExitTime,
                "TradingDate": row.TradingDate,
                "Month": row.Month,
                "Side": row.Side,
                "ResearchPath": row.ResearchPath,
                "InitialRiskPoints": float(row.InitialRiskPoints),
                "Source": "DeferredContinuation",
                "Priority": 2,
                "Classification": "Normal",
                "CountsTowardLimit": True,
                "CountsTowardGross": True,
                "Gross3": trade_gross,
                "Net3": trade_gross - 3.6,
                "ParentTradeID": row.ParentTradeID,
            }
        )
    return pd.DataFrame(output)


def max_losing_streak(weekly: pd.Series) -> int:
    current = 0
    maximum = 0
    for value in weekly:
        current = current + 1 if value < 0 else 0
        maximum = max(maximum, current)
    return maximum


def summarize(trades: pd.DataFrame, all_dates: list, all_weeks: list) -> dict:
    normal = trades[trades["CountsTowardGross"]]
    weekly = trades.groupby("Week")["Net3"].sum().reindex(all_weeks, fill_value=0)
    daily = trades.groupby("TradingDate")["Net3"].sum().reindex(all_dates, fill_value=0)
    cumulative = daily.cumsum()
    positive = trades.loc[trades["Net3"] > 0, "Net3"].sum()
    negative = -trades.loc[trades["Net3"] < 0, "Net3"].sum()
    strategy_gross = float(normal["Gross3"].sum())
    return {
        "Trades": int(len(trades)),
        "NormalTrades": int(len(normal)),
        "Gross": round(strategy_gross, 2),
        "AccountNet": round(float(trades["Net3"].sum()), 2),
        "Commission": round(float(len(trades) * 3.6), 2),
        "PF": round(float(positive / negative), 4) if negative else float("inf"),
        "MaxDD": round(float((cumulative.cummax() - cumulative).max()), 2),
        "PositiveWeekPct": round(float((weekly > 0).mean() * 100), 2),
        "WeeklyMean": round(float(weekly.mean()), 2),
        "WeeklyMedian": round(float(weekly.median()), 2),
        "BestWeek": round(float(weekly.max()), 2),
        "WorstWeek": round(float(weekly.min()), 2),
        "MaxLosingWeeks": max_losing_streak(weekly),
        "BestWeekGrossSharePct": round(float(weekly.max() / strategy_gross * 100), 2)
        if strategy_gross > 0
        else 0,
        "JanFebGross": round(
            float(normal[normal["Month"].isin(["2026-01", "2026-02"])]["Gross3"].sum()), 2
        ),
        "MarAprGross": round(
            float(normal[normal["Month"].isin(["2026-03", "2026-04"])]["Gross3"].sum()), 2
        ),
        "MayJunGross": round(
            float(normal[normal["Month"].isin(["2026-05", "2026-06"])]["Gross3"].sum()), 2
        ),
    }


def simulate(
    core: pd.DataFrame,
    released: pd.DataFrame,
    deferred: pd.DataFrame,
    strict_gate: str,
    daily_cap: int,
    daily_loss: float,
    include_released: bool,
    include_deferred: bool,
):
    parts = [core]
    if include_released:
        parts.append(released)
    if include_deferred:
        parts.append(deferred)
    events = pd.concat(parts, ignore_index=True)
    accepted = []
    diagnostics = {
        "CoreAccepted": 0,
        "ReleasedAccepted": 0,
        "DeferredAccepted": 0,
        "ActiveBlocked": 0,
        "CapBlocked": 0,
        "LossBlocked": 0,
        "StrictGateBlocked": 0,
        "StaleParentBlocked": 0,
    }

    for snapshot_id, day in events.groupby("SnapshotID", sort=False):
        day = day.sort_values(["EntryTime", "Priority", "TradeID"])
        active = None
        realized = 0.0
        normal_count = 0
        accepted_core_ids = set()
        for row in day.itertuples(index=False):
            if active is not None and active["ExitTime"] <= row.EntryTime:
                realized += active["Net3"]
                active = None
            if active is not None:
                diagnostics["ActiveBlocked"] += 1
                continue
            if row.Source == "DeferredContinuation" and row.ParentTradeID not in accepted_core_ids:
                diagnostics["StaleParentBlocked"] += 1
                continue
            if row.CountsTowardLimit:
                if not strict_entry_allowed(row.InitialRiskPoints, strict_gate):
                    diagnostics["StrictGateBlocked"] += 1
                    continue
                if normal_count >= daily_cap:
                    diagnostics["CapBlocked"] += 1
                    continue
                if realized <= -daily_loss:
                    diagnostics["LossBlocked"] += 1
                    continue

            record = row._asdict()
            iso = row.TradingDate.isocalendar()
            record["Week"] = f"{iso.year}-W{iso.week:02d}"
            accepted.append(record)
            active = record
            if row.CountsTowardLimit:
                normal_count += 1
            if row.Source == "Core":
                diagnostics["CoreAccepted"] += 1
                accepted_core_ids.add(row.TradeID)
            elif row.Source == "DeferredContinuation":
                diagnostics["DeferredAccepted"] += 1
            else:
                diagnostics["ReleasedAccepted"] += 1
        if active is not None:
            realized += active["Net3"]
    return pd.DataFrame(accepted), diagnostics


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--rich", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    args = parser.parse_args()

    portfolio = load_module(
        "opf_v207_portfolio", Path(__file__).with_name("Analyze-OPFV207ProfitRiskPortfolio.py")
    )
    deferred_module = load_module(
        "opf_v207_deferred", Path(__file__).with_name("Analyze-OPFV207DeferredContinuation.py")
    )
    rows, core_source, _, _, _ = portfolio.load_candidates(args.evidence)
    bars = deferred_module.load_rich_bars(args.rich)
    core = prepare_core(core_source)
    deferred = prepare_deferred(deferred_module, rows, core_source, bars)
    all_dates = sorted(core["TradingDate"].unique())
    all_weeks = sorted(
        {f"{date.isocalendar().year}-W{date.isocalendar().week:02d}" for date in all_dates}
    )

    results = []
    traces = {}
    for mode in ("Observed", "Conservative"):
        released = prepare_released(rows, mode)
        for strict_gate in ("None", "ExcludeMidRisk13_25To16"):
            for daily_cap in (12, 15, 18, 21, 999):
                for daily_loss in (300, 450, 600, 750):
                    for include_deferred in (False, True):
                        config = (
                            f"{mode}|{strict_gate}|cap{daily_cap}|loss{daily_loss}|"
                            f"deferred{int(include_deferred)}"
                        )
                        trades, diagnostics = simulate(
                            core,
                            released,
                            deferred,
                            strict_gate,
                            daily_cap,
                            daily_loss,
                            True,
                            include_deferred,
                        )
                        result = {
                            "Config": config,
                            "Mode": mode,
                            "StrictGate": strict_gate,
                            "DailyCap": daily_cap,
                            "DailyLoss": daily_loss,
                            "DeferredEnabled": include_deferred,
                            **summarize(trades, all_dates, all_weeks),
                            **diagnostics,
                        }
                        result["TargetMet"] = result["Gross"] >= 20000
                        results.append(result)
                        traces[config] = trades

    summary = pd.DataFrame(results).sort_values(
        ["TargetMet", "Gross", "MaxDD", "PositiveWeekPct"],
        ascending=[False, False, True, False],
    )
    args.output_dir.mkdir(parents=True, exist_ok=True)
    summary.to_csv(args.output_dir / "v208_h1_portfolio_grid_summary.csv", index=False)
    top = summary.head(20)
    weekly_parts = []
    path_parts = []
    for config in top["Config"]:
        trades = traces[config]
        weekly = trades.groupby(["Week", "Source"])[["Gross3", "Net3"]].sum().reset_index()
        weekly.insert(0, "Config", config)
        weekly_parts.append(weekly)
        paths = (
            trades.groupby(["Source", "Side", "ResearchPath"])[["Gross3", "Net3"]]
            .agg(["count", "sum", "mean"])
            .reset_index()
        )
        paths.insert(0, "Config", config)
        path_parts.append(paths)
    pd.concat(weekly_parts, ignore_index=True).to_csv(
        args.output_dir / "v208_h1_portfolio_grid_weekly.csv", index=False
    )
    pd.concat(path_parts, ignore_index=True).to_csv(
        args.output_dir / "v208_h1_portfolio_grid_paths.csv", index=False
    )

    print("Top H1 three-contract portfolio configs")
    print(
        top[
            [
                "Config",
                "NormalTrades",
                "Gross",
                "AccountNet",
                "PF",
                "MaxDD",
                "PositiveWeekPct",
                "WorstWeek",
                "JanFebGross",
                "MarAprGross",
                "MayJunGross",
                "ReleasedAccepted",
                "DeferredAccepted",
                "TargetMet",
            ]
        ].to_string(index=False)
    )


if __name__ == "__main__":
    main()
