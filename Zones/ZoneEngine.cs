using ATAS.Indicators;
using NQOrderFlowV1.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NQOrderFlowV1.Zones
{
    public sealed class ZoneEngine
    {
        private readonly Func<int, IndicatorCandle?> _getCandle;
        private readonly decimal _tickSize;
        private readonly int _lookback;
        private readonly decimal _minFvgGap;
        private readonly decimal _vpMaxDistancePoints;

        // 位移过滤（NQ 15m 经验值：中间K线必须有明显扩张）
        private const decimal MinDisplacementRange = 6m;

        // FVG 合并阈值：重叠比例 >= 70%
        private const decimal FvgMergeOverlapRatio = 0.70m;

        // FVG 最大高度过滤（相对阈值）：maxRange = min(HardCap, middleRange * K)
        private const decimal FvgMaxRangeMultiplierK = 1.5m;
        private const decimal FvgHardCapPoints = 80m;

        // FVG 缓解（Mitigation）：触及 50% 记为缓解，但不失效移除
        private const decimal FvgMitigationPercent = 0.50m;

        // ActiveZones：分别保留 OB 与 FVG 的数量，避免重要 OB 被新 FVG 挤掉
        private const int MaxActiveObs = 6;
        private const int MaxActiveFvgs = 6;

        // VWAP 反射读取（避免不同版本字段名差异导致编译失败）
        private static System.Reflection.PropertyInfo? _vwapProp;

        private readonly List<TradingZone> _zones = new();

        public ZoneEngine(
            Func<int, IndicatorCandle?> getCandle,
            decimal tickSize = 0.25m,
            int lookback = 10,
            int minFvgTicks = 4,
            decimal vpMaxDistancePoints = 8m)
        {
            _getCandle = getCandle;
            _tickSize = tickSize <= 0 ? 0.25m : tickSize;
            _lookback = Math.Max(5, lookback);
            _minFvgGap = Math.Max(1, minFvgTicks) * _tickSize;

            // VP过滤阈值（点数）
            _vpMaxDistancePoints = Math.Max(0m, vpMaxDistancePoints);
        }

        public ZoneUpdateResult Update(int bar, StructureUpdateResult structureResult)
        {
            var newZones = new List<TradingZone>();

            var current = _getCandle(bar);
            if (current is null)
            {
                return new ZoneUpdateResult
                {
                    ActiveZones = GetActiveZones()
                };
            }

            UpdateZoneStates(bar, current);

            TryAddFvg(bar, newZones);
            TryAddOrderBlock(bar, structureResult, newZones);

            foreach (var zone in newZones)
                _zones.Add(zone);

            return new ZoneUpdateResult
            {
                NewZones = newZones,
                ActiveZones = GetActiveZones()
            };
        }

        private void UpdateZoneStates(int bar, IndicatorCandle current)
        {
            // 用于边界容错（避免舍入导致“明明到价了但没算触碰/缓解/失效”）
            var eps = _tickSize / 2m;

            // BearOB“两次收盘站上上沿才失效”需要上一根K线
            var prev = bar > 0 ? _getCandle(bar - 1) : null;

            foreach (var zone in _zones)
            {
                if (zone.IsInvalidated)
                    continue;

                var wasTouched = zone.IsTouched;

                // touched：价格与区间发生重叠就算触碰
                var isOverlapping =
                    current.High >= zone.Low - eps &&
                    current.Low <= zone.High + eps;

                if (isOverlapping)
                    zone.IsTouched = true;

                // VP 过滤：只在“首次触碰”的那个时刻做一次
                // 不通过 => 仅标记 IsVpRejected=true（灰色显示），不再直接 invalidated
                if (!wasTouched && zone.IsTouched)
                {
                    if (_vpMaxDistancePoints > 0m)
                    {
                        var touchPrice = GetTouchPrice(zone, current);
                        var passVp = PassVpFilter(current, touchPrice, _vpMaxDistancePoints);

                        if (!passVp)
                            zone.IsVpRejected = true;
                    }
                }

                // 后续：常规生命周期（失效/缓解）
                switch (zone.Type)
                {
                    // --- OB：失效规则 ---
                    case ZoneType.BullishOB:
                        // BullOB：一次收盘跌破下沿即失效
                        if (current.Close < zone.Low - eps)
                            zone.IsInvalidated = true;
                        break;

                    case ZoneType.BearishOB:
                        // BearOB：连续两根收盘站上上沿才失效（更耐用）
                        if (prev is not null &&
                            prev.Close > zone.High + eps &&
                            current.Close > zone.High + eps)
                        {
                            zone.IsInvalidated = true;
                        }
                        break;

                    // --- FVG：Mitigated 与 Invalidated 分离 ---
                    case ZoneType.BullishFVG:
                        {
                            var mid = zone.Low + (zone.High - zone.Low) * FvgMitigationPercent;

                            // Mitigated：回补到 50%（wick 到 mid）=> 只标记，不移除
                            if (current.Low <= mid + eps)
                                zone.IsMitigated = true;

                            // Invalidated：收盘价穿越下沿 => 真正失效移除（更耐用）
                            if (current.Close < zone.Low - eps)
                                zone.IsInvalidated = true;

                            break;
                        }

                    case ZoneType.BearishFVG:
                        {
                            var mid = zone.Low + (zone.High - zone.Low) * FvgMitigationPercent;

                            // Mitigated：回补到 50%（wick 到 mid）=> 只标记，不移除
                            if (current.High >= mid - eps)
                                zone.IsMitigated = true;

                            // Invalidated：收盘价穿越上沿 => 真正失效移除（更耐用）
                            if (current.Close > zone.High + eps)
                                zone.IsInvalidated = true;

                            break;
                        }
                }
            }
        }

        private void TryAddFvg(int bar, List<TradingZone> newZones)
        {
            if (bar < 2)
                return;

            var left = _getCandle(bar - 2);
            var middle = _getCandle(bar - 1);
            var right = _getCandle(bar);

            if (left is null || middle is null || right is null)
                return;

            // 位移过滤：中间K线需要扩张
            var middleRange = middle.High - middle.Low;
            if (middleRange < MinDisplacementRange)
                return;

            // 相对阈值：maxRange = min(HardCap, middleRange * K)
            var maxAllowedFvgRange = Math.Min(FvgHardCapPoints, middleRange * FvgMaxRangeMultiplierK);

            // Bullish FVG: High[bar-2] < Low[bar]
            var bullishGap = right.Low - left.High;
            if (bullishGap >= _minFvgGap)
            {
                AddOrMergeFvg(
                    newZones,
                    ZoneType.BullishFVG,
                    startBar: bar - 2,
                    createdBar: bar,
                    low: left.High,
                    high: right.Low,
                    maxAllowedRange: maxAllowedFvgRange);
            }

            // Bearish FVG: Low[bar-2] > High[bar]
            var bearishGap = left.Low - right.High;
            if (bearishGap >= _minFvgGap)
            {
                AddOrMergeFvg(
                    newZones,
                    ZoneType.BearishFVG,
                    startBar: bar - 2,
                    createdBar: bar,
                    low: right.High,
                    high: left.Low,
                    maxAllowedRange: maxAllowedFvgRange);
            }
        }

        private void AddOrMergeFvg(
            List<TradingZone> newZones,
            ZoneType type,
            int startBar,
            int createdBar,
            decimal low,
            decimal high,
            decimal maxAllowedRange)
        {
            if (type != ZoneType.BullishFVG && type != ZoneType.BearishFVG)
                throw new ArgumentException("AddOrMergeFvg is only for FVG zone types.", nameof(type));

            if (low > high)
                (low, high) = (high, low);

            // 单个候选 FVG 高度过大，直接忽略
            if ((high - low) > maxAllowedRange)
                return;

            var mergedLow = low;
            var mergedHigh = high;
            var mergedStartBar = startBar;
            var mergedCreatedBar = createdBar;

            var toMerge = new HashSet<TradingZone>();

            while (true)
            {
                var overlaps = _zones
                    .Where(z =>
                        !z.IsInvalidated &&
                        z.Type == type &&
                        GetOverlapRatioByMinRange(mergedLow, mergedHigh, z.Low, z.High) >= FvgMergeOverlapRatio)
                    .ToList();

                var anyNew = false;

                foreach (var z in overlaps)
                {
                    if (toMerge.Add(z))
                    {
                        anyNew = true;

                        mergedLow = Math.Min(mergedLow, z.Low);
                        mergedHigh = Math.Max(mergedHigh, z.High);
                        mergedStartBar = Math.Min(mergedStartBar, z.StartBar);
                        mergedCreatedBar = Math.Max(mergedCreatedBar, z.CreatedBar);
                    }
                }

                if (!anyNew)
                    break;
            }

            // 合并后如果过大，直接放弃（并且不删除旧的）
            if ((mergedHigh - mergedLow) > maxAllowedRange)
                return;

            // 继承状态：Touched / Mitigated / VpRejected 取 OR（只要任一旧区块为 true）
            var mergedTouched = toMerge.Any(z => z.IsTouched);
            var mergedMitigated = toMerge.Any(z => z.IsMitigated);
            var mergedVpRejected = toMerge.Any(z => z.IsVpRejected);

            var exists = _zones.Any(z =>
                !z.IsInvalidated &&
                z.Type == type &&
                z.StartBar == mergedStartBar &&
                z.CreatedBar == mergedCreatedBar &&
                Math.Abs(z.Low - mergedLow) < _tickSize / 2m &&
                Math.Abs(z.High - mergedHigh) < _tickSize / 2m);

            if (exists)
                return;

            foreach (var z in toMerge)
                _zones.Remove(z);

            var name = type == ZoneType.BullishFVG ? "BullFVG" : "BearFVG";
            var zone = new TradingZone
            {
                Type = type,
                StartBar = mergedStartBar,
                CreatedBar = mergedCreatedBar,
                Low = mergedLow,
                High = mergedHigh,
                IsTouched = mergedTouched,
                IsMitigated = mergedMitigated,
                IsVpRejected = mergedVpRejected,
                Text = $"{name} {mergedLow:0.00}-{mergedHigh:0.00}"
            };

            newZones.Add(zone);
        }

        private void TryAddOrderBlock(int bar, StructureUpdateResult structureResult, List<TradingZone> newZones)
        {
            if (!structureResult.HasEvents)
                return;

            foreach (var evt in structureResult.Events)
            {
                // V1：只接受 BOS 产生 OB（CHOCH 暂不生成 OB）
                if (evt.EventType != StructureEventType.BOS)
                    continue;

                if (structureResult.Snapshot.BreakDirection == BreakDirection.Up)
                {
                    TryAddBullishOrderBlock(breakBar: bar, createdBar: evt.Bar, newZones: newZones);
                }
                else if (structureResult.Snapshot.BreakDirection == BreakDirection.Down)
                {
                    TryAddBearishOrderBlock(breakBar: bar, createdBar: evt.Bar, newZones: newZones);
                }
            }
        }

        private void TryAddBullishOrderBlock(int breakBar, int createdBar, List<TradingZone> newZones)
        {
            var start = Math.Max(0, breakBar - _lookback);

            for (var i = breakBar - 1; i >= start; i--)
            {
                var candle = _getCandle(i);
                if (candle is null)
                    continue;

                if (candle.Close < candle.Open)
                {
                    // BullOB：Low -> Open
                    var low = candle.Low;
                    var high = candle.Open;

                    AddZone(
                        newZones,
                        ZoneType.BullishOB,
                        startBar: i,
                        createdBar: createdBar,
                        low: low,
                        high: high,
                        text: $"BullOB {Math.Min(low, high):0.00}-{Math.Max(low, high):0.00}");
                    return;
                }
            }
        }

        private void TryAddBearishOrderBlock(int breakBar, int createdBar, List<TradingZone> newZones)
        {
            var start = Math.Max(0, breakBar - _lookback);

            for (var i = breakBar - 1; i >= start; i--)
            {
                var candle = _getCandle(i);
                if (candle is null)
                    continue;

                if (candle.Close > candle.Open)
                {
                    // BearOB：Open -> High
                    var low = candle.Open;
                    var high = candle.High;

                    AddZone(
                        newZones,
                        ZoneType.BearishOB,
                        startBar: i,
                        createdBar: createdBar,
                        low: low,
                        high: high,
                        text: $"BearOB {Math.Min(low, high):0.00}-{Math.Max(low, high):0.00}");
                    return;
                }
            }
        }

        private void AddZone(
            List<TradingZone> newZones,
            ZoneType type,
            int startBar,
            int createdBar,
            decimal low,
            decimal high,
            string text)
        {
            if (low > high)
                (low, high) = (high, low);

            var exists = _zones.Any(z =>
                z.Type == type &&
                z.StartBar == startBar &&
                z.CreatedBar == createdBar &&
                Math.Abs(z.Low - low) < _tickSize / 2m &&
                Math.Abs(z.High - high) < _tickSize / 2m);

            if (exists)
                return;

            newZones.Add(new TradingZone
            {
                Type = type,
                StartBar = startBar,
                CreatedBar = createdBar,
                Low = low,
                High = high,
                Text = text
            });
        }

        private IReadOnlyList<TradingZone> GetActiveZones()
        {
            var active = _zones.Where(z => !z.IsInvalidated);

            var obs = active
                .Where(z => z.Type == ZoneType.BullishOB || z.Type == ZoneType.BearishOB)
                .OrderByDescending(z => z.CreatedBar)
                .Take(MaxActiveObs);

            var fvgs = active
                .Where(z => z.Type == ZoneType.BullishFVG || z.Type == ZoneType.BearishFVG)
                .OrderByDescending(z => z.CreatedBar)
                .Take(MaxActiveFvgs);

            return obs
                .Concat(fvgs)
                .OrderByDescending(z => z.CreatedBar)
                .ToList();
        }

        private static decimal GetTouchPrice(TradingZone zone, IndicatorCandle candle)
        {
            // 触碰价：更贴近“进入区块的那一下”
            // 多头区：用 Low（下探进入）
            // 空头区：用 High（上探进入）
            return zone.Type switch
            {
                ZoneType.BullishOB or ZoneType.BullishFVG
                    => Clamp(candle.Low, zone.Low, zone.High),

                ZoneType.BearishOB or ZoneType.BearishFVG
                    => Clamp(candle.High, zone.Low, zone.High),

                _ => candle.Close
            };
        }

        private static decimal Clamp(decimal x, decimal min, decimal max)
        {
            if (min > max) (min, max) = (max, min);
            if (x < min) return min;
            if (x > max) return max;
            return x;
        }

        private static bool PassVpFilter(IndicatorCandle candle, decimal price, decimal maxDistancePoints)
        {
            // 取 VP 参考值：POC/VAH/VAL/VWAP（任一接近即可）
            var poc = candle.MaxVolumePriceInfo?.Price ?? 0m;
            var vah = candle.ValueArea?.ValueAreaHigh ?? 0m;
            var val = candle.ValueArea?.ValueAreaLow ?? 0m;
            var vwap = TryGetVwap(candle);

            var refs = new List<decimal>(4);
            if (poc != 0m) refs.Add(poc);
            if (vah != 0m) refs.Add(vah);
            if (val != 0m) refs.Add(val);
            if (vwap != 0m) refs.Add(vwap);

            // 如果拿不到 VP（数据缺失），不做过滤（避免误杀）
            if (refs.Count == 0)
                return true;

            foreach (var r in refs)
            {
                if (Math.Abs(price - r) <= maxDistancePoints)
                    return true;
            }

            return false;
        }

        private static decimal TryGetVwap(IndicatorCandle candle)
        {
            _vwapProp ??= candle.GetType().GetProperty("VWAP")
                         ?? candle.GetType().GetProperty("Vwap");

            if (_vwapProp is null)
                return 0m;

            var value = _vwapProp.GetValue(candle);

            return value switch
            {
                decimal d => d,
                double db => (decimal)db,
                float f => (decimal)f,
                _ => 0m
            };
        }


        private static decimal GetOverlapRatioByMinRange(decimal low1, decimal high1, decimal low2, decimal high2)
        {
            if (low1 > high1) (low1, high1) = (high1, low1);
            if (low2 > high2) (low2, high2) = (high2, low2);

            var range1 = high1 - low1;
            var range2 = high2 - low2;
            if (range1 <= 0 || range2 <= 0)
                return 0m;

            var intersection = Math.Min(high1, high2) - Math.Max(low1, low2);
            if (intersection <= 0)
                return 0m;

            var minRange = Math.Min(range1, range2);
            if (minRange <= 0)
                return 0m;

            return intersection / minRange;
        }
    }
}