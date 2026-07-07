using OPFStrategyV1.Core.MarketData;
using OPFStrategyV1.Core.Scoring;

namespace OPFStrategyV1.Regime;

public sealed class TrendScoreEngine
{
    private const decimal TrendThreshold = 70m;
    private readonly List<OpfCandle> _candles = new();
    private readonly List<(int Bar, decimal Price)> _swingHighs = new();
    private readonly List<(int Bar, decimal Price)> _swingLows = new();
    private DateTime _sessionDate = DateTime.MinValue;
    private decimal _openingRangeHigh;
    private decimal _openingRangeLow;

    public RegimeResult Update(OpfCandle candle)
    {
        ResetSessionIfNeeded(candle);
        _candles.Add(candle);
        UpdateOpeningRange(candle);
        UpdateSwings();

        var bull = BuildTrendScore("BullTrendScore", bullish: true);
        var bear = BuildTrendScore("BearTrendScore", bullish: false);

        var regime = MarketRegime.Unknown;
        if (bull.Passed && !bear.Passed)
            regime = MarketRegime.BullTrend;
        else if (bear.Passed && !bull.Passed)
            regime = MarketRegime.BearTrend;

        return new RegimeResult(regime, bull, bear);
    }

    private void ResetSessionIfNeeded(OpfCandle candle)
    {
        if (_sessionDate == candle.Time.Date)
            return;

        _sessionDate = candle.Time.Date;
        _openingRangeHigh = candle.High;
        _openingRangeLow = candle.Low;
    }

    private void UpdateOpeningRange(OpfCandle candle)
    {
        if (candle.Time.TimeOfDay > TimeSpan.FromMinutes(30))
            return;

        _openingRangeHigh = Math.Max(_openingRangeHigh, candle.High);
        _openingRangeLow = Math.Min(_openingRangeLow, candle.Low);
    }

    private void UpdateSwings()
    {
        if (_candles.Count < 5)
            return;

        var index = _candles.Count - 3;
        var c = _candles[index];
        var isSwingHigh = c.High > _candles[index - 1].High
                          && c.High > _candles[index - 2].High
                          && c.High > _candles[index + 1].High
                          && c.High > _candles[index + 2].High;
        var isSwingLow = c.Low < _candles[index - 1].Low
                         && c.Low < _candles[index - 2].Low
                         && c.Low < _candles[index + 1].Low
                         && c.Low < _candles[index + 2].Low;

        if (isSwingHigh && !_swingHighs.Any(x => x.Bar == c.Bar))
            _swingHighs.Add((c.Bar, c.High));
        if (isSwingLow && !_swingLows.Any(x => x.Bar == c.Bar))
            _swingLows.Add((c.Bar, c.Low));

        TrimSwings(_swingHighs);
        TrimSwings(_swingLows);
    }

    private static void TrimSwings(List<(int Bar, decimal Price)> swings)
    {
        if (swings.Count > 20)
            swings.RemoveRange(0, swings.Count - 20);
    }

    private ScoreBreakdown BuildTrendScore(string scoreName, bool bullish)
    {
        var components = new List<ScoreComponent>
        {
            SwingProgression(bullish),
            VwapSide(bullish),
            OpeningRangeSide(bullish),
            VwapCrossCount(),
            DirectionalDisplacement(bullish),
            AtrExpansion()
        };

        var total = components.Sum(x => x.Contribution);
        return new ScoreBreakdown(scoreName, total, TrendThreshold, total >= TrendThreshold, components);
    }

    private ScoreComponent SwingProgression(bool bullish)
    {
        const decimal weight = 30m;
        if (_swingHighs.Count < 2 || _swingLows.Count < 2)
            return Component("SwingProgression", "InsufficientSwings", false, weight);

        var h1 = _swingHighs[^2].Price;
        var h2 = _swingHighs[^1].Price;
        var l1 = _swingLows[^2].Price;
        var l2 = _swingLows[^1].Price;
        var passed = bullish ? h2 > h1 && l2 > l1 : h2 < h1 && l2 < l1;
        var raw = bullish ? $"HH={h2 > h1};HL={l2 > l1}" : $"LH={h2 < h1};LL={l2 < l1}";
        return Component("SwingProgression", raw, passed, weight);
    }

