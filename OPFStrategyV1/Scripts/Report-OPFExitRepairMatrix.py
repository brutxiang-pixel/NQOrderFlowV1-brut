import argparse
from pathlib import Path

import numpy as np
import pandas as pd


COMBINATIONS = {
    "Baseline": (False, False, False),
    "W": (True, False, False),
    "O": (False, True, False),
    "S": (False, False, True),
    "W_O": (True, True, False),
    "W_S": (True, False, True),
    "O_S": (False, True, True),
    "W_O_S": (True, True, True),
}
POLICIES = {
    "W": ("Long", "ObservationConfirm_WideStop1_5R", "Fixed2_5R"),
    "O": ("Short", "ObservationConfirm", "Protect1RAfter1_5R_Then2_5R"),
}
S_PATHS = {
    "ObservationConfirm",
    "ObservationConfirm_WideStop1_5R",
    "ZoneBirthResearch",
}


def read_csv(path: Path) -> pd.DataFrame:
    return pd.read_csv(path, low_memory=False)


def read_evidence_csvs(root: Path, pattern: str) -> pd.DataFrame:
    files = list(root.rglob(pattern))
    return pd.concat((read_csv(path) for path in files), ignore_index=True)


def stem(combo: str, period: str, mode: str) -> str:
    return f"{combo.lower()}_{period.lower()}_{mode.lower()}"


def trade_key(rows: pd.DataFrame) -> pd.Series:
    entry_time = pd.to_datetime(rows["EntryTime"]).dt.strftime("%Y-%m-%dT%H:%M:%S")
    return (
        rows["SnapshotID"].astype(str)
        + "|"
        + rows["SignalID"].astype(str)
        + "|"
        + rows["ResearchPath"].astype(str)
        + "|"
        + entry_time
    )


def classify_exit(value: str) -> str:
    value = str(value)
    if "TimeStop" in value:
        return "TimeStop"
    if "Protect1R" in value:
        return "Protect1R"
    if "ProtectBE" in value or "BE" in value:
        return "ProtectBE"
    if "Target" in value or "TP" in value:
        return "Target"
    if "Stop" in value or "SL" in value:
        return "Stop"
    return "Other"


def metrics(trades: pd.DataFrame, snapshot_count: int) -> dict:
    trades = trades.copy()
    trades["NetDollars"] = pd.to_numeric(trades["NetDollars"])
    trades["EntryTime"] = pd.to_datetime(trades["EntryTime"])
    wins = trades.loc[trades["NetDollars"].gt(0), "NetDollars"].sum()
    losses = trades.loc[trades["NetDollars"].lt(0), "NetDollars"].sum()
    daily = trades.groupby("SnapshotID", as_index=False).agg(
        Date=("EntryTime", "min"), Net=("NetDollars", "sum")
    ).sort_values("Date")
    equity = daily["Net"].cumsum()
    drawdown = equity.cummax().clip(lower=0) - equity
    return {
        "Trades": len(trades),
        "Net": round(trades["NetDollars"].sum(), 2),
        "DailyTrades": round(len(trades) / snapshot_count, 3),
        "NetPerTrade": round(trades["NetDollars"].mean(), 2),
        "PF": round(wins / abs(losses), 3) if losses else np.inf,
        "WinningDayPct": round(100 * daily["Net"].gt(0).sum() / snapshot_count, 2),
        "MaxDD": round(drawdown.max(), 2),
        "Trade13To15Count": int(trades["DailyTradeCount"].astype(int).ge(13).sum()),
        "Trade13To15Net": round(
            trades.loc[trades["DailyTradeCount"].astype(int).ge(13), "NetDollars"].sum(), 2
        ),
    }


