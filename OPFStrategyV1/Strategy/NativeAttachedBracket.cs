using ATAS.DataFeedsCore;

namespace OPFStrategyV1.Strategy;

public static class NativeAttachedBracket
{
    public static decimal AlignPriceToTick(decimal price, decimal tickSize) =>
        tickSize <= 0m
            ? price
            : Math.Round(price / tickSize, 0, MidpointRounding.AwayFromZero) * tickSize;

    public static bool IsTickAligned(decimal price, decimal tickSize) =>
        tickSize > 0m && price == AlignPriceToTick(price, tickSize);

    public static void Configure(Order entry, Order stop, Order target, string ocoGroup)
    {
        stop.Parent = entry;
        stop.IsAttached = true;
        stop.OCOGroup = ocoGroup;

        target.Parent = entry;
        target.IsAttached = true;
        target.OCOGroup = ocoGroup;
    }
}
