import importlib.util
import unittest
from pathlib import Path

import pandas as pd


PATH = Path(__file__).parents[1] / "Analyze-OPFV500DailyCap13ActualCalibration.py"
SPEC = importlib.util.spec_from_file_location("v500_daily_cap13_actual_calibration", PATH)
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class V500DailyCap13ActualCalibrationTests(unittest.TestCase):
    def test_classify_trade_pairs_separates_stable_role_change_and_one_sided_rows(self):
        candidate = pd.DataFrame([
            {"SignalID": "same", "ResearchPath": "A", "Side": "Long", "ExitRole": "TP", "NetPnLDollars": 10.0},
            {"SignalID": "role", "ResearchPath": "A", "Side": "Long", "ExitRole": "SL", "NetPnLDollars": -5.0},
            {"SignalID": "new", "ResearchPath": "A", "Side": "Long", "ExitRole": "TP", "NetPnLDollars": 8.0},
        ])
        baseline = pd.DataFrame([
            {"SignalID": "same", "ResearchPath": "A", "Side": "Long", "ExitRole": "TP", "NetPnLDollars": 9.0},
            {"SignalID": "role", "ResearchPath": "A", "Side": "Long", "ExitRole": "TP", "NetPnLDollars": 6.0},
            {"SignalID": "old", "ResearchPath": "A", "Side": "Long", "ExitRole": "SL", "NetPnLDollars": -4.0},
        ])

        result = MODULE.classify_trade_pairs(candidate, baseline)

        self.assertEqual(1, int(result["StableMatched"].sum()))
        self.assertEqual(1, int(result["ExitRoleChanged"].sum()))
        self.assertEqual(1, int(result["CandidateOnly"].sum()))
        self.assertEqual(1, int(result["BaselineOnly"].sum()))

    def test_classify_trade_pairs_keeps_two_paths_that_share_a_signal_id_separate(self):
        candidate = pd.DataFrame([
            {"SignalID": "shared", "ResearchPath": "A", "Side": "Long", "ExitRole": "TP", "NetPnLDollars": 10.0},
            {"SignalID": "shared", "ResearchPath": "B", "Side": "Long", "ExitRole": "SL", "NetPnLDollars": -5.0},
        ])

        result = MODULE.classify_trade_pairs(candidate, candidate.copy())

        self.assertEqual(2, int(result["StableMatched"].sum()))

    def test_summarize_candidate_ledger_keeps_abnormal_account_impact_out_of_normal_net(self):
        ledger = pd.DataFrame([
            {"Classification": "Normal", "NetPnLDollars": 10.0},
            {"Classification": "Abnormal", "NetPnLDollars": -2.0},
        ])

        result = MODULE.summarize_candidate_ledger(ledger)

        self.assertEqual({
            "NormalTrades": 1,
            "NormalNetDollars": 10.0,
            "IsolatedAbnormalTrades": 1,
            "AbnormalAccountNetDollars": -2.0,
        }, result)

    def test_normal_execution_join_uses_execution_side_without_suffixing_the_identity_key(self):
        ledger = pd.DataFrame([
            {"TradeID": "trade", "Side": "Short", "Classification": "Normal", "NetPnLDollars": 10.0},
        ])
        trades = pd.DataFrame([
            {"TradeID": "trade", "SignalID": "signal", "ResearchPath": "Path", "Side": "Long", "ExitRole": "TP"},
        ])

        result = MODULE.build_normal_execution_rows(ledger, trades)

        self.assertEqual(["signal"], result["SignalID"].tolist())
        self.assertEqual(["Long"], result["Side"].tolist())

    def test_pair_economics_uses_the_actual_reference_ledger_not_a_contract_estimate(self):
        pairs = pd.DataFrame([
            {"CandidateNetPnLDollars": 10.0, "BaselineNetPnLDollars": 7.0},
            {"CandidateNetPnLDollars": None, "BaselineNetPnLDollars": -2.0},
        ])

        result = MODULE.summarize_pair_economics(pairs)

        self.assertEqual({
            "ReferenceFrozenNormalNetDollars": 5.0,
            "CandidateNormalNetDollars": 10.0,
            "ActualChangeVsReferenceDollars": 5.0,
        }, result)


if __name__ == "__main__":
    unittest.main()
