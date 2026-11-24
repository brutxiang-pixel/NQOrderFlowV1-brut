using OPFStrategyV1.Core.MarketData;
using OPFStrategyV1.Core.Profiles;

namespace OPFStrategyV1.Strategy;

public sealed record CompletedFrame(string Name, DateTime Time, decimal High, decimal Low, decimal Close, decimal Slope, int Count);

public static class IntradayEntryPolicy
{
    internal const string IcebergAbsorptionScalpPath = "IcebergAbsorptionScalp";
    internal const int ScalpMaxRiskTicks = 8;
    internal const int ScalpBreakEvenTriggerTicks = 4;
    internal const int ScalpTargetTicks = 12;

    internal static bool IsIcebergScalpProduct(string? productCode) =>
        ProfileCatalog.ContractRoot(productCode) is "MNQ" or "NQ" or "ES" or "MES" or "GC" or "MGC";

    internal static bool IsLocalIcebergScalp(string chart, string source) =>
        IsIcebergScalpProduct(chart) && string.Equals(chart, source, StringComparison.OrdinalIgnoreCase);

    internal static bool IsSameScalpAbsorption(DateTime received, DateTime attempted) =>
        received <= attempted.AddSeconds(5);

    internal static decimal? IcebergScalpStop(bool isLong, decimal level, decimal tick, DateTime eventTime,
        DateTime received, DateTime now, IReadOnlyList<(DateTime Time, decimal Price, decimal Volume, decimal Signed)> tape,
        DateTime attackAfter = default, DateTime? refilledAt = null)
    {
        var age = now - received;
        if (tick <= 0m || eventTime > received || received - eventTime > TimeSpan.FromSeconds(3) ||
            age < TimeSpan.Zero || age > TimeSpan.FromSeconds(5)) return null;
        var side = isLong ? 1m : -1m;
        var attack = tape.Where(x => x.Time > attackAfter && x.Time >= eventTime.AddSeconds(-2) && x.Time <= eventTime &&
            Math.Abs(x.Price - level) <= tick && side * x.Signed < 0m).ToArray();
        if (attack.Length < 2) return null;
        var after = tape.Where(x => x.Time > (refilledAt ?? received) && x.Time <= now).ToArray();
        var invalidation = level - side * 2m * tick;
        if (after.Length < 2 || after.Any(x => side * (x.Price - invalidation) <= 0m)) return null;
        var last = after[^1];
        if (now - last.Time > TimeSpan.FromMilliseconds(750) || side * (last.Price - level) < tick || side * last.Signed <= 0m)
            return null;
        var latest = after.Where(x => x.Time >= last.Time.AddSeconds(-1)).ToArray();
        if (latest.Count(x => side * x.Signed > 0m && side * (x.Price - level) >= tick) < 2 ||
            side * latest.Sum(x => x.Signed) < latest.Sum(x => x.Volume) * .2m) return null;
        var extreme = isLong ? attack.Concat(after).Min(x => x.Price) : attack.Concat(after).Max(x => x.Price);
        return (isLong ? Math.Min(level, extreme) : Math.Max(level, extreme)) - side * tick;
    }

    internal static bool ScalpQuoteReady(decimal bid, decimal ask, DateTime bidAt, DateTime askAt, DateTime now, decimal tick) =>
        ScalpQuoteBlock(bid, ask, bidAt, askAt, now, tick) is null;

    internal static DateTime ScalpQuoteTime(decimal price, DateTime snapshotAt, (decimal Price, DateTime At) callback, DateTime now)
    {
        if (snapshotAt > now) snapshotAt = default;
        return callback.Price == price && callback.At <= now && callback.At > snapshotAt ? callback.At : snapshotAt;
    }

    internal static string? ScalpQuoteBlock(decimal bid, decimal ask, DateTime bidAt, DateTime askAt, DateTime now, decimal tick)
    {
        if (tick <= 0m || bid <= 0m || ask <= bid) return "QuoteMissingOrCrossed";
        if (ask - bid > 2m * tick) return "QuoteSpreadWide";
        if (bidAt > now || askAt > now || now - bidAt > TimeSpan.FromSeconds(2) || now - askAt > TimeSpan.FromSeconds(2))
            return "QuoteTimestampInvalid";
        return null;
    }

    internal static bool ScalpRiskReady(bool isLong, decimal entry, decimal stop, decimal tick) =>
        (isLong ? entry - stop : stop - entry) >= tick && Math.Abs(entry - stop) <= ScalpMaxRiskTicks * tick;

    internal static decimal ScalpBreakEvenTriggerR(decimal risk, decimal tick, decimal commission, decimal pointValue)
    {
        if (risk <= 0m || tick <= 0m || pointValue <= 0m) return 0m;
        var feeOffset = Math.Ceiling(Math.Max(0m, commission) / pointValue / tick) * tick;
        return Math.Max(ScalpBreakEvenTriggerTicks * tick, feeOffset + tick) / risk;
    }

    internal static bool ScalpNoProgress(TimeSpan elapsed, decimal mfe, decimal tick) =>
        elapsed >= TimeSpan.FromSeconds(20) && mfe < 3m * tick;

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

    public static decimal AvailableConfiguredEntryQuantity(decimal configuredTotal, decimal requested, decimal managedQuantity)
    {
        var totalLimit = Math.Max(0m, configuredTotal);
        var available = totalLimit - Math.Max(0m, managedQuantity);
        return Math.Max(0m, Math.Min(Math.Max(0m, requested), available));
    }

    public static bool IcebergTrendAligned(bool isLong, IReadOnlyDictionary<string, CompletedFrame> frames) =>
        new[] { "15M", "1H", "4H" }.All(name => frames.TryGetValue(name, out var frame) &&
            frame.Count >= 4 && (isLong ? frame.Slope > 0m : frame.Slope < 0m));

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

    public static int EvidenceScore(bool optionContext, bool esPriceReaction, bool localFlow) =>
        (optionContext ? 1 : 0) + (esPriceReaction ? 1 : 0) + (localFlow ? 1 : 0);

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
