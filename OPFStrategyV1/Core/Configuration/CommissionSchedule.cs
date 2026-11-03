namespace OPFStrategyV1.Core.Configuration;

internal static class CommissionSchedule
{
    public static Dictionary<string, decimal> CreateDefaults()
    {
        return new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["MNQ"] = 1.18m,
            ["MES"] = 1.18m,
            ["NQ"] = 3.98m,
            ["ES"] = 3.98m
        };
    }

    public static decimal Resolve(
        string? productCode,
        decimal fallback,
        IReadOnlyDictionary<string, decimal>? configured = null)
    {
        var code = (productCode ?? string.Empty).Trim().ToUpperInvariant();
        if (configured is not null && configured.TryGetValue(code, out var configuredValue) && configuredValue >= 0m)
            return configuredValue;
        if (configured is not null)
        {
            var configuredMatch = configured.FirstOrDefault(pair => code.StartsWith(pair.Key, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(configuredMatch.Key) && configuredMatch.Value >= 0m)
                return configuredMatch.Value;
        }
        var defaults = CreateDefaults();
        var match = defaults.FirstOrDefault(pair => code.StartsWith(pair.Key, StringComparison.OrdinalIgnoreCase));
        return !string.IsNullOrEmpty(match.Key) ? match.Value : fallback;
    }
}
