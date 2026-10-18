namespace OPFStrategyV1.Strategy;

public sealed class ExecutionFillDeduplicator
{
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    public bool TryAccept(string? fillId, string role) =>
        string.IsNullOrWhiteSpace(fillId) || _seen.Add($"{role}|{fillId}");
}
