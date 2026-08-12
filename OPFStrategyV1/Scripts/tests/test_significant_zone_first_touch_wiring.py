from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
STRATEGY = (ROOT / "Strategy" / "OpeningPullbackFailureStrategy.cs").read_text(encoding="utf-8")
ZONES = (ROOT / "Strategy" / "OpeningPullbackFailureStrategy.SignificantZones.cs").read_text(encoding="utf-8")
ENGINE = (ROOT / "Core" / "Signals" / "SignificantZoneFirstTouchEngine.cs").read_text(encoding="utf-8")


def test_first_touch_is_a_grade_untested_reclaim_then_next_bar_entry():
    assert "SignificantZoneFirstTouchEngine" in ENGINE
    assert "zone.Strength?.Grade != SignificantZoneGrade.A" in ENGINE
    assert "candle.Bar + 1" in ENGINE
    assert "TryQueueSignificantZoneFirstTouch(zone, candle, regime);" in ZONES
    assert "TryActivateSignificantZoneFirstTouches(bar);" in STRATEGY
    assert "SignificantZoneFirstTouchLongPath" in STRATEGY
    assert "SignificantZoneFirstTouchShortPath" in STRATEGY


def test_first_touch_stop_uses_confirmation_candle_failure_extreme():
    assert "var stop = zone.Side == TradeSide.Long ? candle.Low - .5m : candle.High + .5m;" in ENGINE
    assert "zone.OuterBoundary - 1m" not in ENGINE
    assert "zone.OuterBoundary + 1m" not in ENGINE


def test_first_touch_does_not_apply_nearest_structure_rr_gate():
    assert "var minEstimatedRr = isSignificantZoneFirstTouch\n            ? 0m" in STRATEGY


def test_passive_limit_execution_paths_are_removed():
    assert "SignificantZonePassiveLimitLong" not in STRATEGY
    assert "SignificantZonePassiveLimitShort" not in STRATEGY
    assert "IsPassiveLimitEntry" not in STRATEGY


if __name__ == "__main__":
    test_first_touch_is_a_grade_untested_reclaim_then_next_bar_entry()
    test_first_touch_stop_uses_confirmation_candle_failure_extreme()
    test_passive_limit_execution_paths_are_removed()
