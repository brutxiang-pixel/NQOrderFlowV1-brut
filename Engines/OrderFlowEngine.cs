using ATAS.Indicators;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NQOrderFlowV1.Engines
{
    [Flags]
    public enum OrderFlowCondition
    {
        None = 0,

        DeltaDivergence = 1 << 0,
        Absorption = 1 << 1,
        ExtremeDelta = 1 << 2,
        Imbalance = 1 << 3
    }

    public sealed class OrderFlowResult
    {
        public int Bar { get; init; }

        public OrderFlowCondition Conditions { get; init; } = OrderFlowCondition.None;

        public int Score
        {
            get
            {
                var c = Conditions;
                var score = 0;
                if (c.HasFlag(OrderFlowCondition.DeltaDivergence)) score++;
                if (c.HasFlag(OrderFlowCondition.Absorption)) score++;
                if (c.HasFlag(OrderFlowCondition.ExtremeDelta)) score++;
                if (c.HasFlag(OrderFlowCondition.Imbalance)) score++;
                return score;
            }
        }

        public string Text { get; init; } = "-";
    }

    /// <summary>
    /// 订单流触发层（V1：只做四选二的条件识别与评分，不下单）
    ///
    /// 四个条件（简化版，可跑通后再逐步增强）：
    /// 1) Delta Divergence（简化：与前一根对比）
    /// 2) Absorption（简化：最大单边Bid/Ask占比 + 价格未延续）
    /// 3) Extreme Delta（简化：|Delta| 占比足够大，且K线反向/不延续）
    /// 4) Volume Imbalance（价位级 Ask/Bid >= ratio，且出现的价位数 >= N）
    /// </summary>
    public sealed class OrderFlowEngine
    {
        private readonly Func<int, IndicatorCandle?> _getCandle;
        private readonly decimal _tickSize;

        // —— 可按后续经验调参 ——
        private readonly decimal _deltaExtremeRatio;      // |Delta| / Volume >= ratio
        private readonly decimal _absorptionSideRatio;    // MaxBid(or MaxAsk) / Volume >= ratio

        private readonly decimal _imbalanceRatio;         // Ask >= Bid * ratio or Bid >= Ask * ratio
        private readonly int _minImbalanceLevels;         // 满足失衡的价位数 >= N
        private readonly decimal _minLevelVolume;         // 单价位 Volume >= X 才计入（过滤噪音）

        public OrderFlowEngine(
            Func<int, IndicatorCandle?> getCandle,
            decimal tickSize = 0.25m,
            decimal deltaExtremeRatio = 0.30m,
            decimal absorptionSideRatio = 0.20m,
            decimal imbalanceRatio = 4m,
            int minImbalanceLevels = 2,
            decimal minLevelVolume = 20m)
        {
            _getCandle = getCandle;
            _tickSize = tickSize <= 0 ? 0.25m : tickSize;

            _deltaExtremeRatio = Clamp01(deltaExtremeRatio);
            _absorptionSideRatio = Clamp01(absorptionSideRatio);

            _imbalanceRatio = Math.Max(2m, imbalanceRatio);
            _minImbalanceLevels = Math.Max(1, minImbalanceLevels);
            _minLevelVolume = Math.Max(0m, minLevelVolume);
        }

        public OrderFlowResult Evaluate(int bar)
        {
            var cur = _getCandle(bar);
            if (cur is null)
                return new OrderFlowResult { Bar = bar, Conditions = OrderFlowCondition.None, Text = "-" };

            var prev = bar > 0 ? _getCandle(bar - 1) : null;

            var conditions = OrderFlowCondition.None;
            var notes = new List<string>(4);

            // 1) Delta Divergence（简化：与前一根对比）
            if (prev is not null)
            {
                if (IsBullishDeltaDivergence(cur, prev))
                {
                    conditions |= OrderFlowCondition.DeltaDivergence;
                    notes.Add("ΔDiv(Bull)");
                }
                else if (IsBearishDeltaDivergence(cur, prev))
                {
                    conditions |= OrderFlowCondition.DeltaDivergence;
                    notes.Add("ΔDiv(Bear)");
                }
            }

            // 2) Absorption（简化）
            if (TryDetectAbsorption(cur, out var absorptionNote))
            {
                conditions |= OrderFlowCondition.Absorption;
                notes.Add(absorptionNote);
            }

            // 3) Extreme Delta（简化）
            if (TryDetectExtremeDelta(cur, out var extremeNote))
            {
                conditions |= OrderFlowCondition.ExtremeDelta;
                notes.Add(extremeNote);
            }

            // 4) Volume Imbalance（价位级）
            if (TryDetectImbalance(cur, out var imbalanceNote))
            {
                conditions |= OrderFlowCondition.Imbalance;
                notes.Add(imbalanceNote);
            }

            var text = notes.Count == 0 ? "-" : string.Join(" | ", notes);

            return new OrderFlowResult
            {
                Bar = bar,
                Conditions = conditions,
                Text = text
            };
        }

        private bool IsBullishDeltaDivergence(IndicatorCandle cur, IndicatorCandle prev)
        {
            // 价格创新低，但 delta 不更差（更高/更不负）=> bullish divergence
            return cur.Low < prev.Low - _tickSize / 2m
                && cur.Delta > prev.Delta;
        }

        private bool IsBearishDeltaDivergence(IndicatorCandle cur, IndicatorCandle prev)
        {
            // 价格创新高，但 delta 不更强（更低）=> bearish divergence
            return cur.High > prev.High + _tickSize / 2m
                && cur.Delta < prev.Delta;
        }

        private bool TryDetectExtremeDelta(IndicatorCandle cur, out string note)
        {
            note = string.Empty;

            if (cur.Volume <= 0)
                return false;

            var ratio = Math.Abs(cur.Delta) / cur.Volume;
            if (ratio < _deltaExtremeRatio)
                return false;

            // 价格“未延续”简化：大Δ但实体方向相反/或收盘靠回中间
            var range = cur.High - cur.Low;
            if (range <= 0)
                return false;

            var body = Math.Abs(cur.Close - cur.Open);
            var bodyRatio = body / range;

            // 大 delta + 但实体不大（吸收/对敲常见形态）
            if (bodyRatio <= 0.45m)
            {
                note = $"ExtremeΔ({ratio:0.00})";
                return true;
            }

            // 或者 delta 与K线方向“冲突”
            var isBullCandle = cur.Close > cur.Open;
            var isBearCandle = cur.Close < cur.Open;

            if (cur.Delta < 0 && isBullCandle)
            {
                note = $"ExtremeΔ(BuyAbs {ratio:0.00})";
                return true;
            }

            if (cur.Delta > 0 && isBearCandle)
            {
                note = $"ExtremeΔ(SellAbs {ratio:0.00})";
                return true;
            }

            return false;
        }

        private bool TryDetectAbsorption(IndicatorCandle cur, out string note)
        {
            note = string.Empty;

            if (cur.Volume <= 0)
                return false;

            // 价位级统计：找最大 Bid / 最大 Ask
            decimal maxBid = 0m;
            decimal maxAsk = 0m;

            foreach (var lvl in cur.GetAllPriceLevels())
            {
                if (_minLevelVolume > 0m && lvl.Volume < _minLevelVolume)
                    continue;

                if (lvl.Bid > maxBid) maxBid = lvl.Bid;
                if (lvl.Ask > maxAsk) maxAsk = lvl.Ask;
            }

            var bidRatio = maxBid / cur.Volume;
            var askRatio = maxAsk / cur.Volume;

            // Bullish absorption：卖压（Bid很大 / Delta偏负）但K线不再下破，甚至收绿
            if (bidRatio >= _absorptionSideRatio &&
                cur.Delta <= 0 &&
                cur.Close >= cur.Open)
            {
                note = $"Absorb(Bid {bidRatio:0.00})";
                return true;
            }

            // Bearish absorption：买压（Ask很大 / Delta偏正）但K线不再上破，甚至收红
            if (askRatio >= _absorptionSideRatio &&
                cur.Delta >= 0 &&
                cur.Close <= cur.Open)
            {
                note = $"Absorb(Ask {askRatio:0.00})";
                return true;
            }

            return false;
        }

        private bool TryDetectImbalance(IndicatorCandle cur, out string note)
        {
            note = string.Empty;

            var buyImb = 0;
            var sellImb = 0;

            foreach (var lvl in cur.GetAllPriceLevels())
            {
                if (_minLevelVolume > 0m && lvl.Volume < _minLevelVolume)
                    continue;

                // 防止除0
                var bid = Math.Max(0m, lvl.Bid);
                var ask = Math.Max(0m, lvl.Ask);

                if (bid == 0m && ask == 0m)
                    continue;

                if (ask >= Math.Max(1m, bid) * _imbalanceRatio)
                    buyImb++;

                if (bid >= Math.Max(1m, ask) * _imbalanceRatio)
                    sellImb++;
            }

            if (buyImb >= _minImbalanceLevels)
            {
                note = $"Imb(Buy x{buyImb})";
                return true;
            }

            if (sellImb >= _minImbalanceLevels)
            {
                note = $"Imb(Sell x{sellImb})";
                return true;
            }

            return false;
        }

        private static decimal Clamp01(decimal x)
        {
            if (x < 0m) return 0m;
            if (x > 1m) return 1m;
            return x;
        }
    }
}