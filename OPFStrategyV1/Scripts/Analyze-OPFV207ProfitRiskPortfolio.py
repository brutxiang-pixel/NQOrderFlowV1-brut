import argparse
from pathlib import Path

import numpy as np
import pandas as pd


KEY = ["SnapshotID", "SignalID", "ResearchPath"]


def read_csvs(root: Path, pattern: str) -> pd.DataFrame:
    files = sorted(root.glob(pattern))
    if not files:
        raise FileNotFoundError(f"No files matched {pattern} in {root}")
    return pd.concat((pd.read_csv(path, low_memory=False) for path in files), ignore_index=True)


def scale_net(gross: float, quantity: int) -> float:
    return gross * quantity / 2 - 1.2 * quantity


def load_candidates(root: Path):
    shadows = read_csvs(root, "*_shadow_trades.csv")
    pnl = read_csvs(root, "*_live_account_pnl.csv")

    for column in ("EntryTime", "ExitTime"):
        shadows[column] = pd.to_datetime(shadows[column])
        pnl[column] = pd.to_datetime(pnl[column])

    trading_date = shadows.groupby("SnapshotID")["EntryTime"].max().dt.normalize()
    shadows["TradingDate"] = shadows["SnapshotID"].map(trading_date)
    shadows["Month"] = shadows["TradingDate"].dt.to_period("M").astype(str)
    shadows["Period"] = np.where(shadows["TradingDate"].dt.year.eq(2025), "Q4", "H1")
    hour = shadows["EntryTime"].dt.hour
    shadows["Session"] = pd.cut(
        hour, [-1, 5, 12, 19, 23], labels=["Asia", "Europe", "US", "Late"]
    ).astype(str)
    shadows["ReasonHead"] = shadows["OriginalReason"].fillna("").str.extract(
        r"^([^:|]+)", expand=False
    )

    actual_columns = [
        "SnapshotID",
        "TradeID",
        "EntryTime",
        "ExitTime",
        "Classification",
        "EntryPrice",
        "ExitPrice",
        "GrossPnLDollars",
        "CommissionDollars",
        "NetPnLDollars",
    ]
    actual = pnl[actual_columns].rename(
        columns={
            "TradeID": "OriginalTradeID",
            "EntryTime": "ActualEntryTime",
            "ExitTime": "ActualExitTime",
            "EntryPrice": "ActualEntryPrice",
            "ExitPrice": "ActualExitPrice",
            "GrossPnLDollars": "ActualGross2",
            "CommissionDollars": "ActualCommission2",
            "NetPnLDollars": "ActualNet2",
        }
    )
    rows = shadows.merge(actual, on=["SnapshotID", "OriginalTradeID"], how="left")
    rows["IsCore"] = rows["ActualNet2"].notna()

    # HistoricalReplayNormalized can collapse the account-ledger ExitTime to
    # EntryTime. Keep Actual prices/PnL, but use the precise shadow lifecycle
    # for occupancy and parent/child timing.

    matched = rows[rows["IsCore"] & rows["Classification"].eq("Normal")].copy()
    matched["Bias2"] = matched["NetDollars"] - matched["ActualNet2"]
    global_bias = float(matched["Bias2"].mean())
    grouped = matched.groupby(["Side", "ResearchPath"])["Bias2"].agg(["count", "mean", "std"])
    grouped["UsedBias2"] = np.where(
        grouped["count"].ge(20), grouped["mean"].clip(lower=0), max(global_bias, 0)
    )
    bias = grouped.reset_index()
    bias_map = bias.set_index(["Side", "ResearchPath"])["UsedBias2"].to_dict()

    rows["ObservedNet2"] = rows["NetDollars"].astype(float)
    ambiguous = rows["Ambiguous"].astype(str).eq("True")
    rows["ConservativeNet2"] = rows["ObservedNet2"]
    rows.loc[ambiguous, "ConservativeNet2"] = (
        -4 * rows.loc[ambiguous, "InitialRiskPoints"].astype(float) - 2.4
    )
    rows["BiasApplied2"] = [
        bias_map.get((side, path), max(global_bias, 0))
        for side, path in zip(rows["Side"], rows["ResearchPath"])
    ]
    rows["ConservativeNet2"] -= rows["BiasApplied2"].clip(lower=0)
    rows.loc[rows["IsCore"], "ObservedNet2"] = rows.loc[rows["IsCore"], "ActualNet2"]
    rows.loc[rows["IsCore"], "ConservativeNet2"] = rows.loc[rows["IsCore"], "ActualNet2"]

    core = rows[rows["IsCore"]].copy()
    secondary = rows[
        ~rows["IsCore"]
        & rows["OriginalDecision"].eq("Skip")
        & rows["ReasonHead"].isin(["ActiveTrade", "DailyTradeLimit"])
    ].copy()
    return rows, core, secondary, bias, global_bias


