import importlib.util
import unittest
from pathlib import Path

import pandas as pd


def load_module(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class OfflineCalibrationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        scripts = Path(__file__).parent
        cls.exit_screen = load_module(
            "opf_exit_screen_test",
            scripts / "Analyze-OPFDecisionTapeH1ExitScreen.py",
        )
        cls.engine = load_module(
            "opf_decision_tape_test",
            scripts / "Analyze-OPFDecisionTapePortfolio.py",
        )
        cls.exact = load_module(
            "opf_v214_exact_test",
            scripts / "Analyze-OPFV214H1ExactCalibration.py",
        )

    def test_rich_coverage_gap_fails_closed_to_stop(self):
        bars = pd.DataFrame(
            [
                {
                    "Time": pd.Timestamp("2026-01-05 23:30:00"),
                    "High": 25588.0,
                    "Low": 25560.0,
                    "Close": 25562.0,
                }
            ]
        )
        gross, exit_time, reason = self.exit_screen.fixed_exit(
            bars,
            pd.Timestamp("2026-01-05 23:20:00"),
            25584.0,
            25589.0,
            "Short",
            4.0,
            0.0,
            36,
        )
        self.assertEqual(-30.0, gross)
        self.assertEqual(pd.Timestamp("2026-01-05 23:20:00"), exit_time)
        self.assertEqual("CoverageGapStop", reason)

    def test_same_bar_collision_is_stop_first(self):
        bars = pd.DataFrame(
            [
                {
                    "Time": pd.Timestamp("2026-01-05 23:20:00"),
                    "High": 110.0,
                    "Low": 90.0,
                    "Close": 100.0,
                }
            ]
        )
        gross, _, reason = self.exit_screen.fixed_exit(
            bars,
            pd.Timestamp("2026-01-05 23:20:00"),
            100.0,
            95.0,
            "Long",
            1.5,
            0.0,
            12,
        )
        self.assertEqual(-30.0, gross)
        self.assertEqual("StopCollision", reason)

    def test_active_release_delay_is_independent_from_exit_time(self):
        exit_time = pd.Timestamp("2026-01-27 23:20:00")
        active_until = self.engine.policy_active_until(
            exit_time, {"ActiveReleaseDelayMinutes": 10}
        )
        self.assertEqual(pd.Timestamp("2026-01-27 23:30:00"), active_until)

    def test_same_timestamp_first_eligible_candidate_becomes_secondary(self):
        tape = pd.DataFrame(
            [
                {
                    "SnapshotID": "S1",
                    "TradingDate": pd.Timestamp("2026-01-02"),
                    "Month": "2026-01",
                    "SignalID": "P1",
                    "ResearchPath": "ObservationConfirm",
                    "Side": "Long",
                    "EntryTime": pd.Timestamp("2026-01-02 01:00:00"),
                    "SourceSequence": 1,
                    "PredictedExitTime": pd.Timestamp("2026-01-02 01:30:00"),
                    "PredictedExitRole": "TP",
                    "ExitOption": "Current",
                    "RiskBand": "R10",
                    "ExactRisk": 10.0,
                    "SecondaryScore": 3.0,
                    "PredictedGross": 90.0,
                },
                {
                    "SnapshotID": "S1",
                    "TradingDate": pd.Timestamp("2026-01-02"),
                    "Month": "2026-01",
                    "SignalID": "S2",
                    "ResearchPath": "ObservationConfirm_WideStop1_5R",
                    "Side": "Long",
                    "EntryTime": pd.Timestamp("2026-01-02 01:00:00"),
                    "SourceSequence": 2,
                    "PredictedExitTime": pd.Timestamp("2026-01-02 01:20:00"),
                    "PredictedExitRole": "TIME_STOP",
                    "ExitOption": "Current",
                    "RiskBand": "R10",
                    "ExactRisk": 9.0,
                    "SecondaryScore": 3.0,
                    "PredictedGross": 45.0,
                },
            ]
        )

        trades, diagnostics = self.exact.simulate(
            tape,
            delay_minutes=5,
            concurrent=True,
            secondary_score_floor=2.0,
            same_direction_secondary=True,
        )

        self.assertEqual(["Primary", "Secondary"], trades["Lane"].tolist())
        self.assertEqual(1, diagnostics["SecondaryAccepted"])

    def test_quote_repriced_strict_risk_band_is_blocked_before_entry(self):
        tape = pd.DataFrame(
            [
                {
                    "SnapshotID": "S1",
                    "TradingDate": pd.Timestamp("2026-01-02"),
                    "Month": "2026-01",
                    "SignalID": "R1",
                    "ResearchPath": "ObservationConfirm",
                    "Side": "Short",
                    "EntryTime": pd.Timestamp("2026-01-02 01:00:00"),
                    "SourceSequence": 1,
                    "PredictedExitTime": pd.Timestamp("2026-01-02 01:20:00"),
                    "PredictedExitRole": "SL",
                    "ExitOption": "Current",
                    "RiskBand": "R20",
                    "ExactRisk": 13.5,
                    "SecondaryScore": 3.0,
                    "PredictedGross": -81.0,
                }
            ]
        )

        trades, diagnostics = self.exact.simulate(
            tape,
            delay_minutes=5,
            concurrent=True,
            secondary_score_floor=2.0,
            same_direction_secondary=True,
        )

        self.assertTrue(trades.empty)
        self.assertEqual(1, diagnostics["StaticRiskBandBlocked"])


if __name__ == "__main__":
    unittest.main()
