namespace OPFStrategyV1.Strategy;

/// <summary>
/// Pure event-trigger detector for forced GEX refresh requests: a completed-bar close crossing a
/// tracked GEX reference level, or a completed-bar true range spiking relative to ATR14. This class
/// does not write files, does not call the network, and does not affect any trading decision; it only
/// decides whether the strategy should ask the out-of-process scheduler for an out-of-cycle refresh.
/// </summary>
public static class GexRefreshTriggerGate
{
    public static readonly TimeSpan ForcedRefreshCooldown = TimeSpan.FromMinutes(10);
    public const decimal VolatilitySpikeAtrMultiplier = 2.5m;

    /// <summary>
    /// Returns a reason string when the previous and current completed-bar closes sit on opposite
    /// sides of the given level price (a genuine crossing), or null when they are on the same side
    /// or either close sits exactly on the level.
    /// </summary>
    public static string? DetectKeyLevelCross(decimal previousClose, decimal currentClose, decimal levelPrice, string levelTag)
    {
        var previousSide = Math.Sign(previousClose - levelPrice);
        var currentSide = Math.Sign(currentClose - levelPrice);
        if (previousSide != 0 && currentSide != 0 && previousSide != currentSide)
            return $"KeyLevelCross:{levelTag}";
        return null;
    }

    /// <summary>
    /// Returns a reason string when the completed bar's true range (High - Low) reaches
    /// <see cref="VolatilitySpikeAtrMultiplier"/> times ATR14, or null when ATR14 is not yet
    /// established (&lt;= 0) or the range does not reach the multiple.
    /// </summary>
    public static string? DetectVolatilitySpike(decimal barHigh, decimal barLow, decimal atr14)
    {
        if (atr14 <= 0m)
            return null;
        var range = barHigh - barLow;
        return range >= atr14 * VolatilitySpikeAtrMultiplier
            ? $"VolatilitySpike:atrMultiple={(range / atr14):0.00}"
            : null;
    }

    public static bool IsInCooldown(DateTime? lastForcedRefreshUtc, DateTime nowUtc) =>
        lastForcedRefreshUtc is not null && nowUtc - lastForcedRefreshUtc.Value < ForcedRefreshCooldown;
}
