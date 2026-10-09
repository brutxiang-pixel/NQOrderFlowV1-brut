namespace OPFStrategyV1.Strategy;

public static class LiveProtectionFailClosedGuard
{
    public static bool RequiresEmergencyFlatten(
        decimal entryFilledQty,
        decimal exitFilledQty,
        bool hasWorkingStop,
        bool exitCompleted,
        bool emergencyFlattenSubmitted)
    {
        return entryFilledQty > exitFilledQty &&
            !hasWorkingStop &&
            !exitCompleted &&
            !emergencyFlattenSubmitted;
    }
}
