using OPFStrategyV1.Core.Scoring;
using OPFStrategyV1.Core.MarketData;
using OPFStrategyV1.Core.Signals;

namespace OPFStrategyV1.Strategy;

public sealed partial class OpeningPullbackFailureStrategy
{
    private readonly HashSet<string> _esCrossMarketTriggeredBars = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _nextEsIntradayAudit;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, EsBreakoutEvent> BreakoutAnchors = new();
    private EsBreakoutEvent? _pendingEsRetest;
    private readonly Dictionary<string, LevelReactionEvent> _levelEvents = new();
    private decimal? _previousEsEventPrice;
    private DateTime _previousEsEventTime;
    private readonly Dictionary<string, decimal?> _previousEsLevels = new();
    private readonly Dictionary<string, (decimal Trigger, decimal Stop, DateTime Expires)> _esReactionPlans = new();
    private readonly CrossMarketAlignment _alignment = new();

    private void ObserveEsLevelEvents(EsOptionsProxySnapshot context)
    {
        if (context.UnderlyingPrice is not { } price || context.CapturedAtUtc <= _previousEsEventTime) return;
        foreach (var (key, level) in new[] { ("Put", context.PutWallProxy), ("Call", context.CallWallProxy), ("Gamma", context.GammaConcentrationStrike) })
        {
            if (_levelEvents.TryGetValue(key, out var existing) && existing.Active(context.CapturedAtUtc))
            { existing.Observe(price, context.CapturedAtUtc); continue; }
            _levelEvents.Remove(key);
            if (level is not { } value || _previousEsEventPrice is not { } previous ||
                context.CapturedAtUtc - _previousEsEventTime > TimeSpan.FromSeconds(90)) continue;
            var created = LevelReactionEvent.TryCreate(value, _previousEsLevels.GetValueOrDefault(key), previous, price, context.CapturedAtUtc);
            if (created is not null)
            {
                created.Observe(price, context.CapturedAtUtc);
                _levelEvents[key] = created;
            }
        }
        _previousEsLevels["Put"] = context.PutWallProxy;
        _previousEsLevels["Call"] = context.CallWallProxy;
        _previousEsLevels["Gamma"] = context.GammaConcentrationStrike;
        _previousEsEventPrice = price;
        _previousEsEventTime = context.CapturedAtUtc;
    }

    private EsOptionsProxySnapshot WithLevelEvent(EsOptionsProxySnapshot context, string path, TradeSide side, DateTime decision)
    {
        if (context.EvidenceSchema < 2 || path is not ("ESWallRejectionReversal" or "ESGammaConcentrationResponse")) return context;
        var key = path == "ESGammaConcentrationResponse" ? "Gamma" : side == TradeSide.Long ? "Put" : "Call";
        if (!_levelEvents.TryGetValue(key, out var item) || !item.Active(decision) || context.UnderlyingPrice is not { } price)
            return context with { EventLevel = null, EventTouchedAtUtc = null, EventKind = null };
        return context with { EventLevel = item.Level, EventTouchedAtUtc = item.Touched,
            EventKind = item.Response(side == TradeSide.Long, price, .25m) };
    }

