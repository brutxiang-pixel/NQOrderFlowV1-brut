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
#if OPF_ATAS_X
    private sealed record MboSweepEvidence(DateTime Time, decimal Price, decimal Volume, decimal PriceRange, int PriceLevels, int TradeCount, ATAS.DataFeedsCore.TradeDirection Direction);
    private readonly ConcurrentQueue<MboSweepEvidence> _mboSweepUpdates = new();
    private readonly List<MboSweepEvidence> _recentMboSweeps = [];
#endif
    private sealed record IcebergSwitch(CandidateSignal Signal, decimal Stop, decimal Price, DateTime Expires, string EventKey);
    private static readonly ConcurrentDictionary<string, IcebergEvent> NativeIcebergBus = new();
    private IMboAnalyticsDataProvider? _nativeMboProvider;
    private readonly Dictionary<string, IcebergEvent> _nativeIcebergs = new();
    private readonly HashSet<string> _nativeIcebergConsumed = new();
    private readonly HashSet<long> _nativeIcebergHistory = new();
    private readonly ConcurrentQueue<IcebergEvent> _nativeIcebergUpdates = new();
    private string _nativeIcebergHud = "Iceberg: NOT_SUBSCRIBED";
    private IcebergSwitch? _pendingIcebergSwitch;
    private readonly Dictionary<string, IcebergStructureConfirmation> _nativeIcebergStructures = new();
    private readonly Dictionary<TradeSide, (DateTime Time, decimal Anchor)> _nativeIcebergFailures = new();
    private DateTime _nativeIcebergStructureDay;
    private readonly ConcurrentQueue<NativeIcebergEvent> _nativeIcebergArchiveWrites = new();
    private NativeIcebergEvent[] _nativeIcebergReplay = Array.Empty<NativeIcebergEvent>();
    private DateTime _nativeIcebergReplayDay;
