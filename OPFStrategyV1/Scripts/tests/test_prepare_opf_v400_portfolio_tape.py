import csv
import importlib.util
import tempfile
import unittest
from pathlib import Path


PATH = Path(__file__).parents[1] / "Prepare-OPFV400PortfolioTape.py"
SPEC = importlib.util.spec_from_file_location("v400_portfolio_tape", PATH)
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class V400PortfolioTapeTests(unittest.TestCase):
    def write_scenarios(self, root, snapshot, rows):
        with (root / f"{snapshot}_market_execution_scenarios.csv").open("w", newline="", encoding="utf-8") as handle:
            writer = csv.DictWriter(handle, fieldnames=MODULE.REQUIRED_COLUMNS)
            writer.writeheader()
            writer.writerows(rows)

    def scenario(self, candidate_id="candidate", entry="100", bid="99.75"):
        row = {column: "" for column in MODULE.REQUIRED_COLUMNS}
        row.update({
            "SnapshotID": "ignored", "CandidateID": candidate_id, "SignalID": candidate_id, "DecisionTime": "2026-01-01T00:00:00",
            "DecisionBar": "1", "Side": "Long", "SetupType": "Setup", "ResearchPath": "Path", "PlannedEntry": entry,
            "PlannedStop": "95", "PlannedTarget": "107.5", "PlannedRiskPoints": "5", "PlannedTargetR": "1.5",
            "EntryBid": bid, "EntryAsk": "100", "MarketSequence": "10", "DailyTradeCount": "0", "DailyGrossDollars": "0",
            "DailyAccountNetDollars": "0", "WeeklyLongNetDollars": "0", "ActiveTradeCount": "0", "SecondarySlotOccupied": "False",
            "GlobexLocked": "False", "GlobexReason": "", "UsOpenBlackout": "False", "UsOpenReason": "", "LatencyGateActive": "False",
            "DailyTradeLimitReached": "False", "DailyLossReached": "False", "WeeklyLongGateActive": "False", "ZoneID": "zone",
            "ZoneType": "FVG", "ZoneDirection": "Bull", "ZoneLow": "96", "ZoneHigh": "100", "ZoneCreatedTime": "2025-12-31T23:00:00",
            "ZoneCreatedBar": "0", "ZoneTouchOrdinal": "1", "ZoneLastTouchTime": "", "ZoneLastTouchBar": "", "ZoneLastTouchMarketSequence": "",
            "ZoneCumulativeBuy": "10", "ZoneCumulativeSell": "8", "ZoneCumulativeDelta": "2", "ZoneCumulativeInZoneBuy": "2",
            "ZoneCumulativeInZoneSell": "1", "ZoneCumulativeInZoneDelta": "1", "Decision": "DataOnlyObserved", "Reason": "ActualOrdersDisabled",
        })
        return row

    def test_same_candidate_with_quote_and_sequence_jitter_is_connector_compatible(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            deep = root / "deep"; broad = root / "broad"
            deep.mkdir(); broad.mkdir()
            self.write_scenarios(deep, "deep", [self.scenario(bid="99.5")])
            self.write_scenarios(broad, "broad", [self.scenario(bid="99.75")])
            comparison, portfolio = MODULE.connect(deep, broad)
            self.assertEqual(1, comparison["StableMatches"])
            self.assertEqual(1, comparison["DynamicMismatches"])
            self.assertEqual("CounterfactualPending", portfolio.iloc[0]["OutcomeSource"])

    def test_planned_trade_geometry_difference_fails_connection(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            deep = root / "deep"; broad = root / "broad"
            deep.mkdir(); broad.mkdir()
            self.write_scenarios(deep, "deep", [self.scenario(entry="100")])
            self.write_scenarios(broad, "broad", [self.scenario(entry="101")])
            with self.assertRaisesRegex(ValueError, "Stable scenario mismatch"):
                MODULE.connect(deep, broad)

    def test_gate_calibration_root_can_be_combined_with_full_broad_collection(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            deep = root / "deep"; gate = root / "gate"; full = root / "full"
            deep.mkdir(); gate.mkdir(); full.mkdir()
            self.write_scenarios(deep, "deep", [self.scenario(candidate_id="gate")])
            self.write_scenarios(gate, "gate", [self.scenario(candidate_id="gate")])
            self.write_scenarios(full, "full", [self.scenario(candidate_id="full")])
            comparison, portfolio = MODULE.connect(deep, [gate, full])
            self.assertEqual(1, comparison["StableMatches"])
            self.assertEqual(2, comparison["BroadCandidates"])
            self.assertEqual({"gate", "full"}, set(portfolio["CandidateID"]))


if __name__ == "__main__":
    unittest.main()
