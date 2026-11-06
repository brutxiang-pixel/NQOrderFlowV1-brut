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
    private readonly Dictionary<string, IcebergStructureConfirmation> _nativeIcebergStructures = new();
    private readonly Dictionary<TradeSide, (DateTime Time, decimal Anchor)> _nativeIcebergFailures = new();
    private DateTime _nativeIcebergStructureDay;
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
        _nativeIcebergStructures.Clear();
        _nativeIcebergFailures.Clear();
        _nativeIcebergStructureDay = default;
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

    private static bool IsNativeIcebergExecutionProduct(string? productCode) =>
        productCode?.StartsWith("NQ", StringComparison.OrdinalIgnoreCase) == true ||
        productCode?.StartsWith("MNQ", StringComparison.OrdinalIgnoreCase) == true;

    private void EvaluateNativeIceberg(OpfCandle live)
    {
        var day = GlobexTradingDayKey(live.Time);
        if (day != _nativeIcebergStructureDay)
        {
            _nativeIcebergStructureDay = day;
            _nativeIcebergStructures.Clear();
            _nativeIcebergFailures.Clear();
        }
        foreach (var key in _nativeIcebergStructures.Keys.Where(key =>
            !_nativeIcebergs.TryGetValue(key, out var item) || live.Time - item.Time > TimeSpan.FromSeconds(30)).ToArray())
            _nativeIcebergStructures.Remove(key);
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
            if (live.Time < item.Time || live.Time - item.Time > TimeSpan.FromSeconds(30) || item.Deleted || _nativeIcebergConsumed.Contains(pair.Key)) continue;
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
            if (volume <= 0m || (item.Side == TradeSide.Long ? signedVolume : -signedVolume) / volume < .15m)
            {
                _reactionHolds.Remove(key);
                if (_reactionAuditKeys.Add($"ICE_FLOW|{pair.Key}|{live.Time:yyyyMMddHHmmss}"))
                    LogLiveReadinessInfo($"NATIVE_ICEBERG_WAIT_LOCAL_FLOW event={pair.Key} side={item.Side} signed={signedVolume:0.##} volume={volume:0.##}");
                continue;
            }
            (DateTime Time, decimal Price, decimal Volume, decimal Signed)[] structureTape;
            lock (_intradaySync) structureTape = _entryTape.Where(x => x.Time <= live.Time && x.Time >= live.Time.AddSeconds(-30)).ToArray();
            if (structureTape.Length == 0) continue;
            var stop = IntradayEntryPolicy.IcebergStructuralStop(item.Side == TradeSide.Long, live.Close, item.Price,
                structureTape.Min(x => x.Price), structureTape.Max(x => x.Price), CalculateAtr14(live), tick);
            var risk = Math.Abs(live.Close - stop);
            var fees = _actualCommissionPerContractRoundTrip / _snapshot.InstrumentProfile.PointValue + tick * 2m;
            if (risk * IntradayEntryPolicy.IcebergTargetR <= fees) continue;
            LogLiveReadinessInfo($"ICEBERG_STRUCTURE_STOP_V213 event={pair.Key} iceberg={item.Price} stop={stop} risk={risk} low={structureTape.Min(x => x.Price)} high={structureTape.Max(x => x.Price)} atr={CalculateAtr14(live)}");
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
            var size = IntradayEntryPolicy.EvidenceSize(optionContext, esReaction, true, IntradayEntryPolicy.ProductQuantityLimit(Security?.Code ?? string.Empty));
            _nativeIcebergHud = $"Iceberg: {item.Side} S{size.Score}/3 x{size.Quantity:0}";
            var score = new ScoreBreakdown("CrossMarketIceberg", 100m, 45m, true,
                new[]
                {
                    new ScoreComponent("NqNativeIceberg", $"hidden={item.Hidden}|filled={item.Filled}|source={item.Instrument}", true, 100m, 100m, "NativeMboOnly"),
                    new ScoreComponent("NqLocalOrderFlow", $"signed={signedVolume:0.##}|volume={volume:0.##}|ratio={(volume == 0 ? 0 : (item.Side == TradeSide.Long ? signedVolume : -signedVolume) / volume):0.000}", true, 100m, 100m, "10sTape"),
                    new ScoreComponent("EsOptionsContext", $"status={es.Status}|coverage={es.Coverage:0.000}|captured={es.CapturedAtUtc:O}", optionContext, optionContext ? 100m : 0m, optionContext ? 100m : 0m, "OptionalSizingEvidence"),
                    new ScoreComponent("EsPriceReaction", $"current={es.UnderlyingPrice}|m1={es.UnderlyingPrice60sAgo}", esReaction, esReaction ? 100m : 0m, esReaction ? 100m : 0m, "OptionalSizingEvidence")
                });
            var eventInstrument = IcebergContract(item.Instrument);
            var signal = new CandidateSignal($"ICE-{item.Instrument}-{item.Id}-{item.Time:yyyyMMddHHmmss}", _snapshot.SnapshotId, live.Time, live.Bar, item.Side,
                SetupType.TrendPullback, SignalStage.Triggered, null, score, score, new[] { "NativeRefreshConfirmed", "IntradayReaction", "CrossMarketIcebergOrderFlowReaction", $"ICE_EVENT_KEY:{eventInstrument}|{item.Id}|{item.Time:O}", $"EvidenceQty:{size.Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture)}" });
            var exitStructure = item.Side == TradeSide.Long ? Math.Min(item.Price, structureTape.Min(x => x.Price)) : Math.Max(item.Price, structureTape.Max(x => x.Price));
            signal = signal with { SkipReasons = signal.SkipReasons.Concat(new[] { $"ICE_EXIT_STRUCTURE:{exitStructure.ToString(System.Globalization.CultureInfo.InvariantCulture)}" }).ToArray() };
            _researchLogger?.AppendSignal(signal);
            LogLiveReadinessInfo($"NATIVE_ICEBERG_REACTION event={pair.Key} side={item.Side} price={item.Price} sourceTime={item.Time:O} reactionTime={live.Time:O} hidden={item.Hidden} filled={item.Filled} tapeRatio={(volume == 0 ? 0 : (item.Side == TradeSide.Long ? signedVolume : -signedVolume) / volume):0.000} score={size.Score}/3 qty={size.Quantity} esStatus={es.Status}");
            var active = ActiveReplayExecutions();
            if (_manualAlertEnabled) { SubmitNativeIceberg(signal, live, stop, pair.Key); break; }
            var arbitration = IntradayEntryPolicy.IcebergArbitration(active.Count, active.Any(x => IsIcebergExecutionPath(x.ResearchPath)),
                active.All(x => x.Side == item.Side), active.All(x => x.BracketSubmitted && IsWorkingExecutionOrder(x.StopOrder)),
                GetCurrentAccountPosition() == 0m, !_replayExecutionsByTradeId.Values.Any(x => x.ProtectionCleanupPending || x.ExitCompleted && CountWorkingProtectionOrders(x) > 0),
                active.Sum(x => x.InitialRiskPoints * _snapshot.InstrumentProfile.PointValue * RemainingExecutionQuantity(x)) + risk * _snapshot.InstrumentProfile.PointValue * size.Quantity);
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
            live.Time - item.Time > TimeSpan.FromSeconds(30) || !NativeIcebergStructureReady(eventKey, item, live))
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
                lock (_intradaySync) before = _entryTape.Where(x => x.Time < item.Time && x.Time >= item.Time.AddSeconds(-30)).ToArray();
                if (before.Length < 2 || before[^1].Time - before[0].Time < TimeSpan.FromSeconds(5))
                {
                    LogNativeIcebergStructureWait(key, "WAIT_PRE_EVENT_CONTEXT", item.Side);
                    return false;
                }
                var isLong = item.Side == TradeSide.Long;
                var anchor = isLong ? before.Max(x => x.Price) : before.Min(x => x.Price);
                if (hasFailure) anchor = isLong ? Math.Max(anchor, failure.Anchor) : Math.Min(anchor, failure.Anchor);
                var opposed = IcebergStructureConfirmation.CounterTrend(isLong, before[0].Price, before[^1].Price,
                    CalculateAtr14(live), _snapshot!.InstrumentProfile.TickSize);
                confirmation = new IcebergStructureConfirmation(isLong, anchor, opposed || hasFailure);
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
            if (item.Deleted || item.Time > live.Time || live.Time - item.Time > TimeSpan.FromSeconds(30)) continue;
            var isLong = item.Side == TradeSide.Long;
            var tick = _snapshot!.InstrumentProfile.TickSize;
            var priceAtEvent = live.Close;
            (DateTime Time, decimal Price, decimal Volume, decimal Signed)[] before;
            lock (_intradaySync) before = _entryTape.Where(x => x.Time < item.Time && x.Time >= item.Time.AddSeconds(-30)).ToArray();
            if (before.Length < 2 || before[^1].Time - before[0].Time < TimeSpan.FromSeconds(5)) continue;
            var anchor = isLong ? before.Max(x => x.Price) : before.Min(x => x.Price);
            var opposed = IcebergStructureConfirmation.CounterTrend(isLong, before[0].Price, before[^1].Price, CalculateAtr14(live), tick);
            if (!_nativeIcebergStructures.TryGetValue(pair.Key, out var confirmation))
            {
                var failed = _nativeIcebergFailures.TryGetValue(item.Side, out var failure);
                if (failed && item.Time <= failure.Time) continue;
                if (failed) anchor = isLong ? Math.Max(anchor, failure.Anchor) : Math.Min(anchor, failure.Anchor);
                confirmation = new IcebergStructureConfirmation(isLong, anchor, opposed || failed);
                _nativeIcebergStructures[pair.Key] = confirmation;
                LogLiveReadinessInfo($"ICEBERG_STRUCTURE_PLAN_V216 event={pair.Key} side={item.Side} counterTrend={opposed} previousLoss={failed} anchor={anchor}");
            }
            priceAtEvent = live.Close;
            var reason = confirmation.Observe(priceAtEvent, tick, live.Time);
            if (reason is "WAIT_STRUCTURE_RETEST" or "WAIT_STRUCTURE_RECLAIM")
                LogNativeIcebergStructureWait(pair.Key, reason, item.Side);
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
            (DateTime Time, decimal Price, decimal Volume, decimal Signed)[] tape;
            lock (_intradaySync) tape = _entryTape.Where(x => x.Time <= live.Time && x.Time >= live.Time.AddSeconds(-10)).ToArray();
            if (!IntradayEntryPolicy.IcebergNoProgressExit(execution.Side == TradeSide.Long, live.Time - execution.IcebergObservedSince.Value,
                execution.IcebergMaxFavorable, execution.InitialRiskPoints, live.Close, execution.IcebergExitStructure,
                tape.Sum(x => x.Signed), tape.Sum(x => x.Volume)))
            { execution.IcebergFailureSince = null; continue; }
            if (execution.IcebergFailureLastObserved is not { } lastObserved || live.Time - lastObserved > TimeSpan.FromSeconds(1))
                execution.IcebergFailureSince = live.Time;
            execution.IcebergFailureLastObserved = live.Time;
            execution.IcebergFailureSince ??= live.Time;
            if (live.Time - execution.IcebergFailureSince.Value < TimeSpan.FromSeconds(5) ||
                execution.TimeStopExitPendingV208 || execution.EmergencyFlattenSubmitted) continue;
            execution.TimeStopExitPendingV208 = true;
            execution.TimeStopReason = "IcebergNoProgressStructureFailed";
            execution.TimeStopReferencePriceV209 = live.Close;
            AppendExecutionEvent(execution, "ICEBERG_STRUCTURE_EXIT_TRIGGERED_V214", "TIME_STOP", live.Close, RemainingExecutionQuantity(execution),
                $"elapsed={(live.Time - execution.CreatedTime).TotalSeconds:0.0}|structure={execution.IcebergExitStructure}|mfe={execution.IcebergMaxFavorable}");
            EnqueueExecutionAction("IcebergStructureExit", async () =>
            {
                if (execution.ExitCompleted) return;
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

    private static bool IsIcebergExecutionPath(string path) => path is "NativeIcebergReaction" or "CrossMarketIcebergOrderFlowReaction";

    private EsOptionsProxySnapshot ReadEsOptionsProxyAsOf(DateTime time)
    {
        if (IsHistoricalReplayTime(time))
        {
            try { return OptionsProxySnapshotStore.ReadAsOf(OptionsProxyArchivePath, DateTime.SpecifyKind(time, DateTimeKind.Utc)); }
            catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
            { return EsOptionsProxySnapshot.Unavailable($"replay:{ex.GetType().Name}"); }
        }
        lock (_esOptionsProxySync) return _esOptionsProxy;
    }

    private decimal EvidenceSizedExecutionQuantity(CandidateSignal signal, string path, DateTime decisionTime)
    {
        if (path is not ("ESResponseAlignedBreakout" or "ESWallRejectionReversal" or "ESExpectedMoveExpansion" or "ESGammaConcentrationResponse" or "CrossMarketIcebergOrderFlowReaction"))
            return 1m;
        var maximum = IntradayEntryPolicy.ProductQuantityLimit(Security?.Code ?? string.Empty);
        var es = ReadEsOptionsProxyAsOf(decisionTime);
        var evidenceTime = IsHistoricalReplayTime(decisionTime) ? decisionTime : DateTime.UtcNow;
        var optionContext = es.Status == "ModelReady" && es.CapturedAtUtc <= evidenceTime &&
            evidenceTime - es.CapturedAtUtc <= TimeSpan.FromSeconds(90) &&
            es.OptionQuotesObservedAtUtc is { } quoteTime && quoteTime <= evidenceTime &&
            evidenceTime - quoteTime <= TimeSpan.FromSeconds(90) && es.AtmQuotesReady == true &&
            es.UnderlyingObservedAtUtc is { } underlyingTime && underlyingTime <= evidenceTime &&
            evidenceTime - underlyingTime <= TimeSpan.FromSeconds(90) &&
            es.LocalQuoteCoverage >= .5m && es.CallQuoteCoverage >= .5m && es.PutQuoteCoverage >= .5m;
        var aligned = es.UnderlyingObservedAtUtc is { } observed && observed <= evidenceTime &&
            evidenceTime - observed <= TimeSpan.FromSeconds(90) && es.PriceReferenceAtUtc is { } reference &&
            reference <= evidenceTime && evidenceTime - reference <= TimeSpan.FromSeconds(90) &&
            (signal.Side == TradeSide.Long
            ? es.UnderlyingPrice.HasValue && es.UnderlyingPrice60sAgo.HasValue && es.UnderlyingPrice > es.UnderlyingPrice60sAgo
            : es.UnderlyingPrice.HasValue && es.UnderlyingPrice60sAgo.HasValue && es.UnderlyingPrice < es.UnderlyingPrice60sAgo);
        (DateTime Time, decimal Price, decimal Volume, decimal Signed)[] tape;
        lock (_intradaySync) tape = _entryTape.Where(x => x.Time <= decisionTime && x.Time >= decisionTime.AddSeconds(-10)).ToArray();
        var volume = tape.Sum(x => x.Volume);
        var signed = tape.Sum(x => x.Signed);
        var flowRatio = volume == 0m ? 0m : (signal.Side == TradeSide.Long ? signed : -signed) / volume;
        var flow = flowRatio >= (path == "CrossMarketIcebergOrderFlowReaction" ? .15m : .1m);
        var barStrong = true;
        if (path != "CrossMarketIcebergOrderFlowReaction" && _intradayEvidence.TryGetValue(signal.SignalId, out var evidence))
        {
            var range = evidence.High - evidence.Low;
            if (range <= 0m) barStrong = false;
            else
            {
                var body = Math.Abs(evidence.Close - evidence.Open) / range;
                var close = signal.Side == TradeSide.Long ? (evidence.Close - evidence.Low) / range : (evidence.High - evidence.Close) / range;
                var minBody = path == "ESResponseAlignedBreakout" ? .5m : path == "ESExpectedMoveExpansion" ? .4m : .25m;
                var minClose = path == "ESResponseAlignedBreakout" ? .8m : path == "ESExpectedMoveExpansion" ? .65m : .25m;
                barStrong = body >= minBody && close >= minClose;
            }
        }
        flow &= barStrong;
        var noRetest = signal.SkipReasons.Contains("LargeRiskBreakoutNoRetest", StringComparer.Ordinal);
        var size = IntradayEntryPolicy.EvidenceSize(optionContext, aligned, flow, noRetest ? 1m : maximum);
        LogLiveReadinessInfo($"EVIDENCE_SIZE path={path} signal={signal.SignalId} score={size.Score}/3 qty={size.Quantity} option={optionContext} esPrice={aligned} localFlow={flow} tapeRatio={flowRatio:0.000} barStrong={barStrong} noRetest={noRetest}");
        return size.Quantity;
    }
}
