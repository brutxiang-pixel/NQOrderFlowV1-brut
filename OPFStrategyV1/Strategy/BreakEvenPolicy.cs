namespace OPFStrategyV1.Strategy;

internal static class BreakEvenPolicy
{
    public static decimal Stop(decimal entry, bool isLong, decimal commissionPerContract, decimal pointValue, decimal tick)
    {
        var offset = Math.Ceiling(commissionPerContract / pointValue / tick) * tick;
        return isLong
            ? Math.Ceiling((entry + offset) / tick) * tick
            : Math.Floor((entry - offset) / tick) * tick;
    }
}