def decomposition(baseline: pd.DataFrame, candidate: pd.DataFrame) -> dict:
    baseline = baseline.copy()
    candidate = candidate.copy()
    baseline["Key"] = trade_key(baseline)
    candidate["Key"] = trade_key(candidate)
    baseline_net = baseline.set_index("Key")["NetDollars"].astype(float)
    candidate_net = candidate.set_index("Key")["NetDollars"].astype(float)
    common = baseline_net.index.intersection(candidate_net.index)
    removed = baseline_net.index.difference(candidate_net.index)
    added = candidate_net.index.difference(baseline_net.index)
    return {
        "CommonTrades": len(common),
        "DirectMatchedDelta": round((candidate_net[common] - baseline_net[common]).sum(), 2),
        "RemovedTrades": len(removed),
        "RemovedDelta": round(-baseline_net[removed].sum(), 2),
        "AddedTrades": len(added),
        "AddedDelta": round(candidate_net[added].sum(), 2),
    }


def policy_lookup(root: Path) -> dict:
    policies = read_evidence_csvs(root, "*_exit_policy_evaluations.csv")
    policies = policies.drop_duplicates(
        ["SnapshotID", "SignalID", "ResearchPath", "EntryBar", "ExitPolicy"]
    )
    return {
        (
            str(row.SnapshotID),
            str(row.SignalID),
            str(row.ResearchPath),
            int(row.EntryBar),
            str(row.ExitPolicy),
        ): row
        for row in policies.itertuples(index=False)
    }


