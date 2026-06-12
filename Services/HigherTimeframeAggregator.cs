using ATAS.Indicators;
using NQOrderFlowV1.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace NQOrderFlowV1.Services
{
    /// <summary>
    /// 将底层周期K线（例如 M5）按时间桶聚合为高周期（例如 M15）K线快照。
    ///
    /// 设计目标：
    /// - 供 M15 结构/区块引擎在 M5 策略里使用（方案A）
    /// - 尽量不依赖 ATAS 某些可能变化的字段（Time 通过反射读取）
    /// - 聚合足迹价位：合并每根底层K线的 GetAllPriceLevels()
    /// - 输出 POC/VAH/VAL/VWAP（近似）
    /// </summary>
    public sealed class HigherTimeframeAggregator
    {
        public sealed class HtfCandle
        {
            public int BaseStartBar { get; init; }
            public int BaseEndBar { get; init; }

            public DateTime? BucketTime { get; init; }

            public decimal Open { get; init; }
            public decimal High { get; init; }
            public decimal Low { get; init; }
            public decimal Close { get; init; }

            public decimal Volume { get; init; }
            public decimal Delta { get; init; }

            public decimal POC { get; init; }
            public decimal VAH { get; init; }
            public decimal VAL { get; init; }
            public decimal VWAP { get; init; }

            public IReadOnlyList<PriceLevelSnapshot> TopLevels { get; init; } = Array.Empty<PriceLevelSnapshot>();
        }

        private readonly Func<int, IndicatorCandle?> _getBaseCandle;
        private readonly int _targetMinutes;
        private readonly decimal _tickSize;
        private readonly decimal _valueAreaPercent;

        private static PropertyInfo? _timeProp;

        public HigherTimeframeAggregator(
            Func<int, IndicatorCandle?> getBaseCandle,
            int targetMinutes = 15,
            decimal tickSize = 0.25m,
            decimal valueAreaPercent = 0.70m)
        {
            _getBaseCandle = getBaseCandle;
            _targetMinutes = Math.Max(1, targetMinutes);
            _tickSize = tickSize <= 0 ? 0.25m : tickSize;

            // 0.7 = 70% Value Area（常用）
            _valueAreaPercent = valueAreaPercent <= 0 || valueAreaPercent >= 1
                ? 0.70m
                : valueAreaPercent;
        }

        /// <summary>
        /// 构建 baseBar 所在时间桶的 HTF Candle（桶内可能是“未完成”的）。
        /// 返回 false 表示取不到足够数据或无法获得时间信息。
        /// </summary>
        public bool TryBuildBucketCandle(int baseBar, out HtfCandle? result)
        {
            result = null;

            var last = _getBaseCandle(baseBar);
            if (last is null)
                return false;

            var lastTime = TryGetCandleTime(last);
            if (lastTime is null)
                return false;

            var bucketTime = FloorToBucket(lastTime.Value, _targetMinutes);

            // 向左扫描：收集同一 bucketTime 的所有底层K线
            // 为避免异常数据导致无限扫描，这里给一个硬上限
            const int maxScan = 500;

            var bars = new List<(int Bar, IndicatorCandle Candle)>();

            for (var i = baseBar; i >= 0 && bars.Count < maxScan; i--)
            {
                var c = _getBaseCandle(i);
                if (c is null)
                    break;

                var t = TryGetCandleTime(c);
                if (t is null)
                    break;

                var bt = FloorToBucket(t.Value, _targetMinutes);
                if (bt != bucketTime)
                    break;

                bars.Add((i, c));
            }

            if (bars.Count == 0)
                return false;

            // bars 当前是从右到左（新->旧），需要反转为旧->新
            bars.Reverse();

            result = AggregateBars(bars, bucketTime);
            return result is not null;
        }

        /// <summary>
        /// 判断 baseBar 是否是一个 HTF 桶的“收盘确认bar”（即下一根bar已经进入下一个桶）。
        /// 用于后续：只在 HTF 收盘时推进 M15 结构/区块引擎，避免重绘/重算。
        /// </summary>
        public bool IsBucketCloseBar(int baseBar)
        {
            var cur = _getBaseCandle(baseBar);
            var next = _getBaseCandle(baseBar + 1);

            var curTime = cur is null ? null : TryGetCandleTime(cur);
            if (curTime is null)
                return false;

            var curBucket = FloorToBucket(curTime.Value, _targetMinutes);

            if (next is null)
                return true; // 没有下一根，视为当前桶的最新bar

            var nextTime = TryGetCandleTime(next);
            if (nextTime is null)
                return true;

            var nextBucket = FloorToBucket(nextTime.Value, _targetMinutes);

            return nextBucket != curBucket;
        }

        private HtfCandle? AggregateBars(List<(int Bar, IndicatorCandle Candle)> bars, DateTime bucketTime)
        {
            if (bars.Count == 0)
                return null;

            var first = bars[0].Candle;
            var last = bars[^1].Candle;

            var open = first.Open;
            var close = last.Close;

            var high = bars.Max(x => x.Candle.High);
            var low = bars.Min(x => x.Candle.Low);

            var volume = bars.Sum(x => x.Candle.Volume);
            var delta = bars.Sum(x => x.Candle.Delta);

            // 合并价位
            var levels = new Dictionary<decimal, PriceLevelSnapshot>(capacity: 512);

            foreach (var (_, c) in bars)
            {
                foreach (var lvl in c.GetAllPriceLevels())
                {
                    if (!levels.TryGetValue(lvl.Price, out var agg))
                    {
                        levels[lvl.Price] = new PriceLevelSnapshot
                        {
                            Price = lvl.Price,
                            Bid = lvl.Bid,
                            Ask = lvl.Ask,
                            Volume = lvl.Volume,
                            Ticks = lvl.Ticks,
                            Between = lvl.Between,
                            Time = lvl.Time
                        };
                    }
                    else
                    {
                        levels[lvl.Price] = new PriceLevelSnapshot
                        {
                            Price = agg.Price,
                            Bid = agg.Bid + lvl.Bid,
                            Ask = agg.Ask + lvl.Ask,
                            Volume = agg.Volume + lvl.Volume,
                            Ticks = agg.Ticks + lvl.Ticks,
                            Between = agg.Between + lvl.Between,
                            Time = agg.Time + lvl.Time
                        };
                    }
                }
            }

            decimal poc = 0m, vah = 0m, val = 0m, vwap = 0m;

            if (levels.Count > 0)
            {
                // POC
                var pocLevel = levels.Values.OrderByDescending(x => x.Volume).First();
                poc = pocLevel.Price;

                // VWAP（按价位成交量加权）
                var totalVol = levels.Values.Sum(x => x.Volume);
                if (totalVol > 0)
                    vwap = levels.Values.Sum(x => x.Price * x.Volume) / totalVol;

                // VAH/VAL（70% value area，围绕 POC 扩展）
                (vah, val) = ComputeValueArea(levels, poc, _valueAreaPercent);
            }

            var topLevels = levels.Values
                .OrderByDescending(x => x.Volume)
                .Take(3)
                .ToList();

            return new HtfCandle
            {
                BaseStartBar = bars[0].Bar,
                BaseEndBar = bars[^1].Bar,
                BucketTime = bucketTime,

                Open = open,
                High = high,
                Low = low,
                Close = close,

                Volume = volume,
                Delta = delta,

                POC = poc,
                VAH = vah,
                VAL = val,
                VWAP = vwap,

                TopLevels = topLevels
            };
        }

        private (decimal VAH, decimal VAL) ComputeValueArea(
            Dictionary<decimal, PriceLevelSnapshot> levels,
            decimal pocPrice,
            decimal valueAreaPercent)
        {
            var sortedPrices = levels.Keys.OrderBy(p => p).ToList();
            if (sortedPrices.Count == 0)
                return (0m, 0m);

            var totalVol = levels.Values.Sum(x => x.Volume);
            if (totalVol <= 0)
                return (0m, 0m);

            var targetVol = totalVol * valueAreaPercent;

            // 找 POC 的索引（若不精确匹配则找最近）
            var pocIndex = sortedPrices.BinarySearch(pocPrice);
            if (pocIndex < 0)
            {
                pocIndex = ~pocIndex;
                if (pocIndex >= sortedPrices.Count)
                    pocIndex = sortedPrices.Count - 1;
            }

            var left = pocIndex;
            var right = pocIndex;

            var cumVol = levels[sortedPrices[pocIndex]].Volume;

            while (cumVol < targetVol && (left > 0 || right < sortedPrices.Count - 1))
            {
                var nextLeft = left > 0 ? sortedPrices[left - 1] : (decimal?)null;
                var nextRight = right < sortedPrices.Count - 1 ? sortedPrices[right + 1] : (decimal?)null;

                var leftVol = nextLeft is null ? -1m : levels[nextLeft.Value].Volume;
                var rightVol = nextRight is null ? -1m : levels[nextRight.Value].Volume;

                // 选成交量更大的一侧扩展（经典 VA 算法之一）
                if (rightVol > leftVol)
                {
                    right++;
                    cumVol += levels[sortedPrices[right]].Volume;
                }
                else
                {
                    left--;
                    cumVol += levels[sortedPrices[left]].Volume;
                }
            }

            var val = sortedPrices[left];
            var vah = sortedPrices[right];

            // 略微对齐 tick（避免出现奇怪的小数）
            val = AlignToTick(val, _tickSize);
            vah = AlignToTick(vah, _tickSize);

            return (vah, val);
        }

        private static decimal AlignToTick(decimal price, decimal tickSize)
        {
            if (tickSize <= 0)
                return price;

            var ticks = Math.Round(price / tickSize, MidpointRounding.AwayFromZero);
            return ticks * tickSize;
        }

        private static DateTime FloorToBucket(DateTime time, int minutes)
        {
            var bucketMinute = (time.Minute / minutes) * minutes;
            return new DateTime(time.Year, time.Month, time.Day, time.Hour, bucketMinute, 0, time.Kind);
        }

        private static DateTime? TryGetCandleTime(IndicatorCandle candle)
        {
            // 常见字段名：Time / OpenTime
            _timeProp ??= candle.GetType().GetProperty("Time")
                         ?? candle.GetType().GetProperty("OpenTime");

            if (_timeProp is null)
                return null;

            var value = _timeProp.GetValue(candle);

            return value switch
            {
                DateTime dt => dt,
                _ => null
            };
        }
    }
}