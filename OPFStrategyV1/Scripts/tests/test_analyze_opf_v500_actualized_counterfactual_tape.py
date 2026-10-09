import importlib.util
import unittest
from pathlib import Path

import pandas as pd


PATH = Path(__file__).parents[1] / "Analyze-OPFV500ActualizedCounterfactualTape.py"
SPEC = importlib.util.spec_from_file_location("v500_actualized_tape", PATH)
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class V500ActualizedCounterfactualTapeTests(unittest.TestCase):
    def baseline(self):
        return pd.DataFrame([
            {
                "SnapshotID": "day", "TradeID": "zbr", "EntryTime": "2026-01-01T10:00:00",
                "ResearchPath": "ZoneBirthResearch", "Side": "Short", "TrainingSegment": "2026-H1",
                "SetupQualityScore": 45.0, "GrossPnLDollars": -20.0, "CommissionDollars": 3.6, "NetPnLDollars": -23.6,
            },
            {
                "SnapshotID": "day", "TradeID": "zbr-high", "EntryTime": "2026-01-01T10:05:00",
                "ResearchPath": "ZoneBirthResearch", "Side": "Short", "TrainingSegment": "2026-H1",
                "SetupQualityScore": 55.0, "GrossPnLDollars": -20.0, "CommissionDollars": 3.6, "NetPnLDollars": -23.6,
            },
            {
                "SnapshotID": "day", "TradeID": "other", "EntryTime": "2026-01-01T09:00:00",
                "ResearchPath": "ObservationConfirm", "Side": "Short", "TrainingSegment": "2026-H1",
                "SetupQualityScore": 60.0, "GrossPnLDollars": 30.0, "CommissionDollars": 3.6, "NetPnLDollars": 26.4,
            },
        ])

    def decisions(self):
        return pd.DataFrame([
            {"SnapshotID": "day", "SignalID": "cap", "Time": "2026-01-01T11:00:00", "Decision": "Skip", "Reason": "DailyTradeLimit:15", "ResearchPath": "ObservationConfirm", "Side": "Short"},
            {"SnapshotID": "day", "SignalID": "active", "Time": "2026-01-01T10:30:00", "Decision": "Skip", "Reason": "ActiveTrade:zbr|until=10:35", "ResearchPath": "ObservationConfirm", "Side": "Short"},
            {"SnapshotID": "day", "SignalID": "unrelated", "Time": "2026-01-01T09:30:00", "Decision": "Skip", "Reason": "DailyTradeLimit:15", "ResearchPath": "ObservationConfirm", "Side": "Short"},
        ])

    def test_tape_distinguishes_direct_active_and_gate_exposure(self):
        tape = MODULE.build_exposure_tape(self.baseline(), self.decisions())

        classes = tape.set_index("SignalID")["ExposureClass"].to_dict()
        self.assertEqual("GateExposure", classes["cap"])
        self.assertEqual("ActiveTradeExposure", classes["active"])
        self.assertNotIn("unrelated", classes)

    def test_frozen_gate_scenario_removes_only_target_actual_trade(self):
        scenario = MODULE.simulate_frozen_gates(self.baseline())

        self.assertEqual(-20.8, scenario["BaselineNetDollars"])
        self.assertEqual(-23.6, scenario["RemovedNetDollars"])
        self.assertEqual(2.8, scenario["FrozenGateNetDollars"])
        self.assertEqual(23.6, scenario["DirectNetDelta"])
        self.assertEqual(1, scenario["RemovedTrades"])


if __name__ == "__main__":
    unittest.main()
