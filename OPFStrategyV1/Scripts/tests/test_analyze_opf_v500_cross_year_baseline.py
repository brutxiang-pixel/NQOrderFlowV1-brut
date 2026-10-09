import importlib.util
import unittest
from pathlib import Path

import pandas as pd


PATH = Path(__file__).parents[1] / "Analyze-OPFV500CrossYearBaseline.py"
SPEC = importlib.util.spec_from_file_location("v500_cross_year_baseline", PATH)
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class V500CrossYearBaselineTests(unittest.TestCase):
    def test_training_filter_keeps_only_registered_months_and_excludes_overlap_snapshot(self):
        rows = pd.DataFrame([
            {"SnapshotID": "apr", "EntryTime": "2025-04-01T10:00:00", "Classification": "Normal"},
            {"SnapshotID": "q4", "EntryTime": "2025-10-01T10:00:00", "Classification": "Normal"},
            {"SnapshotID": "h1", "EntryTime": "2026-06-01T10:00:00", "Classification": "Normal"},
            {"SnapshotID": "july", "EntryTime": "2026-07-01T10:00:00", "Classification": "Normal"},
            {"SnapshotID": "q1-blind", "EntryTime": "2025-03-06T10:00:00", "Classification": "Normal"},
            {"SnapshotID": "aug-blind", "EntryTime": "2025-08-01T10:00:00", "Classification": "Normal"},
            {"SnapshotID": "OPF-20260728-233338", "EntryTime": "2026-07-06T10:00:00", "Classification": "Normal"},
            {"SnapshotID": "abnormal", "EntryTime": "2026-06-01T10:00:00", "Classification": "Abnormal"},
        ])

        result = MODULE.filter_training_rows(rows)

        self.assertEqual({"apr", "q4", "h1", "july"}, set(result["SnapshotID"]))

    def test_segment_assignment_keeps_july_as_training_stress_not_h1(self):
        rows = pd.DataFrame({"EntryTime": pd.to_datetime([
            "2025-04-01T10:00:00", "2025-10-01T10:00:00", "2026-06-30T10:00:00", "2026-07-01T10:00:00",
        ])})

        result = MODULE.add_time_dimensions(rows)

        self.assertEqual(
            ["2025-SpringSummer", "2025-Q4", "2026-H1", "2026-JulyStress"],
            result["TrainingSegment"].tolist(),
        )

    def test_july_batch_assigns_cross_midnight_entry_to_july(self):
        rows = pd.DataFrame([{
            "SnapshotID": "july-run", "EntryTime": "2025-06-30T23:55:00", "Classification": "Normal",
            "ArchiveName": "opf_v300_live_gate_2025_07_rerun_complete_21snapshots_20260730",
        }])

        result = MODULE.add_time_dimensions(MODULE.filter_training_rows(rows))

        self.assertEqual("2025-07", result.iloc[0]["Month"])
        self.assertEqual("2025-SpringSummer", result.iloc[0]["TrainingSegment"])

    def test_summary_reports_net_pf_and_max_drawdown(self):
        rows = pd.DataFrame({
            "EntryTime": pd.to_datetime(["2026-01-01T10:00:00", "2026-01-01T11:00:00", "2026-01-01T12:00:00"]),
            "GrossPnLDollars": [20.0, -10.0, 10.0],
            "CommissionDollars": [3.6, 3.6, 3.6],
            "NetPnLDollars": [16.4, -13.6, 6.4],
        })

        result = MODULE.summarize(rows)

        self.assertEqual(3, result["Trades"])
        self.assertEqual(20.0, result["GrossDollars"])
        self.assertEqual(9.2, result["NetDollars"])
        self.assertEqual(3.0, result["GrossPF"])
        self.assertEqual(-13.6, result["MaxDrawdownNet"])


if __name__ == "__main__":
    unittest.main()
