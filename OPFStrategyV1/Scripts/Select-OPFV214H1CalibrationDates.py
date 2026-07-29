import argparse
from pathlib import Path

import pandas as pd


EXCLUDED_EXACT_DATES = {
    "2026-01-06", "2026-01-28", "2026-02-24", "2026-03-19",
    "2026-04-08", "2026-05-20", "2026-06-17",
}


def select_month(group):
    group = group.sort_values("TradingDate").copy()
    group["NetQuartile"] = pd.qcut(
        group["DailyNet"].rank(method="first"), 4, labels=False
    )
    selected = []
    used_weeks = set()
    for quartile in range(4):
        candidates = group[group["NetQuartile"].eq(quartile)].sort_values(
            ["SecondaryTrades", "PeakRisk", "TradingDate"],
            ascending=[False, False, True],
        )
        fresh_week = candidates[~candidates["IsoWeek"].isin(used_weeks)]
        row = (fresh_week if not fresh_week.empty else candidates).iloc[0]
        selected.append(row)
        used_weeks.add(row["IsoWeek"])
    return pd.DataFrame(selected)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--trades", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()

    trades = pd.read_csv(args.trades, low_memory=False)
    trades["TradingDate"] = pd.to_datetime(trades["TradingDate"])
    selected_config = trades[
        trades["Arm"].eq("D_3plus3")
        & trades["SecondaryPolicy"].eq("PathRiskA50G0_0")
        & trades["RiskCap"].eq(300.0)
        & trades["DelayMinutes"].eq(5.0)
        & trades["CountsNormal"].astype(bool)
    ].copy()
    daily = selected_config.groupby("TradingDate").agg(
        Trades=("Net3", "size"),
        DailyGross=("Gross3", "sum"),
        DailyNet=("Net3", "sum"),
        SecondaryTrades=("Lane", lambda values: int(values.eq("Secondary").sum())),
        SecondaryGross=("Gross3", lambda values: float(values[selected_config.loc[values.index, "Lane"].eq("Secondary")].sum())),
        PeakRisk=("RiskDollars", "max"),
    ).reset_index()
    daily["Month"] = daily["TradingDate"].dt.to_period("M").astype(str)
    iso = daily["TradingDate"].dt.isocalendar()
    daily["IsoWeek"] = iso.year.astype(str) + "-W" + iso.week.astype(str).str.zfill(2)
    daily = daily[~daily["TradingDate"].dt.strftime("%Y-%m-%d").isin(EXCLUDED_EXACT_DATES)]

    selected = pd.concat(
        [select_month(group) for _, group in daily.groupby("Month", sort=True)],
        ignore_index=True,
    ).sort_values("TradingDate")
    selected["CalibrationRole"] = selected["NetQuartile"].map({
        0: "LowNet", 1: "MidLowNet", 2: "MidHighNet", 3: "HighNet",
    })
    if len(selected) != 24 or selected["TradingDate"].nunique() != 24:
        raise RuntimeError("Expected 24 unique calibration dates")
    if selected.groupby("Month")["IsoWeek"].nunique().min() < 4:
        raise RuntimeError("Each month must cover four distinct ISO weeks")
    args.output.parent.mkdir(parents=True, exist_ok=True)
    selected.to_csv(args.output, index=False)
    print(selected.to_string(index=False))
    print()
    print(",".join(selected["TradingDate"].dt.strftime("%Y-%m-%d")))


if __name__ == "__main__":
    main()
