import argparse
import importlib.util
from pathlib import Path

import pandas as pd


STRICT_RISK_MIN_EXCLUSIVE = 13.25
STRICT_RISK_MAX_INCLUSIVE = 16.0
COMMISSION = 3.6
FROZEN_DAILY_CAP = 15
FROZEN_DAILY_LOSS = 250.0
FROZEN_COMBINED_RISK_CAP = 300.0
ACTUAL_G2_KEYS = frozenset(
    {
        "Short|FailureReverse_RetestFailed_WideStop1_5R|R10",
        "Short|TrendPullbackConfirmed|R30",
        "Short|ObservationConfirm_WideStop1_5R|R20",
        "Long|ObservationConfirm_WideStop1_5R|R10",
        "Short|UnknownRegimeZoneTouch|R10",
        "Short|FailureReverse_RetestFailed|R20",
        "Long|FailureReverse_ObservationInvalidated_WideStop1_5R|R10",
        "Long|BreakawayFvg_Qualified|R10",
        "Short|ShadowCandidate|R30",
        "Long|AlmostConfirmed|R10",
        "Short|ObservationConfirm_WideStop1_5R|R10",
        "Short|UnknownRegimeZoneTouch|R30",
        "Short|FailureReverse_ObservationInvalidated_WideStop1_5R|R10",
        "Long|BreakawayRetest|R10",
        "Long|BreakawayRetest|R20",
        "Long|ObservationConfirm|R20",
        "Long|ObservationStrict_Other_WideStop1_5R|R20",
        "Long|AlmostConfirmed|R30",
        "Long|FailureReverse_ObservationInvalidated_WideStop1_5R|R20",
        "Short|ZoneBirthResearch|R10",
        "Long|AlmostConfirmed|R20",
        "Long|ObservationConfirm_WideStop1_5R|R20",
        "Long|ObservationConfirm_WideStop1_5R|R30",
        "Short|FailureReverse_ObservationInvalidated|R20",
        "Long|ObservationStrict_Other|R30",
        "Short|FailureReverse_ObservationInvalidated_WideStop1_5R|R30",
        "Short|ObservationConfirm|R30",
        "Short|FailureReverse_ObservationInvalidated_WideStop1_5R|R20",
        "Short|BreakawayFvg|R30",
    }
)


def risk_band(value: float) -> str:
    if value <= 10.0:
        return "R10"
    if value <= 20.0:
        return "R20"
    if value <= 30.0:
        return "R30"
    return "RHigh"


def actual_g2_eligible(row) -> bool:
    key = f"{row.Side}|{row.ResearchPath}|{risk_band(float(row.InitialRiskPoints))}"
    return key in ACTUAL_G2_KEYS


