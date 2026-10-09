namespace OPFStrategyV1.Core.Signals;

public static class HistoricalSignificantZoneSideSelector
{
    public static IReadOnlyList<SignificantZoneHistoricalReference> Below(
        IEnumerable<SignificantZoneHistoricalReference> zones,
        decimal price) =>
        zones.Where(x => Math.Max(x.InnerBoundary, x.OuterBoundary) <= price).ToArray();

    public static IReadOnlyList<SignificantZoneHistoricalReference> Above(
        IEnumerable<SignificantZoneHistoricalReference> zones,
        decimal price) =>
        zones.Where(x => Math.Min(x.InnerBoundary, x.OuterBoundary) >= price).ToArray();
}
