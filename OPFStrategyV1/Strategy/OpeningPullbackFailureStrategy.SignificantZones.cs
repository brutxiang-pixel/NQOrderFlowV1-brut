using ATAS.Indicators;
using OFT.Rendering.Context;
using OFT.Rendering.Tools;
using OPFStrategyV1.Core.MarketData;
using OPFStrategyV1.Core.Signals;
using OPFStrategyV1.Regime;
using OPFStrategyV1.Zones;
using System.Drawing;

namespace OPFStrategyV1.Strategy;

public sealed partial class OpeningPullbackFailureStrategy
{
    private const string SignificantZoneHistoryModelVersion = "SZ_RANK_1";
    private const int HistoricalSignificantZoneLookbackDays = 14;
    private const decimal HistoricalSignificantZoneClusterGapPoints = 2m;
    private readonly SignificantZoneLifecycle _significantZoneLifecycle = new();
    private readonly SignificantZoneRanker _significantZoneRanker = new();
    private readonly Dictionary<string, SignificantZone> _significantZones = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SignificantZoneHistoricalReference> _historicalSignificantZones = new(StringComparer.Ordinal);
    private SignificantZoneHistoryLedger? _significantZoneHistoryLedger;

    private void UpdateSignificantZones(OpfCandle candle, RegimeResult regime, IReadOnlyList<DetectedZone> zones)
    {
        if (!EnableResearchLogging || _snapshot is null || _researchLogger is null)
            return;

        foreach (var pair in _significantZones.ToArray())
        {
            if (GlobexTradingDayKey(pair.Value.CreatedTime) == GlobexTradingDayKey(candle.Time))
                continue;
            var expired = _significantZoneLifecycle.ExpireGlobexSession(pair.Value, "GlobexTradingDayRollover");
            if (expired == pair.Value)
                continue;
            _significantZones[pair.Key] = expired;
            PersistHistoricalSignificantZone(expired, candle.Time);
        }

        foreach (var detected in zones.Where(x => x.CreatedBar == candle.Bar))
        {
            var side = string.Equals(detected.Direction, "Bull", StringComparison.OrdinalIgnoreCase) ? TradeSide.Long : TradeSide.Short;
            var participation = CaptureSignificantZoneParticipation(candle, detected.Low, detected.High);
            var evidence = new SignificantZoneEvidence(
                HasPriceSource: detected.ZoneType.Contains("FVG", StringComparison.OrdinalIgnoreCase),
                HasParticipationSource: !string.IsNullOrWhiteSpace(detected.Source) && participation.HasObservedTrades,
                PriceEvidence: $"ZoneType={detected.ZoneType}",
                ParticipationEvidence: $"Source={detected.Source}|{participation}",
                TriggerEvidence: "ClosedM5FvgCreatedWithOnNewTradeEvidence");
            var strength = BuildSignificantZoneStrength(candle, detected, participation);
            var inner = side == TradeSide.Long ? detected.High : detected.Low;
            var outer = side == TradeSide.Long ? detected.Low : detected.High;
            var observed = _significantZoneLifecycle.CreateObserved($"SZ-{detected.ZoneId}", side, inner, outer, candle.Time, candle.Bar, evidence) with { Strength = strength };
            var active = _significantZoneLifecycle.PromoteActive(observed, "PriceAndParticipationEvidence");
            _significantZones[active.ZoneId] = active;
            PersistHistoricalSignificantZone(active, candle.Time);
        }

        foreach (var pair in _significantZones.ToArray())
        {
            var zone = pair.Value;
            if (zone.State is SignificantZoneState.Invalidated or SignificantZoneState.Expired or SignificantZoneState.Consumed)
                continue;

            if (zone.CreatedBar == candle.Bar)
                continue;

            TryQueueSignificantZoneFirstTouch(zone, candle, regime);
            var updated = UpdateSignificantZoneState(zone, candle, regime);
            if (updated == zone)
                continue;
            _significantZones[pair.Key] = updated;
            PersistHistoricalSignificantZone(updated, candle.Time);
        }
    }

