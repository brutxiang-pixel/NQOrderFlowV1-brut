import unittest
from pathlib import Path


ROOT = Path(__file__).parents[2]
STRATEGY = ROOT / "Strategy" / "OpeningPullbackFailureStrategy.cs"
SCENARIO = ROOT / "Strategy" / "OpeningPullbackFailureStrategy.MarketExecutionScenario.cs"
TAPE = ROOT / "Strategy" / "OpeningPullbackFailureStrategy.MarketExecutionTape.cs"
ZONE_BEHAVIOR = ROOT / "Strategy" / "OpeningPullbackFailureStrategy.ZoneBehavior.cs"
LOGGER = ROOT / "Research" / "ResearchLogger.cs"
PROFILE = ROOT / "Configs" / "OPFStrategyV1_v400_market_execution_tape_4day_data_only.json"
CANDIDATE_PROFILE = ROOT / "Configs" / "OPFStrategyV1_v400_candidate_scenario_tape_data_only.json"
CANDIDATE_VALIDATOR = ROOT / "Scripts" / "Validate-OPFV400CandidateScenarioTape.py"


class MarketExecutionTapeContractTests(unittest.TestCase):
    def test_data_only_blocks_orders_and_captures_before_early_return(self):
        source = STRATEGY.read_text(encoding="utf-8")
        self.assertIn("!MarketExecutionTapeDataOnly", source)
        start = source.index("private void TrySubmitReplayExecution(")
        capture = source.index("CaptureMarketExecutionScenario(", start)
        early = source.index("if (!ActualOrdersEnabled || _snapshot is null)", start)
        self.assertLess(capture, early)

    def test_scenario_tape_has_stable_identity_zone_context_and_stop_flush(self):
        scenario = SCENARIO.read_text(encoding="utf-8")
        logger = LOGGER.read_text(encoding="utf-8")
        for token in ("CandidateId", "ZoneId", "ResolveMarketExecutionScenario", "FlushMarketExecutionScenarios"):
            self.assertIn(token, scenario)
        self.assertIn("AppendMarketExecutionScenario", logger)
        self.assertIn("market_execution_scenarios.csv", logger)

    def test_four_day_profile_forces_data_only_orders_off(self):
        profile = PROFILE.read_text(encoding="utf-8")
        self.assertIn('"RunProfileId": "V400_MARKET_EXECUTION_TAPE_4DAY_DATA_ONLY"', profile)
        self.assertIn('"RunMode": "MarketExecutionTapeDataOnly"', profile)
        self.assertIn('"EnableActualOrders": false', profile)

    def test_market_execution_mode_collects_zone_context_from_same_time_tape(self):
        source = ZONE_BEHAVIOR.read_text(encoding="utf-8")
        start = source.index("private void RecordZoneBehaviorTrade")
        end = source.index("private void UpdateZoneBehaviorLedger", start)
        self.assertIn("!ZoneContextCollectionEnabled", source[start:end])

    def test_zone_context_is_frozen_at_candidate_time(self):
        source = SCENARIO.read_text(encoding="utf-8")
        self.assertIn("CaptureMarketExecutionZoneContext", source)
        self.assertNotIn("ZoneBehaviorState? _zoneState", source)

    def test_stop_flushes_strategy_scenarios_before_logger_tape(self):
        source = STRATEGY.read_text(encoding="utf-8")
        stop = source.index("protected override void OnStopped()")
        strategy_flush = source.index("FlushMarketExecutionScenarios();", stop)
        logger_flush = source.index("_researchLogger?.FlushMarketExecutionTape();", stop)
        self.assertLess(strategy_flush, logger_flush)

    def test_candidate_scenario_mode_collects_context_without_tick_or_zone_outputs(self):
        strategy = STRATEGY.read_text(encoding="utf-8")
        scenario = SCENARIO.read_text(encoding="utf-8")
        zone_behavior = ZONE_BEHAVIOR.read_text(encoding="utf-8")
        self.assertIn("CandidateScenarioTapeDataOnly", strategy)
        self.assertIn("CandidateScenarioTapeDataOnly || MarketExecutionTapeDataOnly", scenario)
        self.assertIn("ZoneContextCollectionEnabled", zone_behavior)
        self.assertIn("ZoneBehaviorOutputEnabled", zone_behavior)
        self.assertIn("if (!MarketExecutionTapeDataOnly", TAPE.read_text(encoding="utf-8"))

    def test_candidate_scenario_profile_and_validator_require_zero_orders_without_tick_files(self):
        profile = CANDIDATE_PROFILE.read_text(encoding="utf-8")
        validator = CANDIDATE_VALIDATOR.read_text(encoding="utf-8")
        self.assertIn('"RunProfileId": "V400_CANDIDATE_SCENARIO_TAPE_DATA_ONLY"', profile)
        self.assertIn('"RunMode": "CandidateScenarioTapeDataOnly"', profile)
        self.assertIn('"EnableActualOrders": false', profile)
        self.assertIn("market_execution_scenarios.csv", validator)
        self.assertIn("market_execution_ticks.csv", validator)


if __name__ == "__main__":
    unittest.main()
