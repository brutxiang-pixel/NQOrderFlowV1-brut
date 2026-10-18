using ATAS.Indicators;
using ATAS.DataFeedsCore;
using OPFStrategyV1.Core.MarketData;
using OPFStrategyV1.Core.Signals;
using OPFStrategyV1.Core.Scoring;

namespace OPFStrategyV1.Strategy;

public sealed partial class OpeningPullbackFailureStrategy
{
    private readonly Dictionary<string, ICandleSeriesSubscription> _contextSeries = new();
    private readonly object _intradaySync = new();
    private readonly Queue<(DateTime Time, decimal Price, decimal Volume, decimal Signed)> _entryTape = new();
    private readonly Dictionary<string, DateTime> _reactionHolds = new();
    private readonly Dictionary<string, DateTime> _reactionLastSeen = new();
    private readonly Dictionary<string, (DateTime Time, decimal Price, decimal Stop, bool IsLong)> _intradayPairs = new();
    private readonly HashSet<string> _intradayFalseBreaks = new();
    private readonly HashSet<string> _intradayAttempts = new();
    private readonly Dictionary<string, DateTime> _intradayRetryAt = new();
    private readonly Dictionary<string, (string Decision, string Reason)> _intradayOutcomes = new();
    private readonly HashSet<string> _intradayRecorded = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (bool IsLong, decimal Stop, DateTime Since, DateTime Expires)> _intradayStops = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _intradayStopCrossed = new();
    private readonly Dictionary<string, OpfCandle> _intradayEvidence = new();
    private readonly List<(CandidateSignal Signal, string Path, decimal Stop, int ExpiresBar)> _preparedIntraday = new();
    private readonly HashSet<string> _preparedTouched = new();
    private readonly Dictionary<string, DateTime> _preparedObservedAt = new();
    private readonly HashSet<string> _reactionAuditKeys = new();
    private DateTime _lastEntryTradeTime;
    private DateTime _intradayStartedUtc;
    private DateTime _lastIntradayEvaluation;
    private DateTime _nextContextAudit;
    private DateTime _lastContextRefresh;
    private string _multiFrameHud = "MTF: loading";
    private bool _dailyPnlScopeUnresolved;
    private bool _manualInterventionLatch;
    private readonly Dictionary<string, CompletedFrame> _completedFrames = new();
    private readonly Dictionary<int, decimal?> _dailyAverages = new();

    private void InitializeIntradayContext()
    {
        DisposeIntradayContext();
        _intradayStartedUtc = DateTime.UtcNow;
        _manualInterventionLatch = false;
        _intradayAttempts.Clear();
        _intradayRetryAt.Clear();
        _intradayOutcomes.Clear();
        _intradayRecorded.Clear();
        _intradayStops.Clear();
        _intradayStopCrossed.Clear();
        _levelEvents.Clear();
        _previousEsLevels.Clear();
        _esReactionPlans.Clear();
        _alignment.Clear();
        _previousEsEventPrice = null;
        _previousEsEventTime = default;
        _pathOpportunities.Clear();
        _pathOpportunityKeys.Clear();
        _pathOpportunityPackets.Clear();
        _intradayPairs.Clear();
        _intradayFalseBreaks.Clear();
        _intradayEvidence.Clear();
        _preparedIntraday.Clear();
        _preparedTouched.Clear();
        _preparedObservedAt.Clear();
        _reactionAuditKeys.Clear();
        _reactionHolds.Clear();
        _reactionLastSeen.Clear();
        _lastContextRefresh = default;
        _nextContextAudit = default;
        _lastIntradayEvaluation = default;
        _lastEntryTradeTime = default;
        lock (_intradaySync) _entryTape.Clear();
        _completedFrames.Clear();
        _dailyAverages.Clear();
        var provider = DataProvider?.CandlesDataProvider;
        if (provider?.IsAvailable == true)
        {
            foreach (var item in new[] { ("15M", CandleSeriesTimeFrame.Minutes(15)), ("30M", CandleSeriesTimeFrame.Minutes(30)),
                ("1H", CandleSeriesTimeFrame.Hours(1)), ("4H", CandleSeriesTimeFrame.Hours(4)),
                ("6H", CandleSeriesTimeFrame.Hours(6)), ("D", CandleSeriesTimeFrame.Daily()) })
            {
                try
                {
                    _contextSeries[item.Item1] = provider.SubscribeCandles(new CandleSeriesRequest
                    { TimeFrame = item.Item2, DaysToLoad = item.Item1 == "D" ? 220 : 20 });
                }
                catch (Exception ex)
                {
                    LogLiveReadinessInfo($"MTF_SUBSCRIBE_FAILED frame={item.Item1} error={ex.GetType().Name}:{ex.Message}");
                }
            }
        }
        else LogLiveReadinessInfo("MTF_UNAVAILABLE CandlesDataProvider unavailable; no synthetic daily SMA");
        InitializeNativeIceberg();
    }

