import importlib.util
import unittest
from pathlib import Path

import pandas as pd


PATH = Path(__file__).parents[1] / "Analyze-OPFV500DailyGates.py"
SPEC = importlib.util.spec_from_file_location("v500_daily_gates", PATH)
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class V500DailyGateTests(unittest.TestCase):
    def test_rollback_event_names_are_classified_without_treating_normal_exit_as_rollback(self):
        self.assertTrue(MODULE.is_counter_rollback("ENTRY_SUBMISSION_ABORTED_V178"))
        self.assertTrue(MODULE.is_counter_rollback("ABNORMAL_SAFETY_FLATTEN_ISOLATED_V182"))
        self.assertFalse(MODULE.is_counter_rollback("LIVE_ACCOUNT_PNL"))

    def test_replay_state_rolls_back_aborted_entry_and_applies_exit_net_before_next_gate(self):
        events = pd.DataFrame([
            {"Time": "2026-01-01T00:00:00", "Kind": "Execute", "TradeID": "abort"},
            {"Time": "2026-01-01T00:00:00", "Kind": "Rollback", "TradeID": "abort"},
            {"Time": "2026-01-01T00:05:00", "Kind": "Execute", "TradeID": "normal"},
            {"Time": "2026-01-01T00:10:00", "Kind": "AccountPnl", "TradeID": "normal", "Net": -60.0},
            {"Time": "2026-01-01T00:15:00", "Kind": "Execute", "TradeID": "next"},
        ])

        states = MODULE.replay_gate_states(events)

        self.assertEqual(0, states.loc[states.TradeID.eq("normal"), "PreTradeCount"].item())
        self.assertEqual(-60.0, states.loc[states.TradeID.eq("next"), "PreDailyNet"].item())

    def test_stricter_cap_blocks_later_entry_using_reconstructed_counter(self):
        events = pd.DataFrame([
            {"Time": "2026-01-01T00:00:00", "Kind": "Execute", "TradeID": "a"},
            {"Time": "2026-01-01T00:05:00", "Kind": "Execute", "TradeID": "b"},
        ])

        accepted = MODULE.simulate_frozen_gates(events, max_trades=1, loss_limit=250.0)

        self.assertEqual(["a"], accepted["TradeID"].tolist())

    def test_selected_normal_baseline_excludes_pnl_of_blocked_entry(self):
        timeline = pd.DataFrame([
            {"SnapshotID": "day", "Sequence": 0, "Time": "2026-01-01T00:00:00", "Kind": "Execute", "TradeID": "a"},
            {"SnapshotID": "day", "Sequence": 1, "Time": "2026-01-01T00:05:00", "Kind": "AccountPnl", "TradeID": "a", "EntrySequence": 0, "Classification": "Normal", "Net": 10.0},
            {"SnapshotID": "day", "Sequence": 2, "Time": "2026-01-01T00:10:00", "Kind": "Execute", "TradeID": "b"},
            {"SnapshotID": "day", "Sequence": 3, "Time": "2026-01-01T00:15:00", "Kind": "AccountPnl", "TradeID": "b", "EntrySequence": 2, "Classification": "Normal", "Net": 20.0},
        ])
        baseline = pd.DataFrame([
            {"SnapshotID": "day", "TradeID": "a", "NetPnLDollars": 10.0},
            {"SnapshotID": "day", "TradeID": "b", "NetPnLDollars": 20.0},
        ])

        result = MODULE.select_frozen_normal_baseline(timeline, baseline, max_trades=1, loss_limit=250.0)

        self.assertEqual(["a"], result["TradeID"].tolist())


if __name__ == "__main__":
    unittest.main()
