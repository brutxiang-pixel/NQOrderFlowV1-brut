using OPFStrategyV1.Core.MarketData;
using OPFStrategyV1.Core.Signals;
using OPFStrategyV1.Research;
using System.Text;

namespace OPFStrategyV1.Strategy;

public sealed partial class OpeningPullbackFailureStrategy
{
    private readonly Dictionary<string, SignificantZoneShadow> _significantZoneShadows = new(StringComparer.Ordinal);
    private string _lastLiquiditySpikeHudText = "-";

    private sealed record LiquiditySpikeObservation(
        string EventType,
        TradeSide Side,
        decimal RelativeVolume20,
        decimal DeltaRatio,
        decimal PriceMoveAtr,
        decimal CloseLocation);

    private sealed record CandidateMicroContext(
        string EventType,
        string EventSide,
        decimal RelativeVolume20,
        decimal DeltaRatio,
        decimal PriceMoveAtr,
        decimal CloseLocation,
        decimal BigTradeThreshold,
        int BigTradeCount,
        int BigTradePriceClusters,
        decimal BigTradeVolume,
        bool AbsorptionLong,
        bool AbsorptionShort,
        string IcebergAvailability,
        SignificantZone? ContextZone,
        SignificantZoneShadow? ContextShadow,
        string Expectation);

    private void RegisterSignificantZoneShadow(SignificantZone zone, string source)
    {
        if (!_significantZoneShadows.ContainsKey(zone.ZoneId))
            _significantZoneShadows[zone.ZoneId] = SignificantZoneShadow.Create(source, zone.CreatedBar);
    }

    private void UpdateSignificantZoneShadow(SignificantZone zone, OpfCandle candle)
    {
        try
        {
            if (!_significantZoneShadows.TryGetValue(zone.ZoneId, out var shadow))
            {
                shadow = SignificantZoneShadow.Create(SignificantZoneSource(zone), zone.CreatedBar);
                _significantZoneShadows[zone.ZoneId] = shadow;
            }

            _significantZoneShadows[zone.ZoneId] = SignificantZoneShadowEvaluator.Update(zone, shadow, candle);
        }
        catch
        {
            // Shadow state must never affect the active strategy lifecycle.
        }
    }

    private void CaptureCandidateMicroContext(CandidateSignal signal, OpfCandle candle, string researchPath, decimal risk)
    {
        if (_snapshot is null || _researchLogger is null)
            return;

        try
        {
            var context = BuildCandidateMicroContext(signal, candle, researchPath, risk);
            var zone = context.ContextZone;
            var shadow = context.ContextShadow;
            _researchLogger.AppendCandidateMicroContext(_snapshot.SnapshotId, new ResearchLogger.CandidateMicroContext(
                signal.SignalId,
                candle.Time,
                candle.Bar,
                signal.Side.ToString(),
                researchPath,
                zone?.ZoneId ?? string.Empty,
                zone is null ? string.Empty : SignificantZoneSource(zone),
                zone?.Strength?.Grade.ToString() ?? string.Empty,
                zone?.Strength?.Score ?? 0m,
                zone?.State.ToString() ?? string.Empty,
                shadow?.State.ToString() ?? string.Empty,
                zone is null ? 0 : Math.Max(0, candle.Bar - zone.CreatedBar),
                zone?.TestCount ?? 0,
                zone is null ? 0m : SignificantZoneRanker.Distance(zone, candle.Close),
                context.Expectation,
                context.EventType,
                context.EventSide,
                context.RelativeVolume20,
                context.DeltaRatio,
                context.PriceMoveAtr,
                context.CloseLocation,
                context.BigTradeThreshold,
                context.BigTradeCount,
                context.BigTradePriceClusters,
                context.BigTradeVolume,
                context.AbsorptionLong,
                context.AbsorptionShort,
                context.IcebergAvailability));
        }
        catch
        {
            // A research write failure must not block an actual order decision.
        }
    }

