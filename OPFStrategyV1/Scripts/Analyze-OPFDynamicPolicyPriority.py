import argparse
from pathlib import Path

import numpy as np
import pandas as pd


KEY = ["SnapshotID", "SignalID", "ResearchPath", "EntryBar"]
BASE = ["Side", "ResearchPath", "ExitPolicy", "Season"]
CONTEXT = [
    "RegimeScore",
    "QualityBin",
    "RiskBin",
    "Session",
    "ZoneType",
    "ZoneFreshness",
    "TouchBin",
    "AtrBin",
    "RegimeBucket",
    "SetupQualityBucket",
    "RiskBucket",
    "EstimatedRRBucket",
    "TimeBucket",
    "RangeBin",
    "BodyRatioBin",
    "CloseLocationBin",
    "ZoneWidthBin",
    "ZoneAgeBin",
    "PullbackBin",
    "TrendStrengthBin",
    "AlignmentBin",
    "SkipPattern",
    "HasZoneBirthResearch",
    "HasObservationConfirmResearch",
    "HasObservationStrictResearch",
    "HasFailureReverseResearch",
    "HasFailureRetestTrigger",
    "HasBreakawayQualified",
    "ConfirmAvailable",
    "ConfirmReason",
    "ConfirmRangeBin",
    "ConfirmBodyRatioBin",
    "ConfirmCloseLocationBin",
    "ConfirmAlignmentBin",
    "ConfirmPrevBreakBin",
    "ConfirmZoneReclaimBin",
]

SKIP_FEATURES = {
    "ZoneBirthResearch": "HasZoneBirthResearch",
    "ObservationConfirmResearch": "HasObservationConfirmResearch",
    "ObservationConfirmStrictResearch": "HasObservationStrictResearch",
    "FailureReverseResearch": "HasFailureReverseResearch",
    "FailureRetestFailedTriggered": "HasFailureRetestTrigger",
    "BreakawayQualified": "HasBreakawayQualified",
}


def read_csvs(root: Path, pattern: str) -> pd.DataFrame:
    files = list(root.rglob(pattern))
    return pd.concat((pd.read_csv(path, low_memory=False) for path in files), ignore_index=True)


