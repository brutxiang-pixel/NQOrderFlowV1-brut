using OPFStrategyV1.Core.MarketData;

namespace OPFStrategyV1.Strategy;

public sealed record CompletedFrame(string Name, DateTime Time, decimal High, decimal Low, decimal Close, decimal Slope, int Count);

public static class IntradayEntryPolicy
{
    public const decimal IcebergTargetR = EntryExitPolicyParameters.IcebergTargetR;
    public const decimal IcebergBreakEvenR = EntryExitPolicyParameters.IcebergBreakEvenR;

    public static bool IcebergNoProgressExit(bool isLong, TimeSpan elapsed, decimal maxFavorable,
        decimal risk, decimal price, decimal structure, decimal signedVolume, decimal volume) =>
        elapsed >= TimeSpan.FromSeconds(120) && risk > 0m && maxFavorable < risk * .25m &&
        structure > 0m && (isLong ? price < structure : price > structure) && volume > 0m &&
        (isLong ? -signedVolume : signedVolume) / volume >= .15m;

    public static decimal IcebergStructuralStop(bool isLong, decimal entry, decimal icebergPrice,
        decimal recentLow, decimal recentHigh, decimal atr, decimal tick)
    {
        var buffer = Math.Max(tick * 4m, Math.Max(0m, recentHigh - recentLow) * .25m);
        var minimumRisk = Math.Max(tick * 8m, atr * .2m);
        var stop = isLong
            ? Math.Min(Math.Min(icebergPrice, recentLow) - buffer, entry - minimumRisk)
            : Math.Max(Math.Max(icebergPrice, recentHigh) + buffer, entry + minimumRisk);
        return (isLong ? Math.Floor(stop / tick) : Math.Ceiling(stop / tick)) * tick;
    }

    public static decimal ProductQuantityLimit(string productCode) =>
        productCode.StartsWith("MNQ", StringComparison.OrdinalIgnoreCase) ? 2m :
        productCode.StartsWith("NQ", StringComparison.OrdinalIgnoreCase) ? 1m : 3m;

    public static decimal AvailableEntryQuantity(string productCode, decimal requested, decimal managedQuantity) =>
        Math.Max(0m, Math.Min(requested, ProductQuantityLimit(productCode) - managedQuantity));

    public static bool IcebergTrendAligned(bool isLong, IReadOnlyDictionary<string, CompletedFrame> frames) =>
        new[] { "15M", "1H", "4H" }.All(name => frames.TryGetValue(name, out var frame) &&
            frame.Count >= 4 && (isLong ? frame.Slope > 0m : frame.Slope < 0m));

    public static decimal IcebergQuantity(decimal evidenceQuantity, bool retested, bool trendAligned,
        decimal risk, decimal atr, decimal pointValue) =>
        evidenceQuantity >= 2m && retested && trendAligned && risk > 0m && atr > 0m && pointValue > 0m &&
        risk <= atr * EntryExitPolicyParameters.IcebergSecondContractMaxAtrRisk &&
        risk * pointValue * 2m <= EntryExitPolicyParameters.IcebergTwoContractMaxRiskDollars ? 2m : 1m;

    public static decimal IcebergRunnerStop(bool isLong, decimal entry, decimal risk, decimal mfe,
        decimal feeStop, decimal low, decimal high, decimal atr, decimal tick)
    {
        if (risk <= 0m || mfe < risk * .75m) return 0m;
        var lockR = mfe >= risk * 1.25m ? .5m : mfe >= risk ? .25m : 0m;
        var stop = isLong ? Math.Max(feeStop, entry + risk * lockR) : Math.Min(feeStop, entry - risk * lockR);
        if (mfe >= risk && low > 0m && high >= low)
        {
            var buffer = Math.Max(tick * 4m, atr * .15m);
            stop = isLong ? Math.Max(stop, low - buffer) : Math.Min(stop, high + buffer);
        }
        return (isLong ? Math.Floor(stop / tick) : Math.Ceiling(stop / tick)) * tick;
    }

    public static (int Score, decimal Quantity) EvidenceSize(bool optionContext, bool esPriceReaction, bool localFlow,
        decimal configuredMaximum)
    {
        var score = (optionContext ? 1 : 0) + (esPriceReaction ? 1 : 0) + (localFlow ? 1 : 0);
        var quantity = score >= 3 ? 3m : score >= 2 ? 2m : 1m;
        return (score, Math.Min(quantity, Math.Max(1m, configuredMaximum)));
    }

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
