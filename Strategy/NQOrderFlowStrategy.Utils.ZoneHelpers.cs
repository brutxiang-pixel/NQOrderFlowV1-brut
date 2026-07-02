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
        private static bool TryFindZone(IReadOnlyList<TradingZone> zones, ZoneKey key, out TradingZone found)
        {
            foreach (var z in zones)
            {
                if (z.Type == key.Type &&
                    z.StartBar == key.StartBar &&
                    z.CreatedBar == key.CreatedBar &&
                    z.Low == key.Low &&
                    z.High == key.High)
                {
                    found = z;
                    return true;
                }
            }

            found = null!;
            return false;
        }

        private bool TryGetZoneByKeyWithShadow(IReadOnlyList<TradingZone> activeZones, ZoneKey key, out TradingZone zone, out bool usedShadow)
        {
            usedShadow = false;

            if (TryFindZone(activeZones, key, out zone))
            {
                _confirmZoneShadow = CloneZone(zone);
                _confirmZoneUsingShadow = false;
                _loggedShadowUseOnce = false;
                return true;
            }

            if (_confirmZoneShadow is not null && ZoneMatchesKey(_confirmZoneShadow, key))
            {
                zone = _confirmZoneShadow;
                usedShadow = true;

                _confirmZoneUsingShadow = true;

                if (!_loggedShadowUseOnce)
                {
                    _loggedShadowUseOnce = true;
                    AppendLog($"LOCKED_ZONE_NOT_IN_ACTIVE_VIEW -> using shadow: {zone.ToShortText()}");
                }

                return true;
            }

            zone = null!;
            return false;
        }

        private static TradingZone CloneZone(TradingZone z)
        {
            return new TradingZone
            {
                Type = z.Type,
                StartBar = z.StartBar,
                CreatedBar = z.CreatedBar,
                Low = z.Low,
                High = z.High,

                IsTouched = z.IsTouched,
                IsMitigated = z.IsMitigated,
                IsVpRejected = z.IsVpRejected,
                IsInvalidated = z.IsInvalidated,

                Text = z.Text
            };
        }

        private static bool ZoneMatchesKey(TradingZone z, ZoneKey k)
        {
            return z.Type == k.Type &&
                   z.StartBar == k.StartBar &&
                   z.CreatedBar == k.CreatedBar &&
                   z.Low == k.Low &&
                   z.High == k.High;
        }

        private static decimal GetDistanceToRangePoints(decimal price, decimal low, decimal high)
        {
            if (low > high) (low, high) = (high, low);
            if (price < low) return low - price;
            if (price > high) return price - high;
            return 0m;
        }

        private decimal GetDistanceToRangeTicks(int bar, decimal price, decimal low, decimal high)
        {
            var distPts = GetDistanceToRangePoints(price, low, high);
            var tick = GetTickSize(bar, "GetDistanceToRangeTicks");
            if (tick <= 0m)
                return 0m;

            return distPts / tick;
        }
    }
}
