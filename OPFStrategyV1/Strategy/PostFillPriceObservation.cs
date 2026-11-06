namespace OPFStrategyV1.Strategy;

// Whole entry-bar extremes include prices that occurred before the actual fill.
public sealed class PostFillPriceObservation
{
    private readonly object _sync = new();
    public bool Started { get; private set; }
    public DateTime FillTime { get; private set; }
    public decimal Low { get; private set; }
    public decimal High { get; private set; }
    private DateTime _lastPriceTime;

    public void Start(decimal price, DateTime fillTime)
    {
        lock (_sync)
        {
            if (Started || price <= 0m) return;
            Started = true; FillTime = fillTime; _lastPriceTime = fillTime;
            Low = High = price;
        }
    }

    public bool ObservePrice(decimal price, DateTime time)
    {
        lock (_sync)
        {
            if (!Started || price <= 0m || time < _lastPriceTime) return false;
            _lastPriceTime = time;
            Low = Math.Min(Low, price); High = Math.Max(High, price);
            return true;
        }
    }

    public bool CanUseRange(DateTime barStart)
    {
        lock (_sync) return Started && barStart > FillTime;
    }

    public void ObserveRange(decimal low, decimal high, DateTime barStart)
    {
        lock (_sync)
        {
            if (!CanUseRange(barStart) || low <= 0m || high < low) return;
            Low = Math.Min(Low, low); High = Math.Max(High, high);
        }
    }
}
