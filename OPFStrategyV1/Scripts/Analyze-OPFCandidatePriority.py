import argparse
from pathlib import Path

import numpy as np
import pandas as pd


POLICIES = {
    ("Long", "ObservationConfirm_WideStop1_5R"): "Fixed2_5R",
    ("Short", "ObservationConfirm"): "Protect1RAfter1_5R_Then2_5R",
    ("Short", "ZoneBirthResearch"): "ProtectBE1R_Then2_5R",
    ("Short", "BreakawayFvg"): "Fixed3R",
    ("Short", "ObservationConfirm_WideStop1_5R"): "ProtectBE0_75R_Then2_5R",
    ("Long", "ObservationStrict_Other"): "Fixed2_5R",
    ("Short", "UnknownRegimeZoneTouch"): "Protect1RAfter1_5R_Then3R",
    ("Short", "ShadowCandidate"): "Fixed2R",
    ("Long", "AlmostConfirmed"): "Fixed2_5R",
    ("Short", "FailureReverse_ObservationInvalidated"): "Protect1RAfter1_5R_Then3R",
    ("Long", "FailureReverse_ObservationInvalidated"): "Fixed1_5R",
    ("Long", "ObservationStrict_BullFresh_WideStop1_5R"): "ProtectBE1R_Then3R",
    ("Long", "FailureReverse_RetestFailed"): "Fixed3R",
    ("Short", "FailureReverse_ObservationInvalidated_WideStop1_5R"): "ProtectBE0_75R_Then2_5R",
    ("Long", "BreakawayFvg_Qualified"): "Fixed2R",
    ("Short", "FailureReverse_RetestFailed"): "ProtectBE0_75R_Then2_5R",
}

KEY = ["SnapshotID", "SignalID", "ResearchPath", "EntryBar"]
FEATURES = ["Side", "ResearchPath", "RegimeScore", "QualityBin", "RiskBin"]


def read_csvs(root: Path, pattern: str) -> pd.DataFrame:
    files = list(root.rglob(pattern))
    return pd.concat((pd.read_csv(path, low_memory=False) for path in files), ignore_index=True)


def load_candidates(root: Path, conservative: bool) -> pd.DataFrame:
    decisions = read_csvs(root, "*_execution_decisions.csv")
    shadows = read_csvs(root, "*_shadow_trades.csv")
    policies = read_csvs(root, "*_exit_policy_evaluations.csv")

    reason = decisions["Reason"].fillna("")
    candidates = decisions[
        decisions["Decision"].eq("Execute")
        | reason.str.match(r"^(ActiveTrade:|DailyTradeLimit:|LiveDailyLoss:|DailyLoss:|DailyTarget:)")
    ].copy()
    candidates.rename(columns={"Bar": "EntryBar", "Time": "EntryTime"}, inplace=True)
    candidates.drop_duplicates(KEY + ["EntryTime"], inplace=True)

    shadow_outcomes = shadows[KEY + ["ExitBar", "NetDollars", "Ambiguous"]].copy()
    shadow_outcomes.rename(columns={"NetDollars": "OutcomeNet"}, inplace=True)
    candidates = candidates.merge(shadow_outcomes, on=KEY, how="left")

    policy_parts = []
    for (side, path), policy in POLICIES.items():
        rows = policies[
            policies["Side"].eq(side)
            & policies["ResearchPath"].eq(path)
            & policies["ExitPolicy"].eq(policy)
        ][KEY + ["PolicyExitBar", "PnLDollars", "AmbiguousStopAndTargetSameBar", "InitialRiskPoints"]].copy()
        rows["Side"] = side
        rows["ResearchPath"] = path
        policy_parts.append(rows)
    policy_outcomes = pd.concat(policy_parts, ignore_index=True)
    policy_outcomes.rename(
        columns={
            "PolicyExitBar": "OverrideExitBar",
            "PnLDollars": "OverrideGross",
            "AmbiguousStopAndTargetSameBar": "OverrideAmbiguous",
            "InitialRiskPoints": "OverrideRisk",
        },
        inplace=True,
    )
    candidates = candidates.merge(policy_outcomes, on=KEY + ["Side", "ResearchPath"], how="left")

    has_override = candidates["OverrideGross"].notna()
    candidates.loc[has_override, "ExitBar"] = candidates.loc[has_override, "OverrideExitBar"]
    candidates.loc[has_override, "OutcomeNet"] = candidates.loc[has_override, "OverrideGross"] - 2.4
    if conservative:
        ambiguous = has_override & candidates["OverrideAmbiguous"].astype(str).eq("True")
        candidates.loc[ambiguous, "OutcomeNet"] = -4 * candidates.loc[ambiguous, "OverrideRisk"] - 2.4

    candidates = candidates[candidates["OutcomeNet"].notna() & candidates["ExitBar"].notna()].copy()
    candidates["EntryTime"] = pd.to_datetime(candidates["EntryTime"])
    candidates["EntryBar"] = candidates["EntryBar"].astype(int)
    candidates["ExitBar"] = candidates["ExitBar"].astype(int)
    candidates["RegimeScore"] = candidates["RegimeScore"].fillna(0).astype(int)
    candidates["SetupQualityScore"] = candidates["SetupQualityScore"].fillna(0).astype(float)
    candidates["InitialRiskPoints"] = candidates["InitialRiskPoints"].fillna(0).astype(float)
    candidates["QualityBin"] = pd.cut(
        candidates["SetupQualityScore"], [-1, 45, 55, 70, 85, float("inf")], labels=False
    )
    candidates["RiskBin"] = pd.cut(
        candidates["InitialRiskPoints"], [-1, 8, 12, 18, float("inf")], labels=False
    )
    return candidates


