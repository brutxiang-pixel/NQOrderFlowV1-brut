import importlib.util
import json
from pathlib import Path
import tempfile
import unittest


SCRIPT = Path(__file__).parents[1] / "Validate-OPFV500FailureReverseStrictReplacement.py"
SPEC = importlib.util.spec_from_file_location("strict_replacement_validator", SCRIPT)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(MODULE)


class StrictReplacementValidatorTests(unittest.TestCase):
    def write_snapshot(self, root: Path, paths: str, profile: str, status: str = "loaded") -> None:
        (root / "OPF-test_ConfigSnapshot.json").write_text(json.dumps({
            "ActualExecutionConfigStatus": status,
            "ActualExecutionSettings": {
                "Version": "ACTUAL_EXEC_2.51",
                "RunProfileId": profile,
                "ActualExecutionPaths": paths,
            },
        }), encoding="utf-8")

    def test_rejects_auto_upgraded_or_legacy_path_enabled_candidate(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            a_dir, b_dir, out_dir = root / "a", root / "b", root / "out"
            a_dir.mkdir()
            b_dir.mkdir()
            self.write_snapshot(
                b_dir,
                "FailureReverse_ObservationInvalidated_WideStop1_5R",
                "V500_ZONEBIRTH_SHORT_SCORE45_ACTUAL_CALIBRATION",
                "upgradedDefault",
            )
            summary = MODULE.analyze(a_dir, b_dir, out_dir)
            self.assertEqual("FAIL", summary["status"])
            self.assertIn("CONFIG_STATUS_NOT_LOADED", summary["config_failures"])
            self.assertIn("OLD_IMMEDIATE_PATH_ENABLED", summary["config_failures"])

    def test_accepts_loaded_strict_replacement_profile(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            a_dir, b_dir, out_dir = root / "a", root / "b", root / "out"
            a_dir.mkdir()
            b_dir.mkdir()
            self.write_snapshot(
                b_dir,
                "FailureReverse_PreEntryConfirmedWideStopShort",
                "V500_FR_PREENTRY_WIDESHORT_CALIBRATION",
            )
            summary = MODULE.analyze(a_dir, b_dir, out_dir)
            self.assertEqual("PASS", summary["status"])
            self.assertEqual([], summary["config_failures"])


if __name__ == "__main__":
    unittest.main()
