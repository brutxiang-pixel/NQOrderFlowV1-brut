import importlib.util
import unittest
from pathlib import Path

import pandas as pd


PATH = Path(__file__).parents[1] / "Analyze-OPFV400ActualCalibration.py"
SPEC = importlib.util.spec_from_file_location("v400_actual_calibration", PATH)
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class V400ActualCalibrationTests(unittest.TestCase):
    def actual(self, signal_id="signal", path="Path", trade_id="trade"):
        return {
            "SnapshotID": "actual-snapshot", "SignalID": signal_id, "ResearchPath": path,
            "TradeID": trade_id, "Side": "Long", "EntryPrice": 101.0, "ExitPrice": 104.0,
            "StopPrice": 99.0, "FilledRiskPoints": 2.0, "ActualMFEPoints": 4.0,
            "ActualMAEPoints": 1.0, "ExitRole": "TP", "RawDollars": 18.0,
            "InitialRiskPoints": 2.0, "PlannedRiskPoints": 2.0, "PlannedTargetR": 1.5,
            "TargetR": 1.5, "IsAbnormalExecution": "False",
        }

    def account(self, trade_id="trade"):
        return {
            "SnapshotID": "actual-snapshot", "TradeID": trade_id, "Classification": "Normal",
            "GrossPnLDollars": 18.0, "CommissionDollars": 3.6, "NetPnLDollars": 14.4,
        }

    def candidate(self, signal_id="signal", path="Path"):
        return {
            "CandidateID": "candidate", "SignalID": signal_id, "ResearchPath": path,
            "Side": "Long", "PlannedEntry": 100.0, "PlannedStop": 98.0,
            "PlannedTarget": 103.0, "PlannedRiskPoints": 2.0, "PlannedTargetR": 1.5,
        }

    def test_normal_actual_joins_one_candidate_without_overwriting_actual_fields(self):
        detail, summary = MODULE.calibrate(
            pd.DataFrame([self.actual()]),
            pd.DataFrame([self.account()]),
            pd.DataFrame([self.candidate()]),
        )
        self.assertEqual(1, len(detail))
        self.assertEqual("candidate", detail.iloc[0]["CandidateID"])
        self.assertEqual(101.0, detail.iloc[0]["ActualEntryPrice"])
        self.assertEqual(100.0, detail.iloc[0]["CandidatePlannedEntry"])
        self.assertEqual(1.0, detail.iloc[0]["EntryDeltaPoints"])
        self.assertEqual(1, summary["ExactCandidateJoins"])

    def test_candidate_plan_actual_plan_and_filled_risk_are_separate_geometries(self):
        actual = self.actual()
        actual["FilledRiskPoints"] = 2.5
        detail, summary = MODULE.calibrate(
            pd.DataFrame([actual]), pd.DataFrame([self.account()]), pd.DataFrame([self.candidate()])
        )
        row = detail.iloc[0]
        self.assertEqual(2.0, row["CandidatePlannedRiskPoints"])
        self.assertEqual(2.0, row["ActualPlannedRiskPoints"])
        self.assertEqual(2.5, row["FilledRiskPoints"])
        self.assertEqual(1, summary["CandidatePlanActualPlanRiskMatches"])
        self.assertEqual(0, summary["ActualPlanFilledRiskMatches"])

    def test_duplicate_candidate_key_is_rejected(self):
        candidates = pd.DataFrame([self.candidate(), self.candidate()])
        with self.assertRaisesRegex(ValueError, "Duplicate Candidate"):
            MODULE.calibrate(
                pd.DataFrame([self.actual()]), pd.DataFrame([self.account()]), candidates
            )

    def test_abnormal_actual_is_excluded_from_normal_population(self):
        actual = self.actual()
        actual["IsAbnormalExecution"] = "True"
        detail, summary = MODULE.calibrate(
            pd.DataFrame([actual]), pd.DataFrame([self.account()]), pd.DataFrame([self.candidate()])
        )
        self.assertTrue(detail.empty)
        self.assertEqual(0, summary["NormalActualTrades"])

    def test_identical_replay_duplicate_is_excluded_before_candidate_join(self):
        duplicate = self.actual()
        duplicate["SnapshotID"] = "replay-duplicate"
        account_duplicate = self.account()
        account_duplicate["SnapshotID"] = "replay-duplicate"
        detail, summary = MODULE.calibrate(
            pd.DataFrame([self.actual(), duplicate]),
            pd.DataFrame([self.account(), account_duplicate]),
            pd.DataFrame([self.candidate()]),
        )
        self.assertEqual(1, len(detail))
        self.assertEqual(1, summary["DuplicateReplayRowsExcluded"])


if __name__ == "__main__":
    unittest.main()
