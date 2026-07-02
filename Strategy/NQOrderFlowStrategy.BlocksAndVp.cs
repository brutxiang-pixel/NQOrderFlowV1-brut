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
        // =========================
        // Trigger block state setters
        // =========================
        private void ResetBarBlock()
        {
            _barBlockReason = TriggerBlockReason.None;
            _barBlockDetail = "-";
        }

        private void SetBlock(int bar, TriggerBlockReason reason, string detail)
        {
            _barBlockReason = reason;
            _barBlockDetail = detail;

            if (reason != TriggerBlockReason.None)
            {
                _lastBlockReason = reason;
                _lastBlockDetail = detail;
                _lastBlockBar = bar;
            }
        }

        // =========================
        // VP rejected detail helpers
        // =========================
        private string BuildVpRejectedDetail(IndicatorCandle cur, TradingZone zone)
        {
            var bar = Math.Max(0, _lastCalcBar);
            var tick = GetTickSize(bar, "BuildVpRejectedDetail");
            var maxAllowedPts = GetVpMaxDistancePointsEff(bar);
            var maxAllowedTicksText = tick > 0m ? $"{(maxAllowedPts / tick):0.##}t" : "-";

            if (_htfZones is not null && _htfZones.TryGetVpRejectInfo(zone, out var info))
            {
                var time = info.VpBucketTime.HasValue ? info.VpBucketTime.Value.ToString("yyyy-MM-dd HH:mm") : "-";
                var distTicksText = tick > 0m ? $"{(info.DistancePoints / tick):0.##}t" : "-";

                return $"VP拒绝 | touch={info.TouchPrice:0.########} nearest={info.NearestRefName}@{info.NearestRefPrice:0.########} " +
                       $"effDist={info.DistancePoints:0.########}pt({distTicksText}) > {info.MaxAllowedDistancePoints:0.########}pt({maxAllowedTicksText}) " +
                       $"| vpTime={time} | zone={zone.ToShortText()}";
            }

            var vp = _lastClosedVpCandle;
            var touchPrice = GetTouchPriceLikeZoneEngine(zone, cur);

            if (vp is null)
                return $"VP拒绝(无VP参考) | zone={zone.ToShortText()} | touch≈{touchPrice:0.########}";

            var (refName, refPrice, dist) = GetNearestVpRef(vp, touchPrice);
            var ok = dist <= maxAllowedPts;

            var distTicks = tick > 0m ? dist / tick : 0m;
            var distTicksText2 = tick > 0m ? $"{distTicks:0.##}t" : "-";

            return ok
                ? $"VP拒绝(flag已被标记但当前计算显示通过?) | touch≈{touchPrice:0.########} nearest={refName}@{refPrice:0.########} dist={dist:0.########}pt({distTicksText2}) <= {maxAllowedPts:0.########}pt({maxAllowedTicksText}) | zone={zone.ToShortText()}"
                : $"VP拒绝 | touch≈{touchPrice:0.########} nearest={refName}@{refPrice:0.########} dist={dist:0.########}pt({distTicksText2}) > {maxAllowedPts:0.########}pt({maxAllowedTicksText}) | zone={zone.ToShortText()}";
        }

        private static decimal GetTouchPriceLikeZoneEngine(TradingZone zone, IndicatorCandle candle)
        {
            return zone.Type switch
            {
                ZoneType.BullishOB or ZoneType.BullishFVG => Clamp(candle.Low, zone.Low, zone.High),
                ZoneType.BearishOB or ZoneType.BearishFVG => Clamp(candle.High, zone.Low, zone.High),
                _ => candle.Close
            };
        }

        private static (string RefName, decimal RefPrice, decimal Dist) GetNearestVpRef(HigherTimeframeAggregator.HtfCandle vp, decimal price)
        {
            var refs = new List<(string Name, decimal Price)>(4);

            if (vp.POC != 0m) refs.Add(("POC", vp.POC));
            if (vp.VAH != 0m) refs.Add(("VAH", vp.VAH));
            if (vp.VAL != 0m) refs.Add(("VAL", vp.VAL));
            if (vp.VWAP != 0m) refs.Add(("VWAP", vp.VWAP));

            if (refs.Count == 0)
                return ("-", 0m, 0m);

            var best = refs[0];
            var bestDist = Math.Abs(price - best.Price);

            for (var i = 1; i < refs.Count; i++)
            {
                var d = Math.Abs(price - refs[i].Price);
                if (d < bestDist)
                {
                    best = refs[i];
                    bestDist = d;
                }
            }

            return (best.Name, best.Price, bestDist);
        }

        private static decimal Clamp(decimal x, decimal min, decimal max)
        {
            if (min > max) (min, max) = (max, min);
            if (x < min) return min;
            if (x > max) return max;
            return x;
        }
    }
}