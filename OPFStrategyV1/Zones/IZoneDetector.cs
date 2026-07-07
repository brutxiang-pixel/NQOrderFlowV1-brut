using OPFStrategyV1.Core.MarketData;

namespace OPFStrategyV1.Zones;

public interface IZoneDetector
{
    IReadOnlyList<DetectedZone> Update(OpfCandle candle);
}
