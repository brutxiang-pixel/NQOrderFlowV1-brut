namespace OPFStrategyV1.Strategy;

// The level is frozen at touch. A moving model level cannot manufacture a touch.
internal sealed class LevelReactionEvent(decimal level, decimal approach, DateTime touched)
{
    public static LevelReactionEvent? TryCreate(decimal level, decimal? priorLevel, decimal priorPrice, decimal price, DateTime time)
    {
        if (!priorLevel.HasValue || Math.Abs(priorLevel.Value - level) > .25m) return null;
        var enteredBand = Math.Abs(priorPrice - level) > .5m && Math.Abs(price - level) <= .5m;
        return enteredBand || priorPrice < level && price >= level || priorPrice > level && price <= level
            ? new LevelReactionEvent(level, priorPrice, time) : null;
    }
    public decimal Level { get; } = level;
    public decimal Approach { get; } = approach;
    public DateTime Touched { get; } = touched;
    private DateTime? _aboveSince;
    private DateTime? _belowSince;
    private DateTime _lastObserved;
    public void Observe(decimal price, DateTime now)
    {
        if (now < Touched || now < _lastObserved) return;
        if (now - _lastObserved > TimeSpan.FromSeconds(2)) { _aboveSince = null; _belowSince = null; }
        _lastObserved = now;
        _aboveSince = price > Level + .25m ? _aboveSince ?? now : null;
        _belowSince = price < Level - .25m ? _belowSince ?? now : null;
    }
    public bool Active(DateTime now) => now >= Touched && now <= Touched.AddMinutes(5);
    public string? Response(bool isLong, decimal price, decimal threshold)
    {
        if (isLong ? price <= Level + threshold : price >= Level - threshold) return null;
        var held = isLong ? _aboveSince : _belowSince;
        if (!held.HasValue || _lastObserved - held.Value < TimeSpan.FromSeconds(2)) return null;
        return isLong ? Approach < Level ? "CrossHold" : "Reject"
                      : Approach > Level ? "CrossHold" : "Reject";
    }
}
