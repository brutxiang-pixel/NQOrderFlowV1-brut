namespace OPFStrategyV1.Strategy;

// Forward observed prices only; not broker fills or realized PnL.
internal sealed class DynamicExitComparison
{
    private decimal? _stop;
    public decimal? ExitPoints { get; private set; }
    public void Observe(bool isLong, decimal price, decimal entry, decimal risk, decimal originalStop,
        decimal target, decimal feeStop, bool earlyProtection)
    {
        if (ExitPoints.HasValue || risk <= 0m) return;
        _stop ??= originalStop;
        var points = isLong ? price - entry : entry - price;
        if ((isLong ? price <= _stop.Value : price >= _stop.Value) ||
            (isLong ? price >= target : price <= target))
        { ExitPoints = points; return; }
        var desired = earlyProtection && points >= risk * .75m ? feeStop : _stop.Value;
        if (!earlyProtection && points >= risk) desired = feeStop;
        if (!earlyProtection && points >= risk * 1.25m) desired = entry + (isLong ? 1m : -1m) * risk * .5m;
        _stop = isLong ? Math.Max(_stop.Value, desired) : Math.Min(_stop.Value, desired);
    }
}
