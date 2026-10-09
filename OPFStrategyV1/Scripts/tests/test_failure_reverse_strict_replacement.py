import json
from pathlib import Path
import unittest


SOURCE = Path(__file__).parents[2] / "Strategy" / "OpeningPullbackFailureStrategy.cs"
PROFILE = Path(__file__).parents[2] / "Configs" / "OPFStrategyV1_v500_fr_preentry_wideshort_actual_calibration.json"


class FailureReverseStrictReplacementTests(unittest.TestCase):
    def test_only_legacy_admissible_wide_short_is_armed(self):
        source = SOURCE.read_text(encoding="utf-8")
        self.assertIn("TryEvaluateLegacyFailureReverseWideStopAdmission", source)
        self.assertIn("FR_PREENTRY_LEGACY_REJECTED", source)
        self.assertIn("LegacyWouldSubmit", source)

    def test_confirmation_carries_a_mapping_token(self):
        source = SOURCE.read_text(encoding="utf-8")
        self.assertIn("EvaluateFailureReversePreEntryWideShorts", source)
        self.assertIn("FR_PREENTRY_CONFIRM", source)
        self.assertIn("LegacySignalId=", source)
        self.assertIn("LegacyPath=", source)

    def test_new_path_is_execution_eligible_with_the_existing_short_exit_policy(self):
        source = SOURCE.read_text(encoding="utf-8")
        self.assertIn('"FailureReverse_PreEntryConfirmedWideStopShort"', source)
        self.assertIn("(targetR, timeStopBars) = (4m, ShortPolicyTimeStopBarsV209);", source)

    def test_replay_profile_is_compatible_and_replaces_legacy_wide_stop_path(self):
        profile = json.loads(PROFILE.read_text(encoding="utf-8"))
        self.assertEqual("ACTUAL_EXEC_2.51", profile["Version"])
        paths = profile["ActualExecutionPaths"].split("|")
        self.assertIn("FailureReverse_PreEntryConfirmedWideStopShort", paths)
        self.assertNotIn("FailureReverse_ObservationInvalidated_WideStop1_5R", paths)


if __name__ == "__main__":
    unittest.main()
