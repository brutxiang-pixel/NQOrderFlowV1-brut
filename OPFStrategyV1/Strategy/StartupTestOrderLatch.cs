namespace OPFStrategyV1.Strategy;

public sealed class StartupTestOrderLatch
{
    public static bool UseVirtualTarget(string path, bool historicalReplay) =>
        historicalReplay && !string.Equals(path, "StartupTestOrder", StringComparison.Ordinal);

    private bool _pending;
    public bool IsPending => _pending;
    public static bool CanWait(string reason, DateTime now, DateTime deadline) => now < deadline &&
        (reason.StartsWith("LiveReadinessBlocked:", StringComparison.Ordinal) ||
         reason.StartsWith("LiveLatency", StringComparison.Ordinal) || reason == "QuoteUnavailable");
    public void Arm(bool enabled) => _pending = enabled;
    public bool TryConsume()
    {
        var pending = _pending;
        _pending = false;
        return pending;
    }
}