def load_rows(root: Path, conservative: bool) -> pd.DataFrame:
    decisions = read_csvs(root, "*_execution_decisions.csv")
    policies = read_csvs(root, "*_exit_policy_evaluations.csv")
    edges = read_csvs(root, "*_edge_attribution.csv")
    risks = read_csvs(root, "*_risk_evaluations.csv")
    candidate_evaluations = read_csvs(root, "*_candidate_evaluations.csv")
    signals = read_csvs(root, "*_signals.csv")
    confirmations = read_csvs(root, "*_confirmation_evaluations.csv")
    regime_changes = read_csvs(root, "*_regime_changes.csv")
    reason = decisions["Reason"].fillna("")
    decisions = decisions[
        decisions["Decision"].eq("Execute")
        | reason.str.match(r"^(ActiveTrade:|DailyTradeLimit:|LiveDailyLoss:|DailyLoss:|DailyTarget:)")
    ].copy()
    decisions.rename(columns={"Bar": "EntryBar", "Time": "EntryTime"}, inplace=True)
    decisions.drop_duplicates(KEY + ["EntryTime"], inplace=True)

    edges = edges[edges["EventType"].eq("Tracked")].copy()
    edges.rename(columns={"EventBar": "EntryBar"}, inplace=True)
    edge_columns = KEY + [
        "ZoneType",
        "ZoneFreshness",
        "ZoneTouchCount",
        "RegimeBucket",
        "SetupQualityBucket",
        "RiskBucket",
        "EstimatedRRBucket",
        "TimeBucket",
        "ZoneID",
    ]
    edges.drop_duplicates(KEY, inplace=True)
    decisions = decisions.merge(edges[edge_columns], on=KEY, how="left")

    candidate_evaluations.rename(columns={"Bar": "EntryBar"}, inplace=True)
    candidate_columns = [
        "SnapshotID",
        "EntryBar",
        "ZoneID",
        "Open",
        "High",
        "Low",
        "Close",
        "ZoneWidth",
        "CreatedBar",
        "PullbackCountInRegime",
    ]
    candidate_evaluations.drop_duplicates(["SnapshotID", "EntryBar", "ZoneID"], inplace=True)
    decisions = decisions.merge(
        candidate_evaluations[candidate_columns], on=["SnapshotID", "EntryBar", "ZoneID"], how="left"
    )

    signals.rename(columns={"Bar": "EntryBar"}, inplace=True)
    signals.drop_duplicates(["SnapshotID", "SignalID", "EntryBar"], keep="last", inplace=True)
    decisions = decisions.merge(
        signals[["SnapshotID", "SignalID", "EntryBar", "SkipReasons"]],
        on=["SnapshotID", "SignalID", "EntryBar"],
        how="left",
    )

    decisions["ConfirmationSourceID"] = decisions["SignalID"].str.replace(
        r"-OC(?:-STR)?$", "", regex=True
    )
    confirmations = confirmations[
        confirmations["Result"].eq("Confirmed") & confirmations["Stage"].eq("ObservationConfirm")
    ].copy()
    confirmations.rename(
        columns={
            "SignalID": "ConfirmationSourceID",
            "Bar": "EntryBar",
            "Open": "ConfirmOpen",
            "High": "ConfirmHigh",
            "Low": "ConfirmLow",
            "Close": "ConfirmClose",
            "ZoneLow": "ConfirmZoneLow",
            "ZoneHigh": "ConfirmZoneHigh",
            "PrevHigh": "ConfirmPrevHigh",
            "PrevLow": "ConfirmPrevLow",
            "Reason": "ConfirmReason",
        },
        inplace=True,
    )
    confirmation_columns = [
        "SnapshotID",
        "ConfirmationSourceID",
        "EntryBar",
        "ConfirmOpen",
        "ConfirmHigh",
        "ConfirmLow",
        "ConfirmClose",
        "ConfirmZoneLow",
        "ConfirmZoneHigh",
        "ConfirmPrevHigh",
        "ConfirmPrevLow",
        "ConfirmReason",
    ]
    confirmations.drop_duplicates(
        ["SnapshotID", "ConfirmationSourceID", "EntryBar"], keep="last", inplace=True
    )
    decisions = decisions.merge(
        confirmations[confirmation_columns],
        on=["SnapshotID", "ConfirmationSourceID", "EntryBar"],
        how="left",
    )

    risk_columns = KEY + ["ATR14"]
    risks.rename(columns={"Bar": "EntryBar"}, inplace=True)
    risks.drop_duplicates(KEY, inplace=True)
    decisions = decisions.merge(risks[risk_columns], on=KEY, how="left")

    regime_changes.rename(columns={"Bar": "EntryBar", "Regime": "DetailedRegime"}, inplace=True)
    enriched = []
    for snapshot_id, group in decisions.groupby("SnapshotID"):
        changes = regime_changes[regime_changes["SnapshotID"].eq(snapshot_id)][
            ["EntryBar", "DetailedRegime", "BullScore", "BearScore"]
        ].sort_values("EntryBar")
        if changes.empty:
            group = group.assign(DetailedRegime="Unknown", BullScore=0, BearScore=0)
        else:
            group = pd.merge_asof(group.sort_values("EntryBar"), changes, on="EntryBar", direction="backward")
            group["SnapshotID"] = snapshot_id
        enriched.append(group)
    decisions = pd.concat(enriched, ignore_index=True)

    columns = KEY + [
        "Side",
        "ExitPolicy",
        "PolicyExitBar",
        "PnLDollars",
        "AmbiguousStopAndTargetSameBar",
        "InitialRiskPoints",
    ]
    rows = decisions.merge(policies[columns], on=KEY + ["Side"], how="inner", suffixes=("", "_Policy"))
    rows["EntryTime"] = pd.to_datetime(rows["EntryTime"])
    rows["EntryBar"] = rows["EntryBar"].astype(int)
    rows["PolicyExitBar"] = rows["PolicyExitBar"].fillna(rows["EntryBar"] + 1).astype(int)
    rows["OutcomeNet"] = rows["PnLDollars"].astype(float) - 2.4
    if conservative:
        ambiguous = rows["AmbiguousStopAndTargetSameBar"].astype(str).eq("True")
        rows.loc[ambiguous, "OutcomeNet"] = -4 * rows.loc[ambiguous, "InitialRiskPoints_Policy"] - 2.4

    rows["RegimeScore"] = rows["RegimeScore"].fillna(0).astype(int)
    rows["SetupQualityScore"] = rows["SetupQualityScore"].fillna(0).astype(float)
    rows["InitialRiskPoints"] = rows["InitialRiskPoints"].fillna(0).astype(float)
    rows["QualityBin"] = pd.cut(
        rows["SetupQualityScore"], [-1, 45, 55, 70, 85, float("inf")], labels=False
    ).astype(int)
    rows["RiskBin"] = pd.cut(
        rows["InitialRiskPoints"], [-1, 8, 12, 18, float("inf")], labels=False
    ).astype(int)
    hour = rows["EntryTime"].dt.hour
    rows["Session"] = pd.cut(hour, [-1, 5, 12, 19, 23], labels=["Asia", "Europe", "US", "Late"]).astype(str)
    rows["Season"] = np.where(rows["EntryTime"].dt.month.ge(10), "Q4", "H1")
    rows["TouchBin"] = rows["ZoneTouchCount"].fillna(0).clip(upper=2).astype(int).astype(str)
    rows["AtrBin"] = pd.cut(
        rows["ATR14"].fillna(0), [-1, 8, 12, 16, 24, float("inf")], labels=False
    ).astype(int).astype(str)
    candle_range = (rows["High"] - rows["Low"]).fillna(0).clip(lower=0)
    body_ratio = ((rows["Close"] - rows["Open"]).abs() / candle_range.replace(0, np.nan)).fillna(0)
    close_location = ((rows["Close"] - rows["Low"]) / candle_range.replace(0, np.nan)).fillna(0.5)
    rows["CandleRange"] = candle_range
    rows["BodyRatio"] = body_ratio
    rows["CloseLocation"] = close_location
    rows["RangeBin"] = pd.cut(candle_range, [-1, 5, 10, 15, 25, float("inf")], labels=False).astype(str)
    rows["BodyRatioBin"] = pd.cut(body_ratio, [-1, 0.25, 0.5, 0.75, 1.01], labels=False).astype(str)
    rows["CloseLocationBin"] = pd.cut(
        close_location, [-1, 0.25, 0.5, 0.75, 1.01], labels=False
    ).astype(str)
    rows["ZoneWidthBin"] = pd.cut(
        rows["ZoneWidth"].fillna(0), [-1, 2, 4, 8, 12, float("inf")], labels=False
    ).astype(str)
    zone_age = (rows["EntryBar"] - rows["CreatedBar"].fillna(rows["EntryBar"])).clip(lower=0)
    rows["ZoneAge"] = zone_age
    rows["ZoneAgeBin"] = pd.cut(zone_age, [-1, 0, 2, 6, 12, float("inf")], labels=False).astype(str)
    rows["PullbackBin"] = rows["PullbackCountInRegime"].fillna(0).clip(upper=3).astype(int).astype(str)
    trend_strength = rows[["BullScore", "BearScore"]].fillna(0).max(axis=1)
    alignment = np.where(
        rows["Side"].eq("Long"), rows["BullScore"].fillna(0) - rows["BearScore"].fillna(0),
        rows["BearScore"].fillna(0) - rows["BullScore"].fillna(0),
    )
    rows["TrendStrengthBin"] = pd.cut(
        trend_strength, [-1, 30, 50, 70, 90, float("inf")], labels=False
    ).astype(str)
    rows["TrendStrength"] = trend_strength
    rows["Alignment"] = alignment
    rows["AlignmentBin"] = pd.cut(
        alignment, [-float("inf"), -30, 0, 30, float("inf")], labels=False
    ).astype(str)
    skip_tokens = rows["SkipReasons"].fillna("").str.split("|")
    normalized_tokens = skip_tokens.map(
        lambda tokens: sorted({token.split("=", 1)[0].split(":", 1)[0] for token in tokens if token})
    )
    rows["SkipPattern"] = normalized_tokens.map(lambda tokens: "+".join(tokens) if tokens else "None")
    for token, column in SKIP_FEATURES.items():
        rows[column] = normalized_tokens.map(lambda tokens, expected=token: str(expected in tokens))

    rows["ConfirmAvailable"] = rows["ConfirmReason"].notna().astype(str)
    rows["ConfirmReason"] = rows["ConfirmReason"].fillna("Unknown").astype(str)
    confirm_range = (rows["ConfirmHigh"] - rows["ConfirmLow"]).clip(lower=0)
    confirm_body_ratio = (
        (rows["ConfirmClose"] - rows["ConfirmOpen"]).abs() / confirm_range.replace(0, np.nan)
    )
    confirm_close_location = (
        (rows["ConfirmClose"] - rows["ConfirmLow"]) / confirm_range.replace(0, np.nan)
    )
    confirm_alignment = pd.Series(
        np.where(
            rows["Side"].eq("Long"),
            rows["ConfirmClose"] - rows["ConfirmOpen"],
            rows["ConfirmOpen"] - rows["ConfirmClose"],
        ),
        index=rows.index,
    )
    confirm_prev_break = pd.Series(
        np.where(
            rows["Side"].eq("Long"),
            rows["ConfirmClose"] - rows["ConfirmPrevHigh"],
            rows["ConfirmPrevLow"] - rows["ConfirmClose"],
        ),
        index=rows.index,
    )
    confirm_zone_reclaim = pd.Series(
        np.where(
            rows["Side"].eq("Long"),
            rows["ConfirmClose"] - rows["ConfirmZoneHigh"],
            rows["ConfirmZoneLow"] - rows["ConfirmClose"],
        ),
        index=rows.index,
    )
    rows["ConfirmRange"] = confirm_range.fillna(0)
    rows["ConfirmBodyRatio"] = confirm_body_ratio.fillna(0)
    rows["ConfirmCloseLocation"] = confirm_close_location.fillna(0.5)
    rows["ConfirmAlignment"] = confirm_alignment.fillna(0)
    rows["ConfirmPrevBreak"] = confirm_prev_break.fillna(0)
    rows["ConfirmZoneReclaim"] = confirm_zone_reclaim.fillna(0)
    rows["ConfirmRangeBin"] = pd.cut(
        confirm_range, [-float("inf"), 5, 10, 15, 25, float("inf")], labels=False
    ).fillna(-1).astype(int).astype(str)
    rows["ConfirmBodyRatioBin"] = pd.cut(
        confirm_body_ratio, [-float("inf"), 0.25, 0.5, 0.75, float("inf")], labels=False
    ).fillna(-1).astype(int).astype(str)
    rows["ConfirmCloseLocationBin"] = pd.cut(
        confirm_close_location, [-float("inf"), 0.25, 0.5, 0.75, float("inf")], labels=False
    ).fillna(-1).astype(int).astype(str)
    rows["ConfirmAlignmentBin"] = pd.cut(
        confirm_alignment, [-float("inf"), -2, 0, 2, 6, float("inf")], labels=False
    ).fillna(-1).astype(int).astype(str)
    rows["ConfirmPrevBreakBin"] = pd.cut(
        confirm_prev_break, [-float("inf"), -5, -1, 0, 2, float("inf")], labels=False
    ).fillna(-1).astype(int).astype(str)
    rows["ConfirmZoneReclaimBin"] = pd.cut(
        confirm_zone_reclaim, [-float("inf"), 0, 2, 5, 10, float("inf")], labels=False
    ).fillna(-1).astype(int).astype(str)
    for column in [
        "ZoneType",
        "ZoneFreshness",
        "RegimeBucket",
        "SetupQualityBucket",
        "RiskBucket",
        "EstimatedRRBucket",
        "TimeBucket",
    ]:
        rows[column] = rows[column].fillna("Unknown").astype(str)
    rows["Month"] = rows["EntryTime"].dt.to_period("M").astype(str)
    return rows