def load_module(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def contains(reason: str, token: str) -> bool:
    return token in ("" if pd.isna(reason) else str(reason))


def max_allowed_risk(row) -> float:
    reason = "" if pd.isna(row.OriginalReason) else str(row.OriginalReason)
    path = str(row.ResearchPath)
    side = str(row.Side)
    quality = float(row.SetupQualityScore)
    planned = float(row.InitialRiskPoints)

    if "ZoneBirthShortV168" in reason:
        return 8.0 if planned <= 8.0 else 18.0
    if "AggressiveExpansionV164" in reason:
        return 25.0
    if "BreakawayLongSelectiveV134" in reason:
        return 18.0
    if "BreakawayVolumeV128" in reason or "BreakawayShortWideV128" in reason:
        return 25.0 if side == "Long" else 30.0
    if "UnknownMicroRiskVolumeV126" in reason:
        return 8.5
    if "ZoneBirthVolumeV122" in reason or "StrictObservationVolumeV122" in reason:
        return 25.0
    if path == "ObservationConfirm_WideStop1_5R":
        volume_quality = quality >= (70.0 if side == "Long" else 60.0)
        if volume_quality:
            return 25.0 if side == "Long" and planned > 22.0 else 22.0
        if quality >= 56.0:
            return 18.0
        if "DailyVolumeFloor" in reason:
            return 18.0
        return 0.0
    if path == "ObservationConfirm":
        if side == "Long":
            return 12.0
        if "ObservationRiskExpansion" in reason:
            return 20.0
        if quality >= 70.0:
            return 22.0
        if quality >= 60.0:
            return 18.0
        return 11.0
    if path.startswith("Breakaway"):
        return 11.0
    if "FailureReverse_RetestFailed" in path:
        return 11.0
    if "AlmostConfirmed" in path or "ShadowCandidate" in path:
        return 18.0
    if path.startswith("ObservationStrict_"):
        return 25.0
    return 0.0


def preflight_abort_reason(row) -> str:
    risk = float(row.ExactRisk)
    if STRICT_RISK_MIN_EXCLUSIVE < risk <= STRICT_RISK_MAX_INCLUSIVE:
        return "StrictRiskBand"
    maximum = max_allowed_risk(row)
    if maximum <= 0.0 or risk <= maximum:
        return ""
    drift_allowed = (
        not str(row.ResearchPath).startswith("Breakaway")
        and not (row.Side == "Long" and row.ResearchPath == "ObservationConfirm")
        and float(row.InitialRiskPoints) <= maximum
        and risk <= maximum + 1.0
    )
    return "" if drift_allowed else f"QuoteRisk>{maximum:g}"


def simulate(
    tape: pd.DataFrame,
    score_floor: float,
    same_direction: bool,
    daily_cap: int = 18,
    daily_loss: float = 450.0,
    combined_risk_cap: float = 300.0,
    opposite_score_floor: float | None = None,
    actual_abort_keys: frozenset[str] = frozenset(),
    block_trace: list[dict] | None = None,
):
    submissions = []
    accepted = []
    diagnostics = {
        "StaticRiskBlocked": 0,
        "ActiveBlocked": 0,
        "SecondaryGateBlocked": 0,
        "RiskCapBlocked": 0,
        "DailyCapBlocked": 0,
        "DailyLossBlocked": 0,
        "PreflightAborted": 0,
    }
    for snapshot_id, day in tape.groupby("SnapshotID", sort=False):
        active = []
        normal_count = 0
        realized_net = 0.0
        for row in day.sort_values(["EntryTime", "SourceSequence"]).itertuples(index=False):
            def blocked(reason: str):
                if block_trace is not None:
                    block_trace.append(
                        {
                            "SnapshotID": snapshot_id,
                            "TradingDate": row.TradingDate,
                            "EntryTime": row.EntryTime,
                            "SignalID": row.SignalID,
                            "ResearchPath": row.ResearchPath,
                            "Side": row.Side,
                            "Reason": reason,
                        }
                    )

            for trade in active:
                if not trade["Realized"] and trade["ExitTime"] < row.EntryTime:
                    realized_net += trade["Net"]
                    trade["Realized"] = True
            retained = []
            for trade in active:
                if trade["ExitTime"] < row.EntryTime:
                    if not trade["Realized"]:
                        realized_net += trade["Net"]
                    continue
                retained.append(trade)
            active = retained

            risk = float(row.ExactRisk)
            planned_risk = float(row.InitialRiskPoints)
            if daily_cap > 0 and normal_count >= daily_cap:
                diagnostics["DailyCapBlocked"] += 1
                blocked("DailyCap")
                continue
            if realized_net <= -daily_loss:
                diagnostics["DailyLossBlocked"] += 1
                blocked("DailyLoss")
                continue
            if len(active) >= 2:
                diagnostics["ActiveBlocked"] += 1
                blocked("Active")
                continue

            lane = "Primary" if not active else "Secondary"
            relation = "None"
            if lane == "Secondary":
                relation = "Same" if row.Side == active[0]["Side"] else "Opposite"
                if same_direction and relation == "Opposite":
                    diagnostics["SecondaryGateBlocked"] += 1
                    blocked("OppositeSecondary")
                    continue
                required_score = (
                    opposite_score_floor
                    if relation == "Opposite" and opposite_score_floor is not None
                    else score_floor
                )
                uses_actual_g2 = (
                    same_direction
                    and opposite_score_floor is None
                    and required_score == 2.0
                )
                if (
                    uses_actual_g2
                    and not actual_g2_eligible(row)
                    or not uses_actual_g2
                    and float(row.SecondaryScore) < required_score
                ):
                    diagnostics["SecondaryGateBlocked"] += 1
                    blocked("SecondaryG2")
                    continue

            risk_dollars = risk * 6.0
            combined_risk = planned_risk * 6.0 + sum(
                item["RiskDollars"] for item in active
            )
            if combined_risk > combined_risk_cap:
                diagnostics["RiskCapBlocked"] += 1
                blocked("CombinedRisk")
                continue

            submission_identity = f"{row.SignalID}|{row.ResearchPath}|{lane}"
            abort_reason = preflight_abort_reason(row)
            if submission_identity in actual_abort_keys:
                abort_reason = "ActualCurrentPolicyAbort"
            submission = {
                "SnapshotID": snapshot_id,
                "TradingDate": row.TradingDate,
                "SignalID": row.SignalID,
                "ResearchPath": row.ResearchPath,
                "Side": row.Side,
                "Lane": lane,
                "ConcurrentSideRelation": relation,
                "EntryTime": row.EntryTime,
                "ExactRisk": risk,
                "PlannedRisk": planned_risk,
                "MaxAllowedRisk": max_allowed_risk(row),
                "SubmissionStatus": "PreflightAborted" if abort_reason else "Accepted",
                "SubmissionReason": abort_reason,
            }
            submissions.append(submission)
            if abort_reason:
                diagnostics["PreflightAborted"] += 1
                continue

            trade = {
                **submission,
                "ExitTime": row.PredictedExitTime,
                "ExitRole": row.PredictedExitRole,
                "Gross": float(row.PredictedGross),
                "Net": float(row.PredictedGross) - COMMISSION,
                "RiskDollars": risk_dollars,
                "OutcomeSource": row.OutcomeSource,
                "OutcomeClassification": row.OutcomeClassification,
                "Realized": False,
            }
            accepted.append(trade)
            active.append(trade)
            if row.OutcomeClassification in {"Normal", "Predicted"}:
                normal_count += 1
    return pd.DataFrame(submissions), pd.DataFrame(accepted), diagnostics


def actual_tables(connector, evidence: Path):
    trades = connector.read_csvs(evidence, "execution_trades")
    pnl = connector.read_csvs(evidence, "live_account_pnl")
    events = connector.read_csvs(evidence, "execution_events")
    for frame, columns in (
        (trades, ("EntryTime", "ExitTime")),
        (pnl, ("EntryTime", "ExitTime")),
        (events, ("Time",)),
    ):
        for column in columns:
            frame[column] = pd.to_datetime(frame[column])
    sends = events[events["Event"].eq("ENTRY_SEND")].copy()
    sends["Lane"] = sends["TradeID"].str.endswith("-S2").map(
        {True: "Secondary", False: "Primary"}
    )
    return sends, trades, pnl, events


def identity_key(frame: pd.DataFrame) -> pd.Series:
    return frame["SignalID"] + "|" + frame["ResearchPath"] + "|" + frame["Lane"]


def actual_abort_identity_keys(events: pd.DataFrame) -> frozenset[str]:
    aborted = events[events["Event"].eq("ENTRY_SUBMISSION_ABORTED_V178")].copy()
    aborted["Lane"] = aborted["TradeID"].str.endswith("-S2").map(
        {True: "Secondary", False: "Primary"}
    )
    return frozenset(identity_key(aborted))


def apply_current_policy_actual_anchor(
    tape: pd.DataFrame,
    actual_sends: pd.DataFrame,
    actual_trades: pd.DataFrame,
    pnl: pd.DataFrame,
) -> tuple[pd.DataFrame, int]:
    actual = actual_trades.copy()
    actual["OutcomeClassification"] = "Normal"
    send_identity = actual_sends[
        ["TradeID", "SignalID", "ResearchPath"]
    ].drop_duplicates("TradeID")
    quarantine = pnl[pnl["Classification"].ne("Normal")].merge(
        send_identity,
        on="TradeID",
        how="inner",
        validate="one_to_one",
    )
    quarantine = quarantine.rename(columns={"GrossPnLDollars": "Dollars"})
    quarantine["ExitRole"] = "QUARANTINE_FLATTEN"
    quarantine["OutcomeClassification"] = quarantine["Classification"]
    actual = pd.concat(
        [
            actual,
            quarantine[
                [
                    "SignalID",
                    "ResearchPath",
                    "TradeID",
                    "ExitTime",
                    "ExitRole",
                    "Dollars",
                    "OutcomeClassification",
                ]
            ],
        ],
        ignore_index=True,
    )
    actual["Lane"] = actual["TradeID"].str.endswith("-S2").map(
        {True: "Secondary", False: "Primary"}
    )
    actual["IdentityKey"] = identity_key(actual)
    if actual["IdentityKey"].duplicated().any():
        raise RuntimeError("Actual current-policy identity is not unique")

    anchors = actual.set_index("IdentityKey")[
        ["ExitTime", "ExitRole", "Dollars", "OutcomeClassification"]
    ]
    result = tape.copy()
    result["IdentityKey"] = result["SignalID"] + "|" + result["ResearchPath"] + "|Primary"
    primary_match = result["IdentityKey"].isin(anchors.index)

    secondary_keys = result["SignalID"] + "|" + result["ResearchPath"] + "|Secondary"
    secondary_match = secondary_keys.isin(anchors.index)
    result.loc[secondary_match, "IdentityKey"] = secondary_keys[secondary_match]
    matched = primary_match | secondary_match

    result["OutcomeSource"] = "CompressedPathPrediction"
    result["OutcomeClassification"] = "Predicted"
    for index in result.index[matched]:
        anchor = anchors.loc[result.at[index, "IdentityKey"]]
        result.at[index, "PredictedExitTime"] = anchor["ExitTime"]
        result.at[index, "PredictedExitRole"] = anchor["ExitRole"]
        result.at[index, "PredictedGross"] = float(anchor["Dollars"])
        result.at[index, "PredictedNet"] = float(anchor["Dollars"]) - COMMISSION
        result.at[index, "OutcomeSource"] = "ActualCurrentPolicyAnchor"
        result.at[index, "OutcomeClassification"] = anchor["OutcomeClassification"]
    return result.drop(columns="IdentityKey"), int(matched.sum())


def apply_actual_zonebirth_split(connector, evidence: Path, tape: pd.DataFrame) -> pd.DataFrame:
    mask = tape["Side"].eq("Short") & tape["ResearchPath"].eq("ZoneBirthResearch")
    if not mask.any():
        return tape

    calibration, bars, turns, _, trades, _, _ = connector.load_evidence(evidence)
    keys = tape.loc[mask, connector.KEY].drop_duplicates()
    zonebirth = calibration.merge(keys, on=connector.KEY, how="inner", validate="one_to_one")
    outcomes = connector.predict_outcomes(
        zonebirth,
        bars,
        turns,
        trades,
        {tuple(row): "Current" for row in keys.itertuples(index=False, name=None)},
    ).set_index(connector.KEY)
    if len(outcomes) != len(keys):
        raise RuntimeError(
            f"ZoneBirth split outcome mismatch: {len(outcomes)} != {len(keys)}"
        )

    result = tape.copy()
    result.loc[mask, "ExitOption"] = "ActualZoneBirthSplit"
    outcome_columns = [
        "PredictedExitTime",
        "PredictedExitRole",
        "PredictedGross",
        "PredictedNet",
        "PredictedExitRelativeBar",
        "PathEndFallback",
        "FillAnchorUsed",
    ]
    for index in result.index[mask]:
        key = tuple(result.loc[index, connector.KEY])
        for column in outcome_columns:
            result.at[index, column] = outcomes.at[key, column]
    return result


def conservative_net(accepted: pd.DataFrame, callback_gap_reserve: float, quarantine_gross: float) -> float:
    # accepted.Net already includes commission for every accepted submission,
    # including trades later classified as Quarantine. Replace only their
    # predicted gross; charging their commission again would double count it.
    return float(accepted["Net"].sum()) - callback_gap_reserve - quarantine_gross


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--dates", required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--candidate-tape", type=Path)
    args = parser.parse_args()

    dates = pd.to_datetime([value.strip() for value in args.dates.split(",") if value.strip()])
    root = Path(__file__).resolve().parents[1]
    exact = load_module(
        "opf_v217_exact", Path(__file__).parent / "Analyze-OPFV214H1ExactCalibration.py"
    )
    connector = load_module(
        "opf_v217_connector", Path(__file__).parent / "Analyze-OPFV211CalibrationConnector.py"
    )
    if args.candidate_tape:
        tape = pd.read_csv(args.candidate_tape, low_memory=False)
        for column in ("EntryTime", "PredictedExitTime", "TradingDate"):
            tape[column] = pd.to_datetime(tape[column])
        tape = tape[tape["TradingDate"].isin(dates)].copy()
    else:
        tape = exact.exact_candidate_tape(
            connector, args.evidence, root, list(dates), static_exit=True
        )
        tape = apply_actual_zonebirth_split(connector, args.evidence, tape)
    score_map, global_score = exact.static_secondary_scores(root)
    secondary_band = (
        tape["SecondaryRiskBand"]
        if "SecondaryRiskBand" in tape.columns
        else tape["RiskBand"]
    )
    tape["SecondaryScore"] = [
        score_map.get((side, path, band), global_score)
        for side, path, band in zip(tape["Side"], tape["ResearchPath"], secondary_band)
    ]
    actual_sends, actual_trades, pnl, events = actual_tables(connector, args.evidence)
    tape, actual_anchor_count = apply_current_policy_actual_anchor(
        tape, actual_sends, actual_trades, pnl
    )
    actual_abort_keys = actual_abort_identity_keys(events)
    block_trace = []
    submissions, accepted, diagnostics = simulate(
        tape,
        2.0,
        True,
        daily_cap=FROZEN_DAILY_CAP,
        daily_loss=FROZEN_DAILY_LOSS,
        combined_risk_cap=FROZEN_COMBINED_RISK_CAP,
        actual_abort_keys=actual_abort_keys,
        block_trace=block_trace,
    )

    submissions["IdentityKey"] = identity_key(submissions)
    actual_sends["IdentityKey"] = identity_key(actual_sends)
    predicted_keys = set(submissions["IdentityKey"])
    actual_keys = set(actual_sends["IdentityKey"])

    normal = actual_trades.copy()
    normal["Lane"] = normal["TradeID"].str.endswith("-S2").map(
        {True: "Secondary", False: "Primary"}
    )
    normal["IdentityKey"] = identity_key(normal)
    accepted["IdentityKey"] = identity_key(accepted)
    paired = accepted.merge(
        normal[["IdentityKey", "ExitRole", "Dollars", "TradeID"]],
        on="IdentityKey",
        how="inner",
        suffixes=("Predicted", "Actual"),
        validate="one_to_one",
    )
    paired["GrossDelta"] = paired["Gross"] - paired["Dollars"]
    paired["RoleMatch"] = paired["ExitRolePredicted"].eq(paired["ExitRoleActual"])

    quarantine = pnl[pnl["Classification"].ne("Normal")]
    wait_count = int(events["Event"].eq("SECONDARY_WAITING_FOR_PROTECTION_V216").sum())
    ready_count = int(events["Event"].eq("SECONDARY_PROTECTION_READY_RETRY_V216").sum())
    timeout_count = int(events["Event"].eq("SECONDARY_PROTECTION_RETRY_REJECTED_V216").sum())
    actual_gross = float(pnl["GrossPnLDollars"].sum())
    actual_net = float(pnl["NetPnLDollars"].sum())
    predicted_normal_gross = float(paired["Gross"].sum())
    callback_gap_reserve = max(0.0, predicted_normal_gross - float(paired["Dollars"].sum()))
    quarantine_gross = float(
        accepted[
            ~accepted["IdentityKey"].isin(set(normal["IdentityKey"]))
            & accepted["OutcomeSource"].eq("CompressedPathPrediction")
        ]["Gross"].sum()
    )
    predicted_conservative_net = conservative_net(
        accepted, callback_gap_reserve, quarantine_gross
    )

    summary = {
        "ResearchVersion": "|".join(sorted(tape["ResearchSchemaVersion"].unique())),
        "Dates": ",".join(dates.strftime("%Y-%m-%d")),
        "CandidateRows": len(tape),
        "ActualOutcomeAnchors": actual_anchor_count,
        "ActualAbortAnchors": len(actual_abort_keys),
        "DailyCap": FROZEN_DAILY_CAP,
        "DailyLoss": FROZEN_DAILY_LOSS,
        "CombinedRiskCap": FROZEN_COMBINED_RISK_CAP,
        "PredictedEntrySend": len(submissions),
        "ActualEntrySend": len(actual_sends),
        "PredictedSecondarySend": int(submissions["Lane"].eq("Secondary").sum()),
        "ActualSecondarySend": int(actual_sends["Lane"].eq("Secondary").sum()),
        "ExactEntrySendIdentity": len(predicted_keys & actual_keys),
        "MissingEntrySendIdentity": len(actual_keys - predicted_keys),
        "ExtraEntrySendIdentity": len(predicted_keys - actual_keys),
        "PredictedPreflightAborted": diagnostics["PreflightAborted"],
        "ActualPreflightAborted": int(events["Event"].eq("ENTRY_SUBMISSION_ABORTED_V178").sum()),
        "ActualWait": wait_count,
        "ActualReady": ready_count,
        "ActualTimeout": timeout_count,
        "PairedNormalTrades": len(paired),
        "ActualNormalTrades": len(normal),
        "ExitRoleMatchPct": round(float(paired["RoleMatch"].mean() * 100.0), 2),
        "PredictedNormalGross": round(predicted_normal_gross, 2),
        "ActualNormalGross": round(float(paired["Dollars"].sum()), 2),
        "CallbackGapReserve": round(callback_gap_reserve, 2),
        "QuarantineCount": len(quarantine),
        "QuarantinePredictedGross": round(quarantine_gross, 2),
        "QuarantineCommission": round(float(quarantine["CommissionDollars"].sum()), 2),
        "PredictedConservativeNet": round(predicted_conservative_net, 2),
        "ActualAccountGross": round(actual_gross, 2),
        "ActualAccountNet": round(actual_net, 2),
    }
    summary["IdentityGatePassed"] = bool(
        summary["PredictedEntrySend"] == summary["ActualEntrySend"]
        and summary["PredictedSecondarySend"] == summary["ActualSecondarySend"]
        and summary["ExactEntrySendIdentity"] == summary["ActualEntrySend"]
        and summary["MissingEntrySendIdentity"] == 0
        and summary["ExtraEntrySendIdentity"] == 0
        and summary["PredictedPreflightAborted"] == summary["ActualPreflightAborted"]
    )
    summary["EconomicGatePassed"] = bool(
        summary["PairedNormalTrades"] == summary["ActualNormalTrades"]
        and summary["ExitRoleMatchPct"] >= 95.0
        and abs(summary["PredictedConservativeNet"] - summary["ActualAccountNet"]) <= 1.0
    )

    args.output_dir.mkdir(parents=True, exist_ok=True)
    tape.to_csv(args.output_dir / "candidate_tape.csv", index=False)
    submissions.to_csv(args.output_dir / "submission_trace.csv", index=False)
    accepted.to_csv(args.output_dir / "accepted_trades.csv", index=False)
    paired.to_csv(args.output_dir / "normal_trade_pairing.csv", index=False)
    pd.DataFrame(block_trace).to_csv(args.output_dir / "blocked_trace.csv", index=False)
    pd.DataFrame([summary]).to_csv(args.output_dir / "validation_summary.csv", index=False)
    print(pd.DataFrame([summary]).to_string(index=False))


if __name__ == "__main__":
    main()
