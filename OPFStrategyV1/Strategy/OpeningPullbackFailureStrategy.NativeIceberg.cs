using ATAS.DataFeedsCore;
using ATAS.Indicators;
using OPFStrategyV1.Core.MarketData;
using OPFStrategyV1.Core.Signals;
using OPFStrategyV1.Core.Scoring;
using System.Collections.Concurrent;

namespace OPFStrategyV1.Strategy;

public sealed partial class OpeningPullbackFailureStrategy
{
    private sealed record IcebergEvent(string Instrument, long Id, DateTime Time, decimal Price, TradeSide Side, decimal Hidden, decimal Filled, bool Deleted);
    private sealed record IcebergSwitch(CandidateSignal Signal, decimal Stop, decimal Price, DateTime Expires, string EventKey);
    private static readonly ConcurrentDictionary<string, IcebergEvent> NativeIcebergBus = new();
    private IMboAnalyticsDataProvider? _nativeMboProvider;
    private readonly Dictionary<string, IcebergEvent> _nativeIcebergs = new();
    private readonly HashSet<string> _nativeIcebergConsumed = new();
    private readonly HashSet<long> _nativeIcebergHistory = new();
    private readonly ConcurrentQueue<IcebergEvent> _nativeIcebergUpdates = new();
    private string _nativeIcebergHud = "Iceberg: NOT_SUBSCRIBED";
    private IcebergSwitch? _pendingIcebergSwitch;
    private readonly ConcurrentQueue<NativeIcebergEvent> _nativeIcebergArchiveWrites = new();
    private NativeIcebergEvent[] _nativeIcebergReplay = Array.Empty<NativeIcebergEvent>();
    private DateTime _nativeIcebergReplayDay;
    private string NativeIcebergArchiveDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ATAS", "StrategyLogs", "OPFStrategyV1", "NativeIcebergArchive");

    private void InitializeNativeIceberg()
    {
        _nativeIcebergs.Clear();
        _nativeIcebergConsumed.Clear();
        _nativeIcebergHistory.Clear();
        _pendingIcebergSwitch = null;
        _nativeIcebergReplayDay = default;
        while (_nativeIcebergUpdates.TryDequeue(out _)) { }
        try
        {
            if (DataProvider is null || !DataProvider.TryGetMboAnalyticsDataProvider(out _nativeMboProvider))
            {
                _nativeIcebergHud = "Iceberg: UNAVAILABLE (native MBO)";
                LogLiveReadinessInfo("NATIVE_ICEBERG_CAPABILITY unavailable; absorption proxy is not an execution signal");
                return;
            }
            foreach (var record in _nativeMboProvider!.GetMboIcebergs(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow))
                if (record.NativeRefreshConfirmed && record.Hidden > 0m) _nativeIcebergHistory.Add(record.OrderId);
            _nativeMboProvider!.MboIcebergsChanged += OnNativeIcebergsChanged;
            _ = SubscribeNativeIcebergAsync();
        }
        catch (Exception ex)
        {
            _nativeIcebergHud = "Iceberg: SUBSCRIBE_FAILED";
            LogLiveReadinessInfo($"NATIVE_ICEBERG_SUBSCRIBE_FAILED {ex.GetType().Name}:{ex.Message}");
        }
    }

    private async Task SubscribeNativeIcebergAsync()
    {
        try
        {
            var provider = _nativeMboProvider;
            if (provider is null) return;
            await provider.SubscribeMboAnalyticsData();
            if (!ReferenceEquals(provider, _nativeMboProvider)) return;
            _nativeIcebergHud = $"Iceberg: {FormatMboStatus(provider)} / waiting native refresh";
            LogLiveReadinessInfo($"NATIVE_ICEBERG_CAPABILITY status={FormatMboStatus(provider)} diagnostics={provider.MboAnalyticsDiagnostics}; confirmed events required, history excluded");
        }
        catch (Exception ex)
        {
            _nativeIcebergHud = "Iceberg: SUBSCRIBE_FAILED";
            LogLiveReadinessInfo($"NATIVE_ICEBERG_SUBSCRIBE_FAILED {ex.GetType().Name}:{ex.Message}");
        }
    }

    private static string FormatMboStatus(IMboAnalyticsDataProvider provider)
    {
        var status = provider.MboAnalyticsDataStatus;
        return $"started={status.IsStarted} events={status.OrderEventsSinceStart} last={status.LastOrderEventUtc:HH:mm:ss}UTC";
    }

