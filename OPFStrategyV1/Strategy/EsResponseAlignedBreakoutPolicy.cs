namespace OPFStrategyV1.Strategy;

internal static class EsResponseAlignedBreakoutPolicy
{
    public const string Path = "ESResponseAlignedBreakout";
    public const decimal MnqMaxInitialRiskPoints = 15m;
    public const decimal MnqBreakEvenTriggerR = 0.5m;

    public static bool IsDisabledNqShort(string instrument, string path, bool isLong) =>
        string.Equals(instrument, "NQ", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(path, Path, StringComparison.OrdinalIgnoreCase) &&
        !isLong;

    public static bool HasMnqProtection(string instrument, string path) =>
        instrument == "MNQ" && string.Equals(path, Path, StringComparison.OrdinalIgnoreCase);

    public static decimal MaximumInitialRiskPoints(string instrument, string path, decimal configuredMaximum) =>
        HasMnqProtection(instrument, path)
            ? Math.Min(configuredMaximum, MnqMaxInitialRiskPoints)
            : configuredMaximum;

    public static decimal BreakEvenTriggerR(string instrument, string path, decimal configuredTriggerR) =>
        HasMnqProtection(instrument, path)
            ? MnqBreakEvenTriggerR
            : configuredTriggerR;
}
