using ATAS.Indicators;
using OPFStrategyV1.Core.MarketData;
using OPFStrategyV1.Research;
using OPFStrategyV1.Zones;

namespace OPFStrategyV1.Strategy;

public sealed partial class OpeningPullbackFailureStrategy
{
    private readonly Dictionary<DateTime, ZoneBehaviorBarData> _zoneBehaviorM5 = new();
    private readonly Dictionary<string, ZoneBehaviorState> _zoneBehaviorStates = new();
    private long _zoneBehaviorSequence;
    private bool ZoneContextCollectionEnabled => ZoneBehaviorLedgerDataOnly || MarketExecutionTapeDataOnly || CandidateScenarioTapeDataOnly;
    private bool ZoneBehaviorOutputEnabled => ZoneBehaviorLedgerDataOnly || MarketExecutionTapeDataOnly;

    private void InitializeZoneBehaviorLedger()
    {
        _zoneBehaviorM5.Clear();
        _zoneBehaviorStates.Clear();
        _zoneBehaviorSequence = 0;
    }

    private void RecordZoneBehaviorTrade(MarketDataArg trade)
    {
        if (!ZoneContextCollectionEnabled || trade.Price <= 0m || trade.Volume <= 0m)
            return;

        var direction = trade.Direction.ToString();
        var bucket = FloorToM5(trade.Time);
        if (!_zoneBehaviorM5.TryGetValue(bucket, out var barData))
        {
            barData = new ZoneBehaviorBarData();
            _zoneBehaviorM5[bucket] = barData;
        }
        barData.Add(trade.Price, direction, trade.Volume, ++_zoneBehaviorSequence, trade.Time);
    }

    private void UpdateZoneBehaviorLedger(OpfCandle candle, IReadOnlyList<DetectedZone> activeZones, IReadOnlyList<ZoneLifecycleEvent> lifecycleEvents)
    {
        if (!ZoneContextCollectionEnabled || _snapshot is null || _researchLogger is null)
            return;

        foreach (var birth in lifecycleEvents.Where(x => x.EventType == "Birth"))
            StartZoneBehavior(birth);

        var zonesForBar = activeZones
            .Concat(lifecycleEvents.Where(x => x.EventType is "Invalidated" or "Expired").Select(x => x.Zone))
            .GroupBy(x => x.ZoneId)
            .Select(x => x.First())
            .ToArray();
        foreach (var zone in zonesForBar)
        {
            if (_zoneBehaviorStates.TryGetValue(zone.ZoneId, out var state) && !state.Terminal)
                RecordZoneBehaviorBar(candle, state);
        }

        foreach (var terminal in lifecycleEvents.Where(x => x.EventType is "Invalidated" or "Expired"))
            EndZoneBehavior(terminal);
    }

    private void StartZoneBehavior(ZoneLifecycleEvent lifecycle)
    {
        if (_zoneBehaviorStates.ContainsKey(lifecycle.Zone.ZoneId))
            return;

        var state = new ZoneBehaviorState(lifecycle.Zone);
        _zoneBehaviorStates.Add(lifecycle.Zone.ZoneId, state);
        if (!ZoneBehaviorOutputEnabled)
            return;
        var eventId = EventId(lifecycle.Zone.ZoneId, "Birth", 0);
        AppendZoneBehaviorEvent(eventId, lifecycle.EventType, lifecycle.Time, lifecycle.Bar, state);
        AppendZoneBehaviorPriceLevels(eventId, state.Zone, lifecycle.Time);
    }

