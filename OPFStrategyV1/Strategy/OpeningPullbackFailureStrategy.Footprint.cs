using ATAS.Indicators;
using OPFStrategyV1.Research;
using OPFStrategyV1.Core.Signals;
using OPFStrategyV1.Core.MarketData;
using OPFStrategyV1.Zones;

namespace OPFStrategyV1.Strategy;

public sealed partial class OpeningPullbackFailureStrategy
{
    private const decimal FootprintTickSize = 0.25m;
    private static readonly TimeSpan FootprintWindow = TimeSpan.FromMinutes(15);
    private readonly Queue<FootprintTrade> _footprintTrades = new();
    private readonly Dictionary<DateTime, Dictionary<decimal, FootprintLevel>> _footprintM5 = new();
    private long _footprintSequence;
    private DateTime? _footprintFirstTradeTime;
    private DateTime? _footprintLastTradeTime;

    private void InitializeFootprintCollection()
    {
        _footprintTrades.Clear();
        _footprintM5.Clear();
        _footprintSequence = 0;
        _footprintFirstTradeTime = null;
        _footprintLastTradeTime = null;
    }

    private void RecordFootprintTrade(MarketDataArg trade)
    {
        // Significant-zone activation uses the same bounded 15-minute trade window as
        // footprint research.  It must remain available in alert-only runs as well,
        // otherwise every new significant zone remains Observed and cannot be drawn.
        if ((!FootprintDataCollectionOnly && !SweepReclaimDataCollectionOnly && !EnableResearchLogging) || trade.Price <= 0m || trade.Volume <= 0m)
            return;

        var direction = trade.Direction.ToString();
        var signedVolume = direction == "Buy" ? trade.Volume : direction == "Sell" ? -trade.Volume : 0m;
        var item = new FootprintTrade(++_footprintSequence, trade.Time, trade.Price, trade.Volume, signedVolume);
        _footprintTrades.Enqueue(item);
        _footprintFirstTradeTime ??= trade.Time;
        _footprintLastTradeTime = !_footprintLastTradeTime.HasValue || trade.Time > _footprintLastTradeTime.Value
            ? trade.Time
            : _footprintLastTradeTime;
        var barTime = FloorToM5(trade.Time);
        if (!_footprintM5.TryGetValue(barTime, out var levels))
        {
            levels = new Dictionary<decimal, FootprintLevel>();
            _footprintM5[barTime] = levels;
        }
        if (!levels.TryGetValue(trade.Price, out var level))
            level = new FootprintLevel();
        level.Add(trade.Volume, signedVolume);
        levels[trade.Price] = level;

        var cutoff = trade.Time - FootprintWindow;
        while (_footprintTrades.Count > 0 && _footprintTrades.Peek().Time < cutoff)
            _footprintTrades.Dequeue();
        foreach (var stale in _footprintM5.Keys.Where(x => x < FloorToM5(cutoff)).ToArray())
            _footprintM5.Remove(stale);
    }

    private void CaptureFootprintCandidate(CandidateSignal signal, OpfCandle candle, string researchPath, decimal stop, decimal risk)
    {
        if (!FootprintDataCollectionOnly || _snapshot is null || _researchLogger is null || signal.Zone is null)
            return;

        var boundary = _footprintSequence;
        var referenceTime = _footprintLastTradeTime;
        var tick60Complete = referenceTime.HasValue && _footprintFirstTradeTime.HasValue &&
            referenceTime.Value - _footprintFirstTradeTime.Value >= TimeSpan.FromSeconds(60);
        var history15mComplete = referenceTime.HasValue && _footprintFirstTradeTime.HasValue &&
            referenceTime.Value - _footprintFirstTradeTime.Value >= FootprintWindow;
        var visible = _footprintTrades.Where(x => x.Sequence <= boundary).ToArray();
        var aggregate30 = AggregateFootprint(visible, referenceTime, TimeSpan.FromSeconds(30));
        var aggregate60 = AggregateFootprint(visible, referenceTime, TimeSpan.FromSeconds(60));
        var zoneTouch = CaptureZoneTouch(signal.Zone, visible, referenceTime);
        var poc = CapturePocMigration(referenceTime);
        var density = CaptureZoneDensity(signal.Zone, visible, referenceTime);

        _researchLogger.AppendFootprintFeature(_snapshot.SnapshotId, new ResearchLogger.FootprintCandidateFeature(
            signal.SignalId,
            candle.Time,
            candle.Bar,
            "ResearchCandidate",
            signal.Side.ToString(),
            researchPath,
            signal.Zone.ZoneId,
            signal.Zone.Low,
            signal.Zone.High,
            boundary,
            referenceTime,
            tick60Complete,
            history15mComplete,
            aggregate30.Count,
            aggregate30.BuyVolume,
            aggregate30.SellVolume,
            aggregate30.UnknownVolume,
            aggregate60.Count,
            aggregate60.BuyVolume,
            aggregate60.SellVolume,
            aggregate60.UnknownVolume,
            zoneTouch.Sequence,
            zoneTouch.Time,
            zoneTouch.Delta,
            zoneTouch.PriceChange,
            zoneTouch.DeltaPerSecond,
            zoneTouch.Divergence,
            poc.Poc1,
            poc.Poc2,
            poc.Poc3,
            poc.Migration1,
            poc.Migration2,
            density.ZoneVolume,
            density.OutsideVolume,
            density.ObservedLevels,
            density.ExpectedLevels,
            density.OccupiedRatio,
            density.MinObservedLevelVolume));
    }

