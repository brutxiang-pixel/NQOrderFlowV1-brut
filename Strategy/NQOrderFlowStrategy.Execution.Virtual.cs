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
        public int BreakEvenPlusRRatio { get; private set; }

        // =========================
        // TradePlan / Execution
        // =========================
        private void UpdateTradePlanV0(int bar, IndicatorCandle cur, IReadOnlyList<TradingZone> active)
        {
            EnsureInstrumentRulesInitialized(bar, "UpdateTradePlanV0");

            var halfTick = GetHalfTick(bar, "UpdateTradePlanV0");
            var tick = GetTickSize(bar, "UpdateTradePlanV0.tick");

            // ===== 退出（仅虚拟模式使用 candle 模拟；实盘由 OnNewMyTrade 驱动）=====
            if (!EnableLiveOrders)
            {
                if (_planState == PlanState.InPosition && _activePlan is not null && _activePlan.ExitBar is null)
                {
                    var plan = _activePlan;

                    if (plan.Side == "LONG")
                    {
                        if (cur.Low <= plan.Stop)
                            ExitPlan(bar, "SL Hit", exitPrice: plan.Stop, exitTime: cur.Time);
                        else if (cur.High >= plan.Target)
                            ExitPlan(bar, "TP Hit", exitPrice: plan.Target, exitTime: cur.Time);
                        else
                            MaybeMoveBreakEven(bar, cur, plan);
                    MaybeTrailStop(bar, cur, plan);
                    }
                    else
                    {
                        if (cur.High >= plan.Stop)
                            ExitPlan(bar, "SL Hit", exitPrice: plan.Stop, exitTime: cur.Time);
                        else if (cur.Low <= plan.Target)
                            ExitPlan(bar, "TP Hit", exitPrice: plan.Target, exitTime: cur.Time);
                        else
                            MaybeMoveBreakEven(bar, cur, plan);
                    MaybeTrailStop(bar, cur, plan);
                    }
                }
            }
            else
            {
                if (_planState == PlanState.InPosition && _activePlan is not null && _activePlan.ExitBar is null)
                {
                    // MarketClose: check SL/TP virtually (no ATAS bracket orders)
                    if (EntryMode != EntryExecutionMode.LimitAtZoneAnchor)
                    {
                        var plan = _activePlan;
                        if (plan.Side == "LONG")
                        {
                            if (cur.Low <= plan.Stop)
                                ExitPlan(bar, "SL Hit", exitPrice: plan.Stop, exitTime: cur.Time);
                            else if (cur.High >= plan.Target)
                                ExitPlan(bar, "TP Hit", exitPrice: plan.Target, exitTime: cur.Time);
                        }
                        else
                        {
                            if (cur.High >= plan.Stop)
                                ExitPlan(bar, "SL Hit", exitPrice: plan.Stop, exitTime: cur.Time);
                            else if (cur.Low <= plan.Target)
                                ExitPlan(bar, "TP Hit", exitPrice: plan.Target, exitTime: cur.Time);
                        }
                    }
                    MaybeMoveBreakEven(bar, cur, _activePlan);
                }
            }

            if (_planState != PlanState.Flat)
            {
                SetBlock(bar, TriggerBlockReason.InPosition, "已有持仓/计划单（单仓位）");
                return;
            }

            if (ShouldBlockByDailyGuard(bar, cur.Time))
                return;

            if (EnableCooldown && _cooldownUntilBar >= 0 && bar < _cooldownUntilBar)
            {
                var left = _cooldownUntilBar - bar;
                SetBlock(bar, TriggerBlockReason.CooldownActive, $"冷却中 left={left} bars (until={_cooldownUntilBar})");
                return;
            }

            if (_confirmStartBar >= 0 &&
                _confirmStartBar == _lastTriggeredConfirmStartBar &&
                _confirmZoneKey is not null &&
                _lastTriggeredZoneKey is not null &&
                ZoneKeyEquals(_confirmZoneKey, _lastTriggeredZoneKey))
            {
                SetBlock(bar, TriggerBlockReason.DuplicateTriggerSameSession, $"同一确认会话已触发过 (confirmStartBar={_confirmStartBar})");
                return;
            }

            if (_phase != ConfirmPhase.Confirmed)
            {
                var detail = $"M5未确认 phase={_phase} | {_confirmText}";
                if (_htfBias == TrendDirection.Neutral)
                    detail = $"HTF Bias=Neutral({_htfBiasReason}) -> 不交易 | {detail}";

                SetBlock(bar, TriggerBlockReason.NotConfirmed, detail);
                return;
            }

            if (_confirmZoneKey is null)
            {
                SetBlock(bar, TriggerBlockReason.ConfirmZoneKeyMissing, "Confirmed但未锁定区块（confirmZoneKey=null）");
                return;
            }

            if (!TryGetZoneByKeyWithShadow(active, _confirmZoneKey, out var zone, out var usedShadow))
            {
                SetBlock(bar, TriggerBlockReason.ZoneNotFound, $"锁定区块不在ActiveZones且无Shadow | key={FormatZoneKeyCN(_confirmZoneKey)}");
                return;
            }

            if (zone.IsInvalidated)
            {
                SetBlock(bar, TriggerBlockReason.ZoneInvalidated, $"区块已失效 | {zone.ToShortText()}");
                ResetConfirm(bar, "区块失效");
                _phase = ConfirmPhase.WaitZoneTouch;
                return;
            }

            if (zone.IsVpRejected)
            {
                SetBlock(bar, TriggerBlockReason.ZoneVpRejected, BuildVpRejectedDetail(cur, zone));
                ResetConfirm(bar, "VP拒绝");
                _phase = ConfirmPhase.WaitZoneTouch;
                return;
            }

            if (EnableZoneQualityScoreFilter)
            {
                var min = Math.Max(0, Math.Min(10, MinZoneQualityScore));
                if (_lockedZoneQualityScore >= 0 && _lockedZoneQualityScore < min)
                {
                    SetBlock(bar, TriggerBlockReason.ZoneQualityInsufficient, $"区块质量不足(Q-Freeze): lockQ={_lockedZoneQualityScore}/10 < {min}/10 | {_lockedZoneQualityDetail}");
                    ResetConfirm(bar, "锁定区块质量不足(Q-Freeze)");
                    _phase = ConfirmPhase.WaitZoneTouch;
                    return;
                }
            }

            if (_htfBias == TrendDirection.Neutral)
            {
                SetBlock(bar, TriggerBlockReason.BiasNeutral, $"HTF Bias=Neutral({_htfBiasReason})");
                return;
            }

            var isLong = _htfBias == TrendDirection.Bullish;
            var side = isLong ? "LONG" : "SHORT";

            var qLock = _lockedZoneQualityScore;

            if (DisallowMitigatedZones && zone.IsMitigated)
            {
                var armedOrPending = _ofArmed || _pendingEntry is not null || _live is not null;

                if (!armedOrPending)
                {
                    SetBlock(bar, TriggerBlockReason.ZoneQualityInsufficient, $"Mitigated且禁止交易(未Armed) | {zone.ToShortText()}");
                    ResetConfirm(bar, "Mitigated硬过滤(未Armed)");
                    _phase = ConfirmPhase.WaitZoneTouch;
                    return;
                }

                if (!_loggedMitigatedKeepOnce)
                {
                    _loggedMitigatedKeepOnce = true;
                    AppendLog($"ZONE_BECAME_MITIGATED bar={bar} phase={_phase} armed={_ofArmed} pending={(_pendingEntry is null ? "-" : _pendingEntry.LimitPrice.ToString("0.########"))} zone={zone.ToShortText()} -> keep setup");
                }
            }

            // ===== OF Armed =====
            if (!_ofArmed)
            {
                if (EnableOrderFlowTimeWindow)
                {
                    var effMaxWait = GetEffectiveOrderFlowMaxWaitBars(qLock);
                    if (effMaxWait > 0 && _confirmedAtBar >= 0 && (bar - _confirmedAtBar) > effMaxWait)
                    {
                        var waited = (bar - _confirmedAtBar);
                        var detail = $"Confirmed后等待OF超时 barsWaited={waited} > maxWait={effMaxWait} (qLock={qLock}/10) -> reset confirm";
                        SetBlock(bar, TriggerBlockReason.OrderFlowWaitTimeout, detail);

                        AppendLog($"CONFIRMED_OF_TIMEOUT bar={bar} confirmedAt={_confirmedAtBar} waited={waited} maxWait={effMaxWait} qLock={qLock}/10 -> reset confirm");

                        ResetConfirm(bar, "订单流等待超时（未Armed）");
                        _phase = ConfirmPhase.WaitZoneTouch;
                        return;
                    }
                }

                if (_lastOrderFlow is null)
                {
                    SetBlock(bar, TriggerBlockReason.OrderFlowNotReady, "订单流未评分/无数据（lastOrderFlow=null）");
                    return;
                }

                if (_lastOrderFlow.Score < OrderFlowMinScoreThreshold)
                {
                    SetBlock(
                        bar,
                        TriggerBlockReason.OrderFlowScoreInsufficient,
                        $"订单流不足 score={_lastOrderFlow.Score}/4 < {OrderFlowMinScoreThreshold} | {_lastOrderFlow.Text}");
                    return;
                }

                _ofArmed = true;
                _ofArmedAtBar = bar;
                _ofArmedScore = _lastOrderFlow.Score;
                _ofArmedBullWeight = _lastOrderFlow.BullWeight;
                _ofArmedBearWeight = _lastOrderFlow.BearWeight;
                _ofArmedText = _lastOrderFlow.Text;
                _ofSoftenedForEntry = false;
                _ofSoftenedMismatchBars = 0;

                AppendLog($"OF_ARMED bar={bar} score={_ofArmedScore}/4 text={_ofArmedText} zone={zone.ToShortText()}");
            }

            // ===== Armed -> 执行 =====
 
            // ===== OF方向与交易方向一致性检查 =====

            if (_lastOrderFlow is not null)
            if (_ofArmed)
            {
                var ofBull = _ofArmedBullWeight;
                var ofBear = _ofArmedBearWeight;

                // OF方向与HTF一致 -> 重置连续MISMATCH计数
                if ((isLong && ofBull >= ofBear) || (!isLong && ofBear >= ofBull))
                {
                    if (_mismatchCount > 0)
                    {
                        AppendLog($"OF_MISMATCH_RESET bar={bar} count={_mismatchCount} dir={_mismatchDir} -> bias aligned");
                        _mismatchCount = 0;
                        _mismatchDir = "";
                        _ofSoftenedForEntry = false;
                        _ofSoftenedMismatchBars = 0;
                    }
                }

                if (isLong && ofBear > ofBull)
                {
                    if (_mismatchDir != "BEAR") { _mismatchCount = 1; _mismatchDir = "BEAR"; }
                    else { _mismatchCount++; }

                    var softenBars = Math.Max(1, OrderFlowMismatchSofteningBars);
                    if (EnableOrderFlowMismatchSoftening && _mismatchCount >= softenBars)
                    {
                        _ofSoftenedForEntry = true;
                        _ofSoftenedMismatchBars = _mismatchCount;
                        AppendLog($"OF_BEAR_SOFTEN bar={bar} n={_mismatchCount} -> skip mismatch, allow entry");
                    }
                    else
                    {
                        if (!_ofDirMismatchLogged)
                        {
                            _ofDirMismatchLogged = true;
                            AppendLog($"OF_DIR_MISMATCH bar={bar} side=LONG bull={ofBull} bear={ofBear} n={_mismatchCount} -> skipped");
                        }
                        SetBlock(bar, TriggerBlockReason.EntryTooFarFromZone,
                            $"OF方向相反: LONG但OF偏空(Bull={ofBull} Bear={ofBear}) n={_mismatchCount}");
                        return;
                    }
                }
                if (!isLong && ofBull > ofBear)
                {
                    if (_mismatchDir != "BULL") { _mismatchCount = 1; _mismatchDir = "BULL"; }
                    else { _mismatchCount++; }

                    var softenBars = Math.Max(1, OrderFlowMismatchSofteningBars);
                    if (EnableOrderFlowMismatchSoftening && _mismatchCount >= softenBars)
                    {
                        _ofSoftenedForEntry = true;
                        _ofSoftenedMismatchBars = _mismatchCount;
                        AppendLog($"OF_BULL_SOFTEN bar={bar} n={_mismatchCount} -> skip mismatch, allow entry");
                    }
                    else
                    {
                        if (!_ofDirMismatchLogged)
                        {
                            _ofDirMismatchLogged = true;
                            AppendLog($"OF_DIR_MISMATCH bar={bar} side=SHORT bull={ofBull} bear={ofBear} n={_mismatchCount} -> skipped");
                        }
                        SetBlock(bar, TriggerBlockReason.EntryTooFarFromZone,
                            $"OF方向相反: SHORT但OF偏多(Bull={ofBull} Bear={ofBear}) n={_mismatchCount}");
                        return;
                    }
                }
            }
            if (EntryMode == EntryExecutionMode.LimitAtZoneAnchor)
            {
                // Stage B LIVE branch
                if (EnableLiveOrders)
                {
                    HandleLiveLimitExecution(bar, cur, zone, usedShadow, isLong, side, qLock, halfTick);
                    return;
                }

                // -------------------------
                // Virtual branch
                // -------------------------

                if (_pendingEntry is null && EnablePlaceDistanceGate)
                {
                    var thTicks = GetMaxPlaceDistanceTicksIfSet();
                    if (thTicks > 0)
                    {
                        var distTicks = GetDistanceToRangeTicks(bar, cur.Close, zone.Low, zone.High);
                        if (distTicks > thTicks + 0.5m)
                        {
                            SetBlock(bar, TriggerBlockReason.EntryPlaceTooFarFromZone,
                                $"挂单距离闸门(Ticks)：closeDist={distTicks:0.##}t > {thTicks}t | close={cur.Close:0.########} zone={zone.Low:0.########}-{zone.High:0.########} | skip+reset");

                            AppendLog($"ENTRY_ORDER_SKIP bar={bar} reason=PlaceTooFar closeDistTicks={distTicks:0.##} thTicks={thTicks} close={cur.Close:0.########} zone={zone.ToShortText()} -> reset confirm");
                            ResetConfirm(bar, "挂单距离过远(PlaceGateTicks)");
                            _phase = ConfirmPhase.WaitZoneTouch;
                            return;
                        }
                    }
                    else
                    {
                        var thPts = Math.Max(0m, MaxPlaceDistanceFromZonePoints);
                        if (thPts > 0m)
                        {
                            var distPts = GetDistanceToRangePoints(cur.Close, zone.Low, zone.High);
                            if (distPts > thPts + halfTick)
                            {
                                SetBlock(bar, TriggerBlockReason.EntryPlaceTooFarFromZone,
                                    $"挂单距离闸门(LEGACY)：closeDist={distPts:0.00}pt > {thPts:0.##}pt | close={cur.Close:0.########} zone={zone.Low:0.########}-{zone.High:0.########} | skip+reset");

                                AppendLog($"ENTRY_ORDER_SKIP bar={bar} reason=PlaceTooFar closeDist={distPts:0.00}pt th={thPts:0.##}pt close={cur.Close:0.########} zone={zone.ToShortText()} -> reset confirm");
                                ResetConfirm(bar, "挂单距离过远(PlaceGate)");
                                _phase = ConfirmPhase.WaitZoneTouch;
                                return;
                            }
                        }
                    }
                }

                if (_pendingEntry is null)
                {
                    var (limit, anchorUsed, anchorText) = GetLimitEntryPriceDynamic(bar, zone, isLong);
                    var effWait = GetEffectiveEntryMaxWaitBars(anchorUsed, qLock);

                    _pendingEntry = new PendingEntry
                    {
                        CreatedBar = bar,
                        Side = side,
                        LimitPrice = limit,
                        AnchorText = anchorText,
                        AnchorUsed = anchorUsed,
                        EffectiveMaxWaitBars = effWait,
                        Zone = _confirmZoneKey,
                        OfScore = _ofArmedScore,
                        OfText = _ofArmedText,
                        OfSoftened = _ofSoftenedForEntry,
                        OfMismatchBars = _ofSoftenedMismatchBars,
                        LockedQScore = qLock,
                        CancelLogged = false
                    };

                    AppendLog($"ENTRY_ORDER_CREATE bar={bar} side={side} limit={limit:0.########} anchor={anchorText} effWait={effWait} of={_ofArmedScore}/4 qLock={(qLock < 0 ? "-" : qLock.ToString())}/10 zone={FormatZoneKeyCN(_confirmZoneKey)}");
                }

                var waited = Math.Max(0, bar - _pendingEntry.CreatedBar);
                var maxWait = Math.Max(0, _pendingEntry.EffectiveMaxWaitBars);

                if (maxWait > 0 && waited > maxWait)
                {
                    SetBlock(bar, TriggerBlockReason.EntryOrderExpired, $"限价单超时 waited={waited} > {maxWait} | limit={_pendingEntry.LimitPrice:0.########} -> cancel+reset");

                    AppendLog($"ENTRY_ORDER_CANCEL bar={bar} reason=Expired waited={waited} maxWait={maxWait} limit={_pendingEntry.LimitPrice:0.########} side={_pendingEntry.Side} zone={FormatZoneKeyCN(_pendingEntry.Zone)}");
                    _pendingEntry.CancelLogged = true;

                    ResetConfirm(bar, "限价入场超时");
                    _phase = ConfirmPhase.WaitZoneTouch;
                    return;
                }

                if (EnableEarlyCancelRunaway)
                {
                    var minWaitBeforeCancel = Math.Max(0, RunawayCancelMinWaitBars);
                    if (waited >= minWaitBeforeCancel)
                    {
                        var runawayDistPts = GetRunawayDistanceInBiasDirectionPoints(cur.Close, zone, isLong);

                        var thTicks = GetRunawayCancelDistanceTicksIfSet();
                        if (thTicks > 0)
                        {
                            var runawayDistTicks = tick > 0m ? runawayDistPts / tick : 0m;
                            var needBars = Math.Max(1, RunawayCancelConsecutiveBars);

                            if (runawayDistTicks > thTicks + 0.5m)
                                _pendingEntry.RunawayBars++;
                            else
                                _pendingEntry.RunawayBars = 0;

                            if (_pendingEntry.RunawayBars >= needBars)
                            {
                                SetBlock(bar, TriggerBlockReason.EntryOrderEarlyCanceled,
                                    $"提前取消(runaway,ticks): dist={runawayDistTicks:0.##}t > {thTicks}t 连续{_pendingEntry.RunawayBars}bars | waited={waited}/{_pendingEntry.EffectiveMaxWaitBars}");

                                AppendLog($"ENTRY_ORDER_CANCEL bar={bar} reason=Runaway runawayDistTicks={runawayDistTicks:0.##} thTicks={thTicks} nBars={_pendingEntry.RunawayBars} waited={waited}/{_pendingEntry.EffectiveMaxWaitBars} limit={_pendingEntry.LimitPrice:0.########} side={_pendingEntry.Side} zone={FormatZoneKeyCN(_pendingEntry.Zone)} -> reset confirm");
                                _pendingEntry.CancelLogged = true;

                                ResetConfirm(bar, "挂单提前取消(runaway-ticks)");
                                _phase = ConfirmPhase.WaitZoneTouch;
                                return;
                            }
                        }
                        else
                        {
                            var runawayThPts = Math.Max(0m, RunawayCancelDistancePoints);
                            var needBars = Math.Max(1, RunawayCancelConsecutiveBars);

                            if (runawayDistPts > runawayThPts + halfTick)
                                _pendingEntry.RunawayBars++;
                            else
                                _pendingEntry.RunawayBars = 0;

                            if (_pendingEntry.RunawayBars >= needBars)
                            {
                                SetBlock(bar, TriggerBlockReason.EntryOrderEarlyCanceled,
                                    $"提前取消(runaway,legacy): dist={runawayDistPts:0.00}pt > {runawayThPts:0.##}pt 连续{_pendingEntry.RunawayBars}bars | waited={waited}/{_pendingEntry.EffectiveMaxWaitBars}");

                                AppendLog($"ENTRY_ORDER_CANCEL bar={bar} reason=Runaway runawayDist={runawayDistPts:0.00}pt th={runawayThPts:0.##}pt nBars={_pendingEntry.RunawayBars} waited={waited}/{_pendingEntry.EffectiveMaxWaitBars} limit={_pendingEntry.LimitPrice:0.########} side={_pendingEntry.Side} zone={FormatZoneKeyCN(_pendingEntry.Zone)} -> reset confirm");
                                _pendingEntry.CancelLogged = true;

                                ResetConfirm(bar, "挂单提前取消(runaway)");
                                _phase = ConfirmPhase.WaitZoneTouch;
                                return;
                            }
                        }
                    }
                }

                var limitPrice = _pendingEntry.LimitPrice;

                var filled = isLong
                    ? (cur.Low <= limitPrice + halfTick)
                    : (cur.High >= limitPrice - halfTick);

                if (!filled)
                {
                    var dist = GetDistanceToRangePoints(cur.Close, zone.Low, zone.High);
                    SetBlock(
                        bar,
                        TriggerBlockReason.EntryOrderPending,
                        $"等待回撤成交 limit={limitPrice:0.########}({_pendingEntry.AnchorText}) waited={waited}/{maxWait} | closeDistToZone≈{dist:0.00}pt | OF(armed)={_pendingEntry.OfScore}/4 | QLock={(qLock < 0 ? "-" : qLock.ToString())}/10");

                    return;
                }

                var entry = limitPrice;

                if (EnableAntiChaseFilter)
                {
                    var thTicks = GetMaxEntryDistanceTicksIfSet();
                    if (thTicks > 0)
                    {
                        var distTicks = GetDistanceToRangeTicks(bar, entry, zone.Low, zone.High);
                        if (distTicks > thTicks + 0.5m)
                        {
                            SetBlock(bar, TriggerBlockReason.EntryTooFarFromZone,
                                $"防追价(ticks)：entryDist={distTicks:0.##}t > {thTicks}t | entry={entry:0.########} zone={zone.Low:0.########}-{zone.High:0.########} -> cancel+reset");

                            AppendLog($"ENTRY_ORDER_CANCEL bar={bar} reason=AntiChase entryDistTicks={distTicks:0.##} thTicks={thTicks} entry={entry:0.########} limit={limitPrice:0.########} zone={zone.ToShortText()} -> reset confirm");
                            _pendingEntry.CancelLogged = true;

                            _pendingEntry = null;
                            ResetConfirm(bar, "防追价(limit-ticks)");
                            _phase = ConfirmPhase.WaitZoneTouch;
                            return;
                        }
                    }
                    else
                    {
                        var maxDistPts = Math.Max(0m, MaxEntryDistanceFromZonePoints);
                        if (maxDistPts > 0m)
                        {
                            var distPts = GetDistanceToRangePoints(entry, zone.Low, zone.High);
                            if (distPts > maxDistPts + halfTick)
                            {
                                SetBlock(bar, TriggerBlockReason.EntryTooFarFromZone,
                                    $"防追价(legacy)：entryDist={distPts:0.00}pt > {maxDistPts:0.##}pt | entry={entry:0.########} zone={zone.Low:0.########}-{zone.High:0.########} -> cancel+reset");

                                AppendLog($"ENTRY_ORDER_CANCEL bar={bar} reason=AntiChase entryDist={distPts:0.00}pt max={maxDistPts:0.##}pt entry={entry:0.########} limit={limitPrice:0.########} zone={zone.ToShortText()} -> reset confirm");
                                _pendingEntry.CancelLogged = true;

                                _pendingEntry = null;
                                ResetConfirm(bar, "防追价(limit)");
                                _phase = ConfirmPhase.WaitZoneTouch;
                                return;
                            }
                        }
                    }
                }

                if (!TrySelectStop(
                        bar,
                        isLong,
                        entry,
                        zone,
                        usedShadow,
                        out var stop,
                        out var stopSource,
                        out var riskTicks,
                        out var riskPoints,
                        out var failReason,
                        out var failDetail))
                {
                    switch (failReason)
                    {
                        case StopSelectFailReason.TooSmall:
                            SetBlock(bar, TriggerBlockReason.RiskTicksTooSmall, failDetail);
                            break;
                        case StopSelectFailReason.TooLarge:
                        case StopSelectFailReason.MixedOutOfRange:
                            SetBlock(bar, TriggerBlockReason.RiskTicksTooLarge, failDetail);
                            break;
                        default:
                            SetBlock(bar, TriggerBlockReason.RiskInvalid, failDetail);
                            break;
                    }

                    return;
                }

                stop = AlignStopToTick(stop, isLong, bar);
                if (ShouldBlockEntryByV128Filters(
                        bar,
                        side,
                        riskPoints,
                        _confirmZoneKey,
                        "VIRTUAL",
                        _pendingEntry.OfSoftened,
                        _pendingEntry.OfMismatchBars))
                    return;

                var target = ComputeTargetFromRiskTicks(entry, isLong, riskTicks, RiskRewardR, bar);
                target = AlignTargetToTick(target, isLong, bar);

                _activePlan = new TradePlan
                {
                    CreatedBar = bar,
                    Side = side,
                    Entry = entry,
                    Stop = stop,
                    Target = target,
                    InitialStop = stop,
                    InitialRiskPoints = riskPoints,
                    Zone = _confirmZoneKey,
                    OfScore = _pendingEntry.OfScore,
                    OfText = _pendingEntry.OfText,
                    OfSoftened = _pendingEntry.OfSoftened,
                    OfMismatchBars = _pendingEntry.OfMismatchBars
                };

                _planState = PlanState.InPosition;
                LogWideRiskIfNeeded(bar, _activePlan);

                AppendLog($"ENTRY_ORDER_FILLED bar={bar} side={side} entry={entry:0.########} limit={limitPrice:0.########} anchor={_pendingEntry.AnchorText} mode=VIRTUAL -> PLAN_CREATE");
                AppendLog($"PLAN_CREATE bar={bar} side={side} entry={entry:0.########} stop={stop:0.########} tp={target:0.########} riskTicks={riskTicks} of={_activePlan.OfScore}/4 ofSoftened={_activePlan.OfSoftened} mismatchBars={_activePlan.OfMismatchBars} zone={FormatZoneKeyCN(_activePlan.Zone)} src={stopSource} qLock={(qLock < 0 ? "-" : qLock.ToString())}/10 mode=VIRTUAL");

                _lastTriggeredConfirmStartBar = _confirmStartBar;
                _lastTriggeredZoneKey = _confirmZoneKey;

        }
        else
        {
            if (EnableRetraceEntry)
            {
                if (!_mktEntryScheduled)
                {
                    _mktEntryScheduled = true;
                    _mktEntryBar = bar;
                    AppendLog($"MKT_DELAY_SCHEDULED bar={bar} side={side} of={_ofArmedScore}/4 zone={FormatZoneKeyCN(_confirmZoneKey)} -> waiting for retrace to zone");
                    SetBlock(bar, TriggerBlockReason.EntryOrderPending, "延迟入场: 等待价格回撤至zone");
                    return;
                }

                var maxWait = Math.Max(0, MaxRetraceWaitBars);
                if (maxWait > 0 && bar > _mktEntryBar + maxWait)
                {
                    AppendLog($"MKT_DELAY_EXPIRED bar={bar} scheduledBar={_mktEntryBar} maxWait={maxWait} of={_ofArmedScore}/4 zone={FormatZoneKeyCN(_confirmZoneKey)} -> cancel+reset");
                    SetBlock(bar, TriggerBlockReason.EntryOrderExpired, $"延迟入场超时: waited={bar - _mktEntryBar} > {maxWait}");
                    ResetConfirm(bar, "MktDelayExpired");
                    _phase = ConfirmPhase.WaitZoneTouch;
                    return;
                }

                var retraced = isLong
                    ? (cur.Low <= zone.High + halfTick)
                    : (cur.High >= zone.Low - halfTick);

                var maxRetraceDistTicks = Math.Max(0, RetraceEntryMaxDistanceTicks);
                var closeDistTicks = GetDistanceToRangeTicks(bar, cur.Close, zone.Low, zone.High);

                if (!retraced || (maxRetraceDistTicks > 0 && closeDistTicks > maxRetraceDistTicks + 0.5m))
                {
                    AppendLog($"MKT_DELAY_WAITING bar={bar} scheduledBar={_mktEntryBar} side={side} retraced={retraced} closeDistTicks={closeDistTicks:0.##} maxDistTicks={maxRetraceDistTicks} of={_ofArmedScore}/4 zone={FormatZoneKeyCN(_confirmZoneKey)} -> waiting for retrace");
                    SetBlock(bar, TriggerBlockReason.EntryOrderPending, $"等待回撤至zone, 已等待{bar - _mktEntryBar}bars | closeDist={closeDistTicks:0.##}t/{maxRetraceDistTicks}t | of={_ofArmedScore}/4 qLock={(qLock < 0 ? "-" : qLock.ToString())}/10");
                    return;
                }

                AppendLog($"MKT_DELAY_FIRED bar={bar} scheduledBar={_mktEntryBar} side={side} closeDistTicks={closeDistTicks:0.##} of={_ofArmedScore}/4 zone={FormatZoneKeyCN(_confirmZoneKey)} -> retrace detected, entering at market");
                _mktEntryScheduled = false;
                _mktEntryBar = -1;
            }

            // MarketClose: submit real market order when live orders enabled
            if (EnableLiveOrders)
            {
                HandleLiveMarketExecution(bar, cur, zone, usedShadow, isLong, side, qLock, halfTick);
                return;
            }

            // Backtest/virtual: enter at bar close immediately
            var entry = cur.Close;

            if (EnableAntiChaseFilter)
            {
                var thTicks = GetMaxEntryDistanceTicksIfSet();
                if (thTicks > 0)
                {
                    var distTicks = GetDistanceToRangeTicks(bar, entry, zone.Low, zone.High);
                    if (distTicks > thTicks + 0.5m)
                    {
                        SetBlock(bar, TriggerBlockReason.EntryTooFarFromZone,
                            $"防追价(mkt,ticks)：entryDist={distTicks:0.##}t > {thTicks}t | entry={entry:0.########} zone={zone.Low:0.########}-{zone.High:0.########} -> cancel+reset");
                        AppendLog($"ENTRY_ORDER_CANCEL bar={bar} reason=AntiChase entryDistTicks={distTicks:0.##} thTicks={thTicks} entry={entry:0.########} zone={zone.ToShortText()} -> reset confirm");
                        ResetConfirm(bar, "防追价(mkt-ticks)");
                        _phase = ConfirmPhase.WaitZoneTouch;
                        return;
                    }
                }
                else
                {
                    var maxDistPts = Math.Max(0m, MaxEntryDistanceFromZonePoints);
                    if (maxDistPts > 0m)
                    {
                        var distPts = GetDistanceToRangePoints(entry, zone.Low, zone.High);
                        if (distPts > maxDistPts + halfTick)
                        {
                            SetBlock(bar, TriggerBlockReason.EntryTooFarFromZone,
                                $"防追价(mkt,legacy)：entryDist={distPts:0.00}pt > {maxDistPts:0.##}pt | entry={entry:0.########} zone={zone.Low:0.########}-{zone.High:0.########} -> cancel+reset");
                            AppendLog($"ENTRY_ORDER_CANCEL bar={bar} reason=AntiChase entryDist={distPts:0.00}pt max={maxDistPts:0.##}pt entry={entry:0.########} zone={zone.ToShortText()} -> reset confirm");
                            ResetConfirm(bar, "防追价(mkt)");
                            _phase = ConfirmPhase.WaitZoneTouch;
                            return;
                        }
                    }
                }
            }

            if (!TrySelectStop(
                    bar,
                    isLong,
                    entry,
                    zone,
                    usedShadow,
                    out var stop,
                    out var stopSource,
                    out var riskTicks,
                    out var riskPoints,
                    out var failReason,
                    out var failDetail))
            {
                switch (failReason)
                {
                    case StopSelectFailReason.TooSmall:
                        SetBlock(bar, TriggerBlockReason.RiskTicksTooSmall, failDetail);
                        break;
                    case StopSelectFailReason.TooLarge:
                    case StopSelectFailReason.MixedOutOfRange:
                        SetBlock(bar, TriggerBlockReason.RiskTicksTooLarge, failDetail);
                        break;
                    default:
                        SetBlock(bar, TriggerBlockReason.RiskInvalid, failDetail);
                        break;
                }
                return;
            }

            stop = AlignStopToTick(stop, isLong, bar);
            if (ShouldBlockEntryByV128Filters(
                    bar,
                    side,
                    riskPoints,
                    _confirmZoneKey,
                    "VIRTUAL_MARKET",
                    _ofSoftenedForEntry,
                    _ofSoftenedMismatchBars))
                return;

            var target = ComputeTargetFromRiskTicks(entry, isLong, riskTicks, RiskRewardR, bar);
            target = AlignTargetToTick(target, isLong, bar);

            _activePlan = new TradePlan
            {
                CreatedBar = bar,
                Side = side,
                Entry = entry,
                Stop = stop,
                Target = target,
                InitialStop = stop,
                InitialRiskPoints = riskPoints,
                Zone = _confirmZoneKey,
                OfScore = _ofArmedScore,
                OfText = _ofArmedText,
                OfSoftened = _ofSoftenedForEntry,
                OfMismatchBars = _ofSoftenedMismatchBars
            };

            _planState = PlanState.InPosition;
            LogWideRiskIfNeeded(bar, _activePlan);

            AppendLog($"ENTRY_ORDER_FILLED bar={bar} side={side} entry={entry:0.########} mode=VIRTUAL_MARKET -> PLAN_CREATE");
            AppendLog($"PLAN_CREATE bar={bar} side={side} entry={entry:0.########} stop={stop:0.########} tp={target:0.########} riskTicks={riskTicks} of={_activePlan.OfScore}/4 ofSoftened={_activePlan.OfSoftened} mismatchBars={_activePlan.OfMismatchBars} zone={FormatZoneKeyCN(_activePlan.Zone)} src={stopSource} qLock={(qLock < 0 ? "-" : qLock.ToString())}/10 mode=VIRTUAL_MARKET");

            _lastTriggeredConfirmStartBar = _confirmStartBar;
            _lastTriggeredZoneKey = _confirmZoneKey;
        }
        }

        // =========================
        // OrderFlow / Entry wait helpers
        // =========================
        private int GetEffectiveOrderFlowMaxWaitBars(int qLock)
        {
            if (!EnableOrderFlowTimeWindow)
                return 0;

            var baseWait = Math.Max(0, OrderFlowMaxWaitBarsAfterConfirmed);

            if (!EnableAdaptiveOrderFlowWaitWindow)
                return baseWait;

            var highScore = Math.Max(0, Math.Min(10, AdaptiveOrderFlowHighScore));
            var highWait = Math.Max(0, AdaptiveOrderFlowHighWaitBars);

            if (qLock >= highScore && qLock >= 0)
                return Math.Max(baseWait, highWait);

            return baseWait;
        }

        private int GetEffectiveEntryMaxWaitBars(ZoneEntryAnchor anchorUsed, int zoneQualityScore)
        {
            var baseWait = Math.Max(0, EntryMaxWaitBarsAfterArmed);

            if (!EnableAdaptiveEntryWait)
                return baseWait;

            var w = baseWait;

            if (anchorUsed == ZoneEntryAnchor.Mid)
                w += Math.Max(0, AdaptiveWaitMidBonusBars);

            var high = Math.Max(0, Math.Min(10, AdaptiveWaitQualityHighScore));
            var low = Math.Max(0, Math.Min(10, AdaptiveWaitQualityLowScore));

            if (zoneQualityScore >= high)
                w += Math.Max(0, AdaptiveWaitQualityHighBonusBars);

            if (zoneQualityScore <= low && zoneQualityScore >= 0)
                w -= Math.Max(0, AdaptiveWaitQualityLowPenaltyBars);

            var minW = Math.Max(0, AdaptiveWaitMinBars);
            var maxW = Math.Max(minW, AdaptiveWaitMaxBars);

            if (w < minW) w = minW;
            if (w > maxW) w = maxW;

            return w;
        }

        private static decimal GetRunawayDistanceInBiasDirectionPoints(decimal close, TradingZone zone, bool isLongBias)
        {
            if (isLongBias)
            {
                if (close > zone.High)
                    return close - zone.High;
                return 0m;
            }
            else
            {
                if (close < zone.Low)
                    return zone.Low - close;
                return 0m;
            }
        }

        private void MaybeMoveBreakEven(int bar, IndicatorCandle cur, TradePlan plan)
        {
            if (!EnableBreakEvenMove)
                return;

            if (plan.BreakEvenMoved)
                return;

            if (plan.InitialRiskPoints <= 0m)
                return;

            EnsureInstrumentRulesInitialized(bar, "MaybeMoveBreakEven");

            var atR = BreakEvenAtR <= 0m ? 1m : BreakEvenAtR;
            var trig = plan.InitialRiskPoints * atR;

            var tickSz = GetTickSize(bar, "BE.plus");
            var plusTicksFixed = Math.Max(0, BreakEvenPlusTicks);
            var riskTicksForBe = tickSz > 0m ? (int)Math.Round(plan.InitialRiskPoints / tickSz) : 0;
            var plusTicksRatio = Math.Max(0, (int)(riskTicksForBe * BreakEvenPlusRRatio));
            var plusTicks = Math.Max(plusTicksFixed, plusTicksRatio);
            var plus = plusTicks * tickSz;

            if (plan.Side == "LONG")
            {
                var beTrigger = plan.Entry + trig;
                if (cur.High >= beTrigger)
                {
                    var rawNewStop = plan.Entry + plus;
                    var newStop = AlignBreakEvenStopToTick(rawNewStop, isLong: true, bar: bar);

                    if (newStop > plan.Stop)
                    {
                        plan.Stop = newStop;
                        plan.BreakEvenMoved = true;
                        AppendLog($"PLAN_BE_MOVE bar={bar} side=LONG trigger@{beTrigger:0.########} newSL={newStop:0.########} mode={(EnableLiveOrders ? "LIVE" : "VIRTUAL")}");

                        if (EnableLiveOrders && _live is not null && _live.StopOrder is not null)
                        {
                            var old = _live.StopOrder;
                            var neu = old.Clone();
                            neu.TriggerPrice = newStop;
                            neu.TriggerPriceType = LiveTriggerPriceType;

                            EnqueueOrderAction("BE_ModifyStop", async () =>
                            {
                                await ModifyOrderAsync(old, neu);
                            });

                            _live.StopPrice = newStop;
                        }
                    }
                }
            }
            else
            {
                var beTrigger = plan.Entry - trig;
                if (cur.Low <= beTrigger)
                {
                    var rawNewStop = plan.Entry - plus;
                    var newStop = AlignBreakEvenStopToTick(rawNewStop, isLong: false, bar: bar);

                    if (newStop < plan.Stop)
                    {
                        plan.Stop = newStop;
                        plan.BreakEvenMoved = true;
                        AppendLog($"PLAN_BE_MOVE bar={bar} side=SHORT trigger@{beTrigger:0.########} newSL={newStop:0.########} mode={(EnableLiveOrders ? "LIVE" : "VIRTUAL")}");

                        if (EnableLiveOrders && _live is not null && _live.StopOrder is not null)
                        {
                            var old = _live.StopOrder;
                            var neu = old.Clone();
                            neu.TriggerPrice = newStop;
                            neu.TriggerPriceType = LiveTriggerPriceType;

                            EnqueueOrderAction("BE_ModifyStop", async () =>
                            {
                                await ModifyOrderAsync(old, neu);
                            });

                            _live.StopPrice = newStop;
                        }
                    }
                }
            }

            // 动态止盈：BE触发后同时收紧TP到 DynamicTPTargetR，锁定部分利润
            if (plan.BreakEvenMoved && EnableDynamicTPTightening && !plan.DynamicTPAdjusted)
            {
                var tick = GetTickSize(bar, "DynamicTP.tick");
                if (tick > 0m && plan.InitialRiskPoints > 0m)
                {
                    var riskTicks = (int)Math.Round(plan.InitialRiskPoints / tick);
                    if (riskTicks < 1) riskTicks = 1;
                    var newTarget = ComputeTargetFromRiskTicks(plan.Entry, plan.Side == "LONG", riskTicks, DynamicTPTargetR, bar);
                    plan.Target = newTarget;
                    plan.DynamicTPAdjusted = true;
                    AppendLog($"PLAN_DYNAMIC_TP bar={bar} side={plan.Side} newTP={newTarget:0.########} targetR={DynamicTPTargetR} mode={(EnableLiveOrders ? "LIVE" : "VIRTUAL")}");

                    if (EnableLiveOrders && _live is not null && _live.TargetOrder is not null)
                    {
                        var old = _live.TargetOrder;
                        if (IsLiveOrderModifiable(old, "DynamicTP_ModifyTarget"))
                        {
                            var neu = old.Clone();
                            neu.Price = newTarget;
                            EnqueueOrderAction("DynamicTP_ModifyTarget", async () =>
                            {
                                await ModifyOrderAsync(old, neu);
                            });
                            _live.TargetPrice = newTarget;
                        }
                    }
                }
            }
        }


        private void MaybeTrailStop(int bar, IndicatorCandle cur, TradePlan plan)
        {
            if (!EnableTrailingStop)
                return;

            if (plan.InitialRiskPoints <= 0m)
                return;

            EnsureInstrumentRulesInitialized(bar, "MaybeTrailStop");

            var tick = GetTickSize(bar, "MaybeTrailStop.tick");
            if (tick <= 0m) tick = TickSizeNq;

            var activationR = TrailingStopActivationR <= 0m ? 1m : TrailingStopActivationR;
            var activationPrice = plan.Side == "LONG"
                ? plan.Entry + plan.InitialRiskPoints * activationR
                : plan.Entry - plan.InitialRiskPoints * activationR;

            // 尚未到达激活价格
            if (plan.Side == "LONG" && cur.High < activationPrice) return;
            if (plan.Side == "SHORT" && cur.Low > activationPrice) return;

            // 获取最新的 swing 参考点
            var bufferTicks = Math.Max(0, TrailingStopBufferTicks);
            var buffer = bufferTicks * tick;

            decimal newStopRaw;
            string trailSrc;

            if (plan.Side == "LONG")
            {
                if (_lastM5StructureSnapshot?.LastSwingLow is null)
                    return;

                var swingLow = _lastM5StructureSnapshot.LastSwingLow.Price;
                newStopRaw = swingLow - buffer;
                trailSrc = $"TrailSL@{swingLow:0.########}";
            }
            else
            {
                if (_lastM5StructureSnapshot?.LastSwingHigh is null)
                    return;

                var swingHigh = _lastM5StructureSnapshot.LastSwingHigh.Price;
                newStopRaw = swingHigh + buffer;
                trailSrc = $"TrailSH@{swingHigh:0.########}";
            }

            var newStop = AlignStopToTick(newStopRaw, plan.Side == "LONG", bar);

            // 防止追踪止损离当前价太近：新止损必须在距离当前收盘价至少 TrailStopMinDistFromCurrentTicks 之外
            if (TrailStopMinDistFromCurrentTicks > 0)
            {
                var minDist = TrailStopMinDistFromCurrentTicks * tick;
                if (plan.Side == "LONG" && newStop > cur.Close - minDist)
                {
                    AppendLog($"TRAIL_SKIP bar={bar} side=LONG reason=StopTooClose newStop={newStop:0.########} curClose={cur.Close:0.########} minDist={minDist:0.########}");
                    return;
                }
                if (plan.Side == "SHORT" && newStop < cur.Close + minDist)
                {
                    AppendLog($"TRAIL_SKIP bar={bar} side=SHORT reason=StopTooClose newStop={newStop:0.########} curClose={cur.Close:0.########} minDist={minDist:0.########}");
                    return;
                }
            }

            // 只收紧，不放宽
            if (plan.Side == "LONG" && newStop <= plan.Stop) return;
            if (plan.Side == "SHORT" && newStop >= plan.Stop) return;

            plan.Stop = newStop;
            AppendLog($"PLAN_TRAIL_STOP bar={bar} side={plan.Side} newSL={newStop:0.########} src={trailSrc} mode={(EnableLiveOrders ? "LIVE" : "VIRTUAL")}");

            if (EnableLiveOrders && _live is not null && _live.StopOrder is not null)
            {
                var old = _live.StopOrder;
                var neu = old.Clone();
                neu.TriggerPrice = newStop;
                neu.TriggerPriceType = LiveTriggerPriceType;

                EnqueueOrderAction("Trail_ModifyStop", async () =>
                {
                    await ModifyOrderAsync(old, neu);
                });

                _live.StopPrice = newStop;
            }

            // 动态止盈：Trailing Stop 移动后，同步跟进TP保持在 TrailStop + TrailBufferR 的距离
            if (DynamicTPTrailBufferR > 0m && plan.BreakEvenMoved && EnableDynamicTPTightening)
            {
                var tickTp = GetTickSize(bar, "TrailTP.tick");
                if (tickTp > 0m && plan.InitialRiskPoints > 0m)
                {
                    var riskTicks = (int)Math.Round(plan.InitialRiskPoints / tickTp);
                    if (riskTicks < 1) riskTicks = 1;
                    var bufferPointsTp = riskTicks * DynamicTPTrailBufferR * tickTp;
                    var newTP = plan.Side == "LONG"
                        ? plan.Stop + bufferPointsTp
                        : plan.Stop - bufferPointsTp;

                    // 只收紧（LONG:新TP <= 旧TP, SHORT:新TP >= 旧TP）
                    var tightened = plan.Side == "LONG"
                        ? newTP < plan.Target
                        : newTP > plan.Target;

                    if (tightened)
                    {
                        plan.Target = newTP;
                        AppendLog($"PLAN_TRAIL_TP bar={bar} side={plan.Side} newTP={newTP:0.########} stop={plan.Stop:0.########} bufferR={DynamicTPTrailBufferR} mode={(EnableLiveOrders ? "LIVE" : "VIRTUAL")}");

                        if (EnableLiveOrders && _live is not null && _live.TargetOrder is not null)
                        {
                            var old = _live.TargetOrder;
                            if (IsLiveOrderModifiable(old, "TrailTP_ModifyTarget"))
                            {
                                var neu = old.Clone();
                                neu.Price = newTP;
                                EnqueueOrderAction("TrailTP_ModifyTarget", async () =>
                                {
                                    await ModifyOrderAsync(old, neu);
                                });
                                _live.TargetPrice = newTP;
                            }
                        }
                    }
                }
            }
        }

        private bool IsLiveOrderModifiable(Order order, string tag)
        {
            var state = order.State.ToString();
            if (string.Equals(state, "Active", StringComparison.OrdinalIgnoreCase))
                return true;

            AppendLog($"LIVE_MODIFY_SKIP tag={tag} extId={order.ExtId} state={state} type={order.Type} price={order.Price:0.########} trig={order.TriggerPrice:0.########}");
            return false;
        }

        private void ExitPlan(int bar, string reason, decimal exitPrice, DateTime exitTime)
        {
            if (_activePlan is null)
                return;

            _activePlan.ExitBar = bar;
            _activePlan.ExitReason = reason;
            _activePlan.ExitPrice = exitPrice;

            var exitClass = ClassifyExit(_activePlan, reason, exitPrice);
            AppendLog($"PLAN_EXIT bar={bar} reason={reason} exitClass={exitClass} exit={exitPrice:0.########} side={_activePlan.Side} entry={_activePlan.Entry:0.########} stop={_activePlan.Stop:0.########} tp={_activePlan.Target:0.########} mode={(EnableLiveOrders ? "LIVE" : "VIRTUAL")}");

            AddTradeRecord(_activePlan, bar, exitPrice, reason);

            // HUD 风格日汇总
            DateTime hudTodayTime;
            var hudCandle2 = GetCandle(bar);
            if (hudCandle2 is not null)
                hudTodayTime = hudCandle2.Time;
            else if (_lastCandleTime != default)
                hudTodayTime = _lastCandleTime;
            else
                goto skipHudSummary;

            var yesterday = hudTodayTime.Hour >= 6 ? hudTodayTime.Date : hudTodayTime.Date.AddDays(-1);
            if (_tradeHistory.Count > 0)
                AppendLog($"HUD_SUMMARY date={yesterday:yyyy-MM-dd} trades={_tradeHistory.Count} W{_tradeWins}L{_tradeLosses} NetR={_netR:F2} Net$={_netPnLDollar:F2}");
skipHudSummary:

            _planState = PlanState.Flat;
            TryWriteTradeCsv(bar, exitPrice, exitTime);

            if (EnableCooldown)
                _cooldownUntilBar = Math.Max(_cooldownUntilBar, bar + Math.Max(0, CooldownBars));
        }


        private void TryWriteTradeCsv(int bar, decimal exitPrice, DateTime exitTime)
        {
            try
            {
                if (_activePlan is null) return;
                var plan = _activePlan;
                EnsureLogInitialized();
                var csvPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(_logPath!)!, "NQOrderFlowV1_trades.csv");
                var mode = EnableLiveOrders ? "LIVE" : "VIRTUAL";
                var risk = Math.Abs(plan.Entry - plan.InitialStop);
                if (risk <= 0m) risk = plan.InitialRiskPoints;
                var r = risk > 0m ? (plan.Side == "LONG" ? (exitPrice - plan.Entry) / risk : (plan.Entry - exitPrice) / risk) : 0m;
                var tSz = GetTickSize(bar, "csv.pnl");
                var pnlDollar = tSz > 0m ? Math.Round(r * (risk / tSz) * (TickValuePerContract <= 0m ? 0.5m : TickValuePerContract) * Math.Max(1, Contracts), (int)2) : 0m;
                var exitClass = ClassifyExitFromR(plan.ExitReason ?? "-", r);
                var csvLine = string.Join(",",
                    CsvCell(exitTime.ToString("yyyy-MM-dd HH:mm:ss")),
                    CsvCell(bar.ToString()),
                    CsvCell(plan.Side),
                    CsvCell(plan.Entry.ToString("F2")),
                    CsvCell(plan.Stop.ToString("F2")),
                    CsvCell(plan.Target.ToString("F2")),
                    CsvCell(exitPrice.ToString("F2")),
                    CsvCell(plan.ExitReason ?? "-"),
                    CsvCell(exitClass),
                    CsvCell(risk.ToString("F2")),
                    CsvCell(r.ToString("F2")),
                    CsvCell(pnlDollar.ToString("F2")),
                    CsvCell(mode),
                    CsvCell(plan.OfScore.ToString()),
                    CsvCell(plan.OfSoftened ? "true" : "false"),
                    CsvCell(plan.OfMismatchBars.ToString()),
                    CsvCell(NormalizeOfTextForCsv(plan.OfText)),
                    CsvCell(plan.Zone.Type.ToString()),
                    CsvCell(plan.Zone.Low.ToString("F2")),
                    CsvCell(plan.Zone.High.ToString("F2"))
                );

                var lines = new List<string>(_pendingTradeCsvLines.Count + 1);
                lines.AddRange(_pendingTradeCsvLines);
                lines.Add(csvLine);

                var header = "Time,Bar,Side,Entry,Stop,TP,ExitPrice,ExitReason,ExitClass,RiskPts,R,PnL$,Mode,OFScore,OFSoftened,OFMismatchBars,OFText,ZoneType,ZoneLow,ZoneHigh";
                if (TryAppendTradeCsvLines(csvPath, header, lines, bar))
                {
                    if (_pendingTradeCsvLines.Count > 0)
                        AppendLog($"CSV_PENDING_FLUSHED bar={bar} count={_pendingTradeCsvLines.Count}");
                    _pendingTradeCsvLines.Clear();
                }
                else
                {
                    _pendingTradeCsvLines.Add(csvLine);
                    AppendLog($"CSV_WRITE_PENDING bar={bar} pending={_pendingTradeCsvLines.Count}");
                }
            }
            catch (Exception ex) { AppendLog($"CSV_WRITE_ERR bar={bar} err={ex.ToString()}"); }
        }

        private bool TryAppendTradeCsvLines(string csvPath, string header, IReadOnlyList<string> lines, int bar)
        {
            const int maxAttempts = 5;

            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    var exists = File.Exists(csvPath);
                    using var stream = new FileStream(csvPath, FileMode.Append, FileAccess.Write, FileShare.Read);
                    using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

                    if (!exists)
                        writer.WriteLine(header);

                    foreach (var line in lines)
                        writer.WriteLine(line);

                    return true;
                }
                catch (IOException ex) when (attempt < maxAttempts)
                {
                    AppendLog($"CSV_WRITE_RETRY bar={bar} attempt={attempt}/{maxAttempts} err={ex.Message}");
                    Thread.Sleep(100);
                }
            }

            return false;
        }
        private void AddTradeRecord(TradePlan plan, int exitBar, decimal exitPrice, string exitReason)
        {
            EnsureInstrumentRulesInitialized(exitBar, "AddTradeRecord");

            var tick = GetTickSize(exitBar, "AddTradeRecord.tick");
            if (tick <= 0m) tick = TickSizeNq;

            var risk = Math.Abs(plan.Entry - plan.InitialStop);
            if (risk <= 0m)
                risk = plan.InitialRiskPoints;

            decimal r = 0m;
            if (risk > 0m)
            {
                if (plan.Side == "LONG")
                    r = (exitPrice - plan.Entry) / risk;
                else
                    r = (plan.Entry - exitPrice) / risk;
            }

            if (r > 0m) _tradeWins++;
            else if (r < 0m) _tradeLosses++;

            _netR += r;

            var contracts = Math.Max(1, Contracts);
            var tickValue = TickValuePerContract <= 0m ? 0.5m : TickValuePerContract;

            var riskTicks = (tick <= 0m) ? 0m : (risk / tick);
            var riskDollar = riskTicks * tickValue * contracts;
            var pnlDollar = r * riskDollar;

            DateTime exitTime;
            var exitCandle = GetCandle(exitBar);
            if (exitCandle is not null)
                exitTime = exitCandle.Time;
            else
                exitTime = DateTime.Now;

            _netPnLDollar += pnlDollar;

            var exitClass = ClassifyExitFromR(exitReason, r);
            UpdateDailyGuard(exitTime, pnlDollar, exitClass);

            _tradeHistory.Add(new TradeRecord
            {
                EntryBar = plan.CreatedBar,
                ExitBar = exitBar,
                Side = plan.Side,
                Entry = plan.Entry,
                Stop = plan.InitialStop,
                Target = plan.Target,
                Exit = exitPrice,
                R = r,
                RiskDollar = riskDollar,
                PnLDollar = pnlDollar,
                ExitTime = exitTime,
                ExitReason = exitReason,
                ExitClass = exitClass,
                OfScore = plan.OfScore,
                OfText = plan.OfText,
                OfSoftened = plan.OfSoftened,
                OfMismatchBars = plan.OfMismatchBars,
                Zone = plan.Zone
            });

            if (_tradeHistory.Count > 500)
                _tradeHistory.RemoveRange(0, _tradeHistory.Count - 500);
        }

        private void LogWideRiskIfNeeded(int bar, TradePlan plan)
        {
            var riskPts = Math.Abs(plan.Entry - plan.InitialStop);
            if (riskPts < 30m)
                return;

            var tick = GetTickSize(bar, "WideRiskLog.tick");
            var riskTicks = tick > 0m ? riskPts / tick : 0m;
            AppendLog($"RISK_WIDE_ENTRY bar={bar} side={plan.Side} riskPts={riskPts:0.##} riskTicks={riskTicks:0.##} entry={plan.Entry:0.########} stop={plan.InitialStop:0.########} zone={FormatZoneKeyCN(plan.Zone)} of={plan.OfScore}/4 ofSoftened={plan.OfSoftened} mismatchBars={plan.OfMismatchBars}");
        }

        private bool ShouldBlockEntryByV128Filters(
            int bar,
            string side,
            decimal riskPoints,
            ZoneKey? zoneKey,
            string mode,
            bool ofSoftened,
            int mismatchBars)
        {
            if (EnableMaxRiskPointsFilter)
            {
                var maxRisk = Math.Max(0m, MaxEntryRiskPoints);
                if (maxRisk > 0m && riskPoints >= maxRisk)
                {
                    AppendLog($"RISK_WIDE_BLOCK bar={bar} side={side} riskPts={riskPoints:0.##} max={maxRisk:0.##} zone={(zoneKey is null ? "-" : FormatZoneKeyCN(zoneKey))} mode={mode} ofSoftened={ofSoftened} mismatchBars={mismatchBars}");
                    SetBlock(bar, TriggerBlockReason.RiskTicksTooLarge, $"RiskPts {riskPoints:0.##} >= max {maxRisk:0.##}");
                    ResetConfirm(bar, "RiskWideBlocked");
                    _phase = ConfirmPhase.WaitZoneTouch;
                    return true;
                }
            }

            if (EnableStrictShortSoftenedFilter &&
                string.Equals(side, "SHORT", StringComparison.OrdinalIgnoreCase) &&
                ofSoftened)
            {
                var maxBars = Math.Max(0, MaxShortSoftenedMismatchBars);
                if (maxBars > 0 && mismatchBars >= maxBars)
                {
                    AppendLog($"SHORT_SOFTENED_BLOCK bar={bar} side={side} mismatchBars={mismatchBars} max={maxBars} zone={(zoneKey is null ? "-" : FormatZoneKeyCN(zoneKey))} mode={mode} riskPts={riskPoints:0.##}");
                    SetBlock(bar, TriggerBlockReason.EntryTooFarFromZone, $"SHORT softened mismatchBars {mismatchBars} >= max {maxBars}");
                    ResetConfirm(bar, "ShortSoftenedBlocked");
                    _phase = ConfirmPhase.WaitZoneTouch;
                    return true;
                }
            }

            if (EnableStrictLongSoftenedFilter &&
                string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase) &&
                ofSoftened)
            {
                var exactBars = Math.Max(0, BlockLongSoftenedExactMismatchBars);
                var highBars = Math.Max(0, MinLongSoftenedHighMismatchBars);
                var exactBlocked = exactBars > 0 && mismatchBars == exactBars;
                var highBlocked = highBars > 0 && mismatchBars >= highBars;

                if (exactBlocked || highBlocked)
                {
                    var rule = exactBlocked ? $"== {exactBars}" : $">= {highBars}";
                    AppendLog($"LONG_SOFTENED_BLOCK bar={bar} side={side} mismatchBars={mismatchBars} rule={rule} zone={(zoneKey is null ? "-" : FormatZoneKeyCN(zoneKey))} mode={mode} riskPts={riskPoints:0.##}");
                    SetBlock(bar, TriggerBlockReason.EntryTooFarFromZone, $"LONG softened mismatchBars {mismatchBars} {rule}");
                    ResetConfirm(bar, "LongSoftenedBlocked");
                    _phase = ConfirmPhase.WaitZoneTouch;
                    return true;
                }
            }

            return false;
        }

        private bool ShouldBlockByDailyGuard(int bar, DateTime time)
        {
            if (!EnableDailyGuard)
                return false;

            EnsureDailyGuardDate(time);

            var profitTarget = Math.Max(0m, DailyProfitTargetDollars);
            if (profitTarget > 0m && _dailyGuardPnLDollar >= profitTarget)
            {
                AppendLog($"DAILY_GUARD_BLOCK bar={bar} reason=ProfitTarget pnl={_dailyGuardPnLDollar:0.##} target={profitTarget:0.##} trades={_dailyGuardTrades} losses={_dailyGuardLosses}");
                SetBlock(bar, TriggerBlockReason.DailyGuardBlocked, $"Daily profit target hit ${_dailyGuardPnLDollar:0.##} >= ${profitTarget:0.##}");
                ResetConfirm(bar, "DailyProfitTarget");
                _phase = ConfirmPhase.WaitZoneTouch;
                return true;
            }

            var lossLimit = Math.Max(0m, DailyLossLimitDollars);
            if (lossLimit > 0m && _dailyGuardPnLDollar <= -lossLimit)
            {
                AppendLog($"DAILY_GUARD_BLOCK bar={bar} reason=LossLimit pnl={_dailyGuardPnLDollar:0.##} limit={lossLimit:0.##} trades={_dailyGuardTrades} losses={_dailyGuardLosses}");
                SetBlock(bar, TriggerBlockReason.DailyGuardBlocked, $"Daily loss limit hit ${_dailyGuardPnLDollar:0.##} <= -${lossLimit:0.##}");
                ResetConfirm(bar, "DailyLossLimit");
                _phase = ConfirmPhase.WaitZoneTouch;
                return true;
            }

            var maxTrades = Math.Max(0, MaxTradesPerDay);
            if (maxTrades > 0 && _dailyGuardTrades >= maxTrades)
            {
                AppendLog($"DAILY_GUARD_BLOCK bar={bar} reason=MaxTrades trades={_dailyGuardTrades} max={maxTrades} pnl={_dailyGuardPnLDollar:0.##} losses={_dailyGuardLosses}");
                SetBlock(bar, TriggerBlockReason.DailyGuardBlocked, $"Daily max trades hit {_dailyGuardTrades}/{maxTrades}");
                ResetConfirm(bar, "DailyMaxTrades");
                _phase = ConfirmPhase.WaitZoneTouch;
                return true;
            }

            var maxLosses = Math.Max(0, MaxLossesPerDay);
            if (maxLosses > 0 && _dailyGuardLosses >= maxLosses)
            {
                AppendLog($"DAILY_GUARD_BLOCK bar={bar} reason=MaxLosses losses={_dailyGuardLosses} max={maxLosses} pnl={_dailyGuardPnLDollar:0.##} trades={_dailyGuardTrades}");
                SetBlock(bar, TriggerBlockReason.DailyGuardBlocked, $"Daily max losses hit {_dailyGuardLosses}/{maxLosses}");
                ResetConfirm(bar, "DailyMaxLosses");
                _phase = ConfirmPhase.WaitZoneTouch;
                return true;
            }

            return false;
        }

        private void UpdateDailyGuard(DateTime time, decimal pnlDollar, string exitClass)
        {
            EnsureDailyGuardDate(time);

            _dailyGuardTrades++;
            _dailyGuardPnLDollar += pnlDollar;
            if (string.Equals(exitClass, "FullLoss", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(exitClass, "SmallLoss", StringComparison.OrdinalIgnoreCase))
            {
                _dailyGuardLosses++;
            }

            AppendLog($"DAILY_GUARD_UPDATE date={_dailyGuardDate:yyyy-MM-dd} trades={_dailyGuardTrades} losses={_dailyGuardLosses} pnl={_dailyGuardPnLDollar:0.##} lastPnL={pnlDollar:0.##} exitClass={exitClass}");
        }

        private void EnsureDailyGuardDate(DateTime time)
        {
            var tradingDate = GetTradingDate(time);
            if (_dailyGuardDate == tradingDate)
                return;

            _dailyGuardDate = tradingDate;
            _dailyGuardTrades = 0;
            _dailyGuardLosses = 0;
            _dailyGuardPnLDollar = 0m;
            AppendLog($"DAILY_GUARD_RESET date={_dailyGuardDate:yyyy-MM-dd}");
        }

        private static DateTime GetTradingDate(DateTime time)
            => time.Hour >= 6 ? time.Date : time.Date.AddDays(-1);

        private string ClassifyExit(TradePlan plan, string exitReason, decimal exitPrice)
        {
            var risk = Math.Abs(plan.Entry - plan.InitialStop);
            if (risk <= 0m)
                risk = plan.InitialRiskPoints;

            var r = risk > 0m
                ? (plan.Side == "LONG" ? (exitPrice - plan.Entry) / risk : (plan.Entry - exitPrice) / risk)
                : 0m;

            return ClassifyExitFromR(exitReason, r);
        }

        private static string ClassifyExitFromR(string exitReason, decimal r)
        {
            if (exitReason.StartsWith("TP", StringComparison.OrdinalIgnoreCase))
                return "TP";
            if (r > 0m)
                return "ProtectedProfit";
            if (r <= -0.9m)
                return "FullLoss";
            return "SmallLoss";
        }

        private static string NormalizeOfTextForCsv(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "-";

            return text
                .Replace("ΔDiv", "DeltaDiv", StringComparison.Ordinal)
                .Replace("ExtremeΔ", "ExtremeDelta", StringComparison.Ordinal);
        }

        private static string CsvCell(string value)
        {
            value ??= string.Empty;
            return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        }

        private (decimal Price, ZoneEntryAnchor AnchorUsed, string AnchorText) GetLimitEntryPriceDynamic(int bar, TradingZone zone, bool isLong)
        {
            EnsureInstrumentRulesInitialized(bar, "GetLimitEntryPriceDynamic");

            var widthPts = Math.Abs(zone.High - zone.Low);

            ZoneEntryAnchor anchor;

            if (EnableDynamicEntryAnchor)
            {
                var thTicks = GetDynamicAnchorWidthThresholdTicksIfSet();
                if (thTicks > 0)
                {
                    var tick = GetTickSize(bar, "DynamicAnchor");
                    var widthTicks = tick > 0m ? widthPts / tick : 0m;
                    if (widthTicks > thTicks)
                        anchor = ZoneEntryAnchor.Edge;
                    else
                        anchor = LimitEntryAnchor;
                }
                else
                {
                    var thPts = Math.Max(0m, DynamicAnchorWidthThresholdPoints);
                    if (widthPts > thPts)
                        anchor = ZoneEntryAnchor.Edge;
                    else
                        anchor = LimitEntryAnchor;
                }
            }
            else
            {
                anchor = LimitEntryAnchor;
            }

            if (EnableAutoInnerAnchorWhenMid && anchor == ZoneEntryAnchor.Mid)
            {
                anchor = AutoInnerAnchor == InnerAnchorType.Third
                    ? ZoneEntryAnchor.Third
                    : ZoneEntryAnchor.Quarter;
            }

            if (anchor == ZoneEntryAnchor.Mid)
            {
                var p = AlignEntryLimitToTick(zone.Mid, isLong, bar);
                return (p, ZoneEntryAnchor.Mid, "ZoneMid");
            }

            if (anchor == ZoneEntryAnchor.Edge)
            {
                var raw = isLong ? zone.High : zone.Low;
                var p = AlignEntryLimitToTick(raw, isLong, bar);

                return isLong
                    ? (p, ZoneEntryAnchor.Edge, "ZoneHigh(Edge)")
                    : (p, ZoneEntryAnchor.Edge, "ZoneLow(Edge)");
            }

            decimal ratio = anchor == ZoneEntryAnchor.Third ? 0.3333333333m : 0.25m;

            var rawInner = isLong
                ? (zone.High - widthPts * ratio)
                : (zone.Low + widthPts * ratio);

            var pInner = AlignEntryLimitToTick(rawInner, isLong, bar);

            var tag = anchor == ZoneEntryAnchor.Third ? "ZoneT33" : "ZoneQ25";
            if (EnableAutoInnerAnchorWhenMid && LimitEntryAnchor == ZoneEntryAnchor.Mid)
                tag += "(AutoFromMid)";

            return (pInner, anchor, tag);
        }
    }
}