def train(rows: pd.DataFrame, alpha: float):
    global_mean = rows["OutcomeNet"].mean()
    base = rows.groupby(BASE)["OutcomeNet"].agg(["sum", "count"])
    base["score"] = (base["sum"] + alpha * global_mean) / (base["count"] + alpha)
    context_scores = {}
    for column in CONTEXT:
        grouped = rows.groupby(BASE + [column])["OutcomeNet"].agg(["sum", "count"]).reset_index()
        grouped = grouped.merge(base[["score"]], on=BASE, how="left")
        grouped["context_score"] = (grouped["sum"] + alpha * grouped["score"]) / (grouped["count"] + alpha)
        context_scores[column] = grouped.set_index(BASE + [column])["context_score"].to_dict()
    return global_mean, base["score"].to_dict(), context_scores


def score(rows: pd.DataFrame, model) -> pd.Series:
    global_mean, base_scores, context_scores = model
    values = []
    for row in rows.itertuples(index=False):
        base_key = tuple(getattr(row, name) for name in BASE)
        base_score = base_scores.get(base_key, global_mean)
        parts = [
            context_scores[column].get(base_key + (getattr(row, column),), base_score)
            for column in CONTEXT
        ]
        values.append(sum(parts) / len(parts))
    return pd.Series(values, index=rows.index)


