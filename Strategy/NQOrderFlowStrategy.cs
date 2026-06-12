using ATAS.Indicators;
using ATAS.Strategies.Chart;
using NQOrderFlowV1.Engines;
using NQOrderFlowV1.Models;
using NQOrderFlowV1.Services;
using OFT.Rendering.Context;
using OFT.Rendering.Tools;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;

namespace NQOrderFlowV1.Strategy
{
    public class NQOrderFlowStrategy : ChartStrategy
    {
        // ===== 核心参数 =====
        private const decimal TickSizeNq = 0.25m;

        private const int HtfMinutes = 15;
        private const int PivotLength = 2;

        private const int ObLookback = 10;
        private const int MinFvgTicks = 4;
        private const decimal VpMaxDistancePoints = 8m;

        private const int MaxConfirmBars = 24;

        // ===== 虚拟执行层（TradePlan V0）=====
        private const decimal RiskRewardR = 2m;       // 2R
        private const int SlBufferTicks = 4;          // 4 ticks
        private const int OrderFlowMinScore = 2;      // >=2/4
        private static readonly decimal SlBufferPoints = SlBufferTicks * TickSizeNq;

        // ===== 冷却设置（ATAS 参数）=====
        [Category("Plan")]
        [DisplayName("Enable Cooldown")]
        public bool EnableCooldown { get; set; } = true;

        [Category("Plan")]
        [DisplayName("Cooldown Bars (M5)")]
        public int CooldownBars { get; set; } = 6;

        // ===== HUD 开关（ATAS 参数）=====
        [Category("HUD")]
        [DisplayName("Show HUD")]
        public bool ShowHud { get; set; } = true;

        [Category("HUD")]
        [DisplayName("Compact HUD (极简)")]
        public bool CompactHud { get; set; } = true;

        [Category("HUD")]
        [DisplayName("HUD显示的活动区块数量")]
        public int HudActiveZonesCount { get; set; } = 3;

        // ===== 服务/引擎 =====
        private DataProbeService? _probe;

        private HigherTimeframeAggregator? _htfAgg;
        private readonly List<HigherTimeframeAggregator.HtfCandle> _htfSeries = new();

        private HtfStructureEngine? _htfStructure;
        private HtfZoneEngine? _htfZones;

        private OrderFlowEngine? _orderFlow;
        private OrderFlowResult? _lastOrderFlow;

        // ===== 渲染缓存：Zones + HUD + TradePlan =====
        private readonly object _renderLock = new();
        private List<TradingZone> _activeZonesForRender = new();
        private string _hudText = string.Empty;

        private TradePlan? _planForRender;

        // =========================
        // M5 确认层（仅提示）
        // =========================
        private enum ConfirmPhase
        {
            WaitHtfTrend,
            WaitZoneTouch,
            WaitM5Bos,
            Confirmed
        }

        private ConfirmPhase _phase = ConfirmPhase.WaitHtfTrend;

        private sealed record ZoneKey(
            ZoneType Type,
            int StartBar,
            int CreatedBar,
            decimal Low,
            decimal High
        );

        private ZoneKey? _confirmZoneKey;
        private int _confirmStartBar = -1;

        private StructureEngine? _m5ConfirmEngine;
        private int _m5ResetBar = -1;
        private StructureSnapshot? _lastM5ConfirmSnapshot;

        private string _confirmText = "-";

        // =========================
        // 虚拟执行层 TradePlan V0
        // =========================
        private enum PlanState
        {
            Flat,
            InPosition
        }

        private PlanState _planState = PlanState.Flat;
        private TradePlan? _activePlan;

        // 冷却/一次确认一次触发
        private int _cooldownUntilBar = -1;
        private int _lastTriggeredConfirmStartBar = -1;
        private ZoneKey? _lastTriggeredZoneKey;

        private sealed class TradePlan
        {
            public int CreatedBar { get; init; }
            public string Side { get; init; } = "LONG"; // LONG / SHORT

            public decimal Entry { get; init; }
            public decimal Stop { get; init; }
            public decimal Target { get; init; }

            public ZoneKey Zone { get; init; } = new(ZoneType.BullishFVG, 0, 0, 0, 0);

            public int OfScore { get; init; }
            public string OfText { get; init; } = "-";

            public string ExitReason { get; set; } = "-";
            public int? ExitBar { get; set; }
        }

        public NQOrderFlowStrategy()
        {
            EnableCustomDrawing = true;
            SubscribeToDrawingEvents(DrawingLayouts.Final | DrawingLayouts.LatestBar);
        }

        // =========================
        // RENDER
        // =========================
        protected override void OnRender(RenderContext context, DrawingLayouts layout)
        {
            if (ChartInfo is null)
                return;

            List<TradingZone> zones;
            string hud;
            TradePlan? plan;

            lock (_renderLock)
            {
                zones = _activeZonesForRender.ToList();
                hud = _hudText;
                plan = _planForRender;
            }

            DrawZones(context, zones);
            DrawPlanLines(context, plan);

            if (ShowHud)
                DrawHudTopCenter(context, hud);
        }

