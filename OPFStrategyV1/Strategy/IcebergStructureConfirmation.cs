namespace OPFStrategyV1.Strategy;

// All anchors are frozen from observations available before this setup.
public sealed class IcebergStructureConfirmation(bool isLong, decimal anchor, bool required)
{
    public decimal Anchor { get; } = anchor;
    private DateTime? _breakTime;
    private DateTime? _retestTime;
    private DateTime? _reclaimTime;
    private DateTime _lastTime;

    public static bool CounterTrend(bool isLong, decimal first, decimal last, decimal atr, decimal tick) =>
        (isLong ? first - last : last - first) >= Math.Max(4m * tick, atr * .1m);

    public string Observe(decimal price, decimal tick, DateTime time)
    {
        if (time < _lastTime) return "WAIT_ORDERED_MARKET_TIME";
        _lastTime = time;
        if (!required) return "TREND_RESPONSE";
        var displacement = isLong ? price - Anchor : Anchor - price;
        if (displacement < -tick)
        {
            _breakTime = null; _retestTime = null; _reclaimTime = null;
            return "WAIT_STRUCTURE_BREAK";
        }
        if (_breakTime is null)
        {
            if (displacement >= tick) _breakTime = time;
            return "WAIT_STRUCTURE_RETEST";
        }
        if (_retestTime is null)
        {
            if (time > _breakTime && displacement <= 0m) _retestTime = time;
            return "WAIT_STRUCTURE_RECLAIM";
        }
        if (displacement < tick) { _reclaimTime = null; return "WAIT_STRUCTURE_RECLAIM"; }
        _reclaimTime ??= time;
        return time - _reclaimTime.Value >= TimeSpan.FromSeconds(1)
            ? "STRUCTURE_CONFIRMED" : "WAIT_STRUCTURE_RECLAIM";
    }

    public static bool Ready(string reason) => reason is "TREND_RESPONSE" or "STRUCTURE_CONFIRMED";
}