def choose_policy(rows: pd.DataFrame) -> pd.DataFrame:
    candidate_key = KEY + ["EntryTime"]
    ordered = rows.sort_values(candidate_key + ["Score"], ascending=[True] * len(candidate_key) + [False])
    return ordered.drop_duplicates(candidate_key, keep="first")


def simulate(rows: pd.DataFrame, threshold: float, daily_limit: int):
    trades = 0
    total = 0.0
    by_period = {"H1": [0, 0.0], "Q4": [0, 0.0]}
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
            active_until = row.PolicyExitBar
            period = "Q4" if row.EntryTime.year == 2025 else "H1"
            by_period[period][0] += 1
            by_period[period][1] += row.OutcomeNet
    return trades, round(total, 2), by_period


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--h1", type=Path, required=True)
    parser.add_argument("--q4", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()

    results = []
    for conservative in (False, True):
        combined = pd.concat([load_rows(args.h1, conservative), load_rows(args.q4, conservative)], ignore_index=True)
        for alpha in (5, 10, 20, 50):
            selected = []
            for month in sorted(combined["Month"].unique()):
                train_rows = combined[combined["Month"].ne(month)]
                test_rows = combined[combined["Month"].eq(month)].copy()
                test_rows["Score"] = score(test_rows, train(train_rows, alpha))
                selected.append(choose_policy(test_rows))
            scored = pd.concat(selected, ignore_index=True)
            for threshold in (0, 5, 10, 15):
                for limit in (15, 0):
                    trades, net, periods = simulate(scored, threshold, limit)
                    results.append(
                        {
                            "Mode": "Conservative" if conservative else "Observed",
                            "Alpha": alpha,
                            "Threshold": threshold,
                            "DailyLimit": limit,
                            "H1Trades": periods["H1"][0],
                            "H1Net": round(periods["H1"][1], 2),
                            "Q4Trades": periods["Q4"][0],
                            "Q4Net": round(periods["Q4"][1], 2),
                            "CombinedTrades": trades,
                            "CombinedNet": net,
                        }
                    )
    output = pd.DataFrame(results).sort_values(["Mode", "CombinedNet"], ascending=[True, False])
    args.output.parent.mkdir(parents=True, exist_ok=True)
    output.to_csv(args.output, index=False)
    print(output.groupby("Mode").head(15).to_string(index=False))


if __name__ == "__main__":
    main()
