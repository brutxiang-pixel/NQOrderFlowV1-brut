import importlib.util
from pathlib import Path

import pandas as pd


def load_module(path: Path):
    spec = importlib.util.spec_from_file_location("opf_v217_dual_test", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def row(
    signal,
    time,
    exit_time,
    risk,
    side="Long",
    path="ObservationConfirm",
    planned_risk=None,
):
    return {
        "SnapshotID": "S",
        "TradingDate": pd.Timestamp("2026-01-02"),
        "SignalID": signal,
        "ResearchPath": path,
        "Side": side,
        "EntryTime": pd.Timestamp(time),
        "SourceSequence": len(signal),
        "ExactRisk": risk,
        "InitialRiskPoints": min(risk, 11.0) if planned_risk is None else planned_risk,
        "SetupQualityScore": 80.0,
        "SecondaryScore": 5.0,
        "OriginalReason": "LegacyV174PolicyV204",
        "PredictedExitTime": pd.Timestamp(exit_time),
        "PredictedExitRole": "TP",
        "PredictedGross": 60.0,
        "OutcomeSource": "CompressedPathPrediction",
        "OutcomeClassification": "Predicted",
    }


def main():
    module = load_module(
        Path(__file__).with_name("Analyze-OPFV217DualSlotCalibration.py")
    )

    preflight = pd.DataFrame(
        [
            row("A", "2026-01-02 01:00", "2026-01-02 02:00", 12.75),
            row("BB", "2026-01-02 01:05", "2026-01-02 02:00", 10.0),
        ]
    )
    submissions, accepted, _ = module.simulate(preflight, 0.0, True)
    assert list(submissions["Lane"]) == ["Primary", "Primary"]
    assert list(submissions["SubmissionStatus"]) == ["PreflightAborted", "Accepted"]
    assert len(accepted) == 1

    strict_band = pd.DataFrame(
        [row("STRICT", "2026-01-02 01:00", "2026-01-02 02:00", 14.0)]
    )
    submissions, accepted, diagnostics = module.simulate(
        strict_band, 0.0, True
    )
    assert len(submissions) == 1
    assert submissions.iloc[0]["SubmissionStatus"] == "PreflightAborted"
    assert submissions.iloc[0]["SubmissionReason"] == "StrictRiskBand"
    assert diagnostics["StaticRiskBlocked"] == 0
    assert accepted.empty

    planned_risk = pd.DataFrame(
        [
            row(
                "PRIMARY",
                "2026-01-02 01:00",
                "2026-01-02 02:00",
                20.0,
                side="Long",
                path="ObservationConfirm_WideStop1_5R",
                planned_risk=20.0,
            ),
            row(
                "SECONDARY",
                "2026-01-02 01:05",
                "2026-01-02 02:00",
                31.0,
                side="Long",
                path="ObservationConfirm_WideStop1_5R",
                planned_risk=20.0,
            ),
        ]
    )
    submissions, accepted, diagnostics = module.simulate(
        planned_risk,
        2.0,
        True,
        combined_risk_cap=300.0,
    )
    assert list(submissions["Lane"]) == ["Primary", "Secondary"]
    assert diagnostics["RiskCapBlocked"] == 0
    assert submissions.iloc[1]["SubmissionStatus"] == "PreflightAborted"

    compact = pd.DataFrame(
        [
            row("A", "2026-01-02 01:00", "2026-01-02 01:10", 10.0),
            row("BB", "2026-01-02 01:05", "2026-01-02 02:00", 10.0),
            row("CCC", "2026-01-02 01:15", "2026-01-02 02:10", 10.0),
        ]
    )
    submissions, _, _ = module.simulate(compact, 0.0, True)
    assert list(submissions["Lane"]) == ["Primary", "Secondary", "Secondary"]

    accepted = pd.DataFrame({"Net": [96.4, -60.6]})
    actual = module.conservative_net(
        accepted,
        callback_gap_reserve=10.0,
        quarantine_gross=-57.0,
    )
    assert abs(actual - 82.8) < 1e-9

    class FakeConnector:
        KEY = ["SnapshotID", "SignalID", "ResearchPath"]

        @staticmethod
        def load_evidence(_):
            calibration = pd.DataFrame(
                [{"SnapshotID": "S", "SignalID": "Z", "ResearchPath": "ZoneBirthResearch"}]
            )
            return calibration, pd.DataFrame(), pd.DataFrame(), pd.DataFrame(), pd.DataFrame(), pd.DataFrame(), pd.DataFrame()

        @staticmethod
        def predict_outcomes(calibration, bars, turns, trades, exit_options):
            assert next(iter(exit_options.values())) == "Current"
            result = calibration.copy()
            result["PredictedExitTime"] = pd.Timestamp("2026-01-02 01:10")
            result["PredictedExitRole"] = "SPLIT_BASE_SL_RUNNER_SL"
            result["PredictedGross"] = -42.0
            result["PredictedNet"] = -45.6
            result["PredictedExitRelativeBar"] = 2
            result["PathEndFallback"] = False
            result["FillAnchorUsed"] = True
            return result

    tape = pd.DataFrame(
        [
            {
                "SnapshotID": "S",
                "SignalID": "Z",
                "ResearchPath": "ZoneBirthResearch",
                "Side": "Short",
                "ExitOption": "F1.5_T12",
                "PredictedExitTime": pd.Timestamp("2026-01-02 01:05"),
                "PredictedExitRole": "SL",
                "PredictedGross": -42.0,
                "PredictedNet": -45.6,
                "PredictedExitRelativeBar": 1,
                "PathEndFallback": False,
                "FillAnchorUsed": False,
            }
        ]
    )
    rebased = module.apply_actual_zonebirth_split(FakeConnector(), Path("unused"), tape)
    assert rebased.iloc[0]["ExitOption"] == "ActualZoneBirthSplit"
    assert rebased.iloc[0]["PredictedExitRole"] == "SPLIT_BASE_SL_RUNNER_SL"
    assert rebased.iloc[0]["PredictedExitRelativeBar"] == 2

    anchor_tape = pd.DataFrame(
        [
            row("ANCHOR", "2026-01-02 01:00", "2026-01-02 02:00", 10.0),
            row("PREDICT", "2026-01-02 01:05", "2026-01-02 02:05", 10.0),
        ]
    )
    actual_trades = pd.DataFrame(
        [
            {
                "SignalID": "ANCHOR",
                "ResearchPath": "ObservationConfirm",
                "TradeID": "T1",
                "ExitTime": pd.Timestamp("2026-01-02 01:30"),
                "ExitRole": "SL",
                "Dollars": -40.0,
            }
        ]
    )
    actual_sends = actual_trades[["SignalID", "ResearchPath", "TradeID"]].copy()
    pnl = pd.DataFrame(
        columns=["TradeID", "ExitTime", "Classification", "GrossPnLDollars"]
    )
    anchored, count = module.apply_current_policy_actual_anchor(
        anchor_tape, actual_sends, actual_trades, pnl
    )
    assert count == 1
    assert anchored.iloc[0]["PredictedExitTime"] == pd.Timestamp("2026-01-02 01:30")
    assert anchored.iloc[0]["PredictedExitRole"] == "SL"
    assert anchored.iloc[0]["PredictedGross"] == -40.0
    assert anchored.iloc[0]["OutcomeSource"] == "ActualCurrentPolicyAnchor"
    assert anchored.iloc[1]["OutcomeSource"] == "CompressedPathPrediction"

    loss_gate = pd.DataFrame(
        [
            row("LOSS", "2026-01-02 01:00", "2026-01-02 01:05", 10.0),
            row("BLOCKED", "2026-01-02 01:10", "2026-01-02 01:15", 10.0),
        ]
    )
    loss_gate.loc[0, "PredictedGross"] = -250.0
    submissions, _, diagnostics = module.simulate(
        loss_gate,
        2.0,
        True,
        daily_cap=module.FROZEN_DAILY_CAP,
        daily_loss=module.FROZEN_DAILY_LOSS,
        combined_risk_cap=module.FROZEN_COMBINED_RISK_CAP,
    )
    assert len(submissions) == 1
    assert diagnostics["DailyLossBlocked"] == 1

    wide_breakaway = row(
        "WIDE_BREAKAWAY",
        "2026-01-02 01:00",
        "2026-01-02 01:05",
        29.5,
        side="Short",
        path="BreakawayFvg",
        planned_risk=29.5,
    )
    wide_breakaway["OriginalReason"] = "BreakawayShortWideV128:risk=29.5"
    assert module.max_allowed_risk(pd.Series(wide_breakaway)) == 30.0

    forced_abort = pd.DataFrame(
        [row("ACTUAL_ABORT", "2026-01-02 01:00", "2026-01-02 01:05", 10.0)]
    )
    submissions, accepted, diagnostics = module.simulate(
        forced_abort,
        2.0,
        True,
        actual_abort_keys=frozenset(
            {"ACTUAL_ABORT|ObservationConfirm|Primary"}
        ),
    )
    assert submissions.iloc[0]["SubmissionReason"] == "ActualCurrentPolicyAbort"
    assert accepted.empty
    assert diagnostics["PreflightAborted"] == 1

    print("OPFV217 dual-slot calibration tests passed: 10")


if __name__ == "__main__":
    main()
