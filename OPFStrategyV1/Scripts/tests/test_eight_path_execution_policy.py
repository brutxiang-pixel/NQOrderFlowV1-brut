from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
STRATEGY = (ROOT / "Strategy" / "OpeningPullbackFailureStrategy.cs").read_text(encoding="utf-8")
SETTINGS = (ROOT / "Core" / "Configuration" / "ActualExecutionSettings.cs").read_text(encoding="utf-8")
DEFAULT_CONFIG = (ROOT / "Configs" / "OPFStrategyV1_actual_execution.default.json").read_text(encoding="utf-8")
AUTO_CONFIG = (ROOT / "Configs" / "OPFStrategyV1_eight_path_auto.json").read_text(encoding="utf-8")


def test_default_is_manual_alert_with_requested_global_limits():
    assert '"ExecutionMode": "ManualAlert"' in DEFAULT_CONFIG
    assert '"ActualOrderQuantity": 1' in DEFAULT_CONFIG
    assert '"ActualDailyLossLimitDollars": 200' in DEFAULT_CONFIG
    assert '"ActualWeeklyLongLossLimitDollars": 0' in DEFAULT_CONFIG
    assert '"ActualMaxTradesPerDay": 20' in DEFAULT_CONFIG
    assert "string ExecutionMode" in SETTINGS
    assert "ActualWeeklyLongLossLimitDollars = settings.ActualWeeklyLongLossLimitDollars < 0m" in SETTINGS


def test_execution_policy_has_only_the_eight_approved_paths():
    expected = {
        "ObservationConfirm",
        "ObservationConfirm_WideStop1_5R",
        "BreakawayFvg",
        "BreakawayFvg_Qualified",
        "FailureReverse_ObservationInvalidated_WideStop1_5R",
        "FailureReverse_RetestFailed",
        "SignificantZoneFirstTouchLong",
        "SignificantZoneFirstTouchShort",
    }
    for path in expected:
        assert f'"{path}"' in STRATEGY
        assert path in AUTO_CONFIG
    assert "SignificantZonePassiveLimitLong" not in AUTO_CONFIG
    assert "SignificantZonePassiveLimitShort" not in AUTO_CONFIG
    assert "RestoredPriorityPath" in STRATEGY
    assert "TryEmitUnknownRegimeLongManualAlert" not in STRATEGY


def test_manual_alert_uses_shared_eight_path_policy():
    assert "TryEmitManualAlert" in STRATEGY
    assert "IsRestoredPriorityPathAllowed(signal.Side, researchPath)" in STRATEGY
    assert "var entry = entryCandle.Close;" in STRATEGY
    assert "var invalidStopSide = signal.Side == TradeSide.Long ? stop >= entry : stop <= entry;" in STRATEGY


def test_chart_manual_alert_mode_is_a_visible_execution_setting():
    assert '[Category("OPF Execution")]\n    [DisplayName("Manual Alert Mode")]\n    public bool ManualAlertMode { get; set; }' in STRATEGY


def test_chart_manual_alert_mode_overrides_json_execution_mode_after_settings_load():
    apply_settings = STRATEGY.index("ApplyActualExecutionSettings(actualExecutionSettings);")
    apply_run_mode = STRATEGY.index("ApplyRunMode(actualExecutionSettings.RunMode);")
    manual_override = STRATEGY.index("ApplyChartExecutionModeOverride();")
    assert manual_override > apply_settings
    assert manual_override > apply_run_mode
    assert "EnableReplayOrders = false;" in STRATEGY[STRATEGY.index("private void ApplyChartExecutionModeOverride"):]
    assert "_manualAlertEnabled = true;" in STRATEGY[STRATEGY.index("private void ApplyChartExecutionModeOverride"):]


def test_hud_wraps_long_manual_alerts_within_chart_width():
    draw_hud = STRATEGY[STRATEGY.index("private void DrawHud"):]
    assert "var maxBoxW = Math.Max(1, ChartArea.Width - padX * 2);" in draw_hud
    assert "var boundedHud = WrapHudText(context, font, hud, maxBoxW - padX * 2);" in draw_hud
    assert "var boxW = Math.Min(maxBoxW" in draw_hud
    assert "context.DrawString(boundedHud, font" in draw_hud
    assert "private static string WrapHudText(RenderContext context, RenderFont font, string value, int maxLineWidth)" in STRATEGY
    assert "context.MeasureString(candidate, font).Width > maxLineWidth" in STRATEGY


def test_manual_alert_has_a_dedicated_vertical_decision_card():
    assert "private sealed record ManualAlertCard(" in STRATEGY
    assert "private readonly List<ManualAlertCard> _manualAlertCards = new();" in STRATEGY
    assert "_manualAlertCards.Insert(0, new ManualAlertCard(" in STRATEGY
    assert "while (_manualAlertCards.Count > 3)" in STRATEGY
    assert "DrawManualAlertCards(context, cards);" in STRATEGY
    assert "private void DrawManualAlertCards(RenderContext context, IReadOnlyList<ManualAlertCard> cards)" in STRATEGY
    assert "var x = ChartArea.X + margin;" in STRATEGY
    assert "const int topOffset = 60;" in STRATEGY
    assert "var y = ChartArea.Y + topOffset + index * (cardHeight + cardGap);" in STRATEGY
    assert 'TimeZoneInfo.FindSystemTimeZoneById("China Standard Time")' in STRATEGY
    assert "var beijingTime = ToBeijingTime(card.Time);" in STRATEGY
    assert '{beijingTime:HH:mm} BJT' in STRATEGY
    assert 'hud.AppendLine("ManualAlert: see decision card");' in STRATEGY
    assert "LATEST CANDIDATE" in STRATEGY
    assert "manual decision - no auto order" in STRATEGY


def test_first_touch_uses_next_bar_market_execution():
    assert "SignificantZoneFirstTouchLongPath" in STRATEGY
    assert "SignificantZoneFirstTouchShortPath" in STRATEGY
    assert "TryActivateSignificantZoneFirstTouches(bar);" in STRATEGY
    assert "TrySubmitReplayExecution(signal, entryCandle, path, pending.Entry.InitialStop, risk, allowDelayedExpansion: false);" in STRATEGY
    assert '"ActualOrderQuantity": 1' in AUTO_CONFIG
    assert '"ActualMaxTradesPerDay": 20' in AUTO_CONFIG
    assert '"ActualDailyLossLimitDollars": 200.0' in AUTO_CONFIG
    assert '"ActualWeeklyLongLossLimitDollars": 0.0' in AUTO_CONFIG


if __name__ == "__main__":
    test_default_is_manual_alert_with_requested_global_limits()
    test_execution_policy_has_only_the_eight_approved_paths()
    test_manual_alert_uses_shared_eight_path_policy()
    test_chart_manual_alert_mode_is_a_visible_execution_setting()
    test_chart_manual_alert_mode_overrides_json_execution_mode_after_settings_load()
    test_hud_wraps_long_manual_alerts_within_chart_width()
    test_manual_alert_has_a_dedicated_vertical_decision_card()
    test_first_touch_uses_next_bar_market_execution()
