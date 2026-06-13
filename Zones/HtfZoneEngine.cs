// File: Zones/HtfZoneEngine.cs
using ATAS.Indicators;
using NQOrderFlowV1.Models;
using NQOrderFlowV1.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NQOrderFlowV1.Zones
{
    /// <summary>
    /// HTF Zone Engine（用于“挂在 M5 图上运行，但生成/维护 M15 区块”的主引擎）
    ///
    /// 口径：
    /// - 新区块（OB/FVG）在 HTF 收盘时（OnHtfClosed）生成
    /// - touched/mitigated/VP过滤 在每根 base（M5）bar 更新（UpdateByBaseBar）
    /// - invalidated（失效）仅以 HTF（M15）收盘 close 为准（在 OnHtfClosed 内更新）
    ///
    /// VP 过滤（本次优化）：
    /// - 仍只在“首次触碰”时做一次过滤
    /// - 距离口径改为：对每个 VP 参考(POC/VAH/VAL/VWAP)计算
    ///     effDist = min( abs(touchPrice - ref), dist(ref, zoneRange) )
    ///   即：触碰价离参考近，或参考本身靠近/落在区块范围内，都视为通过
    /// - 增加 tick 容差：maxDistancePoints + tickSize/2，减少边界误拒
    ///
    /// HUD 可诊断：
    /// - VP 过滤失败时缓存“首次触碰当下”的诊断信息，可由 TryGetVpRejectInfo 查询
    /// </summary>
    public sealed class HtfZoneEngine
    {
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

        private readonly List<TradingZone> _zones = new();

        // =========================
        // VP Reject Diagnostics (首次触碰时落地)
        // =========================
        private readonly record struct ZoneId(ZoneType Type, int StartBar, int CreatedBar, decimal Low, decimal High);

        public sealed record VpRejectInfo
        {
            public DateTime? VpBucketTime { get; init; }

            /// <summary>首次触碰时用于 VP 过滤的触碰价（与引擎口径一致）</summary>
            public decimal TouchPrice { get; init; }

            /// <summary>最近的参考名：POC/VAH/VAL/VWAP</summary>
            public string NearestRefName { get; init; } = "-";

            public decimal NearestRefPrice { get; init; }

            /// <summary>
            /// 本次 VP 过滤使用的“有效距离”(effDist)：
            /// effDist = min(abs(touch-ref), dist(ref, zoneRange))
            /// </summary>
            public decimal DistancePoints { get; init; }

            public decimal MaxAllowedDistancePoints { get; init; }
        }

        private readonly Dictionary<ZoneId, VpRejectInfo> _vpRejectInfos = new();

        public HtfZoneEngine(decimal tickSize = 0.25m, int lookback = 10, int minFvgTicks = 4, decimal vpMaxDistancePoints = 8m)
        {
            _tickSize = tickSize <= 0 ? 0.25m : tickSize;
            _lookback = Math.Max(5, lookback);
            _minFvgGap = Math.Max(1, minFvgTicks) * _tickSize;

            // VP过滤阈值（点数）
            _vpMaxDistancePoints = Math.Max(0m, vpMaxDistancePoints);
        }

        /// <summary>
        /// 供策略层查询：某个 zone 被 VP 拒绝时，首次触碰当下的诊断信息。
        /// </summary>
        public bool TryGetVpRejectInfo(TradingZone zone, out VpRejectInfo info)
        {
            if (zone is null)
            {
                info = null!;
                return false;
            }

            return _vpRejectInfos.TryGetValue(ToId(zone), out info!);
        }

        /// <summary>
        /// 每根 base（例如 M5）bar 更新：
        /// - touched / mitigated
        /// - VP过滤（首次触碰时）
        ///
        /// 注意：invalidated 已改为仅在 HTF 收盘时确认（见 OnHtfClosed），这里不再失效删除区块。
        /// </summary>
        public void UpdateByBaseBar(
            IndicatorCandle? prevBase,
            IndicatorCandle curBase,
            Func<HigherTimeframeAggregator.HtfCandle?> getVpCandle)
        {
            var eps = _tickSize / 2m;

            foreach (var z in _zones)
            {
                if (z.IsInvalidated)
                    continue;

                var wasTouched = z.IsTouched;

                // touched：价格与区间发生重叠就算触碰
                var overlapping = curBase.High >= z.Low - eps && curBase.Low <= z.High + eps;
                if (overlapping)
                    z.IsTouched = true;

                // VP过滤：只在“首次触碰”时做一次
                if (!wasTouched && z.IsTouched && _vpMaxDistancePoints > 0m)
                {
                    var vp = getVpCandle();
                    if (vp is not null)
                    {
                        var touchPrice = GetTouchPrice(z, curBase);

                        var eval = EvaluateVpFilter(vp, z, touchPrice, _vpMaxDistancePoints, _tickSize);

                        if (!eval.Pass)
                        {
                            z.IsVpRejected = true;

                            var id = ToId(z);
                            _vpRejectInfos[id] = new VpRejectInfo
                            {
                                VpBucketTime = vp.BucketTime,
                                TouchPrice = touchPrice,
                                NearestRefName = eval.RefName,
                                NearestRefPrice = eval.RefPrice,
                                DistancePoints = eval.EffectiveDistancePoints,
                                MaxAllowedDistancePoints = _vpMaxDistancePoints
                            };
                        }
                    }
                }

                // mitigated（缓解）仍允许用 LTF wick 来更新（更及时），但不移除
                switch (z.Type)
                {
                    case ZoneType.BullishFVG:
                        {
                            var mid = z.Low + (z.High - z.Low) * FvgMitigationPercent;
                            if (curBase.Low <= mid + eps)
                                z.IsMitigated = true;
                            break;
                        }

                    case ZoneType.BearishFVG:
                        {
                            var mid = z.Low + (z.High - z.Low) * FvgMitigationPercent;
                            if (curBase.High >= mid - eps)
                                z.IsMitigated = true;
                            break;
                        }
                }
            }
        }

        /// <summary>
        /// 在 HTF 桶收盘确认时调用：
        /// 1) 用 HTF close 更新 invalidation（更耐用、符合 HTF 定义）
        /// 2) 生成新 FVG / OB
        /// </summary>
        public ZoneUpdateResult OnHtfClosed(
            int htfIndex,
            IReadOnlyList<HigherTimeframeAggregator.HtfCandle> series,
            StructureUpdateResult structureResult)
        {
            // 先用“已收盘的 HTF candle”更新失效
            UpdateInvalidationsByHtfClose(htfIndex, series);

            var newZones = new List<TradingZone>();

            TryAddFvg(htfIndex, series, newZones);
            TryAddOrderBlock(htfIndex, series, structureResult, newZones);

            foreach (var z in newZones)
                _zones.Add(z);

            return new ZoneUpdateResult
            {
                NewZones = newZones,
                ActiveZones = GetActiveZones()
            };
        }

        public IReadOnlyList<TradingZone> GetActiveZones()
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

            return obs.Concat(fvgs).OrderByDescending(z => z.CreatedBar).ToList();
        }

        private void UpdateInvalidationsByHtfClose(int htfIndex, IReadOnlyList<HigherTimeframeAggregator.HtfCandle> series)
        {
            if (series is null || htfIndex < 0 || htfIndex >= series.Count)
                return;

            var cur = series[htfIndex];
            var prev = htfIndex > 0 ? series[htfIndex - 1] : null;

            var eps = _tickSize / 2m;

            foreach (var z in _zones)
            {
                if (z.IsInvalidated)
                    continue;

                // 避免“刚创建就用同一根 HTF close 立即失效”
                if (cur.BaseEndBar <= z.CreatedBar)
                    continue;

                var invalidatedNow = false;

                switch (z.Type)
                {
                    case ZoneType.BullishOB:
                        // BullOB：HTF 收盘跌破下沿才失效
                        if (cur.Close < z.Low - eps)
                            invalidatedNow = true;
                        break;

                    case ZoneType.BearishOB:
                        // BearOB：连续两根 HTF 收盘站上上沿才失效（更耐用）
                        if (prev is not null &&
                            prev.BaseEndBar > z.CreatedBar &&
                            prev.Close > z.High + eps &&
                            cur.Close > z.High + eps)
                        {
                            invalidatedNow = true;
                        }
                        break;

                    case ZoneType.BullishFVG:
                        // BullFVG：HTF 收盘穿越下沿才失效
                        if (cur.Close < z.Low - eps)
                            invalidatedNow = true;
                        break;

                    case ZoneType.BearishFVG:
                        // BearFVG：HTF 收盘穿越上沿才失效
                        if (cur.Close > z.High + eps)
                            invalidatedNow = true;
                        break;
                }

                if (invalidatedNow)
                {
                    z.IsInvalidated = true;
                    _vpRejectInfos.Remove(ToId(z));
                }
            }
        }

        // =========================
        // HTF: FVG
        // =========================
        private void TryAddFvg(int htfIndex, IReadOnlyList<HigherTimeframeAggregator.HtfCandle> s, List<TradingZone> newZones)
        {
            if (htfIndex < 2)
                return;

            var left = s[htfIndex - 2];
            var middle = s[htfIndex - 1];
            var right = s[htfIndex];

            var middleRange = middle.High - middle.Low;
            if (middleRange < MinDisplacementRange)
                return;

            var maxAllowed = Math.Min(FvgHardCapPoints, middleRange * FvgMaxRangeMultiplierK);

            // Bull FVG: left.High < right.Low
            var bullGap = right.Low - left.High;
            if (bullGap >= _minFvgGap)
            {
                AddOrMergeFvg(
                    newZones,
                    ZoneType.BullishFVG,
                    startBar: left.BaseStartBar,
                    createdBar: right.BaseEndBar,
                    low: left.High,
                    high: right.Low,
                    maxAllowedRange: maxAllowed);
            }

            // Bear FVG: left.Low > right.High
            var bearGap = left.Low - right.High;
            if (bearGap >= _minFvgGap)
            {
                AddOrMergeFvg(
                    newZones,
                    ZoneType.BearishFVG,
                    startBar: left.BaseStartBar,
                    createdBar: right.BaseEndBar,
                    low: right.High,
                    high: left.Low,
                    maxAllowedRange: maxAllowed);
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
            if (low > high)
                (low, high) = (high, low);

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

            if ((mergedHigh - mergedLow) > maxAllowedRange)
                return;

            var mergedTouched = toMerge.Any(z => z.IsTouched);
            var mergedMitigated = toMerge.Any(z => z.IsMitigated);
            var mergedRejected = toMerge.Any(z => z.IsVpRejected);

            // 合并 VP reject 诊断：选“最接近通过”的那个（EffectiveDistance 最小）
            VpRejectInfo? mergedVpInfo = null;
            if (mergedRejected && toMerge.Count > 0)
            {
                foreach (var z in toMerge)
                {
                    if (_vpRejectInfos.TryGetValue(ToId(z), out var info))
                    {
                        if (mergedVpInfo is null || info.DistancePoints < mergedVpInfo.DistancePoints)
                            mergedVpInfo = info;
                    }
                }
            }

            var exists = _zones.Any(z =>
                !z.IsInvalidated &&
                z.Type == type &&
                z.StartBar == mergedStartBar &&
                z.CreatedBar == mergedCreatedBar &&
                Math.Abs(z.Low - mergedLow) < _tickSize / 2m &&
                Math.Abs(z.High - mergedHigh) < _tickSize / 2m);

            if (exists)
                return;

            // 删除被合并的旧区块，同时清理 VP reject 缓存
            foreach (var z in toMerge)
            {
                _zones.Remove(z);
                _vpRejectInfos.Remove(ToId(z));
            }

            var name = type == ZoneType.BullishFVG ? "BullFVG" : "BearFVG";

            var mergedZone = new TradingZone
            {
                Type = type,
                StartBar = mergedStartBar,
                CreatedBar = mergedCreatedBar,
                Low = mergedLow,
                High = mergedHigh,
                IsTouched = mergedTouched,
                IsMitigated = mergedMitigated,
                IsVpRejected = mergedRejected,
                Text = $"{name} {mergedLow:0.00}-{mergedHigh:0.00}"
            };

            newZones.Add(mergedZone);

            // 合并后若仍为 VP 拒绝，给新 zone 继续挂上诊断信息（若有）
            if (mergedRejected && mergedVpInfo is not null)
            {
                _vpRejectInfos[ToId(mergedZone)] = mergedVpInfo with
                {
                    MaxAllowedDistancePoints = _vpMaxDistancePoints
                };
            }
        }

        // =========================
        // HTF: OB (only from BOS)
        // =========================
        private void TryAddOrderBlock(
            int htfIndex,
            IReadOnlyList<HigherTimeframeAggregator.HtfCandle> s,
            StructureUpdateResult structureResult,
            List<TradingZone> newZones)
        {
            if (!structureResult.HasEvents)
                return;

            foreach (var evt in structureResult.Events)
            {
                if (evt.EventType != StructureEventType.BOS)
                    continue;

                if (structureResult.Snapshot.BreakDirection == BreakDirection.Up)
                    TryAddBullishOrderBlock(htfIndex, s, newZones);
                else if (structureResult.Snapshot.BreakDirection == BreakDirection.Down)
                    TryAddBearishOrderBlock(htfIndex, s, newZones);
            }
        }

        private void TryAddBullishOrderBlock(int htfIndex, IReadOnlyList<HigherTimeframeAggregator.HtfCandle> s, List<TradingZone> newZones)
        {
            var start = Math.Max(0, htfIndex - _lookback);

            for (var i = htfIndex - 1; i >= start; i--)
            {
                var c = s[i];
                if (c.Close < c.Open)
                {
                    // BullOB：Low -> Open（去掉上影线）
                    AddZone(
                        newZones,
                        ZoneType.BullishOB,
                        startBar: c.BaseStartBar,
                        createdBar: s[htfIndex].BaseEndBar,
                        low: c.Low,
                        high: c.Open,
                        text: $"BullOB {Math.Min(c.Low, c.Open):0.00}-{Math.Max(c.Low, c.Open):0.00}");
                    return;
                }
            }
        }

        private void TryAddBearishOrderBlock(int htfIndex, IReadOnlyList<HigherTimeframeAggregator.HtfCandle> s, List<TradingZone> newZones)
        {
            var start = Math.Max(0, htfIndex - _lookback);

            for (var i = htfIndex - 1; i >= start; i--)
            {
                var c = s[i];
                if (c.Close > c.Open)
                {
                    // BearOB：Open -> High（去掉下影线）
                    AddZone(
                        newZones,
                        ZoneType.BearishOB,
                        startBar: c.BaseStartBar,
                        createdBar: s[htfIndex].BaseEndBar,
                        low: c.Open,
                        high: c.High,
                        text: $"BearOB {Math.Min(c.Open, c.High):0.00}-{Math.Max(c.Open, c.High):0.00}");
                    return;
                }
            }
        }

        private void AddZone(List<TradingZone> newZones, ZoneType type, int startBar, int createdBar, decimal low, decimal high, string text)
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

        // =========================
        // Helpers
        // =========================
        private ZoneId ToId(TradingZone z) => new(z.Type, z.StartBar, z.CreatedBar, z.Low, z.High);

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

        private static decimal GetTouchPrice(TradingZone zone, IndicatorCandle candle)
        {
            return zone.Type switch
            {
                ZoneType.BullishOB or ZoneType.BullishFVG => Clamp(candle.Low, zone.Low, zone.High),
                ZoneType.BearishOB or ZoneType.BearishFVG => Clamp(candle.High, zone.Low, zone.High),
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

        private readonly record struct VpEvalResult(
            bool Pass,
            string RefName,
            decimal RefPrice,
            decimal EffectiveDistancePoints);

        private static VpEvalResult EvaluateVpFilter(
            HigherTimeframeAggregator.HtfCandle vp,
            TradingZone zone,
            decimal touchPrice,
            decimal maxDistancePoints,
            decimal tickSize)
        {
            if (maxDistancePoints <= 0m)
                return new VpEvalResult(true, "-", 0m, 0m);

            var refs = new List<(string Name, decimal Price)>(4);

            if (vp.POC != 0m) refs.Add(("POC", vp.POC));
            if (vp.VAH != 0m) refs.Add(("VAH", vp.VAH));
            if (vp.VAL != 0m) refs.Add(("VAL", vp.VAL));
            if (vp.VWAP != 0m) refs.Add(("VWAP", vp.VWAP));

            // 如果拿不到 VP 参考值，不做拒绝（避免误杀）
            if (refs.Count == 0)
                return new VpEvalResult(true, "-", 0m, 0m);

            // tick 容差：减少边界误拒
            var tol = tickSize > 0m ? tickSize / 2m : 0m;
            var allow = maxDistancePoints + tol;

            var best = refs[0];
            var bestEff = ComputeEffectiveDistance(best.Price, touchPrice, zone.Low, zone.High);

            for (var i = 1; i < refs.Count; i++)
            {
                var eff = ComputeEffectiveDistance(refs[i].Price, touchPrice, zone.Low, zone.High);
                if (eff < bestEff)
                {
                    best = refs[i];
                    bestEff = eff;
                }
            }

            var pass = bestEff <= allow;

            return new VpEvalResult(pass, best.Name, best.Price, bestEff);
        }

        private static decimal ComputeEffectiveDistance(decimal refPrice, decimal touchPrice, decimal zoneLow, decimal zoneHigh)
        {
            if (zoneLow > zoneHigh) (zoneLow, zoneHigh) = (zoneHigh, zoneLow);

            var touchDist = Math.Abs(touchPrice - refPrice);

            // ref 到区块区间的距离：ref 在区块内 => 0；否则到最近边界的距离
            decimal rangeDist;
            if (refPrice < zoneLow) rangeDist = zoneLow - refPrice;
            else if (refPrice > zoneHigh) rangeDist = refPrice - zoneHigh;
            else rangeDist = 0m;

            return Math.Min(touchDist, rangeDist);
        }
    }
}