#if OPF_ATAS_X
    [System.ComponentModel.Category("OPF Iceberg")]
    [System.ComponentModel.DisplayName("Enable Iceberg Absorption Scalp")]
    public bool EnableMnqIcebergAbsorptionScalp { get; set; }
    private sealed record ScalpSegment(NativeIcebergEvent Event, DateTime AttackAfter, DateTime AttemptedAt = default);
    private sealed class ScalpFunnel
    {
        public int ConfirmedIds, Segments, InferredSegments, Signals, Submitted, Filled;
        public readonly Dictionary<string, int> EvaluationBlocks = new();
    }
    private readonly Dictionary<string, NativeIcebergEvent> _scalpRefreshes = new();
    private readonly Dictionary<string, ScalpSegment> _scalpIcebergs = new();
    private readonly IcebergReplenishment _scalpReplenishment = new();
    private readonly Dictionary<(decimal Price, bool IsLong), DateTime> _scalpAttempts = new();
    private readonly ConcurrentDictionary<DateTime, ScalpFunnel> _scalpFunnels = new();
    private DateTime _nextScalpFunnelLog;
    private (decimal Price, DateTime At) _scalpBid, _scalpAsk;

    private ScalpFunnel ScalpFunnelFor(DateTime time) => _scalpFunnels.GetOrAdd(GlobexTradingDayKey(time), _ => new());

    private void ObserveScalpRefresh(NativeIcebergEvent update)
    {
        var key = update.Instrument + "|" + update.Id;
        _scalpRefreshes.TryGetValue(key, out var previous);
        if (previous is not null && (update.Time < previous.Time || previous.Deleted)) return;
        if (update.Deleted)
        {
            _scalpRefreshes[key] = update;
            _scalpIcebergs.Remove(key);
            return;
        }
        var funnel = ScalpFunnelFor(update.CapturedAtUtc);
        if (previous is null) Interlocked.Increment(ref funnel.ConfirmedIds);
        if (previous is null || update.Time > previous.Time && (update.Hidden > previous.Hidden || update.Filled > previous.Filled))
            _scalpRefreshes[key] = update;
        if (!update.IsFreshScalpRefreshAfter(previous)) return;
        var attackAfter = previous?.Time ?? default;
        if (_scalpIcebergs.TryGetValue(key, out var current))
        {
            if (current.AttackAfter > attackAfter) attackAfter = current.AttackAfter;
            if (current.AttemptedAt > attackAfter) attackAfter = current.AttemptedAt;
        }
        _scalpIcebergs[key] = new ScalpSegment(update, attackAfter);
        Interlocked.Increment(ref funnel.Segments);
    }

    private bool ReportScalpFunnel(OpfCandle live, bool signal, string status)
    {
        var totals = ScalpFunnelFor(live.Time);
        if (!signal && status is not ("Disabled" or "NoFreshSegment"))
            totals.EvaluationBlocks[status] = totals.EvaluationBlocks.GetValueOrDefault(status) + 1;
        if (live.Time >= _nextScalpFunnelLog)
        {
            _nextScalpFunnelLog = live.Time.AddMinutes(1);
            var blocks = string.Join(",", totals.EvaluationBlocks.OrderBy(x => x.Key).Select(x => $"{x.Key}:{x.Value}"));
            LogLiveReadinessInfo($"ICEBERG_SCALP_FUNNEL day={GlobexTradingDayKey(live.Time):yyyy-MM-dd} at={live.Time:O} product={Security?.Code} status={status} " +
                $"confirmedIds={Volatile.Read(ref totals.ConfirmedIds)} segments={Volatile.Read(ref totals.Segments)} inferredSegments={Volatile.Read(ref totals.InferredSegments)} signals={Volatile.Read(ref totals.Signals)} " +
                $"iocSubmitted={Volatile.Read(ref totals.Submitted)} entryFilled={Volatile.Read(ref totals.Filled)} evaluationBlocks={blocks}");
        }
        return signal;
    }

    private void ObserveScalpQuote(MarketDataArg quote)
    {
        if (!EnableMnqIcebergAbsorptionScalp || !IntradayEntryPolicy.IsIcebergScalpProduct(Security?.Code)) return;
        lock (_intradaySync)
        {
            if (quote.IsBid && quote.Time >= _scalpBid.At) _scalpBid = (quote.Price, quote.Time);
            if (quote.IsAsk && quote.Time >= _scalpAsk.At) _scalpAsk = (quote.Price, quote.Time);
        }
    }

    private void ObserveScalpDepth(MarketDataArg depth)
    {
        if (_isStoppingActualExecution || !EnableMnqIcebergAbsorptionScalp || depth.Time < _intradayStartedUtc ||
            !IntradayEntryPolicy.IsIcebergScalpProduct(Security?.Code) || !(depth.IsBid || depth.IsAsk)) return;
        IcebergReplenishment.Signal? signal;
        lock (_intradaySync)
            signal = _scalpReplenishment.Depth(depth.Price, depth.IsBid, depth.Volume, depth.Time, DateTime.UtcNow);
        if (signal is null) return;
        Interlocked.Increment(ref ScalpFunnelFor(signal.ConfirmedAt).InferredSegments);
        LogLiveReadinessInfo($"ICEBERG_SCALP_REPLENISHMENT product={Security?.Code} id={signal.Id} price={signal.Price} side={(signal.IsLong ? "Bid" : "Ask")} attack={signal.AttackTime:O} at={signal.ConfirmedAt:O}");
    }

    private string? ScalpQuoteBlock(DateTime now, decimal tick, out decimal bid, out decimal ask)
    {
        lock (_intradaySync)
        {
            var bestBid = BestBid;
            var bestAsk = BestAsk;
            bid = bestBid?.Price ?? 0m;
            ask = bestAsk?.Price ?? 0m;
            var bidAt = IntradayEntryPolicy.ScalpQuoteTime(bid, bestBid?.Time ?? default, _scalpBid, now);
            var askAt = IntradayEntryPolicy.ScalpQuoteTime(ask, bestAsk?.Time ?? default, _scalpAsk, now);
            return IntradayEntryPolicy.ScalpQuoteBlock(bid, ask, bidAt, askAt, now, tick);
        }
    }

    private bool EvaluateIcebergAbsorptionScalp(OpfCandle live)
    {
        var path = IntradayEntryPolicy.IcebergAbsorptionScalpPath;
        if (_snapshot is null || !IntradayEntryPolicy.IsIcebergScalpProduct(_snapshot.InstrumentProfile.Instrument) || IsHistoricalReplayTime(live.Time)) return false;
        if (!EnableMnqIcebergAbsorptionScalp) return ReportScalpFunnel(live, false, "Disabled");
        var fresh = _scalpIcebergs.Where(x => x.Value.AttemptedAt == default &&
            x.Value.Event.CapturedAtUtc <= live.Time && live.Time - x.Value.Event.CapturedAtUtc <= TimeSpan.FromSeconds(5));
        IcebergReplenishment.Signal[] inferred;
        lock (_intradaySync) inferred = _scalpReplenishment.FreshSignals(live.Time);
        if (!fresh.Any() && inferred.Length == 0) return ReportScalpFunnel(live, false, "NoFreshSegment");
        if (_pendingIcebergSwitch is not null || ActiveReplayExecutions().Count != 0 || GetCurrentAccountPosition() != 0m)
            return ReportScalpFunnel(live, false, "PositionBusy");
        var tick = _snapshot.InstrumentProfile.TickSize;
        if (ScalpQuoteBlock(live.Time, tick, out var bid, out var ask) is { } quoteBlock)
            return ReportScalpFunnel(live, false, quoteBlock);
        if (IntradayEntryPolicy.ScalpTargetTicks * tick <= _actualCommissionPerContractRoundTrip / _snapshot.InstrumentProfile.PointValue + 2m * tick)
            return ReportScalpFunnel(live, false, "FeeGuard");
        (DateTime Time, decimal Price, decimal Volume, decimal Signed)[] tape;
        lock (_intradaySync) tape = _entryTape.Where(x => x.Time > live.Time.AddSeconds(-10) && x.Time <= live.Time).ToArray();
        var status = "WaitingForTape";
        var nativeCandidates = fresh.Select(x => (Key: x.Key, Price: x.Value.Event.Price, IsLong: x.Value.Event.IsLong,
            Time: x.Value.Event.Time, Received: x.Value.Event.CapturedAtUtc, Id: x.Value.Event.Id, AttackAfter: x.Value.AttackAfter,
            Inferred: (IcebergReplenishment.Signal?)null));
        var inferredCandidates = inferred.Select(x => (Key: $"{Security?.Code}|REFILL|{x.Id}", x.Price, x.IsLong,
            Time: x.AttackTime, Received: x.ConfirmedAt, x.Id, AttackAfter: default(DateTime), Inferred: (IcebergReplenishment.Signal?)x));
        foreach (var candidate in nativeCandidates.OrderByDescending(x => x.Received).Concat(inferredCandidates.OrderByDescending(x => x.Received)))
        {
            var (key, price, isLong, time, received, id, attackAfter, replenishment) = candidate;
            var side = isLong ? TradeSide.Long : TradeSide.Short;
            if (replenishment is null && (!_nativeIcebergs.TryGetValue(key, out var latest) || latest.Deleted || latest.Price != price || latest.Side != side)) continue;
            if (_scalpAttempts.TryGetValue((price, isLong), out var attempted) && IntradayEntryPolicy.IsSameScalpAbsorption(received, attempted))
            {
                status = "DuplicateAbsorption";
                continue;
            }
            var stop = IntradayEntryPolicy.IcebergScalpStop(isLong, price, tick, time, received, live.Time, tape, attackAfter, replenishment?.RefilledAt);
            var entry = isLong ? ask : bid;
            if (stop is not { } stopPrice) continue;
            if ((isLong ? bid <= price : ask >= price) || !IntradayEntryPolicy.ScalpRiskReady(isLong, entry, stopPrice, tick))
            {
                status = "EntryPriceOrRisk";
                continue;
            }
            if (replenishment is null)
                _scalpIcebergs[key] = _scalpIcebergs[key] with { AttemptedAt = live.Time };
            else
            {
                lock (_intradaySync) if (!_scalpReplenishment.Consume(replenishment)) continue;
            }
            foreach (var expired in _scalpAttempts.Where(x => live.Time - x.Value > TimeSpan.FromSeconds(10)).Select(x => x.Key).ToArray())
                _scalpAttempts.Remove(expired);
            _scalpAttempts[(price, isLong)] = live.Time;
            Interlocked.Increment(ref ScalpFunnelFor(live.Time).Signals);
            var source = replenishment is null ? "NativeRefresh" : "InferredReplenishment";
            var detail = $"source={source}|level={price}|received={received:O}|bid={bid}|ask={ask}";
            var score = new ScoreBreakdown(path, 100m, 45m, true,
                new[] { new ScoreComponent("IcebergAndTape", detail, true, 100m, 100m, "LocalAbsorptionReclaim") });
            var signal = new CandidateSignal($"SC-{(replenishment is null ? "" : "R-")}{id}-{time:HHmmssfff}", _snapshot.SnapshotId, live.Time, live.Bar, side,
                SetupType.TrendPullback, SignalStage.Triggered, null, score, score, new[] { "IntradayReaction", $"ICE_EVENT_KEY:{key}|{time.Ticks}" });
            LogLiveReadinessInfo($"ICEBERG_ABSORPTION_SCALP signal={signal.SignalId} product={Security?.Code} side={side} {detail} stop={stopPrice}");
            _researchLogger?.AppendSignal(signal);
            TrySubmitReplayExecution(signal, live with { Open = entry, High = entry, Low = entry, Close = entry }, path, stopPrice,
                Math.Abs(entry - stopPrice), allowDelayedExpansion: false);
            return ReportScalpFunnel(live, true, "Signal");
        }
        return ReportScalpFunnel(live, false, status);
    }
