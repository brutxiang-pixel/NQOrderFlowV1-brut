namespace OPFStrategyV1.Strategy;

internal sealed record EsBreakoutEvent(DateTime Started, bool IsLong, decimal Price, decimal Stop, decimal Trigger)
{
    public decimal Risk => Math.Abs(Price - Stop);
    public bool Active(DateTime now, decimal price) => now >= Started && now <= Started.AddMinutes(15) &&
        (IsLong ? price > Stop : price < Stop);
    public bool NeedsRetest(decimal price) => Risk > 25m ||
        IntradayEntryPolicy.BreakoutChase(IsLong, price, Stop, Price, Risk) is not null;
    public bool RetestReady(decimal price, decimal tick) =>
        Math.Abs(price - Stop) <= 25m &&
        (IsLong ? price >= Trigger + tick : price <= Trigger - tick) &&
        Math.Abs(price - Trigger) <= Math.Max(tick * 4m, Risk * .25m);
}
