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
        // =====================================================================
        // Stage B callbacks (order/trade driven)
        // =====================================================================
        protected override void OnNewOrder(Order order)
        {
            base.OnNewOrder(order);

            if (!EnableLiveOrders)
                return;

            // attach live trading orders (do NOT gate by selfTestTag)
            TryAttachLiveOrder(order, "OnNewOrder");

            // selftest logs only
            if (!string.IsNullOrWhiteSpace(_selfTestTag) &&
                order?.Comment is not null &&
                order.Comment.Contains(_selfTestTag, StringComparison.Ordinal))
            {
                _selfTestExtId = order.ExtId;

                AppendLog($"SELFTEST_ORDER_NEW extId={order.ExtId} state={order.State} " +
                          $"type={order.Type} dir={order.Direction} price={order.Price:0.########} trig={order.TriggerPrice:0.########} " +
                          $"unfilled={order.Unfilled:0.########} qty={order.QuantityToFill:0.########} " +
                          $"portfolio={(order.Portfolio is null ? "null" : order.Portfolio.ToString())} " +
                          $"security={(order.Security is null ? "null" : order.Security.ToString())}");
            }
        }

        protected override void OnOrderChanged(Order order)
        {
            if (order is null)
                return;

            base.OnOrderChanged(order);

            if (!EnableLiveOrders)
                return;

            // attach live trading orders (do NOT gate by selfTestTag)
            TryAttachLiveOrder(order, "OnOrderChanged");

            // selftest de-dup logs
            if (!string.IsNullOrWhiteSpace(_selfTestTag) &&
                order.Comment is not null &&
                order.Comment.Contains(_selfTestTag, StringComparison.Ordinal))
            {
                if (_selfTestLastState.HasValue && _selfTestLastState.Value == order.State)
                    return;

                _selfTestLastState = order.State;
                _selfTestExtId = order.ExtId;

                AppendLog($"SELFTEST_ORDER_CHANGED extId={order.ExtId} state={order.State} " +
                          $"type={order.Type} dir={order.Direction} price={order.Price:0.########} trig={order.TriggerPrice:0.########} " +
                          $"unfilled={order.Unfilled:0.########} qty={order.QuantityToFill:0.########} oco={order.OCOGroup}");
            }

            if (_live is not null)
            {
                if (TryParseLiveComment(order.Comment, out var tradeId, out var role) &&
                    string.Equals(tradeId, _live.TradeId, StringComparison.OrdinalIgnoreCase))
                {
                    AppendLog($"LIVE_ORDER_CHANGED tradeId={_live.TradeId} role={role} extId={order.ExtId} state={order.State} type={order.Type} dir={order.Direction} price={order.Price:0.########} trig={order.TriggerPrice:0.########} unfilled={order.Unfilled:0.########} qty={order.QuantityToFill:0.########} oco={order.OCOGroup}");
                }
            }
        }

        protected override void OnNewMyTrade(MyTrade myTrade)
        {
            base.OnNewMyTrade(myTrade);

            if (!EnableLiveOrders)
                return;

            var mt = myTrade.Clone();
            _ = HandleMyTradeAsync(mt);
        }

        protected override void OnOrderRegisterFailed(Order order, string message)
        {
            if (order is null)
            {
                AppendLog($"LIVE_ORDER_REGISTER_FAILED order=null msg={message}");
                RaiseShowNotification($"Order register failed: {message}", "NQOrderFlowV1");
                return;
            }

            base.OnOrderRegisterFailed(order, message);

            if (!EnableLiveOrders)
                return;

            // selftest
            if (!string.IsNullOrWhiteSpace(_selfTestTag) &&
                order.Comment != null &&
                order.Comment.Contains(_selfTestTag, StringComparison.Ordinal))
            {
                AppendLog($"SELFTEST_REGISTER_FAILED extId={order.ExtId} msg={message} " +
                          $"type={order.Type} dir={order.Direction} price={order.Price:0.########} trig={order.TriggerPrice:0.########} qty={order.QuantityToFill:0.########}");
            }

            AppendLog($"LIVE_ORDER_REGISTER_FAILED extId={order.ExtId} type={order.Type} dir={order.Direction} " +
                      $"price={order.Price:0.########} trig={order.TriggerPrice:0.########} qty={order.QuantityToFill:0.########} msg={message}");

            RaiseShowNotification($"Order register failed: {message}", "NQOrderFlowV1");

            // if this was bracket failure and we are in position -> flatten (optional)
            if (LiveFlattenOnCriticalBracketFailure && _planState == PlanState.InPosition)
                EnqueueOrderAction("FlattenOnRegisterFailed", FlattenPositionMarketAsync);
        }

        protected override void OnOrderModifyFailed(Order order, Order newOrder, string message)
        {
            base.OnOrderModifyFailed(order, newOrder, message);

            if (!EnableLiveOrders)
                return;

            AppendLog($"LIVE_ORDER_MODIFY_FAILED extId={order.ExtId} msg={message} old(trig={order.TriggerPrice:0.########},price={order.Price:0.########},qty={order.QuantityToFill:0.########}) new(trig={newOrder.TriggerPrice:0.########},price={newOrder.Price:0.########},qty={newOrder.QuantityToFill:0.########})");
            RaiseShowNotification($"Order modify failed: {message}", "NQOrderFlowV1");
        }

        protected override void OnOrderCancelFailed(Order order, string message)
        {
            base.OnOrderCancelFailed(order, message);

            if (!EnableLiveOrders)
                return;

            // selftest
            if (!string.IsNullOrWhiteSpace(_selfTestTag) &&
                order?.Comment != null &&
                order.Comment.Contains(_selfTestTag, StringComparison.Ordinal))
            {
                AppendLog($"SELFTEST_CANCEL_FAILED extId={order.ExtId} msg={message}");
            }

            if (order is null)
            {
                AppendLog($"LIVE_ORDER_CANCEL_FAILED order=null msg={message}");
                RaiseShowNotification($"Order cancel failed: {message}", "NQOrderFlowV1");
                return;
            }

            AppendLog($"LIVE_ORDER_CANCEL_FAILED extId={order.ExtId} msg={message}");
        }

        protected override void OnStarted()
        {
            base.OnStarted();

            // 确保日志已初始化（否则启动时AppendLog可能没路径）
            EnsureLogInitialized();

            EnsureInstrumentRulesInitialized(-1, "OnStarted");
            LoadConfigFromFile();
            // 每轮新回测重置交易记录，防止旧数据残留
            _tradeHistory.Clear();
            _tradeWins = 0;
            _tradeLosses = 0;
            _netR = 0m;
            _netPnLDollar = 0m;

            AppendLog($"ON_STARTED state={State} " +
                      $"portfolio={(Portfolio is null ? "null" : Portfolio.ToString())} " +
                      $"security={(Security is null ? "null" : Security.ToString())} " +
                      $"connector={(Connector is null ? "null" : Connector.GetType().Name)}");

            if (EnableLiveOrders && EnableLiveSelfTest)
            {
                _selfTestRequested = true;
                _selfTestSubmitted = false;
                _selfTestTag = null;
                AppendLog("SELFTEST_REQUESTED (will submit when context ready)");
            }
        }

        private void TryAttachLiveOrder(Order order, string src)
        {
            if (_live is null)
                return;

            if (!TryParseLiveComment(order.Comment, out var tradeId, out var role))
                return;

            if (!string.Equals(tradeId, _live.TradeId, StringComparison.OrdinalIgnoreCase))
                return;

            if (string.Equals(role, "ENTRY", StringComparison.OrdinalIgnoreCase))
                _live.EntryOrder = order;
            else if (string.Equals(role, "SL", StringComparison.OrdinalIgnoreCase))
                _live.StopOrder = order;
            else if (string.Equals(role, "TP", StringComparison.OrdinalIgnoreCase))
                _live.TargetOrder = order;

            AppendLog($"LIVE_ATTACH_ORDER src={src} tradeId={tradeId} role={role} extId={order.ExtId} state={order.State}");
        }

        private bool TryParseLiveComment(string? comment, out string tradeId, out string role)
        {
            tradeId = "-";
            role = "-";

            if (string.IsNullOrWhiteSpace(comment))
                return false;

            var parts = comment.Split('|');
            if (parts.Length < 3)
                return false;

            if (!string.Equals(parts[0], LiveCommentPrefix, StringComparison.OrdinalIgnoreCase))
                return false;

            tradeId = parts[1];
            role = parts[2];
            return true;
        }

        private async Task HandleMyTradeAsync(MyTrade t)
        {
            await _orderActionLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_live is null)
                    return;

                var o = t.Order;
                if (o is null)
                    return;

                if (!TryParseLiveComment(o.Comment, out var tradeId, out var role))
                    return;

                if (!string.Equals(tradeId, _live.TradeId, StringComparison.OrdinalIgnoreCase))
                    return;

                AppendLog($"LIVE_MYTRADE tradeId={tradeId} role={role} price={t.Price:0.########} vol={t.Volume:0.########} time={t.Time:HH:mm:ss.fff} orderExtId={o.ExtId} state={o.State}");

                if (string.Equals(role, "ENTRY", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleLiveEntryFillAsync(t).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(role, "SL", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleLiveExitFillAsync(t, "SL Hit").ConfigureAwait(false);
                    return;
                }

                if (string.Equals(role, "TP", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleLiveExitFillAsync(t, "TP Hit").ConfigureAwait(false);
                    return;
                }
            }
            catch (Exception ex)
            {
                AppendLog($"LIVE_HANDLE_MYTRADE_ERR err={ex.GetType().Name}:{ex.Message}");
            }
            finally
            {
                _orderActionLock.Release();
            }
        }

        private async Task HandleLiveEntryFillAsync(MyTrade t)
        {
            if (_live is null)
                return;

            EnsureInstrumentRulesInitialized(_lastCalcBar, "HandleLiveEntryFillAsync");

            // accumulate filled
            _live.EntryFilledQty += Math.Max(0m, t.Volume);

            // create plan & bracket on first fill
            if (!_live.BracketSubmitted)
            {
                // compute stop/target using locked zone shadow
                var entry = _live.EntryLimit;
                var zone = _live.ZoneShadow;
                var isLong = _live.IsLong;
                var side = _live.Side;

                var usedShadow = true;

                if (!TrySelectStop(
                        bar: Math.Max(0, _lastCalcBar),
                        isLong: isLong,
                        entry: entry,
                        zone: zone,
                        usedShadow: usedShadow,
                        out var stop,
                        out var stopSource,
                        out var riskTicks,
                        out var riskPoints,
                        out var failReason,
                        out var failDetail))
                {
                    AppendLog($"LIVE_STOP_SELECT_FAIL tradeId={_live.TradeId} detail={failDetail}");
                    if (LiveFlattenOnCriticalBracketFailure)
                        await FlattenPositionMarketAsync().ConfigureAwait(false);
                    return;
                }

                // stop/target align

                stop = AlignStopToTick(stop, isLong, _lastCalcBar);
                var target = ComputeTargetFromRiskTicks(entry, isLong, riskTicks, RiskRewardR, _lastCalcBar);
                target = AlignTargetToTick(target, isLong, _lastCalcBar);

                // submit bracket (SL first, then TP)
                _live.OcoGroup = NewOcoGroup(_live.TradeId);
                _live.StopPrice = stop;
                _live.TargetPrice = target;

                // bracket qty: floor to step, never exceed filled qty
                var filled = _live.EntryFilledQty;
                var qty = AlignQtyFloorNotExceed(filled, filled, _lastCalcBar, role: "BRACKET_INIT");
                if (qty <= 0m)
                {
                    // 极端兜底：不对齐也要尽量保护（可能会被交易所拒单，但至少日志可见）
                    qty = filled;
                    AppendLog($"LIVE_BRACKET_QTY_FALLBACK tradeId={_live.TradeId} filled={filled:0.########} -> useRawQty={qty:0.########}");
                }

                _live.BracketQty = qty;

                AppendLog($"QTY_ALIGN bar={_lastCalcBar} role=BRACKET_INIT filled={filled:0.########} aligned={qty:0.########} step={GetQtyStep(_lastCalcBar):0.########} min={GetMinQty(_lastCalcBar):0.########}");

                var sl = new Order
                {
                    Portfolio = Portfolio,
                    Security = Security,
                    Type = OrderTypes.Stop,
                    Direction = isLong ? OrderDirections.Sell : OrderDirections.Buy,
                    TriggerPriceType = LiveTriggerPriceType,
                    TriggerPrice = stop,
                    QuantityToFill = qty,
                    TimeInForce = LiveTimeInForce,
                    OCOGroup = _live.OcoGroup,
                    Comment = $"{LiveCommentPrefix}|{_live.TradeId}|SL|stop={stop:0.########}|bar={_lastCalcBar}",
                    AutoCancel = false
                };

                var tp = new Order
                {
                    Portfolio = Portfolio,
                    Security = Security,
                    Type = OrderTypes.Limit,
                    Direction = isLong ? OrderDirections.Sell : OrderDirections.Buy,
                    Price = target,
                    QuantityToFill = qty,
                    TimeInForce = LiveTimeInForce,
                    OCOGroup = _live.OcoGroup,
                    Comment = $"{LiveCommentPrefix}|{_live.TradeId}|TP|tp={target:0.########}|bar={_lastCalcBar}",
                    AutoCancel = false
                };

                _live.StopOrder = sl;
                _live.TargetOrder = tp;

                try
                {
                    await OpenOrderAsync(sl).ConfigureAwait(false);
                    AppendLog($"LIVE_BRACKET_SL_SENT tradeId={_live.TradeId} oco={_live.OcoGroup} trig={stop:0.########} qty={qty:0.########}");

                    await OpenOrderAsync(tp).ConfigureAwait(false);
                    AppendLog($"LIVE_BRACKET_TP_SENT tradeId={_live.TradeId} oco={_live.OcoGroup} price={target:0.########} qty={qty:0.########}");

                    _live.BracketSubmitted = true;
                    _activePlan = new TradePlan
                    {
                        CreatedBar = _lastCalcBar,
                        Side = side,
                        Entry = entry,
                        Stop = stop,
                        Target = target,
                        InitialStop = stop,
                        InitialRiskPoints = riskPoints,
                        Zone = _live.ZoneKey,
                        OfScore = _live.OfScore,
                        OfText = _live.OfText,
                    };
                    _planState = PlanState.InPosition;

                    // after we are in position, clear confirm session
                    ResetConfirm(_lastCalcBar, "已成交入场(LIVE)");
                    _phase = ConfirmPhase.WaitZoneTouch;
                }
                catch (Exception ex)
                {
                    AppendLog($"LIVE_BRACKET_SEND_ERR tradeId={_live.TradeId} err={ex.GetType().Name}:{ex.Message}");
                    if (LiveFlattenOnCriticalBracketFailure)
                        await FlattenPositionMarketAsync().ConfigureAwait(false);
                }

                return;
            }

            // bracket already submitted -> resize qty to match total entry filled (floor, not exceed filled)
            if (_live.StopOrder is not null && _live.TargetOrder is not null)
            {
                var filled = _live.EntryFilledQty;
                var newQty = AlignQtyFloorNotExceed(filled, filled, _lastCalcBar, role: "BRACKET_RESIZE");
                if (newQty <= 0m)
                    return;

                // compare by step
                var step = GetQtyStep(_lastCalcBar);
                var eps = Math.Max(0.00000001m, step / 2m);

                if (newQty > _live.BracketQty + eps)
                {
                    var oldSl = _live.StopOrder;
                    var oldTp = _live.TargetOrder;

                    var newSl = oldSl.Clone();
                    newSl.QuantityToFill = newQty;

                    var newTp = oldTp.Clone();
                    newTp.QuantityToFill = newQty;

                    await ModifyOrderAsync(oldSl, newSl).ConfigureAwait(false);
                    await ModifyOrderAsync(oldTp, newTp).ConfigureAwait(false);

                    _live.BracketQty = newQty;

                    AppendLog($"LIVE_BRACKET_RESIZE tradeId={_live.TradeId} filled={filled:0.########} newQty={newQty:0.########}");
                }
            }
        }

        private async Task HandleLiveExitFillAsync(MyTrade t, string reason)
        {
            if (_live is null)
                return;

            if (_live.ExitCompleted)
                return;

            _live.ExitFilledQty += Math.Max(0m, t.Volume);

            // first exit trade -> cancel remaining entry and opposite leg (best-effort)
            if (_live.ExitFilledQty > 0m)
            {
                await TryCancelOrderSafeAsync(_live.EntryOrder, "ExitCancelEntry").ConfigureAwait(false);

                if (reason.StartsWith("SL", StringComparison.OrdinalIgnoreCase))
                    await TryCancelOrderSafeAsync(_live.TargetOrder, "ExitCancelTP").ConfigureAwait(false);
                else
                    await TryCancelOrderSafeAsync(_live.StopOrder, "ExitCancelSL").ConfigureAwait(false);
            }

            // treat exit complete when filled qty >= bracket qty
            if (_live.BracketQty > 0m && _live.ExitFilledQty + 0.0000001m >= _live.BracketQty)
            {
                _live.ExitCompleted = true;

                var exitBar = Math.Max(0, _lastCalcBar);
                var exitPrice = t.Price;

                if (_activePlan is null)
                {
                    AppendLog($"LIVE_EXIT_NO_PLAN tradeId={_live.TradeId} reason={reason} price={exitPrice:0.########}");
                }
                else
                {
                    ExitPlan(exitBar, reason, exitPrice);
                }

                AppendLog($"LIVE_TRADE_DONE tradeId={_live.TradeId} reason={reason} exit={exitPrice:0.########} exitFilled={_live.ExitFilledQty:0.########}/{_live.BracketQty:0.########}");

                // clear live refs
                _live = null;
            }
        }

        private async Task FlattenPositionMarketAsync()
        {
            try
            {
                EnsureInstrumentRulesInitialized(_lastCalcBar, "FlattenPositionMarketAsync");

                var pos = CurrentPosition;
                if (pos == 0m)
                    return;

                var rawQty = Math.Abs(pos);

                // flatten qty: floor and never exceed position qty
                var qty = AlignQtyFloorNotExceed(rawQty, rawQty, _lastCalcBar, role: "FLATTEN");
                if (qty <= 0m)
                    qty = rawQty;

                var dir = pos > 0m ? OrderDirections.Sell : OrderDirections.Buy;

                var o = new Order
                {
                    Portfolio = Portfolio,
                    Security = Security,
                    Type = OrderTypes.Market,
                    Direction = dir,
                    QuantityToFill = qty,
                    TimeInForce = LiveTimeInForce,
                    Comment = $"{LiveCommentPrefix}|FLATTEN|MKT|qty={qty:0.########}|t={DateTime.Now:HHmmss}"
                };

                await OpenOrderAsync(o).ConfigureAwait(false);
                AppendLog($"LIVE_FLATTEN_SENT dir={dir} qty={qty:0.########} rawPosQty={rawQty:0.########}");
            }
            catch (Exception ex)
            {
                AppendLog($"LIVE_FLATTEN_ERR err={ex.GetType().Name}:{ex.Message}");
            }
        }
    }
}