        private void DrawZones(RenderContext context, List<TradingZone> zones)
        {
            if (ChartInfo is null || zones.Count == 0)
                return;

            var rightBar = LastVisibleBarNumber;

            foreach (var zone in zones)
            {
                if (zone.IsInvalidated)
                    continue;

                var x1 = ChartInfo.GetXByBar(zone.StartBar, true);
                var x2 = ChartInfo.GetXByBar(rightBar, false);
                if (x2 <= x1)
                    continue;

                var yHigh = ChartInfo.GetYByPrice(zone.High, false);
                var yLow = ChartInfo.GetYByPrice(zone.Low, false);

                var top = (int)Math.Min(yHigh, yLow);
                var bottom = (int)Math.Max(yHigh, yLow);

                var height = bottom - top;
                if (height <= 1)
                    continue;

                var rect = new Rectangle(
                    x: (int)x1,
                    y: top,
                    width: (int)(x2 - x1),
                    height: height);

                var (fillColor, borderColor) = GetZoneColors(zone);

                if (zone.IsVpRejected)
                {
                    fillColor = Color.FromArgb(Math.Max(6, fillColor.A / 2), fillColor);
                    borderColor = Color.FromArgb(Math.Max(18, borderColor.A / 2), borderColor);
                }
                else
                {
                    if (zone.IsMitigated)
                    {
                        fillColor = Color.FromArgb(Math.Max(8, fillColor.A / 4), fillColor);
                        borderColor = Color.FromArgb(Math.Max(20, borderColor.A / 2), borderColor);
                    }
                    else if (zone.IsTouched)
                    {
                        fillColor = Color.FromArgb(Math.Max(12, fillColor.A / 2), fillColor);
                        borderColor = Color.FromArgb(Math.Max(30, borderColor.A / 2), borderColor);
                    }
                }

                context.FillRectangle(fillColor, rect);
                context.DrawRectangle(new RenderPen(borderColor, 1), rect);
            }
        }

        private void DrawPlanLines(RenderContext context, TradePlan? plan)
        {
            if (ChartInfo is null || plan is null)
                return;

            var x1 = ChartArea.X;
            var x2 = ChartArea.X + ChartArea.Width;

            void DrawHLine(decimal price, Color color, string label)
            {
                var y = (int)ChartInfo.GetYByPrice(price, false);
                var pen = new RenderPen(color, 2);

                context.DrawLine(pen, x1, y, x2, y);

                var font = new RenderFont("Consolas", 11);
                var text = label;
                var size = context.MeasureString(text, font);
                var rect = new Rectangle(x1 + 6, y - (int)size.Height - 2, (int)size.Width + 8, (int)size.Height + 4);

                context.FillRectangle(Color.FromArgb(120, 0, 0, 0), rect);
                context.DrawString(text, font, color, rect.X + 4, rect.Y + 2);
            }

            DrawHLine(plan.Entry, Color.DeepSkyBlue, $"{plan.Side} ENTRY {plan.Entry:0.00}");
            DrawHLine(plan.Stop, Color.OrangeRed, $"SL {plan.Stop:0.00}");
            DrawHLine(plan.Target, Color.LimeGreen, $"TP {plan.Target:0.00} ({RiskRewardR:0.0}R)");

            if (plan.ExitBar is not null)
            {
                var font = new RenderFont("Consolas", 11);
                var msg = $"EXIT: {plan.ExitReason}";
                var size = context.MeasureString(msg, font);

                var x = ChartArea.X + ChartArea.Width - (int)size.Width - 20;
                var y = ChartArea.Y + 60;
                var rect = new Rectangle(x, y, (int)size.Width + 10, (int)size.Height + 6);

                context.FillRectangle(Color.FromArgb(120, 0, 0, 0), rect);
                context.DrawString(msg, font, Color.Gold, rect.X + 5, rect.Y + 3);
            }
        }

        private void DrawHudTopCenter(RenderContext context, string hud)
        {
            if (string.IsNullOrWhiteSpace(hud))
                return;

            var font = new RenderFont("Consolas", 12);
            var size = context.MeasureString(hud, font);

            var textW = Math.Ceiling((double)size.Width);
            var textH = Math.Ceiling((double)size.Height);

            const int padX = 10;
            const int padY = 8;

            var boxW = (int)textW + padX * 2;
            var boxH = (int)textH + padY * 2;

            var x = ChartArea.X + (ChartArea.Width - boxW) / 2;
            var y = ChartArea.Y + 6;

            var rect = new Rectangle(x, y, boxW, boxH);

            var bg = Color.FromArgb(110, 0, 0, 0);
            var border = Color.FromArgb(160, 40, 40, 40);

            context.FillRectangle(bg, rect);
            context.DrawRectangle(new RenderPen(border, 1), rect);

            context.DrawString(hud, font, Color.DeepSkyBlue, x + padX, y + padY);
        }