    private void EvaluateEsCrossMarketPaths(OpfCandle candle, bool intraday = false)
    {
        if (!IsEsOptionsConsumer || _snapshot is null)
            return;
        if (!EnableEsOptionsEntryGate) return;

        EsOptionsProxySnapshot context;
        if (IsHistoricalReplayTime(candle.Time))
        {
            try { context = OptionsProxySnapshotStore.ReadAsOf(OptionsProxyArchivePath, DateTime.SpecifyKind(candle.Time, DateTimeKind.Utc)); }
            catch (Exception ex) when (ex is System.IO.IOException or System.Text.Json.JsonException or UnauthorizedAccessException) { return; }
        }
        else
        {
            RefreshEsOptionsSharedContext();
            lock (_esOptionsProxySync) context = _esOptionsProxy;
        }
        if (context.Status != "ModelReady" || context.UnderlyingPrice is null)
        {
            if (intraday && candle.Time < _nextEsIntradayAudit) return;
            _nextEsIntradayAudit = candle.Time.AddSeconds(5);
            _researchLogger?.AppendInfo(_snapshot.SnapshotId, candle.Bar, candle.Time,
                $"ES_CROSS_MARKET_FUNNEL stage=DataBlocked status={context.Status} reason={context.Detail}");
            return;
        }

        var esMove = context.UnderlyingPrice5mAgo is { } prior ? context.UnderlyingPrice.Value - prior : 0m;
        ObserveEsLevelEvents(context);
        if (intraday && context.CapturedAtUtc <= candle.Time && candle.Time - context.CapturedAtUtc <= TimeSpan.FromSeconds(5))
            _alignment.Observe(context.CapturedAtUtc, context.UnderlyingPrice.Value, candle.Close);
        var responseThreshold = Math.Max(0.5m, (context.ExpectedMoveProxy ?? 0m) * 0.05m);
        var longBody = candle.Close - candle.Open;
        var shortBody = candle.Open - candle.Close;
        var range = candle.High - candle.Low;
        var structure = _recentCandles.Where(x => x.Bar < candle.Bar).TakeLast(3).ToArray();
        var breakHigh = structure.Length == 3 ? structure.Max(x => x.High) : decimal.MaxValue;
        var breakLow = structure.Length == 3 ? structure.Min(x => x.Low) : decimal.MinValue;
        var tick = _snapshot.InstrumentProfile.TickSize;
        if (intraday && _pendingEsRetest is { } pending)
        {
            if (!pending.Active(candle.Time, candle.Close))
            {
                _pendingEsRetest = null;
                LogLiveReadinessInfo($"ES_BREAKOUT_RETEST_EXPIRED event={pending.Started:O}");
            }
            else if (pending.RetestReady(candle.Close, tick))
                SubmitEsCrossMarketCandidate(candle, "ESResponseAlignedBreakout", pending.IsLong ? TradeSide.Long : TradeSide.Short,
                    "ESBreakoutRetest", true, pending.Trigger);
        }
        if (!intraday || candle.Time >= _nextEsIntradayAudit)
        {
        _nextEsIntradayAudit = candle.Time.AddSeconds(5);
        _researchLogger?.AppendInfo(_snapshot.SnapshotId, candle.Bar, candle.Time,
            $"CROSS_MARKET_ALIGNMENT auditOnly=true m1={System.Text.Json.JsonSerializer.Serialize(_alignment.Window(60))} m5={System.Text.Json.JsonSerializer.Serialize(_alignment.Window(300))}");
        _researchLogger?.AppendInfo(_snapshot.SnapshotId, candle.Bar, candle.Time,
            $"ES_CROSS_MARKET_CONTEXT m5Reference={context.Price5mReferenceAtUtc:O} m5Move={esMove:0.####} m1Move={(context.UnderlyingPrice - context.UnderlyingPrice60sAgo):0.####} atm={context.AtmQuotesReady} local={context.LocalQuoteCoverage:0.000} call={context.CallQuoteCoverage:0.000} put={context.PutQuoteCoverage:0.000} anchorDay={context.AnchorTradingDay:yyyy-MM-dd}");
        _researchLogger?.AppendInfo(_snapshot.SnapshotId, candle.Bar, candle.Time,
            $"ES_CROSS_MARKET_FUNNEL stage=Prefilter captured={context.CapturedAtUtc:O} coverage={context.Coverage:0.000} esMove={esMove:0.####} threshold={responseThreshold:0.####} reference={context.PriceReferenceAtUtc:O} range={range:0.####} longBody={longBody:0.####} shortBody={shortBody:0.####} breakHigh={(_previousCandle is not null && candle.Close > _previousCandle.High)} breakLow={(_previousCandle is not null && candle.Close < _previousCandle.Low)} esPrice={context.UnderlyingPrice} callWall={context.CallWallProxy} putWall={context.PutWallProxy} em={context.ExpectedMoveProxy} anchor={context.ExpectedMoveAnchor} anchorEm={context.ExpectedMoveAtAnchor} cluster={context.GammaConcentrationStrike} share={context.GammaConcentrationShare:0.000}");
        }

        if (Math.Abs(esMove) >= responseThreshold && range > 0m)
        {
            if (esMove > 0m && longBody >= range * 0.35m && candle.Close > breakHigh + tick)
                SubmitEsCrossMarketCandidate(candle, "ESResponseAlignedBreakout", TradeSide.Long, "ES5mUpStructureBreak", intraday, breakHigh);
            if (esMove < 0m && shortBody >= range * 0.35m && candle.Close < breakLow - tick)
                SubmitEsCrossMarketCandidate(candle, "ESResponseAlignedBreakout", TradeSide.Short, "ES5mDownStructureBreak", intraday, breakLow);
        }

        if (context.ExpectedMoveAtAnchor is > 0m && context.ExpectedMoveAnchor is { } anchor)
        {
            var displacement = context.UnderlyingPrice.Value - anchor;
            if (Math.Abs(displacement) >= context.ExpectedMoveAtAnchor.Value * 0.75m &&
                Math.Abs(displacement) <= context.ExpectedMoveAtAnchor.Value * 1.25m && range > 0m)
            {
                if (displacement > 0m && longBody >= range * 0.35m)
                    SubmitEsCrossMarketCandidate(candle, "ESExpectedMoveExpansion", TradeSide.Long, "ESExpectedMoveUp", intraday, breakHigh);
                if (displacement < 0m && shortBody >= range * 0.35m)
                    SubmitEsCrossMarketCandidate(candle, "ESExpectedMoveExpansion", TradeSide.Short, "ESExpectedMoveDown", intraday, breakLow);
            }
        }

        if (context.ExpectedMoveProxy is > 0m && range > 0m)
        {
            var wallReach = Math.Max(1m, context.ExpectedMoveProxy.Value * 0.15m);
            var putLevel = _levelEvents.TryGetValue("Put", out var putEvent) && putEvent.Active(candle.Time) ? putEvent.Level : context.PutWallProxy;
            var callLevel = _levelEvents.TryGetValue("Call", out var callEvent) && callEvent.Active(candle.Time) ? callEvent.Level : context.CallWallProxy;
            if (putLevel is { } putWall && Math.Abs(context.UnderlyingPrice.Value - putWall) <= wallReach && esMove >= responseThreshold && longBody >= range * 0.25m)
                SubmitEsCrossMarketCandidate(candle, "ESWallRejectionReversal", TradeSide.Long, "ESPutWallReject", intraday, _previousCandle?.High ?? candle.Open);
            if (callLevel is { } callWall && Math.Abs(context.UnderlyingPrice.Value - callWall) <= wallReach && esMove <= -responseThreshold && shortBody >= range * 0.25m)
                SubmitEsCrossMarketCandidate(candle, "ESWallRejectionReversal", TradeSide.Short, "ESCallWallReject", intraday, _previousCandle?.Low ?? candle.Open);
        }

        var gammaLevel = _levelEvents.TryGetValue("Gamma", out var gammaEvent) && gammaEvent.Active(candle.Time) ? gammaEvent.Level : context.GammaConcentrationStrike;
        if (gammaLevel is { } concentration && context.GammaConcentrationShare >= 0.18m && context.ExpectedMoveProxy is > 0m &&
            Math.Abs(context.UnderlyingPrice.Value - concentration) <= Math.Max(1m, context.ExpectedMoveProxy.Value * 0.2m) && range > 0m)
        {
            if (esMove >= responseThreshold && longBody >= range * 0.25m)
                SubmitEsCrossMarketCandidate(candle, "ESGammaConcentrationResponse", TradeSide.Long, "ESGammaClusterUp", intraday, _previousCandle?.High ?? candle.Open);
            if (esMove <= -responseThreshold && shortBody >= range * 0.25m)
                SubmitEsCrossMarketCandidate(candle, "ESGammaConcentrationResponse", TradeSide.Short, "ESGammaClusterDown", intraday, _previousCandle?.Low ?? candle.Open);
        }
    }

