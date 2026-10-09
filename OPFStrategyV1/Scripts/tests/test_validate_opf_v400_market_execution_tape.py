import csv
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path


PATH = Path(__file__).parents[1] / "Validate-OPFV400MarketExecutionTape.py"
SPEC = importlib.util.spec_from_file_location("v400_market_execution_tape", PATH)
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class MarketExecutionTapeValidationTests(unittest.TestCase):
    def write_csv(self, root, snapshot, suffix, header, rows):
        with (root / f"{snapshot}_{suffix}.csv").open("w", newline="", encoding="utf-8") as handle:
            writer = csv.DictWriter(handle, fieldnames=header)
            writer.writeheader()
            writer.writerows(rows)

    def make_snapshot(self, root, bad_sequence=False):
        snapshot = "OPF-test"
        (root / f"{snapshot}_ConfigSnapshot.json").write_text(json.dumps({
            "ResearchSchemaVersion": "OPF_RESEARCH_2.33",
            "ActualExecutionSettings": {
                "Version": "ACTUAL_EXEC_2.50",
                "EnableActualOrders": False,
                "RunProfileId": "V400_MARKET_EXECUTION_TAPE_4DAY_DATA_ONLY",
                "RunMode": "MarketExecutionTapeDataOnly",
            },
        }), encoding="utf-8")
        (root / f"{snapshot}_research.log").write_text(
            "MARKET_EXECUTION_TAPE_DATA_ONLY enabled=true actualOrders=false source=OnNewTrade+candidateScenario+zoneContext",
            encoding="utf-8")
        self.write_csv(root, snapshot, "market_execution_ticks",
            ["Sequence", "M5Time", "TickTime", "Price", "Volume", "Direction"],
            [
                {"Sequence": "1", "M5Time": "2026-01-01T00:00:00", "TickTime": "2026-01-01T00:00:01", "Price": "100", "Volume": "2", "Direction": "Buy"},
                {"Sequence": "3" if bad_sequence else "2", "M5Time": "2026-01-01T00:00:00", "TickTime": "2026-01-01T00:00:02", "Price": "101", "Volume": "3", "Direction": "Sell"},
            ])
        self.write_csv(root, snapshot, "rich_bar_features", ["Time", "Volume"],
            [{"Time": "2026-01-01T00:00:00", "Volume": "5"}])
        scenario_header = MODULE.SCENARIO_REQUIRED
        scenario = {name: "" for name in scenario_header}
        scenario.update({
            "CandidateID": "signal|path|time|1", "SignalID": "signal", "DecisionTime": "2026-01-01T00:00:00", "DecisionBar": "1",
            "ResearchPath": "Path", "PlannedEntry": "100", "PlannedStop": "95", "PlannedTarget": "107.5", "PlannedRiskPoints": "5",
            "EntryBid": "99.75", "EntryAsk": "100", "MarketSequence": "2", "ZoneID": "zone", "Decision": "DataOnlyObserved", "Reason": "ActualOrdersDisabled",
        })
        self.write_csv(root, snapshot, "market_execution_scenarios", scenario_header, [scenario])
        self.write_csv(root, snapshot, "risk_evaluations", ["SignalID", "Time", "Bar", "ResearchPath"],
            [{"SignalID": "signal", "Time": "2026-01-01T00:00:00", "Bar": "1", "ResearchPath": "Path"}])
        self.write_csv(root, snapshot, "execution_events", ["Event"], [])
        return snapshot

    def test_valid_snapshot_passes_and_exports_m5_rows(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            snapshot = self.make_snapshot(root)
            result, m5 = MODULE.validate_snapshot(root, snapshot)
            self.assertTrue(result["Passed"])
            self.assertEqual(1, result["ScenarioRows"])
            self.assertEqual(1, len(m5))
            self.assertEqual(5.0, m5[0]["Volume"])

    def test_sequence_gap_fails_gate(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            snapshot = self.make_snapshot(root, bad_sequence=True)
            result, _ = MODULE.validate_snapshot(root, snapshot)
            self.assertFalse(result["Passed"])
            self.assertEqual(1, result["TickSequenceErrors"])


if __name__ == "__main__":
    unittest.main()
