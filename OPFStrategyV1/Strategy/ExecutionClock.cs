namespace OPFStrategyV1.Strategy;

public static class ExecutionClock
{
    public static bool TimeStopDue(int elapsedBars, int maxBars) => maxBars > 0 && elapsedBars >= maxBars;

    public static DateTime ExitTimestamp(DateTime entry, DateTime? fill, DateTime marketTime, DateTime receivedUtc, bool replay)
    {
        static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Local
            ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);
        var start = Utc(entry);
        var clock = Utc(replay ? marketTime : receivedUtc);
        var candidate = fill.HasValue ? Utc(fill.Value) : clock;
        if (candidate < start || candidate > clock.AddSeconds(5)) candidate = clock;
        return candidate < start ? start : candidate;
    }

    public static TimeSpan? MarketSilence(DateTime utcNow, DateTime? sdkReception, bool connected, DateTime? chartReception) =>
        sdkReception is { } sdk ? utcNow - sdk :
        connected && chartReception is { } chart ? utcNow - chart : null;
    // ATAS candle timestamps without Kind represent UTC, as do the research logs.
    public static bool IsHistorical(DateTime candleTime, DateTime utcNow)
    {
        var candleUtc = candleTime.Kind == DateTimeKind.Local
            ? candleTime.ToUniversalTime()
            : DateTime.SpecifyKind(candleTime, DateTimeKind.Utc);
        return Math.Abs((utcNow - candleUtc).TotalMinutes) > 10d;
    }
}
