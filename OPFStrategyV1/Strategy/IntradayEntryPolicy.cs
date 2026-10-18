using OPFStrategyV1.Core.MarketData;

namespace OPFStrategyV1.Strategy;

public sealed record CompletedFrame(string Name, DateTime Time, decimal High, decimal Low, decimal Close, decimal Slope, int Count);

public static class IntradayEntryPolicy
{
    public static string? BreakoutChase(bool isLong, decimal price, decimal stop, decimal anchorPrice, decimal anchorRisk)
    {
        var advance = isLong ? price - anchorPrice : anchorPrice - price;
        if (advance > anchorRisk * .5m) return "CrossMarketChaseTooFar";
        return Math.Abs(price - stop) > anchorRisk * 1.25m ? "CrossMarketRiskExpanded" : null;
    }

    // End time is explicit: daily sessions must use the next session's opening time.
    public static OpfCandle[] ClosedAsOf(IEnumerable<(OpfCandle Candle, DateTime End)> candles, DateTime now) =>
        candles.Where(x => x.End <= now && x.Candle.Time < now).Select(x => x.Candle).OrderBy(x => x.Time).ToArray();

    public static decimal? Sma(IReadOnlyList<OpfCandle> closed, int period) =>
        closed.Count < period ? null : closed.TakeLast(period).Average(x => x.Close);

    public static string? Reaction(bool isLong, decimal price, decimal trigger, decimal stop, decimal tick,
        DateTime now, DateTime expires, DateTime? heldSince, decimal signedVolume, decimal volume, decimal maxChase)
    {
        if (now > expires) return "Expired";
        if (isLong ? price <= stop : price >= stop) return "Invalidated";
        var advance = isLong ? price - trigger : trigger - price;
        if (advance < tick) return "WaitingBreak";
        if (advance > maxChase) return "ChaseTooFar";
        if (!heldSince.HasValue || now - heldSince.Value < TimeSpan.FromSeconds(2)) return "WaitingHold";
        if (volume <= 0m || (isLong ? signedVolume : -signedVolume) / volume < 0.1m) return "WaitingFlow";
        return null;
    }

    public static string IcebergArbitration(int activeCount, bool hasIceberg, bool sameDirection, bool protectedOrders,
        bool flat, bool cleanupDone, decimal combinedRisk) =>
        activeCount == 0 ? flat && cleanupDone ? "Enter" : "WaitCleanup" :
        hasIceberg || activeCount >= 2 ? "Occupied" :
        !protectedOrders ? "WaitProtection" :
        !sameDirection ? "FlattenThenWait" :
        combinedRisk > 300m ? "RiskCap" : "Enter";
}
