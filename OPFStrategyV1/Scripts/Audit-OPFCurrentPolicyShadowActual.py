import argparse
from pathlib import Path

import pandas as pd


COMMISSION_PER_CONTRACT = 1.2


def read_files(root: Path, pattern: str, usecols: list[str]) -> pd.DataFrame:
    files = list(root.rglob(pattern))
    if not files:
        return pd.DataFrame(columns=usecols)
    return pd.concat(
        (pd.read_csv(path, usecols=usecols, low_memory=False) for path in files),
        ignore_index=True,
    )


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--evidence", type=Path, action="append", required=True)
    parser.add_argument("--detail-output", type=Path, required=True)
    parser.add_argument("--summary-output", type=Path, required=True)
    args = parser.parse_args()

    daily_parts = []
    trade_parts = []
    shadow_parts = []
    for order, root in enumerate(args.evidence):
        daily = read_files(root, "*_regime_daily.csv", ["SnapshotID", "Date"])
        daily["EvidenceOrder"] = order
        daily_parts.append(daily)

        trades = read_files(
            root,
            "*_execution_trades.csv",
            [
                "SnapshotID",
                "SignalID",
                "TradeID",
                "Side",
                "ResearchPath",
                "Quantity",
                "EntryPrice",
                "FilledRiskPoints",
                "Dollars",
                "ExitRole",
                "IsAbnormalExecution",
            ],
        )
        trades["EvidenceOrder"] = order
        trade_parts.append(trades)

        shadows = read_files(
            root,
            "*_shadow_trades.csv",
            [
                "SnapshotID",
                "SignalID",
                "Side",
                "ResearchPath",
                "Entry",
                "InitialRiskPoints",
                "ExitReason",
                "NetDollars",
                "Ambiguous",
                "OriginalTradeID",
            ],
        )
        shadows["EvidenceOrder"] = order
        shadow_parts.append(shadows)

    daily = pd.concat(daily_parts, ignore_index=True)
    daily["Date"] = daily["Date"].astype(str)
    selected = (
        daily.sort_values("EvidenceOrder")
        .drop_duplicates("Date", keep="last")
        .loc[:, ["Date", "SnapshotID"]]
    )
    selected_snapshots = set(selected["SnapshotID"].astype(str))
    date_by_snapshot = dict(zip(selected["SnapshotID"].astype(str), selected["Date"]))

    trades = pd.concat(trade_parts, ignore_index=True)
    trades = trades[
        trades["SnapshotID"].astype(str).isin(selected_snapshots)
        & ~trades["IsAbnormalExecution"].astype(str).eq("True")
    ].copy()
    trades["TradingDate"] = trades["SnapshotID"].astype(str).map(date_by_snapshot)
    trades["ActualNetDollars"] = (
        trades["Dollars"].astype(float)
        - trades["Quantity"].astype(float) * COMMISSION_PER_CONTRACT
    )

    shadows = pd.concat(shadow_parts, ignore_index=True)
    shadows = shadows[
        shadows["SnapshotID"].astype(str).isin(selected_snapshots)
        & shadows["OriginalTradeID"].fillna("").ne("")
    ].copy()
    shadows = shadows.drop_duplicates("OriginalTradeID", keep="last")
    shadows.rename(
        columns={
            "SignalID": "ShadowSignalID",
            "Side": "ShadowSide",
            "ResearchPath": "ShadowResearchPath",
            "Entry": "ShadowEntry",
            "InitialRiskPoints": "ShadowRiskPoints",
            "ExitReason": "ShadowExitReason",
            "NetDollars": "ShadowNetDollars",
            "Ambiguous": "ShadowAmbiguous",
        },
        inplace=True,
    )

    rows = trades.merge(
        shadows[
            [
                "OriginalTradeID",
                "ShadowSignalID",
                "ShadowSide",
                "ShadowResearchPath",
                "ShadowEntry",
                "ShadowRiskPoints",
                "ShadowExitReason",
                "ShadowNetDollars",
                "ShadowAmbiguous",
            ]
        ],
        left_on="TradeID",
        right_on="OriginalTradeID",
        how="left",
        validate="one_to_one",
    )
    if rows["ShadowNetDollars"].isna().any():
        missing = rows.loc[rows["ShadowNetDollars"].isna(), ["TradingDate", "TradeID"]]
        raise ValueError("Actual trades without exact shadow:\n" + missing.to_string(index=False))
    if not rows["Side"].eq(rows["ShadowSide"]).all():
        raise ValueError("Side mismatch between Actual and exact shadow")
    if not rows["ResearchPath"].eq(rows["ShadowResearchPath"]).all():
        raise ValueError("ResearchPath mismatch between Actual and exact shadow")

    rows["Period"] = rows["TradingDate"].where(
        rows["TradingDate"].str.startswith("2025"), "H1"
    )
    rows.loc[rows["Period"].ne("H1"), "Period"] = "Q4"
    rows["EntryDriftPoints"] = rows["EntryPrice"].astype(float) - rows["ShadowEntry"].astype(float)
    rows["RiskDriftPoints"] = (
        rows["FilledRiskPoints"].astype(float) - rows["ShadowRiskPoints"].astype(float)
    )
    rows["ShadowBiasDollars"] = (
        rows["ShadowNetDollars"].astype(float) - rows["ActualNetDollars"]
    )
    rows["SignMismatch"] = (
        rows["ShadowNetDollars"].astype(float).gt(0)
        != rows["ActualNetDollars"].gt(0)
    )

    detail_columns = [
        "Period",
        "TradingDate",
        "SnapshotID",
        "SignalID",
        "TradeID",
        "Side",
        "ResearchPath",
        "EntryPrice",
        "ShadowEntry",
        "EntryDriftPoints",
        "FilledRiskPoints",
        "ShadowRiskPoints",
        "RiskDriftPoints",
        "ExitRole",
        "ShadowExitReason",
        "ActualNetDollars",
        "ShadowNetDollars",
        "ShadowBiasDollars",
        "SignMismatch",
        "ShadowAmbiguous",
    ]
    detail = rows[detail_columns].copy()

    def summarize(group: pd.DataFrame) -> pd.Series:
        return pd.Series(
            {
                "Trades": len(group),
                "ActualNetDollars": group["ActualNetDollars"].sum(),
                "ShadowNetDollars": group["ShadowNetDollars"].sum(),
                "ShadowBiasDollars": group["ShadowBiasDollars"].sum(),
                "BiasPerTrade": group["ShadowBiasDollars"].mean(),
                "MedianBiasPerTrade": group["ShadowBiasDollars"].median(),
                "MeanAbsEntryDrift": group["EntryDriftPoints"].abs().mean(),
                "MeanAbsRiskDrift": group["RiskDriftPoints"].abs().mean(),
                "SignMismatchTrades": int(group["SignMismatch"].sum()),
                "AmbiguousShadowTrades": int(
                    group["ShadowAmbiguous"].astype(str).eq("True").sum()
                ),
            }
        )

    summary_parts = []
    for period, group in rows.groupby("Period"):
        item = summarize(group).to_frame().T
        item.insert(0, "Scope", period)
        summary_parts.append(item)
    for (period, side, path), group in rows.groupby(["Period", "Side", "ResearchPath"]):
        item = summarize(group).to_frame().T
        item.insert(0, "Scope", f"{period}|{side}|{path}")
        summary_parts.append(item)
    combined = summarize(rows).to_frame().T
    combined.insert(0, "Scope", "Combined")
    summary = pd.concat([*summary_parts, combined], ignore_index=True)

    numeric = detail.select_dtypes(include="number").columns
    detail[numeric] = detail[numeric].round(4)
    summary = summary.round(4)
    args.detail_output.parent.mkdir(parents=True, exist_ok=True)
    detail.to_csv(args.detail_output, index=False)
    summary.to_csv(args.summary_output, index=False)
    print(summary[summary["Scope"].isin(["Q4", "H1", "Combined"])].to_string(index=False))


if __name__ == "__main__":
    main()