        // =========================
        // CALC
        // =========================
        protected override void OnCalculate(int bar, decimal value)
        {
            var cur = GetCandle(bar);
            if (cur is null)
                return;

            var prev = bar > 0 ? GetCandle(bar - 1) : null;

            _probe ??= new DataProbeService(GetCandle);

            _htfAgg ??= new HigherTimeframeAggregator(
                getBaseCandle: GetCandle,
                targetMinutes: HtfMinutes,
                tickSize: TickSizeNq,
                valueAreaPercent: 0.70m);

            _htfStructure ??= new HtfStructureEngine(pivotLength: PivotLength);

            _htfZones ??= new HtfZoneEngine(
                tickSize: TickSizeNq,
                lookback: ObLookback,
                minFvgTicks: MinFvgTicks,
                vpMaxDistancePoints: VpMaxDistancePoints);

            _orderFlow ??= new OrderFlowEngine(GetCandle, tickSize: TickSizeNq);

            // 1) 每根 M5 bar：更新区块状态
            _htfZones.UpdateByBaseBar(
                prevBase: prev,
                curBase: cur,
                getVpCandle: () =>
                {
                    if (_htfAgg.TryBuildBucketCandle(bar, out var bucket))
                        return bucket;
                    return null;
                });

            // 2) HTF 收盘确认时：推进 HTF 结构/区块
            var didHtfClose = _htfAgg.IsBucketCloseBar(bar);

            if (didHtfClose && _htfAgg.TryBuildBucketCandle(bar, out var htf))
            {
                UpsertHtfCandle(htf);
                var htfIndex = _htfSeries.Count - 1;

                var htfStructureResult = _htfStructure.Update(htfIndex, _htfSeries);
                _ = _htfZones.OnHtfClosed(htfIndex, _htfSeries, htfStructureResult);
            }

            // 3) ActiveZones
            var active = _htfZones.GetActiveZones();

            // 4) M5 确认
            RunM5Confirmation(bar, cur, active, _htfStructure.LastSnapshot.Trend);

            // 5) 订单流评分（仅 Confirmed）
            _lastOrderFlow = _phase == ConfirmPhase.Confirmed
                ? _orderFlow.Evaluate(bar)
                : null;

            // 6) 虚拟执行层（含冷却）
            UpdateTradePlanV0(bar, cur, active);

            // 7) HUD
            var hud = BuildHudText(bar, cur, _htfStructure.LastSnapshot, active);

            lock (_renderLock)
            {
                _activeZonesForRender = active.ToList();
                _hudText = ShowHud ? hud : string.Empty;
                _planForRender = _activePlan;
            }
        }

        private void UpdateTradePlanV0(int bar, IndicatorCandle cur, IReadOnlyList<TradingZone> active)
        {
            // 退出：触碰即出场，优先 SL
            if (_planState == PlanState.InPosition && _activePlan is not null && _activePlan.ExitBar is null)
            {
                var plan = _activePlan;

                if (plan.Side == "LONG")
                {
                    if (cur.Low <= plan.Stop)
                        ExitPlan(bar, "SL Hit");
                    else if (cur.High >= plan.Target)
                        ExitPlan(bar, "TP Hit");
                }
                else
                {
                    if (cur.High >= plan.Stop)
                        ExitPlan(bar, "SL Hit");
                    else if (cur.Low <= plan.Target)
                        ExitPlan(bar, "TP Hit");
                }
            }

            if (_planState != PlanState.Flat)
                return;

            // 冷却：未到解冻 bar，跳过
            if (EnableCooldown && _cooldownUntilBar >= 0 && bar < _cooldownUntilBar)
                return;

            // 一次确认会话只触发一次（同 confirmStartBar + 同 zone）
            if (_confirmStartBar >= 0 &&
                _confirmStartBar == _lastTriggeredConfirmStartBar &&
                _confirmZoneKey is not null &&
                _lastTriggeredZoneKey is not null &&
                ZoneKeyEquals(_confirmZoneKey, _lastTriggeredZoneKey))
            {
                return;
            }

            // 触发条件
            if (_phase != ConfirmPhase.Confirmed)
                return;

            if (_lastOrderFlow is null || _lastOrderFlow.Score < OrderFlowMinScore)
                return;

            if (_confirmZoneKey is null)
                return;

            if (!TryFindZone(active, _confirmZoneKey, out var zone))
                return;

            if (zone.IsInvalidated || zone.IsVpRejected)
                return;

            var trend = _htfStructure?.LastSnapshot.Trend ?? TrendDirection.Neutral;
            if (trend == TrendDirection.Neutral)
                return;

            var isLong = trend == TrendDirection.Bullish;
            var entry = cur.Close;

            // 止损：优先 M5 swing + buffer，兜底 zone 边界 + buffer
            var stop = GetFallbackStopFromZone(isLong, zone);

            if (_lastM5ConfirmSnapshot is not null)
            {
                if (isLong && _lastM5ConfirmSnapshot.LastSwingLow is not null)
                    stop = _lastM5ConfirmSnapshot.LastSwingLow.Price - SlBufferPoints;
                else if (!isLong && _lastM5ConfirmSnapshot.LastSwingHigh is not null)
                    stop = _lastM5ConfirmSnapshot.LastSwingHigh.Price + SlBufferPoints;
            }

            // 方向纠正
            if (isLong && stop >= entry - TickSizeNq)
                stop = Math.Min(entry - TickSizeNq, zone.Low - SlBufferPoints);

            if (!isLong && stop <= entry + TickSizeNq)
                stop = Math.Max(entry + TickSizeNq, zone.High + SlBufferPoints);

            var risk = Math.Abs(entry - stop);
            if (risk < TickSizeNq)
                return;

            var target = isLong
                ? entry + risk * RiskRewardR
                : entry - risk * RiskRewardR;

            _activePlan = new TradePlan
            {
                CreatedBar = bar,
                Side = isLong ? "LONG" : "SHORT",
                Entry = entry,
                Stop = stop,
                Target = target,
                Zone = _confirmZoneKey,
                OfScore = _lastOrderFlow.Score,
                OfText = _lastOrderFlow.Text
            };

            _planState = PlanState.InPosition;

            // 触发后立即进入冷却 + 锁定本次确认会话已用过
            _lastTriggeredConfirmStartBar = _confirmStartBar;
            _lastTriggeredZoneKey = _confirmZoneKey;

            if (EnableCooldown)
                _cooldownUntilBar = Math.Max(_cooldownUntilBar, bar + Math.Max(0, CooldownBars));
        }

        private void ExitPlan(int bar, string reason)
        {
            if (_activePlan is null)
                return;

            _activePlan.ExitBar = bar;
            _activePlan.ExitReason = reason;
            _planState = PlanState.Flat;

            if (EnableCooldown)
                _cooldownUntilBar = Math.Max(_cooldownUntilBar, bar + Math.Max(0, CooldownBars));
        }

