// File: Engines/HtfStructureEngine.cs
using NQOrderFlowV1.Models;
using NQOrderFlowV1.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NQOrderFlowV1.Engines
{
    /// <summary>
    /// HTF 结构引擎（基于 HigherTimeframeAggregator.HtfCandle 序列）
    /// - Swing/pivot 识别（pivotLength）
    /// - Trend: Bull/Bear/Neutral
    /// - BOS/CHOCH：以 HTF candle close 突破最近 swing 为准
    ///
    /// 注意：
    /// SwingPoint.Bar 使用的是 HTF candle 的 BaseEndBar（映射回底层图表的 bar index），用于绘图/对齐。
    /// </summary>
    public sealed class HtfStructureEngine
    {
        private readonly int _pivotLength;
        private readonly List<SwingPoint> _swings = new();

        public StructureSnapshot LastSnapshot { get; private set; } = new();

        public HtfStructureEngine(int pivotLength = 2)
        {
            _pivotLength = Math.Max(1, pivotLength);
        }

        public StructureUpdateResult Update(int htfIndex, IReadOnlyList<HigherTimeframeAggregator.HtfCandle> series)
        {
            var events = new List<StructureEvent>();

            if (series is null || series.Count == 0)
                return new StructureUpdateResult { Snapshot = LastSnapshot, Events = Array.Empty<StructureEvent>() };

            if (htfIndex <= 0 || htfIndex >= series.Count)
                return new StructureUpdateResult { Snapshot = LastSnapshot, Events = Array.Empty<StructureEvent>() };

            var current = series[htfIndex];
            var prev = series[htfIndex - 1];

            var candidateIndex = htfIndex - _pivotLength;
            var newHighAdded = false;
            var newLowAdded = false;

            // candidateIndex 左右都必须存在 pivotLength 根
            if (candidateIndex >= _pivotLength && candidateIndex + _pivotLength < series.Count)
            {
                if (IsSwingHigh(candidateIndex, series))
                    newHighAdded = AddSwing(candidateIndex, SwingPointType.High, series);

                if (IsSwingLow(candidateIndex, series))
                    newLowAdded = AddSwing(candidateIndex, SwingPointType.Low, series);
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

            // 向上突破（HTF close 确认）
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
            // 向下突破（HTF close 确认）
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
                // 事件 bar 对齐到底层 base 的收盘 bar
                events.Add(new StructureEvent
                {
                    Bar = current.BaseEndBar,
                    Price = breakPrice,
                    EventType = breakType == StructureBreakType.BOS
                        ? StructureEventType.BOS
                        : StructureEventType.CHOCH,
                    Text = breakText
                });
            }

            LastSnapshot = new StructureSnapshot
            {
                CurrentBar = current.BaseEndBar,
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
                Snapshot = LastSnapshot,
                Events = events
            };
        }

        private bool AddSwing(int htfIndex, SwingPointType type, IReadOnlyList<HigherTimeframeAggregator.HtfCandle> series)
        {
            var c = series[htfIndex];
            var bar = c.BaseEndBar;

            if (_swings.Any(x => x.Bar == bar && x.Type == type))
                return false;

            var price = type == SwingPointType.High ? c.High : c.Low;

            _swings.Add(new SwingPoint
            {
                Bar = bar,
                Price = price,
                Type = type
            });

            // 防止无界增长
            if (_swings.Count > 600)
                _swings.RemoveRange(0, _swings.Count - 600);

            return true;
        }

        private bool IsSwingHigh(int idx, IReadOnlyList<HigherTimeframeAggregator.HtfCandle> series)
        {
            var center = series[idx].High;

            for (var i = 1; i <= _pivotLength; i++)
            {
                if (center <= series[idx - i].High || center <= series[idx + i].High)
                    return false;
            }

            return true;
        }

        private bool IsSwingLow(int idx, IReadOnlyList<HigherTimeframeAggregator.HtfCandle> series)
        {
            var center = series[idx].Low;

            for (var i = 1; i <= _pivotLength; i++)
            {
                if (center >= series[idx - i].Low || center >= series[idx + i].Low)
                    return false;
            }

            return true;
        }

        private static TrendDirection DetectTrend(
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

        private static string GetLabel(SwingPoint? last, SwingPoint? prev, bool isHigh)
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
    }
}