    private void SubmitEsCrossMarketCandidate(OpfCandle candle, string path, TradeSide side, string reason, bool intraday, decimal trigger)
    {
        if (!IsReplayExecutionPathEnabled(path)) return;
        var decisionUtc = IsHistoricalReplayTime(candle.Time) ? DateTime.SpecifyKind(candle.Time, DateTimeKind.Utc) : DateTime.UtcNow;
        EsOptionsProxySnapshot context;
        if (IsHistoricalReplayTime(candle.Time))
            context = OptionsProxySnapshotStore.ReadAsOf(OptionsProxyArchivePath, decisionUtc);
        else lock (_esOptionsProxySync) context = _esOptionsProxy;
        context = WithLevelEvent(context, path, side, decisionUtc);
        var gate = OptionsProxyEntryGate.Evaluate(context, decisionUtc, path, side == TradeSide.Long,
            candle.Open, candle.High, candle.Low, candle.Close, Math.Abs(candle.Close - (side == TradeSide.Long ? candle.Low - .5m : candle.High + .5m)));
        if (gate is not null)
        {
            if (_reactionAuditKeys.Add($"ES_GATE|{path}|{side}|{candle.Bar}|{gate}"))
                _researchLogger?.AppendInfo(_snapshot!.SnapshotId, candle.Bar, candle.Time, $"ES_CROSS_MARKET_REJECT path={path} side={side} reason={gate}");
            return;
        }
        var eventId = context.EventTouchedAtUtc is { } touch ? $"{path}-{side}-{touch:yyyyMMddHHmmssfffffff}" : $"{path}-{candle.Bar:000000}-{side}";
        var key = $"{path}|{side}|{candle.Bar}";
        if (intraday && _intradayAttempts.Contains(path + "|" + eventId)) return;
        if (intraday && _intradayAttempts.Contains(path + "|" + $"{path}-{candle.Bar:000000}-{side}")) return;
        if (!intraday && !_esCrossMarketTriggeredBars.Add(key))
            return;

        var stop = reason == "ESBreakoutRetest" && _pendingEsRetest is { } retest
            ? retest.Stop : side == TradeSide.Long ? candle.Low - 0.5m : candle.High + 0.5m;
        var risk = Math.Abs(candle.Close - stop);
        if (risk <= 0m)
            return;
        if (intraday && path == "ESResponseAlignedBreakout")
        {
            var replay = IsHistoricalReplayTime(candle.Time);
            // NQ and MNQ share index prices; isolate accounts and live/Replay streams.
            var anchorKey = BreakoutAnchorKey(side, replay);
            var proposed = new EsBreakoutEvent(candle.Time, side == TradeSide.Long, candle.Close, stop, trigger);
            var anchor = BreakoutAnchors.AddOrUpdate(anchorKey, proposed, (_, old) =>
                old.StoppedAt.HasValue || old.Active(candle.Time, candle.Close) ? old : proposed);
            if (anchor.StoppedAt is { } stoppedAt)
            {
                var referenceAt = context.Price5mReferenceAtUtc ?? DateTime.MinValue;
                if (anchor.NeedsFreshReference(referenceAt))
                {
                    if (_reactionAuditKeys.Add($"ES_BREAKOUT_COOLDOWN|{anchor.Started:O}|{side}|{referenceAt:O}"))
                        LogLiveReadinessInfo($"ES_BREAKOUT_COOLDOWN_WAIT side={side} stoppedAt={stoppedAt:O} newReference={referenceAt:O}");
                    return;
                }
                anchor = proposed;
                BreakoutAnchors[anchorKey] = anchor;
                LogLiveReadinessInfo($"ES_BREAKOUT_COOLDOWN_RELEASE side={side} stoppedAt={stoppedAt:O} newReference={referenceAt:O}");
            }
            trigger = anchor.Trigger;
            stop = anchor.Stop;
            risk = Math.Abs(candle.Close - stop);
            if (anchor.NeedsRetest(candle.Close) || reason == "ESBreakoutRetest" || _pendingEsRetest == anchor)
            {
                _pendingEsRetest = anchor;
                if (!anchor.RetestReady(candle.Close, _snapshot!.InstrumentProfile.TickSize))
                {
                    if (_reactionAuditKeys.Add($"ES_RETEST|{anchor.Started:O}|{side}"))
                        LogLiveReadinessInfo($"ES_BREAKOUT_RETEST_WAIT event={anchor.Started:O} side={side} anchorPrice={anchor.Price} anchorRisk={anchor.Risk} trigger={trigger} stop={stop} price={candle.Close}");
                    return;
                }
            }
            key = $"{path}-{side}-{anchor.Started:yyyyMMddHHmmssfffffff}";
            if (_intradayAttempts.Contains(path + "|" + key)) return;
        }

        var range = candle.High - candle.Low;
        if (range <= 0m) return;
        var bodyFraction = Math.Abs(candle.Close - candle.Open) / range;
        var directionalClose = side == TradeSide.Long ? (candle.Close - candle.Low) / range : (candle.High - candle.Close) / range;
        var quality = Math.Min(100m, 50m * bodyFraction + 50m * directionalClose);
        var score = new ScoreBreakdown(
            "ESCrossMarket",
            quality,
            45m,
            quality >= 45m,
            new[] { new ScoreComponent("CandleResponse", $"body={bodyFraction:0.000}|close={directionalClose:0.000}", quality >= 45m, 100m, quality, reason) });
        var signal = new CandidateSignal(
            intraday && path == "ESResponseAlignedBreakout" ? key : eventId,
            _snapshot!.SnapshotId,
            candle.Time,
            candle.Bar,
            side,
            SetupType.TrendPullback,
            SignalStage.Triggered,
            null,
            score,
            score,
            new[] { "IndependentESContext", reason });
        if (intraday)
        {
            if (!_esReactionPlans.TryGetValue(signal.SignalId, out var plan))
                _esReactionPlans[signal.SignalId] = plan = (trigger, stop, context.EventTouchedAtUtc?.AddMinutes(5) ??
                    (path == "ESResponseAlignedBreakout" ? candle.Time.AddSeconds(30) : GetCandle(candle.Bar).Time.AddMinutes(5)));
            if (_reactionAuditKeys.Add($"ES_EVENT|{eventId}"))
                LogLiveReadinessInfo($"ES_EVENT path={path} event={eventId} level={context.EventLevel} touched={context.EventTouchedAtUtc:O} kind={context.EventKind} anchorAt={context.AnchorEstablishedAtUtc:O} anchorSource={context.AnchorSource} esM5={context.UnderlyingPrice-context.UnderlyingPrice5mAgo} nqBody={candle.Close-candle.Open}");
            TryIntradayReaction(signal, candle, path, plan.Trigger, plan.Stop, plan.Expires);
            return;
        }
        _researchLogger?.AppendSignal(signal);
        AddResearchTracker(signal, candle, path, stop, risk, submitReplayExecution: false);
        AuditIntradayPair(signal, candle, path);
        _researchLogger?.AppendInfo(_snapshot.SnapshotId, candle.Bar, candle.Time,
            $"ES_CROSS_MARKET_CLOSED_BASELINE path={path} side={side} reason={reason}");
    }

    private void MarkEsBreakoutStopped(ReplayExecutionState execution)
    {
        var anchorKey = BreakoutAnchorKey(execution.Side, execution.HistoricalReplayAtCreation);
        if (!BreakoutAnchors.TryGetValue(anchorKey, out var anchor)) return;
        var stoppedAt = execution.ExitTime ?? DateTime.UtcNow;
        BreakoutAnchors[anchorKey] = anchor with { StoppedAt = stoppedAt };
        LogLiveReadinessInfo($"ES_BREAKOUT_STOP_COOLDOWN_ARMED trade={execution.TradeId} side={execution.Side} event={anchor.Started:O} stoppedAt={stoppedAt:O}");
    }

    private string BreakoutAnchorKey(TradeSide side, bool replay)
    {
        var contract = (Security?.Code ?? string.Empty).Replace("MNQ", "NQ", StringComparison.OrdinalIgnoreCase);
        return $"{LiveAccountScope.FromPortfolio(Portfolio)}|{contract}|{side}|{replay}";
    }
}