def static_direct_delta(
    baseline_trace: pd.DataFrame,
    lookup: dict,
    flags: tuple[bool, bool, bool],
    conservative: bool,
) -> float:
    use_w, use_o, use_s = flags
    selected = baseline_trace[baseline_trace["Disposition"].eq("Selected")].copy()
    selected["Time"] = pd.to_datetime(selected["Time"])
    delta = 0.0
    for row in selected.itertuples(index=False):
        baseline_net = float(row.NetDollars)
        if (
            use_s
            and row.Side == "Short"
            and row.ResearchPath in S_PATHS
            and 13 <= row.Time.hour <= 19
        ):
            delta -= baseline_net
            continue
        policy = None
        if use_w and (row.Side, row.ResearchPath) == POLICIES["W"][:2]:
            policy = POLICIES["W"][2]
        if use_o and (row.Side, row.ResearchPath) == POLICIES["O"][:2]:
            policy = POLICIES["O"][2]
        if policy is None:
            continue
        outcome = lookup[(str(row.SnapshotID), str(row.SignalID), str(row.ResearchPath), int(row.Bar), policy)]
        gross = float(outcome.PnLDollars)
        if conservative and str(outcome.AmbiguousStopAndTargetSameBar) == "True":
            gross = -4 * float(row.InitialRiskPoints)
        delta += gross - 2.4 - baseline_net
    return round(delta, 2)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--raw", type=Path, required=True)
    parser.add_argument("--h1", type=Path, required=True)
    parser.add_argument("--q4", type=Path, required=True)
    parser.add_argument("--output-prefix", type=Path, required=True)
    args = parser.parse_args()

    evidence = {"H1": args.h1, "Q4": args.q4}
    lookups = {period: policy_lookup(root) for period, root in evidence.items()}
    summaries = []
    monthly = []
    structures = []
    decompositions = []
    timestops = []

    for combo, flags in COMBINATIONS.items():
        for period in ("H1", "Q4"):
            for mode in ("Observed", "Conservative"):
                file_stem = stem(combo, period, mode)
                trades = read_csv(args.raw / f"{file_stem}_trades.csv")
                trace = read_csv(args.raw / f"{file_stem}_trace.csv")
                summary = read_csv(args.raw / f"{file_stem}_summary.csv").iloc[0]
                row = {
                    "Combination": combo,
                    "W": flags[0],
                    "O": flags[1],
                    "S": flags[2],
                    "Period": period,
                    "Mode": mode,
                    **metrics(trades, int(summary.SnapshotCount)),
                    "MissingOutcomes": int(summary.MissingOutcomes),
                    "MissingOverrideOutcomes": int(summary.MissingOverrideOutcomes),
                }
                row["StaticDirectDelta"] = static_direct_delta(
                    read_csv(args.raw / f"{stem('Baseline', period, mode)}_trace.csv"),
                    lookups[period],
                    flags,
                    mode == "Conservative",
                )
                baseline_summary = read_csv(
                    args.raw / f"{stem('Baseline', period, mode)}_summary.csv"
                ).iloc[0]
                row["CombinationDelta"] = round(
                    row["Net"] - float(baseline_summary.SimulatedNetDollars), 2
                )
                row["RetentionPct"] = (
                    round(100 * row["CombinationDelta"] / row["StaticDirectDelta"], 2)
                    if row["StaticDirectDelta"] > 0
                    else np.nan
                )
                summaries.append(row)

                trades["EntryTime"] = pd.to_datetime(trades["EntryTime"])
                for month, group in trades.groupby(trades["EntryTime"].dt.to_period("M")):
                    monthly.append(
                        {
                            "Combination": combo,
                            "Period": period,
                            "Mode": mode,
                            "Month": str(month),
                            "Trades": len(group),
                            "Net": round(pd.to_numeric(group["NetDollars"]).sum(), 2),
                        }
                    )
                trades["ExitGroup"] = trades["ExitReason"].map(classify_exit)
                for exit_group, group in trades.groupby("ExitGroup"):
                    structures.append(
                        {
                            "Combination": combo,
                            "Period": period,
                            "Mode": mode,
                            "ExitGroup": exit_group,
                            "Trades": len(group),
                            "Net": round(pd.to_numeric(group["NetDollars"]).sum(), 2),
                        }
                    )
                applied = trades[trades["AppliedExitPolicy"].fillna("").ne("")]
                for policy, group in applied.groupby("AppliedExitPolicy"):
                    count = len(group)
                    time_stop_count = group["ExitReason"].eq("TimeStop").sum()
                    timestops.append(
                        {
                            "Combination": combo,
                            "Period": period,
                            "Mode": mode,
                            "Policy": policy,
                            "AppliedTrades": count,
                            "TimeStopTrades": int(time_stop_count),
                            "TimeStopPct": round(100 * time_stop_count / count, 2),
                        }
                    )
                baseline_trades = read_csv(
                    args.raw / f"{stem('Baseline', period, mode)}_trades.csv"
                )
                decompositions.append(
                    {
                        "Combination": combo,
                        "Period": period,
                        "Mode": mode,
                        **decomposition(baseline_trades, trades),
                    }
                )

    prefix = args.output_prefix
    prefix.parent.mkdir(parents=True, exist_ok=True)
    summary_frame = pd.DataFrame(summaries)
    summary_frame.to_csv(prefix.with_name(prefix.name + "_summary.csv"), index=False)
    pd.DataFrame(monthly).to_csv(prefix.with_name(prefix.name + "_monthly.csv"), index=False)
    pd.DataFrame(structures).to_csv(prefix.with_name(prefix.name + "_exit_structure.csv"), index=False)
    pd.DataFrame(decompositions).to_csv(prefix.with_name(prefix.name + "_decomposition.csv"), index=False)
    pd.DataFrame(timestops).to_csv(prefix.with_name(prefix.name + "_timestop.csv"), index=False)

    combined_rows = []
    for combo in COMBINATIONS:
        for mode in ("Observed", "Conservative"):
            parts = [
                read_csv(args.raw / f"{stem(combo, period, mode)}_trades.csv")
                for period in ("Q4", "H1")
            ]
            combined_trades = pd.concat(parts, ignore_index=True)
            row = {
                "Combination": combo,
                "Mode": mode,
                **metrics(combined_trades, 184),
            }
            period_rows = summary_frame[
                summary_frame["Combination"].eq(combo) & summary_frame["Mode"].eq(mode)
            ]
            row["StaticDirectDelta"] = round(period_rows["StaticDirectDelta"].sum(), 2)
            row["CombinationDelta"] = round(period_rows["CombinationDelta"].sum(), 2)
            combined_rows.append(row)
    combined = pd.DataFrame(combined_rows)
    combined["RetentionPct"] = np.where(
        combined["StaticDirectDelta"].gt(0),
        100 * combined["CombinationDelta"] / combined["StaticDirectDelta"],
        np.nan,
    ).round(2)
    combined.to_csv(prefix.with_name(prefix.name + "_combined.csv"), index=False)
    print(combined.sort_values(["Mode", "Net"], ascending=[True, False]).to_string(index=False))


if __name__ == "__main__":
    main()
