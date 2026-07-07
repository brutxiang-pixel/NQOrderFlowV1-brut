using OPFStrategyV1.Core.MarketData;

namespace OPFStrategyV1.Zones;

public sealed class NoOpZoneDetector : IZoneDetector
{
    public IReadOnlyList<DetectedZone> Update(OpfCandle candle)
    {
        return Array.Empty<DetectedZone>();
    }
}
