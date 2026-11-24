namespace OPFStrategyV1.Strategy;

internal sealed class IcebergReplenishment
{
    internal sealed record Signal(long Id, decimal Price, bool IsLong, DateTime AttackTime, DateTime ConfirmedAt, DateTime RefilledAt);
    private sealed class Level
    {
        public decimal Size;
        public DateTime DepthTime, DropTime, FirstTrade, LastTrade, RefilledAt;
        public int AttackCount;
        public Signal? Candidate;
    }

    private static readonly TimeSpan Window = TimeSpan.FromSeconds(2);
    private readonly Dictionary<(decimal Price, bool IsLong), Level> _levels = new();
    private DateTime _nextCleanup;
    private long _id;

    internal Signal? Depth(decimal price, bool isLong, decimal size, DateTime time, DateTime received)
    {
        if (price <= 0m || size < 0m || !Fresh(time, received)) return null;
        if (received >= _nextCleanup)
        {
            _nextCleanup = received.AddSeconds(5);
            foreach (var key in _levels.Keys.Where(key => received - _levels[key].DepthTime > TimeSpan.FromSeconds(5)).ToArray())
                _levels.Remove(key);
        }
        if (!_levels.TryGetValue((price, isLong), out var level))
            _levels.Add((price, isLong), level = new Level { Size = size, DepthTime = time });
        if (time < level.DepthTime || time < level.LastTrade) return null;
        if (level.Candidate is { } expired && received - expired.ConfirmedAt > TimeSpan.FromSeconds(5))
        {
            level.Candidate = null;
            level.FirstTrade = level.LastTrade = level.DropTime = level.RefilledAt = default;
            level.AttackCount = 0;
        }

        Signal? created = null;
        if (size < level.Size)
        {
            level.DropTime = time;
            if (size == 0m)
            {
                level.Candidate = null;
                level.FirstTrade = level.LastTrade = level.DropTime = level.RefilledAt = default;
                level.AttackCount = 0;
            }
        }
        else if (size > level.Size)
        {
            if (level.DropTime != default && level.LastTrade != default && level.AttackCount >= 2 &&
                time - level.FirstTrade <= Window && (level.DropTime - level.LastTrade).Duration() <= TimeSpan.FromMilliseconds(250) &&
                level.Candidate is null)
            {
                level.RefilledAt = time;
                created = level.Candidate = new Signal(++_id, price, isLong, level.LastTrade, received, time);
            }
            level.DropTime = default;
        }
        level.Size = size;
        level.DepthTime = time;
        return created;
    }

    internal void Trade(decimal price, bool aggressiveBuy, DateTime time, DateTime received)
    {
        if (price <= 0m || !Fresh(time, received)) return;
        foreach (var (key, level) in _levels)
        {
            if (level.FirstTrade == default || time < level.DepthTime || (key.IsLong ? price >= key.Price : price <= key.Price)) continue;
            level.Candidate = null;
            level.FirstTrade = level.LastTrade = level.DropTime = level.RefilledAt = default;
            level.AttackCount = 0;
        }
        if (!_levels.TryGetValue((price, !aggressiveBuy), out var hit) || time < hit.LastTrade ||
            (time - hit.DepthTime).Duration() > Window) return;
        if (hit.Candidate is { } expired && received - expired.ConfirmedAt > TimeSpan.FromSeconds(5))
        {
            hit.Candidate = null;
            hit.FirstTrade = hit.LastTrade = hit.DropTime = hit.RefilledAt = default;
            hit.AttackCount = 0;
        }
        if (hit.FirstTrade == default || time - hit.FirstTrade > Window)
        {
            hit.FirstTrade = time;
            hit.RefilledAt = default;
            hit.AttackCount = 0;
        }
        hit.LastTrade = time;
        hit.AttackCount++;
    }

    internal Signal[] FreshSignals(DateTime now) => _levels.Values.Select(x => x.Candidate)
        .OfType<Signal>().Where(x => x.ConfirmedAt <= now && now - x.ConfirmedAt <= TimeSpan.FromSeconds(5)).ToArray();

    internal bool Consume(Signal signal)
    {
        if (!_levels.TryGetValue((signal.Price, signal.IsLong), out var level) || level.Candidate != signal) return false;
        level.Candidate = null;
        level.FirstTrade = level.LastTrade = level.DropTime = level.RefilledAt = default;
        level.AttackCount = 0;
        return true;
    }

    internal void Reset() => _levels.Clear();

    private static bool Fresh(DateTime time, DateTime received) => time <= received && received - time <= TimeSpan.FromSeconds(1);
}