    private CandidateMicroContext BuildCandidateMicroContext(CandidateSignal signal, OpfCandle candle, string researchPath, decimal risk)
    {
        var side = signal.Side;
        var barEnd = candle.Time.AddMinutes(5);
        var current = _footprintTrades.Where(x => x.Time >= candle.Time && x.Time < barEnd).ToArray();
        var history = _footprintTrades.Where(x => x.Time >= candle.Time.AddMinutes(-15) && x.Time < candle.Time).Select(x => x.Volume).OrderBy(x => x).ToArray();
        var spike = BuildLiquiditySpikeObservation(candle, side);
        var threshold = history.Length < 10 ? 0m : Math.Max(history[(int)Math.Floor((history.Length - 1) * 0.90m)] * 2m, history.Average() * 3m);
        var bigTrades = threshold <= 0m ? Array.Empty<FootprintTrade>() : current.Where(x => x.Volume >= threshold).ToArray();
        var eligible = _significantZones.Values.Where(SignificantZoneRanker.IsEligible).OrderBy(x => SignificantZoneRanker.Distance(x, candle.Close)).ToArray();
        var same = eligible.FirstOrDefault(x => x.Side == side);
        var opposing = eligible.FirstOrDefault(x => x.Side != side);
        var zone = eligible.FirstOrDefault(x => _significantZoneShadows.TryGetValue(x.ZoneId, out var state) && SignificantZoneShadowEvaluator.ExpectedSide(x, state).HasValue) ?? same ?? opposing;
        var shadow = zone is not null && _significantZoneShadows.TryGetValue(zone.ZoneId, out var selected) ? selected : null;
        var expected = zone is null || shadow is null ? null : SignificantZoneShadowEvaluator.ExpectedSide(zone, shadow);
        var target = TargetFromRisk(side, candle.Close, risk, ActualTargetRFor(signal, researchPath, risk));
        var caution = opposing is not null && IsZoneBeforeTarget(opposing, side, candle.Close, target);
        var expectation = expected == side ? "Favorable" : expected.HasValue ? "Adverse" : caution ? "Caution" : "Neutral";

        return new CandidateMicroContext(
            spike.EventType,
            spike.Side.ToString(),
            Math.Round(spike.RelativeVolume20, 4),
            Math.Round(spike.DeltaRatio, 4),
            Math.Round(spike.PriceMoveAtr, 4),
            Math.Round(spike.CloseLocation, 4),
            Math.Round(threshold, 4),
            bigTrades.Length,
            bigTrades.GroupBy(x => x.Price).Count(),
            bigTrades.Sum(x => x.Volume),
            HasAbsorptionAtLevel(TradeSide.Long, candle),
            HasAbsorptionAtLevel(TradeSide.Short, candle),
            "Unavailable:TradeTapeOnly",
            zone,
            shadow,
            expectation);
    }

    private void UpdateLiquiditySpikeHud(OpfCandle candle)
    {
        var spike = BuildLiquiditySpikeObservation(candle, TradeSide.Long);
        if (spike.EventType == "None")
            return;

        _lastLiquiditySpikeHudText = $"{spike.EventType} {spike.Side} {candle.Time:MM-dd HH:mm} relVol={spike.RelativeVolume20:0.##} delta={spike.DeltaRatio:0.##} move={spike.PriceMoveAtr:0.##}ATR";
        if (IsHistoricalReplayTime(candle.Time) || !_liveNotificationKeys.Add($"LiquiditySpike:{candle.Time:O}"))
            return;

        RaiseShowNotification(
            $"OPF 毛刺提示 | {spike.EventType} {spike.Side} | {candle.Time:HH:mm} | relVol={spike.RelativeVolume20:0.##} delta={spike.DeltaRatio:0.##} move={spike.PriceMoveAtr:0.##}ATR",
            "OPFStrategyV1");
        if (spike.EventType is "Initiative" or "Exhaustion")
            NotifyFeishuLiquiditySpike(candle, spike);
    }