    private void RecordZoneBehaviorBar(OpfCandle candle, ZoneBehaviorState state)
    {
        var barData = BarDataFor(candle.Time);
        var levels = barData.Levels;
        var touching = candle.Bar != state.Zone.CreatedBar && candle.High >= state.Zone.Low && candle.Low <= state.Zone.High;
        if (touching && !state.TouchEpisodeActive)
        {
            state.TouchOrdinal++;
            state.LastTouchTime = candle.Time;
            state.LastTouchBar = candle.Bar;
            state.LastTouchMarketSequence = barData.LastSequence;
            if (ZoneBehaviorOutputEnabled)
            {
                var eventId = EventId(state.Zone.ZoneId, "Touch", state.TouchOrdinal);
                AppendZoneBehaviorEvent(eventId, "Touch", candle.Time, candle.Bar, state);
                AppendZoneBehaviorPriceLevels(eventId, state.Zone, candle.Time);
            }
        }
        state.TouchEpisodeActive = touching;

        var all = Aggregate(levels.Values);
        var inZone = Aggregate(levels.Where(x => x.Key >= state.Zone.Low && x.Key <= state.Zone.High).Select(x => x.Value));
        state.CumulativeAll.Add(all);
        state.CumulativeZone.Add(inZone);
        state.MaxFavorableExcursion = Math.Max(state.MaxFavorableExcursion, FavorableExcursion(state.Zone, candle));
        state.MaxAdverseExcursion = Math.Max(state.MaxAdverseExcursion, AdverseExcursion(state.Zone, candle));
        if (!ZoneBehaviorOutputEnabled)
            return;
        _researchLogger!.AppendZoneBehaviorBar(_snapshot!.SnapshotId, new ResearchLogger.ZoneBehaviorBar(
            state.Zone.ZoneId, candle.Time, candle.Bar, candle.Open, candle.High, candle.Low, candle.Close, touching, state.TouchOrdinal,
            barData.LastSequence, barData.LastTime, levels.Count > 0,
            all.Buy, all.Sell, all.Unknown, inZone.Buy, inZone.Sell, inZone.Unknown,
            state.CumulativeAll.Buy, state.CumulativeAll.Sell, state.CumulativeAll.Unknown,
            state.CumulativeZone.Buy, state.CumulativeZone.Sell, state.CumulativeZone.Unknown,
            state.MaxFavorableExcursion, state.MaxAdverseExcursion));
    }

    private void EndZoneBehavior(ZoneLifecycleEvent lifecycle)
    {
        if (!_zoneBehaviorStates.TryGetValue(lifecycle.Zone.ZoneId, out var state) || state.Terminal)
            return;
        if (ZoneBehaviorOutputEnabled)
            AppendZoneBehaviorEvent(EventId(state.Zone.ZoneId, lifecycle.EventType, state.TouchOrdinal), lifecycle.EventType, lifecycle.Time, lifecycle.Bar, state);
        state.Terminal = true;
    }

    private void FinalizeZoneBehaviorLedger()
    {
        if (!ZoneContextCollectionEnabled || _snapshot is null || _researchLogger is null)
            return;

        var time = _lastResearchCandle?.Time ?? DateTime.UtcNow;
        var bar = _lastResearchCandle?.Bar ?? 0;
        foreach (var state in _zoneBehaviorStates.Values.Where(x => !x.Terminal))
        {
            if (ZoneBehaviorOutputEnabled)
                AppendZoneBehaviorEvent(EventId(state.Zone.ZoneId, "SnapshotEnd", state.TouchOrdinal), "SnapshotEnd", time, bar, state);
            state.Terminal = true;
        }
        _zoneBehaviorM5.Clear();
    }

    private void AppendZoneBehaviorEvent(string eventId, string eventType, DateTime time, int bar, ZoneBehaviorState state)
    {
        var barData = BarDataFor(time);
        _researchLogger!.AppendZoneBehaviorEvent(_snapshot!.SnapshotId, new ResearchLogger.ZoneBehaviorEvent(
            eventId, state.Zone.ZoneId, eventType, time, bar, state.TouchOrdinal, state.Zone.ZoneType, state.Zone.Direction,
            state.Zone.Low, state.Zone.High, barData.LastSequence, barData.LastTime));
    }

