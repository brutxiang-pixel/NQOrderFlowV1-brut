namespace OPFStrategyV1.Strategy;

public static class LiveBracketConfirmationGate
{
    public static bool RequiresEmergencyFlatten(
        decimal entryFilledQuantity,
        decimal exitFilledQuantity,
        bool stopConfirmed,
        bool targetConfirmed,
        bool exitCompleted)
    {
        return entryFilledQuantity > exitFilledQuantity &&
            !exitCompleted &&
            (!stopConfirmed || !targetConfirmed);
    }
}
