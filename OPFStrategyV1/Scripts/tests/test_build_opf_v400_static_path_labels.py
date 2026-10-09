import importlib.util
import unittest
from pathlib import Path

import pandas as pd


PATH = Path(__file__).parents[1] / "Build-OPFV400StaticPathLabels.py"
SPEC = importlib.util.spec_from_file_location("v400_static_labels", PATH)
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class V400StaticPathLabelTests(unittest.TestCase):
    def candidate(self, side="Long"):
        return pd.Series({
            "CandidateID": "candidate", "DecisionTime": "2026-01-01T10:00:00",
            "DecisionBar": 10, "Side": side, "PlannedEntry": 100.0,
            "PlannedStop": 98.0 if side == "Long" else 102.0,
            "PlannedTarget": 103.0 if side == "Long" else 97.0,
        })

    def bars(self, first=None, count=12):
        first = first or {"Open": 100.0, "High": 101.0, "Low": 99.0, "Close": 100.5}
        times = pd.date_range("2026-01-01T10:05:00", periods=count, freq="5min")
        return pd.DataFrame([{"Time": time, **first} for time in times])

    def test_long_target_after_decision_bar_is_tp(self):
        bars = self.bars({"Open": 100.0, "High": 103.0, "Low": 99.0, "Close": 102.0})
        label = MODULE.label_candidate(self.candidate(), bars)
        self.assertEqual("TP", label["TerminalLabel"])

    def test_short_stop_after_decision_bar_is_sl(self):
        bars = self.bars({"Open": 100.0, "High": 102.0, "Low": 99.0, "Close": 101.0})
        label = MODULE.label_candidate(self.candidate("Short"), bars)
        self.assertEqual("SL", label["TerminalLabel"])

    def test_same_m5_stop_and_target_is_ambiguous(self):
        bars = self.bars({"Open": 100.0, "High": 103.0, "Low": 98.0, "Close": 100.0})
        label = MODULE.label_candidate(self.candidate(), bars)
        self.assertEqual("Ambiguous", label["TerminalLabel"])
        self.assertTrue(pd.isna(label["StrictGrossDollars"]))
        self.assertLess(label["LowerBoundGrossDollars"], label["UpperBoundGrossDollars"])

    def test_incomplete_continuous_window_is_censored(self):
        label = MODULE.label_candidate(self.candidate(), self.bars(count=11))
        self.assertEqual("Censored", label["TerminalLabel"])
        self.assertTrue(pd.isna(label["LowerBoundGrossDollars"]))

    def test_decision_bar_is_not_scanned(self):
        decision = pd.DataFrame([{
            "Time": pd.Timestamp("2026-01-01T10:00:00"), "Open": 100.0, "High": 103.0,
            "Low": 98.0, "Close": 100.0,
        }])
        label = MODULE.label_candidate(self.candidate(), pd.concat([decision, self.bars()], ignore_index=True))
        self.assertEqual("TimeStop", label["TerminalLabel"])

    def test_tick_calibration_ignores_ticks_at_or_before_decision_sequence(self):
        candidate = self.candidate()
        candidate["MarketSequence"] = 10
        ticks = pd.DataFrame([
            {"Sequence": 10, "M5Time": "2026-01-01T10:05:00", "Price": 103.0},
            {"Sequence": 11, "M5Time": "2026-01-01T10:05:00", "Price": 100.5},
        ])
        tick = MODULE.label_candidate_ticks(candidate, ticks)
        self.assertEqual("TimeStop", tick["TickTerminalLabel"])


if __name__ == "__main__":
    unittest.main()
