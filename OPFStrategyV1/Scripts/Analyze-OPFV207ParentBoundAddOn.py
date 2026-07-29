import argparse
import importlib.util
from pathlib import Path

import numpy as np
import pandas as pd


EXCLUDED_PATHS = {
    ("Long", "ObservationStrict_Other"),
    ("Short", "FailureReverse_RetestFailed_WideStop1_5R"),
}


def load_module():
    path = Path(__file__).with_name("Analyze-OPFV207ProfitRiskPortfolio.py")
    spec = importlib.util.spec_from_file_location("opf_v207_portfolio", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def attach_parent(core: pd.DataFrame, secondary: pd.DataFrame) -> pd.DataFrame:
    candidates = secondary[secondary["ReasonHead"].eq("ActiveTrade")].copy()
    candidates = candidates[
        ~candidates["ResearchPath"].eq("ZoneBirthResearch")
        & ~pd.Series(
            list(zip(candidates["Side"], candidates["ResearchPath"])),
            index=candidates.index,
        ).isin(EXCLUDED_PATHS)
    ]

    rows = []
    for snapshot_id, day_candidates in candidates.groupby("SnapshotID", sort=False):
        parents = core[
            core["SnapshotID"].eq(snapshot_id) & core["Classification"].eq("Normal")
        ]
        for child in day_candidates.itertuples(index=False):
            active = parents[
                parents["EntryTime"].le(child.EntryTime)
                & parents["ExitTime"].gt(child.EntryTime)
                & parents["Side"].eq(child.Side)
            ]
            if active.empty:
                continue
            parent = active.sort_values("EntryTime").iloc[-1]
            row = child._asdict()
            row.update(
                {
                    "ParentTradeID": parent["OriginalTradeID"],
                    "ParentPath": parent["ResearchPath"],
                    "ParentEntryTime": parent["EntryTime"],
                    "ParentExitTime": parent["ExitTime"],
                    "ParentEntryPrice": float(parent["ActualEntryPrice"]),
                    "ParentExitPrice": float(parent["ActualExitPrice"]),
                    "ParentRiskPoints": float(parent["InitialRiskPoints"]),
                    "ParentRiskDollars": float(parent["InitialRiskPoints"]) * 4,
                }
            )
            direction = 1 if child.Side == "Long" else -1
            row["ParentProgressR"] = (
                direction * (float(child.Entry) - row["ParentEntryPrice"])
                / row["ParentRiskPoints"]
            )
            rows.append(row)
    return pd.DataFrame(rows)


def child_net(row, mode: str, parent_bound: bool) -> float:
    if not parent_bound:
        net2 = float(row.ObservedNet2 if mode == "Observed" else row.ConservativeNet2)
        return (net2 + 2.4) / 2 - 1.2

    direction = 1 if row.Side == "Long" else -1
    gross = direction * (float(row.ParentExitPrice) - float(row.Entry)) * 2
    net = gross - 1.2
    if mode == "Conservative":
        net -= max(float(row.BiasApplied2), 0) / 2
    return net


def simulate(
    module,
    core: pd.DataFrame,
    candidates: pd.DataFrame,
    day_order: pd.DataFrame,
    mode: str,
    progress_floor: float,
    risk_cap: float,
    daily_limit: int,
    add_on_loss_limit: float,
):
    accepted = []
    diag = {
        "AddOnAccepted": 0,
        "AddOnNet": 0.0,
        "NaturalExitCount": 0,
        "ParentFlattenCount": 0,
        "ProgressBlocked": 0,
        "RiskBlocked": 0,
        "LossBlocked": 0,
        "LimitBlocked": 0,
        "BusyBlocked": 0,
        "PeakRisk": 0.0,
    }

    for snapshot_id, core_day in core.groupby("SnapshotID", sort=False):
        core_day = core_day.sort_values("EntryTime")
        core_records = []
        for row in core_day.itertuples(index=False):
            record = {
                "SnapshotID": snapshot_id,
                "EntryTime": row.EntryTime,
                "ExitTime": row.ExitTime,
                "Side": row.Side,
                "ResearchPath": row.ResearchPath,
                "Period": row.Period,
                "Month": row.Month,
                "Lane": "Core",
                "Net": float(row.ActualNet2),
                "Risk": float(row.InitialRiskPoints) * 4,
            }
            core_records.append(record)
            accepted.append(record)

        day_candidates = candidates[candidates["SnapshotID"].eq(snapshot_id)].sort_values(
            ["EntryTime", "SignalID"]
        )
        active_add_on = None
        add_on_realized = 0.0
        add_on_count = 0
        for row in day_candidates.itertuples(index=False):
            if active_add_on is not None and active_add_on["ExitTime"] <= row.EntryTime:
                add_on_realized += active_add_on["Net"]
                active_add_on = None
            if active_add_on is not None:
                diag["BusyBlocked"] += 1
                continue
            if add_on_count >= daily_limit:
                diag["LimitBlocked"] += 1
                continue
            if float(row.ParentProgressR) < progress_floor:
                diag["ProgressBlocked"] += 1
                continue

            core_realized = sum(r["Net"] for r in core_records if r["ExitTime"] <= row.EntryTime)
            if add_on_realized <= -add_on_loss_limit or core_realized + add_on_realized <= -300:
                diag["LossBlocked"] += 1
                continue

            add_on_risk = float(row.InitialRiskPoints) * 2
            combined_risk = float(row.ParentRiskDollars) + add_on_risk
            if combined_risk > risk_cap:
                diag["RiskBlocked"] += 1
                continue

            parent_bound = row.ParentExitTime < row.ExitTime
            exit_time = row.ParentExitTime if parent_bound else row.ExitTime
            net = child_net(row, mode, parent_bound)
            record = {
                "SnapshotID": snapshot_id,
                "EntryTime": row.EntryTime,
                "ExitTime": exit_time,
                "Side": row.Side,
                "ResearchPath": row.ResearchPath,
                "ParentPath": row.ParentPath,
                "ParentProgressR": float(row.ParentProgressR),
                "Period": row.Period,
                "Month": row.Month,
                "Lane": "AddOn",
                "Net": net,
                "Risk": add_on_risk,
            }
            accepted.append(record)
            active_add_on = record
            add_on_count += 1
            diag["AddOnAccepted"] += 1
            diag["PeakRisk"] = max(diag["PeakRisk"], combined_risk)
            if parent_bound:
                diag["ParentFlattenCount"] += 1
            else:
                diag["NaturalExitCount"] += 1

        if active_add_on is not None:
            add_on_realized += active_add_on["Net"]
        diag["AddOnNet"] += add_on_realized

    trades = pd.DataFrame(accepted)
    add_ons = trades[trades["Lane"].eq("AddOn")]
    diag["AddOnNet"] = round(float(diag["AddOnNet"]), 2)
    diag["AddOnExpectancy"] = (
        round(float(add_ons["Net"].mean()), 2) if not add_ons.empty else 0.0
    )
    diag["MeanParentProgressR"] = (
        round(float(add_ons["ParentProgressR"].mean()), 3) if not add_ons.empty else 0.0
    )
    return trades, {**module.metrics(trades, day_order), **diag}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    args = parser.parse_args()

    module = load_module()
    _, core, secondary, _, _ = module.load_candidates(args.evidence)
    candidates = attach_parent(core, secondary)
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
    baseline = module.metrics(baseline_trades, day_order)

    rows = []
    traces = {}
    for progress_floor in (-99, 0, 0.25, 0.5, 0.75, 1.0):
        for risk_cap in (150, 175, 200):
            for daily_limit in (1, 2, 3):
                for add_on_loss_limit in (100, 150):
                    config = (
                        f"progress{progress_floor:g}|risk{risk_cap}|"
                        f"limit{daily_limit}|loss{add_on_loss_limit}"
                    )
                    for mode in ("Observed", "Conservative"):
                        trades, result = simulate(
                            module,
                            core,
                            candidates,
                            day_order,
                            mode,
                            progress_floor,
                            risk_cap,
                            daily_limit,
                            add_on_loss_limit,
                        )
                        rows.append({"Config": config, "Mode": mode, **result})
                        traces[(config, mode)] = trades

    summary = pd.DataFrame(rows)
    conservative = summary[summary["Mode"].eq("Conservative")].copy()
    conservative["NetDelta"] = conservative["Net"] - baseline["Net"]
    conservative["PFDelta"] = conservative["PF"] - baseline["PF"]
    conservative["MaxDDDelta"] = conservative["MaxDD"] - baseline["MaxDD"]
    conservative["Q4Delta"] = conservative["Q4Net"] - baseline["Q4Net"]
    conservative["H1Delta"] = conservative["H1Net"] - baseline["H1Net"]
    conservative["StrictPareto"] = (
        conservative["NetDelta"].gt(0)
        & conservative["PFDelta"].gt(0)
        & conservative["MaxDDDelta"].le(0)
        & conservative["Q4Delta"].ge(0)
        & conservative["H1Delta"].ge(0)
    )
    summary = summary.merge(
        conservative[
            [
                "Config",
                "NetDelta",
                "PFDelta",
                "MaxDDDelta",
                "Q4Delta",
                "H1Delta",
                "StrictPareto",
            ]
        ],
        on="Config",
        how="left",
    )

    args.output_dir.mkdir(parents=True, exist_ok=True)
    summary.to_csv(args.output_dir / "v207_parent_bound_addon_summary.csv", index=False)
    top = conservative.sort_values(
        ["StrictPareto", "Net", "PF"], ascending=[False, False, False]
    ).head(20)
    monthly = []
    paths = []
    for config in top["Config"]:
        for mode in ("Observed", "Conservative"):
            trades = traces[(config, mode)]
            add_ons = trades[trades["Lane"].eq("AddOn")]
            month = add_ons.groupby("Month")["Net"].agg(["count", "sum", "mean"]).reset_index()
            month.insert(0, "Mode", mode)
            month.insert(0, "Config", config)
            monthly.append(month)
            path = (
                add_ons.groupby(["Side", "ParentPath", "ResearchPath"])["Net"]
                .agg(["count", "sum", "mean"])
                .reset_index()
            )
            path.insert(0, "Mode", mode)
            path.insert(0, "Config", config)
            paths.append(path)
    pd.concat(monthly, ignore_index=True).to_csv(
        args.output_dir / "v207_parent_bound_addon_monthly.csv", index=False
    )
    pd.concat(paths, ignore_index=True).to_csv(
        args.output_dir / "v207_parent_bound_addon_paths.csv", index=False
    )

    print("Baseline")
    print(pd.Series(baseline).to_string())
    print(f"\nSame-direction deployable candidates: {len(candidates)}")
    print("\nTop conservative configs")
    print(
        top[
            [
                "Config",
                "Net",
                "PF",
                "MaxDD",
                "Q4Net",
                "H1Net",
                "AddOnAccepted",
                "AddOnNet",
                "AddOnExpectancy",
                "ParentFlattenCount",
                "PeakRisk",
                "StrictPareto",
            ]
        ].to_string(index=False)
    )


if __name__ == "__main__":
    main()