def train_model(rows: pd.DataFrame, alpha: float):
    global_mean = rows["OutcomeNet"].mean()
    path = rows.groupby(["Side", "ResearchPath"])["OutcomeNet"].agg(["sum", "count"])
    path["score"] = (path["sum"] + alpha * global_mean) / (path["count"] + alpha)
    detailed = rows.groupby(FEATURES)["OutcomeNet"].agg(["sum", "count"]).reset_index()
    detailed = detailed.merge(path[["score"]], on=["Side", "ResearchPath"], how="left")
    detailed["detail_score"] = (
        detailed["sum"] + alpha * detailed["score"]
    ) / (detailed["count"] + alpha)
    return global_mean, path["score"].to_dict(), detailed.set_index(FEATURES)["detail_score"].to_dict()


def ridge_design(train: pd.DataFrame, test: pd.DataFrame):
    combined = pd.concat([train, test], ignore_index=True)
    combined["PathSide"] = combined["Side"] + "|" + combined["ResearchPath"]
    combined["RegimeCategory"] = combined["RegimeScore"].astype(str)
    combined["Hour"] = combined["EntryTime"].dt.hour
    combined["DayOfWeek"] = combined["EntryTime"].dt.dayofweek.astype(str)
    combined["QualityExact"] = combined["SetupQualityScore"].round().astype(int).astype(str)
    combined["RiskRounded"] = (combined["InitialRiskPoints"] / 2).round().mul(2).astype(str)
    combined["RRBin"] = pd.cut(
        combined["EstimatedRR"].fillna(0), [-1, 1, 1.5, 2, 2.5, 3, float("inf")], labels=False
    ).astype(str)
    combined["Season"] = np.where(combined["EntryTime"].dt.month.ge(10), "Q4", "H1")
    combined["Session"] = pd.cut(
        combined["Hour"], [-1, 5, 12, 19, 23], labels=["Asia", "Europe", "US", "Late"]
    ).astype(str)
    combined["PathRegime"] = combined["PathSide"] + "|" + combined["RegimeCategory"]
    combined["PathSession"] = combined["PathSide"] + "|" + combined["Session"]
    combined["PathHour"] = combined["PathSide"] + "|" + combined["Hour"].astype(str)
    combined["PathSeason"] = combined["PathSide"] + "|" + combined["Season"]
    categories = [
        "PathSide",
        "RegimeCategory",
        "QualityBin",
        "RiskBin",
        "Session",
        "Hour",
        "DayOfWeek",
        "QualityExact",
        "RiskRounded",
        "RRBin",
        "Season",
        "SetupType",
        "PathRegime",
        "PathSession",
        "PathHour",
        "PathSeason",
    ]
    categorical = pd.get_dummies(combined[categories].astype(str), dtype=float)
    continuous = combined[["SetupQualityScore", "InitialRiskPoints", "EstimatedRR"]].fillna(0).astype(float)
    train_continuous = continuous.iloc[: len(train)]
    mean = train_continuous.mean()
    std = train_continuous.std().replace(0, 1)
    continuous = (continuous - mean) / std
    matrix = np.column_stack([np.ones(len(combined)), continuous.to_numpy(), categorical.to_numpy()])
    return matrix[: len(train)], matrix[len(train) :]


def train_ridge(train: pd.DataFrame, test: pd.DataFrame, alpha: float):
    train_x, test_x = ridge_design(train, test)
    penalty = np.eye(train_x.shape[1]) * alpha
    penalty[0, 0] = 0
    beta = np.linalg.solve(train_x.T @ train_x + penalty, train_x.T @ train["OutcomeNet"].to_numpy())
    return test_x @ beta


def score_rows(rows: pd.DataFrame, model) -> pd.Series:
    global_mean, path_scores, detail_scores = model
    values = []
    for row in rows.itertuples(index=False):
        detail_key = tuple(getattr(row, name) for name in FEATURES)
        path_key = (row.Side, row.ResearchPath)
        values.append(detail_scores.get(detail_key, path_scores.get(path_key, global_mean)))
    return pd.Series(values, index=rows.index)


