namespace OPFStrategyV1.Strategy;

/// <summary>
/// Pure time-slot resolver for GEX intraday refresh scheduling. All comparisons use Eastern time
/// because dealer gamma exposure is driven by the US equity/index options market session.
/// This class does not touch the network, the filesystem, or any trading logic; it only answers
/// "which refresh slot (if any) does this Eastern-time minute belong to".
/// </summary>
public static class GexRefreshScheduleGate
{
    public static readonly TimeSpan PreMarketEastern = new(9, 0, 0);
    public static readonly TimeSpan IntradayStartEastern = new(9, 30, 0);
    public static readonly TimeSpan IntradayEndEastern = new(16, 0, 0);
    public const int IntradayIntervalMinutes = 30;

    /// <summary>
    /// Resolves the refresh slot key for the given Eastern-time minute (seconds/sub-second
    /// components are ignored so a 60-second polling loop reliably lands on each slot exactly
    /// once). Returns null on weekends or outside any defined slot boundary.
    /// </summary>
    public static string? ResolveSlotKey(DateTime easternTime)
    {
        if (easternTime.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            return null;

        var t = new TimeSpan(easternTime.TimeOfDay.Hours, easternTime.TimeOfDay.Minutes, 0);
        var dateKey = easternTime.ToString("yyyy-MM-dd");

        if (t == PreMarketEastern)
            return $"{dateKey}|PreMarket";

        if (t >= IntradayStartEastern && t <= IntradayEndEastern && t.Minutes % IntradayIntervalMinutes == 0)
            return $"{dateKey}|Intraday|{t:hh\\:mm}";

        return null;
    }
}
