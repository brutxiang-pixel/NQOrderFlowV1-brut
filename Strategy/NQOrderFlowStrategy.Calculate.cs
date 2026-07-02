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
        protected override void OnCalculate(int bar, decimal value)
        {
            _lastCalcBar = bar;

            var cur = GetCandle(bar);
            if (cur is null)
                return;

            // 数据断层检测：超过30分钟的K线间隔视为数据断层，跳过处理
            if (_lastCandleTime != default)
            {
                var gap = (cur.Time - _lastCandleTime).TotalMinutes;
                if (gap > 30)
                {
                    _lastCandleTime = cur.Time;
                    AppendLog($"GAP_DETECTED bar={bar} gapMinutes={gap:F0} close={cur.Close:0.########} -> skip");
                    return;
                }
            }
            _lastCandleTime = cur.Time;

            EnsureLogInitialized();
            EnsureInstrumentRulesInitialized(bar, "OnCalculate");

            var curTime = TryGetBaseCandleTime(cur);
            if (curTime is not null && _lastBaseTime is not null && curTime.Value < _lastBaseTime.Value)
                ResetAllState("Replay时间倒退（自动清空统计/引擎状态）");

            if (curTime is not null)
                _lastBaseTime = curTime;

            ResetBarBlock();
            MaybeStartLiveSelfTest(bar, cur);

            var prev = bar > 0 ? GetCandle(bar - 1) : null;

            var tick = GetTickSize(bar, "OnCalculate.tick");

            _probe ??= new DataProbeService(GetCandle);

            _htfAgg ??= new HigherTimeframeAggregator(
                getBaseCandle: GetCandle,
                targetMinutes: HtfMinutes,
                tickSize: tick,
                valueAreaPercent: 0.70m);

            _htfStructure ??= new HtfStructureEngine(pivotLength: PivotLength);

            _htfZones ??= new HtfZoneEngine(
                tickSize: tick,
                lookback: ObLookback,
                minFvgTicks: MinFvgTicks,
                vpMaxDistancePoints: GetVpMaxDistancePointsEff(bar));

            _orderFlow ??= new OrderFlowEngine(GetCandle, tickSize: tick);

            _m5Structure ??= new StructureEngine(GetCandle, pivotLength: 1);
            var m5Struct = _m5Structure.Update(bar);
            _lastM5StructureSnapshot = m5Struct.Snapshot;

            // ===== M5 zones engine =====
            _m5Zones ??= new M5ZoneEngine(GetCandle, tickSize: tick);

            // apply params each bar (safe for live tweaking)
            ApplyM5ZoneParamsToEngine(bar, _m5Zones);

            // update M5 zones
            var m5ZoneResult = _m5Zones.Update(bar);
            var activeM5 = _m5Zones.GetActiveZones();

            // ===== HTF zones lifecycle update (touch/mitigated/vp reject) =====
            _htfZones.UpdateByBaseBar(
                prevBase: prev,
                curBase: cur,
                getVpCandle: () => _lastClosedVpCandle);

            var didHtfClose = _htfAgg.IsBucketCloseBar(bar);

            if (didHtfClose && _htfAgg.TryBuildBucketCandle(bar, out var htf))
            {
                UpsertHtfCandle(htf);
                var htfIndex = _htfSeries.Count - 1;

                UpdateConfirmShadowInvalidationByHtfClose(htfIndex);

                var htfStructureResult = _htfStructure.Update(htfIndex, _htfSeries);
                UpdateHtfBiasFromStructure(htfIndex, htfStructureResult);

                _ = _htfZones.OnHtfClosed(htfIndex, _htfSeries, htfStructureResult);

                _lastClosedVpCandle = htf;
            }

            var activeHtf = _htfZones.GetActiveZones();

            // ===== Shadow lifecycle update on EACH base bar (needed for M5 zones) =====
            UpdateConfirmShadowStateByBaseBar(bar, prev, cur);

            // ===== Choose candidate pool for trading =====
            var tradeActiveZones = EnableM5ZonesForTrading ? (IReadOnlyList<TradingZone>)activeM5 : activeHtf;

            RunM5Confirmation(bar, cur, prev, tradeActiveZones, _htfBias);
            MaybeLogPhaseChange(bar, cur);

            _lastOrderFlow = _phase == ConfirmPhase.Confirmed
                ? _orderFlow.Evaluate(bar)
                : null;

            UpdateTradePlanV0(bar, cur, tradeActiveZones);
            MaybeLogTriggerBlock(bar, cur);

            // render zones (optional: both)
            var renderZones = BuildRenderZones(activeHtf, activeM5);

            var hud = BuildHudText(bar, cur, _htfStructure.LastSnapshot, tradeActiveZones, activeHtf, activeM5);

            lock (_renderLock)
            {
                _activeZonesForRender = renderZones.ToList();
                _hudText = ShowHud ? hud : string.Empty;
                _planForRender = _activePlan;
            }

            if (EnableFileLog && m5ZoneResult.NewZones.Count > 0)
            {
                AppendLog($"M5_ZONE_NEW bar={bar} count={m5ZoneResult.NewZones.Count} ex={m5ZoneResult.NewZones[0].ToShortText()}");
            }

            // 配置持久化：面板设为true则保存，随后自动重置
            if (SaveConfig)
            {
                SaveConfigToFile();
                SaveConfig = false;
            }

            // 兜底：每根K线写入HUD_SUMMARY（ExitPlan写入失败时仍有数据）
            if (_tradeHistory.Count > _lastHudTradeCount)
            {
                _lastHudTradeCount = _tradeHistory.Count;
                var cDate = _lastCandleTime.Hour >= 6 ? _lastCandleTime.Date : _lastCandleTime.Date.AddDays(-1);
                AppendLog($"HUD_SUMMARY date={cDate:yyyy-MM-dd} trades={_tradeHistory.Count} W{_tradeWins}L{_tradeLosses} NetR={_netR:F2} Net$={_netPnLDollar:F2}");
            }
        }

        private void ApplyM5ZoneParamsToEngine(int bar, M5ZoneEngine eng)
        {
            eng.EnableFvg = true;

            eng.MinFvgTicks = Math.Max(1, M5MinFvgTicks);

            eng.EnableDisplacementFilter = M5EnableDisplacementFilter;

            // ticks 优先；否则 legacy points
            eng.MinDisplacementRangePoints = Math.Max(0m, GetM5MinDisplacementRangePointsEff(bar));
            eng.MaxZoneWidthPoints = Math.Max(0m, GetM5MaxZoneWidthPointsEff(bar));

            eng.MergeOverlapRatio = Math.Max(0m, Math.Min(1m, M5MergeOverlapRatio));

            eng.MaxZonesPerType = Math.Max(0, M5MaxZonesPerType);
            eng.MaxLookbackBars = Math.Max(0, M5MaxLookbackBars);
        }

        private IReadOnlyList<TradingZone> BuildRenderZones(
            IReadOnlyList<TradingZone> htfZones,
            IReadOnlyList<TradingZone> m5Zones)
        {
            var list = new List<TradingZone>(capacity: (htfZones?.Count ?? 0) + (m5Zones?.Count ?? 0));

            if (RenderHtfZones && htfZones is not null)
                list.AddRange(htfZones);

            if (RenderM5Zones && m5Zones is not null)
                list.AddRange(m5Zones);

            return list;
        }

        private void ResetAllState(string reason)
        {
            AppendLog($"=== ResetAllState: {reason} ===");

            _probe = null;

            _htfAgg = null;
            _htfSeries.Clear();
            _htfStructure = null;
            _htfZones = null;

            _m5Zones = null;

            _orderFlow = null;
            _lastOrderFlow = null;

            _lastClosedVpCandle = null;

            _htfBias = TrendDirection.Neutral;
            _htfBiasReason = "-";

            _m5Structure = null;
            _lastM5StructureSnapshot = null;

            _planState = PlanState.Flat;
            _activePlan = null;

            _cooldownUntilBar = -1;
            _lastTriggeredConfirmStartBar = -1;
            _lastTriggeredZoneKey = null;

            ResetConfirm(-1, reason);
            _phase = ConfirmPhase.WaitHtfTrend;

            _tradeHistory.Clear();
            _tradeWins = 0;
            _tradeLosses = 0;
            _netR = 0m;
            _netPnLDollar = 0m;

            _barBlockReason = TriggerBlockReason.None;
            _barBlockDetail = "-";
            _lastBlockReason = TriggerBlockReason.None;
            _lastBlockDetail = "-";
            _lastBlockBar = -1;

            _lastLoggedPhase = (ConfirmPhase)(-1);
            _lastLoggedPlanState = (PlanState)(-1);
            _lastLoggedBlockReason = (TriggerBlockReason)(-1);
            _lastLoggedBar = -1;
            _sameReasonRun = 0;

            _confirmZoneShadow = null;
            _confirmZoneUsingShadow = false;
            _loggedShadowUseOnce = false;
            _invalidCloseBars = 0;

            _ofArmed = false;
            _ofArmedAtBar = -1;
            _ofArmedScore = 0;
            _ofArmedText = "-";
            _pendingEntry = null;

            _lockedZoneQualityScore = -1;
            _lockedZoneQualityDetail = "-";

            _loggedMitigatedKeepOnce = false;

            // live cleanup (best effort)
            if (_live is not null)
            {
                var live = _live;
                _live = null;
                EnqueueOrderAction("ResetAllStateCancelLive", async () =>
                {
                    await TryCancelOrderSafeAsync(live.EntryOrder, "ResetAllState");
                    await TryCancelOrderSafeAsync(live.StopOrder, "ResetAllState");
                    await TryCancelOrderSafeAsync(live.TargetOrder, "ResetAllState");
                });
            }

            lock (_renderLock)
            {
                _activeZonesForRender = new List<TradingZone>();
                _hudText = string.Empty;
                _planForRender = null;
            }
        }

        private void UpdateHtfBiasFromStructure(int htfIndex, StructureUpdateResult r)
        {
            var bt = r.Snapshot.BreakType;

            if (bt == StructureBreakType.None)
            {
                if (_biasHeldByChoch && _chochHoldUntilHtfIndex >= 0 && htfIndex >= _chochHoldUntilHtfIndex)
                {
                    _htfBias = TrendDirection.Neutral;
                    _htfBiasReason = $"CHOCHHoldExpired({_lastDirectionalBias})";
                    _biasHeldByChoch = false;
                    _chochHoldUntilHtfIndex = -1;
                }
                return;
            }

            if (bt == StructureBreakType.BOS)
            {
                if (r.Snapshot.BreakDirection == BreakDirection.Up)
                {
                    _htfBias = TrendDirection.Bullish;
                    _htfBiasReason = "BOS↑";
                }
                else if (r.Snapshot.BreakDirection == BreakDirection.Down)
                {
                    _htfBias = TrendDirection.Bearish;
                    _htfBiasReason = "BOS↓";
                }

                _htfBosHtfIndex = htfIndex;
                _lastDirectionalBias = _htfBias;
                _biasHeldByChoch = false;
                _chochHoldUntilHtfIndex = -1;
                return;
            }

            if (bt == StructureBreakType.CHOCH)
            {
                if (EnableChochBiasHold && _lastDirectionalBias != TrendDirection.Neutral)
                {
                    var holdBars = Math.Max(1, ChochHoldHtfBars);

                    _htfBias = _lastDirectionalBias;
                    _htfBiasReason = $"CHOCH({r.Snapshot.BreakDirection})->Hold({_lastDirectionalBias}) {holdBars}HTF";

                    _chochHoldUntilHtfIndex = htfIndex + holdBars;
                    _biasHeldByChoch = true;
                }
                else
                {
                    // 3.3 CHOCH智能偏差恢复：CHOCH方向就是新偏差方向
                    if (r.Snapshot.BreakDirection == BreakDirection.Up)
                    {
                        _htfBias = TrendDirection.Bullish;
                        _htfBiasReason = "CHOCH↑";
                    }
                    else if (r.Snapshot.BreakDirection == BreakDirection.Down)
                    {
                        _htfBias = TrendDirection.Bearish;
                        _htfBiasReason = "CHOCH↓";
                    }
                    else
                    {
                        _htfBias = TrendDirection.Neutral;
                        _htfBiasReason = $"CHOCH({r.Snapshot.BreakDirection})";
                    }
                    _biasHeldByChoch = false;
                    _chochHoldUntilHtfIndex = -1;
                }

                return;
            }
        }
    }
}
