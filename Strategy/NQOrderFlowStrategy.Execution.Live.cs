// Strategy/NQOrderFlowStrategy.Execution.Live.cs
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
using System.CodeDom;
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
        // Stage B LIVE: Limit execution
        // =====================================================================
        private void HandleLiveLimitExecution(int bar, IndicatorCandle cur, TradingZone zone, bool usedShadow, bool isLong, string side, int qLock, decimal halfTick)
        {
            EnsureInstrumentRulesInitialized(bar, "HandleLiveLimitExecution");

            var tick = GetTickSize(bar, "HandleLiveLimitExecution.tick");

            // place gate (before sending)
            if (_live is null && EnablePlaceDistanceGate)
            {
                var thTicks = GetMaxPlaceDistanceTicksIfSet();
                if (thTicks > 0)
                {
                    var distTicks = GetDistanceToRangeTicks(bar, cur.Close, zone.Low, zone.High);
                    if (distTicks > thTicks + 0.5m)
                    {
                        SetBlock(bar, TriggerBlockReason.EntryPlaceTooFarFromZone,
                            $"[LIVE]挂单距离闸门(Ticks)：closeDist={distTicks:0.##}t > {thTicks}t | close={cur.Close:0.########} zone={zone.Low:0.########}-{zone.High:0.########} | skip+reset");

                        AppendLog($"ENTRY_ORDER_SKIP bar={bar} reason=PlaceTooFar mode=LIVE closeDistTicks={distTicks:0.##} thTicks={thTicks} close={cur.Close:0.########} zone={zone.ToShortText()} -> reset confirm");
                        ResetConfirm(bar, "挂单距离过远(PlaceGateTicks)");
                        _phase = ConfirmPhase.WaitZoneTouch;
                        return;
                    }
                }
                else
                {
                    var th = Math.Max(0m, MaxPlaceDistanceFromZonePoints);
                    if (th > 0m)
                    {
                        var dist = GetDistanceToRangePoints(cur.Close, zone.Low, zone.High);
                        if (dist > th + halfTick)
                        {
                            SetBlock(bar, TriggerBlockReason.EntryPlaceTooFarFromZone,
                                $"[LIVE]挂单距离闸门(LEGACY)：closeDistToZone={dist:0.00}pt > {th:0.##}pt | close={cur.Close:0.########} zone={zone.Low:0.########}-{zone.High:0.########} | skip+reset");

                            AppendLog($"ENTRY_ORDER_SKIP bar={bar} reason=PlaceTooFar mode=LIVE closeDist={dist:0.00}pt th={th:0.##}pt close={cur.Close:0.########} zone={zone.ToShortText()} -> reset confirm");
                            ResetConfirm(bar, "挂单距离过远(PlaceGate)");
                            _phase = ConfirmPhase.WaitZoneTouch;
                            return;
                        }
                    }
                }
            }

            if (_live is null)
            {
                // ===== 1) 上下文预检（先做，失败则不创建_live）=====
                AppendLog($"LIVE_PRECHECK bar={bar} state={State} " +
                          $"portfolio={(Portfolio is null ? "null" : Portfolio.ToString())} " +
                          $"security={(Security is null ? "null" : Security.ToString())} " +
                          $"connector={(Connector is null ? "null" : Connector.GetType().Name)}");

                if (!IsLiveContextReady(out var why))
                {
                    AppendLog($"LIVE_SUBMIT_SKIP bar={bar} reason=ContextNotReady {why}");
                    SetBlock(bar, TriggerBlockReason.LiveOrderError, $"[LIVE]无法下单: {why}");
                    return;
                }

                // ===== 2) 计算价格/等待 =====
                var (limitRaw, anchorUsed, anchorText) = GetLimitEntryPriceDynamic(bar, zone, isLong);
                var limit = AlignEntryLimitToTick(limitRaw, isLong, bar);
                var effWait = GetEffectiveEntryMaxWaitBars(anchorUsed, qLock);

                // live anti-chase
                if (EnableAntiChaseFilter)
                {
                    var thTicks = GetMaxEntryDistanceTicksIfSet();
                    if (thTicks > 0)
                    {
                        var distTicks = GetDistanceToRangeTicks(bar, limit, zone.Low, zone.High);
                        if (distTicks > thTicks + 0.5m)
                        {
                            SetBlock(bar, TriggerBlockReason.EntryTooFarFromZone,
                                $"[LIVE]防追价(limit,ticks)：entryDist={distTicks:0.##}t > {thTicks}t | entry={limit:0.########} zone={zone.Low:0.########}-{zone.High:0.########} | skip+reset");

                            AppendLog($"ENTRY_ORDER_SKIP bar={bar} reason=AntiChase mode=LIVE entryDistTicks={distTicks:0.##} thTicks={thTicks} entry={limit:0.########} zone={zone.ToShortText()} -> reset confirm");
                            ResetConfirm(bar, "防追价(limit-live-ticks)");
                            _phase = ConfirmPhase.WaitZoneTouch;
                            return;
                        }
                    }
                    else
                    {
                        var maxDist = Math.Max(0m, MaxEntryDistanceFromZonePoints);
                        if (maxDist > 0m)
                        {
                            var dist = GetDistanceToRangePoints(limit, zone.Low, zone.High);
                            if (dist > maxDist + halfTick)
                            {
                                SetBlock(bar, TriggerBlockReason.EntryTooFarFromZone,
                                    $"[LIVE]防追价(limit,LEGACY)：entryDist={dist:0.00}pt > {maxDist:0.##}pt | entry={limit:0.########} zone={zone.Low:0.########}-{zone.High:0.########} | skip+reset");

                                AppendLog($"ENTRY_ORDER_SKIP bar={bar} reason=AntiChase mode=LIVE entryDist={dist:0.00}pt max={maxDist:0.##}pt entry={limit:0.########} zone={zone.ToShortText()} -> reset confirm");
                                ResetConfirm(bar, "防追价(limit-live)");
                                _phase = ConfirmPhase.WaitZoneTouch;
                                return;
                            }
                        }
                    }
                }

                // ===== 3) 数量对齐（ENTRY用Ceil，确保>=期望且满足min）=====
                if (!TrySelectStop(
                        bar,
                        isLong,
                        limit,
                        zone,
                        usedShadow,
                        out var preStop,
                        out _,
                        out _,
                        out var preRiskPoints,
                        out var preFailReason,
                        out var preFailDetail))
                {
                    switch (preFailReason)
                    {
                        case StopSelectFailReason.TooSmall:
                            SetBlock(bar, TriggerBlockReason.RiskTicksTooSmall, preFailDetail);
                            break;
                        case StopSelectFailReason.TooLarge:
                        case StopSelectFailReason.MixedOutOfRange:
                            SetBlock(bar, TriggerBlockReason.RiskTicksTooLarge, preFailDetail);
                            break;
                        default:
                            SetBlock(bar, TriggerBlockReason.RiskInvalid, preFailDetail);
                            break;
                    }

                    return;
                }

                preStop = AlignStopToTick(preStop, isLong, bar);
                preRiskPoints = Math.Abs(limit - preStop);
                if (ShouldBlockEntryByV128Filters(
                        bar,
                        side,
                        preRiskPoints,
                        _confirmZoneKey,
                        "LIVE_LIMIT",
                        _ofSoftenedForEntry,
                        _ofSoftenedMismatchBars))
                    return;

                var rawQty = GetRawEntryQuantity();
                var qty = AlignQtyCeilForEntry(rawQty, bar, role: "ENTRY");
                AppendLog($"QTY_ALIGN bar={bar} role=ENTRY raw={rawQty:0.########} aligned={qty:0.########} step={GetQtyStep(bar):0.########} min={GetMinQty(bar):0.########}");

                // ===== 4) 创建订单对象 =====
                var tradeId = CreateLiveTradeId(bar, cur);
                var zoneShadow = CloneZone(zone);

                var entryOrder = new Order
                {
                    Portfolio = Portfolio,
                    Security = Security,
                    Type = OrderTypes.Limit,
                    Direction = isLong ? OrderDirections.Buy : OrderDirections.Sell,
                    Price = limit,
                    QuantityToFill = qty,
                    TimeInForce = LiveTimeInForce,
                    Comment = $"{LiveCommentPrefix}|{tradeId}|ENTRY|{FormatZoneKeyCN(_confirmZoneKey)}|anchor={anchorText}|bar={bar}",
                    AutoCancel = false
                };

                // ===== 5) 只有准备提交时才创建_live（避免ghost pending）=====
                _live = new LiveOrderState
                {
                    TradeId = tradeId,
                    Side = side,
                    IsLong = isLong,
                    ZoneKey = _confirmZoneKey!,
                    ZoneShadow = zoneShadow,
                    CreatedBar = bar,
                    EffectiveMaxWaitBars = effWait,
                    RunawayBars = 0,
                    EntryLimit = limit,
                    AnchorText = anchorText,
                    AnchorUsed = anchorUsed,
                    OfScore = _ofArmedScore,
                    OfText = _ofArmedText,
                    OfSoftened = _ofSoftenedForEntry,
                    OfMismatchBars = _ofSoftenedMismatchBars,
                    LockedQScore = qLock,
                    EntryOrder = entryOrder
                };

                AppendLog($"ENTRY_ORDER_CREATE bar={bar} side={side} limit={limit:0.########} anchor={anchorText} effWait={effWait} of={_ofArmedScore}/4 qLock={(qLock < 0 ? "-" : qLock.ToString())}/10 zone={FormatZoneKeyCN(_confirmZoneKey)} mode=LIVE tradeId={tradeId}");
                AppendLog($"LIVE_SUBMIT_REQUEST bar={bar} tradeId={tradeId} type=Limit dir={(isLong ? "Buy" : "Sell")} price={limit:0.########} qty={qty:0.########}");

                // ===== 6) 提交（由EnqueueOrderAction串行化执行）=====
                EnqueueOrderAction("OpenEntry", async () =>
                {
                    try
                    {
                        AppendLog($"LIVE_SUBMIT_START tradeId={tradeId}");
                        await OpenOrderAsync(entryOrder);
                        AppendLog($"LIVE_SUBMIT_RETURNED tradeId={tradeId} extId={entryOrder.ExtId} (returned != accepted)");
                    }
                    catch (Exception ex)
                    {
                        AppendLog($"LIVE_SUBMIT_EXCEPTION tradeId={tradeId} err={ex.GetType().Name}:{ex.Message}");
                    }
                });

                SetBlock(bar, TriggerBlockReason.LiveOrderPending,
                    $"[LIVE]已请求提交EntryLimit @{limit:0.########}({anchorText}) tradeId={tradeId} waited=0/{effWait}");

                return;
            }

            // manage pending lifecycle (only if no fills yet)
            if (_live.EntryFilledQty <= 0m)
            {
                var waited = Math.Max(0, bar - _live.CreatedBar);
                var maxWait = Math.Max(0, _live.EffectiveMaxWaitBars);

                if (maxWait > 0 && waited > maxWait)
                {
                    AppendLog($"ENTRY_ORDER_CANCEL bar={bar} reason=Expired mode=LIVE waited={waited} maxWait={maxWait} limit={_live.EntryLimit:0.########} side={_live.Side} tradeId={_live.TradeId} zone={FormatZoneKeyCN(_live.ZoneKey)}");
                    CancelLiveAndResetConfirm(bar, "限价入场超时(LIVE)");
                    _phase = ConfirmPhase.WaitZoneTouch;
                    SetBlock(bar, TriggerBlockReason.EntryOrderExpired, $"[LIVE]限价单超时 waited={waited} > {maxWait} -> cancel+reset");
                    return;
                }

                if (EnableEarlyCancelRunaway)
                {
                    var minWaitBeforeCancel = Math.Max(0, RunawayCancelMinWaitBars);
                    if (waited >= minWaitBeforeCancel)
                    {
                        var runawayDistPts = GetRunawayDistanceInBiasDirectionPoints(cur.Close, zone, isLong);
                        var needBars = Math.Max(1, RunawayCancelConsecutiveBars);

                        var thTicks = GetRunawayCancelDistanceTicksIfSet();
                        if (thTicks > 0)
                        {
                            var runawayDistTicks = tick > 0m ? runawayDistPts / tick : 0m;

                            if (runawayDistTicks > thTicks + 0.5m)
                                _live.RunawayBars++;
                            else
                                _live.RunawayBars = 0;

                            if (_live.RunawayBars >= needBars)
                            {
                                AppendLog($"ENTRY_ORDER_CANCEL bar={bar} reason=Runaway mode=LIVE runawayDistTicks={runawayDistTicks:0.##} thTicks={thTicks} nBars={_live.RunawayBars} waited={waited}/{_live.EffectiveMaxWaitBars} limit={_live.EntryLimit:0.########} side={_live.Side} tradeId={_live.TradeId} zone={FormatZoneKeyCN(_live.ZoneKey)} -> reset confirm");
                                CancelLiveAndResetConfirm(bar, "挂单提前取消(runaway-live-ticks)");
                                _phase = ConfirmPhase.WaitZoneTouch;
                                SetBlock(bar, TriggerBlockReason.EntryOrderEarlyCanceled, $"[LIVE]提前取消(runaway,ticks) dist>{thTicks}t nBars={_live.RunawayBars}");
                                return;
                            }
                        }
                        else
                        {
                            var runawayTh = Math.Max(0m, RunawayCancelDistancePoints);

                            if (runawayDistPts > runawayTh + halfTick)
                                _live.RunawayBars++;
                            else
                                _live.RunawayBars = 0;

                            if (_live.RunawayBars >= needBars)
                            {
                                AppendLog($"ENTRY_ORDER_CANCEL bar={bar} reason=Runaway mode=LIVE runawayDist={runawayDistPts:0.00}pt th={runawayTh:0.##}pt nBars={_live.RunawayBars} waited={waited}/{_live.EffectiveMaxWaitBars} limit={_live.EntryLimit:0.########} side={_live.Side} tradeId={_live.TradeId} zone={FormatZoneKeyCN(_live.ZoneKey)} -> reset confirm");
                                CancelLiveAndResetConfirm(bar, "挂单提前取消(runaway-live)");
                                _phase = ConfirmPhase.WaitZoneTouch;
                                SetBlock(bar, TriggerBlockReason.EntryOrderEarlyCanceled, $"[LIVE]提前取消(runaway) dist>{runawayTh:0.##}pt nBars={_live.RunawayBars}");
                                return;
                            }
                        }
                    }
                }

                var distToZone = GetDistanceToRangePoints(cur.Close, zone.Low, zone.High);
                SetBlock(bar, TriggerBlockReason.LiveOrderPending,
                    $"[LIVE]等待成交 EntryLimit={_live.EntryLimit:0.########}({_live.AnchorText}) waited={waited}/{maxWait} closeDistToZone≈{distToZone:0.00}pt tradeId={_live.TradeId}");
                return;
            }

            // partially filled already -> in position, UpdateTradePlanV0 will be blocked by planState soon.
            SetBlock(bar, TriggerBlockReason.LiveOrderPending,
                $"[LIVE]已部分成交 qty={_live.EntryFilledQty:0.########} tradeId={_live.TradeId} -> 等待 bracket/退出回报");
        }

        private void HandleLiveMarketExecution(int bar, IndicatorCandle cur, TradingZone zone, bool usedShadow, bool isLong, string side, int qLock, decimal halfTick)
        {
            EnsureInstrumentRulesInitialized(bar, "HandleLiveMarketExecution");

            // minimal support: fire Market once (NOT recommended)
            if (_live is not null)
            {
                SetBlock(bar, TriggerBlockReason.LiveOrderPending, $"[LIVE]已存在live会话 tradeId={_live.TradeId}");
                return;
            }

            // qty align for market entry (ENTRY用Ceil)
            var rawQty = GetRawEntryQuantity();
            var qty = AlignQtyCeilForEntry(rawQty, bar, role: "ENTRY_MKT");
            AppendLog($"QTY_ALIGN bar={bar} role=ENTRY_MKT raw={rawQty:0.########} aligned={qty:0.########} step={GetQtyStep(bar):0.########} min={GetMinQty(bar):0.########}");

            if (EnableAntiChaseFilter)
            {
                var thTicks = GetMaxEntryDistanceTicksIfSet();
                if (thTicks > 0)
                {
                    var entryRef = RoundToTick(cur.Close, bar);
                    var distTicks = GetDistanceToRangeTicks(bar, entryRef, zone.Low, zone.High);

                    if (distTicks > thTicks + 0.5m)
                    {
                        SetBlock(bar, TriggerBlockReason.EntryTooFarFromZone,
                            $"[LIVE]防追价(mkt,ticks)：entryDist={distTicks:0.##}t > {thTicks}t | entry={entryRef:0.########} zone={zone.Low:0.########}-{zone.High:0.########} -> skip+reset");
                        ResetConfirm(bar, "防追价(mkt-live-ticks)");
                        _phase = ConfirmPhase.WaitZoneTouch;
                        return;
                    }
                }
                else
                {
                    var maxDist = Math.Max(0m, MaxEntryDistanceFromZonePoints);
                    if (maxDist > 0m)
                    {
                        var entryRef = RoundToTick(cur.Close, bar);
                        var dist = GetDistanceToRangePoints(entryRef, zone.Low, zone.High);
                        if (dist > maxDist + halfTick)
                        {
                            SetBlock(bar, TriggerBlockReason.EntryTooFarFromZone,
                                $"[LIVE]防追价(mkt,LEGACY)：entryDist={dist:0.00}pt > {maxDist:0.##}pt | entry={entryRef:0.########} zone={zone.Low:0.########}-{zone.High:0.########} -> skip+reset");
                            ResetConfirm(bar, "防追价(mkt-live)");
                            _phase = ConfirmPhase.WaitZoneTouch;
                            return;
                        }
                    }
                }
            }

            var preEntry = RoundToTick(cur.Close, bar);
            if (!TrySelectStop(
                    bar,
                    isLong,
                    preEntry,
                    zone,
                    usedShadow,
                    out var preStop,
                    out _,
                    out _,
                    out var preRiskPoints,
                    out var preFailReason,
                    out var preFailDetail))
            {
                switch (preFailReason)
                {
                    case StopSelectFailReason.TooSmall:
                        SetBlock(bar, TriggerBlockReason.RiskTicksTooSmall, preFailDetail);
                        break;
                    case StopSelectFailReason.TooLarge:
                    case StopSelectFailReason.MixedOutOfRange:
                        SetBlock(bar, TriggerBlockReason.RiskTicksTooLarge, preFailDetail);
                        break;
                    default:
                        SetBlock(bar, TriggerBlockReason.RiskInvalid, preFailDetail);
                        break;
                }

                return;
            }

            preStop = AlignStopToTick(preStop, isLong, bar);
            preRiskPoints = Math.Abs(preEntry - preStop);
            if (ShouldBlockEntryByV128Filters(
                    bar,
                    side,
                    preRiskPoints,
                    _confirmZoneKey,
                    "LIVE_MARKET",
                    _ofSoftenedForEntry,
                    _ofSoftenedMismatchBars))
                return;

            var tradeId = CreateLiveTradeId(bar, cur);
            var zoneShadow = CloneZone(zone);

            var entryOrder = new Order
            {
                Portfolio = Portfolio,
                Security = Security,
                Type = OrderTypes.Market,
                Direction = isLong ? OrderDirections.Buy : OrderDirections.Sell,
                QuantityToFill = qty,
                TimeInForce = LiveTimeInForce,
                Comment = $"{LiveCommentPrefix}|{tradeId}|ENTRY|{FormatZoneKeyCN(_confirmZoneKey)}|MKT|bar={bar}",
                AutoCancel = false
            };

            _live = new LiveOrderState
            {
                TradeId = tradeId,
                Side = side,
                IsLong = isLong,
                ZoneKey = _confirmZoneKey!,
                ZoneShadow = zoneShadow,
                CreatedBar = bar,
                EffectiveMaxWaitBars = 0,
                EntryLimit = RoundToTick(cur.Close, bar),
                AnchorText = "Market",
                AnchorUsed = ZoneEntryAnchor.Mid,
                OfScore = _ofArmedScore,
                OfText = _ofArmedText,
                OfSoftened = _ofSoftenedForEntry,
                OfMismatchBars = _ofSoftenedMismatchBars,
                LockedQScore = qLock,
                EntryOrder = entryOrder
            };

            AppendLog($"ENTRY_ORDER_CREATE bar={bar} side={side} type=Market qty={qty:0.########} mode=LIVE tradeId={tradeId} zone={FormatZoneKeyCN(_confirmZoneKey)}");

                EnqueueOrderAction("OpenMarketEntry", async () =>
                {
                    await OpenOrderAsync(entryOrder);
                });
        }

        private void CancelLiveAndResetConfirm(int bar, string reason)
        {
            if (_live is null)
            {
                ResetConfirm(bar, reason);
                return;
            }


            var live = _live;
            _live = null;

            EnqueueOrderAction("CancelLive", async () =>
            {
                await TryCancelOrderSafeAsync(live.EntryOrder, $"CancelLive({reason})");
                await TryCancelOrderSafeAsync(live.StopOrder, $"CancelLive({reason})");
                await TryCancelOrderSafeAsync(live.TargetOrder, $"CancelLive({reason})");
            });

            ResetConfirm(bar, reason);
        }

        private string CreateLiveTradeId(int bar, IndicatorCandle cur)
        {
            var t = TryGetBaseCandleTime(cur);
            var ts = t.HasValue ? t.Value.ToString("yyyyMMdd-HHmm") : "NA";
            return $"{ts}-bar{bar}";
        }

        private void EnqueueOrderAction(string tag, Func<Task> action)
        {
            _ = ExecuteOrderActionAsync(tag, action);
        }

        private async Task ExecuteOrderActionAsync(string tag, Func<Task> action)
        {
            await _orderActionLock.WaitAsync(); // 不要 ConfigureAwait(false)
            try
            {
                await action();                 // 不要 ConfigureAwait(false)
            }
            catch (Exception ex)
            {
                AppendLog($"LIVE_ACTION_FAIL tag={tag} err={ex.GetType().Name}:{ex.Message}");
            }
            finally
            {
                _orderActionLock.Release();
            }
        }

        private async Task TryCancelOrderSafeAsync(Order? order, string reason)
        {
            if (order is null)
                return;

            try
            {
                await CancelOrderAsync(order);
                AppendLog($"LIVE_CANCEL_OK reason={reason} extId={order.ExtId} type={order.Type} dir={order.Direction} price={order.Price:0.########} trig={order.TriggerPrice:0.########} qty={order.QuantityToFill:0.########}");
            }
            catch (Exception ex)
            {
                AppendLog($"LIVE_CANCEL_ERR reason={reason} extId={order.ExtId} err={ex.GetType().Name}:{ex.Message}");
            }
        }
    }
}