    private SignificantZone UpdateSignificantZoneState(SignificantZone zone, OpfCandle candle, RegimeResult regime)
    {
        var low = Math.Min(zone.InnerBoundary, zone.OuterBoundary);
        var high = Math.Max(zone.InnerBoundary, zone.OuterBoundary);
        if (IsSignificantZoneInvalidated(zone, candle))
            return _significantZoneLifecycle.Invalidate(zone, "ClosedM5AcceptedBeyondOuterBoundary");

        var opposedByMomentum = (zone.Side == TradeSide.Long && regime.Regime == MarketRegime.BearTrend) ||
            (zone.Side == TradeSide.Short && regime.Regime == MarketRegime.BullTrend);
        if (opposedByMomentum)
            return _significantZoneLifecycle.SuppressByMomentum(zone, $"OpposingRegime={regime.Regime}");

        if (zone.State == SignificantZoneState.SuppressedByMomentum)
        {
            var reactivated = _significantZoneLifecycle.TryReactivate(zone, $"RegimeNoLongerOpposing={regime.Regime}");
            if (reactivated is not null)
                zone = reactivated;
        }

        return TryRegisterSignificantZoneTest(zone, candle, low, high, regime);
    }

    private SignificantZone TryRegisterSignificantZoneTest(SignificantZone zone, OpfCandle candle, decimal low, decimal high, RegimeResult regime)
    {
        if (candle.High < low || candle.Low > high)
            return zone;

        var participation = CaptureSignificantZoneParticipation(candle, low, high);
        if (!participation.HasObservedTrades)
            return zone;

        return _significantZoneLifecycle.RegisterMeaningfulTest(zone, $"OnNewTradeBehaviorBandTest|{participation}|close={candle.Close:0.########}");
    }

    private SignificantZoneParticipation CaptureSignificantZoneParticipation(OpfCandle candle, decimal low, decimal high)
    {
        var barEnd = candle.Time.AddMinutes(5);
        var trades = _footprintTrades.Where(x => x.Time >= candle.Time && x.Time < barEnd && x.Price >= low && x.Price <= high).ToArray();
        if (trades.Length == 0)
            return SignificantZoneParticipation.Empty;

        var buyVolume = trades.Where(x => x.SignedVolume > 0m).Sum(x => x.Volume);
        var sellVolume = trades.Where(x => x.SignedVolume < 0m).Sum(x => x.Volume);
        var byPrice = trades.GroupBy(x => x.Price).Select(x => x.Sum(y => y.Volume)).ToArray();
        return new SignificantZoneParticipation(trades.Length, buyVolume, sellVolume, buyVolume - sellVolume, trades.Sum(x => x.Volume), byPrice.Length, byPrice.Length == 0 ? 0m : byPrice.Max(), trades[0].Price, trades[^1].Price);
    }

    private SignificantZoneStrength BuildSignificantZoneStrength(OpfCandle candle, DetectedZone detected, SignificantZoneParticipation participation)
    {
        var barEnd = candle.Time.AddMinutes(5);
        var recent = _footprintTrades.Where(x => x.Time > barEnd.AddSeconds(-60) && x.Time <= barEnd).ToArray();
        var averageVolume = _recentCandles.TakeLast(20).Select(x => x.Volume).DefaultIfEmpty(candle.Volume).Average();
        return _significantZoneRanker.Score(new SignificantZoneStrengthInput(
            detected.High - detected.Low,
            Math.Abs(candle.Close - candle.Open),
            candle.High - candle.Low,
            CalculateAtr14(candle),
            candle.Volume,
            averageVolume,
            recent.Where(x => x.SignedVolume > 0m).Sum(x => x.Volume),
            recent.Where(x => x.SignedVolume < 0m).Sum(x => x.Volume),
            participation.BuyVolume,
            participation.SellVolume,
            participation.TradeCount,
            participation.PriceLevelCount,
            participation.LargestPriceLevelVolume));
    }

    private static bool IsSignificantZoneInvalidated(SignificantZone zone, OpfCandle candle) =>
        zone.Side == TradeSide.Long
            ? candle.Close < zone.OuterBoundary && candle.Open < zone.OuterBoundary
            : candle.Close > zone.OuterBoundary && candle.Open > zone.OuterBoundary;

