using ATAS.DataFeedsCore;
using ATAS.Indicators;
using ATAS.Strategies;
using ATAS.Strategies.Chart;
using NQOrderFlowV1.Engines;
using NQOrderFlowV1.Models;
using NQOrderFlowV1.Services;
using NQOrderFlowV1.Zones;
using OFT.Rendering.Context;
using OFT.Rendering.Tools;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace NQOrderFlowV1.Strategy
{
    public partial class NQOrderFlowStrategy
    {
        // ===== Tick/Qty alignment helpers (direction-safe) =====

        private decimal GetSlBufferPoints(int bar = -1)
            => SlBufferTicks * GetTickSize(bar, "GetSlBufferPoints");

        private static int GetDecimalPlaces(decimal value)
        {
            value = Math.Abs(value);
            var bits = decimal.GetBits(value);
            return (bits[3] >> 16) & 0xFF;
        }

        private decimal NormalizeByDecimals(decimal v, int decimals)
        {
            if (decimals < 0) decimals = 0;
            if (decimals > 28) decimals = 28;
            return Math.Round(v, decimals, MidpointRounding.ToZero);
        }

        // -------------------------
        // Points/Ticks resolver (P0.5)
        // -------------------------
        private decimal ResolvePointsFromTicksOrLegacy(int ticksParam, decimal legacyPoints, int bar, string name)
        {
            if (ticksParam > 0)
            {
                var tick = GetTickSize(bar, $"ResolvePoints({name})");
                return ticksParam * tick;
            }

            return Math.Max(0m, legacyPoints);
        }

        private int ResolveTicksIfSet(int ticksParam)
            => Math.Max(0, ticksParam);

        private decimal GetVpMaxDistancePointsEff(int bar)
            => ResolvePointsFromTicksOrLegacy(VpMaxDistanceTicks, VpMaxDistancePoints, bar, "VpMaxDist");

        private decimal GetConfirmDisplacementPointsEff(int bar)
            => ResolvePointsFromTicksOrLegacy(ConfirmDisplacementTicks, ConfirmDisplacementPoints, bar, "ConfirmDisp");

        private int GetConfirmDisplacementTicksIfSet()
            => ResolveTicksIfSet(ConfirmDisplacementTicks);

        private int GetMaxPlaceDistanceTicksIfSet()
            => ResolveTicksIfSet(MaxPlaceDistanceFromZoneTicks);

        private int GetMaxEntryDistanceTicksIfSet()
            => ResolveTicksIfSet(MaxEntryDistanceFromZoneTicks);

        private int GetRunawayCancelDistanceTicksIfSet()
            => ResolveTicksIfSet(RunawayCancelDistanceTicks);

        private int GetDynamicAnchorWidthThresholdTicksIfSet()
            => ResolveTicksIfSet(DynamicAnchorWidthThresholdTicks);

        private int GetRefMaxDistanceTicksIfSet()
            => ResolveTicksIfSet(RefMaxDistanceTicks);

        private decimal GetM5MinDisplacementRangePointsEff(int bar)
            => ResolvePointsFromTicksOrLegacy(M5MinDisplacementRangeTicks, M5MinDisplacementRangePoints, bar, "M5MinDispRange");

        private decimal GetM5MaxZoneWidthPointsEff(int bar)
            => ResolvePointsFromTicksOrLegacy(M5MaxZoneWidthTicks, M5MaxZoneWidthPoints, bar, "M5MaxZoneWidth");

        // -------------------------
        // Tick align
        // -------------------------

        private decimal FloorToTick(decimal price, int bar = -1)
        {
            var tick = GetTickSize(bar, "FloorToTick");
            if (tick <= 0m)
                return price;

            var ticks = Math.Floor(price / tick);
            var aligned = (decimal)ticks * tick;
            return NormalizeByDecimals(aligned, GetDecimalPlaces(tick));
        }

        private decimal CeilToTick(decimal price, int bar = -1)
        {
            var tick = GetTickSize(bar, "CeilToTick");
            if (tick <= 0m)
                return price;

            var ticks = Math.Ceiling(price / tick);
            var aligned = (decimal)ticks * tick;
            return NormalizeByDecimals(aligned, GetDecimalPlaces(tick));
        }

        private decimal RoundToTick(decimal price, int bar = -1)
        {
            var tick = GetTickSize(bar, "RoundToTick");
            if (tick <= 0m)
                return price;

            var ticks = Math.Round(price / tick, 0, MidpointRounding.AwayFromZero);
            var aligned = ticks * tick;
            return NormalizeByDecimals(aligned, GetDecimalPlaces(tick));
        }

        private int PointsToTicksRound(decimal points, int bar = -1)
        {
            var tick = GetTickSize(bar, "PointsToTicksRound");
            if (tick <= 0m)
                return 0;

            var t = Math.Abs(points) / tick;
            var ticks = (int)Math.Round((double)t, MidpointRounding.AwayFromZero);
            return Math.Abs(ticks);
        }

        private decimal AlignEntryLimitToTick(decimal raw, bool isLong, int bar = -1)
            => isLong ? FloorToTick(raw, bar) : CeilToTick(raw, bar);

        // Stop：方向安全（不放松风险）
        // - Long stop 在 entry 下方：Ceil 收紧（更靠近 entry）
        // - Short stop 在 entry 上方：Floor 收紧（更靠近 entry）
        private decimal AlignStopToTick(decimal rawStop, bool isLong, int bar = -1)
            => isLong ? CeilToTick(rawStop, bar) : FloorToTick(rawStop, bar);

        private decimal AlignTargetToTick(decimal rawTarget, bool isLong, int bar = -1)
            => isLong ? CeilToTick(rawTarget, bar) : FloorToTick(rawTarget, bar);

        private decimal AlignBreakEvenStopToTick(decimal rawNewStop, bool isLong, int bar = -1)
            => isLong ? CeilToTick(rawNewStop, bar) : FloorToTick(rawNewStop, bar);

        private decimal ComputeTargetFromRiskTicks(decimal entry, bool isLong, int riskTicks, decimal rr, int bar = -1)
        {
            var tick = GetTickSize(bar, "ComputeTargetFromRiskTicks");
            if (tick <= 0m)
                return entry;

            var safeRiskTicks = Math.Max(1, riskTicks);
            var safeRr = rr <= 0m ? 2m : rr;

            var tpTicksDec = safeRiskTicks * safeRr;
            var tpTicks = (int)Math.Ceiling((double)tpTicksDec);
            if (tpTicks < 1) tpTicks = 1;

            var raw = isLong
                ? entry + tpTicks * tick
                : entry - tpTicks * tick;

            return NormalizeByDecimals(raw, GetDecimalPlaces(tick));
        }

        // -------------------------
        // Qty alignment
        // -------------------------

        private decimal AlignQtyCeilForEntry(decimal rawQty, int bar = -1, string role = "ENTRY")
        {
            EnsureInstrumentRulesInitialized(bar, $"AlignQtyCeilForEntry({role})");

            var step = GetQtyStep(bar, "AlignQtyCeilForEntry.step");
            var min = GetMinQty(bar, "AlignQtyCeilForEntry.min");

            if (rawQty <= 0m)
                rawQty = min;

            var qty = rawQty < min ? min : rawQty;

            if (step <= 0m)
                return qty;

            var steps = Math.Ceiling(qty / step);
            var aligned = steps * step;

            aligned = NormalizeByDecimals(aligned, GetDecimalPlaces(step));
            if (aligned < min) aligned = min;

            return aligned;
        }

        private decimal AlignQtyFloorNotExceed(decimal rawQty, decimal maxQty, int bar = -1, string role = "BRACKET")
        {
            EnsureInstrumentRulesInitialized(bar, $"AlignQtyFloorNotExceed({role})");

            var step = GetQtyStep(bar, "AlignQtyFloorNotExceed.step");
            var min = GetMinQty(bar, "AlignQtyFloorNotExceed.min");

            if (maxQty <= 0m)
                return 0m;

            var qty = rawQty;
            if (qty > maxQty)
                qty = maxQty;

            if (qty <= 0m)
                return 0m;

            if (step <= 0m)
                return qty;

            var steps = Math.Floor(qty / step);
            var aligned = steps * step;
            aligned = NormalizeByDecimals(aligned, GetDecimalPlaces(step));

            if (aligned < 0m) aligned = 0m;

            if (aligned > 0m && aligned < min && min <= maxQty)
                aligned = min;

            if (aligned > maxQty)
                aligned = maxQty;

            return aligned;
        }

        private string NewOcoGroup(string tradeId)
            => $"{LiveCommentPrefix}|{tradeId}|OCO|{Guid.NewGuid():N}";
    }
}