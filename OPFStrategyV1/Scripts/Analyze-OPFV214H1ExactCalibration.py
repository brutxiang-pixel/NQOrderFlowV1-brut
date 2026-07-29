import argparse
import importlib.util
from pathlib import Path

import numpy as np
import pandas as pd


KEY = ["SnapshotID", "SignalID", "ResearchPath"]
FROZEN_DYNAMIC_NAME = "PathRisk_N60_Z1.645_M4"
SECONDARY_SCORE_FLOORS = (0, 1, 2, 4, 6)
MIN_LOWER95_GROSS_RATIO_PCT = 81.60


def load_module(path: Path):
    spec = importlib.util.spec_from_file_location("opf_v214_exact_connector", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def risk_band(value: float) -> str:
    if value <= 10:
        return "R10"
    if value <= 20:
        return "R20"
    if value <= 30:
        return "R30"
    return "RHigh"


def frozen_exit_choices(root: Path):
    selections = pd.read_csv(
        root / "Reports/v214_h1_dynamic_exit_lomo/dynamic_exit_selection_audit.csv"
    )
    selections = selections[selections["Name"].eq(FROZEN_DYNAMIC_NAME)]
    return {
        (row.TestMonth, row.Side, row.ResearchPath, row.RiskBand): row.ChosenOption
        for row in selections.itertuples(index=False)
    }


def static_exit_choices(root: Path):
    outcomes = pd.read_csv(
        root / "Reports/v214_h1_dynamic_exit_lomo/dynamic_exit_outcomes.csv"
    )
    group_columns = ["Side", "ResearchPath", "RiskBand"]
    stats = outcomes.groupby([*group_columns, "Option"])["ConservativeNet3"].agg(
        ["count", "mean", "std"]
    ).reset_index()
    stats["LowerMean"] = (
        stats["mean"]
        - 1.645 * stats["std"].fillna(0) / np.sqrt(stats["count"])
    )
    choices = {}
    for group_key, part in stats.groupby(group_columns, sort=False):
        current = part[part["Option"].eq("Current")]
        chosen = "Current"
        if not current.empty and int(current.iloc[0]["count"]) >= 60:
            eligible = part[part["count"].ge(60)].sort_values(
                ["LowerMean", "Option"], ascending=[False, True]
            )
            best = eligible.iloc[0]
            gain = float(best["LowerMean"] - current.iloc[0]["LowerMean"])
            if gain >= 4.0:
                chosen = best["Option"]
        choices[group_key] = chosen
    return choices


def frozen_secondary_scores(root: Path):
    outcomes = pd.read_csv(
        root / "Reports/v214_h1_dynamic_exit_lomo/dynamic_exit_outcomes.csv"
    )
    choices = frozen_exit_choices(root)
    outcomes["ChosenOption"] = [
        choices.get((month, side, path, band), "Current")
        for month, side, path, band in zip(
            outcomes["Month"], outcomes["Side"], outcomes["ResearchPath"], outcomes["RiskBand"]
        )
    ]
    selected = outcomes[outcomes["Option"].eq(outcomes["ChosenOption"])].copy()
    selected["ConservativeNet1"] = selected["ConservativeNet3"] / 3.0
    scores = {}
    for month in sorted(selected["Month"].unique()):
        train = selected[selected["Month"].ne(month)]
        global_mean = float(train["ConservativeNet1"].mean())
        grouped = train.groupby(["Side", "ResearchPath", "RiskBand"])[
            "ConservativeNet1"
        ].agg(["sum", "count"])
        grouped["Score"] = (grouped["sum"] + 50 * global_mean) / (
            grouped["count"] + 50
        )
        for key, value in grouped["Score"].items():
            scores[(month, *key)] = float(value)
        scores[(month, "__GLOBAL__")] = global_mean
    return scores


def static_secondary_scores(root: Path):
    outcomes = pd.read_csv(
        root / "Reports/v214_h1_dynamic_exit_lomo/dynamic_exit_outcomes.csv"
    )
    choices = frozen_exit_choices(root)
    outcomes["ChosenOption"] = [
        choices.get((month, side, path, band), "Current")
        for month, side, path, band in zip(
            outcomes["Month"], outcomes["Side"], outcomes["ResearchPath"], outcomes["RiskBand"]
        )
    ]
    selected = outcomes[outcomes["Option"].eq(outcomes["ChosenOption"])].copy()
    selected["ConservativeNet1"] = selected["ConservativeNet3"] / 3.0
    global_mean = float(selected["ConservativeNet1"].mean())
    grouped = selected.groupby(["Side", "ResearchPath", "RiskBand"])[
        "ConservativeNet1"
    ].agg(["sum", "count"])
    grouped["Score"] = (grouped["sum"] + 50 * global_mean) / (
        grouped["count"] + 50
    )
    return grouped["Score"].to_dict(), global_mean


def secondary_score(
    frozen_scores: dict,
    static_scores: dict,
    static_global: float,
    month: str,
    side: str,
    path: str,
    band: str,
) -> float:
    month_global = frozen_scores.get((month, "__GLOBAL__"))
    if month_global is None:
        return float(static_scores.get((side, path, band), static_global))
    return float(frozen_scores.get((month, side, path, band), month_global))


def exact_candidate_tape(
    connector, evidence: Path, root: Path, frozen_dates, static_exit: bool = False
):
    calibration, bars, turns, decisions, trades, _, _ = connector.load_evidence(
        evidence, require_turns=True
    )
    joined = calibration.merge(
        decisions[
            [
                "SnapshotID",
                "SignalID",
                "ResearchPath",
                "Time",
                "Decision",
                "ReasonHead",
                "SourceSequence",
            ]
        ],
        left_on=[*KEY, "EntryTime"],
        right_on=[*KEY, "Time"],
        validate="one_to_one",
    )
    candidates = joined[
        joined["Decision"].eq("Execute")
        | joined["ReasonHead"].isin(connector.DYNAMIC_REASON_HEADS)
    ].copy()

    fill_risk = {
        (row.SnapshotID, row.SignalID, row.ResearchPath): float(row.InitialRiskPoints)
        for row in trades.itertuples(index=False)
    }
    candidates["ExactRisk"] = [
        fill_risk.get(
            (row.SnapshotID, row.SignalID, row.ResearchPath),
            abs(connector.execution_entry(row) - float(row.Stop)),
        )
        for row in candidates.itertuples(index=False)
    ]
    snapshots = sorted(candidates["SnapshotID"].unique())
    if len(snapshots) != len(frozen_dates):
        raise RuntimeError(
            f"Snapshot/date count mismatch: {len(snapshots)} != {len(frozen_dates)}"
        )
    snapshot_dates = dict(zip(snapshots, sorted(frozen_dates)))
    candidates["TradingDate"] = candidates["SnapshotID"].map(snapshot_dates)
    candidates["Month"] = candidates["TradingDate"].dt.strftime("%Y-%m")
    candidates["RiskBand"] = candidates["ExactRisk"].map(risk_band)
    candidates["SecondaryRiskBand"] = candidates["InitialRiskPoints"].map(risk_band)

    if static_exit:
        choices = static_exit_choices(root)
        candidates["ExitOption"] = [
            choices.get((side, path, band), "Current")
            for side, path, band in zip(
                candidates["Side"], candidates["ResearchPath"], candidates["RiskBand"]
            )
        ]
    else:
        choices = frozen_exit_choices(root)
        candidates["ExitOption"] = [
            choices.get((month, side, path, band), "Current")
            for month, side, path, band in zip(
                candidates["Month"],
                candidates["Side"],
                candidates["ResearchPath"],
                candidates["RiskBand"],
            )
        ]
    exit_options = {
        (row.SnapshotID, row.SignalID, row.ResearchPath): row.ExitOption
        for row in candidates.itertuples(index=False)
    }
    exact_outcomes = connector.predict_outcomes(
        calibration, bars, turns, trades, exit_options
    )
    tape = candidates.merge(exact_outcomes, on=KEY, validate="one_to_one")

    scores = frozen_secondary_scores(root)
    static_scores, static_global = static_secondary_scores(root)
    tape["SecondaryScore"] = [
        secondary_score(
            scores,
            static_scores,
            static_global,
            month,
            side,
            path,
            band,
        )
        for month, side, path, band in zip(
            tape["Month"],
            tape["Side"],
            tape["ResearchPath"],
            tape["SecondaryRiskBand"],
        )
    ]
    return tape


def simulate(
    tape: pd.DataFrame,
    delay_minutes: int,
    concurrent: bool,
    secondary_score_floor: float = 0.0,
    same_direction_secondary: bool = False,
):
    accepted = []
    diagnostics = {
        "PrimaryAccepted": 0,
        "SecondaryAccepted": 0,
        "StaticRiskBandBlocked": 0,
        "ActiveBlocked": 0,
        "SecondaryGateBlocked": 0,
        "RiskCapBlocked": 0,
        "DailyCapBlocked": 0,
        "DailyLossBlocked": 0,
        "PeakConcurrentRisk": 0.0,
        "RiskCapViolations": 0,
    }
    for snapshot_id, day in tape.groupby("SnapshotID", sort=False):
        active = {"Primary": None, "Secondary": None}
        normal_count = 0
        realized_net = 0.0
        for row in day.sort_values(["EntryTime", "SourceSequence"]).itertuples(index=False):
            for lane in ("Primary", "Secondary"):
                trade = active[lane]
                if trade is not None and not trade["Realized"] and trade["ExitTime"] < row.EntryTime:
                    realized_net += trade["Net"]
                    trade["Realized"] = True
                if trade is not None and trade["ActiveUntil"] < row.EntryTime:
                    if not trade["Realized"]:
                        realized_net += trade["Net"]
                    active[lane] = None

            if 13.25 < float(row.ExactRisk) <= 16.0:
                diagnostics["StaticRiskBandBlocked"] += 1
                continue
            if normal_count >= 18:
                diagnostics["DailyCapBlocked"] += 1
                continue
            if realized_net <= -450.0:
                diagnostics["DailyLossBlocked"] += 1
                continue

            if active["Primary"] is None:
                lane = "Primary"
            elif concurrent and active["Secondary"] is None:
                if same_direction_secondary and row.Side != active["Primary"]["Side"]:
                    diagnostics["SecondaryGateBlocked"] += 1
                    continue
                if float(row.SecondaryScore) < secondary_score_floor:
                    diagnostics["SecondaryGateBlocked"] += 1
                    continue
                lane = "Secondary"
            else:
                diagnostics["ActiveBlocked"] += 1
                continue

            risk_dollars = float(row.ExactRisk) * 2.0 * 3
            concurrent_risk = risk_dollars + sum(
                item["RiskDollars"] for item in active.values() if item is not None
            )
            if concurrent_risk > 300.0:
                diagnostics["RiskCapBlocked"] += 1
                continue
            if concurrent_risk > 300.0 + 1e-9:
                diagnostics["RiskCapViolations"] += 1

            record = {
                "SnapshotID": snapshot_id,
                "TradingDate": row.TradingDate,
                "Month": row.Month,
                "SignalID": row.SignalID,
                "ResearchPath": row.ResearchPath,
                "Side": row.Side,
                "Lane": lane,
                "EntryTime": row.EntryTime,
                "ExitTime": row.PredictedExitTime,
                "ActiveUntil": row.PredictedExitTime + pd.Timedelta(minutes=delay_minutes),
                "ExitRole": row.PredictedExitRole,
                "ExitOption": row.ExitOption,
                "RiskBand": row.RiskBand,
                "ExactRisk": float(row.ExactRisk),
                "RiskDollars": risk_dollars,
                "SecondaryScore": float(row.SecondaryScore),
                "Gross": float(row.PredictedGross),
                "Net": float(row.PredictedGross) - 3.6,
                "Realized": False,
            }
            accepted.append(record)
            active[lane] = record
            normal_count += 1
            diagnostics[f"{lane}Accepted"] += 1
            diagnostics["PeakConcurrentRisk"] = max(
                diagnostics["PeakConcurrentRisk"], concurrent_risk
            )

    return pd.DataFrame(accepted), diagnostics


def metrics(trades: pd.DataFrame):
    positive = trades.loc[trades["Net"] > 0, "Net"].sum()
    negative = -trades.loc[trades["Net"] < 0, "Net"].sum()
    block = pd.cut(
        trades["TradingDate"].dt.month,
        [0, 2, 4, 6],
        labels=["JanFeb", "MarApr", "MayJun"],
    )
    block_gross = trades.groupby(block, observed=False)["Gross"].sum()
    return {
        "Trades": len(trades),
        "Gross": float(trades["Gross"].sum()),
        "Net": float(trades["Net"].sum()),
        "PF": float(positive / negative) if negative else float("inf"),
        "JanFebGross": float(block_gross.get("JanFeb", 0.0)),
        "MarAprGross": float(block_gross.get("MarApr", 0.0)),
        "MayJunGross": float(block_gross.get("MayJun", 0.0)),
    }


def coarse_metrics(
    root: Path,
    dates: set,
    delay: int,
    candidate: bool,
    secondary_policy: str,
    source: Path | None = None,
):
    trade_source = (
        source
        if candidate and source is not None
        else root / "Reports/v214_h1_dynamic_exit_concurrency/concurrency_trades.csv"
    )
    trades = pd.read_csv(
        trade_source,
        parse_dates=["TradingDate"],
    )
    mask = trades["TradingDate"].dt.strftime("%Y-%m-%d").isin(dates)
    if candidate:
        mask &= (
            trades["Arm"].eq("D_3plus3")
            & trades["SecondaryPolicy"].eq(secondary_policy)
            & trades["RiskCap"].eq(300.0)
        )
    else:
        mask &= trades["Arm"].eq("A_3x1")
    mask &= trades["DelayMinutes"].eq(float(delay))
    selected = trades[mask & trades["CountsNormal"].astype(bool)].copy()
    selected.rename(columns={"Gross3": "Gross", "Net3": "Net"}, inplace=True)
    return selected, metrics(selected)


def h1_robust_metrics(root: Path, secondary_policy: str, source: Path | None = None):
    rows = pd.read_csv(
        source or root / "Reports/v214_h1_dynamic_exit_concurrency/concurrency_robust.csv"
    )
    selected = rows[
        rows["Arm"].eq("D_3plus3")
        & rows["SecondaryPolicy"].eq(secondary_policy)
        & rows["RiskCap"].eq(300.0)
    ]
    if len(selected) != 1:
        raise RuntimeError(
            f"Expected one H1 robust row for {secondary_policy}, found {len(selected)}"
        )
    row = selected.iloc[0]
    return {
        "Gross": float(row["RobustConservativeGross"]),
        "PF": float(row["RobustPF"]),
        "JanFebGross": float(row["RobustJanFebGross"]),
        "MarAprGross": float(row["RobustMarAprGross"]),
        "MayJunGross": float(row["RobustMayJunGross"]),
    }


def lower_ratio_projection(exact_daily, coarse_daily, h1_gross, seed):
    paired = pd.concat(
        [exact_daily.rename("Exact"), coarse_daily.rename("Coarse")], axis=1
    ).fillna(0.0)
    paired["Month"] = pd.to_datetime(paired.index).month
    groups = [group[["Exact", "Coarse"]].to_numpy() for _, group in paired.groupby("Month")]
    rng = np.random.default_rng(seed)
    exact_sums = np.zeros(100000)
    coarse_sums = np.zeros(100000)
    for group in groups:
        indexes = rng.integers(0, len(group), size=(100000, len(group)))
        exact_sums += group[indexes, 0].sum(axis=1)
        coarse_sums += group[indexes, 1].sum(axis=1)
    valid = coarse_sums > 0
    lower_ratio = float(np.quantile(exact_sums[valid] / coarse_sums[valid], 0.05))
    return lower_ratio, h1_gross * lower_ratio


def main():
    parser = argparse.ArgumentParser()
    source = parser.add_mutually_exclusive_group(required=True)
    source.add_argument("--evidence", type=Path)
    source.add_argument("--candidate-tape", type=Path)
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument(
        "--secondary-score-floor",
        type=int,
        choices=SECONDARY_SCORE_FLOORS,
        default=0,
    )
    parser.add_argument("--same-direction-secondary", action="store_true")
    parser.add_argument("--static-secondary-score", action="store_true")
    parser.add_argument("--static-dynamic-exit", action="store_true")
    parser.add_argument("--h1-robust", type=Path)
    parser.add_argument("--coarse-trades", type=Path)
    args = parser.parse_args()

    root = Path(__file__).resolve().parents[1]
    connector = load_module(Path(__file__).parent / "Analyze-OPFV211CalibrationConnector.py")
    frozen_date_series = pd.to_datetime(
        pd.read_csv(root / "Reports/v214_h1_exact_calibration_24day_dates.csv")[
            "TradingDate"
        ]
    )
    frozen_dates = set(frozen_date_series.dt.strftime("%Y-%m-%d"))
    if args.candidate_tape:
        if args.static_dynamic_exit:
            raise RuntimeError(
                "Static dynamic exit requires --evidence so exact outcomes can be rebuilt"
            )
        tape = pd.read_csv(
            args.candidate_tape,
            parse_dates=["EntryTime", "PredictedExitTime", "TradingDate"],
        )
    else:
        tape = exact_candidate_tape(
            connector,
            args.evidence,
            root,
            list(frozen_date_series),
            args.static_dynamic_exit,
        )
    if args.static_secondary_score:
        score_map, global_score = static_secondary_scores(root)
        tape["SecondaryScore"] = [
            score_map.get((side, path, band), global_score)
            for side, path, band in zip(
                tape["Side"], tape["ResearchPath"], tape["RiskBand"]
            )
        ]
    policy_prefix = "StaticPathRisk" if args.static_secondary_score else "PathRisk"
    secondary_policy = f"{policy_prefix}A50G{args.secondary_score_floor}_0"
    if args.same_direction_secondary:
        secondary_policy = f"Same+{secondary_policy}"
    h1 = h1_robust_metrics(root, secondary_policy, args.h1_robust)
    rows = []
    all_trades = []
    for delay in (5, 10):
        exact_base, base_diag = simulate(tape, delay, False)
        exact_candidate, candidate_diag = simulate(
            tape,
            delay,
            True,
            args.secondary_score_floor,
            args.same_direction_secondary,
        )
        coarse_base, coarse_base_metrics = coarse_metrics(
            root, frozen_dates, delay, False, secondary_policy, args.coarse_trades
        )
        coarse_candidate, coarse_candidate_metrics = coarse_metrics(
            root, frozen_dates, delay, True, secondary_policy, args.coarse_trades
        )
        exact_base_metrics = metrics(exact_base)
        exact_candidate_metrics = metrics(exact_candidate)

        coarse_improvement = coarse_candidate_metrics["Gross"] - coarse_base_metrics["Gross"]
        exact_improvement = exact_candidate_metrics["Gross"] - exact_base_metrics["Gross"]
        retention = exact_improvement / coarse_improvement if coarse_improvement > 0 else np.nan

        exact_daily = exact_candidate.groupby("TradingDate")["Gross"].sum()
        coarse_daily = coarse_candidate.groupby("TradingDate")["Gross"].sum()
        lower_ratio, projected = lower_ratio_projection(
            exact_daily, coarse_daily, h1["Gross"], 214 + delay
        )
        row = {
            "SecondaryPolicy": secondary_policy,
            "SecondaryScoreFloor": args.secondary_score_floor,
            "DelayMinutes": delay,
            **{f"CoarseBase{k}": v for k, v in coarse_base_metrics.items()},
            **{f"CoarseCandidate{k}": v for k, v in coarse_candidate_metrics.items()},
            **{f"ExactBase{k}": v for k, v in exact_base_metrics.items()},
            **{f"ExactCandidate{k}": v for k, v in exact_candidate_metrics.items()},
            "CoarseImprovement": coarse_improvement,
            "ExactImprovement": exact_improvement,
            "ImprovementRetentionPct": retention * 100.0,
            "ExactVsCoarseCandidatePct": exact_candidate_metrics["Gross"] / coarse_candidate_metrics["Gross"] * 100.0,
            "Lower95GrossRatioPct": lower_ratio * 100.0,
            "Lower95ProjectedH1Gross": projected,
            "ProjectedPF": min(h1["PF"], exact_candidate_metrics["PF"]),
            **{f"Candidate{k}": v for k, v in candidate_diag.items()},
            **{f"Base{k}": v for k, v in base_diag.items()},
        }
        for block in ("JanFeb", "MarApr", "MayJun"):
            row[f"Lower95Projected{block}Gross"] = (
                h1[f"{block}Gross"] * lower_ratio
            )
        rows.append(row)

        exact_base = exact_base.assign(Arm="ExactBase", DelayMinutes=delay)
        exact_candidate = exact_candidate.assign(Arm="ExactCandidate", DelayMinutes=delay)
        all_trades.extend([exact_base, exact_candidate])

    results = pd.DataFrame(rows)
    worst = results.sort_values("Lower95ProjectedH1Gross").iloc[0]
    gate = {
        "SecondaryPolicy": secondary_policy,
        "SecondaryScoreFloor": args.secondary_score_floor,
        "WorstDelayMinutes": int(worst["DelayMinutes"]),
        "ImprovementRetentionPct": round(float(worst["ImprovementRetentionPct"]), 2),
        "ExactVsCoarseCandidatePct": round(float(worst["ExactVsCoarseCandidatePct"]), 2),
        "Lower95GrossRatioPct": round(float(worst["Lower95GrossRatioPct"]), 2),
        "Lower95ProjectedH1Gross": round(float(worst["Lower95ProjectedH1Gross"]), 2),
        "ProjectedPF": round(float(worst["ProjectedPF"]), 4),
        "Lower95ProjectedJanFebGross": round(float(worst["Lower95ProjectedJanFebGross"]), 2),
        "Lower95ProjectedMarAprGross": round(float(worst["Lower95ProjectedMarAprGross"]), 2),
        "Lower95ProjectedMayJunGross": round(float(worst["Lower95ProjectedMayJunGross"]), 2),
        "PeakConcurrentRisk": round(float(worst["CandidatePeakConcurrentRisk"]), 2),
        "RiskCapViolations": int(worst["CandidateRiskCapViolations"]),
    }
    gate["ExactCalibrationPassed"] = bool(
        gate["ImprovementRetentionPct"] >= 60.0
        and gate["Lower95GrossRatioPct"] >= MIN_LOWER95_GROSS_RATIO_PCT
        and gate["Lower95ProjectedH1Gross"] >= 20000.0
        and gate["ProjectedPF"] >= 1.35
        and min(
            gate["Lower95ProjectedJanFebGross"],
            gate["Lower95ProjectedMarAprGross"],
            gate["Lower95ProjectedMayJunGross"],
        ) > 0
        and gate["RiskCapViolations"] == 0
    )

    args.output_dir.mkdir(parents=True, exist_ok=True)
    tape.to_csv(args.output_dir / "exact_candidate_tape.csv", index=False)
    pd.concat(all_trades, ignore_index=True).to_csv(
        args.output_dir / "exact_calibration_trades.csv", index=False
    )
    results.to_csv(args.output_dir / "exact_calibration_by_delay.csv", index=False)
    pd.DataFrame([gate]).to_csv(args.output_dir / "exact_calibration_gate.csv", index=False)
    print(pd.DataFrame([gate]).to_string(index=False))


if __name__ == "__main__":
    main()
