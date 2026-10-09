import importlib.util
import unittest
from pathlib import Path

import pandas as pd


PATH = Path(__file__).parents[1] / "Analyze-OPFV400StaticActualCalibration.py"
SPEC = importlib.util.spec_from_file_location("v400_static_actual_calibration", PATH)
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class V400StaticActualCalibrationTests(unittest.TestCase):
    def detail(self, candidate_id="candidate", exit_role="TP", net=10.0):
        return {
            "CandidateID": candidate_id, "Side": "Long", "ResearchPath": "Path",
            "ExitRole": exit_role, "ActualNetPnLDollars": net,
            "EntryDeltaPoints": 0.25, "CandidatePlanActualPlanRiskDeltaPoints": 0.0,
            "ActualPlanFilledRiskDeltaPoints": 0.25,
        }

    def label(self, candidate_id="candidate", terminal="TP"):
        return {"CandidateID": candidate_id, "TerminalLabel": terminal}

    def test_exact_candidate_join_classifies_exit_family_and_diagnostic_sign(self):
        detail, summary = MODULE.calibrate(
            pd.DataFrame([self.detail()]), pd.DataFrame([self.label()])
        )
        self.assertEqual("TP", detail.iloc[0]["ActualExitFamily"])
        self.assertEqual("Positive", detail.iloc[0]["ActualNetSign"])
        self.assertTrue(detail.iloc[0]["StaticSignEligible"])
        self.assertEqual(1, summary["ExactStaticLabelJoins"])

    def test_ambiguous_and_censored_are_excluded_from_sign_metric(self):
        details = pd.DataFrame([self.detail("amb"), self.detail("censor")])
        labels = pd.DataFrame([self.label("amb", "Ambiguous"), self.label("censor", "Censored")])
        detail, summary = MODULE.calibrate(details, labels)
        self.assertFalse(detail["StaticSignEligible"].any())
        self.assertEqual(0, summary["StaticSignEligibleRows"])

    def test_split_exit_family_is_not_misclassified_as_plain_stop_or_target(self):
        self.assertEqual("Split", MODULE.exit_family("SPLIT_BASE_TP_RUNNER_SL"))


if __name__ == "__main__":
    unittest.main()
