import importlib.util
from pathlib import Path
import unittest


SCRIPT = Path(__file__).parents[1] / "Analyze-OPFV277StaticPlanAudit.py"
SPEC = importlib.util.spec_from_file_location("audit", SCRIPT)
audit = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(audit)


class StaticPlanAuditTests(unittest.TestCase):
    def test_family_mapping_groups_exit_variants(self):
        self.assertEqual(audit.family_for("ObservationConfirm"), "ObservationConfirm")
        self.assertEqual(audit.family_for("ObservationConfirm_WideStop1_5R"), "ObservationConfirm")
        self.assertEqual(audit.family_for("StructureConfirmShadow_ConfirmBarStop_Wait1"), "StructureConfirmShadow")

    def test_stability_gate_requires_both_views_and_multiple_months(self):
        passing = {"Strict": [10, 10, 10, 10, -1, 5, 5], "LowerBound": [9, 9, 9, 9, -1, 4, 4]}
        failing = {"Strict": [50, -10, -10, -10, -10, -10, -10], "LowerBound": [50, -10, -10, -10, -10, -10, -10]}
        self.assertTrue(audit.evaluate_stability(passing, {"Strict": 25, "LowerBound": 25}, {"Strict": 0, "LowerBound": 0})["Pass"])
        self.assertFalse(audit.evaluate_stability(failing, {"Strict": 25, "LowerBound": 25}, {"Strict": 0, "LowerBound": 0})["Pass"])
