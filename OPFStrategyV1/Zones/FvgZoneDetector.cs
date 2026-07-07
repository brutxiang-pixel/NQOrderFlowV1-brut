using OPFStrategyV1.Core.MarketData;
using OPFStrategyV1.Core.Versions;

namespace OPFStrategyV1.Zones;

public sealed class FvgZoneDetector : IZoneDetector
{
    private readonly decimal _minGapPoints;
    private readonly decimal _maxGapPoints;
    private readonly decimal _maxSourceCandleRangePoints;
    private readonly List<OpfCandle> _candles = new();
    private readonly List<ZoneState> _zones = new();

    public FvgZoneDetector(decimal minGapPoints, decimal maxGapPoints, decimal maxSourceCandleRangePoints)
    {
        _minGapPoints = Math.Max(0m, minGapPoints);
        _maxGapPoints = Math.Max(_minGapPoints, maxGapPoints);
        _maxSourceCandleRangePoints = Math.Max(0m, maxSourceCandleRangePoints);
    }

    public IReadOnlyList<DetectedZone> Update(OpfCandle candle)
    {
        _candles.Add(candle);
        DetectNewFvg();
        UpdateZoneStates(candle);
        return _zones
            .Where(x => !x.Invalidated)
            .Select(x => x.ToDetectedZone())
            .ToArray();
    }

    private void DetectNewFvg()
    {
        if (_candles.Count < 3)
            return;

        var left = _candles[^3];
        var middle = _candles[^2];
        var right = _candles[^1];

        if (IsAbnormalSourceCandle(left) || IsAbnormalSourceCandle(middle) || IsAbnormalSourceCandle(right))
            return;

        var bullGap = right.Low - left.High;
        if (IsValidGap(bullGap))
            AddZone("BullFVG", "Bull", left.High, right.Low, right);

        var bearGap = left.Low - right.High;
        if (IsValidGap(bearGap))
            AddZone("BearFVG", "Bear", right.High, left.Low, right);
    }

    private bool IsValidGap(decimal gap)
    {
        return gap >= _minGapPoints && gap <= _maxGapPoints;
    }

    private bool IsAbnormalSourceCandle(OpfCandle candle)
    {
        return _maxSourceCandleRangePoints > 0m && candle.High - candle.Low > _maxSourceCandleRangePoints;
    }

    private void AddZone(string zoneType, string direction, decimal low, decimal high, OpfCandle created)
    {
        if (_zones.Any(x => x.ZoneType == zoneType && Math.Abs(x.Low - low) < 0.0001m && Math.Abs(x.High - high) < 0.0001m))
            return;

        var zoneId = $"{created.Time:yyyyMMdd-HHmm}-{zoneType}-{created.Bar:000000}";
        _zones.Add(new ZoneState(zoneId, zoneType, direction, low, high, created.Time, created.Bar));

        if (_zones.Count > 80)
            _zones.RemoveRange(0, _zones.Count - 80);
    }

    private void UpdateZoneStates(OpfCandle candle)
    {
        foreach (var zone in _zones)
        {
            if (zone.Invalidated)
                continue;

            var touched = candle.Bar != zone.CreatedBar && candle.High >= zone.Low && candle.Low <= zone.High;
            if (touched)
                zone.TouchCount++;

            var mid = zone.Low + (zone.High - zone.Low) / 2m;
            if (zone.Direction == "Bull")
            {
                if (candle.Low <= mid)
                    zone.Mitigated = true;
                if (candle.Close < zone.Low)
                    zone.Invalidated = true;
            }
            else
            {
                if (candle.High >= mid)
                    zone.Mitigated = true;
                if (candle.Close > zone.High)
                    zone.Invalidated = true;
            }
        }
    }

    private sealed class ZoneState
    {
        public ZoneState(string zoneId, string zoneType, string direction, decimal low, decimal high, DateTime createdTime, int createdBar)
        {
            ZoneId = zoneId;
            ZoneType = zoneType;
            Direction = direction;
            Low = low;
            High = high;
            CreatedTime = createdTime;
            CreatedBar = createdBar;
        }

        public string ZoneId { get; }
        public string ZoneType { get; }
        public string Direction { get; }
        public decimal Low { get; }
        public decimal High { get; }
        public DateTime CreatedTime { get; }
        public int CreatedBar { get; }
        public int TouchCount { get; set; }
        public bool Mitigated { get; set; }
        public bool Invalidated { get; set; }

        public DetectedZone ToDetectedZone()
        {
            return new DetectedZone(
                ZoneId,
                ZoneType,
                Direction,
                Low,
                High,
                CreatedTime,
                CreatedBar,
                Freshness(),
                TouchCount,
                Mitigated,
                "M5FVG",
                StrategyVersions.ZoneDetectorVersion);
        }

        private string Freshness()
        {
            return TouchCount switch
            {
                0 => "Fresh",
                1 => "SecondTouch",
                2 => "ThirdTouch",
                _ => "Mitigated"
            };
        }
    }
}
