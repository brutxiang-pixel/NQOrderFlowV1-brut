namespace OPFStrategyV1.Strategy;

internal sealed record AlignmentWindow(int Seconds, DateTime? Reference, decimal? EsMove, decimal? NqMove,
    decimal? EsRangeFraction, decimal? NqRangeFraction, bool? SameDirection);

internal sealed class CrossMarketAlignment
{
    private readonly Queue<(DateTime Time, decimal Es, decimal Nq)> _prices = new();
    public void Observe(DateTime time, decimal es, decimal nq)
    {
        if (_prices.Count > 0 && time - _prices.Last().Time < TimeSpan.FromSeconds(1)) return;
        _prices.Enqueue((time, es, nq));
        while (_prices.Count > 0 && time - _prices.Peek().Time > TimeSpan.FromSeconds(315)) _prices.Dequeue();
    }
    public AlignmentWindow Window(int seconds)
    {
        if (_prices.Count == 0) return new(seconds, null, null, null, null, null, null);
        var current = _prices.Last();
        var prior = _prices.LastOrDefault(x => current.Time - x.Time >= TimeSpan.FromSeconds(seconds));
        if (prior.Time == default || current.Time - prior.Time > TimeSpan.FromSeconds(seconds + 15))
            return new(seconds, null, null, null, null, null, null);
        var rows = _prices.Where(x => x.Time >= prior.Time).ToArray();
        var esRange = rows.Max(x => x.Es) - rows.Min(x => x.Es);
        var nqRange = rows.Max(x => x.Nq) - rows.Min(x => x.Nq);
        var esMove = current.Es - prior.Es; var nqMove = current.Nq - prior.Nq;
        return new(seconds, prior.Time, esMove, nqMove, esRange > 0m ? esMove / esRange : null,
            nqRange > 0m ? nqMove / nqRange : null, esMove == 0m || nqMove == 0m ? null : Math.Sign(esMove) == Math.Sign(nqMove));
    }
    public void Clear() => _prices.Clear();
}
