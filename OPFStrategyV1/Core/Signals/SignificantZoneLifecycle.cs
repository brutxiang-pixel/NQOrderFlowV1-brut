namespace OPFStrategyV1.Core.Signals;

public enum SignificantZoneState
{
    Observed,
    Active,
    TestedOnce,
    TestedTwice,
    Consumed,
    SuppressedByMomentum,
    Invalidated,
    Expired
}

public sealed record SignificantZoneEvidence(
    bool HasPriceSource,
    bool HasParticipationSource,
    string PriceEvidence,
    string ParticipationEvidence,
    string TriggerEvidence);

public sealed record SignificantZone(
    string ZoneId,
    TradeSide Side,
    decimal InnerBoundary,
    decimal OuterBoundary,
    DateTime CreatedTime,
    int CreatedBar,
    SignificantZoneState State,
    int TestCount,
    SignificantZoneEvidence Evidence,
    string LastReason = "Created",
    SignificantZoneStrength? Strength = null);

public sealed class SignificantZoneLifecycle
{
    public SignificantZone CreateObserved(string zoneId, TradeSide side, decimal innerBoundary, decimal outerBoundary, DateTime time, int bar, SignificantZoneEvidence evidence) =>
        new(zoneId, side, innerBoundary, outerBoundary, time, bar, SignificantZoneState.Observed, 0, evidence);

    public SignificantZone PromoteActive(SignificantZone zone, string reason) =>
        zone.State == SignificantZoneState.Observed && zone.Evidence.HasPriceSource && zone.Evidence.HasParticipationSource
            ? zone with { State = SignificantZoneState.Active, LastReason = reason }
            : zone;

    public SignificantZone RegisterMeaningfulTest(SignificantZone zone, string reason)
    {
        if (zone.State is not (SignificantZoneState.Active or SignificantZoneState.TestedOnce or SignificantZoneState.TestedTwice))
            return zone;
        var tests = zone.TestCount + 1;
        var state = tests switch
        {
            1 => SignificantZoneState.TestedOnce,
            2 => SignificantZoneState.TestedTwice,
            _ => SignificantZoneState.Consumed
        };
        return zone with { State = state, TestCount = tests, LastReason = reason };
    }

    public SignificantZone SuppressByMomentum(SignificantZone zone, string reason) =>
        zone.State is SignificantZoneState.Active or SignificantZoneState.TestedOnce
            ? zone with { State = SignificantZoneState.SuppressedByMomentum, LastReason = reason }
            : zone;

    public SignificantZone? TryReactivate(SignificantZone zone, string reason) =>
        zone.State == SignificantZoneState.SuppressedByMomentum
            ? zone with { State = zone.TestCount == 0 ? SignificantZoneState.Active : SignificantZoneState.TestedOnce, LastReason = reason }
            : null;

    public SignificantZone Invalidate(SignificantZone zone, string reason) =>
        IsTerminal(zone.State) ? zone : zone with { State = SignificantZoneState.Invalidated, LastReason = reason };

    public SignificantZone ExpireGlobexSession(SignificantZone zone, string reason) =>
        IsTerminal(zone.State) ? zone : zone with { State = SignificantZoneState.Expired, LastReason = reason };

    private static bool IsTerminal(SignificantZoneState state) =>
        state is SignificantZoneState.Consumed or SignificantZoneState.Invalidated or SignificantZoneState.Expired;
}


