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
        private void MaybeStartLiveSelfTest(int bar, IndicatorCandle cur)
        {
            if (!EnableLiveOrders || !EnableLiveSelfTest)
                return;

            if (!_selfTestRequested || _selfTestSubmitted)
                return;

            EnsureInstrumentRulesInitialized(bar, "MaybeStartLiveSelfTest");

            // 每隔 N bars 打一次状态，避免刷屏
            if (bar - _selfTestLastLogBar >= 20)
            {
                _selfTestLastLogBar = bar;
                AppendLog($"SELFTEST_WAIT bar={bar} state={State} " +
                          $"portfolio={(Portfolio is null ? "null" : Portfolio.ToString())} " +
                          $"security={(Security is null ? "null" : Security.ToString())} " +
                          $"connector={(Connector is null ? "null" : Connector.GetType().Name)}");
            }

            // 必要条件：Portfolio + Security（Connector 仅记录，不作为硬门槛）
            if (Portfolio is null || Security is null)
                return;

            // 有些环境下 Connector 永远 null，但 OpenOrderAsync 仍可能工作，所以不拦。
            StartLiveSelfTestForce(bar, cur);
            _selfTestSubmitted = true;
        }

        private void StartLiveSelfTestForce(int bar, IndicatorCandle cur)
        {
            EnsureLogInitialized();
            EnsureInstrumentRulesInitialized(bar, "StartLiveSelfTestForce");

            decimal refPrice = 0m;
            try
            {
                refPrice = SelfTestBuy ? (BestBid?.Price ?? 0m) : (BestAsk?.Price ?? 0m);
            }
            catch { }

            if (refPrice <= 0m)
                refPrice = cur.Close;

            if (refPrice <= 0m)
            {
                AppendLog("SELFTEST_SKIP reason=NoRefPrice");
                return;
            }

            var offset = Math.Max(0m, SelfTestOffsetPoints);
            var rawPrice = SelfTestBuy ? (refPrice - offset) : (refPrice + offset);

            // tick align (direction-safe)
            var price = AlignEntryLimitToTick(rawPrice, isLong: SelfTestBuy, bar: bar);

            _selfTestTag = $"{LiveCommentPrefix}|SELFTEST|{DateTime.Now:yyyyMMdd-HHmmss}";
            var rawQty = SelfTestQuantity;
            if (rawQty <= 0m) rawQty = GetMinQty(bar);

            // entry qty align (ceil)
            var qty = AlignQtyCeilForEntry(rawQty, bar, role: "SELFTEST_ENTRY");

            AppendLog($"SELFTEST_SUBMIT_REQUEST bar={bar} state={State} connector={(Connector is null ? "null" : Connector.GetType().Name)} " +
                      $"dir={(SelfTestBuy ? "Buy" : "Sell")} rawPrice={rawPrice:0.########} price={price:0.########} rawQty={rawQty:0.########} qty={qty:0.########}");

            var o = new Order
            {
                Portfolio = Portfolio,
                Security = Security,
                Type = OrderTypes.Limit,
                Direction = SelfTestBuy ? OrderDirections.Buy : OrderDirections.Sell,
                Price = price,
                QuantityToFill = qty,
                TimeInForce = LiveTimeInForce,
                Comment = $"{_selfTestTag}|{(SelfTestBuy ? "BUY" : "SELL")}|p={price:0.########}|bar={bar}",
                AutoCancel = false
            };

            EnqueueOrderAction("SelfTestOpen", async () =>
            {
                try
                {
                    AppendLog("SELFTEST_SUBMIT_START");
                    await OpenOrderAsync(o);
                    AppendLog($"SELFTEST_SUBMIT_RETURNED extId={o.ExtId}");
                }
                catch (Exception ex)
                {
                    AppendLog($"SELFTEST_SUBMIT_EXCEPTION err={ex.GetType().Name}:{ex.Message}");
                    return;
                }

                var ms = Math.Max(0, SelfTestCancelAfterMs);
                if (ms > 0)
                    await Task.Delay(ms);

                try
                {
                    await CancelOrderAsync(o);
                    AppendLog($"SELFTEST_CANCEL_SENT extId={o.ExtId}");
                }
                catch (Exception ex)
                {
                    AppendLog($"SELFTEST_CANCEL_EXCEPTION extId={o.ExtId} err={ex.GetType().Name}:{ex.Message}");
                }
            });
        }

        // 你原代码里还保留了这个旧版本 selftest（目前未被主流程调用）
        // 为保证“行为不变”，此处保留，但 P0 也做了 tick/qty 对齐以便未来切换时更安全。
        private void StartLiveSelfTest()
        {
            // 先检查上下文是否具备下单条件
            if (Connector is null || Portfolio is null || Security is null)
            {
                AppendLog($"SELFTEST_SKIP reason=MissingContext " +
                          $"connector={(Connector is null ? "null" : Connector.GetType().Name)} " +
                          $"portfolio={(Portfolio is null ? "null" : Portfolio.ToString())} " +
                          $"security={(Security is null ? "null" : Security.ToString())}");
                return;
            }

            EnsureInstrumentRulesInitialized(CurrentBar - 1, "StartLiveSelfTest(legacy)");

            // 当前参考价：优先用 BestBid/BestAsk，否则用最新 candle close
            decimal refPrice = 0m;
            try
            {
                if (SelfTestBuy)
                    refPrice = BestBid?.Price ?? 0m;
                else
                    refPrice = BestAsk?.Price ?? 0m;
            }
            catch { }

            if (refPrice <= 0m)
            {
                var bar = CurrentBar - 1;
                var c = bar >= 0 ? GetCandle(bar) : null;
                refPrice = c?.Close ?? 0m;
            }

            if (refPrice <= 0m)
            {
                AppendLog("SELFTEST_SKIP reason=NoRefPrice");
                return;
            }

            var offset = Math.Max(0m, SelfTestOffsetPoints);
            var rawPrice = SelfTestBuy ? (refPrice - offset) : (refPrice + offset);
            var price = AlignEntryLimitToTick(rawPrice, isLong: SelfTestBuy, bar: CurrentBar - 1);

            var rawQty = Math.Max(1, Contracts);
            var qty = AlignQtyCeilForEntry(rawQty, CurrentBar - 1, role: "SELFTEST_LEGACY");

            var o = new Order
            {
                Portfolio = Portfolio,
                Security = Security,
                Type = OrderTypes.Limit,
                Direction = SelfTestBuy ? OrderDirections.Buy : OrderDirections.Sell,
                Price = price,
                QuantityToFill = qty,
                TimeInForce = LiveTimeInForce,
                Comment = $"{LiveCommentPrefix}|SELFTEST|{DateTime.Now:HHmmss}|{(SelfTestBuy ? "BUY" : "SELL")}|p={price:0.########}",
                AutoCancel = false
            };

            _selfTestOrder = o;

            AppendLog($"SELFTEST_SUBMIT_REQUEST dir={o.Direction} price={o.Price:0.########} qty={o.QuantityToFill:0.########} " +
                      $"state={State} connector={Connector.GetType().Name}");

            EnqueueOrderAction("SelfTestOpen", async () =>
            {
                try
                {
                    await OpenOrderAsync(o);
                    AppendLog($"SELFTEST_SUBMIT_RETURNED extId={o.ExtId}");
                    _selfTestTag = $"{LiveCommentPrefix}|SELFTEST|{DateTime.Now:yyyyMMdd-HHmmss}";
                    o.Comment = $"{_selfTestTag}|{(SelfTestBuy ? "BUY" : "SELL")}|p={price:0.########}";
                }
                catch (Exception ex)
                {
                    AppendLog($"SELFTEST_SUBMIT_EXCEPTION err={ex.GetType().Name}:{ex.Message}");
                    return;
                }

                // 延迟撤单
                var ms = Math.Max(0, SelfTestCancelAfterMs);
                if (ms > 0)
                    await Task.Delay(ms);

                try
                {
                    await CancelOrderAsync(o);
                    AppendLog($"SELFTEST_CANCEL_SENT extId={o.ExtId}");
                }
                catch (Exception ex)
                {
                    AppendLog($"SELFTEST_CANCEL_EXCEPTION extId={o.ExtId} err={ex.GetType().Name}:{ex.Message}");
                }
            });
        }
    }
}