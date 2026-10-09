import csv
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path


PATH = Path(__file__).parents[1] / "Validate-OPFV400CandidateScenarioTape.py"
SPEC = importlib.util.spec_from_file_location("v400_candidate_scenario_tape", PATH)
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class CandidateScenarioTapeValidationTests(unittest.TestCase):
    def write_csv(self, root, snapshot, suffix, header, rows):
        with (root / f"{snapshot}_{suffix}.csv").open("w", newline="", encoding="utf-8") as handle:
            writer = csv.DictWriter(handle, fieldnames=header)
            writer.writeheader()
            writer.writerows(rows)

    def make_snapshot(self, root, create_forbidden_file=False, risk_rows=None, scenario_extra=None):
        snapshot = "OPF-test"
        (root / f"{snapshot}_ConfigSnapshot.json").write_text(json.dumps({
            "ResearchSchemaVersion": MODULE.RESEARCH_SCHEMA,
            "ActualExecutionSettings": {
                "Version": MODULE.ACTUAL_VERSION,
                "EnableActualOrders": False,
                "RunProfileId": MODULE.PROFILE,
                "RunMode": MODULE.MODE,
            },
        }), encoding="utf-8")
        (root / f"{snapshot}_research.log").write_text(
            "CANDIDATE_SCENARIO_TAPE_DATA_ONLY enabled=true actualOrders=false source=OnNewTrade+candidateScenario+zoneContext tickOutput=false zoneOutput=false",
            encoding="utf-8")
        scenario = {field: "" for field in MODULE.SCENARIO_REQUIRED}
        scenario.update({
            "CandidateID": "signal|path|time|1", "SignalID": "signal", "DecisionTime": "2026-01-01T00:00:00", "DecisionBar": "1",
            "ResearchPath": "Path", "PlannedEntry": "100", "PlannedStop": "95", "PlannedTarget": "107.5", "PlannedRiskPoints": "5",
            "EntryBid": "99.75", "EntryAsk": "100", "MarketSequence": "2", "ZoneID": "zone", "Decision": "DataOnlyObserved", "Reason": "ActualOrdersDisabled",
        })
        scenario.update(scenario_extra or {})
        scenario_header = [*MODULE.SCENARIO_REQUIRED, *[key for key in scenario if key not in MODULE.SCENARIO_REQUIRED]]
        self.write_csv(root, snapshot, "market_execution_scenarios", scenario_header, [scenario])
        self.write_csv(root, snapshot, "risk_evaluations", ["SignalID", "Time", "Bar", "ResearchPath"],
                       risk_rows or [{"SignalID": "signal", "Time": "2026-01-01T00:00:00", "Bar": "1", "ResearchPath": "Path"}])
        self.write_csv(root, snapshot, "execution_events", ["Event"], [])
        if create_forbidden_file:
            self.write_csv(root, snapshot, "market_execution_ticks", ["Sequence"], [])
        return snapshot

    def test_valid_broad_snapshot_passes_without_tick_or_zone_csv(self):
        with tempfile.TemporaryDirectory() as temp:
            result = MODULE.validate_snapshot(Path(temp), self.make_snapshot(Path(temp)))
            self.assertTrue(result["Passed"])
            self.assertEqual(0, result["ForbiddenOutputFiles"])

    def test_tick_output_fails_broad_snapshot_gate(self):
        with tempfile.TemporaryDirectory() as temp:
            result = MODULE.validate_snapshot(Path(temp), self.make_snapshot(Path(temp), create_forbidden_file=True))
            self.assertFalse(result["Passed"])
            self.assertEqual(1, result["ForbiddenOutputFiles"])

    def test_research_only_risk_row_is_reported_without_failing_execution_scenario_gate(self):
        with tempfile.TemporaryDirectory() as temp:
            risk_rows = [
                {"SignalID": "signal", "Time": "2026-01-01T00:00:00", "Bar": "1", "ResearchPath": "Path"},
                {"SignalID": "retest", "Time": "2026-01-01T00:05:00", "Bar": "2", "ResearchPath": "BreakawayRetest12Research"},
            ]
            result = MODULE.validate_snapshot(Path(temp), self.make_snapshot(Path(temp), risk_rows=risk_rows))
            self.assertTrue(result["Passed"])
            self.assertEqual(1, result["RiskRowsOutsideScenario"])

    def test_zone_sequence_uses_a_separate_counter_from_market_execution_sequence(self):
        with tempfile.TemporaryDirectory() as temp:
            result = MODULE.validate_snapshot(Path(temp), self.make_snapshot(
                Path(temp), scenario_extra={"ZoneLastTouchMarketSequence": "8"}))
            self.assertTrue(result["Passed"])


if __name__ == "__main__":
    unittest.main()