def simulate(rows: pd.DataFrame, threshold: float, daily_limit: int) -> tuple[int, float]:
    trades = 0
    total = 0.0
    for _, day in rows.groupby("SnapshotID"):
        active_until = -1
        daily_net = 0.0
        daily_trades = 0
        for row in day.sort_values(["EntryTime", "EntryBar"]).itertuples(index=False):
            if row.Score < threshold or row.EntryBar <= active_until or daily_net <= -300:
                continue
            if daily_limit and daily_trades >= daily_limit:
                continue
            trades += 1
            daily_trades += 1
            daily_net += row.OutcomeNet
            total += row.OutcomeNet
            active_until = row.ExitBar
    return trades, round(total, 2)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--h1", type=Path, required=True)
    parser.add_argument("--q4", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()

    results = []
    for conservative in (False, True):
        h1 = load_candidates(args.h1, conservative)
        q4 = load_candidates(args.q4, conservative)
        for alpha in (10, 20, 50):
            h1_model = train_model(h1, alpha)
            q4_model = train_model(q4, alpha)
            q4_scored = q4.assign(Score=score_rows(q4, h1_model))
            h1_scored = h1.assign(Score=score_rows(h1, q4_model))
            for threshold in (0, 5, 10, 15):
                for limit in (15, 0):
                    h1_trades, h1_net = simulate(h1_scored, threshold, limit)
                    q4_trades, q4_net = simulate(q4_scored, threshold, limit)
                    results.append(
                        {
                            "Model": "GroupedShrinkage",
                            "Validation": "CrossPeriod",
                            "Mode": "Conservative" if conservative else "Observed",
                            "Alpha": alpha,
                            "Threshold": threshold,
                            "DailyLimit": limit,
                            "H1Trades": h1_trades,
                            "H1Net": h1_net,
                            "Q4Trades": q4_trades,
                            "Q4Net": q4_net,
                            "CombinedTrades": h1_trades + q4_trades,
                            "CombinedNet": round(h1_net + q4_net, 2),
                        }
                    )
        for alpha in (100, 1000, 10000):
            q4_scored = q4.assign(Score=train_ridge(h1, q4, alpha))
            h1_scored = h1.assign(Score=train_ridge(q4, h1, alpha))
            for threshold in (0, 5, 10, 15):
                for limit in (15, 0):
                    h1_trades, h1_net = simulate(h1_scored, threshold, limit)
                    q4_trades, q4_net = simulate(q4_scored, threshold, limit)
                    results.append(
                        {
                            "Model": "Ridge",
                            "Validation": "CrossPeriod",
                            "Mode": "Conservative" if conservative else "Observed",
                            "Alpha": alpha,
                            "Threshold": threshold,
                            "DailyLimit": limit,
                            "H1Trades": h1_trades,
                            "H1Net": h1_net,
                            "Q4Trades": q4_trades,
                            "Q4Net": q4_net,
                            "CombinedTrades": h1_trades + q4_trades,
                            "CombinedNet": round(h1_net + q4_net, 2),
                        }
                    )
        combined = pd.concat([h1, q4], ignore_index=True)
        combined["Month"] = combined["EntryTime"].dt.to_period("M").astype(str)
        for model_name, alphas in (("GroupedShrinkage", (10, 20, 50)), ("Ridge", (100, 1000, 10000))):
            for alpha in alphas:
                scored_parts = []
                for month in sorted(combined["Month"].unique()):
                    train = combined[combined["Month"].ne(month)].copy()
                    test = combined[combined["Month"].eq(month)].copy()
                    if model_name == "GroupedShrinkage":
                        scores = score_rows(test, train_model(train, alpha))
                    else:
                        scores = train_ridge(train, test, alpha)
                    scored_parts.append(test.assign(Score=scores))
                scored = pd.concat(scored_parts, ignore_index=True)
                for threshold in (0, 5, 10, 15):
                    for limit in (15, 0):
                        h1_scored = scored[scored["EntryTime"].dt.year.eq(2026)]
                        q4_scored = scored[scored["EntryTime"].dt.year.eq(2025)]
                        h1_trades, h1_net = simulate(h1_scored, threshold, limit)
                        q4_trades, q4_net = simulate(q4_scored, threshold, limit)
                        results.append(
                            {
                                "Model": model_name,
                                "Validation": "LeaveOneMonthOut",
                                "Mode": "Conservative" if conservative else "Observed",
                                "Alpha": alpha,
                                "Threshold": threshold,
                                "DailyLimit": limit,
                                "H1Trades": h1_trades,
                                "H1Net": h1_net,
                                "Q4Trades": q4_trades,
                                "Q4Net": q4_net,
                                "CombinedTrades": h1_trades + q4_trades,
                                "CombinedNet": round(h1_net + q4_net, 2),
                            }
                        )
    output = pd.DataFrame(results).sort_values(["Mode", "CombinedNet"], ascending=[True, False])
    args.output.parent.mkdir(parents=True, exist_ok=True)
    output.to_csv(args.output, index=False)
    print(output.groupby("Mode").head(15).to_string(index=False))


if __name__ == "__main__":
    main()
