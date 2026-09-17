from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
STRATEGY = (ROOT / "Strategy" / "OpeningPullbackFailureStrategy.cs").read_text(encoding="utf-8")
GEX = (ROOT / "Strategy" / "OpeningPullbackFailureStrategy.Gex.cs").read_text(encoding="utf-8")
SCHEDULE_GATE = (ROOT / "Strategy" / "GexRefreshScheduleGate.cs").read_text(encoding="utf-8")
TRIGGER_GATE = (ROOT / "Strategy" / "GexRefreshTriggerGate.cs").read_text(encoding="utf-8")
LOADER = (ROOT / "Core" / "MarketData" / "GexDailySnapshot.cs").read_text(encoding="utf-8")
SCHEDULER_PS1 = (ROOT / "Scripts" / "Start-OPFGexSnapshotScheduler.ps1").read_text(encoding="utf-8")
SYNC_PS1 = (ROOT / "Scripts" / "Sync-OPFGexSnapshot.ps1").read_text(encoding="utf-8")


def test_strategy_reloads_gex_snapshot_on_every_closed_bar_without_restart():
    # Periodic in-strategy reload: no ATAS restart required to pick up a scheduler-refreshed file.
    call_site = STRATEGY.index("UpdateSignificantZones(current, regime, zones);")
    next_line = STRATEGY[call_site:call_site + 200]
    assert "UpdateGexIntradayRefresh(current);" in next_line
    assert "private void UpdateGexIntradayRefresh(OpfCandle current)" in GEX
    body = GEX[GEX.index("private void UpdateGexIntradayRefresh"):]
    assert "LoadGexSnapshot();" in body[: body.index("RequestForcedGexRefresh")]


def test_key_level_cross_and_volatility_spike_feed_a_single_forced_refresh_request():
    body = GEX[GEX.index("private void UpdateGexIntradayRefresh"):]
    assert "GexRefreshTriggerGate.DetectKeyLevelCross(previousClose.Value, current.Close, level.Price, GexTag(level))" in body
    assert "GexRefreshTriggerGate.DetectVolatilitySpike(current.High, current.Low, CalculateAtr14(current))" in body
    assert "GexRefreshTriggerGate.IsInCooldown(_gexLastForcedRefreshRequestUtc, nowUtc)" in body
    assert "RequestForcedGexRefresh(reason, nowUtc);" in body


def test_forced_refresh_request_is_a_file_handshake_not_a_direct_network_call():
    assert "private void RequestForcedGexRefresh(string reason, DateTime requestedAtUtc)" in GEX
    request_body = GEX[GEX.index("private void RequestForcedGexRefresh"):]
    assert "GexRefreshRequestFileName" in request_body
    assert 'requestedAtUtc = requestedAtUtc.ToString("O"), reason' in request_body
    assert "HttpClient" not in GEX
    assert "Invoke-WebRequest" not in GEX


def test_gex_reference_levels_used_for_trigger_are_the_same_four_hud_tags():
    assert 'level.LevelType.Equals("call_wall"' in GEX
    assert '"GEX-CW"' in GEX and '"GEX-PW"' in GEX and '"GEX-ZG"' in GEX and '"GEX-VT"' in GEX
    assert "private IReadOnlyList<GexLevel> GexReferenceLevels(decimal price)" in GEX


def test_loader_has_a_90_minute_scheduler_sync_staleness_overlay_independent_of_data_date():
    assert "public static readonly TimeSpan SyncFreshnessThreshold = TimeSpan.FromMinutes(90);" in LOADER
    assert "public static GexDailySnapshot Load(string path, decimal tickSize, DateTimeOffset nowUtc, string? syncStatePath)" in LOADER
    assert "var syncStale = lastSyncCompletedUtc is not null && nowUtc.UtcDateTime - lastSyncCompletedUtc.Value > SyncFreshnessThreshold;" in LOADER
    assert '"syncOlderThan90Minutes"' in LOADER
    # A missing/optional sync-state path must never force Stale by itself (unknown freshness != stale).
    assert "return null;" in LOADER[LOADER.index("TryReadSyncCompletedAtUtc"):]


def test_schedule_gate_resolves_premarket_and_half_hourly_intraday_slots_in_eastern_time():
    assert 'new(9, 0, 0)' in SCHEDULE_GATE
    assert 'new(9, 30, 0)' in SCHEDULE_GATE
    assert 'new(16, 0, 0)' in SCHEDULE_GATE
    assert "IntradayIntervalMinutes = 30" in SCHEDULE_GATE
    assert "DayOfWeek.Saturday or DayOfWeek.Sunday" in SCHEDULE_GATE
    assert "public static string? ResolveSlotKey(DateTime easternTime)" in SCHEDULE_GATE