    private void DisposeIntradayContext()
    {
        foreach (var series in _contextSeries.Values) series.Dispose();
        _contextSeries.Clear();
        DisposeNativeIceberg();
    }

    private void RecordEntryTrade(MarketDataArg trade)
    {
        if (_isStoppingActualExecution || trade.Price <= 0m || trade.Volume <= 0m) return;
        ObserveEsUnderlying(trade.Price, trade.Time);
        foreach (var pair in _intradayStops)
        {
            if (trade.Time > pair.Value.Expires) { _intradayStops.TryRemove(pair.Key, out _); continue; }
            if (trade.Time >= pair.Value.Since && (pair.Value.IsLong ? trade.Price <= pair.Value.Stop : trade.Price >= pair.Value.Stop))
                _intradayStopCrossed.TryAdd(pair.Key, 0);
        }
        lock (_intradaySync)
        {
            if (trade.Time < _lastEntryTradeTime) return;
            _lastEntryTradeTime = trade.Time;
            _entryTape.Enqueue((trade.Time, trade.Price, trade.Volume, trade.Direction == ATAS.Indicators.TradeDirection.Buy ? trade.Volume :
                trade.Direction == ATAS.Indicators.TradeDirection.Sell ? -trade.Volume : 0m));
            while (_entryTape.Count > 0 && _entryTape.Peek().Time < trade.Time.AddSeconds(-30)) _entryTape.Dequeue();
        }
    }

    private DateTime IntradayDecisionTime(IndicatorCandle candle)
    {
        var time = _lastEntryTradeTime > candle.Time && _lastEntryTradeTime < candle.Time.AddMinutes(5)
            ? _lastEntryTradeTime : candle.LastTime > candle.Time ? candle.LastTime : candle.Time;
        return IsHistoricalReplayTime(candle.Time) ? time : DateTime.UtcNow;
    }

