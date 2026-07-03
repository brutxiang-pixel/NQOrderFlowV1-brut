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
        // HUD
        // =========================
        private string BuildHudText(
            int bar,
            IndicatorCandle curM5,
            StructureSnapshot snap,
            IReadOnlyList<TradingZone> tradeActive,
            IReadOnlyList<TradingZone> activeHtf,
            IReadOnlyList<TradingZone> activeM5)
        {
            var trendCN = snap.Trend switch
            {
                TrendDirection.Bullish => "多头",
                TrendDirection.Bearish => "空头",
                _ => "中性"
            };

            var biasCN = _htfBias switch
            {
                TrendDirection.Bullish => "多头",
                TrendDirection.Bearish => "空头",
                _ => "中性"
            };

            var shPrice = snap.LastSwingHigh?.Price.ToString("0.00") ?? "-";
            var slPrice = snap.LastSwingLow?.Price.ToString("0.00") ?? "-";

            var breakCN = string.IsNullOrWhiteSpace(snap.BreakText)
                ? "无突破"
                : SimplifyBreakText(snap.BreakText);

            var zoneCN = _confirmZoneKey is null ? "-" : FormatZoneKeyCN(_confirmZoneKey) + (_confirmZoneUsingShadow ? " (shadow)" : "");

            var takeN = Math.Max(0, HudActiveZonesCount);
            var actCN = tradeActive
                .OrderByDescending(z => z.CreatedBar)
                .Take(takeN)
                .Select(FormatZoneCNShort)
                .ToList();

            var actText = actCN.Count == 0 ? "-" : string.Join(" | ", actCN);

            string ofLine;
            if (_phase != ConfirmPhase.Confirmed)
            {
                ofLine = "订单流：跳过（原因：M5未确认）";
            }
            else
            {
                var armed = _ofArmed ? $"Armed({_ofArmedScore}/4) {_ofArmedText}" : "未Armed";
                if (_lastOrderFlow is null)
                    ofLine = $"订单流：未评分 | {armed}";
                else
                    ofLine = $"订单流：last={_lastOrderFlow.Score}/4 {_lastOrderFlow.Text} | {armed}";
            }

            var entryLine = EnableLiveOrders
                ? (_live is null
                    ? (_mktEntryScheduled
                        ? $"入场(LIVE)：Market等待回撤 waited={Math.Max(0, bar - _mktEntryBar)}/{Math.Max(0, MaxRetraceWaitBars)}"
                        : "入场(LIVE)：-")
                    : $"入场(LIVE)：{_live.Side} Limit @{_live.EntryLimit:0.00}({_live.AnchorText}) filled={_live.EntryFilledQty:0.##} bracketQty={_live.BracketQty:0.##} tradeId={_live.TradeId}")
                : (_pendingEntry is null
                    ? (_mktEntryScheduled
                        ? $"入场：Market等待回撤 waited={Math.Max(0, bar - _mktEntryBar)}/{Math.Max(0, MaxRetraceWaitBars)}"
                        : "入场：-")
                    : $"入场：Limit {_pendingEntry.Side} @{_pendingEntry.LimitPrice:0.00}({_pendingEntry.AnchorText}) waited={Math.Max(0, bar - _pendingEntry.CreatedBar)}/{_pendingEntry.EffectiveMaxWaitBars}");

            var planLine = _activePlan is null
                ? "计划：无"
                : (_activePlan.ExitBar is null
                    ? $"计划：{_activePlan.Side} E={_activePlan.Entry:0.00} SL={_activePlan.Stop:0.00} TP={_activePlan.Target:0.00}"
                    : $"计划：已退出({_activePlan.ExitReason})");

            var cdLeft = (EnableCooldown && _cooldownUntilBar > bar)
                ? (_cooldownUntilBar - bar)
                : 0;

            var cdLine = EnableCooldown
                ? (cdLeft > 0 ? $"冷却：{cdLeft} bars" : "冷却：无")
                : "冷却：关闭";

            var tradeLogLine = ShowTradeLog ? BuildTradeLogLine() : string.Empty;
            var tradeRecentLine = ShowTradeLog ? BuildTradeRecentLine() : string.Empty;

            var refLine = _phase is ConfirmPhase.WaitM5Bos or ConfirmPhase.Confirmed
                ? (_confirmRefIsFallback ? $"参考: {_confirmRefText} (FB)" : $"参考: {_confirmRefText}")
                : "参考: -";

            var qLine = _confirmZoneKey is null
                ? "区块质量: -"
                : (_lockedZoneQualityScore >= 0
                    ? $"区块质量(锁定冻结): {_lockedZoneQualityScore}/10 | {_lockedZoneQualityDetail}"
                    : "区块质量: (未评分)");

            var mitigatedLine = (DisallowMitigatedZones && (_ofArmed || _pendingEntry is not null) && _loggedMitigatedKeepOnce)
                ? "Mitigated: 已Armed/挂单保留(setup不取消)"
                : string.Empty;

            var blockLine = BuildTriggerBlockHudLine(bar);

            var zoneSrc = EnableM5ZonesForTrading ? "ZoneSrc=M5FVG" : "ZoneSrc=HTF15";
            var renderSrc = $"Render={(RenderHtfZones ? "HTF" : "")}{(RenderHtfZones && RenderM5Zones ? "+" : "")}{(RenderM5Zones ? "M5" : "")}";
            var counts = $"HTF={activeHtf.Count} M5={activeM5.Count}";

            if (CompactHud)
            {
                var lines = new List<string>(12)
                {
                    $"结构(M15*): 趋势={trendCN} | Bias={biasCN}({_htfBiasReason}) | {zoneSrc} | {renderSrc} | {counts} | SH {snap.HighLabel}:{shPrice} | SL {snap.LowLabel}:{slPrice} | {breakCN}",
                    $"确认(M5): {_phase} | 关注:{zoneCN} | {_confirmText}",
                    refLine,
                    qLine,
                    ofLine,
                    entryLine,
                    blockLine,
                    $"{planLine} | {cdLine} | 区块:{actText}"
                };

                if (!string.IsNullOrWhiteSpace(mitigatedLine))
                    lines.Add(mitigatedLine);

                if (ShowTradeLog)
                {
                    lines.Add(tradeLogLine);
                    if (!string.IsNullOrWhiteSpace(tradeRecentLine))
                        lines.Add(tradeRecentLine);
                }

                return string.Join("\n", lines);
            }

            var fullLines = new List<string>(20)
            {
                $"结构(M15*): 趋势={trendCN} | Bias={biasCN}({_htfBiasReason}) | {zoneSrc} | {renderSrc} | {counts} | SH={snap.HighLabel}:{shPrice} | SL={snap.LowLabel}:{slPrice} | {breakCN}",
                $"M5: O={curM5.Open:0.00} H={curM5.High:0.00} L={curM5.Low:0.00} C={curM5.Close:0.00} | V={curM5.Volume:0} | Δ={curM5.Delta:0}",
                $"确认(M5): {_phase} | 关注={zoneCN} | {_confirmText}",
                refLine,
                qLine,
                ofLine,
                entryLine,
                blockLine,
                planLine,
                cdLine,
                $"区块: {actText}",
                $"图例: T触碰 M回补75% RVP拒绝(灰色)"
            };

            if (!string.IsNullOrWhiteSpace(mitigatedLine))
                fullLines.Add(mitigatedLine);

            if (ShowTradeLog)
            {
                fullLines.Add(tradeLogLine);
                if (!string.IsNullOrWhiteSpace(tradeRecentLine))
                    fullLines.Add(tradeRecentLine);
            }

            return string.Join("\n", fullLines);
        }

        private string BuildTradeLogLine()
        {
            var total = _tradeHistory.Count;
            var last = _tradeHistory.Count > 0 ? _tradeHistory[^1] : null;

            var lastText = last is null
                ? "-"
                : $"{(last.Side == "LONG" ? "L" : "S")} {last.ExitReason} R={last.R:0.00}  ${last.PnLDollar:0.##}";

            var contracts = Math.Max(1, Contracts);
            var tickValue = TickValuePerContract <= 0m ? 0.5m : TickValuePerContract;

            var dateStr = last is not null ? last.ExitTime.ToString("MM-dd") : "??";
            return $"{dateStr} 记录: {total}笔 W{_tradeWins} L{_tradeLosses} NetR={_netR:0.00} Net$={_netPnLDollar:0.##} | 上一笔: {lastText} | {contracts}x @${tickValue:0.##}/tick";
        }

        private string BuildTradeRecentLine()
        {
            var n = Math.Max(0, TradeLogCount);
            if (n == 0 || _tradeHistory.Count == 0)
                return string.Empty;

            var items = _tradeHistory
                .TakeLast(Math.Min(n, _tradeHistory.Count))
                .Select(t =>
                {
                    var dir = t.Side == "LONG" ? "L" : "S";
                    var reason = t.ExitReason.Replace(" Hit", "");
                    return $"{t.ExitTime:MM-dd} {dir}{reason} {t.R:0.00}R ${t.PnLDollar:0.##}";
                });

            return $"最近{Math.Min(n, _tradeHistory.Count)}笔: {string.Join(" | ", items)}";
        }

        private static string SimplifyBreakText(string breakText)
        {
            var s = breakText.Trim().Replace(" @ ", " ");
            s = s.Replace("UP", "↑").Replace("DOWN", "↓");
            return $"突破:{s}";
        }

        private static string FormatZoneCNShort(TradingZone z)
        {
            var type = z.Type switch
            {
                ZoneType.BullishOB => "多OB",
                ZoneType.BearishOB => "空OB",
                ZoneType.BullishFVG => "多FVG",
                ZoneType.BearishFVG => "空FVG",
                _ => "Z"
            };

            var flag =
                z.IsInvalidated ? "X" :
                z.IsVpRejected ? "R" :
                z.IsMitigated ? "M" :
                z.IsTouched ? "T" : "";

            return string.IsNullOrEmpty(flag)
                ? $"{type} {z.Low:0.00}-{z.High:0.00}"
                : $"{type} {z.Low:0.00}-{z.High:0.00}{flag}";
        }

        private static string FormatZoneKeyCN(ZoneKey? k)
        {
            if (k is null)
                return "-";

            var type = k.Type switch
            {
                ZoneType.BullishOB => "多OB",
                ZoneType.BearishOB => "空OB",
                ZoneType.BullishFVG => "多FVG",
                ZoneType.BearishFVG => "空FVG",
                _ => "Z"
            };

            return $"{type} {k.Low:0.00}-{k.High:0.00}";
        }

        private string BuildTriggerBlockHudLine(int bar)
        {
            if (_barBlockReason != TriggerBlockReason.None && !string.IsNullOrWhiteSpace(_barBlockDetail))
                return $"触发过滤: {FormatBlockReasonCN(_barBlockReason)} | {_barBlockDetail}";

            if (_barBlockReason == TriggerBlockReason.None &&
                !string.IsNullOrWhiteSpace(_barBlockDetail) &&
                _barBlockDetail.StartsWith("OK:", StringComparison.OrdinalIgnoreCase))
            {
                return $"触发: {_barBlockDetail}";
            }

            if (_lastBlockReason != TriggerBlockReason.None && _lastBlockBar >= 0 && (bar - _lastBlockBar) <= 50)
                return $"触发过滤(最近@{_lastBlockBar}): {FormatBlockReasonCN(_lastBlockReason)} | {_lastBlockDetail}";

            return "触发过滤: -";
        }

        private static string FormatBlockReasonCN(TriggerBlockReason r)
        {
            return r switch
            {
                TriggerBlockReason.InPosition => "跳过-持仓中",
                TriggerBlockReason.CooldownActive => "跳过-冷却中",
                TriggerBlockReason.DuplicateTriggerSameSession => "跳过-重复触发",

                TriggerBlockReason.NotConfirmed => "跳过-M5未确认",

                TriggerBlockReason.OrderFlowNotReady => "跳过-订单流无数据",
                TriggerBlockReason.OrderFlowScoreInsufficient => "跳过-订单流不足",
                TriggerBlockReason.OrderFlowWaitTimeout => "跳过-订单流超时",

                TriggerBlockReason.BiasNeutral => "跳过-HTF Bias中性",

                TriggerBlockReason.ConfirmZoneKeyMissing => "跳过-无锁定区块",
                TriggerBlockReason.ZoneNotFound => "跳过-区块消失",
                TriggerBlockReason.ZoneInvalidated => "跳过-区块失效",
                TriggerBlockReason.ZoneVpRejected => "跳过-VP拒绝",

                TriggerBlockReason.ZoneQualityInsufficient => "跳过-区块质量不足",

                TriggerBlockReason.EntryPlaceTooFarFromZone => "跳过-挂单距离过远",
                TriggerBlockReason.EntryTooFarFromZone => "跳过-防追价(等回撤)",
                TriggerBlockReason.EntryOrderPending => "等待-回撤成交",
                TriggerBlockReason.EntryOrderExpired => "跳过-挂单超时",
                TriggerBlockReason.EntryOrderEarlyCanceled => "跳过-提前取消",

                TriggerBlockReason.RiskInvalid => "跳过-风险无效",
                TriggerBlockReason.RiskTicksTooSmall => "跳过-风险过小",
                TriggerBlockReason.RiskTicksTooLarge => "跳过-风险过大",

                TriggerBlockReason.LiveOrderPending => "LIVE-等待回报",
                TriggerBlockReason.LiveOrderError => "LIVE-错误",

                _ => "-"
            };
        }
    }
}