def test_trigger_gate_is_pure_and_shares_a_single_cooldown_between_both_trigger_types():
    assert "ForcedRefreshCooldown = TimeSpan.FromMinutes(10);" in TRIGGER_GATE
    assert "VolatilitySpikeAtrMultiplier = 2.5m;" in TRIGGER_GATE
    assert "public static string? DetectKeyLevelCross(decimal previousClose, decimal currentClose, decimal levelPrice, string levelTag)" in TRIGGER_GATE
    assert "public static string? DetectVolatilitySpike(decimal barHigh, decimal barLow, decimal atr14)" in TRIGGER_GATE
    assert "public static bool IsInCooldown(DateTime? lastForcedRefreshUtc, DateTime nowUtc)" in TRIGGER_GATE
    for keyword in ("File.", "HttpClient", "Invoke-", "Console."):
        assert keyword not in TRIGGER_GATE


def test_scheduler_script_implements_all_four_refresh_mechanisms_with_dedupe_and_single_instance_guard():
    assert "param([switch]$RunOnce)" in SCHEDULER_PS1
    assert "'Local\\OPFStrategyV1-GexSnapshotScheduler'" in SCHEDULER_PS1
    assert "$baselineTimeOfDay = [TimeSpan]'09:15:00'" in SCHEDULER_PS1
    assert "$preMarketEastern = [TimeSpan]'09:00:00'" in SCHEDULER_PS1
    assert "$intradayStartEastern = [TimeSpan]'09:30:00'" in SCHEDULER_PS1
    assert "$intradayEndEastern = [TimeSpan]'16:00:00'" in SCHEDULER_PS1
    assert "$intradayIntervalMinutes = 30" in SCHEDULER_PS1
    assert "function Resolve-GexIntradaySlotKey" in SCHEDULER_PS1
    assert "completedSlots" in SCHEDULER_PS1
    assert "lastProcessedRefreshRequestAtUtc" in SCHEDULER_PS1
    assert "requestedAtUtc -gt $lastProcessed" in SCHEDULER_PS1


def test_scheduler_never_throws_out_of_its_polling_loop_on_a_single_mechanism_failure():
    # Each of the four mechanisms is wrapped in its own try/catch that only Write-Warning on failure,
    # so a vendor outage on one slot never stops the other mechanisms or crashes the 60s poll loop.
    assert SCHEDULER_PS1.count("catch {") >= 4
    assert "Write-Warning \"GEX baseline refresh check failed" in SCHEDULER_PS1
    assert "Write-Warning \"GEX intraday slot refresh check failed" in SCHEDULER_PS1
    assert "Write-Warning \"GEX forced refresh request handling failed" in SCHEDULER_PS1


def test_sync_script_keeps_the_last_valid_snapshot_on_any_validation_or_network_failure():
    assert '$temp = "$target.tmp"' in SYNC_PS1
    assert "Move-Item -LiteralPath $temp -Destination $target -Force" in SYNC_PS1
    assert "exit 1" in SYNC_PS1
    # On failure the temp file is removed and $target (the last valid snapshot) is left untouched.
    assert "if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Force }" in SYNC_PS1


def test_gex_never_touches_any_trading_gate_or_order_path():
    for forbidden in ("EnableReplayOrders", "TrySubmitReplayExecution", "AppendExecutionDecision"):
        assert forbidden not in GEX


if __name__ == "__main__":
    test_strategy_reloads_gex_snapshot_on_every_closed_bar_without_restart()
    test_key_level_cross_and_volatility_spike_feed_a_single_forced_refresh_request()
    test_forced_refresh_request_is_a_file_handshake_not_a_direct_network_call()
    test_gex_reference_levels_used_for_trigger_are_the_same_four_hud_tags()
    test_loader_has_a_90_minute_scheduler_sync_staleness_overlay_independent_of_data_date()
    test_schedule_gate_resolves_premarket_and_half_hourly_intraday_slots_in_eastern_time()
    test_trigger_gate_is_pure_and_shares_a_single_cooldown_between_both_trigger_types()
    test_scheduler_script_implements_all_four_refresh_mechanisms_with_dedupe_and_single_instance_guard()
    test_scheduler_never_throws_out_of_its_polling_loop_on_a_single_mechanism_failure()
    test_sync_script_keeps_the_last_valid_snapshot_on_any_validation_or_network_failure()
    test_gex_never_touches_any_trading_gate_or_order_path()
    print("PASS test_gex_intraday_refresh_wiring")