    private ScoreComponent VwapSide(bool bullish)
    {
        const decimal weight = 20m;
        var recent = _candles.Where(x => x.Vwap > 0m).TakeLast(5).ToArray();
        if (recent.Length < 5)
            return Component("VWAPSide", "VwapUnavailable", false, weight);

        var count = bullish
            ? recent.Count(x => x.Close > x.Vwap)
            : recent.Count(x => x.Close < x.Vwap);
        return Component("VWAPSide", $"Count={count}/5", count >= 4, weight);
    }

    private ScoreComponent OpeningRangeSide(bool bullish)
    {
        const decimal weight = 20m;
        if (_openingRangeHigh <= _openingRangeLow || _candles.Count < 5)
            return Component("OpeningRangeSide", "OpeningRangeUnavailable", false, weight);

        var recent = _candles.TakeLast(5).ToArray();
        var count = bullish
            ? recent.Count(x => x.Close > _openingRangeHigh)
            : recent.Count(x => x.Close < _openingRangeLow);
        return Component("OpeningRangeSide", $"Count={count}/5", count >= 3, weight);
    }

    private ScoreComponent VwapCrossCount()
    {
        const decimal weight = 10m;
        var recent = _candles.Where(x => x.Vwap > 0m).TakeLast(12).ToArray();
        if (recent.Length < 12)
            return Component("VwapCrossCount", "VwapUnavailable", false, weight);

        var crosses = 0;
        for (var i = 1; i < recent.Length; i++)
        {
            var prev = Math.Sign(recent[i - 1].Close - recent[i - 1].Vwap);
            var cur = Math.Sign(recent[i].Close - recent[i].Vwap);
            if (prev != 0 && cur != 0 && prev != cur)
                crosses++;
        }

        return Component("VwapCrossCount", $"Crosses={crosses}", crosses <= 2, weight);
    }

    private ScoreComponent DirectionalDisplacement(bool bullish)
    {
        const decimal weight = 10m;
        var atr = Atr(14);
        if (atr <= 0m || _candles.Count < 10)
            return Component("DirectionalDisplacement", "AtrUnavailable", false, weight);

        var passed = _candles.TakeLast(10).Any(x =>
        {
            var body = Math.Abs(x.Close - x.Open);
            var directionOk = bullish ? x.Close > x.Open : x.Close < x.Open;
            return directionOk && body >= 1.2m * atr;
        });

        return Component("DirectionalDisplacement", $"ATR14={atr:0.####}", passed, weight);
    }

    private ScoreComponent AtrExpansion()
    {
        const decimal weight = 10m;
        if (_candles.Count < 65)
            return Component("AtrExpansion", "InsufficientBars", false, weight);

        var current = Atr(14);
        var atrSamples = new List<decimal>();
        for (var end = _candles.Count - 50; end < _candles.Count; end++)
        {
            var sample = AtrAt(end, 14);
            if (sample > 0m)
                atrSamples.Add(sample);
        }

        if (current <= 0m || atrSamples.Count == 0)
            return Component("AtrExpansion", "AtrUnavailable", false, weight);

        var avg = atrSamples.Average();
        return Component("AtrExpansion", $"ATR14={current:0.####};Avg50={avg:0.####}", current >= 1.1m * avg, weight);
    }

    private decimal Atr(int length)
    {
        return AtrAt(_candles.Count, length);
    }

    private decimal AtrAt(int endExclusive, int length)
    {
        if (endExclusive <= length || endExclusive > _candles.Count)
            return 0m;

        var trueRanges = new List<decimal>();
        for (var i = endExclusive - length; i < endExclusive; i++)
        {
            var cur = _candles[i];
            var prevClose = i > 0 ? _candles[i - 1].Close : cur.Close;
            var tr = Math.Max(cur.High - cur.Low, Math.Max(Math.Abs(cur.High - prevClose), Math.Abs(cur.Low - prevClose)));
            trueRanges.Add(tr);
        }

        return trueRanges.Count == 0 ? 0m : trueRanges.Average();
    }

    private static ScoreComponent Component(string name, string raw, bool passed, decimal weight)
    {
        return new ScoreComponent(name, raw, passed, weight, passed ? weight : 0m, passed ? "Passed" : "Failed");
    }
}
