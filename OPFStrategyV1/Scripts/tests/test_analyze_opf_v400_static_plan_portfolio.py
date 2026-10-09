import importlib.util
import unittest
from pathlib import Path

import pandas as pd


PATH = Path(__file__).parents[1] / "Analyze-OPFV400StaticPlanPortfolio.py"
SPEC = importlib.util.spec_from_file_location("v400_static_portfolio", PATH)
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class V400StaticPlanPortfolioTests(unittest.TestCase):
    def label(self, terminal="TP"):
        return pd.Series({
            "TerminalLabel": terminal, "StrictGrossDollars": 18.0 if terminal != "Ambiguous" else pd.NA,
            "LowerBoundGrossDollars": -12.0 if terminal == "Ambiguous" else 18.0,
            "UpperBoundGrossDollars": 18.0, "OutcomeTime": "2026-01-01T10:05:00",
        })

    def test_strict_excludes_ambiguous_and_censored(self):
        self.assertIsNone(MODULE.resolve_outcome(self.label("Ambiguous"), "Strict"))
        self.assertIsNone(MODULE.resolve_outcome(self.label("Censored"), "Strict"))

    def test_lower_and_upper_resolve_ambiguous_differently(self):
        ambiguous = self.label("Ambiguous")
        self.assertEqual(-12.0, MODULE.resolve_outcome(ambiguous, "LowerBound"))
        self.assertEqual(18.0, MODULE.resolve_outcome(ambiguous, "UpperBound"))

    def test_same_direction_two_slot_limit_blocks_third_candidate(self):
        candidates = pd.DataFrame([
            {"CandidateID": "a", "SnapshotID": "day", "DecisionTime": "2026-01-01T10:00:00", "Side": "Long", "ResearchPath": "P", "TerminalLabel": "TimeStop", "OutcomeTime": "2026-01-01T10:20:00", "StrictGrossDollars": 1.0, "LowerBoundGrossDollars": 1.0, "UpperBoundGrossDollars": 1.0},
            {"CandidateID": "b", "SnapshotID": "day", "DecisionTime": "2026-01-01T10:01:00", "Side": "Long", "ResearchPath": "P", "TerminalLabel": "TimeStop", "OutcomeTime": "2026-01-01T10:20:00", "StrictGrossDollars": 1.0, "LowerBoundGrossDollars": 1.0, "UpperBoundGrossDollars": 1.0},
            {"CandidateID": "c", "SnapshotID": "day", "DecisionTime": "2026-01-01T10:02:00", "Side": "Long", "ResearchPath": "P", "TerminalLabel": "TimeStop", "OutcomeTime": "2026-01-01T10:20:00", "StrictGrossDollars": 1.0, "LowerBoundGrossDollars": 1.0, "UpperBoundGrossDollars": 1.0},
        ])
        trades, decisions = MODULE.simulate(candidates, "Strict")
        self.assertEqual({"a", "b"}, set(trades["CandidateID"]))
        self.assertEqual("SameDirectionSlotLimit", decisions.set_index("CandidateID").loc["c", "BlockReason"])

    def test_globex_locked_candidate_is_not_accepted(self):
        candidates = pd.DataFrame([{
            "CandidateID": "locked", "SnapshotID": "day", "DecisionTime": "2026-01-01T10:00:00",
            "Side": "Long", "ResearchPath": "P", "TerminalLabel": "TP", "OutcomeTime": "2026-01-01T10:05:00",
            "StrictGrossDollars": 18.0, "LowerBoundGrossDollars": 18.0, "UpperBoundGrossDollars": 18.0,
            "GlobexLocked": True,
        }])
        trades, decisions = MODULE.simulate(candidates, "Strict")
        self.assertTrue(trades.empty)
        self.assertEqual("GlobexLocked", decisions.iloc[0]["BlockReason"])


if __name__ == "__main__":
    unittest.main()
