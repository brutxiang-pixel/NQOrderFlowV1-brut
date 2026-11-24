namespace OPFStrategyV1.Strategy;

internal sealed class StoppedExitFills
{
    private readonly Dictionary<string, (decimal Price, decimal Quantity, DateTime Time, string Role)> _fills = new();

    public void Add(string id, decimal price, decimal quantity, DateTime time, string role) =>
        _fills.TryAdd(id, (price, quantity, time, role));

    public (decimal Price, DateTime Time, string Role)? Summarize(decimal entryQuantity, decimal? position)
    {
        if (position != 0m || entryQuantity <= 0m || _fills.Values.Sum(fill => fill.Quantity) != entryQuantity)
            return null;

        var last = _fills.Values.OrderBy(fill => fill.Time).Last();
        return (_fills.Values.Sum(fill => fill.Price * fill.Quantity) / entryQuantity, last.Time, last.Role);
    }
}
