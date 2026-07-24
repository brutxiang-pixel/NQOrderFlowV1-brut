import argparse
from pathlib import Path

import pandas as pd


WIDE_PATH = "ObservationConfirm_WideStop1_5R"
POINT_VALUE_TWO_CONTRACTS = 4.0
TARGET_R = 2.5
TIME_STOP_BARS = 12


def read_csvs(roots: list[Path], pattern: str, usecols: list[str]) -> pd.DataFrame:
    files = [path for root in roots for path in root.rglob(pattern)]
    if not files:
        raise ValueError(f"No files matched {pattern}")
    return pd.concat(
        (pd.read_csv(path, usecols=usecols, low_memory=False) for path in files),
        ignore_index=True,
    )


def resolve_actual_fill_policy(trade, bars: pd.DataFrame) -> dict:
    entry = float(trade.EntryPrice)
    risk = float(trade.FilledRiskPoints)
    stop = entry - risk
    target = entry + TARGET_R * risk
    horizon = int(trade.EntryBar) + TIME_STOP_BARS
    window = bars[(bars["Bar"] > int(trade.EntryBar)) & (bars["Bar"] <= horizon)].sort_values("Bar")
    if window.empty:
        return {"Status": "MissingBars"}

    for bar in window.itertuples(index=False):
        stop_touched = float(bar.Low) <= stop
        target_touched = float(bar.High) >= target
        if stop_touched or target_touched:
            ambiguous = stop_touched and target_touched
            if stop_touched:
                points = -risk
                reason = "Stop"
                exit_price = stop
            else:
                points = TARGET_R * risk
                reason = "Target"
                exit_price = target
            return {
                "Status": "Resolved",
                "ExitReason": reason,
                "ExitBar": int(bar.Bar),
                "ExitPrice": exit_price,
                "GrossDollars": points * POINT_VALUE_TWO_CONTRACTS,
                "Ambiguous": ambiguous,
                "SessionEndTruncated": False,
                "LowerGrossDollars": (
                    -risk * POINT_VALUE_TWO_CONTRACTS
                    if ambiguous
                    else points * POINT_VALUE_TWO_CONTRACTS
                ),
                "UpperGrossDollars": (
                    TARGET_R * risk * POINT_VALUE_TWO_CONTRACTS
                    if ambiguous
                    else points * POINT_VALUE_TWO_CONTRACTS
                ),
            }

    close = float(window.iloc[-1]["Close"])
    points = close - entry
    exit_bar = int(window.iloc[-1]["Bar"])
    return {
        "Status": "Resolved",
        "ExitReason": "TimeStop",
        "ExitBar": exit_bar,
        "ExitPrice": close,
        "GrossDollars": points * POINT_VALUE_TWO_CONTRACTS,
        "Ambiguous": False,
        "SessionEndTruncated": exit_bar < horizon,
        "LowerGrossDollars": points * POINT_VALUE_TWO_CONTRACTS,
        "UpperGrossDollars": points * POINT_VALUE_TWO_CONTRACTS,
    }


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--evidence", type=Path, action="append", required=True)
    parser.add_argument("--rich", type=Path, required=True)
    parser.add_argument("--detail-output", type=Path, required=True)
    parser.add_argument("--summary-output", type=Path, required=True)
    args = parser.parse_args()

    daily = read_csvs(args.evidence, "*_regime_daily.csv", ["SnapshotID", "Date"])
    daily["Date"] = daily["Date"].astype(str)
    date_by_snapshot = dict(zip(daily["SnapshotID"].astype(str), daily["Date"]))

    trades = read_csvs(
        args.evidence,
        "*_execution_trades.csv",
        [
            "SnapshotID",
            "SignalID",
            "TradeID",
            "EntryTime",
            "EntryBar",
            "Side",
            "ResearchPath",
            "EntryPrice",
            "FilledRiskPoints",
            "Dollars",
            "IsAbnormalExecution",
        ],
    )
    trades = trades[
        trades["Side"].eq("Long")
        & trades["ResearchPath"].eq(WIDE_PATH)
        & ~trades["IsAbnormalExecution"].astype(str).eq("True")
    ].copy()
    trades["TradingDate"] = trades["SnapshotID"].astype(str).map(date_by_snapshot)
    if trades["TradingDate"].isna().any():
        raise ValueError("Some Actual trades have no trading-date mapping")

    policies = read_csvs(
        args.evidence,
        "*_exit_policy_evaluations.csv",
        [
            "SnapshotID",
            "SignalID",
            "EntryBar",
            "ResearchPath",
            "ExitPolicy",
            "Entry",
            "InitialRiskPoints",
            "ExitReason",
            "PolicyExitBar",
            "PnLDollars",
        ],
    )
    policies = policies[
        policies["ResearchPath"].eq(WIDE_PATH) & policies["ExitPolicy"].eq("Fixed2_5R")
    ].drop_duplicates(["SnapshotID", "SignalID", "EntryBar"])
    policies.rename(
        columns={
            "Entry": "PlannedEntry",
            "InitialRiskPoints": "PlannedRisk",
            "ExitReason": "PlannedExitReason",
            "PolicyExitBar": "PlannedExitBar",
            "PnLDollars": "PlannedGrossDollars",
        },
        inplace=True,
    )
    rows = trades.merge(
        policies,
        on=["SnapshotID", "SignalID", "EntryBar", "ResearchPath"],
        how="left",
        validate="one_to_one",
    )
    if rows["PlannedExitReason"].isna().any():
        raise ValueError("Some matched Actual W trades have no Fixed2_5R policy row")

    selected = pd.read_csv(args.rich / "selected_snapshots.csv", dtype={"Date": str, "SnapshotId": str})
    rich_file_by_date = {
        row.Date: args.rich / f"{row.SnapshotId}_rich_bar_features.csv"
        for row in selected.itertuples(index=False)
    }
    rich_cache: dict[str, pd.DataFrame] = {}
    results = []
    for trade in rows.itertuples(index=False):
        date = str(trade.TradingDate)
        if date not in rich_file_by_date:
            raise ValueError(f"Missing Rich date {date}")
        if date not in rich_cache:
            rich_cache[date] = pd.read_csv(
                rich_file_by_date[date], usecols=["Bar", "High", "Low", "Close"]
            )
        corrected = resolve_actual_fill_policy(trade, rich_cache[date])
        results.append(
            {
                "Period": "Q4" if date < "2026-01-01" else "H1",
                "TradingDate": date,
                "SnapshotID": trade.SnapshotID,
                "SignalID": trade.SignalID,
                "TradeID": trade.TradeID,
                "EntryTime": trade.EntryTime,
                "EntryBar": int(trade.EntryBar),
                "PlannedEntry": float(trade.PlannedEntry),
                "ActualEntry": float(trade.EntryPrice),
                "PlannedRisk": float(trade.PlannedRisk),
                "ActualRisk": float(trade.FilledRiskPoints),
                "BaselineActualGrossDollars": float(trade.Dollars),
                "PlannedExitReason": trade.PlannedExitReason,
                "PlannedExitBar": int(trade.PlannedExitBar),
                "PlannedGrossDollars": float(trade.PlannedGrossDollars),
                "CorrectedStatus": corrected["Status"],
                "CorrectedExitReason": corrected.get("ExitReason", ""),
                "CorrectedExitBar": corrected.get("ExitBar", ""),
                "CorrectedExitPrice": corrected.get("ExitPrice", ""),
                "CorrectedGrossDollars": corrected.get("GrossDollars", ""),
                "CorrectionDelta": (
                    corrected.get("GrossDollars", 0) - float(trade.PlannedGrossDollars)
                    if corrected["Status"] == "Resolved"
                    else ""
                ),
                "CorrectedVsActualDelta": (
                    corrected.get("GrossDollars", 0) - float(trade.Dollars)
                    if corrected["Status"] == "Resolved"
                    else ""
                ),
                "Ambiguous": corrected.get("Ambiguous", ""),
                "SessionEndTruncated": corrected.get("SessionEndTruncated", ""),
                "LowerGrossDollars": corrected.get("LowerGrossDollars", ""),
                "UpperGrossDollars": corrected.get("UpperGrossDollars", ""),
            }
        )

    detail = pd.DataFrame(results)
    if detail["CorrectedStatus"].ne("Resolved").any():
        unresolved = detail[detail["CorrectedStatus"].ne("Resolved")]
        columns = [
            "TradingDate",
            "SignalID",
            "TradeID",
            "EntryBar",
            "PlannedExitReason",
            "PlannedExitBar",
        ]
        raise ValueError(
            "Actual-fill correction has unresolved trades:\n"
            + unresolved[columns].to_string(index=False)
        )
    numeric = [
        "PlannedGrossDollars",
        "BaselineActualGrossDollars",
        "CorrectedGrossDollars",
        "CorrectionDelta",
        "CorrectedVsActualDelta",
        "LowerGrossDollars",
        "UpperGrossDollars",
    ]
    detail[numeric] = detail[numeric].astype(float).round(2)
    summary = (
        detail.groupby("Period", as_index=False)
        .agg(
            Trades=("TradeID", "size"),
            BaselineActualGrossDollars=("BaselineActualGrossDollars", "sum"),
            PlannedGrossDollars=("PlannedGrossDollars", "sum"),
            CorrectedGrossDollars=("CorrectedGrossDollars", "sum"),
            CorrectionDelta=("CorrectionDelta", "sum"),
            CorrectedVsActualDelta=("CorrectedVsActualDelta", "sum"),
            AmbiguousTrades=("Ambiguous", "sum"),
            SessionEndTruncatedTrades=("SessionEndTruncated", "sum"),
            LowerGrossDollars=("LowerGrossDollars", "sum"),
            UpperGrossDollars=("UpperGrossDollars", "sum"),
        )
    )
    total = pd.DataFrame(
        [
            {
                "Period": "Combined",
                "Trades": len(detail),
                "BaselineActualGrossDollars": detail["BaselineActualGrossDollars"].sum(),
                "PlannedGrossDollars": detail["PlannedGrossDollars"].sum(),
                "CorrectedGrossDollars": detail["CorrectedGrossDollars"].sum(),
                "CorrectionDelta": detail["CorrectionDelta"].sum(),
                "CorrectedVsActualDelta": detail["CorrectedVsActualDelta"].sum(),
                "AmbiguousTrades": int(detail["Ambiguous"].sum()),
                "SessionEndTruncatedTrades": int(detail["SessionEndTruncated"].sum()),
                "LowerGrossDollars": detail["LowerGrossDollars"].sum(),
                "UpperGrossDollars": detail["UpperGrossDollars"].sum(),
            }
        ]
    )
    summary = pd.concat([summary, total], ignore_index=True).round(2)
    args.detail_output.parent.mkdir(parents=True, exist_ok=True)
    detail.to_csv(args.detail_output, index=False)
    summary.to_csv(args.summary_output, index=False)
    print(summary.to_string(index=False))


if __name__ == "__main__":
    main()