#endif
    private string NativeIcebergArchiveDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        OPFStrategyV1.Core.Versions.StrategyVersions.PlatformDataDirectory, "StrategyLogs", "OPFStrategyV1", "NativeIcebergArchive");

    private void InitializeNativeIceberg()
    {
#if OPF_ATAS_X
        _scalpIcebergs.Clear();
        _scalpRefreshes.Clear();
        lock (_intradaySync) _scalpBid = _scalpAsk = default;
#endif
        _nativeIcebergs.Clear();
        _nativeIcebergConsumed.Clear();
        _nativeIcebergHistory.Clear();
        _pendingIcebergSwitch = null;
        _nativeIcebergStructures.Clear();
        _nativeIcebergFailures.Clear();
        _nativeIcebergStructureDay = default;
        _nativeIcebergReplayDay = default;
        while (_nativeIcebergUpdates.TryDequeue(out _)) { }
#if OPF_ATAS_X
        while (_mboSweepUpdates.TryDequeue(out _)) { }
        _recentMboSweeps.Clear();
#endif
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
#if OPF_ATAS_X
            _nativeMboProvider.MboAnnotatedTradesEmitted += OnNativeMboAnnotatedTrades;
#endif
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
#if OPF_ATAS_X
            _nativeMboProvider.MboAnnotatedTradesEmitted -= OnNativeMboAnnotatedTrades;
#endif
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

#if OPF_ATAS_X
    private void OnNativeMboAnnotatedTrades(IReadOnlyList<MboAnnotatedTrade> trades)
    {
        foreach (var trade in trades)
        {
            if (trade.Time < _intradayStartedUtc || trade.Time > DateTime.UtcNow.AddSeconds(1) ||
                trade.Price <= 0m || trade.AggressorEventVolume <= 0m || trade.AggressorPriceLevels < 2 ||
                trade.AggressorPriceRange <= 0m || trade.Direction == ATAS.DataFeedsCore.TradeDirection.Between)
                continue;
            _mboSweepUpdates.Enqueue(new MboSweepEvidence(trade.Time, trade.AggressorLastPrice,
                trade.AggressorEventVolume, trade.AggressorPriceRange, trade.AggressorPriceLevels,
                trade.AggressorTradeCount, trade.Direction));
        }
    }
#endif

    private static string IcebergContract(string instrument) => instrument.StartsWith("MNQ", StringComparison.OrdinalIgnoreCase)
        ? instrument[1..] : instrument.StartsWith("MES", StringComparison.OrdinalIgnoreCase) ? instrument[1..] : instrument;

    private static bool IsNativeIcebergExecutionProduct(string? productCode) =>
        productCode?.StartsWith("NQ", StringComparison.OrdinalIgnoreCase) == true ||
        productCode?.StartsWith("MNQ", StringComparison.OrdinalIgnoreCase) == true;

    private void EvaluateNativeIceberg(OpfCandle live)
    {
        var day = GlobexTradingDayKey(live.Time);
        if (day != _nativeIcebergStructureDay)
        {
#if OPF_ATAS_X
            _scalpIcebergs.Clear();
            _scalpRefreshes.Clear();
#endif
            _nativeIcebergStructureDay = day;
            _nativeIcebergStructures.Clear();
            _nativeIcebergFailures.Clear();
        }
        foreach (var key in _nativeIcebergStructures.Keys.Where(key =>
            !_nativeIcebergs.TryGetValue(key, out var item) || live.Time - item.Time > TimeSpan.FromSeconds(90)).ToArray())
            _nativeIcebergStructures.Remove(key);
        while (_nativeIcebergArchiveWrites.TryDequeue(out var archive))
        {
#if OPF_ATAS_X
            if (GlobexTradingDayKey(archive.CapturedAtUtc) == day &&
                IntradayEntryPolicy.IsLocalIcebergScalp(Security?.Code ?? "", archive.Instrument))
                ObserveScalpRefresh(archive);
#endif
            try { NativeIcebergArchive.Append(NativeIcebergArchiveDirectory, archive); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { LogLiveReadinessInfo($"NATIVE_ICEBERG_ARCHIVE_FAILED {ex.GetType().Name}"); }
        }
        while (_nativeIcebergUpdates.TryDequeue(out var update)) _nativeIcebergs[update.Instrument + "|" + update.Id] = update;
#if OPF_ATAS_X
        if (EvaluateIcebergAbsorptionScalp(live)) return;
        while (_mboSweepUpdates.TryDequeue(out var sweep)) _recentMboSweeps.Add(sweep);
        _recentMboSweeps.RemoveAll(x => live.Time < x.Time || live.Time - x.Time > TimeSpan.FromSeconds(10));
#endif
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
                if (DateTime.UtcNow - pair.Value.Time > TimeSpan.FromSeconds(90)) { NativeIcebergBus.TryRemove(pair.Key, out _); continue; }
                if (IcebergContract(pair.Value.Instrument).Equals(IcebergContract(Security?.Code ?? ""), StringComparison.OrdinalIgnoreCase))
                    _nativeIcebergs[pair.Key] = pair.Value;
            }
        }
        if (_pendingIcebergSwitch is { } waiting)
        {
            if (!IsNativeIcebergExecutionProduct(Security?.Code))
            {
                LogLiveReadinessInfo($"ICEBERG_ARBITRATION_REJECT_UNSUPPORTED_PRODUCT product={Security?.Code ?? "-"} event={waiting.EventKey}");
                _pendingIcebergSwitch = null;
                return;
            }
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
                SubmitNativeIceberg(waiting.Signal, live, waiting.Stop, waiting.EventKey);
            }
            else ScheduleProtectionCleanupIfNeeded("IcebergSwitch");
            return;
        }
        foreach (var pair in _nativeIcebergs.OrderByDescending(x => x.Value.Time).ToArray())
        {
            var item = pair.Value;
            if (!IsHistoricalReplayTime(live.Time) && item.Time < _intradayStartedUtc) continue;
            if (live.Time < item.Time || live.Time - item.Time > TimeSpan.FromSeconds(90) || item.Deleted || _nativeIcebergConsumed.Contains(pair.Key)) continue;
#if OPF_ATAS_X
            if (EnableMnqIcebergAbsorptionScalp && _scalpIcebergs.TryGetValue(pair.Key, out var scalpSegment) &&
                scalpSegment.Event.CapturedAtUtc <= live.Time && live.Time - scalpSegment.Event.CapturedAtUtc <= TimeSpan.FromSeconds(5)) continue;
#endif
            if (!NativeIcebergStructureReady(pair.Key, item, live)) continue;
            var tick = _snapshot!.InstrumentProfile.TickSize;
            var trigger = item.Price;
            var distance = item.Side == TradeSide.Long ? live.Close - trigger : trigger - live.Close;
            var key = "ICE|" + pair.Key;
            if (distance < tick * 2m || distance > Math.Max(tick * 8m, CalculateAtr14(live) * .2m))
            { _reactionHolds.Remove(key); continue; }
            if (_reactionLastSeen.TryGetValue(key, out var lastSeen) && live.Time - lastSeen > TimeSpan.FromSeconds(1))
                _reactionHolds.Remove(key);
            _reactionLastSeen[key] = live.Time;
            if (!_reactionHolds.TryGetValue(key, out var held)) { _reactionHolds[key] = live.Time; continue; }
            if (live.Time - held < TimeSpan.FromSeconds(2)) continue;
            (DateTime Time, decimal Price, decimal Volume, decimal Signed)[] tape;
            lock (_intradaySync) tape = _entryTape.Where(x => x.Time <= live.Time && x.Time >= live.Time.AddSeconds(-10)).ToArray();
            var signedVolume = tape.Sum(x => x.Signed);
            var volume = tape.Sum(x => x.Volume);
            var tapeRatio = volume > 0m ? (item.Side == TradeSide.Long ? signedVolume : -signedVolume) / volume : 0m;
            if (volume <= 0m || (item.Side == TradeSide.Long ? signedVolume : -signedVolume) / volume < .15m)
            {
                _reactionHolds.Remove(key);
                if (_reactionAuditKeys.Add($"ICE_FLOW|{pair.Key}|{live.Time:yyyyMMddHHmmss}"))
                    LogLiveReadinessInfo($"NATIVE_ICEBERG_WAIT_LOCAL_FLOW event={pair.Key} side={item.Side} signed={signedVolume:0.##} volume={volume:0.##}");
                continue;
            }
            var structure = _nativeIcebergStructures[pair.Key];
#if OPF_ATAS_X
            var nearestSweep = _recentMboSweeps
                .Where(x => x.Time <= live.Time && live.Time - x.Time <= TimeSpan.FromSeconds(10) &&
                    Math.Abs(x.Price - item.Price) <= Math.Max(tick * 8m, CalculateAtr14(live) * .2m))
                .OrderByDescending(x => x.Time)
                .FirstOrDefault();
            var sweepAligned = nearestSweep is not null &&
                (item.Side == TradeSide.Long
                    ? nearestSweep.Direction == ATAS.DataFeedsCore.TradeDirection.Buy
                    : nearestSweep.Direction == ATAS.DataFeedsCore.TradeDirection.Sell);
#endif
            var stop = IntradayEntryPolicy.IcebergStructuralStop(item.Side == TradeSide.Long, live.Close, item.Price,
                structure.FrozenLow, structure.FrozenHigh, structure.FrozenAtr, tick);
            var risk = Math.Abs(live.Close - stop);
            var fees = _actualCommissionPerContractRoundTrip / _snapshot.InstrumentProfile.PointValue + tick * 2m;
            if (risk * IntradayEntryPolicy.IcebergTargetR <= fees) continue;
            LogLiveReadinessInfo($"ICEBERG_STRUCTURE_STOP_V219 event={pair.Key} iceberg={item.Price} stop={stop} risk={risk} low={structure.FrozenLow} high={structure.FrozenHigh} atr={structure.FrozenAtr}");
            _nativeIcebergConsumed.Add(pair.Key);
            _nativeIcebergHud = $"Iceberg: CONFIRMED {item.Side} @{item.Price:0.##}";
            var es = ReadEsOptionsProxyAsOf(live.Time);
            var optionContext = es.Status == "ModelReady" && es.CallWallProxy.HasValue && es.PutWallProxy.HasValue &&
                es.CapturedAtUtc <= live.Time && live.Time - es.CapturedAtUtc <= TimeSpan.FromSeconds(90) &&
                es.OptionQuotesObservedAtUtc is { } optionAt && optionAt <= live.Time && live.Time - optionAt <= TimeSpan.FromSeconds(90) &&
                es.UnderlyingObservedAtUtc is { } underlyingAt && underlyingAt <= live.Time && live.Time - underlyingAt <= TimeSpan.FromSeconds(90) &&
                es.AtmQuotesReady == true && es.LocalQuoteCoverage >= .5m && es.CallQuoteCoverage >= .5m && es.PutQuoteCoverage >= .5m;
            var esReaction = es.UnderlyingObservedAtUtc is { } esObserved && esObserved <= live.Time && live.Time - esObserved <= TimeSpan.FromSeconds(90) &&
                es.PriceReferenceAtUtc is { } esReference && esReference <= live.Time && live.Time - esReference <= TimeSpan.FromSeconds(90) &&
                es.UnderlyingPrice.HasValue && es.UnderlyingPrice60sAgo.HasValue &&
                (item.Side == TradeSide.Long ? es.UnderlyingPrice > es.UnderlyingPrice60sAgo : es.UnderlyingPrice < es.UnderlyingPrice60sAgo);
            var evidenceScore = IntradayEntryPolicy.EvidenceScore(optionContext, esReaction, true);
            var configuredQuantity = ConfiguredExecutionQuantity;
            _nativeIcebergHud = $"Iceberg: {item.Side} S{evidenceScore}/3 x{configuredQuantity:0}";
            var score = new ScoreBreakdown("CrossMarketIceberg", 100m, 45m, true,
                new[]
                {
                    new ScoreComponent("NqNativeIceberg", $"hidden={item.Hidden}|filled={item.Filled}|source={item.Instrument}", true, 100m, 100m, "NativeMboOnly"),
                    new ScoreComponent("NqLocalOrderFlow", $"signed={signedVolume:0.##}|volume={volume:0.##}|ratio={tapeRatio:0.000}", true, 100m, 100m, "10sTape"),
#if OPF_ATAS_X
                    new ScoreComponent("NativeMboSweep", nearestSweep is null ? "none" : $"side={nearestSweep.Direction}|qty={nearestSweep.Volume:0.##}|levels={nearestSweep.PriceLevels}|range={nearestSweep.PriceRange:0.##}|trades={nearestSweep.TradeCount}|aligned={sweepAligned}", nearestSweep is not null, sweepAligned ? 100m : 0m, sweepAligned ? 100m : 0m, "MboAnnotatedTradesEmitted"),
#endif
                    new ScoreComponent("EsOptionsContext", $"status={es.Status}|coverage={es.Coverage:0.000}|captured={es.CapturedAtUtc:O}", optionContext, optionContext ? 100m : 0m, optionContext ? 100m : 0m, "OptionalSizingEvidence"),
                    new ScoreComponent("EsPriceReaction", $"current={es.UnderlyingPrice}|m1={es.UnderlyingPrice60sAgo}", esReaction, esReaction ? 100m : 0m, esReaction ? 100m : 0m, "OptionalSizingEvidence")
                });
            var eventInstrument = IcebergContract(item.Instrument);
            var signal = new CandidateSignal($"ICE-{item.Instrument}-{item.Id}-{item.Time:yyyyMMddHHmmss}", _snapshot.SnapshotId, live.Time, live.Bar, item.Side,
                SetupType.TrendPullback, SignalStage.Triggered, null, score, score, new[] { "NativeRefreshConfirmed", "IntradayReaction", "CrossMarketIcebergOrderFlowReaction", $"ICE_EVENT_KEY:{eventInstrument}|{item.Id}|{item.Time:O}", $"ConfiguredQty:{configuredQuantity.ToString(System.Globalization.CultureInfo.InvariantCulture)}" });
            var exitStructure = item.Side == TradeSide.Long ? Math.Min(item.Price, structure.FrozenLow) : Math.Max(item.Price, structure.FrozenHigh);
            signal = signal with { SkipReasons = signal.SkipReasons.Concat(new[] { $"ICE_EXIT_STRUCTURE:{exitStructure.ToString(System.Globalization.CultureInfo.InvariantCulture)}" }).ToArray() };
            _researchLogger?.AppendSignal(signal);
            var sweepDetail = string.Empty;
#if OPF_ATAS_X
            if (nearestSweep is not null)
                sweepDetail = $" mboSweepAligned={sweepAligned} mboSweepTime={nearestSweep.Time:O} mboSweepLevels={nearestSweep.PriceLevels}";
#endif
            LogLiveReadinessInfo($"NATIVE_ICEBERG_REACTION event={pair.Key} side={item.Side} price={item.Price} sourceTime={item.Time:O} reactionTime={live.Time:O} hidden={item.Hidden} filled={item.Filled} tapeRatio={tapeRatio:0.000}{sweepDetail} score={evidenceScore}/3 qty={configuredQuantity} esStatus={es.Status}");
            var active = ActiveReplayExecutions();
            if (_manualAlertEnabled) { SubmitNativeIceberg(signal, live, stop, pair.Key); break; }
            var arbitration = IntradayEntryPolicy.IcebergArbitration(active.Count, active.Any(x => IsIcebergExecutionPath(x.ResearchPath)),
                active.All(x => x.Side == item.Side), active.All(x => x.BracketSubmitted && IsWorkingExecutionOrder(x.StopOrder)),
                GetCurrentAccountPosition() == 0m, !_replayExecutionsByTradeId.Values.Any(x => x.ProtectionCleanupPending || x.ExitCompleted && CountWorkingProtectionOrders(x) > 0),
                active.Sum(x => x.InitialRiskPoints * _snapshot.InstrumentProfile.PointValue * RemainingExecutionQuantity(x)) + risk * _snapshot.InstrumentProfile.PointValue * configuredQuantity);
            LogLiveReadinessInfo($"ICEBERG_ARBITRATION event={pair.Key} decision={arbitration}");
            if (arbitration == "Enter") SubmitNativeIceberg(signal, live, stop, pair.Key);
            else if (arbitration is "FlattenThenWait" or "WaitProtection" or "WaitCleanup")
            {
                var expectedPosition = active.Sum(x => (x.Side == TradeSide.Long ? 1m : -1m) * RemainingExecutionQuantity(x));
                if (GetCurrentAccountPosition() != expectedPosition) continue;
                var permitted = false;
                var instant = live with { Open = live.Close, High = live.Close, Low = live.Close };
                TrySubmitReplayExecution(signal, instant, "CrossMarketIcebergOrderFlowReaction", stop, risk,
                    allowDelayedExpansion: false, switchPreflightPassed: () => permitted = true);
                if (!permitted)
                {
                    LogLiveReadinessInfo($"ICEBERG_SWITCH_PREFLIGHT_REJECT event={pair.Key} oldPositionRetained=true");
                    continue;
                }
                _pendingIcebergSwitch = new IcebergSwitch(signal, stop, live.Close, live.Time.AddSeconds(30), pair.Key);
                if (arbitration == "FlattenThenWait")
                {
                    foreach (var execution in active) EnqueueExecutionAction("IcebergSwitchFlatten", async () =>
                        await SubmitIcebergSwitchExitAsync(execution, live.Close));
                }
            }
            break;
        }
    }

    private void SubmitNativeIceberg(CandidateSignal signal, OpfCandle live, decimal stop, string eventKey)
    {
        // Cleanup may delay a direction switch: recheck the actual event and structure.
        if (!_nativeIcebergs.TryGetValue(eventKey, out var item) || item.Deleted || live.Time < item.Time ||
            live.Time - item.Time > TimeSpan.FromSeconds(90) || !NativeIcebergStructureReady(eventKey, item, live))
        {
            LogLiveReadinessInfo($"ICEBERG_REJECT_V216 event={eventKey} reason=StructureOrEventChangedAfterWait");
            return;
        }
        var product = (Security?.Code ?? string.Empty).ToUpperInvariant();
        if (!IsNativeIcebergExecutionProduct(product))
        {
            LogLiveReadinessInfo($"ICEBERG_EXECUTION_UNSUPPORTED_PRODUCT product={product}");
            return;
        }
        var risk = Math.Abs(live.Close - stop);
        var feePoints = _actualCommissionPerContractRoundTrip / _snapshot!.InstrumentProfile.PointValue + _snapshot.InstrumentProfile.TickSize * 2m;
        if (risk * IntradayEntryPolicy.IcebergTargetR <= feePoints)
        { LogLiveReadinessInfo("ICEBERG_REJECT targetDoesNotCoverCostsAfterWait"); return; }
        var instant = live with { Open = live.Close, High = live.Close, Low = live.Close };
        signal = signal with { Time = live.Time, Bar = live.Bar };
        var path = signal.SkipReasons.Contains("CrossMarketIcebergOrderFlowReaction", StringComparer.Ordinal) || signal.SignalId.StartsWith("ICE-", StringComparison.Ordinal)
            ? "CrossMarketIcebergOrderFlowReaction" : "NativeIcebergReaction";
        AddResearchTracker(signal, instant, path, stop, risk, submitReplayExecution: false);
        TrySubmitReplayExecution(signal, instant, path, stop, risk, allowDelayedExpansion: false);
    }

    private async Task SubmitIcebergSwitchExitAsync(ReplayExecutionState execution, decimal reference)
    {
        if (execution.ExitCompleted || execution.TimeStopExitPendingV208 || execution.EmergencyFlattenSubmitted ||
            Portfolio is null || Security is null) return;
        var position = GetCurrentAccountPosition();
        if (Math.Sign(position) != (execution.Side == TradeSide.Long ? 1 : -1)) return;
        var quantity = Math.Min(Math.Abs(position), RemainingExecutionQuantity(execution));
        if (quantity <= 0m) return;
        execution.TimeStopReason = "NativeIcebergDirectionSwitch";
        execution.TimeStopReferencePriceV209 = reference;
        execution.TimeStopExitPendingV208 = true;
        var order = new Order
        {
            Portfolio = Portfolio, Security = Security, Type = OrderTypes.Market,
            Direction = execution.Side == TradeSide.Long ? OrderDirections.Sell : OrderDirections.Buy,
            QuantityToFill = quantity, TimeInForce = ReplayTimeInForce,
            Comment = $"OPF|{execution.TradeId}|TIME_STOP|{execution.TimeStopReason}", AutoCancel = false
        };
        execution.TimeStopOrderV208 = order;
        AppendExecutionEvent(execution, "STRATEGY_SWITCH_EXIT_SEND", "TIME_STOP", reference, quantity, execution.TimeStopReason);
        try { await OpenOrderAsync(order); }
        catch
        {
            execution.TimeStopExitPendingV208 = false;
            _pendingIcebergSwitch = null;
            throw;
        }
    }

    private bool NativeIcebergStructureReady(string key, IcebergEvent item, OpfCandle live)
    {
        string reason;
        var hasFailure = _nativeIcebergFailures.TryGetValue(item.Side, out var failure);
        if (hasFailure && item.Time <= failure.Time) reason = "WAIT_FRESH_EVENT_AFTER_LOSS";
        else
        {
            if (!_nativeIcebergStructures.TryGetValue(key, out var confirmation))
            {
                (DateTime Time, decimal Price, decimal Volume, decimal Signed)[] before;
                lock (_intradaySync) before = _entryTape.Where(x => x.Time < item.Time && x.Time >= item.Time.AddSeconds(-60)).ToArray();
                if (before.Length < 2 || before[^1].Time - before[0].Time < TimeSpan.FromSeconds(5))
                {
                    LogNativeIcebergStructureWait(key, "WAIT_PRE_EVENT_CONTEXT", item.Side);
                    return false;
                }
                var isLong = item.Side == TradeSide.Long;
                var atr = CalculateAtr14(live);
                var opposed = IcebergStructureConfirmation.CounterTrend(isLong, before[0].Price, before[^1].Price,
                    atr, _snapshot!.InstrumentProfile.TickSize);
                var anchor = opposed ? (isLong ? before.Max(x => x.Price) : before.Min(x => x.Price)) : item.Price;
                if (hasFailure) anchor = isLong ? Math.Max(anchor, failure.Anchor) : Math.Min(anchor, failure.Anchor);
                RefreshCompletedContext(item.Time);
                var low = before.Min(x => x.Price);
                var high = before.Max(x => x.Price);
                if (_contextSeries.TryGetValue("5M", out var fiveMinute) && fiveMinute.IsLoaded)
                {
                    var closed = fiveMinute.Candles.ToArray().Where(x => x.Time.AddMinutes(5) <= item.Time).OrderBy(x => x.Time).LastOrDefault();
                    if (closed is not null) { low = Math.Min(low, closed.Low); high = Math.Max(high, closed.High); }
                }
                confirmation = new IcebergStructureConfirmation(isLong, anchor, true)
                {
                    FrozenLow = low, FrozenHigh = high, FrozenAtr = atr,
                    TrendAligned = IntradayEntryPolicy.IcebergTrendAligned(isLong, _completedFrames)
                };
                _nativeIcebergStructures[key] = confirmation;
                LogLiveReadinessInfo($"ICEBERG_STRUCTURE_PLAN_V216 event={key} side={item.Side} counterTrend={opposed} previousLoss={hasFailure} anchor={anchor} contextFrom={before[0].Time:O} contextTo={before[^1].Time:O}");
            }
            reason = confirmation.Observe(live.Close, _snapshot!.InstrumentProfile.TickSize, live.Time);
            if (IcebergStructureConfirmation.Ready(reason)) return true;
        }
        LogNativeIcebergStructureWait(key, reason, item.Side);
        return false;
    }

    private void LogNativeIcebergStructureWait(string key, string reason, TradeSide side)
    {
        _nativeIcebergHud = $"Iceberg: {side} {reason}";
        if (_reactionAuditKeys.Add($"ICE_STRUCTURE|{key}|{reason}"))
            LogLiveReadinessInfo($"ICEBERG_STRUCTURE_WAIT_V216 event={key} side={side} reason={reason}");
    }

    private void MarkNativeIcebergFailed(ReplayExecutionState execution)
    {
        if (execution.ResearchPath == IntradayEntryPolicy.IcebergAbsorptionScalpPath) return;
        var time = execution.ExitTime ?? execution.CreatedTime;
        if (_nativeIcebergFailures.TryGetValue(execution.Side, out var previous) && previous.Time >= time) return;
        (DateTime Time, decimal Price, decimal Volume, decimal Signed)[] before;
        lock (_intradaySync) before = _entryTape.Where(x => x.Time <= time && x.Time >= time.AddSeconds(-30)).ToArray();
        var entry = execution.EntryAvgPrice;
        var anchor = execution.Side == TradeSide.Long
            ? Math.Max(entry, before.Length == 0 ? entry : before.Max(x => x.Price))
            : Math.Min(entry, before.Length == 0 ? entry : before.Min(x => x.Price));
        if (_nativeIcebergFailures.TryGetValue(execution.Side, out previous))
            anchor = execution.Side == TradeSide.Long ? Math.Max(anchor, previous.Anchor) : Math.Min(anchor, previous.Anchor);
        _nativeIcebergFailures[execution.Side] = (time, anchor);
        _nativeIcebergStructures.Clear();
        LogLiveReadinessInfo($"ICEBERG_FAILURE_REBUILD_V216 trade={execution.TradeId} side={execution.Side} anchor={anchor} time={time:O}");
    }

    private void ObserveNativeIcebergStructure(OpfCandle live)
    {
        foreach (var pair in _nativeIcebergs.ToArray())
        {
            var item = pair.Value;
            if (item.Deleted || item.Time > live.Time || live.Time - item.Time > TimeSpan.FromSeconds(90)) continue;
            NativeIcebergStructureReady(pair.Key, item, live);
        }
    }

    private static decimal SplitBaseQuantity(ReplayExecutionState execution) =>
        execution.IcebergSplit ? 1m : ZoneBirthSplitRunnerV172BaseQuantity;

    private void ScheduleIcebergProtectionV219(decimal marketPrice)
    {
        if (_snapshot is null || marketPrice <= 0m) return;
        foreach (var execution in ActiveReplayExecutions().Where(x => IsIcebergExecutionPath(x.ResearchPath) &&
            x.ResearchPath != IntradayEntryPolicy.IcebergAbsorptionScalpPath && x.BracketSubmitted && !x.IcebergProtectionPending))
        {
            if (!execution.ProtectionObservationStarted) continue;
            var isLong = execution.Side == TradeSide.Long;
            var entry = execution.EntryAvgPrice;
            var mfe = isLong ? execution.ProtectiveObservedHigh - entry : entry - execution.ProtectiveObservedLow;
            var time = CurrentBar > 0 && GetCandle(CurrentBar - 1) is { } current ? IntradayDecisionTime(current) : execution.CreatedTime;
            (DateTime Time, decimal Price, decimal Volume, decimal Signed)[] tape;
            lock (_intradaySync) tape = _entryTape.Where(x => x.Time >= execution.EntryFillTime && x.Time <= time && x.Time >= time.AddSeconds(-30)).ToArray();
            var feeStop = BreakEvenPolicy.Stop(entry, isLong, _actualCommissionPerContractRoundTrip,
                _snapshot.InstrumentProfile.PointValue, _snapshot.InstrumentProfile.TickSize);
            var stop = IntradayEntryPolicy.IcebergRunnerStop(isLong, entry, execution.InitialRiskPoints, mfe, feeStop,
                tape.Length == 0 ? 0m : tape.Min(x => x.Price), tape.Length == 0 ? 0m : tape.Max(x => x.Price),
                _lastResearchCandle is { } last ? CalculateAtr14(last) : execution.InitialRiskPoints, _snapshot.InstrumentProfile.TickSize);
            var reference = CurrentExecutionMarketReference(marketPrice);
            if (stop <= 0m || (isLong ? reference - stop : stop - reference) < _snapshot.InstrumentProfile.TickSize) continue;
            var legs = new[] { (Runner: false, Order: execution.StopOrder), (Runner: true, Order: execution.RunnerStopOrder) }
                .Where(x => IsWorkingExecutionOrder(x.Order) &&
                    (!execution.IcebergSplit ? !x.Runner : x.Runner ? execution.RunnerExitFilledQty < 1m : execution.BaseExitFilledQty < 1m) &&
                    (isLong ? stop > x.Order!.TriggerPrice : stop < x.Order!.TriggerPrice)).ToArray();
            if (legs.Length == 0) continue;
            execution.IcebergProtectionPending = true;
            EnqueueExecutionAction("IcebergProtectionV219", async () =>
            {
                try
                {
                    foreach (var leg in legs)
                    {
                        var order = leg.Runner ? execution.RunnerStopOrder : execution.StopOrder;
                        if (execution.ExitCompleted || !IsWorkingExecutionOrder(order) ||
                            execution.IcebergSplit && (leg.Runner ? execution.RunnerExitFilledQty >= 1m : execution.BaseExitFilledQty >= 1m)) continue;
                        var latest = CurrentExecutionMarketReference(marketPrice);
                        if ((isLong ? latest - stop : stop - latest) < _snapshot.InstrumentProfile.TickSize ||
                            !(isLong ? stop > order!.TriggerPrice : stop < order!.TriggerPrice)) continue;
                        var replacement = order!.Clone();
                        replacement.TriggerPrice = stop;
                        execution.IcebergProtectionModifyFailed = false;
                        var role = leg.Runner ? "RUNNER_SL" : execution.IcebergSplit ? "BASE_SL" : "SL";
                        AppendExecutionEvent(execution, "ICEBERG_PROTECT_MODIFY_SEND_V219", role, stop, 1m, $"mfe={mfe}|risk={execution.InitialRiskPoints}");
                        await ModifyOrderAsync(order, replacement);
                        if (execution.ExitCompleted || execution.IcebergProtectionModifyFailed) continue;
                        if (leg.Runner)
                        {
                            execution.RunnerStop = stop; execution.RunnerStopOrder = replacement;
                            execution.RunnerBreakEvenApplied = true;
                        }
                        else
                        {
                            execution.Stop = stop; execution.StopOrder = replacement;
                            execution.ProtectBreakEvenAppliedV186 = true;
                        }
                        AppendExecutionEvent(execution, "ICEBERG_PROTECT_MODIFIED_V219", role, stop, 1m, "structure-and-stage");
                    }
                }
                finally { execution.IcebergProtectionPending = false; }
            });
        }
    }

    private void ScheduleIcebergNoProgress(OpfCandle live)
    {
        foreach (var execution in ActiveReplayExecutions().Where(x => IsIcebergExecutionPath(x.ResearchPath)))
        {
            if (execution.EntryFilledQty <= 0m) continue;
            execution.IcebergObservedSince ??= live.Time;
            var favorable = execution.Side == TradeSide.Long ? live.Close - execution.EntryAvgPrice : execution.EntryAvgPrice - live.Close;
            execution.IcebergMaxFavorable = Math.Max(execution.IcebergMaxFavorable, favorable);
            var scalp = execution.ResearchPath == IntradayEntryPolicy.IcebergAbsorptionScalpPath;
            if (scalp && (execution.EntryFillTime == default || !execution.BracketSubmitted ||
                !IntradayEntryPolicy.ScalpNoProgress(live.Time - execution.EntryFillTime,
                    Math.Max(execution.IcebergMaxFavorable, execution.ActualMfePoints),
                    _snapshot!.InstrumentProfile.TickSize))) continue;
            if (!scalp)
            {
                (DateTime Time, decimal Price, decimal Volume, decimal Signed)[] tape;
                lock (_intradaySync) tape = _entryTape.Where(x => x.Time <= live.Time && x.Time >= live.Time.AddSeconds(-10)).ToArray();
                if (!IntradayEntryPolicy.IcebergNoProgressExit(execution.Side == TradeSide.Long, live.Time - execution.IcebergObservedSince.Value,
                    execution.IcebergMaxFavorable, execution.InitialRiskPoints, live.Close, execution.IcebergExitStructure,
                    tape.Sum(x => x.Signed), tape.Sum(x => x.Volume)))
                { execution.IcebergFailureSince = null; continue; }
            }
            if (execution.IcebergFailureLastObserved is not { } lastObserved || live.Time - lastObserved > TimeSpan.FromSeconds(1))
                execution.IcebergFailureSince = live.Time;
            execution.IcebergFailureLastObserved = live.Time;
            execution.IcebergFailureSince ??= live.Time;
            if ((!scalp && live.Time - execution.IcebergFailureSince.Value < TimeSpan.FromSeconds(5)) ||
                execution.TimeStopExitPendingV208 || execution.EmergencyFlattenSubmitted) continue;
            execution.TimeStopExitPendingV208 = true;
            execution.TimeStopReason = scalp ? "IcebergAbsorptionScalpNoProgress20s" : "IcebergNoProgressStructureFailed";
            execution.TimeStopReferencePriceV209 = live.Close;
            AppendExecutionEvent(execution, "ICEBERG_STRUCTURE_EXIT_TRIGGERED_V214", "TIME_STOP", live.Close, RemainingExecutionQuantity(execution),
                $"elapsed={(live.Time - execution.CreatedTime).TotalSeconds:0.0}|structure={execution.IcebergExitStructure}|mfe={execution.IcebergMaxFavorable}");
            EnqueueExecutionAction("IcebergStructureExit", async () =>
            {
                if (execution.ExitCompleted) return;
                if (scalp && (execution.ProtectiveExitCallbackPendingV187 || !IsWorkingExecutionOrder(execution.StopOrder) ||
                    !IsWorkingExecutionOrder(execution.TargetOrder)))
                {
                    execution.TimeStopExitPendingV208 = false;
                    return;
                }
                var position = GetCurrentAccountPosition();
                var qty = Math.Min(Math.Abs(position), RemainingExecutionQuantity(execution));
                if (Portfolio is null || Security is null || qty <= 0m || Math.Sign(position) != (execution.Side == TradeSide.Long ? 1 : -1))
                { execution.TimeStopExitPendingV208 = false; return; }
                var order = new Order { Portfolio = Portfolio, Security = Security, Type = OrderTypes.Market,
                    Direction = execution.Side == TradeSide.Long ? OrderDirections.Sell : OrderDirections.Buy,
                    QuantityToFill = qty, TimeInForce = ReplayTimeInForce,
                    Comment = $"OPF|{execution.TradeId}|TIME_STOP|{execution.TimeStopReason}", AutoCancel = false };
                execution.TimeStopOrderV208 = order;
                AppendExecutionEvent(execution, "ICEBERG_STRUCTURE_EXIT_SEND_V214", "TIME_STOP", live.Close, qty, execution.TimeStopReason);
                try { await OpenOrderAsync(order); }
                catch { execution.TimeStopExitPendingV208 = false; AppendExecutionEvent(execution, "ICEBERG_STRUCTURE_EXIT_REJECTED_V214", "TIME_STOP", live.Close, qty, "protectionRetained"); throw; }
            });
        }
    }

    private static bool IsIcebergExecutionPath(string path) => path is "NativeIcebergReaction" or "CrossMarketIcebergOrderFlowReaction" or IntradayEntryPolicy.IcebergAbsorptionScalpPath;

    private EsOptionsProxySnapshot ReadEsOptionsProxyAsOf(DateTime time)
    {
        if (IsHistoricalReplayTime(time))
        {
            try { return ReadEsOptionsProxyArchiveAsOf(DateTime.SpecifyKind(time, DateTimeKind.Utc)); }
            catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
            { return EsOptionsProxySnapshot.Unavailable($"replay:{ex.GetType().Name}"); }
        }
        lock (_esOptionsProxySync) return _esOptionsProxy;
    }

}
