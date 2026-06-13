using ATAS.Indicators;
using NQOrderFlowV1.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NQOrderFlowV1.Zones
{
    /// <summary>
    /// M5 Zone Engine (LTF):
    /// - Generate higher-frequency FVG zones on the base M5 chart.
    /// - Lightweight & durable state machine:
    ///   * Create on M5 close using (bar-2, bar) gap rule (same as HTF logic, but relaxed params)
    ///   * Touched: range overlap
    ///   * Mitigated: wick reaches 50% (mid)
    ///   * Invalidated: M5 close crosses boundary
    ///
    /// Notes:
    /// - No VP filter here (keep frequency high). Strategy can still filter later if desired.
    /// - TradingZone has init-only fields (Low/High/CreatedBar/StartBar...), so merging is implemented
    ///   by removing old zones and adding a new merged zone (immutable core).
    /// </summary>
    public sealed class M5ZoneEngine
    {
        private readonly Func<int, IndicatorCandle?> _getCandle;
        private readonly decimal _tickSize;

        private readonly List<TradingZone> _zones = new();
        private int _lastProcessedBar = -1;

        // Mitigation: 50%
        private const decimal FvgMitigationPercent = 0.50m;

        public M5ZoneEngine(Func<int, IndicatorCandle?> getCandle, decimal tickSize = 0.25m)
        {
            _getCandle = getCandle ?? throw new ArgumentNullException(nameof(getCandle));
            _tickSize = tickSize <= 0 ? 0.25m : tickSize;
        }

        // ===== Tunable parameters =====

        public bool EnableFvg { get; set; } = true;

        /// <summary>Min FVG gap in ticks. M5 default is smaller for higher frequency.</summary>
        public int MinFvgTicks { get; set; } = 2;

        /// <summary>
        /// Optional displacement filter (middle candle expansion).
        /// For M5 you may want it ON but much smaller than HTF.
        /// </summary>
        public bool EnableDisplacementFilter { get; set; } = true;

        /// <summary>Min range (High-Low) of the middle candle (points) when displacement filter is enabled.</summary>
        public decimal MinDisplacementRangePoints { get; set; } = 2.0m;

        /// <summary>
        /// Optional hard cap of zone width (points). 0 disables.
        /// Keep it OFF initially to maximize frequency, then tighten if too noisy.
        /// </summary>
        public decimal MaxZoneWidthPoints { get; set; } = 0m;

        /// <summary>Merge same-type zones when overlap ratio >= this threshold (by smaller range). Default 70%.</summary>
        public decimal MergeOverlapRatio { get; set; } = 0.70m;

        /// <summary>Keep at most N zones per type (BullFVG / BearFVG separately).</summary>
        public int MaxZonesPerType { get; set; } = 24;

        /// <summary>Remove zones older than this many bars (CreatedBar age). 0 disables.</summary>
        public int MaxLookbackBars { get; set; } = 800;

        // ===== Public API =====

        public void Reset()
        {
            _zones.Clear();
            _lastProcessedBar = -1;
        }

        /// <summary>
        /// Update on each M5 bar close (call from Strategy.OnCalculate with current bar index).
        /// Returns:
        /// - NewZones: zones created/merged on this bar
        /// - ActiveZones: current active zones snapshot (invalidated excluded, per-type capped)
        /// </summary>
        public ZoneUpdateResult Update(int bar)
        {
            if (bar < 0)
                return new ZoneUpdateResult { ActiveZones = GetActiveZones() };

            // Avoid duplicate processing if platform calls multiple times per bar index
            if (bar == _lastProcessedBar)
                return new ZoneUpdateResult { ActiveZones = GetActiveZones() };

            _lastProcessedBar = bar;

            var cur = _getCandle(bar);
            if (cur is null)
                return new ZoneUpdateResult { ActiveZones = GetActiveZones() };

            // 1) Update states first (using current bar)
            UpdateZoneStates(bar, cur);

            // 2) Create new zones
            var newZones = new List<TradingZone>();
            if (EnableFvg)
                TryAddFvg(bar, newZones);

            // 3) Append
            foreach (var z in newZones)
                _zones.Add(z);

            // 4) Cleanup/cap
            Cleanup(bar);

            return new ZoneUpdateResult
            {
                NewZones = newZones,
                ActiveZones = GetActiveZones()
            };
        }

        public IReadOnlyList<TradingZone> GetActiveZones()
        {
            var active = _zones.Where(z => !z.IsInvalidated);

            var bull = active
                .Where(z => z.Type == ZoneType.BullishFVG)
                .OrderByDescending(z => z.CreatedBar)
                .Take(Math.Max(0, MaxZonesPerType));

            var bear = active
                .Where(z => z.Type == ZoneType.BearishFVG)
                .OrderByDescending(z => z.CreatedBar)
                .Take(Math.Max(0, MaxZonesPerType));

            return bull
                .Concat(bear)
                .OrderByDescending(z => z.CreatedBar)
                .ToList();
        }

        // ===== Internals =====

        private void UpdateZoneStates(int bar, IndicatorCandle current)
        {
            var eps = _tickSize / 2m;

            foreach (var zone in _zones)
            {
                if (zone.IsInvalidated)
                    continue;

                // Don't update lifecycle on the creation bar for M5 FVG:
                // the "right" candle forming the gap touches the boundary by definition.
                if (bar <= zone.CreatedBar)
                    continue;

                // --- Invalidated (close confirmed) ---
                if (zone.Type == ZoneType.BullishFVG)
                {
                    if (current.Close < zone.Low - eps)
                    {
                        zone.IsInvalidated = true;
                        continue;
                    }
                }
                else if (zone.Type == ZoneType.BearishFVG)
                {
                    if (current.Close > zone.High + eps)
                    {
                        zone.IsInvalidated = true;
                        continue;
                    }
                }
                else
                {
                    continue; // M5 engine only manages FVG zones
                }

                // --- Touched (overlap) ---
                var overlapped =
                    current.High >= zone.Low - eps &&
                    current.Low <= zone.High + eps;

                if (overlapped)
                    zone.IsTouched = true;

                // --- Mitigated (75% fill) ---
                if (zone.IsTouched && !zone.IsMitigated)
                {
                    var range = zone.High - zone.Low;
                    if (range > 0m)
                    {
                        const decimal fill = 0.75m;

                        if (zone.Type == ZoneType.BullishFVG)
                        {
                            // bull: price retraces DOWN, require deep fill -> threshold near Low
                            var level = zone.High - range * fill;     // = Low + range*(1-fill)
                            if (current.Low <= level + eps)
                                zone.IsMitigated = true;
                        }
                        else if (zone.Type == ZoneType.BearishFVG)
                        {
                            // bear: price retraces UP, require deep fill -> threshold near High
                            var level = zone.Low + range * fill;
                            if (current.High >= level - eps)
                                zone.IsMitigated = true;
                        }
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

            // Displacement filter (optional, relaxed for M5)
            if (EnableDisplacementFilter)
            {
                var middleRange = middle.High - middle.Low;
                if (middleRange < MinDisplacementRangePoints)
                    return;
            }

            var eps = _tickSize / 2m;
            var minGap = Math.Max(1, MinFvgTicks) * _tickSize;

            // Bullish FVG: left.High < right.Low
            var bullGap = right.Low - left.High;
            if (bullGap + eps >= minGap)
            {
                var low = left.High;
                var high = right.Low;

                AddOrMergeFvg(
                    newZones,
                    ZoneType.BullishFVG,
                    startBar: bar - 2,
                    createdBar: bar,
                    low: low,
                    high: high);
            }

            // Bearish FVG: left.Low > right.High
            var bearGap = left.Low - right.High;
            if (bearGap + eps >= minGap)
            {
                var low = right.High;
                var high = left.Low;

                AddOrMergeFvg(
                    newZones,
                    ZoneType.BearishFVG,
                    startBar: bar - 2,
                    createdBar: bar,
                    low: low,
                    high: high);
            }
        }

        private void AddOrMergeFvg(
            List<TradingZone> newZones,
            ZoneType type,
            int startBar,
            int createdBar,
            decimal low,
            decimal high)
        {
            if (type != ZoneType.BullishFVG && type != ZoneType.BearishFVG)
                throw new ArgumentException("M5ZoneEngine only supports FVG types.", nameof(type));

            if (low > high)
                (low, high) = (high, low);

            var eps = _tickSize / 2m;

            // Width cap (optional)
            if (MaxZoneWidthPoints > 0m)
            {
                if ((high - low) > MaxZoneWidthPoints + eps)
                    return;
            }

            // Merge
            var mergeTh = Math.Max(0m, Math.Min(1m, MergeOverlapRatio));

            var mergedLow = low;
            var mergedHigh = high;
            var mergedStartBar = startBar;
            var mergedCreatedBar = createdBar;

            var toMerge = new HashSet<TradingZone>();

            if (mergeTh > 0m)
            {
                while (true)
                {
                    var overlaps = _zones
                        .Where(z =>
                            !z.IsInvalidated &&
                            z.Type == type &&
                            GetOverlapRatioByMinRange(mergedLow, mergedHigh, z.Low, z.High) >= mergeTh)
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
            }

            // After merge, width cap again
            if (MaxZoneWidthPoints > 0m)
            {
                if ((mergedHigh - mergedLow) > MaxZoneWidthPoints + eps)
                    return;
            }

            // Prevent exact duplicates
            var exists = _zones.Any(z =>
                !z.IsInvalidated &&
                z.Type == type &&
                Math.Abs(z.Low - mergedLow) < eps &&
                Math.Abs(z.High - mergedHigh) < eps &&
                z.CreatedBar == mergedCreatedBar);

            if (exists)
                return;

            // Inherit flags from merged old zones (OR)
            var mergedTouched = toMerge.Any(z => z.IsTouched);
            var mergedMitigated = toMerge.Any(z => z.IsMitigated);
            var mergedVpRejected = toMerge.Any(z => z.IsVpRejected); // should be false in M5 engine, but keep for compatibility

            // Remove merged zones from storage (they will be replaced by the new merged zone)
            foreach (var z in toMerge)
                _zones.Remove(z);

            var text = type == ZoneType.BullishFVG
                ? $"M5BullFVG {mergedLow:0.00}-{mergedHigh:0.00}"
                : $"M5BearFVG {mergedLow:0.00}-{mergedHigh:0.00}";

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
                IsInvalidated = false,
                Text = text
            };

            newZones.Add(zone);
        }

        private void Cleanup(int bar)
        {
            // Remove old zones by lookback
            var lb = Math.Max(0, MaxLookbackBars);
            if (lb > 0)
            {
                _zones.RemoveAll(z => z.CreatedBar >= 0 && (bar - z.CreatedBar) > lb);
            }

            // Per-type cap: keep most recent
            var cap = Math.Max(0, MaxZonesPerType);
            if (cap <= 0)
                return;

            TrimType(ZoneType.BullishFVG, cap);
            TrimType(ZoneType.BearishFVG, cap);

            void TrimType(ZoneType t, int max)
            {
                var list = _zones
                    .Where(z => z.Type == t && !z.IsInvalidated)
                    .OrderByDescending(z => z.CreatedBar)
                    .ToList();

                if (list.Count <= max)
                    return;

                var keep = new HashSet<TradingZone>(list.Take(max));
                _zones.RemoveAll(z => z.Type == t && !z.IsInvalidated && !keep.Contains(z));
            }
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