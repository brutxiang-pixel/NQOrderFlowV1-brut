namespace OPFStrategyV1.Zones;

public sealed record ZoneLifecycleEvent(
    string EventType,
    DetectedZone Zone,
    DateTime Time,
    int Bar);

public interface IZoneLifecycleEventSource
{
    IReadOnlyList<ZoneLifecycleEvent> DrainLifecycleEvents();
}