    private void NotifyFeishuLiquiditySpike(OpfCandle candle, LiquiditySpikeObservation spike)
    {
        if (!_feishuNotificationEnabled ||
            !Uri.TryCreate(_feishuWebhookUrl, UriKind.Absolute, out var webhook) ||
            webhook.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(webhook.Host, "open.feishu.cn", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var text = $"【毛刺提示】OPF 订单流\n类型: {spike.EventType}\n方向: {spike.Side}\n时间: {candle.Time:yyyy-MM-dd HH:mm}\n相对量: {spike.RelativeVolume20:0.##}\nDelta比: {spike.DeltaRatio:0.##}\n位移: {spike.PriceMoveAtr:0.##} ATR\n说明: 观察提示，不触发下单";
        _researchLogger?.AppendInfo(_snapshot?.SnapshotId ?? "-", candle.Bar, candle.Time, $"FEISHU_LIQUIDITY_SPIKE_QUEUED type={spike.EventType}|side={spike.Side}");
        _ = SendFeishuLiquiditySpikeAsync(webhook, text, candle);
    }

    private async Task SendFeishuLiquiditySpikeAsync(Uri webhook, string text, OpfCandle candle)
    {
        try
        {
            using var content = new StringContent(FeishuTradeNotification.CreatePayload(text), Encoding.UTF8, "application/json");
            using var response = await FeishuHttpClient.PostAsync(webhook, content).ConfigureAwait(false);
            _researchLogger?.AppendInfo(_snapshot?.SnapshotId ?? "-", candle.Bar, candle.Time, $"FEISHU_LIQUIDITY_SPIKE_RESULT status={(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            _researchLogger?.AppendInfo(_snapshot?.SnapshotId ?? "-", candle.Bar, candle.Time, $"FEISHU_LIQUIDITY_SPIKE_FAILED error={ex.GetType().Name}");
        }
    }

    private SignificantZoneDecisionResult BuildLiveSignificantZoneDecision(TradeSide side, OpfCandle candle, decimal riskPoints)
    {
        var atr = CalculateAtr14(candle);
        var baseline = _significantZoneRanker.BuildContext(_significantZones.Values, side, candle.Close, atr, riskPoints);
        if (!Enum.TryParse<SignificantZoneDecision>(baseline.Decision, out var baselineDecision) ||
            baselineDecision == SignificantZoneDecision.StrongNegativeFilter)
        {
            return new SignificantZoneDecisionResult(baselineDecision, baseline.Reason);
        }

        var limit = Math.Max(riskPoints, Math.Max(atr / 2m, 5m));
        var candidates = _significantZones.Values
            .Where(SignificantZoneRanker.IsEligible)
            .Where(zone => zone.Strength?.Grade is SignificantZoneGrade.A or SignificantZoneGrade.B)
            .Where(zone => SignificantZoneRanker.Distance(zone, candle.Close) <= limit)
            .OrderByDescending(zone => zone.Strength?.Score ?? 0m)
            .ThenBy(zone => SignificantZoneRanker.Distance(zone, candle.Close));

        foreach (var zone in candidates)
        {
            if (!_significantZoneShadows.TryGetValue(zone.ZoneId, out var shadow))
                continue;

            var expected = SignificantZoneShadowEvaluator.ExpectedSide(zone, shadow);
            if (!expected.HasValue)
                continue;

            var grade = zone.Strength!.Grade;
            var reason = $"Shadow{shadow.State}:zone={zone.ZoneId}|grade={grade}|expected={expected}|distance={SignificantZoneRanker.Distance(zone, candle.Close):0.##}|limit={limit:0.##}";
            if (expected == side)
                return new SignificantZoneDecisionResult(SignificantZoneDecision.PositiveSignal, reason);
            if (grade == SignificantZoneGrade.A && shadow.State is SignificantZoneShadowState.Defending or SignificantZoneShadowState.Reclaimed)
                return new SignificantZoneDecisionResult(SignificantZoneDecision.StrongNegativeFilter, reason);
        }

        return new SignificantZoneDecisionResult(baselineDecision, baseline.Reason);
    }

    private LiquiditySpikeObservation BuildLiquiditySpikeObservation(OpfCandle candle, TradeSide fallbackSide)
    {
        var barEnd = candle.Time.AddMinutes(5);
        var current = _footprintTrades.Where(x => x.Time >= candle.Time && x.Time < barEnd).ToArray();
        var averageVolume = _recentCandles.TakeLast(20).Select(x => x.Volume).DefaultIfEmpty(candle.Volume).Average();
        var relativeVolume = averageVolume <= 0m ? 0m : candle.Volume / averageVolume;
        var total = current.Sum(x => x.Volume);
        var delta = current.Sum(x => x.SignedVolume);
        var deltaRatio = total <= 0m ? 0m : Math.Abs(delta) / total;
        var range = candle.High - candle.Low;
        var body = candle.Close - candle.Open;
        var moveAtr = Math.Abs(body) / Math.Max(CalculateAtr14(candle), 1m);
        var closeLocation = range <= 0m ? 0.5m : (candle.Close - candle.Low) / range;
        var side = delta > 0m ? TradeSide.Long : delta < 0m ? TradeSide.Short : fallbackSide;
        var directionalClose = side == TradeSide.Long ? closeLocation >= 0.75m : closeLocation <= 0.25m;
        var bodyAligned = side == TradeSide.Long ? body > 0m : body < 0m;
        var initiative = relativeVolume >= 1.8m && deltaRatio >= 0.35m && moveAtr >= 0.25m && directionalClose && bodyAligned;
        var exhaustion = relativeVolume >= 1.8m && deltaRatio >= 0.35m && (!bodyAligned || moveAtr <= 0.15m || !directionalClose);
        var sweepReclaimLong = _previousCandle is not null && candle.Low < _previousCandle.Low && candle.Close >= _previousCandle.Low + 0.5m;
        var sweepReclaimShort = _previousCandle is not null && candle.High > _previousCandle.High && candle.Close <= _previousCandle.High - 0.5m;
        var eventType = initiative ? "Initiative" : exhaustion ? "Exhaustion" : sweepReclaimLong || sweepReclaimShort ? "SweepReclaim" : "None";
        var classifiedSide = sweepReclaimLong ? TradeSide.Long : sweepReclaimShort ? TradeSide.Short : side;
        return new LiquiditySpikeObservation(eventType, classifiedSide, relativeVolume, deltaRatio, moveAtr, closeLocation);
    }

    private static string SignificantZoneSource(SignificantZone zone) =>
        zone.Evidence.TriggerEvidence.Contains("ImpulseLiquidity", StringComparison.OrdinalIgnoreCase)
            ? "ImpulseLiquidity"
            : zone.Evidence.PriceEvidence.Contains("FVG", StringComparison.OrdinalIgnoreCase)
                ? "StructuralFVG"
                : "Structural";

    private static bool IsZoneBeforeTarget(SignificantZone zone, TradeSide side, decimal entry, decimal target)
    {
        var low = Math.Min(zone.InnerBoundary, zone.OuterBoundary);
        var high = Math.Max(zone.InnerBoundary, zone.OuterBoundary);
        return side == TradeSide.Long
            ? low >= entry && low <= target
            : high <= entry && high >= target;
    }
}
