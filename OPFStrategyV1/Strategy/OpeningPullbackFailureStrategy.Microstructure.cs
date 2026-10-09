using OPFStrategyV1.Core.MarketData;
using OPFStrategyV1.Core.Signals;

namespace OPFStrategyV1.Strategy;

public sealed partial class OpeningPullbackFailureStrategy
{
    private void LogMicrostructureLabels(OpfCandle candle)
    {
        if (_snapshot is null || _researchLogger is null)
            return;

        var longImpulse = HasBigTradeImpulse(TradeSide.Long, candle);
        var shortImpulse = HasBigTradeImpulse(TradeSide.Short, candle);
        var longAbsorption = HasAbsorptionAtLevel(TradeSide.Long, candle);
        var shortAbsorption = HasAbsorptionAtLevel(TradeSide.Short, candle);
        if (!longImpulse && !shortImpulse && !longAbsorption && !shortAbsorption)
            return;

        _researchLogger.AppendInfo(
            _snapshot.SnapshotId,
            candle.Bar,
            candle.Time,
            $"MICROSTRUCTURE_LABEL bigTradeLong={longImpulse}|bigTradeShort={shortImpulse}|absorptionLong={longAbsorption}|absorptionShort={shortAbsorption}");
    }

    private bool HasBigTradeImpulse(TradeSide side, OpfCandle candle)
    {
        var barEnd = candle.Time.AddMinutes(5);
        var current = _footprintTrades.Where(x => x.Time >= candle.Time && x.Time < barEnd).ToArray();
        if (current.Length < 3)
            return false;

        var history = _footprintTrades.Where(x => x.Time >= candle.Time.AddMinutes(-15) && x.Time < candle.Time).Select(x => x.Volume).OrderBy(x => x).ToArray();
        if (history.Length < 10)
            return false;

        var p90 = history[(int)Math.Floor((history.Length - 1) * 0.90m)];
        var threshold = Math.Max(p90 * 2m, history.Average() * 3m);
        var signed = side == TradeSide.Long ? 1m : -1m;
        var big = current.Where(x => x.SignedVolume * signed > 0m && x.Volume >= threshold).ToArray();
        if (big.Length == 0)
            return false;

        var range = candle.High - candle.Low;
        if (range <= 0m)
            return false;
        var directionalClose = side == TradeSide.Long
            ? candle.Close >= candle.Low + range * 0.70m
            : candle.Close <= candle.High - range * 0.70m;
        return directionalClose;
    }

    private bool HasDirectionalFootprintImpulse(TradeSide side, OpfCandle candle)
    {
        var barEnd = candle.Time.AddMinutes(5);
        var trades = _footprintTrades.Where(x => x.Time >= candle.Time && x.Time < barEnd).ToArray();
        var total = trades.Sum(x => x.Volume);
        if (total <= 0m)
            return false;

        var delta = trades.Sum(x => x.SignedVolume);
        var aligned = side == TradeSide.Long ? delta > 0m : delta < 0m;
        var averageVolume = _recentCandles.TakeLast(20).Select(x => x.Volume).DefaultIfEmpty(candle.Volume).Average();
        var relativeVolume = averageVolume <= 0m ? 0m : candle.Volume / averageVolume;
        var range = candle.High - candle.Low;
        var directionalClose = range > 0m && (side == TradeSide.Long
            ? candle.Close >= candle.Low + range * 0.70m
            : candle.Close <= candle.High - range * 0.70m);
        return aligned && relativeVolume >= 1.5m && Math.Abs(delta) / total >= 0.30m && directionalClose;
    }

    private bool HasAbsorptionAtLevel(TradeSide reversalSide, OpfCandle candle)
    {
        var barEnd = candle.Time.AddMinutes(5);
        var trades = _footprintTrades.Where(x => x.Time >= candle.Time && x.Time < barEnd).ToArray();
        if (trades.Length < 4)
            return false;

        var range = candle.High - candle.Low;
        if (range < 1m)
            return false;
        var incomingBuyers = reversalSide == TradeSide.Short;
        var extreme = incomingBuyers ? candle.High : candle.Low;
        var extremeTrades = trades.Where(x => Math.Abs(x.Price - extreme) <= 0.50m).ToArray();
        var total = extremeTrades.Sum(x => x.Volume);
        var incoming = extremeTrades.Where(x => incomingBuyers ? x.SignedVolume > 0m : x.SignedVolume < 0m).Sum(x => x.Volume);
        if (total <= 0m || incoming / total < 0.65m)
            return false;

        var rejection = reversalSide == TradeSide.Short
            ? candle.Close <= candle.Low + range * 0.45m && candle.Close < candle.Open
            : candle.Close >= candle.High - range * 0.45m && candle.Close > candle.Open;
        return rejection;
    }
}
