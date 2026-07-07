namespace OPFStrategyV1.Zones;

public sealed record DetectedZone(
    string ZoneId,
    string ZoneType,
    string Direction,
    decimal Low,
    decimal High,
    DateTime CreatedTime,
    int CreatedBar,
    string Freshness,
    int TouchCount,
    bool Mitigated,
    string Source,
    string DetectorVersion);