    private void AppendZoneBehaviorPriceLevels(string eventId, DetectedZone zone, DateTime time)
    {
        var barData = BarDataFor(time);
        foreach (var level in barData.Levels.OrderBy(x => x.Key))
            _researchLogger!.AppendZoneBehaviorPriceLevel(_snapshot!.SnapshotId, new ResearchLogger.ZoneBehaviorPriceLevel(
                eventId, zone.ZoneId, level.Key, level.Value.Buy, level.Value.Sell, level.Value.Unknown,
                barData.LastSequence, barData.LastTime));
    }

    private ZoneBehaviorBarData BarDataFor(DateTime time)
    {
        return _zoneBehaviorM5.TryGetValue(FloorToM5(time), out var data)
            ? data
            : ZoneBehaviorBarData.Empty;
    }

    private static ZoneBehaviorVolumes Aggregate(IEnumerable<ZoneBehaviorLevel> levels)
    {
        var result = new ZoneBehaviorVolumes();
        foreach (var level in levels)
            result.Add(level);
        return result;
    }

    private static decimal FavorableExcursion(DetectedZone zone, OpfCandle candle) => zone.Direction == "Bull"
        ? Math.Max(0m, candle.High - zone.High)
        : Math.Max(0m, zone.Low - candle.Low);

    private static decimal AdverseExcursion(DetectedZone zone, OpfCandle candle) => zone.Direction == "Bull"
        ? Math.Max(0m, zone.Low - candle.Low)
        : Math.Max(0m, candle.High - zone.High);

    private static string EventId(string zoneId, string eventType, int ordinal) => $"{zoneId}:{eventType}:{ordinal}";

    private sealed class ZoneBehaviorState
    {
        public ZoneBehaviorState(DetectedZone zone) => Zone = zone;
        public DetectedZone Zone { get; }
        public int TouchOrdinal { get; set; }
        public bool TouchEpisodeActive { get; set; }
        public bool Terminal { get; set; }
        public DateTime? LastTouchTime { get; set; }
        public int? LastTouchBar { get; set; }
        public long LastTouchMarketSequence { get; set; }
        public ZoneBehaviorVolumes CumulativeAll { get; } = new();
        public ZoneBehaviorVolumes CumulativeZone { get; } = new();
        public decimal MaxFavorableExcursion { get; set; }
        public decimal MaxAdverseExcursion { get; set; }
    }

    private struct ZoneBehaviorLevel
    {
        public decimal Buy { get; private set; }
        public decimal Sell { get; private set; }
        public decimal Unknown { get; private set; }
        public void Add(string direction, decimal volume)
        {
            if (direction == "Buy") Buy += volume;
            else if (direction == "Sell") Sell += volume;
            else Unknown += volume;
        }
    }

    private sealed class ZoneBehaviorBarData
    {
        public static readonly ZoneBehaviorBarData Empty = new();
        public Dictionary<decimal, ZoneBehaviorLevel> Levels { get; } = new();
        public long LastSequence { get; private set; }
        public DateTime? LastTime { get; private set; }
        public void Add(decimal price, string direction, decimal volume, long sequence, DateTime time)
        {
            if (!Levels.TryGetValue(price, out var level))
                level = new ZoneBehaviorLevel();
            level.Add(direction, volume);
            Levels[price] = level;
            LastSequence = Math.Max(LastSequence, sequence);
            LastTime = !LastTime.HasValue || time > LastTime.Value ? time : LastTime;
        }
    }

    private sealed class ZoneBehaviorVolumes
    {
        public decimal Buy { get; private set; }
        public decimal Sell { get; private set; }
        public decimal Unknown { get; private set; }
        public void Add(ZoneBehaviorLevel value) => Add(value.Buy, value.Sell, value.Unknown);
        public void Add(ZoneBehaviorVolumes value) => Add(value.Buy, value.Sell, value.Unknown);
        private void Add(decimal buy, decimal sell, decimal unknown)
        {
            Buy += buy;
            Sell += sell;
            Unknown += unknown;
        }
    }
}