    private static FootprintAggregate AggregateFootprint(IEnumerable<FootprintTrade> trades, DateTime? referenceTime, TimeSpan window)
    {
        if (!referenceTime.HasValue)
            return FootprintAggregate.Empty;

        var cutoff = referenceTime.Value - window;
        var selected = trades.Where(x => x.Time >= cutoff && x.Time <= referenceTime.Value).ToArray();
        return new FootprintAggregate(
            selected.Length,
            selected.Where(x => x.SignedVolume > 0m).Sum(x => x.Volume),
            selected.Where(x => x.SignedVolume < 0m).Sum(x => x.Volume),
            selected.Where(x => x.SignedVolume == 0m).Sum(x => x.Volume));
    }

    private static ZoneTouchFeature CaptureZoneTouch(DetectedZone zone, IReadOnlyList<FootprintTrade> trades, DateTime? referenceTime)
    {
        if (!referenceTime.HasValue)
            return ZoneTouchFeature.Empty;

        var first = trades.LastOrDefault(x => x.Price >= zone.Low && x.Price <= zone.High);
        if (first.Sequence == 0)
            return ZoneTouchFeature.Empty;

        var touchTrades = trades.Where(x => x.Sequence >= first.Sequence && x.Time <= referenceTime.Value).ToArray();
        var delta = touchTrades.Sum(x => x.SignedVolume);
        var priceChange = touchTrades.Length == 0 ? 0m : touchTrades[^1].Price - first.Price;
        var seconds = Math.Max(1d, (referenceTime.Value - first.Time).TotalSeconds);
        var divergence = priceChange == 0m || delta == 0m
            ? "FlatOrUnknown"
            : Math.Sign(priceChange) == Math.Sign(delta) ? "Aligned" : "Divergent";
        return new ZoneTouchFeature(first.Sequence, first.Time, delta, priceChange, decimal.Round(delta / (decimal)seconds, 6), divergence);
    }

    private PocFeature CapturePocMigration(DateTime? referenceTime)
    {
        if (!referenceTime.HasValue)
            return PocFeature.Empty;

        var current = FloorToM5(referenceTime.Value);
        var pocs = Enumerable.Range(1, 3)
            .Select(i => PocForBar(current.AddMinutes(-5 * i)))
            .ToArray();
        return new PocFeature(pocs[0], pocs[1], pocs[2], pocs[0] - pocs[1], pocs[1] - pocs[2]);
    }

    private decimal PocForBar(DateTime barTime)
    {
        if (!_footprintM5.TryGetValue(barTime, out var levels) || levels.Count == 0)
            return 0m;
        return levels.OrderByDescending(x => x.Value.TotalVolume).ThenBy(x => x.Key).First().Key;
    }

    private static ZoneDensityFeature CaptureZoneDensity(DetectedZone zone, IReadOnlyList<FootprintTrade> trades, DateTime? referenceTime)
    {
        if (!referenceTime.HasValue)
            return ZoneDensityFeature.Empty;

        var selected = trades.Where(x => x.Time <= referenceTime.Value).ToArray();
        var inZone = selected.Where(x => x.Price >= zone.Low && x.Price <= zone.High).ToArray();
        var byPrice = inZone.GroupBy(x => x.Price).Select(x => x.Sum(y => y.Volume)).ToArray();
        var expected = Math.Max(1, (int)decimal.Floor((zone.High - zone.Low) / FootprintTickSize) + 1);
        var observed = byPrice.Length;
        return new ZoneDensityFeature(
            inZone.Sum(x => x.Volume),
            selected.Where(x => x.Price < zone.Low || x.Price > zone.High).Sum(x => x.Volume),
            observed,
            expected,
            decimal.Round((decimal)observed / expected, 6),
            byPrice.Length == 0 ? 0m : byPrice.Min());
    }

    private static DateTime FloorToM5(DateTime time) => new(time.Year, time.Month, time.Day, time.Hour, time.Minute / 5 * 5, 0, time.Kind);

    private void FlushFootprintCollection()
    {
        _footprintTrades.Clear();
        _footprintM5.Clear();
    }

    private readonly record struct FootprintTrade(long Sequence, DateTime Time, decimal Price, decimal Volume, decimal SignedVolume);
    private struct FootprintLevel
    {
        public decimal TotalVolume { get; private set; }
        public void Add(decimal volume, decimal _) => TotalVolume += volume;
    }
    private readonly record struct FootprintAggregate(int Count, decimal BuyVolume, decimal SellVolume, decimal UnknownVolume)
    {
        public static readonly FootprintAggregate Empty = new(0, 0m, 0m, 0m);
    }
    private readonly record struct ZoneTouchFeature(long? Sequence, DateTime? Time, decimal Delta, decimal PriceChange, decimal DeltaPerSecond, string Divergence)
    {
        public static readonly ZoneTouchFeature Empty = new(null, null, 0m, 0m, 0m, "NoObservedTouch");
    }
    private readonly record struct PocFeature(decimal Poc1, decimal Poc2, decimal Poc3, decimal Migration1, decimal Migration2)
    {
        public static readonly PocFeature Empty = new(0m, 0m, 0m, 0m, 0m);
    }
    private readonly record struct ZoneDensityFeature(decimal ZoneVolume, decimal OutsideVolume, int ObservedLevels, int ExpectedLevels, decimal OccupiedRatio, decimal MinObservedLevelVolume)
    {
        public static readonly ZoneDensityFeature Empty = new(0m, 0m, 0, 0, 0m, 0m);
    }
}
