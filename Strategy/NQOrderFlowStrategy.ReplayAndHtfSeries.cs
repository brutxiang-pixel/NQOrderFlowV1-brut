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
        // HTF series storage
        // =========================
        private void UpsertHtfCandle(HigherTimeframeAggregator.HtfCandle? candle)
        {
            if (candle is null)
                return;

            if (_htfSeries.Count == 0)
            {
                _htfSeries.Add(candle);
                return;
            }

            var last = _htfSeries[^1];

            if (last.BucketTime.HasValue &&
                candle.BucketTime.HasValue &&
                last.BucketTime.Value == candle.BucketTime.Value)
            {
                _htfSeries[^1] = candle;
                return;
            }

            _htfSeries.Add(candle);

            if (_htfSeries.Count > 2000)
                _htfSeries.RemoveRange(0, _htfSeries.Count - 2000);
        }

        // =========================
        // Replay reset helper
        // =========================
        private static DateTime? TryGetBaseCandleTime(IndicatorCandle candle)
        {
            _baseTimeProp ??= candle.GetType().GetProperty("Time")
                          ?? candle.GetType().GetProperty("OpenTime");

            if (_baseTimeProp is null)
                return null;

            var value = _baseTimeProp.GetValue(candle);

            return value switch
            {
                DateTime dt => dt,
                _ => null
            };
        }
    }
}