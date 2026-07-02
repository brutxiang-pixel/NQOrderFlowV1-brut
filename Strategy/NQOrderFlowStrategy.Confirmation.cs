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
        // CONFIRM
        // =========================
        private void RunM5Confirmation(
            int bar,
            IndicatorCandle curBase,
            IndicatorCandle? prevBase,
            IReadOnlyList<TradingZone> activeZones,
            TrendDirection htfBias)
        {
            EnsureInstrumentRulesInitialized(bar, "RunM5Confirmation");

            var halfTick = GetHalfTick(bar, "RunM5Confirmation");

            if (htfBias == TrendDirection.Neutral)
            {
                ResetConfirm(bar, "HTF Bias中性");
                _phase = ConfirmPhase.WaitHtfTrend;
                return;
            }

            if (_confirmZoneKey is not null && !IsZoneAlignedWithBias(_confirmZoneKey.Type, htfBias))
            {
                ResetConfirm(bar, "HTF Bias反向");
                _phase = ConfirmPhase.WaitZoneTouch;
                return;
            }

            var candidates = activeZones
                .Where(z => !z.IsInvalidated && !z.IsVpRejected)
                .Where(z => IsZoneAlignedWithBias(z.Type, htfBias))
                .Where(z => IsZoneTradeableByQuality(bar, z, out _, out _))
                .ToList();

            TradingZone focusZone;
            ZoneKey focusKey;

            if (_phase is ConfirmPhase.WaitM5Bos or ConfirmPhase.Confirmed)
            {
                if (_confirmZoneKey is null)
                {
                    ResetConfirm(bar, "无锁定区块");
                    _phase = ConfirmPhase.WaitZoneTouch;
                    return;
                }

                if (!TryGetZoneByKeyWithShadow(activeZones, _confirmZoneKey, out var lockedZone, out var usedShadow))
                {
                    ResetConfirm(bar, "锁定区块失效/消失");
                    _phase = ConfirmPhase.WaitZoneTouch;
                    return;
                }

                if (lockedZone.IsInvalidated || lockedZone.IsVpRejected)
                {
                    ResetConfirm(bar, "锁定区块失效/VP拒绝");
                    _phase = ConfirmPhase.WaitZoneTouch;
                    return;
                }

                if (DisallowMitigatedZones && lockedZone.IsMitigated)
                {
                    var armedOrPending = _ofArmed || _pendingEntry is not null || _live is not null;

                    if (!armedOrPending)
                    {
                        ResetConfirm(bar, "Mitigated硬过滤(未Armed)");
                        _phase = ConfirmPhase.WaitZoneTouch;
                        return;
                    }

                    if (!_loggedMitigatedKeepOnce)
                    {
                        _loggedMitigatedKeepOnce = true;
                        AppendLog($"ZONE_BECAME_MITIGATED bar={bar} phase={_phase} armed={_ofArmed} pending={(_pendingEntry is null ? "-" : _pendingEntry.LimitPrice.ToString("0.00"))} zone={lockedZone.ToShortText()} -> keep setup");
                    }
                }

                focusZone = lockedZone;
                focusKey = _confirmZoneKey;
                _confirmZoneUsingShadow = usedShadow;
            }
            else
            {
                if (candidates.Count == 0)
                {
                    ResetConfirm(bar, "无可用区块(质量过滤后为空)");
                    _phase = ConfirmPhase.WaitZoneTouch;
                    return;
                }

                focusZone = candidates.OrderBy(z => Math.Abs(curBase.Close - z.Mid)).First();
                // 2.2 区块质量：同K线创建的区块太新，不交易
                if (bar <= focusZone.CreatedBar)
                {
                    ResetConfirm(bar, "区块太新(同K线创建)");
                    _phase = ConfirmPhase.WaitZoneTouch;
                    return;
                }
                // 2.3 HTF偏差新鲜度：BOS后至少等2根HTF收盘稳定再交易
                if (_htfBosHtfIndex >= 0)
                {
                    var htfIdx = _htfSeries.Count - 1;
                    if (htfIdx - _htfBosHtfIndex < 2)
                    {
                        ResetConfirm(bar, "偏差太新(BOS未稳定2根HTF)");
                        _phase = ConfirmPhase.WaitZoneTouch;
                        return;
                    }
                }
                focusKey = new ZoneKey(focusZone.Type, focusZone.StartBar, focusZone.CreatedBar, focusZone.Low, focusZone.High);
                _confirmZoneUsingShadow = false;
            }

            var inZone = curBase.High >= focusZone.Low - halfTick && curBase.Low <= focusZone.High + halfTick;

            if (_phase is ConfirmPhase.WaitM5Bos)
            {
                if (inZone) _outOfZoneBars = 0;
                else _outOfZoneBars++;

                if (_outOfZoneBars >= OutOfZoneResetBars)
                {
                    ResetConfirm(bar, "离开区块太久(确认前)");
                    _phase = ConfirmPhase.WaitZoneTouch;
                    return;
                }
            }

            switch (_phase)
            {
                case ConfirmPhase.WaitHtfTrend:
                case ConfirmPhase.WaitZoneTouch:
                    _confirmZoneKey = focusKey;
                    _confirmZoneShadow = CloneZone(focusZone);
                    _loggedShadowUseOnce = false;

                    _ = TryEvaluateZoneQualityScore(bar, focusZone, out _lockedZoneQualityScore, out _lockedZoneQualityDetail);

                    _confirmText = inZone ? "已进入区块" : "等待触碰";

                    if (inZone)
                    {
                        _confirmStartBar = bar;
                        _outOfZoneBars = 0;
                        _confirmedAtBar = -1;

                        _ofArmed = false;
                        _ofArmedAtBar = -1;
                        _ofArmedScore = 0;
                        _ofArmedBullWeight = 0;
                        _ofArmedBearWeight = 0;
                        _ofDirMismatchLogged = false;
                        _ofArmedText = "-";
                        _pendingEntry = null;

                        _loggedMitigatedKeepOnce = false;

                        if (ConfirmationMode == ConfirmMode.SwingBreak)
                        {
                            CaptureReferenceSwing(htfBias, bar, curBase.Close, forceRefresh: true);
                            _confirmText = "已触碰：等待突破参考Swing（close确认）";
                        }
                        else
                        {
                            _confirmRefPrice = null;
                            _confirmRefBar = -1;
                            _confirmRefIsFallback = false;

                            var dispTicks = GetConfirmDisplacementTicksIfSet();
                            if (dispTicks > 0)
                                _confirmRefText = $"Disp>={dispTicks}t";
                            else
                                _confirmRefText = $"Disp>{Math.Max(0m, ConfirmDisplacementPoints):0.##}pt(LEGACY)";

                            _confirmText = "已触碰：等待区块反应（位移确认）";
                        }

                        _phase = ConfirmPhase.WaitM5Bos;
                    }
                    else
                    {
                        _phase = ConfirmPhase.WaitZoneTouch;
                    }
                    break;

                case ConfirmPhase.WaitM5Bos:
                    if (_confirmStartBar >= 0 && (bar - _confirmStartBar) > MaxConfirmBars)
                    {
                        ResetConfirm(bar, "确认超时");
                        _phase = ConfirmPhase.WaitZoneTouch;
                        break;
                    }

                    if (ConfirmationMode == ConfirmMode.ZoneDisplacement)
                    {
                        var dispPtsEff = GetConfirmDisplacementPointsEff(bar);

                        var confirmed =
                            htfBias == TrendDirection.Bullish
                                ? (curBase.Close >= focusZone.High + dispPtsEff)
                                : (curBase.Close <= focusZone.Low - dispPtsEff);

                        if (confirmed)
                        {
                            _phase = ConfirmPhase.Confirmed;
                            _confirmedAtBar = bar;
                            _confirmText = $"确认完成：位移确认({_confirmRefText})";
                        }
                        else
                        {
                            _confirmText = inZone ? "等待位移确认（在区内）" : "等待位移确认（已锁定区块）";
                        }

                        break;
                    }

                    if (_confirmRefPrice is null ||
                        IsRefUnacceptable(htfBias, bar, curBase.Close, _confirmRefPrice.Value, _confirmRefBar))
                    {
                        CaptureReferenceSwing(htfBias, bar, curBase.Close, forceRefresh: true);
                    }

                    if (_confirmRefPrice is null)
                    {
                        _confirmText = "等待参考Swing形成（暂无可用Swing）";
                        break;
                    }

                    if (prevBase is null)
                    {
                        _confirmText = "等待突破（prev为空）";
                        break;
                    }

                    var refP = _confirmRefPrice.Value;

                    var swingConfirmed =
                        htfBias == TrendDirection.Bullish
                            ? (prevBase.Close <= refP && curBase.Close > refP)
                            : (prevBase.Close >= refP && curBase.Close < refP);

                    if (swingConfirmed)
                    {
                        _phase = ConfirmPhase.Confirmed;
                        _confirmedAtBar = bar;
                        _confirmText = $"确认完成：突破{_confirmRefText}";
                    }
                    else
                    {
                        _confirmText = inZone ? "等待突破参考Swing（在区内）" : "等待突破参考Swing（已锁定区块）";
                    }

                    break;

                case ConfirmPhase.Confirmed:
                    _confirmText = _ofArmed
                        ? "Confirmed+Armed（等待回撤成交）"
                        : "Confirmed（等待OF出现以武装）";
                    break;
            }
        }

        private void CaptureReferenceSwing(TrendDirection htfBias, int bar, decimal curClose, bool forceRefresh)
        {
            if (!forceRefresh && _confirmRefPrice is not null)
                return;

            _confirmRefPrice = null;
            _confirmRefBar = -1;
            _confirmRefText = "-";
            _confirmRefIsFallback = false;

            if (_lastM5StructureSnapshot is null)
                return;

            if (htfBias == TrendDirection.Bullish)
            {
                var sh = _lastM5StructureSnapshot.LastSwingHigh;
                if (sh is not null && !IsRefUnacceptable(htfBias, bar, curClose, sh.Price, sh.Bar))
                {
                    _confirmRefPrice = sh.Price;
                    _confirmRefBar = sh.Bar;
                    _confirmRefText = $"SH @{sh.Price:0.00}";
                    return;
                }

                if (EnableRefSwingFallback)
                {
                    if (TryGetRollingHigh(bar, Math.Max(2, RefFallbackLookbackBars), out var rhPrice, out var rhBar))
                    {
                        _confirmRefPrice = rhPrice;
                        _confirmRefBar = rhBar;
                        _confirmRefText = $"RH{Math.Max(2, RefFallbackLookbackBars)} @{rhPrice:0.00}";
                        _confirmRefIsFallback = true;
                        return;
                    }
                }
            }
            else if (htfBias == TrendDirection.Bearish)
            {
                var sl = _lastM5StructureSnapshot.LastSwingLow;
                if (sl is not null && !IsRefUnacceptable(htfBias, bar, curClose, sl.Price, sl.Bar))
                {
                    _confirmRefPrice = sl.Price;
                    _confirmRefBar = sl.Bar;
                    _confirmRefText = $"SL @{sl.Price:0.00}";
                    return;
                }

                if (EnableRefSwingFallback)
                {
                    if (TryGetRollingLow(bar, Math.Max(2, RefFallbackLookbackBars), out var rlPrice, out var rlBar))
                    {
                        _confirmRefPrice = rlPrice;
                        _confirmRefBar = rlBar;
                        _confirmRefText = $"RL{Math.Max(2, RefFallbackLookbackBars)} @{rlPrice:0.00}";
                        _confirmRefIsFallback = true;
                        return;
                    }
                }
            }
        }

        private bool IsRefUnacceptable(TrendDirection bias, int curBar, decimal curClose, decimal refPrice, int refBar)
        {
            var maxAge = Math.Max(0, RefMaxAgeBars);

            if (refBar >= 0 && maxAge > 0 && (curBar - refBar) > maxAge)
                return true;

            var tick = GetTickSize(curBar, "IsRefUnacceptable");
            if (tick <= 0m)
                tick = TickSizeNq;

            // ticks 优先
            var maxDistTicks = GetRefMaxDistanceTicksIfSet();
            if (maxDistTicks > 0)
            {
                if (bias == TrendDirection.Bullish)
                {
                    var distTicks = (refPrice - curClose) / tick;
                    if (distTicks > maxDistTicks)
                        return true;
                }
                else if (bias == TrendDirection.Bearish)
                {
                    var distTicks = (curClose - refPrice) / tick;
                    if (distTicks > maxDistTicks)
                        return true;
                }

                return false;
            }

            // legacy points
            var maxDistPts = Math.Max(0m, RefMaxDistancePoints);
            if (maxDistPts <= 0m)
                return false;

            if (bias == TrendDirection.Bullish)
            {
                var dist = refPrice - curClose;
                if (dist > maxDistPts)
                    return true;
            }
            else if (bias == TrendDirection.Bearish)
            {
                var dist = curClose - refPrice;
                if (dist > maxDistPts)
                    return true;
            }

            return false;
        }

        private bool TryGetRollingHigh(int curBar, int lookbackBars, out decimal price, out int priceBar)
        {
            price = 0m;
            priceBar = -1;

            if (lookbackBars <= 0)
                return false;

            var end = curBar - 1;
            if (end < 0)
                return false;

            var start = Math.Max(0, end - lookbackBars + 1);

            var max = decimal.MinValue;
            var maxBar = -1;

            for (var i = start; i <= end; i++)
            {
                var c = GetCandle(i);
                if (c is null)
                    continue;

                if (c.High > max)
                {
                    max = c.High;
                    maxBar = i;
                }
            }

            if (maxBar < 0 || max == decimal.MinValue)
                return false;

            price = max;
            priceBar = maxBar;
            return true;
        }

        private bool TryGetRollingLow(int curBar, int lookbackBars, out decimal price, out int priceBar)
        {
            price = 0m;
            priceBar = -1;

            if (lookbackBars <= 0)
                return false;

            var end = curBar - 1;
            if (end < 0)
                return false;

            var start = Math.Max(0, end - lookbackBars + 1);

            var min = decimal.MaxValue;
            var minBar = -1;

            for (var i = start; i <= end; i++)
            {
                var c = GetCandle(i);
                if (c is null)
                    continue;

                if (c.Low < min)
                {
                    min = c.Low;
                    minBar = i;
                }
            }

            if (minBar < 0 || min == decimal.MaxValue)
                return false;

            price = min;
            priceBar = minBar;
            return true;
        }

        private static bool IsZoneAlignedWithBias(ZoneType t, TrendDirection bias)
        {
            if (bias == TrendDirection.Bullish)
                return t == ZoneType.BullishOB || t == ZoneType.BullishFVG;

            if (bias == TrendDirection.Bearish)
                return t == ZoneType.BearishOB || t == ZoneType.BearishFVG;

            return false;
        }

        private static bool ZoneKeyEquals(ZoneKey a, ZoneKey b)
            => a.Type == b.Type && a.StartBar == b.StartBar && a.CreatedBar == b.CreatedBar && a.Low == b.Low && a.High == b.High;

        private void ResetConfirm(string reason) => ResetConfirm(-1, reason);

        private void ResetConfirm(int bar, string reason)
        {
            if (_pendingEntry is not null && !_pendingEntry.CancelLogged)
            {
                var waited = bar >= 0 ? Math.Max(0, bar - _pendingEntry.CreatedBar) : -1;
                var waitedText = waited >= 0 ? waited.ToString() : "?";

                AppendLog(
                    $"ENTRY_ORDER_CANCEL bar={bar} reason=ResetConfirm({reason}) waited={waitedText}/{_pendingEntry.EffectiveMaxWaitBars} " +
                    $"limit={_pendingEntry.LimitPrice:0.00} anchor={_pendingEntry.AnchorText} side={_pendingEntry.Side} qLock={(_pendingEntry.LockedQScore < 0 ? "-" : _pendingEntry.LockedQScore.ToString())}/10 " +
                    $"zone={FormatZoneKeyCN(_pendingEntry.Zone)}");
            }

            if (EnableLiveOrders && _planState == PlanState.Flat && _live is not null)
            {
                // 当且仅当：M5收盘失效 + 关闭开关 => 不撤单，保留挂单继续等待
                var isM5CloseInvalid = string.Equals(reason, ResetReason_M5CloseZoneInvalid, StringComparison.Ordinal);
                if (isM5CloseInvalid && !CancelLiveOnM5CloseZoneInvalidation)
                {
                    var liveKeep = _live; // 注意：不要 _live=null
                    AppendLog($"LIVE_RESETCONFIRM_SKIP bar={bar} reason={reason} keepOrder=1 tradeId={liveKeep.TradeId} entryFilled={liveKeep.EntryFilledQty:0.##}");
                }
                else
                {
                    var live = _live;
                    _live = null;

                    AppendLog($"LIVE_RESETCONFIRM_CANCEL bar={bar} reason={reason} tradeId={live.TradeId} entryFilled={live.EntryFilledQty:0.##}");

                    EnqueueOrderAction("ResetConfirmCancelLive", async () =>
                    {
                        await TryCancelOrderSafeAsync(live.EntryOrder, $"ResetConfirm({reason})");
                        await TryCancelOrderSafeAsync(live.StopOrder, $"ResetConfirm({reason})");
                        await TryCancelOrderSafeAsync(live.TargetOrder, $"ResetConfirm({reason})");
                    });
                }
            }

            _confirmZoneKey = null;
            _confirmStartBar = -1;

            _outOfZoneBars = 0;
            _confirmedAtBar = -1;

            _confirmRefPrice = null;
            _confirmRefBar = -1;
            _confirmRefText = "-";
            _confirmRefIsFallback = false;

            _confirmText = $"重置:{reason}";

            _confirmZoneShadow = null;
            _confirmZoneUsingShadow = false;
            _loggedShadowUseOnce = false;

            _ofArmed = false;
            _ofArmedAtBar = -1;
            _ofArmedScore = 0;
            _ofArmedBullWeight = 0;
            _ofArmedBearWeight = 0;
            _ofDirMismatchLogged = false;
            _ofArmedText = "-";
            _pendingEntry = null;
            _retraceEntryBar = -1;

            _lockedZoneQualityScore = -1;
            _lockedZoneQualityDetail = "-";

            _loggedMitigatedKeepOnce = false;
        }
    }
}