def add_lomo_scores(secondary: pd.DataFrame, alphas: list[int]) -> pd.DataFrame:
    parts = []
    group_columns = ["Side", "ResearchPath", "Session"]
    secondary = secondary.copy()
    secondary["LabelPerContract"] = (secondary["ConservativeNet2"] + 2.4) / 2 - 1.2
    for alpha in alphas:
        scored_months = []
        for month in sorted(secondary["Month"].unique()):
            train = secondary[secondary["Month"].ne(month)]
            test = secondary[secondary["Month"].eq(month)].copy()
            global_mean = float(train["LabelPerContract"].mean())
            grouped = train.groupby(group_columns)["LabelPerContract"].agg(["sum", "count"])
            grouped["Score"] = (grouped["sum"] + alpha * global_mean) / (
                grouped["count"] + alpha
            )
            score_map = grouped["Score"].to_dict()
            test["ScorePerContract"] = [
                score_map.get((side, path, session), global_mean)
                for side, path, session in zip(test["Side"], test["ResearchPath"], test["Session"])
            ]
            period_stats = (
                train.groupby(["Side", "ResearchPath", "Period"])["LabelPerContract"]
                .agg(["count", "sum"])
                .reset_index()
            )
            robust_paths = set()
            for (side, path), group in period_stats.groupby(["Side", "ResearchPath"]):
                by_period = group.set_index("Period")
                if (
                    {"Q4", "H1"}.issubset(by_period.index)
                    and int(by_period["count"].sum()) >= 20
                    and int(by_period.loc["Q4", "count"]) >= 5
                    and int(by_period.loc["H1", "count"]) >= 5
                    and float(by_period.loc["Q4", "sum"]) > 0
                    and float(by_period.loc["H1", "sum"]) > 0
                ):
                    robust_paths.add((side, path))
            test["RobustPathEligible"] = [
                (side, path) in robust_paths
                for side, path in zip(test["Side"], test["ResearchPath"])
            ]
            test["Alpha"] = alpha
            scored_months.append(test)
        parts.append(pd.concat(scored_months, ignore_index=True))
    return pd.concat(parts, ignore_index=True)


def trade_net(row, mode: str, quantity: int) -> float:
    if row.IsCore:
        return scale_net(float(row.ActualGross2), quantity)
    net2 = float(row.ObservedNet2 if mode == "Observed" else row.ConservativeNet2)
    gross2 = net2 + 2.4
    return scale_net(gross2, quantity)


def metrics(trades: pd.DataFrame, day_order: pd.DataFrame) -> dict:
    if trades.empty:
        return {
            "Trades": 0,
            "Net": 0.0,
            "PF": 0.0,
            "MaxDD": 0.0,
            "WinningDayPct": 0.0,
            "Q4Net": 0.0,
            "H1Net": 0.0,
        }
    positive = trades.loc[trades["Net"] > 0, "Net"].sum()
    negative = -trades.loc[trades["Net"] < 0, "Net"].sum()
    daily = trades.groupby("SnapshotID")["Net"].sum().reindex(day_order["SnapshotID"], fill_value=0)
    cumulative = daily.cumsum()
    max_dd = float((cumulative.cummax() - cumulative).max())
    return {
        "Trades": len(trades),
        "Net": round(float(trades["Net"].sum()), 2),
        "PF": round(float(positive / negative), 4) if negative else float("inf"),
        "MaxDD": round(max_dd, 2),
        "WinningDayPct": round(float((daily > 0).mean() * 100), 2),
        "Q4Net": round(float(trades.loc[trades["Period"].eq("Q4"), "Net"].sum()), 2),
        "H1Net": round(float(trades.loc[trades["Period"].eq("H1"), "Net"].sum()), 2),
    }