    private void RefreshCompletedContext(DateTime now)
    {
        if (now >= _lastContextRefresh && now - _lastContextRefresh < TimeSpan.FromSeconds(1)) return;
        _lastContextRefresh = now;
        _completedFrames.Clear();
        _dailyAverages.Clear();
        foreach (var pair in _contextSeries)
        {
            if (!pair.Value.IsLoaded) continue;
            // Always exclude the latest session candle, even if its LastTime is old.
            var raw = pair.Value.Candles.ToArray();
            var duration = pair.Value.Request.TimeFrame.TimeBasedDuration ?? TimeSpan.FromDays(1);
            var closed = IntradayEntryPolicy.ClosedAsOf(raw.Select((c, i) =>
                (ToOpfCandle(i, c), pair.Key == "D" && i + 1 < raw.Length ? raw[i + 1].Time : c.Time + duration)).SkipLast(1), now);
            if (closed.Length == 0) continue;
            var latest = closed[^1];
            var slope = closed.Length >= 4 ? latest.Close - closed[^4].Close : 0m;
            _completedFrames[pair.Key] = new CompletedFrame(pair.Key, latest.Time, latest.High, latest.Low, latest.Close, slope, closed.Length);
            if (pair.Key == "D")
                foreach (var period in new[] { 10, 20, 60, 120 }) _dailyAverages[period] = IntradayEntryPolicy.Sma(closed, period);
        }
        _multiFrameHud = "MTF: " + string.Join(" ", new[] { "15M", "30M", "1H", "4H", "6H", "D" }.Select(f =>
            _completedFrames.TryGetValue(f, out var x) ? $"{f}:{(x.Slope > 0 ? "UP" : x.Slope < 0 ? "DN" : "FLAT")}" : $"{f}:N/A"));
        if (now >= _nextContextAudit)
        {
            _nextContextAudit = now.AddMinutes(5);
            LogLiveReadinessInfo($"MTF_CONTEXT asOf={now:O} {_multiFrameHud} SMA=" + string.Join("|", new[] { 10, 20, 60, 120 }.Select(p =>
                $"{p}:{(_dailyAverages.GetValueOrDefault(p)?.ToString("0.##") ?? "INSUFFICIENT")}")));
        }
    }

    private string? ContextEntryReason(CandidateSignal signal, OpfCandle candle, string path, decimal risk)
    {
        var decisionTime = candle.Time;
        if (_intradayEvidence.TryGetValue(signal.SignalId, out var evidence)) candle = evidence;
        RefreshCompletedContext(decisionTime);
        var continuation = path is "ObservationConfirm" or "BreakawayFvg_Qualified" or "ESResponseAlignedBreakout" or "ESExpectedMoveExpansion";
        var isLong = signal.Side == TradeSide.Long;
        var fast = new[] { "15M", "30M", "1H" }.Select(x => _completedFrames.GetValueOrDefault(x)).Where(x => x is not null && x.Count >= 4).ToArray();
        if (continuation && fast.Length >= 2 && fast.Count(x => isLong ? x!.Slope < 0m : x!.Slope > 0m) >= 2)
            return "MTF:ContinuationAgainstTwoFastFrames";
        var spike = BuildLiquiditySpikeObservation(candle, signal.Side);
        if (continuation && spike.EventType == "Exhaustion" && spike.Side == signal.Side) return "Micro:ContinuationExhaustion";
        if (!continuation && path != "NativeIcebergReaction" && spike.EventType == "Initiative" && spike.Side != signal.Side)
            return "Micro:ReversalAgainstInitiative";
        var obstacles = _completedFrames.Values.Where(x => x.Name is "4H" or "6H" or "D")
            .Select(x => isLong ? x.High : x.Low).Concat(_dailyAverages.Values.Where(x => x.HasValue).Select(x => x!.Value));
        obstacles = obstacles.Concat(_significantZones.Values.Where(z => z.Side != signal.Side &&
            z.State is SignificantZoneState.Active or SignificantZoneState.TestedOnce or SignificantZoneState.TestedTwice)
            .Select(z => isLong ? Math.Min(z.InnerBoundary, z.OuterBoundary) : Math.Max(z.InnerBoundary, z.OuterBoundary)));
        var distance = obstacles.Select(x => isLong ? x - candle.Close : candle.Close - x).Where(x => x > 0m).DefaultIfEmpty(decimal.MaxValue).Min();
        var tick = _snapshot!.InstrumentProfile.TickSize;
        var feePoints = _actualCommissionPerContractRoundTrip / _snapshot.InstrumentProfile.PointValue + tick * 2m;
        LogLiveReadinessInfo($"ENTRY_CONTEXT signal={signal.SignalId} path={path} scenario={(continuation ? "Continuation" : "RejectionOrFailure")} plannedR={ActualTargetRFor(signal, path, risk):0.##} forwardSpace={(distance == decimal.MaxValue ? "UNKNOWN" : distance.ToString("0.##"))} feeSlippage={feePoints:0.##}");
        // This is a fee/near-obstacle check, not a global structural RR >= 1 gate.
        return distance < Math.Max(feePoints, risk * (continuation ? .25m : .1m)) ? "Context:ImmediateObstacle" : null;
    }

    private void EvaluateIntradayEntries(int bar, decimal value)
    {
        if (_snapshot is null || _isStoppingActualExecution || bar != CurrentBar - 1 || GetCandle(bar) is not { } raw) return;
        var now = IntradayDecisionTime(raw);
        if (now - _lastIntradayEvaluation < TimeSpan.FromMilliseconds(250) || (!IsHistoricalReplayTime(raw.Time) && now - _intradayStartedUtc < TimeSpan.FromSeconds(3))) return;
        lock (_intradaySync) if (_entryTape.Count == 0 || _lastEntryTradeTime < raw.Time) return;
        _lastIntradayEvaluation = now;
        var price = value > 0 ? value : raw.Close;
        var live = ToOpfCandle(bar, raw) with { Time = now, Close = price };
        foreach (var pair in _intradayPairs.Where(x => live.Time >= x.Value.Time &&
            (x.Value.IsLong ? price <= x.Value.Stop : price >= x.Value.Stop)))
            _intradayFalseBreaks.Add(pair.Key);
        foreach (var tracker in _researchTrackers.Where(x => x.Signal.SkipReasons.Contains("IntradayReaction") && x.EntryTime <= now))
            tracker.Update(live with { High = price, Low = price });
        RefreshCompletedContext(now);
        ScheduleIcebergNoProgress(live);
        EvaluateNativeIceberg(live); // Priority before ordinary candidates.
        if (_pendingIcebergSwitch is not null) return;
        foreach (var pending in _pendingObservationConfirms.ToArray())
        {
            var signal = pending.Signal;
            if (signal.Zone is null || bar <= signal.Bar || bar > pending.MaxConfirmBar) continue;
            var stop = signal.Side == TradeSide.Long ? signal.Zone.Low - .5m : signal.Zone.High + .5m;
            var trigger = signal.Side == TradeSide.Long ? signal.Zone.High : signal.Zone.Low;
            TryIntradayReaction(signal, live, "ObservationConfirm", trigger, stop, raw.Time.AddMinutes(5));
        }
        foreach (var pending in _pendingFailureReverseRetests.ToArray())
        {
            var signal = pending.Signal;
            if (signal.Zone is null || bar <= signal.Bar || bar > pending.MaxRetestBar) continue;
            var touchedAt = ObservedZoneTouch(signal.Zone.Low, signal.Zone.High, pending.RealtimeObservedAt, live);
            pending.RealtimeObservedAt = now;
            if (!pending.RealtimeTouchedAt.HasValue && touchedAt.HasValue)
            {
                pending.RealtimeTouchedAt = touchedAt;
                _reactionHolds.Remove("FailureReverse_RetestFailed|" + signal.SignalId);
                continue; // touch and failed retest require separate events
            }
            if (!pending.RealtimeTouchedAt.HasValue || now <= pending.RealtimeTouchedAt.Value) continue;
            TryIntradayReaction(signal, live, "FailureReverse_RetestFailed", signal.Side == TradeSide.Long ? signal.Zone.High : signal.Zone.Low,
                signal.Side == TradeSide.Long ? signal.Zone.Low - .5m : signal.Zone.High + .5m, raw.Time.AddMinutes(5));
        }
        foreach (var pending in _preparedIntraday.ToArray())
        {
            if (bar > pending.ExpiresBar) { _preparedIntraday.Remove(pending); continue; }
            if (bar <= pending.Signal.Bar || pending.Signal.Zone is not { } zone) continue;
            var key = pending.Path + "|" + pending.Signal.SignalId;
            if (ObservedZoneTouch(zone.Low, zone.High, _preparedObservedAt.TryGetValue(key, out var observedAt) ? observedAt : null, live).HasValue)
                _preparedTouched.Add(key);
            _preparedObservedAt[key] = now;
            if (!_preparedTouched.Contains(key)) continue;
            TryIntradayReaction(pending.Signal, live, pending.Path, pending.Signal.Side == TradeSide.Long ? zone.High : zone.Low,
                pending.Stop, raw.Time.AddMinutes(5));
        }
        EvaluateEsCrossMarketPaths(live, intraday: true);
    }

    private void PrepareIntraday(CandidateSignal signal, string path, decimal stop)
    {
        if (signal.Zone is null || !IsReplayExecutionPathEnabled(path)) return;
        _preparedIntraday.Add((signal, path, stop, signal.Bar + 3));
        LogLiveReadinessInfo($"REALTIME_PREPARED path={path} origin={signal.SignalId} expiresBar={signal.Bar + 3}");
    }

    private DateTime? ObservedZoneTouch(decimal low, decimal high, DateTime? since, OpfCandle live)
    {
        if (live.Close >= low && live.Close <= high) return live.Time;
        if (!since.HasValue) return null;
        lock (_intradaySync)
        {
            var touch = _entryTape.FirstOrDefault(x => x.Time > since.Value && x.Time <= live.Time && x.Price >= low && x.Price <= high);
            return touch.Time == default ? null : touch.Time;
        }
    }

    private void TryIntradayReaction(CandidateSignal signal, OpfCandle live, string path, decimal trigger, decimal stop, DateTime expires)
    {
        var key = path + "|" + signal.SignalId;
        if (_intradayAttempts.Contains(key) || !IsReplayExecutionPathEnabled(path)) return;
        _intradayStops.TryAdd(key, (signal.Side == TradeSide.Long, stop, live.Time, expires));
        var crossedStop = _intradayStopCrossed.ContainsKey(key);
        var auditSignal = signal with { SignalId = signal.SignalId + "-RT" };
        if (live.Time > expires || crossedStop || (signal.Side == TradeSide.Long ? live.Close <= stop : live.Close >= stop))
        {
            _intradayAttempts.Add(key);
            _intradayStops.TryRemove(key, out _);
            RecordPathOpportunityDecision(auditSignal.SignalId, "Invalidated", live.Time > expires ? "Expired" : "StopCrossed", "");
            LogLiveReadinessInfo($"REALTIME_INVALIDATED path={path} origin={signal.SignalId} time={live.Time:O}");
            return;
        }
        var tick = _snapshot!.InstrumentProfile.TickSize;
        var beyond = signal.Side == TradeSide.Long ? live.Close >= trigger + tick : live.Close <= trigger - tick;
        if (!beyond) { _reactionHolds.Remove(key); return; }
        if (_reactionLastSeen.TryGetValue(key, out var lastSeen) && live.Time - lastSeen > TimeSpan.FromSeconds(1))
            _reactionHolds.Remove(key);
        _reactionLastSeen[key] = live.Time;
        if (!_reactionHolds.TryGetValue(key, out var held)) _reactionHolds[key] = held = live.Time;
        (DateTime Time, decimal Price, decimal Volume, decimal Signed)[] tape;
        lock (_intradaySync) tape = _entryTape.Where(x => x.Time <= live.Time && x.Time >= live.Time.AddSeconds(-10)).ToArray();
        var reason = IntradayEntryPolicy.Reaction(signal.Side == TradeSide.Long, live.Close, trigger, stop, tick, live.Time, expires, held,
            tape.Sum(x => x.Signed), tape.Sum(x => x.Volume), Math.Max(tick * 4m, Math.Abs(trigger - stop) * .5m));
        if (reason is null && _intradayRetryAt.TryGetValue(key, out var retryAt) && live.Time < retryAt) return;
        if (reason is null || !_reactionAuditKeys.Contains(key + "|" + reason))
        {
            try
            {
                _researchLogger?.AppendValidationPacket(_snapshot?.SnapshotId ?? "-", "reaction",
                    new ReactionPacket(_snapshot?.SnapshotId ?? "-", signal.SignalId, Security?.Code ?? "-", path,
                        signal.Side == TradeSide.Long, live.Close, trigger, stop, tick, live.Time, expires, held,
                        tape.Sum(x => x.Signed), tape.Sum(x => x.Volume), Math.Max(tick * 4m, Math.Abs(trigger - stop) * .5m), reason));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            { LogLiveReadinessInfo($"VALIDATION_REACTION_CAPTURE_FAILED {ex.GetType().Name}"); }
        }
        if (reason is not null)
        {
            RecordPathOpportunityDecision(auditSignal.SignalId, "Waiting", reason, "");
            if (reason == "ChaseTooFar") _reactionHolds.Remove(key);
            if (_reactionAuditKeys.Add(key + "|" + reason))
                LogLiveReadinessInfo($"REALTIME_FUNNEL path={path} origin={signal.SignalId} stage={reason} time={live.Time:O}");
            return;
        }
        if (_manualInterventionLatch)
        {
            _intradayAttempts.Add(key);
            _intradayStops.TryRemove(key, out _);
            RecordPathOpportunityDecision(auditSignal.SignalId, "Skip", "ManualInterventionDetected", "");
            return;
        }
        var instant = live with { Open = live.Close, High = live.Close, Low = live.Close };
        var triggered = signal with { SignalId = signal.SignalId + "-RT", Time = live.Time, Bar = live.Bar, Stage = SignalStage.Triggered,
            SetupQualityScore = path == "ObservationConfirm" ? BuildSetupQualityScore(signal.RegimeScore, signal.Zone, path, signal.Side, live) : signal.SetupQualityScore,
            SkipReasons = signal.SkipReasons.Concat(new[] { "IntradayReaction", "Origin:" + signal.SignalId }).ToArray() };
        _intradayPairs[key] = (live.Time, live.Close, stop, signal.Side == TradeSide.Long);
        _intradayEvidence[triggered.SignalId] = live with { Time = GetCandle(live.Bar).Time };
        var risk = Math.Abs(live.Close - stop);
        if (risk <= 0m) return;
        if (_intradayRecorded.Add(key))
        {
            _researchLogger?.AppendSignal(triggered);
            LogLiveReadinessInfo($"REALTIME_TRIGGER path={path} origin={signal.SignalId} time={live.Time:O} price={live.Close} trigger={trigger} stop={stop} holdMs={(live.Time-held).TotalMilliseconds:0}");
            AddResearchTracker(triggered, instant, path, stop, risk, submitReplayExecution: false);
            StartPathOpportunity(triggered, live, path, stop, risk);
        }
        _intradayOutcomes.Remove(triggered.SignalId);
        TrySubmitReplayExecution(triggered, instant, path, stop, risk, allowDelayedExpansion: false);
        var outcome = _intradayOutcomes.GetValueOrDefault(triggered.SignalId, ("Observed", "NoSubmission"));
        var retry = CandidateAttemptPolicy.CanRetry(outcome.Item1, outcome.Item2);
        if (retry) _intradayRetryAt[key] = live.Time.AddSeconds(1);
        else
        {
            _intradayAttempts.Add(key);
            _intradayStops.TryRemove(key, out _);
        }
        if (_reactionAuditKeys.Add($"ATTEMPT|{key}|{outcome.Item1}|{outcome.Item2}"))
            LogLiveReadinessInfo($"CANDIDATE_ATTEMPT path={path} origin={signal.SignalId} state={(retry ? "Waiting" : outcome.Item1 == "Execute" ? "Submitted" : "Finished")} reason={outcome.Item2}");
    }

    private bool AuditIntradayPair(CandidateSignal signal, OpfCandle candle, string path)
    {
        var origin = signal.SignalId;
        if (origin.EndsWith("-OC", StringComparison.Ordinal) || origin.EndsWith("-RT", StringComparison.Ordinal)) origin = origin[..^3];
        var key = path + "|" + origin;
        if (!_intradayPairs.TryGetValue(key, out var entry)) return false;
        LogLiveReadinessInfo($"REALTIME_CLOSED_PAIR path={path} origin={origin} early={entry.Time:O} baseline={candle.Time.AddMinutes(5):O} latencySeconds={(candle.Time.AddMinutes(5)-entry.Time).TotalSeconds:0} earlyPrice={entry.Price} baselinePrice={candle.Close} drift={candle.Close-entry.Price} stopBeforeBaseline={_intradayFalseBreaks.Contains(key)}");
        return true;
    }

    private bool IsCurrentPnlScope(string[] fields) => AccountPnlScope.Matches(fields, LiveAccountScope.FromPortfolio(Portfolio),
        Security?.Code ?? ResolveFeishuProductCode(), IsHistoricalReplayTime(_lastResearchCandle?.Time ?? DateTime.UtcNow));

    private bool TryRestoreLegacyPnlFromAccountFills(string tradeId, out decimal net)
    {
        net = 0m;
        if (Connector?.MyTrades is null || _snapshot is null) return false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var fills = Connector.MyTrades.Where(t => Equals(t.Portfolio ?? t.Order?.Portfolio, Portfolio) &&
            Equals(t.Security ?? t.Order?.Security, Security) && !string.IsNullOrWhiteSpace(t.Id) &&
            TryParseExecutionComment(t.Order?.Comment, out var id, out _) && id == tradeId && seen.Add(t.Id)).ToArray();
        var buys = fills.Where(x => x.OrderDirection == OrderDirections.Buy).ToArray();
        var sells = fills.Where(x => x.OrderDirection == OrderDirections.Sell).ToArray();
        var qty = buys.Sum(x => x.Volume);
        if (qty <= 0m || qty != sells.Sum(x => x.Volume)) return false;
        net = Math.Round((sells.Sum(x => x.Price * x.Volume) - buys.Sum(x => x.Price * x.Volume)) *
            _snapshot.InstrumentProfile.PointValue - _actualCommissionPerContractRoundTrip * qty, 2);
        LogLiveReadinessInfo($"LEGACY_PNL_ACCOUNT_FILLS_RECONSTRUCTED trade={tradeId} quantity={qty} uniqueFills={fills.Length} net={net}");
        return true;
    }

    private void ObserveExternalTrade(MyTrade trade)
    {
        var replay = IsHistoricalReplayTime(GetCandle(CurrentBar - 1)?.Time ?? DateTime.UtcNow);
        if (trade.Time < (replay ? GetCandle(CurrentBar - 1)?.Time ?? _intradayStartedUtc : _intradayStartedUtc)) return;
        if (trade.Order is not { } order || !Equals(order.Portfolio, Portfolio) || !Equals(order.Security, Security) ||
            ExecutionForOrder(order) is not null || TryParseExecutionComment(order.Comment, out _, out _)) return;
        _manualInterventionLatch = true;
        _pendingIcebergSwitch = null;
        SetLiveReadinessBlocked("ManualInterventionDetected", "External account trade detected. New automatic entries stay blocked until strategy restart.");
        if (GetCurrentAccountPosition() != 0m) return;
        foreach (var execution in ActiveReplayExecutions())
        {
            execution.ExitCompleted = true;
            execution.ExitRole = "MANUAL_FLAT_UNATTRIBUTED";
            MarkProtectionCleanupPending(execution, "ManualAccountFlat");
        }
        ScheduleProtectionCleanupIfNeeded("ManualAccountFlat");
    }
}
