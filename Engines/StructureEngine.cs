using ATAS.Indicators;
using NQOrderFlowV1.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NQOrderFlowV1.Engines
{
    public sealed class StructureEngine
    {
        private readonly Func<int, IndicatorCandle?> _getCandle;
        private readonly int _pivotLength;
        private readonly List<SwingPoint> _swings = new();

        private StructureSnapshot _lastSnapshot = new();

        public StructureEngine(Func<int, IndicatorCandle?> getCandle, int pivotLength = 2)
        {
            _getCandle = getCandle;
            _pivotLength = Math.Max(1, pivotLength);
        }

        public StructureUpdateResult Update(int bar)
        {
            var events = new List<StructureEvent>();

            var current = _getCandle(bar);
            var prev = _getCandle(bar - 1);

            if (current is null || prev is null)
            {
                return new StructureUpdateResult
                {
                    Snapshot = _lastSnapshot,
                    Events = Array.Empty<StructureEvent>()
                };
            }

            var candidateBar = bar - _pivotLength;
            var newHighAdded = false;
            var newLowAdded = false;

            if (candidateBar >= _pivotLength)
            {
                if (IsSwingHigh(candidateBar))
                    newHighAdded = AddSwing(candidateBar, SwingPointType.High);

                if (IsSwingLow(candidateBar))
                    newLowAdded = AddSwing(candidateBar, SwingPointType.Low);
            }

            var highs = _swings
                .Where(x => x.Type == SwingPointType.High)
                .OrderBy(x => x.Bar)
                .ToList();

            var lows = _swings
                .Where(x => x.Type == SwingPointType.Low)
                .OrderBy(x => x.Bar)
                .ToList();

            var lastHigh = highs.LastOrDefault();
            var prevHigh = highs.Count > 1 ? highs[^2] : null;

            var lastLow = lows.LastOrDefault();
            var prevLow = lows.Count > 1 ? lows[^2] : null;

            var trend = DetectTrend(lastHigh, prevHigh, lastLow, prevLow);

            var highLabel = GetLabel(lastHigh, prevHigh, isHigh: true);
            var lowLabel = GetLabel(lastLow, prevLow, isHigh: false);

            if (newHighAdded && lastHigh is not null)
            {
                events.Add(new StructureEvent
                {
                    Bar = lastHigh.Bar,
                    Price = lastHigh.Price,
                    EventType = StructureEventType.SwingHigh,
                    Text = $"SH {highLabel} @ {lastHigh.Price:0.00}"
                });
            }

            if (newLowAdded && lastLow is not null)
            {
                events.Add(new StructureEvent
                {
                    Bar = lastLow.Bar,
                    Price = lastLow.Price,
                    EventType = StructureEventType.SwingLow,
                    Text = $"SL {lowLabel} @ {lastLow.Price:0.00}"
                });
            }

            var breakType = StructureBreakType.None;
            var breakDirection = BreakDirection.None;
            var breakText = string.Empty;
            decimal breakPrice = 0m;

            // 向上突破
            if (lastHigh is not null &&
                prev.Close <= lastHigh.Price &&
                current.Close > lastHigh.Price)
            {
                breakDirection = BreakDirection.Up;
                breakType = trend == TrendDirection.Bearish
                    ? StructureBreakType.CHOCH
                    : StructureBreakType.BOS;

                breakPrice = lastHigh.Price;
                breakText = $"{breakType} UP @ {breakPrice:0.00}";
            }
            // 向下突破
            else if (lastLow is not null &&
                     prev.Close >= lastLow.Price &&
                     current.Close < lastLow.Price)
            {
                breakDirection = BreakDirection.Down;
                breakType = trend == TrendDirection.Bullish
                    ? StructureBreakType.CHOCH
                    : StructureBreakType.BOS;

                breakPrice = lastLow.Price;
                breakText = $"{breakType} DOWN @ {breakPrice:0.00}";
            }

            if (breakType != StructureBreakType.None)
            {
                events.Add(new StructureEvent
                {
                    Bar = bar,
                    Price = breakPrice,
                    EventType = breakType == StructureBreakType.BOS
                        ? StructureEventType.BOS
                        : StructureEventType.CHOCH,
                    Text = breakText
                });
            }

            _lastSnapshot = new StructureSnapshot
            {
                CurrentBar = bar,
                CurrentClose = current.Close,
                Trend = trend,
                LastSwingHigh = lastHigh,
                PrevSwingHigh = prevHigh,
                LastSwingLow = lastLow,
                PrevSwingLow = prevLow,
                HighLabel = highLabel,
                LowLabel = lowLabel,
                BreakType = breakType,
                BreakDirection = breakDirection,
                BreakText = breakText
            };

            return new StructureUpdateResult
            {
                Snapshot = _lastSnapshot,
                Events = events
            };
        }

        private bool AddSwing(int bar, SwingPointType type)
        {
            if (_swings.Any(x => x.Bar == bar && x.Type == type))
                return false;

            var candle = _getCandle(bar);
            if (candle is null)
                return false;

            var price = type == SwingPointType.High
                ? candle.High
                : candle.Low;

            _swings.Add(new SwingPoint
            {
                Bar = bar,
                Price = price,
                Type = type
            });

            if (_swings.Count > 300)
                _swings.RemoveRange(0, _swings.Count - 300);

            return true;
        }

        private TrendDirection DetectTrend(
            SwingPoint? lastHigh,
            SwingPoint? prevHigh,
            SwingPoint? lastLow,
            SwingPoint? prevLow)
        {
            if (lastHigh is null || prevHigh is null || lastLow is null || prevLow is null)
                return TrendDirection.Neutral;

            if (lastHigh.Price > prevHigh.Price && lastLow.Price > prevLow.Price)
                return TrendDirection.Bullish;

            if (lastHigh.Price < prevHigh.Price && lastLow.Price < prevLow.Price)
                return TrendDirection.Bearish;

            return TrendDirection.Neutral;
        }

        private string GetLabel(SwingPoint? last, SwingPoint? prev, bool isHigh)
        {
            if (last is null)
                return "-";

            if (prev is null)
                return isHigh ? "H1" : "L1";

            if (isHigh)
            {
                if (last.Price > prev.Price) return "HH";
                if (last.Price < prev.Price) return "LH";
                return "EH";
            }

            if (last.Price > prev.Price) return "HL";
            if (last.Price < prev.Price) return "LL";
            return "EL";
        }

        private bool IsSwingHigh(int bar)
        {
            var center = _getCandle(bar);
            if (center is null)
                return false;

            var high = center.High;

            for (var i = 1; i <= _pivotLength; i++)
            {
                var left = _getCandle(bar - i);
                var right = _getCandle(bar + i);

                if (left is null || right is null)
                    return false;

                if (high <= left.High || high <= right.High)
                    return false;
            }

            return true;
        }

        private bool IsSwingLow(int bar)
        {
            var center = _getCandle(bar);
            if (center is null)
                return false;

            var low = center.Low;

            for (var i = 1; i <= _pivotLength; i++)
            {
                var left = _getCandle(bar - i);
                var right = _getCandle(bar + i);

                if (left is null || right is null)
                    return false;

                if (low >= left.Low || low >= right.Low)
                    return false;
            }

            return true;
        }
    }
}