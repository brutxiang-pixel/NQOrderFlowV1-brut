using OPFStrategyV1.Core.MarketData;

namespace OPFStrategyV1.Core.Signals;

public enum SignificantZoneShadowState
{
    Active,
    FirstTouch,
    Defending,
    Breached,
    Accepted,
    Reclaimed
}

public sealed record SignificantZoneShadow(
    string Source,
    SignificantZoneShadowState State,
    int MeaningfulTouchCount,
    int ConsecutiveBreachCloses,
    int LastUpdatedBar)
{
    public static SignificantZoneShadow Create(string source, int bar) =>
        new(source, SignificantZoneShadowState.Active, 0, 0, bar);
}

public static class SignificantZoneShadowEvaluator
{
    public static SignificantZoneShadow Update(SignificantZone zone, SignificantZoneShadow shadow, OpfCandle candle)
    {
        var low = Math.Min(zone.InnerBoundary, zone.OuterBoundary);
        var high = Math.Max(zone.InnerBoundary, zone.OuterBoundary);
        var overlap = Math.Min(high, candle.High) - Math.Max(low, candle.Low);
        var touched = overlap >= 0m;
        var meaningful = touched && overlap >= Math.Max(0.50m, (high - low) * 0.25m);
        var brokeOuter = zone.Side == TradeSide.Long
            ? candle.Close < low && candle.Open < low
            : candle.Close > high && candle.Open > high;
        var reclaimed = (shadow.State is SignificantZoneShadowState.Breached or SignificantZoneShadowState.Accepted) &&
            (zone.Side == TradeSide.Long ? candle.Close >= low : candle.Close <= high);

        if (reclaimed)
            return shadow with { State = SignificantZoneShadowState.Reclaimed, ConsecutiveBreachCloses = 0, LastUpdatedBar = candle.Bar };

        if (brokeOuter)
        {
            var consecutive = shadow.State == SignificantZoneShadowState.Breached && shadow.LastUpdatedBar == candle.Bar - 1
                ? shadow.ConsecutiveBreachCloses + 1
                : 1;
            return shadow with
            {
                State = consecutive >= 2 ? SignificantZoneShadowState.Accepted : SignificantZoneShadowState.Breached,
                ConsecutiveBreachCloses = consecutive,
                LastUpdatedBar = candle.Bar
            };
        }

        if (!meaningful)
            return shadow;

        var defended = zone.Side == TradeSide.Long ? candle.Close >= high : candle.Close <= low;
        return shadow with
        {
            State = defended ? SignificantZoneShadowState.Defending : SignificantZoneShadowState.FirstTouch,
            MeaningfulTouchCount = shadow.MeaningfulTouchCount + 1,
            ConsecutiveBreachCloses = 0,
            LastUpdatedBar = candle.Bar
        };
    }

    public static TradeSide? ExpectedSide(SignificantZone zone, SignificantZoneShadow shadow) => shadow.State switch
    {
        SignificantZoneShadowState.Defending or SignificantZoneShadowState.Reclaimed => zone.Side,
        SignificantZoneShadowState.Accepted => zone.Side == TradeSide.Long ? TradeSide.Short : TradeSide.Long,
        _ => null
    };
}