        private static decimal GetFallbackStopFromZone(bool isLong, TradingZone zone)
            => isLong ? zone.Low - SlBufferPoints : zone.High + SlBufferPoints;

        private static bool TryFindZone(IReadOnlyList<TradingZone> zones, ZoneKey key, out TradingZone found)
        {
            foreach (var z in zones)
            {
                if (z.Type == key.Type &&
                    z.StartBar == key.StartBar &&
                    z.CreatedBar == key.CreatedBar &&
                    z.Low == key.Low &&
                    z.High == key.High)
                {
                    found = z;
                    return true;
                }
            }

            found = null!;
            return false;
        }

        private string BuildHudText(int bar, IndicatorCandle curM5, StructureSnapshot snap, IReadOnlyList<TradingZone> active)
        {
            var trendCN = snap.Trend switch
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

            var zoneCN = _confirmZoneKey is null ? "-" : FormatZoneKeyCN(_confirmZoneKey);

            var takeN = Math.Max(0, HudActiveZonesCount);
            var actCN = active
                .OrderByDescending(z => z.CreatedBar)
                .Take(takeN)
                .Select(FormatZoneCNShort)
                .ToList();
            var actText = actCN.Count == 0 ? "-" : string.Join(" | ", actCN);

            // 订单流：交易员口吻
            string ofLine;
            if (_phase != ConfirmPhase.Confirmed)
            {
                ofLine = "订单流：跳过（原因：M5未确认）";
            }
            else
            {
                if (_lastOrderFlow is null)
                    ofLine = "订单流：未评分（无数据）";
                else if (_lastOrderFlow.Score >= OrderFlowMinScore)
                    ofLine = $"订单流：触发({_lastOrderFlow.Score}/4) | {_lastOrderFlow.Text}";
                else
                    ofLine = $"订单流：未触发({_lastOrderFlow.Score}/4) | {_lastOrderFlow.Text}";
            }

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

            if (CompactHud)
            {
                return
                    $"结构(M15*): {trendCN} | SH {snap.HighLabel}:{shPrice} | SL {snap.LowLabel}:{slPrice} | {breakCN}\n" +
                    $"确认(M5): {_phase} | 关注:{zoneCN}\n" +
                    $"{ofLine}\n" +
                    $"{planLine} | {cdLine} | 区块:{actText}";
            }

            return
                $"结构(M15*): 趋势={trendCN} | SH={snap.HighLabel}:{shPrice} | SL={snap.LowLabel}:{slPrice} | {breakCN}\n" +
                $"M5: O={curM5.Open:0.00} H={curM5.High:0.00} L={curM5.Low:0.00} C={curM5.Close:0.00} | V={curM5.Volume:0} | Δ={curM5.Delta:0}\n" +
                $"确认(M5): {_phase} | 关注={zoneCN} | {_confirmText}\n" +
                $"{ofLine}\n" +
                $"{planLine}\n" +
                $"{cdLine}\n" +
                $"区块: {actText}\n" +
                $"图例: T触碰 M回补50% RVP拒绝(灰色)";
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

        private static string FormatZoneKeyCN(ZoneKey k)
        {
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

        private static (Color Fill, Color Border) GetZoneColors(TradingZone zone)
        {
            if (zone.IsVpRejected)
                return (Color.FromArgb(35, Color.Gray), Color.FromArgb(90, Color.Gray));

            return zone.Type switch
            {
                ZoneType.BullishOB => (Color.FromArgb(70, Color.LimeGreen), Color.FromArgb(160, Color.LimeGreen)),
                ZoneType.BearishOB => (Color.FromArgb(70, Color.Red), Color.FromArgb(160, Color.Red)),
                ZoneType.BullishFVG => (Color.FromArgb(55, Color.DodgerBlue), Color.FromArgb(140, Color.DodgerBlue)),
                ZoneType.BearishFVG => (Color.FromArgb(55, Color.Orange), Color.FromArgb(140, Color.Orange)),
                _ => (Color.FromArgb(50, Color.Gray), Color.FromArgb(120, Color.Gray))
            };
        }

        // =========================
        // CONFIRM
        // =========================
        private void RunM5Confirmation(int bar, IndicatorCandle curBase, IReadOnlyList<TradingZone> activeZones, TrendDirection htfTrend)
        {
            if (htfTrend == TrendDirection.Neutral)
            {
                ResetConfirm("HTF中性");
                _phase = ConfirmPhase.WaitHtfTrend;
                _lastM5ConfirmSnapshot = null;
                return;
            }

            var candidates = activeZones
                .Where(z => !z.IsInvalidated && !z.IsVpRejected)
                .Where(z => htfTrend == TrendDirection.Bullish
                    ? (z.Type == ZoneType.BullishOB || z.Type == ZoneType.BullishFVG)
                    : (z.Type == ZoneType.BearishOB || z.Type == ZoneType.BearishFVG))
                .ToList();

            if (candidates.Count == 0)
            {
                ResetConfirm("无可用区块");
                _phase = ConfirmPhase.WaitZoneTouch;
                _lastM5ConfirmSnapshot = null;
                return;
            }

            var focus = candidates
                .OrderBy(z => Math.Abs(curBase.Close - z.Mid))
                .First();

            var focusKey = new ZoneKey(focus.Type, focus.StartBar, focus.CreatedBar, focus.Low, focus.High);

            var eps = TickSizeNq / 2m;
            var inZone = curBase.High >= focus.Low - eps && curBase.Low <= focus.High + eps;

            switch (_phase)
            {
                case ConfirmPhase.WaitHtfTrend:
                case ConfirmPhase.WaitZoneTouch:
                    _confirmText = inZone ? "已进入区块" : "等待触碰";
                    _confirmZoneKey = focusKey;

                    if (inZone)
                    {
                        _m5ResetBar = bar;
                        _m5ConfirmEngine = new StructureEngine(GetCandleAfterReset, pivotLength: 2);

                        _confirmStartBar = bar;
                        _phase = ConfirmPhase.WaitM5Bos;
                        _confirmText = "开始等待M5顺势BOS";
                    }
                    break;

                case ConfirmPhase.WaitM5Bos:
                    if (_confirmZoneKey is null || !ZoneKeyEquals(_confirmZoneKey, focusKey))
                    {
                        ResetConfirm("关注区变化");
                        _phase = ConfirmPhase.WaitZoneTouch;
                        _lastM5ConfirmSnapshot = null;
                        break;
                    }

                    if (_confirmStartBar >= 0 && (bar - _confirmStartBar) > MaxConfirmBars)
                    {
                        ResetConfirm("确认超时");
                        _phase = ConfirmPhase.WaitZoneTouch;
                        _lastM5ConfirmSnapshot = null;
                        break;
                    }

                    if (_m5ConfirmEngine is null)
                    {
                        ResetConfirm("确认引擎为空");
                        _phase = ConfirmPhase.WaitZoneTouch;
                        _lastM5ConfirmSnapshot = null;
                        break;
                    }

                    var r = _m5ConfirmEngine.Update(bar);
                    var s = r.Snapshot;
                    _lastM5ConfirmSnapshot = s;

                    var wantDir = htfTrend == TrendDirection.Bullish ? BreakDirection.Up : BreakDirection.Down;

                    if (s.BreakType == StructureBreakType.BOS && s.BreakDirection == wantDir)
                    {
                        _phase = ConfirmPhase.Confirmed;
                        _confirmText = $"确认完成：{s.BreakText}";
                    }
                    else
                    {
                        _confirmText = inZone ? "等待M5顺势BOS（在区内）" : "等待M5顺势BOS";
                    }
                    break;

                case ConfirmPhase.Confirmed:
                    break;
            }
        }

        private IndicatorCandle? GetCandleAfterReset(int bar)
        {
            if (_m5ResetBar < 0)
                return GetCandle(bar);

            if (bar < _m5ResetBar)
                return null;

            return GetCandle(bar);
        }

        private static bool ZoneKeyEquals(ZoneKey a, ZoneKey b)
            => a.Type == b.Type && a.StartBar == b.StartBar && a.CreatedBar == b.CreatedBar && a.Low == b.Low && a.High == b.High;

        private void ResetConfirm(string reason)
        {
            _confirmZoneKey = null;
            _confirmStartBar = -1;
            _m5ConfirmEngine = null;
            _m5ResetBar = -1;
            _confirmText = $"重置:{reason}";
        }

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
        // HTF Structure Engine (基于 HtfCandle)
        // =========================
        private sealed class HtfStructureEngine
        {
            private readonly int _pivotLength;
            private readonly List<SwingPoint> _swings = new();

            public StructureSnapshot LastSnapshot { get; private set; } = new();

            public HtfStructureEngine(int pivotLength) => _pivotLength = Math.Max(1, pivotLength);

            public StructureUpdateResult Update(int htfIndex, IReadOnlyList<HigherTimeframeAggregator.HtfCandle> s)
            {
                var events = new List<StructureEvent>();

                if (htfIndex <= 0 || htfIndex >= s.Count)
                    return new StructureUpdateResult { Snapshot = LastSnapshot, Events = Array.Empty<StructureEvent>() };

                var current = s[htfIndex];
                var prev = s[htfIndex - 1];

                var candidateIndex = htfIndex - _pivotLength;
                var newHighAdded = false;
                var newLowAdded = false;

                if (candidateIndex >= _pivotLength && candidateIndex + _pivotLength < s.Count)
                {
                    if (IsSwingHigh(candidateIndex, s))
                        newHighAdded = AddSwing(candidateIndex, SwingPointType.High, s);

                    if (IsSwingLow(candidateIndex, s))
                        newLowAdded = AddSwing(candidateIndex, SwingPointType.Low, s);
                }

                var highs = _swings.Where(x => x.Type == SwingPointType.High).OrderBy(x => x.Bar).ToList();
                var lows = _swings.Where(x => x.Type == SwingPointType.Low).OrderBy(x => x.Bar).ToList();

                var lastHigh = highs.LastOrDefault();
                var prevHigh = highs.Count > 1 ? highs[^2] : null;

                var lastLow = lows.LastOrDefault();
                var prevLow = lows.Count > 1 ? lows[^2] : null;

                var trend = DetectTrend(lastHigh, prevHigh, lastLow, prevLow);

                var highLabel = GetLabel(lastHigh, prevHigh, isHigh: true);
                var lowLabel = GetLabel(lastLow, prevLow, isHigh: false);

                if (newHighAdded && lastHigh is not null)
                    events.Add(new StructureEvent { Bar = lastHigh.Bar, Price = lastHigh.Price, EventType = StructureEventType.SwingHigh, Text = $"SH {highLabel} @ {lastHigh.Price:0.00}" });

                if (newLowAdded && lastLow is not null)
                    events.Add(new StructureEvent { Bar = lastLow.Bar, Price = lastLow.Price, EventType = StructureEventType.SwingLow, Text = $"SL {lowLabel} @ {lastLow.Price:0.00}" });

                var breakType = StructureBreakType.None;
                var breakDirection = BreakDirection.None;
                var breakText = string.Empty;
                decimal breakPrice = 0m;

                if (lastHigh is not null && prev.Close <= lastHigh.Price && current.Close > lastHigh.Price)
                {
                    breakDirection = BreakDirection.Up;
                    breakType = trend == TrendDirection.Bearish ? StructureBreakType.CHOCH : StructureBreakType.BOS;
                    breakPrice = lastHigh.Price;
                    breakText = $"{breakType} UP @ {breakPrice:0.00}";
                }
                else if (lastLow is not null && prev.Close >= lastLow.Price && current.Close < lastLow.Price)
                {
                    breakDirection = BreakDirection.Down;
                    breakType = trend == TrendDirection.Bullish ? StructureBreakType.CHOCH : StructureBreakType.BOS;
                    breakPrice = lastLow.Price;
                    breakText = $"{breakType} DOWN @ {breakPrice:0.00}";
                }

                if (breakType != StructureBreakType.None)
                    events.Add(new StructureEvent
                    {
                        Bar = current.BaseEndBar,
                        Price = breakPrice,
                        EventType = breakType == StructureBreakType.BOS ? StructureEventType.BOS : StructureEventType.CHOCH,
                        Text = breakText
                    });

                LastSnapshot = new StructureSnapshot
                {
                    CurrentBar = current.BaseEndBar,
                    CurrentClose = current.Close,
                    Trend = trend,
                    LastSwingHigh = lastHigh,
                    PrevSwingHigh = prevHigh,
                    LastSwingLow = lastLow,
                    PrevSwingLow = prevLow,
                    HighLabel = highLabel,
                    LowLabel = lowLabel,
                    BreakType = breakType,
                    BreakDirection = breakDirection,
                    BreakText = breakText
                };

                return new StructureUpdateResult { Snapshot = LastSnapshot, Events = events };
            }

            private bool AddSwing(int htfIndex, SwingPointType type, IReadOnlyList<HigherTimeframeAggregator.HtfCandle> s)
            {
                var c = s[htfIndex];
                var bar = c.BaseEndBar;

                if (_swings.Any(x => x.Bar == bar && x.Type == type))
                    return false;

                var price = type == SwingPointType.High ? c.High : c.Low;
                _swings.Add(new SwingPoint { Bar = bar, Price = price, Type = type });

                if (_swings.Count > 600)
                    _swings.RemoveRange(0, _swings.Count - 600);

                return true;
            }

            private bool IsSwingHigh(int idx, IReadOnlyList<HigherTimeframeAggregator.HtfCandle> s)
            {
                var center = s[idx].High;
                for (var i = 1; i <= _pivotLength; i++)
                    if (center <= s[idx - i].High || center <= s[idx + i].High) return false;
                return true;
            }

            private bool IsSwingLow(int idx, IReadOnlyList<HigherTimeframeAggregator.HtfCandle> s)
            {
                var center = s[idx].Low;
                for (var i = 1; i <= _pivotLength; i++)
                    if (center >= s[idx - i].Low || center >= s[idx + i].Low) return false;
                return true;
            }

            private static TrendDirection DetectTrend(SwingPoint? lastHigh, SwingPoint? prevHigh, SwingPoint? lastLow, SwingPoint? prevLow)
            {
                if (lastHigh is null || prevHigh is null || lastLow is null || prevLow is null)
                    return TrendDirection.Neutral;

                if (lastHigh.Price > prevHigh.Price && lastLow.Price > prevLow.Price) return TrendDirection.Bullish;
                if (lastHigh.Price < prevHigh.Price && lastLow.Price < prevLow.Price) return TrendDirection.Bearish;
                return TrendDirection.Neutral;
            }

            private static string GetLabel(SwingPoint? last, SwingPoint? prev, bool isHigh)
            {
                if (last is null) return "-";
                if (prev is null) return isHigh ? "H1" : "L1";

                if (isHigh)
                {
                    if (last.Price > prev.Price) return "HH";
                    if (last.Price < prev.Price) return "LH";
                    return "EH";
                }

                if (last.Price > prev.Price) return "HL";
                if (last.Price < prev.Price) return "LL";
                return "EL";
            }
        }

        // =========================
        // HTF Zone Engine（同上一版本完整实现已在文件前半部分）
        // =========================
        private sealed class HtfZoneEngine
        {
            private readonly decimal _tickSize;
            private readonly int _lookback;
            private readonly decimal _minFvgGap;
            private readonly decimal _vpMaxDistancePoints;

            private const decimal MinDisplacementRange = 6m;
            private const decimal FvgMergeOverlapRatio = 0.70m;
            private const decimal FvgMaxRangeMultiplierK = 1.5m;
            private const decimal FvgHardCapPoints = 80m;
            private const decimal FvgMitigationPercent = 0.50m;

            private const int MaxActiveObs = 6;
            private const int MaxActiveFvgs = 6;

            private readonly List<TradingZone> _zones = new();

            public HtfZoneEngine(decimal tickSize, int lookback, int minFvgTicks, decimal vpMaxDistancePoints)
            {
                _tickSize = tickSize <= 0 ? 0.25m : tickSize;
                _lookback = Math.Max(5, lookback);
                _minFvgGap = Math.Max(1, minFvgTicks) * _tickSize;
                _vpMaxDistancePoints = Math.Max(0m, vpMaxDistancePoints);
            }

            public void UpdateByBaseBar(IndicatorCandle? prevBase, IndicatorCandle curBase, Func<HigherTimeframeAggregator.HtfCandle?> getVpCandle)
            {
                var eps = _tickSize / 2m;

                foreach (var z in _zones)
                {
                    if (z.IsInvalidated)
                        continue;

                    var wasTouched = z.IsTouched;

                    var overlapping = curBase.High >= z.Low - eps && curBase.Low <= z.High + eps;
                    if (overlapping)
                        z.IsTouched = true;

                    if (!wasTouched && z.IsTouched && _vpMaxDistancePoints > 0m)
                    {
                        var vp = getVpCandle();
                        if (vp is not null)
                        {
                            var touchPrice = GetTouchPrice(z, curBase);
                            if (!PassVpFilter(vp, touchPrice, _vpMaxDistancePoints))
                                z.IsVpRejected = true;
                        }
                    }

                    switch (z.Type)
                    {
                        case ZoneType.BullishOB:
                            if (curBase.Close < z.Low - eps)
                                z.IsInvalidated = true;
                            break;

                        case ZoneType.BearishOB:
                            if (prevBase is not null &&
                                prevBase.Close > z.High + eps &&
                                curBase.Close > z.High + eps)
                                z.IsInvalidated = true;
                            break;

                        case ZoneType.BullishFVG:
                            {
                                var mid = z.Low + (z.High - z.Low) * FvgMitigationPercent;
                                if (curBase.Low <= mid + eps)
                                    z.IsMitigated = true;
                                if (curBase.Close < z.Low - eps)
                                    z.IsInvalidated = true;
                                break;
                            }

                        case ZoneType.BearishFVG:
                            {
                                var mid = z.Low + (z.High - z.Low) * FvgMitigationPercent;
                                if (curBase.High >= mid - eps)
                                    z.IsMitigated = true;
                                if (curBase.Close > z.High + eps)
                                    z.IsInvalidated = true;
                                break;
                            }
                    }
                }
            }

            public ZoneUpdateResult OnHtfClosed(int htfIndex, IReadOnlyList<HigherTimeframeAggregator.HtfCandle> s, StructureUpdateResult structureResult)
            {
                var newZones = new List<TradingZone>();

                TryAddFvg(htfIndex, s, newZones);
                TryAddOrderBlock(htfIndex, s, structureResult, newZones);

                foreach (var z in newZones)
                    _zones.Add(z);

                return new ZoneUpdateResult { NewZones = newZones, ActiveZones = GetActiveZones() };
            }

            public IReadOnlyList<TradingZone> GetActiveZones()
            {
                var active = _zones.Where(z => !z.IsInvalidated);

                var obs = active
                    .Where(z => z.Type == ZoneType.BullishOB || z.Type == ZoneType.BearishOB)
                    .OrderByDescending(z => z.CreatedBar)
                    .Take(MaxActiveObs);

                var fvgs = active
                    .Where(z => z.Type == ZoneType.BullishFVG || z.Type == ZoneType.BearishFVG)
                    .OrderByDescending(z => z.CreatedBar)
                    .Take(MaxActiveFvgs);

                return obs.Concat(fvgs).OrderByDescending(z => z.CreatedBar).ToList();
            }

            private void TryAddFvg(int htfIndex, IReadOnlyList<HigherTimeframeAggregator.HtfCandle> s, List<TradingZone> newZones)
            {
                if (htfIndex < 2)
                    return;

                var left = s[htfIndex - 2];
                var middle = s[htfIndex - 1];
                var right = s[htfIndex];

                var middleRange = middle.High - middle.Low;
                if (middleRange < MinDisplacementRange)
                    return;

                var maxAllowed = Math.Min(FvgHardCapPoints, middleRange * FvgMaxRangeMultiplierK);

                var bullGap = right.Low - left.High;
                if (bullGap >= _minFvgGap)
                    AddOrMergeFvg(newZones, ZoneType.BullishFVG, left.BaseStartBar, right.BaseEndBar, left.High, right.Low, maxAllowed);

                var bearGap = left.Low - right.High;
                if (bearGap >= _minFvgGap)
                    AddOrMergeFvg(newZones, ZoneType.BearishFVG, left.BaseStartBar, right.BaseEndBar, right.High, left.Low, maxAllowed);
            }

            private void TryAddOrderBlock(int htfIndex, IReadOnlyList<HigherTimeframeAggregator.HtfCandle> s, StructureUpdateResult structureResult, List<TradingZone> newZones)
            {
                if (!structureResult.HasEvents)
                    return;

                foreach (var evt in structureResult.Events)
                {
                    if (evt.EventType != StructureEventType.BOS)
                        continue;

                    if (structureResult.Snapshot.BreakDirection == BreakDirection.Up)
                        TryAddBullishOrderBlock(htfIndex, s, newZones);
                    else if (structureResult.Snapshot.BreakDirection == BreakDirection.Down)
                        TryAddBearishOrderBlock(htfIndex, s, newZones);
                }
            }

            private void TryAddBullishOrderBlock(int htfIndex, IReadOnlyList<HigherTimeframeAggregator.HtfCandle> s, List<TradingZone> newZones)
            {
                var start = Math.Max(0, htfIndex - _lookback);

                for (var i = htfIndex - 1; i >= start; i--)
                {
                    var c = s[i];
                    if (c.Close < c.Open)
                    {
                        AddZone(newZones, ZoneType.BullishOB, c.BaseStartBar, s[htfIndex].BaseEndBar, c.Low, c.Open,
                            $"BullOB {Math.Min(c.Low, c.Open):0.00}-{Math.Max(c.Low, c.Open):0.00}");
                        return;
                    }
                }
            }

            private void TryAddBearishOrderBlock(int htfIndex, IReadOnlyList<HigherTimeframeAggregator.HtfCandle> s, List<TradingZone> newZones)
            {
                var start = Math.Max(0, htfIndex - _lookback);

                for (var i = htfIndex - 1; i >= start; i--)
                {
                    var c = s[i];
                    if (c.Close > c.Open)
                    {
                        AddZone(newZones, ZoneType.BearishOB, c.BaseStartBar, s[htfIndex].BaseEndBar, c.Open, c.High,
                            $"BearOB {Math.Min(c.Open, c.High):0.00}-{Math.Max(c.Open, c.High):0.00}");
                        return;
                    }
                }
            }

            private void AddOrMergeFvg(List<TradingZone> newZones, ZoneType type, int startBar, int createdBar, decimal low, decimal high, decimal maxAllowedRange)
            {
                if (low > high) (low, high) = (high, low);
                if ((high - low) > maxAllowedRange) return;

                var mergedLow = low;
                var mergedHigh = high;
                var mergedStartBar = startBar;
                var mergedCreatedBar = createdBar;

                var toMerge = new HashSet<TradingZone>();

                while (true)
                {
                    var overlaps = _zones
                        .Where(z => !z.IsInvalidated && z.Type == type && GetOverlapRatioByMinRange(mergedLow, mergedHigh, z.Low, z.High) >= FvgMergeOverlapRatio)
                        .ToList();

                    var anyNew = false;

                    foreach (var z in overlaps)
                    {
                        if (toMerge.Add(z))
                        {
                            anyNew = true;
                            mergedLow = Math.Min(mergedLow, z.Low);
                            mergedHigh = Math.Max(mergedHigh, z.High);
                            mergedStartBar = Math.Min(mergedStartBar, z.StartBar);
                            mergedCreatedBar = Math.Max(mergedCreatedBar, z.CreatedBar);
                        }
                    }

                    if (!anyNew) break;
                }

                if ((mergedHigh - mergedLow) > maxAllowedRange) return;

                var mergedTouched = toMerge.Any(z => z.IsTouched);
                var mergedMitigated = toMerge.Any(z => z.IsMitigated);
                var mergedRejected = toMerge.Any(z => z.IsVpRejected);

                var exists = _zones.Any(z =>
                    !z.IsInvalidated &&
                    z.Type == type &&
                    z.StartBar == mergedStartBar &&
                    z.CreatedBar == mergedCreatedBar &&
                    Math.Abs(z.Low - mergedLow) < _tickSize / 2m &&
                    Math.Abs(z.High - mergedHigh) < _tickSize / 2m);

                if (exists) return;

                foreach (var z in toMerge)
                    _zones.Remove(z);

                var name = type == ZoneType.BullishFVG ? "BullFVG" : "BearFVG";

                newZones.Add(new TradingZone
                {
                    Type = type,
                    StartBar = mergedStartBar,
                    CreatedBar = mergedCreatedBar,
                    Low = mergedLow,
                    High = mergedHigh,
                    IsTouched = mergedTouched,
                    IsMitigated = mergedMitigated,
                    IsVpRejected = mergedRejected,
                    Text = $"{name} {mergedLow:0.00}-{mergedHigh:0.00}"
                });
            }

            private void AddZone(List<TradingZone> newZones, ZoneType type, int startBar, int createdBar, decimal low, decimal high, string text)
            {
                if (low > high) (low, high) = (high, low);

                var exists = _zones.Any(z =>
                    z.Type == type &&
                    z.StartBar == startBar &&
                    z.CreatedBar == createdBar &&
                    Math.Abs(z.Low - low) < _tickSize / 2m &&
                    Math.Abs(z.High - high) < _tickSize / 2m);

                if (exists) return;

                newZones.Add(new TradingZone
                {
                    Type = type,
                    StartBar = startBar,
                    CreatedBar = createdBar,
                    Low = low,
                    High = high,
                    Text = text
                });
            }

            private static decimal GetOverlapRatioByMinRange(decimal low1, decimal high1, decimal low2, decimal high2)
            {
                if (low1 > high1) (low1, high1) = (high1, low1);
                if (low2 > high2) (low2, high2) = (high2, low2);

                var range1 = high1 - low1;
                var range2 = high2 - low2;
                if (range1 <= 0 || range2 <= 0) return 0m;

                var intersection = Math.Min(high1, high2) - Math.Max(low1, low2);
                if (intersection <= 0) return 0m;

                var minRange = Math.Min(range1, range2);
                if (minRange <= 0) return 0m;

                return intersection / minRange;
            }

            private static decimal GetTouchPrice(TradingZone zone, IndicatorCandle candle)
            {
                return zone.Type switch
                {
                    ZoneType.BullishOB or ZoneType.BullishFVG => Clamp(candle.Low, zone.Low, zone.High),
                    ZoneType.BearishOB or ZoneType.BearishFVG => Clamp(candle.High, zone.Low, zone.High),
                    _ => candle.Close
                };
            }

            private static decimal Clamp(decimal x, decimal min, decimal max)
            {
                if (min > max) (min, max) = (max, min);
                if (x < min) return min;
                if (x > max) return max;
                return x;
            }

            private static bool PassVpFilter(HigherTimeframeAggregator.HtfCandle vp, decimal price, decimal maxDistancePoints)
            {
                var refs = new List<decimal>(4);

                if (vp.POC != 0m) refs.Add(vp.POC);
                if (vp.VAH != 0m) refs.Add(vp.VAH);
                if (vp.VAL != 0m) refs.Add(vp.VAL);
                if (vp.VWAP != 0m) refs.Add(vp.VWAP);

                if (refs.Count == 0) return true;

                foreach (var r in refs)
                    if (Math.Abs(price - r) <= maxDistancePoints) return true;

                return false;
            }
        }
    }
}