    private void FinalizeSignificantZones()
    {
        if (!EnableResearchLogging || _snapshot is null || _researchLogger is null)
            return;
        foreach (var pair in _significantZones.ToArray())
        {
            var expired = _significantZoneLifecycle.ExpireGlobexSession(pair.Value, "StrategyStopped");
            if (expired == pair.Value)
                continue;
            _significantZones[pair.Key] = expired;
            PersistHistoricalSignificantZone(expired, DateTime.UtcNow);
        }
    }

    private void RestoreHistoricalSignificantZones()
    {
        if (_snapshot is null)
            return;

        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ATAS", "StrategyLogs", "OPFStrategyV1");
            _significantZoneHistoryLedger = new SignificantZoneHistoryLedger(directory);
            _historicalSignificantZones.Clear();
            foreach (var reference in _significantZoneHistoryLedger.Load(_snapshot.InstrumentProfile.Instrument, SignificantZoneHistoryModelVersion))
                _historicalSignificantZones[reference.ZoneId] = reference;
            _researchLogger?.AppendInfo(_snapshot.SnapshotId, 0, DateTime.UtcNow, $"SIGNIFICANT_ZONE_HISTORY_RESTORED count={_historicalSignificantZones.Count} model={SignificantZoneHistoryModelVersion}");
        }
        catch (Exception ex)
        {
            _researchLogger?.AppendInfo(_snapshot.SnapshotId, 0, DateTime.UtcNow, $"SIGNIFICANT_ZONE_HISTORY_RESTORE_FAILED type={ex.GetType().Name} message={ex.Message}");
        }
    }

    private void PersistHistoricalSignificantZone(SignificantZone zone, DateTime updatedAt)
    {
        if (_snapshot is null || zone.Strength?.Grade != SignificantZoneGrade.A)
            return;

        try
        {
            _significantZoneHistoryLedger ??= new SignificantZoneHistoryLedger(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ATAS", "StrategyLogs", "OPFStrategyV1"));
            _significantZoneHistoryLedger.Append(_snapshot.InstrumentProfile.Instrument, SignificantZoneHistoryModelVersion, zone, updatedAt);
            _historicalSignificantZones[zone.ZoneId] = new SignificantZoneHistoricalReference(
                _snapshot.InstrumentProfile.Instrument, SignificantZoneHistoryModelVersion, zone.ZoneId, zone.Side,
                zone.InnerBoundary, zone.OuterBoundary, zone.CreatedTime, zone.CreatedBar, zone.State, zone.TestCount,
                zone.Strength.Grade, zone.Strength.Score, zone.LastReason, updatedAt);
        }
        catch (Exception ex)
        {
            _researchLogger?.AppendInfo(_snapshot.SnapshotId, _lastSeenBar, updatedAt, $"SIGNIFICANT_ZONE_HISTORY_WRITE_FAILED type={ex.GetType().Name} message={ex.Message}");
        }
    }

    private void DrawSignificantZones(RenderContext context)
    {
        if (ChartInfo is null)
            return;
        var visible = _significantZoneRanker.Select(_significantZones.Values, _lastResearchCandle?.Close ?? 0m, _lastResearchCandle is null ? 0m : CalculateAtr14(_lastResearchCandle));
        foreach (var zone in visible.VisibleZones)
        {
            var x1 = Math.Max(ChartArea.X, ChartInfo.GetXByBar(zone.CreatedBar, true));
            var x2 = ChartArea.X + ChartArea.Width;
            var y1 = (int)ChartInfo.GetYByPrice(zone.OuterBoundary, false);
            var y2 = (int)ChartInfo.GetYByPrice(zone.InnerBoundary, false);
            var rect = Rectangle.FromLTRB(x1, Math.Min(y1, y2), x2, Math.Max(y1, y2));
            var color = GetSignificantZoneColor(zone);
            context.FillRectangle(Color.FromArgb(45, color), rect);
            context.DrawRectangle(new RenderPen(Color.FromArgb(180, color), 1), rect);
            var extreme = zone.ZoneId == visible.UpperExtreme?.ZoneId ? " UpperExtreme" : zone.ZoneId == visible.LowerExtreme?.ZoneId ? " LowerExtreme" : string.Empty;
            context.DrawString($"{zone.Strength?.Grade} {zone.Strength?.Score:0} {zone.Side} {zone.Evidence.PriceEvidence} {zone.State}{extreme} {zone.CreatedTime:HH:mm}", new RenderFont("Consolas", 9), color, x1 + 4, rect.Top + 2);
        }
        DrawHistoricalSignificantZones(context);
    }

    private void DrawHistoricalSignificantZones(RenderContext context)
    {
        if (_lastResearchCandle is null)
            return;

        var chart = ChartInfo!;
        var cutoff = GlobexTradingDayKey(_lastResearchCandle.Time).AddDays(-HistoricalSignificantZoneLookbackDays);
        var historical = _historicalSignificantZones.Values
            .Where(x => GlobexTradingDayKey(x.CreatedTime) >= cutoff)
            .Where(x => !_significantZones.ContainsKey(x.ZoneId))
            .Concat(_significantZones.Values
                .Where(x => x.Strength?.Grade == SignificantZoneGrade.A && !SignificantZoneRanker.IsEligible(x) && GlobexTradingDayKey(x.CreatedTime) >= cutoff)
                .Select(x => new SignificantZoneHistoricalReference(string.Empty, SignificantZoneHistoryModelVersion, x.ZoneId, x.Side, x.InnerBoundary, x.OuterBoundary, x.CreatedTime, x.CreatedBar, x.State, x.TestCount, x.Strength!.Grade, x.Strength.Score, x.LastReason, x.CreatedTime)))
            .OrderBy(x => x.CreatedTime)
            .ToArray();

        foreach (var cluster in SelectHistoricalSignificantZoneDisplay(historical, _lastResearchCandle.Close))
        {
            var zone = cluster.Representative;
            var x = ResolveHistoricalSignificantZoneX(zone);
            if (!x.HasValue)
                continue;
            var x1 = Math.Max(ChartArea.X, x.Value);
            var x2 = ChartArea.X + ChartArea.Width;
            var y1 = (int)chart.GetYByPrice(zone.OuterBoundary, false);
            var y2 = (int)chart.GetYByPrice(zone.InnerBoundary, false);
            var rect = Rectangle.FromLTRB(x1, Math.Min(y1, y2), x2, Math.Max(y1, y2));
            var color = Color.SteelBlue;
            context.FillRectangle(Color.FromArgb(22, color), rect);
            DrawHistoricalSignificantZoneBorder(context, rect, color);
            context.DrawString($"A Historical Cluster({cluster.Count}) {zone.State} {zone.Side} {zone.CreatedTime:MM-dd HH:mm}", new RenderFont("Consolas", 9), Color.FromArgb(180, color), x1 + 4, rect.Top + 2);
        }
    }

    private static IReadOnlyList<HistoricalSignificantZoneCluster> SelectHistoricalSignificantZoneDisplay(
        IReadOnlyList<SignificantZoneHistoricalReference> zones,
        decimal price)
    {
        var clusters = BuildHistoricalSignificantZoneClusters(zones);
        var selected = new List<HistoricalSignificantZoneCluster>(2);
        AddHistoricalCluster(selected, clusters
            .Where(x => x.Low <= price)
            .OrderBy(x => HistoricalClusterDistance(x, price))
            .ThenByDescending(x => x.Representative.Score)
            .FirstOrDefault());
        AddHistoricalCluster(selected, clusters
            .Where(x => x.High >= price)
            .OrderBy(x => HistoricalClusterDistance(x, price))
            .ThenByDescending(x => x.Representative.Score)
            .FirstOrDefault());
        return selected;
    }

    private static IReadOnlyList<HistoricalSignificantZoneCluster> BuildHistoricalSignificantZoneClusters(IReadOnlyList<SignificantZoneHistoricalReference> zones)
    {
        var clusters = new List<HistoricalSignificantZoneCluster>();
        var members = new List<SignificantZoneHistoricalReference>();
        var high = decimal.MinValue;
        foreach (var zone in zones.OrderBy(x => Math.Min(x.InnerBoundary, x.OuterBoundary)).ThenByDescending(x => x.Score))
        {
            var low = Math.Min(zone.InnerBoundary, zone.OuterBoundary);
            if (members.Count > 0 && low > high + HistoricalSignificantZoneClusterGapPoints)
            {
                clusters.Add(CreateHistoricalSignificantZoneCluster(members));
                members.Clear();
                high = decimal.MinValue;
            }
            members.Add(zone);
            high = Math.Max(high, Math.Max(zone.InnerBoundary, zone.OuterBoundary));
        }
        if (members.Count > 0)
            clusters.Add(CreateHistoricalSignificantZoneCluster(members));
        return clusters;
    }

    private static HistoricalSignificantZoneCluster CreateHistoricalSignificantZoneCluster(IReadOnlyList<SignificantZoneHistoricalReference> members)
    {
        var representative = members.OrderByDescending(x => x.Score).ThenByDescending(x => x.UpdatedAt).First();
        return new HistoricalSignificantZoneCluster(
            representative,
            members.Min(x => Math.Min(x.InnerBoundary, x.OuterBoundary)),
            members.Max(x => Math.Max(x.InnerBoundary, x.OuterBoundary)),
            members.Count);
    }

    private static void AddHistoricalCluster(ICollection<HistoricalSignificantZoneCluster> selected, HistoricalSignificantZoneCluster? cluster)
    {
        if (cluster is not null && !selected.Any(x => x.Representative.ZoneId == cluster.Representative.ZoneId))
            selected.Add(cluster);
    }

    private static decimal HistoricalClusterDistance(HistoricalSignificantZoneCluster cluster, decimal price) =>
        price < cluster.Low ? cluster.Low - price : price > cluster.High ? price - cluster.High : 0m;

    private int? ResolveHistoricalSignificantZoneX(SignificantZoneHistoricalReference zone)
    {
        var exact = _recentCandles.LastOrDefault(x => x.Time == zone.CreatedTime);
        if (exact is not null)
            return ChartInfo!.GetXByBar(exact.Bar, true);

        if (_recentCandles.Count == 0)
            return null;

        if (zone.CreatedTime < _recentCandles[0].Time)
            return ChartArea.X;

        var next = _recentCandles.FirstOrDefault(x => x.Time > zone.CreatedTime);
        return next is null ? null : ChartInfo!.GetXByBar(next.Bar, true);
    }

    private static void DrawHistoricalSignificantZoneBorder(RenderContext context, Rectangle rect, Color color)
    {
        var pen = new RenderPen(Color.FromArgb(150, color), 1);
        const int dash = 6;
        for (var x = rect.Left; x < rect.Right; x += dash * 2)
        {
            context.DrawLine(pen, x, rect.Top, Math.Min(x + dash, rect.Right), rect.Top);
            context.DrawLine(pen, x, rect.Bottom, Math.Min(x + dash, rect.Right), rect.Bottom);
        }
        for (var y = rect.Top; y < rect.Bottom; y += dash * 2)
        {
            context.DrawLine(pen, rect.Left, y, rect.Left, Math.Min(y + dash, rect.Bottom));
            context.DrawLine(pen, rect.Right, y, rect.Right, Math.Min(y + dash, rect.Bottom));
        }
    }

    private static Color GetSignificantZoneColor(SignificantZone zone) => zone.State switch
    {
        SignificantZoneState.Active => zone.Side == TradeSide.Long ? Color.LimeGreen : Color.Red,
        SignificantZoneState.TestedOnce => Color.Gold,
        SignificantZoneState.TestedTwice => Color.Orange,
        SignificantZoneState.SuppressedByMomentum => Color.Gray,
        _ => Color.FromArgb(110, Color.Gray)
    };

    private sealed record HistoricalSignificantZoneCluster(SignificantZoneHistoricalReference Representative, decimal Low, decimal High, int Count);

    private readonly record struct SignificantZoneParticipation(int TradeCount, decimal BuyVolume, decimal SellVolume, decimal Delta, decimal TotalVolume, int PriceLevelCount, decimal LargestPriceLevelVolume, decimal FirstPrice, decimal LastPrice)
    {
        public bool HasObservedTrades => TradeCount > 0;
        public static readonly SignificantZoneParticipation Empty = new(0, 0m, 0m, 0m, 0m, 0, 0m, 0m, 0m);
        public override string ToString() => $"trades={TradeCount}|buy={BuyVolume:0.########}|sell={SellVolume:0.########}|delta={Delta:0.########}|volume={TotalVolume:0.########}|levels={PriceLevelCount}|maxLevel={LargestPriceLevelVolume:0.########}|first={FirstPrice:0.########}|last={LastPrice:0.########}";
    }
}
