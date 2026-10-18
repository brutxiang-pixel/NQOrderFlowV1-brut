namespace OPFStrategyV1.Strategy;

// Forward tick comparisons only. Does not submit or modify orders.
internal sealed class PathOpportunity(string signal, string path, bool isLong, DateTime time, int bar,
    decimal entry, decimal stop, decimal risk, decimal targetR, decimal feePoints)
{
    public string Signal { get; } = signal;
    public string Path { get; } = path;
    public bool IsLong { get; } = isLong;
    public DateTime Time { get; } = time;
    public int Bar { get; } = bar;
    public decimal Entry { get; } = entry;
    public decimal Stop { get; } = stop;
    public decimal Risk { get; } = risk;
    public decimal FeePoints { get; } = feePoints;
    public decimal MfeR { get; private set; }
    public decimal MaeR { get; private set; }
    public decimal? FixedPoints { get; private set; }
    public decimal? ProtectedPoints { get; private set; }
    public decimal? EarlyPoints { get; private set; }
    public decimal? LockedPoints { get; private set; }
    public string Decision { get; set; } = "Pending";
    public string Reason { get; set; } = "";
    public string TradeId { get; set; } = "";
    private decimal _protectedStop = -risk;
    private decimal _earlyStop = -risk;
    private decimal _lockedStop = -risk;
    public bool Observe(DateTime now, int currentBar, decimal price)
    {
        if (now < Time) return false;
        var points = IsLong ? price - Entry : Entry - price;
        MfeR = Math.Max(MfeR, points / Risk);
        MaeR = Math.Max(MaeR, -points / Risk);
        var timedOut = currentBar - Bar >= (Path == "ObservationConfirm" ? 6 : 36) || now - Time >= TimeSpan.FromHours(3);
        var target = Risk * targetR;
        if (!FixedPoints.HasValue && (points <= -Risk || points >= target || timedOut)) FixedPoints = points;
        if (!ProtectedPoints.HasValue && (points <= _protectedStop || points >= target || timedOut)) ProtectedPoints = points;
        if (!EarlyPoints.HasValue && (points <= _earlyStop || points >= target || timedOut)) EarlyPoints = points;
        if (!LockedPoints.HasValue && (points <= _lockedStop || points >= target || timedOut)) LockedPoints = points;
        // A stop change only applies to the next observed tick.
        if (points >= Risk) _protectedStop = Math.Max(_protectedStop, FeePoints);
        if (points >= Risk * .75m) _earlyStop = Math.Max(_earlyStop, FeePoints);
        if (points >= Risk * .75m) _lockedStop = Math.Max(_lockedStop, FeePoints);
        if (points >= Risk * 1.25m) _lockedStop = Math.Max(_lockedStop, Risk * .5m);
        return FixedPoints.HasValue && ProtectedPoints.HasValue && EarlyPoints.HasValue && LockedPoints.HasValue;
    }
    public bool Overlaps(PathOpportunity other, decimal tick) => IsLong == other.IsLong &&
        Math.Abs((Time - other.Time).TotalSeconds) <= 30 && Math.Abs(Stop - other.Stop) <= tick * 2 &&
        Math.Abs(Entry - other.Entry) <= Math.Max(tick * 2, Math.Min(Risk, other.Risk) * .25m);
}
