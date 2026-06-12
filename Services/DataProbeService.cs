using ATAS.Indicators;
using NQOrderFlowV1.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NQOrderFlowV1.Services
{
    public sealed class DataProbeService
    {
        private readonly Func<int, IndicatorCandle?> _getCandle;

        public DataProbeService(Func<int, IndicatorCandle?> getCandle)
        {
            _getCandle = getCandle;
        }

        public CandleSnapshot? Build(int bar)
        {
            var candle = _getCandle(bar);
            if (candle is null)
                return null;

            var vah = candle.ValueArea?.ValueAreaHigh ?? 0m;
            var val = candle.ValueArea?.ValueAreaLow ?? 0m;
            var poc = candle.MaxVolumePriceInfo?.Price ?? 0m;

            var topLevels = candle.GetAllPriceLevels()
                .OrderByDescending(x => x.Volume)
                .Take(3)
                .Select(x => new PriceLevelSnapshot
                {
                    Price = x.Price,
                    Bid = x.Bid,
                    Ask = x.Ask,
                    Volume = x.Volume,
                    Ticks = x.Ticks,
                    Between = x.Between,
                    Time = x.Time
                })
                .ToList();

            return new CandleSnapshot
            {
                Bar = bar,
                Open = candle.Open,
                High = candle.High,
                Low = candle.Low,
                Close = candle.Close,
                Volume = candle.Volume,
                Delta = candle.Delta,
                POC = poc,
                VAH = vah,
                VAL = val,
                TopLevels = topLevels
            };
        }
    }
}