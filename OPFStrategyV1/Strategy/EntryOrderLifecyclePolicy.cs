namespace OPFStrategyV1.Strategy;

internal static class EntryOrderLifecyclePolicy
{
    public static bool ShouldCancelIocRemainder(
        string researchPath,
        string orderState,
        decimal entryFilledQuantity,
        decimal orderUnfilledQuantity,
        bool exitCompleted) =>
        researchPath == IntradayEntryPolicy.IcebergAbsorptionScalpPath &&
        string.Equals(orderState, "Active", StringComparison.OrdinalIgnoreCase) &&
        entryFilledQuantity <= 0m &&
        orderUnfilledQuantity > 0m &&
        !exitCompleted;
}
