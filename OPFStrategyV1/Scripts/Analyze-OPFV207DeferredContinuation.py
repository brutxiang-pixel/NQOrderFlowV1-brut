import argparse
import importlib.util
from pathlib import Path

import pandas as pd


def load_module(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def load_rich_bars(root: Path) -> pd.DataFrame:
    selected = pd.read_csv(root / "selected_snapshots.csv", dtype={"Date": str})
    parts = []
    for row in selected.itertuples(index=False):
        path = root / f"{row.SnapshotId}_rich_bar_features.csv"
        part = pd.read_csv(
            path,
            usecols=["Time", "Bar", "Open", "High", "Low", "Close"],
            low_memory=False,
        )
        part["TradingDate"] = row.Date
        parts.append(part)
    bars = pd.concat(parts, ignore_index=True)
    bars["Time"] = pd.to_datetime(bars["Time"])
    bars["Bar"] = bars["Bar"].astype(int)
    bars.sort_values(["TradingDate", "Time", "Bar"], inplace=True)
    return bars


def attach_parent(core: pd.DataFrame, rows: pd.DataFrame) -> pd.DataFrame:
    candidates = rows[
        ~rows["IsCore"]
        & rows["OriginalDecision"].eq("Skip")
        & rows["ReasonHead"].eq("ActiveTrade")
    ].copy()
    output = []
    for snapshot_id, day_candidates in candidates.groupby("SnapshotID", sort=False):
        parents = core[
            core["SnapshotID"].eq(snapshot_id) & core["Classification"].eq("Normal")
        ]
        for child in day_candidates.itertuples(index=False):
            active = parents[
                parents["EntryTime"].le(child.EntryTime)
                & parents["ExitTime"].gt(child.EntryTime)
            ]
            if active.empty:
                continue
            parent = active.sort_values("EntryTime").iloc[-1]
            if parent["ExitTime"] >= child.ExitTime:
                continue
            row = child._asdict()
            row.update(
                {
                    "ParentTradeID": parent["OriginalTradeID"],
                    "ParentExitTime": parent["ActualExitTime"],
                }
            )
            row["DeferredReadyTime"] = max(child.EntryTime, parent["ActualExitTime"])
            if row["DeferredReadyTime"] >= child.ExitTime:
                continue
            output.append(row)
    return pd.DataFrame(output)


def add_lomo_path_eligibility(candidates: pd.DataFrame) -> pd.DataFrame:
    candidates = candidates.copy()
    candidates["LomoEligible"] = False
    group_columns = ["Side", "ResearchPath"]
    for month in sorted(candidates["Month"].unique()):
        train = candidates[candidates["Month"].ne(month)]
        period = (
            train.groupby(group_columns + ["Period"])["ConservativeNet2"]
            .agg(["count", "sum"])
            .reset_index()
        )
        eligible = set()
        for key, group in period.groupby(group_columns):
            by_period = group.set_index("Period")
            if (
                {"Q4", "H1"}.issubset(by_period.index)
                and int(by_period["count"].sum()) >= 10
                and int(by_period.loc["Q4", "count"]) >= 3
                and int(by_period.loc["H1", "count"]) >= 3
                and float(by_period.loc["Q4", "sum"]) > 0
                and float(by_period.loc["H1", "sum"]) > 0
            ):
                eligible.add(key if isinstance(key, tuple) else (key,))
        mask = candidates["Month"].eq(month)
        candidates.loc[mask, "LomoEligible"] = [
            (side, path) in eligible
            for side, path in zip(
                candidates.loc[mask, "Side"], candidates.loc[mask, "ResearchPath"]
            )
        ]
    return candidates


def build_outcomes(
    candidates: pd.DataFrame,
    bars: pd.DataFrame,
    target_r: float,
    max_bars: int,
    collision_mode: str,
) -> pd.DataFrame:
    bars_by_date = {
        date: part.reset_index(drop=True) for date, part in bars.groupby("TradingDate", sort=False)
    }
    rows = []
    for candidate in candidates.itertuples(index=False):
        day = bars_by_date.get(str(candidate.TradingDate.date()))
        if day is None:
            continue
        later = day[day["Time"].gt(candidate.DeferredReadyTime)]
        if later.empty:
            continue
        entry_index = int(later.index[0])
        entry_price = float(day.loc[entry_index, "Open"])
        risk = float(candidate.InitialRiskPoints)
        direction = 1 if candidate.Side == "Long" else -1
        stop = entry_price - direction * risk
        target = entry_price + direction * risk * target_r
        be_armed = False
        exit_price = None
        exit_reason = None
        exit_time = None
        scan = day.iloc[entry_index : entry_index + max_bars]
        for bar in scan.itertuples(index=False):
            stop_hit = bar.Low <= stop if direction == 1 else bar.High >= stop
            target_hit = bar.High >= target if direction == 1 else bar.Low <= target
            if stop_hit and target_hit:
                if collision_mode == "TargetFirst":
                    exit_price, exit_reason = target, "TargetCollision"
                else:
                    exit_price, exit_reason = stop, "StopCollision"
                exit_time = bar.Time
                break
            if stop_hit:
                exit_price, exit_reason, exit_time = stop, "Stop", bar.Time
                break
            if target_hit:
                exit_price, exit_reason, exit_time = target, "Target", bar.Time
                break
            if not be_armed:
                reached_one_r = (
                    bar.High >= entry_price + risk
                    if direction == 1
                    else bar.Low <= entry_price - risk
                )
                if reached_one_r:
                    be_armed = True
                    stop = entry_price
        if exit_price is None:
            last = scan.iloc[-1]
            exit_price = float(last["Close"])
            exit_reason = "TimeStop"
            exit_time = last["Time"]
        gross = direction * (exit_price - entry_price) * 4
        rows.append(
            {
                **candidate._asdict(),
                "DeferredEntryTime": day.loc[entry_index, "Time"],
                "DeferredExitTime": exit_time,
                "DeferredEntryPrice": entry_price,
                "DeferredExitPrice": exit_price,
                "DeferredExitReason": exit_reason,
                "DeferredNet": gross - 2.4,
            }
        )
    return pd.DataFrame(rows)


def simulate_portfolio(module, core: pd.DataFrame, opportunities: pd.DataFrame, day_order: pd.DataFrame):
    trades = []
    diagnostics = {
        "DeferredAccepted": 0,
        "DeferredNet": 0.0,
        "CoreAccepted": 0,
        "CoreBlocked": 0,
        "LimitBlocked": 0,
        "LossBlocked": 0,
        "StaleParentBlocked": 0,
    }
    for snapshot_id, core_day in core.groupby("SnapshotID", sort=False):
        core_day = core_day.sort_values(["EntryTime", "OriginalTradeID"])
        opp_day = opportunities[opportunities["SnapshotID"].eq(snapshot_id)].copy()
        if not opp_day.empty:
            opp_day.sort_values(
                ["DeferredEntryTime", "EntryTime"], ascending=[True, False], inplace=True
            )
            opp_day = opp_day.drop_duplicates("DeferredEntryTime", keep="first")

        events = []
        for row in core_day.itertuples(index=False):
            events.append((row.ActualEntryTime, 0, "Core", row))
        for row in opp_day.itertuples(index=False):
            events.append((row.DeferredEntryTime, 1, "Deferred", row))
        events.sort(key=lambda item: (item[0], item[1]))

        active = None
        realized = 0.0
        accepted_count = 0
        accepted_core_ids = set()
        for time, _, lane, row in events:
            if active is not None and active["ExitTime"] <= time:
                realized += active["Net"]
                active = None
            if active is not None:
                if lane == "Core":
                    diagnostics["CoreBlocked"] += 1
                continue
            if accepted_count >= 15:
                diagnostics["LimitBlocked"] += 1
                continue
            if realized <= -300:
                diagnostics["LossBlocked"] += 1
                continue

            if lane == "Deferred" and row.ParentTradeID not in accepted_core_ids:
                diagnostics["StaleParentBlocked"] += 1
                continue
            if lane == "Core":
                record = {
                    "SnapshotID": snapshot_id,
                    "EntryTime": row.ActualEntryTime,
                    "ExitTime": row.ActualExitTime,
                    "Side": row.Side,
                    "ResearchPath": row.ResearchPath,
                    "Period": row.Period,
                    "Month": row.Month,
                    "Lane": "Core",
                    "Net": float(row.ActualNet2),
                    "Risk": float(row.InitialRiskPoints) * 4,
                }
                accepted_core_ids.add(row.OriginalTradeID)
                diagnostics["CoreAccepted"] += 1
            else:
                record = {
                    "SnapshotID": snapshot_id,
                    "EntryTime": row.DeferredEntryTime,
                    "ExitTime": row.DeferredExitTime,
                    "Side": row.Side,
                    "ResearchPath": row.ResearchPath,
                    "Period": row.Period,
                    "Month": row.Month,
                    "Lane": "Deferred",
                    "Net": float(row.DeferredNet),
                    "Risk": float(row.InitialRiskPoints) * 4,
                }
                diagnostics["DeferredAccepted"] += 1
                diagnostics["DeferredNet"] += record["Net"]
            active = record
            accepted_count += 1
            trades.append(record)
        if active is not None:
            realized += active["Net"]

    diagnostics["DeferredNet"] = round(diagnostics["DeferredNet"], 2)
    frame = pd.DataFrame(trades)
    return frame, {**module.metrics(frame, day_order), **diagnostics}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--rich", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    args = parser.parse_args()

    module = load_module(
        "opf_v207_portfolio", Path(__file__).with_name("Analyze-OPFV207ProfitRiskPortfolio.py")
    )
    rows, core, _, _, _ = module.load_candidates(args.evidence)
    candidates = add_lomo_path_eligibility(attach_parent(core, rows))
    bars = load_rich_bars(args.rich)
    day_order = (
        core.groupby("SnapshotID")["EntryTime"].min().sort_values().reset_index()[["SnapshotID"]]
    )
    baseline = module.metrics(
        pd.DataFrame(
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
        ),
        day_order,
    )

    results = []
    traces = {}
    for target_r in (1.5, 2.0, 3.0):
        for max_bars in (6, 12):
            for collision_mode in ("StopFirst", "TargetFirst"):
                outcomes = build_outcomes(
                    candidates, bars, target_r, max_bars, collision_mode
                )
                for selection in ("All", "LomoRobust"):
                    selected = (
                        outcomes
                        if selection == "All"
                        else outcomes[outcomes["LomoEligible"]]
                    )
                    config = (
                        f"target{target_r:g}|bars{max_bars}|"
                        f"{collision_mode}|{selection}"
                    )
                    trades, result = simulate_portfolio(module, core, selected, day_order)
                    results.append({"Config": config, **result})
                    traces[config] = trades

    summary = pd.DataFrame(results)
    summary["NetDelta"] = summary["Net"] - baseline["Net"]
    summary["PFDelta"] = summary["PF"] - baseline["PF"]
    summary["MaxDDDelta"] = summary["MaxDD"] - baseline["MaxDD"]
    summary["Q4Delta"] = summary["Q4Net"] - baseline["Q4Net"]
    summary["H1Delta"] = summary["H1Net"] - baseline["H1Net"]
    summary["StrictPareto"] = (
        summary["NetDelta"].gt(0)
        & summary["PFDelta"].gt(0)
        & summary["MaxDDDelta"].le(0)
        & summary["Q4Delta"].ge(0)
        & summary["H1Delta"].ge(0)
    )
    args.output_dir.mkdir(parents=True, exist_ok=True)
    summary.to_csv(args.output_dir / "v207_deferred_continuation_summary.csv", index=False)

    top = summary.sort_values(
        ["StrictPareto", "Net", "PF"], ascending=[False, False, False]
    ).head(12)
    monthly = []
    for config in top["Config"]:
        trade = traces[config]
        part = trade.groupby(["Month", "Lane"])["Net"].agg(["count", "sum"]).reset_index()
        part.insert(0, "Config", config)
        monthly.append(part)
    pd.concat(monthly, ignore_index=True).to_csv(
        args.output_dir / "v207_deferred_continuation_monthly.csv", index=False
    )

    print("Baseline")
    print(pd.Series(baseline).to_string())
    print(f"\nDeferred opportunities: {len(candidates)}")
    print("\nTop configs")
    print(
        top[
            [
                "Config",
                "Net",
                "PF",
                "MaxDD",
                "Q4Net",
                "H1Net",
                "DeferredAccepted",
                "DeferredNet",
                "CoreBlocked",
                "NetDelta",
                "StrictPareto",
            ]
        ].to_string(index=False)
    )


if __name__ == "__main__":
    main()