def simulate(
    core_by_day: dict,
    secondary_by_day: dict,
    mode: str,
    core_quantity: int,
    secondary_quantity: int,
    risk_cap: float,
    secondary_limit: int,
    secondary_loss_limit: float,
    combined_loss_limit: float,
    threshold: float,
    robust_only: bool,
    allow_same_direction: bool,
    require_same_direction: bool,
    source: str,
):
    accepted = []
    diagnostics = {
        "SecondaryAccepted": 0,
        "SecondaryNet": 0.0,
        "SameDirectionOverlap": 0,
        "RiskBlocked": 0,
        "LossBlocked": 0,
        "LimitBlocked": 0,
        "DirectionBlocked": 0,
        "PeakRisk": 0.0,
    }
    for snapshot_id, core_day in core_by_day.items():
        sec_day = secondary_by_day.get(snapshot_id)
        if sec_day is None:
            sec_day = pd.DataFrame(
                columns=["ReasonHead", "ScorePerContract", "RobustPathEligible", "EntryTime"]
            )
        else:
            sec_day = sec_day.copy()
        if source == "ActiveTradeOnly":
            sec_day = sec_day[sec_day["ReasonHead"].eq("ActiveTrade")]
        if robust_only:
            sec_day = sec_day.loc[sec_day["RobustPathEligible"].fillna(False).astype(bool)]
        sec_day = sec_day[sec_day["ScorePerContract"].ge(threshold)].sort_values(
            ["EntryTime", "ScorePerContract"], ascending=[True, False]
        )
        core_day = core_day.sort_values("EntryTime")

        core_records = []
        for row in core_day.itertuples(index=False):
            net = trade_net(row, mode, core_quantity)
            risk = float(row.InitialRiskPoints) * 2 * core_quantity
            record = {
                "SnapshotID": snapshot_id,
                "EntryTime": row.ActualEntryTime,
                "ExitTime": row.ActualExitTime,
                "Side": row.Side,
                "ResearchPath": row.ResearchPath,
                "Period": row.Period,
                "Month": row.Month,
                "Lane": "Core",
                "Net": net,
                "Risk": risk,
            }
            core_records.append(record)
            accepted.append(record)

        secondary_active = None
        secondary_realized = 0.0
        secondary_count = 0
        for row in sec_day.itertuples(index=False):
            if secondary_active is not None and secondary_active["ExitTime"] <= row.EntryTime:
                secondary_realized += secondary_active["Net"]
                secondary_active = None
            if secondary_active is not None:
                continue
            if secondary_count >= secondary_limit:
                diagnostics["LimitBlocked"] += 1
                continue

            core_realized = sum(r["Net"] for r in core_records if r["ExitTime"] <= row.EntryTime)
            if secondary_realized <= -secondary_loss_limit or core_realized + secondary_realized <= -combined_loss_limit:
                diagnostics["LossBlocked"] += 1
                continue

            active_core = [r for r in core_records if r["EntryTime"] <= row.EntryTime < r["ExitTime"]]
            if (
                not allow_same_direction
                and active_core
                and any(r["Side"] == row.Side for r in active_core)
            ):
                diagnostics["DirectionBlocked"] += 1
                continue
            if require_same_direction and not any(r["Side"] == row.Side for r in active_core):
                diagnostics["DirectionBlocked"] += 1
                continue
            secondary_risk = float(row.InitialRiskPoints) * 2 * secondary_quantity
            combined_risk = secondary_risk + sum(r["Risk"] for r in active_core)
            if combined_risk > risk_cap:
                diagnostics["RiskBlocked"] += 1
                continue

            net = trade_net(row, mode, secondary_quantity)
            record = {
                "SnapshotID": snapshot_id,
                "EntryTime": row.EntryTime,
                "ExitTime": row.ExitTime,
                "Side": row.Side,
                "ResearchPath": row.ResearchPath,
                "Period": row.Period,
                "Month": row.Month,
                "Lane": "Secondary",
                "Net": net,
                "Risk": secondary_risk,
            }
            accepted.append(record)
            secondary_active = record
            secondary_count += 1
            diagnostics["SecondaryAccepted"] += 1
            diagnostics["PeakRisk"] = max(diagnostics["PeakRisk"], combined_risk)
            if active_core and any(r["Side"] == row.Side for r in active_core):
                diagnostics["SameDirectionOverlap"] += 1

        if secondary_active is not None:
            secondary_realized += secondary_active["Net"]
        diagnostics["SecondaryNet"] += secondary_realized

    trades = pd.DataFrame(accepted)
    diagnostics["SecondaryNet"] = round(diagnostics["SecondaryNet"], 2)
    if diagnostics["SecondaryAccepted"]:
        diagnostics["SameDirectionOverlapPct"] = round(
            diagnostics["SameDirectionOverlap"] / diagnostics["SecondaryAccepted"] * 100, 2
        )
        diagnostics["SecondaryExpectancy"] = round(
            diagnostics["SecondaryNet"] / diagnostics["SecondaryAccepted"], 2
        )
    else:
        diagnostics["SameDirectionOverlapPct"] = 0.0
        diagnostics["SecondaryExpectancy"] = 0.0
    return trades, diagnostics


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    args = parser.parse_args()

    _, core, secondary, bias, global_bias = load_candidates(args.evidence)
    alphas = [20]
    scored = add_lomo_scores(secondary, alphas)
    day_order = (
        core.groupby("SnapshotID")["EntryTime"].min().sort_values().reset_index()[["SnapshotID"]]
    )

    baseline_trades = pd.DataFrame(
        {
            "SnapshotID": core["SnapshotID"],
            "EntryTime": core["EntryTime"],
            "ExitTime": core["ExitTime"],
            "Side": core["Side"],
            "ResearchPath": core["ResearchPath"],
            "Period": core["Period"],
            "Month": core["Month"],
            "Lane": "Core",
            "Net": core["ActualNet2"].astype(float),
            "Risk": core["InitialRiskPoints"].astype(float) * 4,
        }
    )
    baseline = metrics(baseline_trades, day_order)
    baseline_ratio = baseline["Net"] / baseline["MaxDD"]

    arms = [
        ("EqualRisk_1plus1_R200", 1, 1, 200, 100, 300),
        ("Moderate_2plus1_R300", 2, 1, 300, 150, 300),
        ("Moderate_2plus1_R350", 2, 1, 350, 150, 400),
        ("Upper_2plus2_R400", 2, 2, 400, 200, 400),
        ("Upper_2plus2_R500", 2, 2, 500, 250, 600),
    ]
    results = []
    traces = {}
    for alpha in alphas:
        alpha_rows = scored[scored["Alpha"].eq(alpha)]
        core_by_day = {key: value for key, value in core.groupby("SnapshotID", sort=False)}
        secondary_by_day = {
            key: value for key, value in alpha_rows.groupby("SnapshotID", sort=False)
        }
        for arm, core_qty, sec_qty, risk_cap, sec_loss, total_loss in arms:
            source = "ActiveTradeOnly"
            secondary_limit = 3
            selections = [
                ("All", -999, False),
                ("Score0", 0, False),
                ("Score3", 3, False),
                ("Robust", -999, True),
                ("RobustScore0", 0, True),
            ]
            for selection, threshold, robust_only in selections:
                config = f"{arm}|{source}|sec{secondary_limit}|a{alpha}|{selection}"
                for mode in ("Observed", "Conservative"):
                    trades, diag = simulate(
                        core_by_day,
                        secondary_by_day,
                        mode,
                        core_qty,
                        sec_qty,
                        risk_cap,
                        secondary_limit,
                        sec_loss,
                        total_loss,
                        threshold,
                        robust_only,
                        True,
                        False,
                        source,
                    )
                    row = {
                        "Config": config,
                        "Mode": mode,
                        "Arm": arm,
                        "Source": source,
                        "SecondaryLimit": secondary_limit,
                        "Alpha": alpha,
                        "Selection": selection,
                        "ThresholdPerContract": threshold,
                        "CoreQuantity": core_qty,
                        "SecondaryQuantity": sec_qty,
                        "RiskCap": risk_cap,
                        "SecondaryLossLimit": sec_loss,
                        "CombinedLossLimit": total_loss,
                        **metrics(trades, day_order),
                        **diag,
                    }
                    results.append(row)
                    traces[(config, mode)] = trades

    summary = pd.DataFrame(results)
    conservative = summary[summary["Mode"].eq("Conservative")].copy()
    conservative["NetDelta"] = conservative["Net"] - baseline["Net"]
    conservative["PFDelta"] = conservative["PF"] - baseline["PF"]
    conservative["Q4Delta"] = conservative["Q4Net"] - baseline["Q4Net"]
    conservative["H1Delta"] = conservative["H1Net"] - baseline["H1Net"]
    conservative["ReturnDD"] = conservative["Net"] / conservative["MaxDD"].replace(0, np.nan)
    conservative["ParetoEligible"] = (
        conservative["Net"].gt(baseline["Net"])
        & conservative["PF"].gt(baseline["PF"])
        & conservative["SecondaryNet"].gt(0)
        & conservative["Q4Delta"].ge(-500)
        & conservative["H1Delta"].ge(-500)
        & (
            conservative["MaxDD"].le(baseline["MaxDD"])
            | conservative["ReturnDD"].ge(baseline_ratio * 1.3)
        )
    )
    summary = summary.merge(
        conservative[
            ["Config", "NetDelta", "PFDelta", "Q4Delta", "H1Delta", "ReturnDD", "ParetoEligible"]
        ],
        on="Config",
        how="left",
    )

    args.output_dir.mkdir(parents=True, exist_ok=True)
    summary.to_csv(args.output_dir / "v207_profit_risk_portfolio_summary.csv", index=False)
    bias.assign(GlobalBias2=global_bias).to_csv(
        args.output_dir / "v207_profit_risk_shadow_bias.csv", index=False
    )

    top = conservative.sort_values(
        ["ParetoEligible", "Net", "PF"], ascending=[False, False, False]
    ).head(20)
    detail_parts = []
    monthly_parts = []
    for config in top["Config"]:
        for mode in ("Observed", "Conservative"):
            trades = traces[(config, mode)]
            path = (
                trades.groupby(["Lane", "Side", "ResearchPath"])["Net"]
                .agg(["count", "sum", "mean"])
                .reset_index()
            )
            path.insert(0, "Mode", mode)
            path.insert(0, "Config", config)
            detail_parts.append(path)
            month = trades.groupby(["Month", "Lane"])["Net"].agg(["count", "sum"]).reset_index()
            month.insert(0, "Mode", mode)
            month.insert(0, "Config", config)
            monthly_parts.append(month)
    pd.concat(detail_parts, ignore_index=True).to_csv(
        args.output_dir / "v207_profit_risk_top_path_contribution.csv", index=False
    )
    pd.concat(monthly_parts, ignore_index=True).to_csv(
        args.output_dir / "v207_profit_risk_top_monthly.csv", index=False
    )

    print("Baseline:")
    print(pd.Series(baseline).to_string())
    print(f"Matched normal shadow global overestimate: ${global_bias:.2f} per 2-contract trade")
    print("\nTop conservative configurations:")
    columns = [
        "Config",
        "Net",
        "PF",
        "MaxDD",
        "Q4Net",
        "H1Net",
        "SecondaryAccepted",
        "SecondaryNet",
        "SecondaryExpectancy",
        "PeakRisk",
        "SameDirectionOverlapPct",
        "ParetoEligible",
    ]
    print(top[columns].to_string(index=False))


if __name__ == "__main__":
    main()
