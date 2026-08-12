using OPFStrategyV1.Core.MarketData;

namespace OPFStrategyV1.Core.Signals;

public enum SignificantZoneFirstTouchOutcome { NotEligible, TouchedRejected, Confirmed }

public sealed record SignificantZoneFirstTouchEntry(string ZoneId, TradeSide Side, int ConfirmationBar, int NextEntryBar, decimal InitialStop, decimal ZoneScore, decimal ReclaimDistance);

public sealed record SignificantZoneFirstTouchEvaluation(SignificantZoneFirstTouchOutcome Outcome, string Reason, SignificantZoneFirstTouchEntry? Entry = null);

public sealed class SignificantZoneFirstTouchEngine
{
    public SignificantZoneFirstTouchEvaluation Evaluate(SignificantZone zone, OpfCandle candle, bool hasObservedTrades)
    {
        if (zone.State != SignificantZoneState.Active || zone.TestCount != 0 || zone.Strength?.Grade != SignificantZoneGrade.A || !hasObservedTrades)
            return new(SignificantZoneFirstTouchOutcome.NotEligible, "NotActiveUntestedAWithObservedTouch");

        var body = zone.Side == TradeSide.Long ? candle.Close - candle.Open : candle.Open - candle.Close;
        var confirmed = zone.Side == TradeSide.Long ? body >= 1m && candle.Close >= zone.InnerBoundary + .5m : body >= 1m && candle.Close <= zone.InnerBoundary - .5m;
        if (!confirmed)
            return new(SignificantZoneFirstTouchOutcome.TouchedRejected, $"ConfirmationFailed:body={body:0.##}|close={candle.Close:0.##}|inner={zone.InnerBoundary:0.##}");

        var stop = zone.Side == TradeSide.Long ? candle.Low - .5m : candle.High + .5m;
        var reclaim = Math.Abs(candle.Close - zone.InnerBoundary);
        return new(SignificantZoneFirstTouchOutcome.Confirmed, $"Confirmed:body={body:0.##}|reclaim={reclaim:0.##}", new(zone.ZoneId, zone.Side, candle.Bar, candle.Bar + 1, stop, zone.Strength.Score, reclaim));
    }
}
