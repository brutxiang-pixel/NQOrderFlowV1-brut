using System.Collections.Concurrent;

namespace OPFStrategyV1.Strategy;

internal static class PhysicalEventExecutionDedup
{
    private static readonly ConcurrentDictionary<string, DateTime> Claims = new(StringComparer.Ordinal);

    public static bool IsClaimed(string key, DateTime now) =>
        Claims.TryGetValue(key, out var claimedAt) && (now < claimedAt || now - claimedAt <= TimeSpan.FromHours(6));

    public static bool TryClaim(string key, DateTime now)
    {
        foreach (var stale in Claims.Where(x => now >= x.Value && now - x.Value > TimeSpan.FromHours(6)).Select(x => x.Key).ToArray())
            Claims.TryRemove(stale, out _);
        return Claims.TryAdd(key, now);
    }

    public static void Release(string? key)
    {
        if (!string.IsNullOrEmpty(key)) Claims.TryRemove(key, out _);
    }
}