    private void DisposeNativeIceberg()
    {
        if (_nativeMboProvider is not null)
        {
            _nativeMboProvider.MboIcebergsChanged -= OnNativeIcebergsChanged;
            _nativeMboProvider.UnsubscribeMboAnalyticsData();
            _nativeMboProvider = null;
        }
        while (_nativeIcebergArchiveWrites.TryDequeue(out var item))
        {
            try { NativeIcebergArchive.Append(NativeIcebergArchiveDirectory, item); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { LogLiveReadinessInfo($"NATIVE_ICEBERG_ARCHIVE_FAILED {ex.GetType().Name}"); }
        }
    }

    private void OnNativeIcebergsChanged(IReadOnlyList<MboIcebergRecord> records)
    {
        var received = DateTime.UtcNow;
        foreach (var record in records)
        {
            if (record.LastMs < 0 || record.LastMs > 253402300799999L) continue;
            var time = DateTimeOffset.FromUnixTimeMilliseconds(record.LastMs).UtcDateTime;
            // The initial subscription can return previously confirmed icebergs.
            if (time < _intradayStartedUtc || received - time > TimeSpan.FromSeconds(5))
            {
                if (record.NativeRefreshConfirmed && record.Hidden > 0m)
                    lock (_intradaySync) _nativeIcebergHistory.Add(record.OrderId);
                continue;
            }
            lock (_intradaySync) if (_nativeIcebergHistory.Contains(record.OrderId)) continue;
            if ((!record.DeleteConfirmed && (!record.NativeRefreshConfirmed || record.Hidden <= 0m)) || record.Price <= 0m || record.Side == MboOrderSide.Unknown) continue;
            var item = new IcebergEvent(Security?.Code ?? "", record.OrderId, time, record.Price,
                record.Side == MboOrderSide.Bid ? TradeSide.Long : TradeSide.Short, record.Hidden, record.Filled, record.DeleteConfirmed);
            _nativeIcebergUpdates.Enqueue(item);
            _nativeIcebergArchiveWrites.Enqueue(new NativeIcebergEvent(item.Instrument, item.Id, item.Time, item.Price, item.Side == TradeSide.Long, item.Hidden, item.Filled, item.Deleted, received));
            NativeIcebergBus[item.Instrument + "|" + item.Id] = item;
        }
    }

    private static string IcebergContract(string instrument) => instrument.StartsWith("MNQ", StringComparison.OrdinalIgnoreCase)
        ? instrument[1..] : instrument.StartsWith("MES", StringComparison.OrdinalIgnoreCase) ? instrument[1..] : instrument;

    private void EvaluateNativeIceberg(OpfCandle live)
    {
        while (_nativeIcebergArchiveWrites.TryDequeue(out var archive))
        {
            try { NativeIcebergArchive.Append(NativeIcebergArchiveDirectory, archive); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { LogLiveReadinessInfo($"NATIVE_ICEBERG_ARCHIVE_FAILED {ex.GetType().Name}"); }
        }
        while (_nativeIcebergUpdates.TryDequeue(out var update)) _nativeIcebergs[update.Instrument + "|" + update.Id] = update;
        if (IsHistoricalReplayTime(live.Time))
        {
            if (_nativeIcebergReplayDay != live.Time.Date)
            {
                _nativeIcebergReplayDay = live.Time.Date;
                try { _nativeIcebergReplay = NativeIcebergArchive.Read(NativeIcebergArchiveDirectory, live.Time); }
                catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
                { _nativeIcebergReplay = Array.Empty<NativeIcebergEvent>(); LogLiveReadinessInfo($"NATIVE_ICEBERG_REPLAY_FAILED {ex.GetType().Name}"); }
                _nativeIcebergHud = _nativeIcebergReplay.Length == 0 ? "Iceberg: REPLAY_ARCHIVE_MISSING" : "Iceberg: REPLAY_ASOF";
            }
            foreach (var item in _nativeIcebergReplay.Where(x => NativeIcebergArchive.AvailableAsOf(x, live.Time) &&
                IcebergContract(x.Instrument).Equals(IcebergContract(Security?.Code ?? ""), StringComparison.OrdinalIgnoreCase)))
                _nativeIcebergs[item.Instrument + "|" + item.Id] = new IcebergEvent(item.Instrument, item.Id, item.Time, item.Price, item.IsLong ? TradeSide.Long : TradeSide.Short, item.Hidden, item.Filled, item.Deleted);
        }
        if (!IsHistoricalReplayTime(live.Time))
        {
            foreach (var pair in NativeIcebergBus)
            {
                if (DateTime.UtcNow - pair.Value.Time > TimeSpan.FromMinutes(1)) { NativeIcebergBus.TryRemove(pair.Key, out _); continue; }
                if (IcebergContract(pair.Value.Instrument).Equals(IcebergContract(Security?.Code ?? ""), StringComparison.OrdinalIgnoreCase))
                    _nativeIcebergs[pair.Key] = pair.Value;
            }
        }
        if (_pendingIcebergSwitch is { } waiting)
        {
            if (live.Time > waiting.Expires || Math.Abs(live.Close - waiting.Price) > Math.Abs(waiting.Price - waiting.Stop) * .5m ||
                (waiting.Signal.Side == TradeSide.Long ? live.Close <= waiting.Stop : live.Close >= waiting.Stop) ||
                _nativeIcebergs.TryGetValue(waiting.EventKey, out var changed) && changed.Deleted)
            {
                LogLiveReadinessInfo($"ICEBERG_SWITCH_EXPIRED event={waiting.EventKey}");
                _pendingIcebergSwitch = null;
            }
            else if ((ActiveReplayExecutions().Count == 0 && GetCurrentAccountPosition() == 0m ||
                ActiveReplayExecutions().Count == 1 && ActiveReplayExecutions().All(x => x.Side == waiting.Signal.Side && x.BracketSubmitted && IsWorkingExecutionOrder(x.StopOrder))) &&
                !_replayExecutionsByTradeId.Values.Any(x => x.ProtectionCleanupPending || x.ExitCompleted && CountWorkingProtectionOrders(x) > 0))
            {
                _pendingIcebergSwitch = null;
                SubmitNativeIceberg(waiting.Signal, live, waiting.Stop);
            }
            else ScheduleProtectionCleanupIfNeeded("IcebergSwitch");
            return;
        }
        foreach (var pair in _nativeIcebergs.OrderByDescending(x => x.Value.Time).ToArray())
        {
            var item = pair.Value;
            if (!IsHistoricalReplayTime(live.Time) && item.Time < _intradayStartedUtc) continue;
            if (live.Time < item.Time || live.Time - item.Time > TimeSpan.FromSeconds(30) || item.Deleted || _nativeIcebergConsumed.Contains(pair.Key)) continue;
            var tick = _snapshot!.InstrumentProfile.TickSize;
            var trigger = item.Price;
            var stop = item.Side == TradeSide.Long ? item.Price - tick * 4m : item.Price + tick * 4m;
            var distance = item.Side == TradeSide.Long ? live.Close - trigger : trigger - live.Close;
            var key = "ICE|" + pair.Key;
            if (distance < tick * 2m || distance > Math.Max(tick * 8m, CalculateAtr14(live) * .2m))
            { _reactionHolds.Remove(key); continue; }
            if (_reactionLastSeen.TryGetValue(key, out var lastSeen) && live.Time - lastSeen > TimeSpan.FromSeconds(1))
                _reactionHolds.Remove(key);
            _reactionLastSeen[key] = live.Time;
            if (!_reactionHolds.TryGetValue(key, out var held)) { _reactionHolds[key] = live.Time; continue; }
            if (live.Time - held < TimeSpan.FromSeconds(2)) continue;
            var risk = Math.Abs(live.Close - stop);
            var fees = _actualCommissionPerContractRoundTrip / _snapshot.InstrumentProfile.PointValue + tick * 2m;
            if (risk * .75m <= fees) continue;
            _nativeIcebergConsumed.Add(pair.Key);
            _nativeIcebergHud = $"Iceberg: CONFIRMED {item.Side} @{item.Price:0.##}";
            var score = new ScoreBreakdown("NativeIceberg", 100m, 60m, true,
                new[] { new ScoreComponent("NativeRefreshReaction", $"hidden={item.Hidden}|filled={item.Filled}|source={item.Instrument}", true, 100m, 100m, "NativeMboOnly") });
            var signal = new CandidateSignal($"ICE-{item.Instrument}-{item.Id}-{item.Time:yyyyMMddHHmmss}", _snapshot.SnapshotId, live.Time, live.Bar, item.Side,
                SetupType.TrendPullback, SignalStage.Triggered, null, score, score, new[] { "NativeRefreshConfirmed", "IntradayReaction" });
            _researchLogger?.AppendSignal(signal);
            LogLiveReadinessInfo($"NATIVE_ICEBERG_REACTION event={pair.Key} side={item.Side} price={item.Price} sourceTime={item.Time:O} reactionTime={live.Time:O} hidden={item.Hidden} filled={item.Filled}");
            var active = ActiveReplayExecutions();
            if (_manualAlertEnabled) { SubmitNativeIceberg(signal, live, stop); break; }
            var arbitration = IntradayEntryPolicy.IcebergArbitration(active.Count, active.Any(x => x.ResearchPath == "NativeIcebergReaction"),
                active.All(x => x.Side == item.Side), active.All(x => x.BracketSubmitted && IsWorkingExecutionOrder(x.StopOrder)),
                GetCurrentAccountPosition() == 0m, !_replayExecutionsByTradeId.Values.Any(x => x.ProtectionCleanupPending || x.ExitCompleted && CountWorkingProtectionOrders(x) > 0),
                active.Sum(x => x.InitialRiskPoints * _snapshot.InstrumentProfile.PointValue * RemainingExecutionQuantity(x)) + risk * _snapshot.InstrumentProfile.PointValue);
            LogLiveReadinessInfo($"ICEBERG_ARBITRATION event={pair.Key} decision={arbitration}");
            if (arbitration == "Enter") SubmitNativeIceberg(signal, live, stop);
            else if (arbitration is "FlattenThenWait" or "WaitProtection" or "WaitCleanup")
            {
                if (!ActualOrdersEnabled || _manualAlertEnabled || _liveReadinessBlocked ||
                    _dailyPnlScopeUnresolved || _manualInterventionLatch ||
                    _actualDailyLossLimitDollars > 0m && _liveAccountDailyNetPnlDollars <= -_actualDailyLossLimitDollars ||
                    ReplayMaxTradesPerDay > 0 && _replayTradesToday >= ReplayMaxTradesPerDay) continue;
                if (IsGlobexCloseoutLockWindow(live.Time, out _) || IsUsCashOpenBlackout(live.Time, out _) || IsLiveLatencyBlocked(live.Time, out _)) continue;
                _pendingIcebergSwitch = new IcebergSwitch(signal, stop, live.Close, live.Time.AddSeconds(30), pair.Key);
                if (arbitration == "FlattenThenWait")
                    foreach (var execution in active) EnqueueExecutionAction("IcebergSwitchFlatten", async () =>
                        await SubmitEmergencyFlattenAsync(execution, RemainingExecutionQuantity(execution), "NativeIcebergDirectionSwitch"));
            }
            break;
        }
    }

    private void SubmitNativeIceberg(CandidateSignal signal, OpfCandle live, decimal stop)
    {
        var risk = Math.Abs(live.Close - stop);
        var feePoints = _actualCommissionPerContractRoundTrip / _snapshot!.InstrumentProfile.PointValue + _snapshot.InstrumentProfile.TickSize * 2m;
        if (risk * .75m <= feePoints)
        { LogLiveReadinessInfo("ICEBERG_REJECT targetDoesNotCoverCostsAfterWait"); return; }
        var instant = live with { Open = live.Close, High = live.Close, Low = live.Close };
        signal = signal with { Time = live.Time, Bar = live.Bar };
        AddResearchTracker(signal, instant, "NativeIcebergReaction", stop, risk, submitReplayExecution: false);
        TrySubmitReplayExecution(signal, instant, "NativeIcebergReaction", stop, risk, allowDelayedExpansion: false);
    }

    private void ScheduleIcebergNoProgress(OpfCandle live)
    {
        foreach (var execution in ActiveReplayExecutions().Where(x => x.ResearchPath == "NativeIcebergReaction"))
        {
            if (execution.EntryFilledQty <= 0m) continue;
            var favorable = execution.Side == TradeSide.Long ? live.Close - execution.EntryAvgPrice : execution.EntryAvgPrice - live.Close;
            execution.IcebergMaxFavorable = Math.Max(execution.IcebergMaxFavorable, favorable);
            if (live.Time - execution.CreatedTime < TimeSpan.FromSeconds(45) ||
                execution.IcebergMaxFavorable >= execution.InitialRiskPoints * .25m || execution.EmergencyFlattenSubmitted) continue;
            EnqueueExecutionAction("IcebergNoProgress", async () => await SubmitEmergencyFlattenAsync(execution, RemainingExecutionQuantity(execution), "IcebergNoProgress45s"));
        }
    }
}
