// File: Strategy/NQOrderFlowStrategy.cs
// Version intent (2026-06):
// - Q-Freeze: Zone Quality only evaluated at lock time; no dynamic Q reset while pending/confirmed.
// - Fix: Do NOT reset locked/confirmed session when candidate list becomes empty (quality filter/topN view).
// - Pending lifecycle: controlled by OF window (pre-armed) + pending effWait/expired/runaway.
// - Stop: Swing/Rolling candidates on wrong side are INVALID (no "-> Corrected").
// - Place Distance Gate: avoid placing limit when price already too far from zone (ENTRY_ORDER_SKIP PlaceTooFar).
// - Inner Anchor (Q25/T33): reduce Mid-too-deep expirations.
// - Mitigated policy M1: DisallowMitigatedZones applies only pre-armed (before OF_ARMED / before pending). After armed/pending: keep setup; log once.
// - Adaptive OF wait window: qLock>=HighScore => allow longer wait (e.g. 12), else base (e.g. 8).
//
// NEW (M5 Zones integration):
// - HTF15 zones remain for reference/drawing (optional).
// - Trading candidate pool can switch to M5 FVG zones for higher frequency, still gated by HTF bias.
// - Shadow lifecycle updated on EACH base bar (touched/mitigated/invalidated), not only HTF close.

using ATAS.Indicators;
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

        // M5 确认等待的最长 bar 数（进入区块开始计时）
        private const int MaxConfirmBars = 24;

        // 离开区块太久重置（M5 bar 数）——仅用于“确认前”的等待阶段
        private const int OutOfZoneResetBars = 6;

        // ===== 虚拟执行层（TradePlan V0）=====
        private const decimal RiskRewardR = 2m;       // baseline: 2R
        private const int SlBufferTicks = 4;          // 4 ticks（SwingStop / RollingStop / ZoneStop 共用）
        private const int OrderFlowMinScore = 1;      // >=1/4
        private static readonly decimal SlBufferPoints = SlBufferTicks * TickSizeNq;


        [Category("HTF Bias")]
        [DisplayName("Enable CHOCH Bias Hold (CHOCH后短时间延续上一Bias)")]
        public bool EnableChochBiasHold { get; set; } = true;

        [Category("HTF Bias")]
        [DisplayName("CHOCH Hold HTF Bars (延续多少根HTF收盘)")]
        public int ChochHoldHtfBars { get; set; } = 2;

        // ===== 风险过滤（ticks）=====
        [Category("Risk Filter")]
        [DisplayName("Min Risk (ticks) - 最小止损距离")]
        public int MinRiskTicks { get; set; } = 12;

        [Category("Risk Filter")]
        [DisplayName("Max Risk (ticks) - 最大止损距离")]
        public int MaxRiskTicks { get; set; } = 200;

        // ===== Confirm 优化：确认模式（更贴近区块）=====
        public enum ConfirmMode
        {
            SwingBreak = 0,
            ZoneDisplacement = 1
        }

        [Category("Confirm")]
        [DisplayName("Confirm Mode (确认模式)")]
        public ConfirmMode ConfirmationMode { get; set; } = ConfirmMode.ZoneDisplacement;

        [Category("Confirm")]
        [DisplayName("Zone Displacement Confirm (points) - 触碰后离开区块的最小位移")]

        public decimal ConfirmDisplacementPoints { get; set; } = 2m;

        // ===== Plan：执行模式 =====
        public enum EntryExecutionMode
        {
            MarketClose = 0,
            LimitAtZoneAnchor = 1
        }

        public enum ZoneEntryAnchor
        {
            // keep numeric stability for saved params
            Edge = 0,    // 多：zone.High；空：zone.Low
            Mid = 1,     // zone.Mid
            Quarter = 2, // zone内 25%
            Third = 3    // zone内 33%
        }

        public enum InnerAnchorType
        {
            Quarter = 0,
            Third = 1
        }

        [Category("Plan")]
        [DisplayName("Entry Execution Mode (入场执行模式)")]
        public EntryExecutionMode EntryMode { get; set; } = EntryExecutionMode.LimitAtZoneAnchor;

        [Category("Plan")]
        [DisplayName("Limit Entry Anchor (区块限价锚点)")]
        public ZoneEntryAnchor LimitEntryAnchor { get; set; } = ZoneEntryAnchor.Mid;

        [Category("Plan")]
        [DisplayName("Enable Auto Inner Anchor When Mid (当选择Mid时，自动改为内侧锚点)")]
        public bool EnableAutoInnerAnchorWhenMid { get; set; } = true;

        [Category("Plan")]
        [DisplayName("Auto Inner Anchor Type (Quarter/Third) - Mid替换用哪个")]
        public InnerAnchorType AutoInnerAnchor { get; set; } = InnerAnchorType.Quarter;

        [Category("Plan")]
        [DisplayName("Entry Max Wait Bars After Armed (M5) - Armed后等待回撤成交最大bars(基础值)")]
        public int EntryMaxWaitBarsAfterArmed { get; set; } = 8;

        // =====================================================================
        // 动态锚点 / 自适应等待 / 提前取消
        // =====================================================================
        [Category("Plan")]
        [DisplayName("Enable Dynamic Entry Anchor (宽区块自动用Edge)")]
        public bool EnableDynamicEntryAnchor { get; set; } = true;

        [Category("Plan")]
        [DisplayName("Dynamic Anchor Width Threshold (points) - zoneWidth > 该值用Edge")]
        public decimal DynamicAnchorWidthThresholdPoints { get; set; } = 20m;

        [Category("Plan")]
        [DisplayName("Enable Adaptive Entry Wait (自适应挂单等待bars)")]
        public bool EnableAdaptiveEntryWait { get; set; } = true;

        [Category("Plan")]
        [DisplayName("Adaptive Wait: Mid Anchor Bonus Bars (Mid额外等待)")]
        public int AdaptiveWaitMidBonusBars { get; set; } = 4;

        [Category("Plan")]
        [DisplayName("Adaptive Wait: Quality High Score (>=)")]
        public int AdaptiveWaitQualityHighScore { get; set; } = 8;

        [Category("Plan")]
        [DisplayName("Adaptive Wait: Quality High Bonus Bars")]
        public int AdaptiveWaitQualityHighBonusBars { get; set; } = 4;

        [Category("Plan")]
        [DisplayName("Adaptive Wait: Quality Low Score (<=)")]
        public int AdaptiveWaitQualityLowScore { get; set; } = 6;

        [Category("Plan")]
        [DisplayName("Adaptive Wait: Quality Low Penalty Bars")]
        public int AdaptiveWaitQualityLowPenaltyBars { get; set; } = 2;

        [Category("Plan")]
        [DisplayName("Adaptive Wait: Min Bars")]
        public int AdaptiveWaitMinBars { get; set; } = 2;

        [Category("Plan")]
        [DisplayName("Adaptive Wait: Max Bars")]
        public int AdaptiveWaitMaxBars { get; set; } = 24;

        [Category("Plan")]
        [DisplayName("Enable Early Cancel (Runaway) - 价格顺势远离区块时提前取消")]
        public bool EnableEarlyCancelRunaway { get; set; } = true;

        [Category("Plan")]
        [DisplayName("Early Cancel: Runaway Distance (points)")]
        public decimal RunawayCancelDistancePoints { get; set; } = 30m;

        [Category("Plan")]
        [DisplayName("Early Cancel: Consecutive Bars")]
        public int RunawayCancelConsecutiveBars { get; set; } = 3;

        [Category("Plan")]
        [DisplayName("Early Cancel: Min Wait Bars Before Cancel (避免刚挂就取消)")]
        public int RunawayCancelMinWaitBars { get; set; } = 2;

        // ===== 挂单创建距离闸门 =====
        [Category("Plan")]
        [DisplayName("Enable Place Distance Gate (挂单创建时：离区块过远则不挂)")]
        public bool EnablePlaceDistanceGate { get; set; } = true;

        [Category("Plan")]
        [DisplayName("Max Place Distance From Zone (points) - 创建挂单时Close离区块最大距离")]
        public decimal MaxPlaceDistanceFromZonePoints { get; set; } = 60m;

        // ===== 防追价（Anti-Chase）=====
        [Category("Plan")]
        [DisplayName("Enable Anti-Chase Filter (防追价：入场离区块过远则不进)")]
        public bool EnableAntiChaseFilter { get; set; } = true;

        [Category("Plan")]
        [DisplayName("Max Entry Distance From Zone (points) - 入场离区块最大距离")]
        public decimal MaxEntryDistanceFromZonePoints { get; set; } = 8m;

        // ===== 订单流等待窗口 =====
        [Category("Plan")]
        [DisplayName("Enable OrderFlow Time Window (Confirmed后限定等待OF的bar数)")]
        public bool EnableOrderFlowTimeWindow { get; set; } = true;

        [Category("Plan")]
        [DisplayName("OrderFlow Max Wait Bars After Confirmed (M5) - 基础值(中低Q)")]
        public int OrderFlowMaxWaitBarsAfterConfirmed { get; set; } = 8;

        // ===== NEW：自适应订单流等待窗口（按 qLock）=====
        [Category("Plan")]
        [DisplayName("Enable Adaptive OF Wait Window (按qLock动态调整等待OF的bars)")]
        public bool EnableAdaptiveOrderFlowWaitWindow { get; set; } = true;

        [Category("Plan")]
        [DisplayName("Adaptive OF Wait: High Q Score (>=)")]
        public int AdaptiveOrderFlowHighScore { get; set; } = 8;

        [Category("Plan")]
        [DisplayName("Adaptive OF Wait: High Q Wait Bars")]
        public int AdaptiveOrderFlowHighWaitBars { get; set; } = 12;

        // ===== RollingStop（自适应 lookback，作为第三止损候选）=====
        [Category("Plan")]
        [DisplayName("Enable RollingStop Candidate (第三止损候选)")]
        public bool EnableRollingStop { get; set; } = true;

        [Category("Plan")]
        [DisplayName("RollingStop Max Lookback Bars (M5, start, default=6)")]
        public int RollingStopLookbackBars { get; set; } = 6;

        [Category("Plan")]
        [DisplayName("RollingStop Min Lookback Bars (M5, end, default=2)")]
        public int RollingStopMinLookbackBars { get; set; } = 2;

        // =====================================================================
        // Zone Quality Score（0-10）
        // =====================================================================
        [Category("Zone Quality")]
        [DisplayName("Enable Zone Quality Score Filter (启用区块质量评分过滤)")]
        public bool EnableZoneQualityScoreFilter { get; set; } = true;

        [Category("Zone Quality")]
        [DisplayName("Min Zone Quality Score (0-10) - 最小质量分")]
        public int MinZoneQualityScore { get; set; } = 6;

        [Category("Zone Quality")]
        [DisplayName("Quality: Ideal Zone Width Min (points)")]
        public decimal QualityIdealWidthMinPoints { get; set; } = 1.0m;

        [Category("Zone Quality")]
        [DisplayName("Quality: Ideal Zone Width Max (points)")]
        public decimal QualityIdealWidthMaxPoints { get; set; } = 10.0m;

        [Category("Zone Quality")]
        [DisplayName("Quality: Fresh Strong Bars (<= gives max freshness score)")]
        public int QualityFreshStrongBars { get; set; } = 60;

        [Category("Zone Quality")]
        [DisplayName("Quality: Fresh Ok Bars (<= gives mid freshness score)")]
        public int QualityFreshOkBars { get; set; } = 180;

        [Category("Zone Quality")]
        [DisplayName("Quality: VP Near Strong Dist (points)")]
        public decimal QualityVpStrongDistPoints { get; set; } = 1.0m;

        [Category("Zone Quality")]
        [DisplayName("Quality: VP Near Ok Dist (points)")]
        public decimal QualityVpOkDistPoints { get; set; } = 3.0m;

        [Category("Zone Quality")]
        [DisplayName("Quality: VP Near Weak Dist (points)")]
        public decimal QualityVpWeakDistPoints { get; set; } = 6.0m;

        // ===== Zone 过滤（硬过滤）=====
        [Category("Zone Filter")]
        [DisplayName("Disallow Mitigated Zones (不交易已回补50%的区块)")]
        public bool DisallowMitigatedZones { get; set; } = false;

        // ===== 风控/管理：Break-even（最小实现）=====
        [Category("Plan")]
        [DisplayName("Enable BreakEven Move (到达指定R后移动止损到保本)")]
        public bool EnableBreakEvenMove { get; set; } = false;

        [Category("Plan")]
        [DisplayName("BreakEven At R (e.g. 1.0 = 到达1R后保本)")]
        public decimal BreakEvenAtR { get; set; } = 1m;

        [Category("Plan")]
        [DisplayName("BreakEven Plus Ticks (保本上移/下移ticks，默认0)")]
        public int BreakEvenPlusTicks { get; set; } = 0;

        // ===== 回放统计：美元口径参数 =====
        [Category("PnL ($)")]
        [DisplayName("Contracts (数量/手数)")]
        public int Contracts { get; set; } = 1;

        [Category("PnL ($)")]
        [DisplayName("Tick Value ($/tick/contract)")]
        public decimal TickValuePerContract { get; set; } = 0.5m;

        // ===== 冷却设置 =====
        [Category("Plan")]
        [DisplayName("Enable Cooldown")]
        public bool EnableCooldown { get; set; } = true;

        [Category("Plan")]
        [DisplayName("Cooldown Bars (M5)")]
        public int CooldownBars { get; set; } = 6;

        // ===== Confirm（参考Swing fallback）=====
        [Category("Confirm")]
        [DisplayName("Enable RefSwing Fallback (Rolling High/Low)")]
        public bool EnableRefSwingFallback { get; set; } = true;

        [Category("Confirm")]
        [DisplayName("Ref Fallback Lookback Bars (rolling)")]
        public int RefFallbackLookbackBars { get; set; } = 12;

        [Category("Confirm")]
        [DisplayName("Ref Max Distance (points) - 参考Swing过远阈值")]
        public decimal RefMaxDistancePoints { get; set; } = 15m;

        [Category("Confirm")]
        [DisplayName("Ref Max Age (bars) - 参考Swing过旧阈值")]
        public int RefMaxAgeBars { get; set; } = 60;

        // ===== NEW: M5 Zones (LTF) =====
        [Category("M5 Zones")]
        [DisplayName("Enable M5 Zones For Trading (用M5区块作为交易候选池)")]
        public bool EnableM5ZonesForTrading { get; set; } = true;

        [Category("M5 Zones")]
        [DisplayName("M5 Min FVG Gap (ticks)")]
        public int M5MinFvgTicks { get; set; } = 2;

        [Category("M5 Zones")]
        [DisplayName("M5 Enable Displacement Filter")]
        public bool M5EnableDisplacementFilter { get; set; } = true;

        [Category("M5 Zones")]
        [DisplayName("M5 Min Displacement Range (points)")]
        public decimal M5MinDisplacementRangePoints { get; set; } = 2.0m;

        [Category("M5 Zones")]
        [DisplayName("M5 Max Zone Width (points, 0 disables)")]
        public decimal M5MaxZoneWidthPoints { get; set; } = 0m;

        [Category("M5 Zones")]
        [DisplayName("M5 Merge Overlap Ratio (0-1)")]
        public decimal M5MergeOverlapRatio { get; set; } = 0.70m;

        [Category("M5 Zones")]
        [DisplayName("M5 Max Zones Per Type")]
        public int M5MaxZonesPerType { get; set; } = 24;

        [Category("M5 Zones")]
        [DisplayName("M5 Max Lookback Bars (0 disables)")]
        public int M5MaxLookbackBars { get; set; } = 800;

        [Category("M5 Zones")]
        [DisplayName("Render HTF Zones (画HTF区块)")]
        public bool RenderHtfZones { get; set; } = true;

        [Category("M5 Zones")]
        [DisplayName("Render M5 Zones (画M5区块)")]
        public bool RenderM5Zones { get; set; } = true;

        // ===== 日志（文件）=====
        [Category("Log")]
        [DisplayName("Enable File Log (保存触发过滤原因到文件)")]
        public bool EnableFileLog { get; set; } = true;

        [Category("Log")]
        [DisplayName("Log Same Reason Every N Bars (同原因降频)")]
        public int LogSameReasonEveryNBars { get; set; } = 20;

        [Category("Log")]
        [DisplayName("Log Folder Name (under %APPDATA%\\ATAS\\)")]
        public string LogFolderName { get; set; } = "StrategyLogs";

        [Category("Log")]
        [DisplayName("Log File Name")]
        public string LogFileName { get; set; } = "NQOrderFlowV1.log";

        // ===== HUD =====
        [Category("HUD")]
        [DisplayName("Show HUD")]
        public bool ShowHud { get; set; } = true;

        [Category("HUD")]
        [DisplayName("Compact HUD (极简)")]
        public bool CompactHud { get; set; } = true;

        [Category("HUD")]
        [DisplayName("HUD显示的活动区块数量")]
        public int HudActiveZonesCount { get; set; } = 3;

        // ===== Trade Log on HUD =====
        [Category("HUD")]
        [DisplayName("Show Trade Log (回测记录)")]
        public bool ShowTradeLog { get; set; } = true;

        [Category("HUD")]
        [DisplayName("Trade Log Count (显示最近几笔)")]
        public int TradeLogCount { get; set; } = 5;

        // ===== 服务/引擎 =====
        private DataProbeService? _probe;

        private HigherTimeframeAggregator? _htfAgg;
        private readonly List<HigherTimeframeAggregator.HtfCandle> _htfSeries = new();

        private HtfStructureEngine? _htfStructure;
        private HtfZoneEngine? _htfZones;

        private M5ZoneEngine? _m5Zones;

        private OrderFlowEngine? _orderFlow;
        private OrderFlowResult? _lastOrderFlow;

        // ===== HTF Bias（最近一次 HTF BOS）=====
        private TrendDirection _htfBias = TrendDirection.Neutral;
        private string _htfBiasReason = "-";

        private TrendDirection _lastDirectionalBias = TrendDirection.Neutral;
        private int _chochHoldUntilHtfIndex = -1;
        private bool _biasHeldByChoch = false;

        // ===== VP参考：上一根已收盘桶 =====
        private HigherTimeframeAggregator.HtfCandle? _lastClosedVpCandle;

        // ===== M5 结构 =====
        private StructureEngine? _m5Structure;
        private StructureSnapshot? _lastM5StructureSnapshot;

        // ===== Replay reset =====
        private DateTime? _lastBaseTime;
        private static PropertyInfo? _baseTimeProp;

        // ===== 渲染缓存 =====
        private readonly object _renderLock = new();
        private List<TradingZone> _activeZonesForRender = new();
        private string _hudText = string.Empty;
        private TradePlan? _planForRender;

        // =========================
        // M5 确认层
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

        private int _outOfZoneBars = 0;
        private int _confirmedAtBar = -1;

        // 参考 swing / displacement
        private decimal? _confirmRefPrice;
        private int _confirmRefBar = -1;
        private string _confirmRefText = "-";
        private bool _confirmRefIsFallback = false;

        private string _confirmText = "-";

        // ===== 锁定区块质量分缓存（Q-Freeze）=====
        private int _lockedZoneQualityScore = -1;
        private string _lockedZoneQualityDetail = "-";

        // ===== Mitigated M1：当已Armed/pending时，变Mitigated只记录，不砍单（一次性日志）=====
        private bool _loggedMitigatedKeepOnce = false;

        // =========================
        // Fix A：锁定区块 shadow
        // =========================
        private TradingZone? _confirmZoneShadow;
        private bool _confirmZoneUsingShadow = false;
        private bool _loggedShadowUseOnce = false;

        // =========================
        // OF Armed
        // =========================
        private bool _ofArmed = false;
        private int _ofArmedAtBar = -1;
        private int _ofArmedScore = 0;
        private string _ofArmedText = "-";

        // =========================
        // 入场：虚拟限价（回撤成交）
        // =========================
        private sealed class PendingEntry
        {
            public int CreatedBar { get; init; }
            public string Side { get; init; } = "LONG";
            public decimal LimitPrice { get; init; }
            public string AnchorText { get; init; } = "-";
            public ZoneEntryAnchor AnchorUsed { get; init; } = ZoneEntryAnchor.Mid;

            public int EffectiveMaxWaitBars { get; init; } = 8;

            // runaway tracking
            public int RunawayBars { get; set; } = 0;

            public ZoneKey Zone { get; init; } = new(ZoneType.BullishFVG, 0, 0, 0, 0);

            public int OfScore { get; init; }
            public string OfText { get; init; } = "-";

            public int LockedQScore { get; init; } = -1;
        }

        private PendingEntry? _pendingEntry;

        // =========================
        // TradePlan
        // =========================
        private enum PlanState
        {
            Flat,
            InPosition
        }

        private PlanState _planState = PlanState.Flat;
        private TradePlan? _activePlan;

        private int _cooldownUntilBar = -1;
        private int _lastTriggeredConfirmStartBar = -1;
        private ZoneKey? _lastTriggeredZoneKey;

        private sealed class TradePlan
        {
            public int CreatedBar { get; init; }
            public string Side { get; init; } = "LONG"; // LONG / SHORT

            public decimal Entry { get; init; }
            public decimal Stop { get; set; }
            public decimal Target { get; init; }

            public decimal InitialStop { get; init; }
            public decimal InitialRiskPoints { get; init; }

            public bool BreakEvenMoved { get; set; } = false;

            public ZoneKey Zone { get; init; } = new(ZoneType.BullishFVG, 0, 0, 0, 0);

            public int OfScore { get; init; }
            public string OfText { get; init; } = "-";

            public string ExitReason { get; set; } = "-";
            public int? ExitBar { get; set; }
            public decimal? ExitPrice { get; set; }
        }

        private sealed class TradeRecord
        {
            public int EntryBar { get; init; }
            public int ExitBar { get; init; }

            public string Side { get; init; } = "LONG";

            public decimal Entry { get; init; }
            public decimal Stop { get; init; }
            public decimal Target { get; init; }
            public decimal Exit { get; init; }

            public decimal R { get; init; }

            public decimal RiskDollar { get; init; }
            public decimal PnLDollar { get; init; }

            public string ExitReason { get; init; } = "-";

            public int OfScore { get; init; }
            public string OfText { get; init; } = "-";

            public ZoneKey Zone { get; init; } = new(ZoneType.BullishFVG, 0, 0, 0, 0);
        }

        private readonly List<TradeRecord> _tradeHistory = new();
        private int _tradeWins = 0;
        private int _tradeLosses = 0;
        private decimal _netR = 0m;
        private decimal _netPnLDollar = 0m;

        // =========================
        // 触发过滤原因（HUD可诊断）
        // =========================
        private enum TriggerBlockReason
        {
            None = 0,

            InPosition,
            CooldownActive,
            DuplicateTriggerSameSession,

            NotConfirmed,

            OrderFlowNotReady,
            OrderFlowScoreInsufficient,
            OrderFlowWaitTimeout,

            BiasNeutral,

            ConfirmZoneKeyMissing,
            ZoneNotFound,
            ZoneInvalidated,
            ZoneVpRejected,

            ZoneQualityInsufficient,

            EntryPlaceTooFarFromZone,
            EntryTooFarFromZone,
            EntryOrderPending,
            EntryOrderExpired,
            EntryOrderEarlyCanceled,

            RiskInvalid,
            RiskTicksTooSmall,
            RiskTicksTooLarge
        }

        private TriggerBlockReason _barBlockReason = TriggerBlockReason.None;
        private string _barBlockDetail = "-";

        private TriggerBlockReason _lastBlockReason = TriggerBlockReason.None;
        private string _lastBlockDetail = "-";
        private int _lastBlockBar = -1;

        // =========================
        // 文件日志状态
        // =========================
        private string? _logPath;
        private ConfirmPhase _lastLoggedPhase = (ConfirmPhase)(-1);
        private PlanState _lastLoggedPlanState = (PlanState)(-1);
        private TriggerBlockReason _lastLoggedBlockReason = (TriggerBlockReason)(-1);
        private int _lastLoggedBar = -1;
        private int _sameReasonRun = 0;

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

                // visually distinguish M5 zones slightly (more transparent)
                if (IsM5ZoneText(zone.Text))
                {
                    fillColor = Color.FromArgb(Math.Max(6, fillColor.A / 2), fillColor);
                    borderColor = Color.FromArgb(Math.Max(18, borderColor.A / 2), borderColor);
                }

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
                var size = context.MeasureString(label, font);
                var rect = new Rectangle(x1 + 6, y - (int)size.Height - 2, (int)size.Width + 8, (int)size.Height + 4);

                context.FillRectangle(Color.FromArgb(120, 0, 0, 0), rect);
                context.DrawString(label, font, color, rect.X + 4, rect.Y + 2);
            }

            DrawHLine(plan.Entry, Color.DeepSkyBlue, $"{plan.Side} ENTRY {plan.Entry:0.00}");
            DrawHLine(plan.Stop, Color.OrangeRed, $"SL {plan.Stop:0.00}");
            DrawHLine(plan.Target, Color.LimeGreen, $"TP {plan.Target:0.00} ({RiskRewardR:0.0}R)");
        }

        private void DrawHudTopCenter(RenderContext context, string hud)
        {
            if (string.IsNullOrWhiteSpace(hud))
                return;

            var font = new RenderFont("Consolas", 12);
            var size = context.MeasureString(hud, font);

            const int padX = 10;
            const int padY = 8;

            var boxW = (int)Math.Ceiling((double)size.Width) + padX * 2;
            var boxH = (int)Math.Ceiling((double)size.Height) + padY * 2;

            var x = ChartArea.X + (ChartArea.Width - boxW) / 2;
            var y = ChartArea.Y + 6;

            var rect = new Rectangle(x, y, boxW, boxH);

            context.FillRectangle(Color.FromArgb(110, 0, 0, 0), rect);
            context.DrawRectangle(new RenderPen(Color.FromArgb(160, 40, 40, 40), 1), rect);

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

            var curTime = TryGetBaseCandleTime(cur);
            if (curTime is not null && _lastBaseTime is not null && curTime.Value < _lastBaseTime.Value)
                ResetAllState("Replay时间倒退（自动清空统计/引擎状态）");

            if (curTime is not null)
                _lastBaseTime = curTime;

            ResetBarBlock();
            EnsureLogInitialized();

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

            _m5Structure ??= new StructureEngine(GetCandle, pivotLength: 1);
            var m5Struct = _m5Structure.Update(bar);
            _lastM5StructureSnapshot = m5Struct.Snapshot;

            // ===== M5 zones engine =====
            _m5Zones ??= new M5ZoneEngine(GetCandle, tickSize: TickSizeNq);

            // apply params each bar (safe for live tweaking)
            ApplyM5ZoneParamsToEngine(_m5Zones);

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

                // For M5 trading zones, shadow invalidation should be handled by base bars.
                // Keep HTF close invalidation only for non-M5 zones (i.e., HTF zones).
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

            // optional debug log for m5 new zones (very light, keep disabled by default via LogSameReasonEveryNBars)
            if (EnableFileLog && m5ZoneResult.NewZones.Count > 0)
            {
                AppendLog($"M5_ZONE_NEW bar={bar} count={m5ZoneResult.NewZones.Count} ex={m5ZoneResult.NewZones[0].ToShortText()}");
            }
        }

        private void ApplyM5ZoneParamsToEngine(M5ZoneEngine eng)
        {
            eng.EnableFvg = true;

            eng.MinFvgTicks = Math.Max(1, M5MinFvgTicks);

            eng.EnableDisplacementFilter = M5EnableDisplacementFilter;
            eng.MinDisplacementRangePoints = Math.Max(0m, M5MinDisplacementRangePoints);

            eng.MaxZoneWidthPoints = Math.Max(0m, M5MaxZoneWidthPoints);
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

            _ofArmed = false;
            _ofArmedAtBar = -1;
            _ofArmedScore = 0;
            _ofArmedText = "-";
            _pendingEntry = null;

            _lockedZoneQualityScore = -1;
            _lockedZoneQualityDetail = "-";

            _loggedMitigatedKeepOnce = false;

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

            // 如果没有新 break 事件，也要处理 “CHOCH hold 到期”
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

            // BOS：正常切换方向（并刷新 lastDirectionalBias）
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

                _lastDirectionalBias = _htfBias;
                _biasHeldByChoch = false;
                _chochHoldUntilHtfIndex = -1;
                return;
            }

            // CHOCH：不立刻 Neutral，而是短时间延续上一 Bias
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
                    _htfBias = TrendDirection.Neutral;
                    _htfBiasReason = $"CHOCH({r.Snapshot.BreakDirection})";
                    _biasHeldByChoch = false;
                    _chochHoldUntilHtfIndex = -1;
                }

                return;
            }
        }

        private static bool IsM5ZoneText(string? text)
            => !string.IsNullOrWhiteSpace(text) && text.StartsWith("M5", StringComparison.OrdinalIgnoreCase);

        // =========================
        // Fix A：shadow invalidation（HTF close 口径复刻）
        // - Only applies to HTF zones (non-M5).
        // =========================
        private void UpdateConfirmShadowInvalidationByHtfClose(int htfIndex)
        {
            if (_confirmZoneShadow is null || _confirmZoneShadow.IsInvalidated)
                return;

            // if locked zone is M5 zone, do NOT use HTF-close invalidation.
            if (IsM5ZoneText(_confirmZoneShadow.Text))
                return;

            if (htfIndex < 0 || htfIndex >= _htfSeries.Count)
                return;

            var cur = _htfSeries[htfIndex];
            var prev = htfIndex > 0 ? _htfSeries[htfIndex - 1] : null;

            if (cur.BaseEndBar <= _confirmZoneShadow.CreatedBar)
                return;

            var halfTick = TickSizeNq / 2m;

            var invalidatedNow = false;

            switch (_confirmZoneShadow.Type)
            {
                case ZoneType.BullishOB:
                    if (cur.Close < _confirmZoneShadow.Low - halfTick)
                        invalidatedNow = true;
                    break;

                case ZoneType.BearishOB:
                    if (prev is not null &&
                        prev.BaseEndBar > _confirmZoneShadow.CreatedBar &&
                        prev.Close > _confirmZoneShadow.High + halfTick &&
                        cur.Close > _confirmZoneShadow.High + halfTick)
                    {
                        invalidatedNow = true;
                    }
                    break;

                case ZoneType.BullishFVG:
                    if (cur.Close < _confirmZoneShadow.Low - halfTick)
                        invalidatedNow = true;
                    break;

                case ZoneType.BearishFVG:
                    if (cur.Close > _confirmZoneShadow.High + halfTick)
                        invalidatedNow = true;
                    break;
            }

            if (!invalidatedNow)
                return;

            _confirmZoneShadow.IsInvalidated = true;

            if (_confirmZoneKey is not null &&
                ZoneMatchesKey(_confirmZoneShadow, _confirmZoneKey) &&
                _phase is ConfirmPhase.WaitM5Bos or ConfirmPhase.Confirmed)
            {
                AppendLog($"LOCKED_ZONE_INVALIDATED_BY_HTF closeBar={cur.BaseEndBar} zone={_confirmZoneShadow.ToShortText()} X -> reset confirm");
                ResetConfirm(cur.BaseEndBar, "锁定区块HTF收盘失效");
                _phase = ConfirmPhase.WaitZoneTouch;
            }
        }

        // =========================
        // Shadow lifecycle update by base bar (M5):
        // - touched/mitigated/invalidated updated each bar to keep shadow correct
        //   when locked zone falls out of ActiveZones view (cap/cleanup).
        // =========================
        private void UpdateConfirmShadowStateByBaseBar(int bar, IndicatorCandle? prevBase, IndicatorCandle curBase)
        {
            if (_confirmZoneShadow is null || _confirmZoneShadow.IsInvalidated)
                return;

            if (bar <= _confirmZoneShadow.CreatedBar)
                return;

            var eps = TickSizeNq / 2m;

            // touched
            var overlapped =
                curBase.High >= _confirmZoneShadow.Low - eps &&
                curBase.Low <= _confirmZoneShadow.High + eps;

            if (overlapped)
                _confirmZoneShadow.IsTouched = true;

            // mitigated (FVG only) - 75% fill
            if (_confirmZoneShadow.IsTouched && !_confirmZoneShadow.IsMitigated)
            {
                var range = _confirmZoneShadow.High - _confirmZoneShadow.Low;
                if (range > 0m)
                {
                    const decimal fill = 0.75m;

                    if (_confirmZoneShadow.Type == ZoneType.BullishFVG)
                    {
                        var level = _confirmZoneShadow.High - range * fill;
                        if (curBase.Low <= level + eps)
                            _confirmZoneShadow.IsMitigated = true;
                    }
                    else if (_confirmZoneShadow.Type == ZoneType.BearishFVG)
                    {
                        var level = _confirmZoneShadow.Low + range * fill;
                        if (curBase.High >= level - eps)
                            _confirmZoneShadow.IsMitigated = true;
                    }
                }
            }

            // invalidation (close-confirmed)
            var invalidatedNow = false;

            switch (_confirmZoneShadow.Type)
            {
                case ZoneType.BullishOB:
                    if (curBase.Close < _confirmZoneShadow.Low - eps)
                        invalidatedNow = true;
                    break;

                case ZoneType.BearishOB:
                    if (prevBase is not null &&
                        prevBase.Close > _confirmZoneShadow.High + eps &&
                        curBase.Close > _confirmZoneShadow.High + eps)
                    {
                        invalidatedNow = true;
                    }
                    break;

                case ZoneType.BullishFVG:
                    if (curBase.Close < _confirmZoneShadow.Low - eps)
                        invalidatedNow = true;
                    break;

                case ZoneType.BearishFVG:
                    if (curBase.Close > _confirmZoneShadow.High + eps)
                        invalidatedNow = true;
                    break;
            }

            if (!invalidatedNow)
                return;

            _confirmZoneShadow.IsInvalidated = true;

            if (_confirmZoneKey is not null &&
                ZoneMatchesKey(_confirmZoneShadow, _confirmZoneKey) &&
                _phase is ConfirmPhase.WaitM5Bos or ConfirmPhase.Confirmed)
            {
                AppendLog($"LOCKED_ZONE_INVALIDATED_BY_BASE bar={bar} zone={_confirmZoneShadow.ToShortText()} X -> reset confirm");
                ResetConfirm(bar, "锁定区块M5收盘失效");
                _phase = ConfirmPhase.WaitZoneTouch;
            }
        }

        // =========================
        // Zone Quality Score (0-10)
        // =========================
        private bool TryEvaluateZoneQualityScore(int bar, TradingZone z, out int score, out string detail)
        {
            score = 0;
            detail = "-";

            var stateScore =
                !z.IsTouched ? 2 :
                (z.IsTouched && !z.IsMitigated ? 1 : 0);

            var width = Math.Abs(z.High - z.Low);
            var idealMin = Math.Max(0m, QualityIdealWidthMinPoints);
            var idealMax = Math.Max(idealMin, QualityIdealWidthMaxPoints);

            var widthScore =
                (width >= idealMin && width <= idealMax) ? 3 :
                (width >= idealMin * 0.5m && width <= idealMax * 2m) ? 2 :
                0;

            var strongBars = Math.Max(1, QualityFreshStrongBars);
            var okBars = Math.Max(strongBars, QualityFreshOkBars);

            var ageBars = z.CreatedBar >= 0 ? Math.Max(0, bar - z.CreatedBar) : int.MaxValue;

            var freshScore =
                ageBars <= strongBars ? 2 :
                ageBars <= okBars ? 1 :
                0;

            var vpScore = 0;
            string vpRefName = "-";
            decimal vpRefPrice = 0m;
            decimal vpDist = 0m;

            if (TryGetNearestVpRefDistanceToZone(z, out vpRefName, out vpRefPrice, out vpDist))
            {
                var strongD = Math.Max(0m, QualityVpStrongDistPoints);
                var okD = Math.Max(strongD, QualityVpOkDistPoints);
                var weakD = Math.Max(okD, QualityVpWeakDistPoints);

                vpScore =
                    vpDist <= strongD ? 3 :
                    vpDist <= okD ? 2 :
                    vpDist <= weakD ? 1 :
                    0;
            }
            else
            {
                vpScore = 1;
            }

            score = stateScore + widthScore + freshScore + vpScore;

            if (score < 0) score = 0;
            if (score > 10) score = 10;

            detail =
                $"Q={score}/10 | state={stateScore}/2({(z.IsMitigated ? "Mitigated" : (z.IsTouched ? "Touched" : "Untouched"))})" +
                $" | width={width:0.00}pt->{widthScore}/3(ideal {idealMin:0.##}-{idealMax:0.##})" +
                $" | age={ageBars}b->{freshScore}/2(<= {strongBars}/{okBars})" +
                $" | vp={vpRefName}@{(vpRefPrice == 0m ? "-" : vpRefPrice.ToString("0.00"))} dist={vpDist:0.00}pt->{vpScore}/3";

            return true;
        }

        private bool TryGetNearestVpRefDistanceToZone(TradingZone z, out string refName, out decimal refPrice, out decimal dist)
        {
            refName = "-";
            refPrice = 0m;
            dist = 0m;

            var vp = _lastClosedVpCandle;
            if (vp is null)
                return false;

            var refs = new List<(string Name, decimal Price)>(4);
            if (vp.POC != 0m) refs.Add(("POC", vp.POC));
            if (vp.VAH != 0m) refs.Add(("VAH", vp.VAH));
            if (vp.VAL != 0m) refs.Add(("VAL", vp.VAL));
            if (vp.VWAP != 0m) refs.Add(("VWAP", vp.VWAP));

            if (refs.Count == 0)
                return false;

            decimal distToZone(decimal p) => GetDistanceToRangePoints(p, z.Low, z.High);

            var best = refs[0];
            var bestDist = distToZone(best.Price);

            for (var i = 1; i < refs.Count; i++)
            {
                var d = distToZone(refs[i].Price);
                if (d < bestDist)
                {
                    best = refs[i];
                    bestDist = d;
                }
            }

            refName = best.Name;
            refPrice = best.Price;
            dist = bestDist;
            return true;
        }

        private bool IsZoneTradeableByQuality(int bar, TradingZone z, out int qScore, out string qDetail)
        {
            qScore = -1;
            qDetail = "-";

            // 候选池阶段：Mitigated硬过滤（M1 保持：只用于锁定前/未Armed阶段）
            if (DisallowMitigatedZones && z.IsMitigated)
            {
                qScore = 0;
                qDetail = $"区块质量：Mitigated且禁止交易 | {z.ToShortText()}";
                return false;
            }

            if (!EnableZoneQualityScoreFilter)
                return true;

            _ = TryEvaluateZoneQualityScore(bar, z, out qScore, out qDetail);

            var min = Math.Max(0, Math.Min(10, MinZoneQualityScore));
            return qScore >= min;
        }

        // =========================
        // Stop selection：Swing -> Rolling(adaptive) -> Zone
        // Swing/Rolling 反面直接判无效（不做 Corrected）
        // =========================
        private enum StopSelectFailReason
        {
            Invalid,
            TooSmall,
            TooLarge,
            MixedOutOfRange
        }

        private readonly record struct StopEval(
            decimal Stop,
            string Source,
            decimal RiskPoints,
            int RiskTicks,
            bool InRange,
            bool IsValid);

        private bool TrySelectStop(
            int bar,
            bool isLong,
            decimal entry,
            TradingZone zone,
            bool usedShadow,
            out decimal stop,
            out string stopSource,
            out int riskTicks,
            out decimal riskPoints,
            out StopSelectFailReason failReason,
            out string failDetail)
        {
            stop = 0m;
            stopSource = "-";
            riskTicks = 0;
            riskPoints = 0m;
            failReason = StopSelectFailReason.Invalid;
            failDetail = "-";

            var minTicks = Math.Max(0, MinRiskTicks);
            var maxTicks = Math.Max(minTicks, MaxRiskTicks);

            bool IsValidSide(decimal s)
                => isLong ? (s <= entry - TickSizeNq) : (s >= entry + TickSizeNq);

            StopEval EvalCandidate(decimal rawStop, string rawSource)
            {
                if (!IsValidSide(rawStop))
                {
                    return new StopEval(
                        Stop: rawStop,
                        Source: rawSource + " (InvalidSide)",
                        RiskPoints: 0m,
                        RiskTicks: 0,
                        InRange: false,
                        IsValid: false);
                }

                var rp = Math.Abs(entry - rawStop);
                if (rp < TickSizeNq)
                {
                    return new StopEval(
                        Stop: rawStop,
                        Source: rawSource + " (TooClose)",
                        RiskPoints: rp,
                        RiskTicks: 0,
                        InRange: false,
                        IsValid: false);
                }

                var rtDec = rp / TickSizeNq;
                var rt = (int)Math.Round((double)rtDec, MidpointRounding.AwayFromZero);

                var ok = rt >= minTicks && rt <= maxTicks;

                return new StopEval(
                    Stop: rawStop,
                    Source: rawSource,
                    RiskPoints: rp,
                    RiskTicks: rt,
                    InRange: ok,
                    IsValid: true);
            }

            static int DistToRange(int rt, int min, int max)
            {
                if (rt <= 0) return int.MaxValue / 2;
                if (rt < min) return min - rt;
                if (rt > max) return rt - max;
                return 0;
            }

            var zoneStopRaw = GetFallbackStopFromZone(isLong, zone);
            var zoneSrc = usedShadow ? "ZoneStop(Shadow)" : "ZoneStop";
            zoneSrc += isLong ? $"@{zone.Low:0.00}" : $"@{zone.High:0.00}";
            var zoneEval = EvalCandidate(zoneStopRaw, zoneSrc);

            StopEval? swingEval = null;
            if (_lastM5StructureSnapshot is not null)
            {
                if (isLong && _lastM5StructureSnapshot.LastSwingLow is not null)
                {
                    var sl = _lastM5StructureSnapshot.LastSwingLow.Price;
                    var raw = sl - SlBufferPoints;
                    var src = (usedShadow ? "M5SL(Shadow)" : "M5SL") + $"@{sl:0.00}";
                    var ev = EvalCandidate(raw, src);
                    if (ev.IsValid) swingEval = ev;
                }
                else if (!isLong && _lastM5StructureSnapshot.LastSwingHigh is not null)
                {
                    var sh = _lastM5StructureSnapshot.LastSwingHigh.Price;
                    var raw = sh + SlBufferPoints;
                    var src = (usedShadow ? "M5SH(Shadow)" : "M5SH") + $"@{sh:0.00}";
                    var ev = EvalCandidate(raw, src);
                    if (ev.IsValid) swingEval = ev;
                }
            }

            StopEval? rollingSelected = null;
            StopEval? rollingBest = null;
            string rollingTriedText = "-";

            if (EnableRollingStop)
            {
                var maxLb = Math.Max(2, RollingStopLookbackBars);
                var minLb = Math.Max(2, RollingStopMinLookbackBars);
                if (minLb > maxLb) minLb = maxLb;

                rollingTriedText = $"{maxLb}->{minLb}";

                var rollingEvals = new List<StopEval>(capacity: Math.Max(1, maxLb - minLb + 1));

                for (var lb = maxLb; lb >= minLb; lb--)
                {
                    if (isLong)
                    {
                        if (!TryGetRollingLow(bar, lb, out var rl, out var rlBar))
                            continue;

                        var raw = rl - SlBufferPoints;
                        var src = $"RollingStop RL{lb}@{rl:0.00}(bar={rlBar})";
                        var ev = EvalCandidate(raw, src);

                        if (ev.IsValid) rollingEvals.Add(ev);
                        if (ev.InRange) { rollingSelected = ev; break; }
                    }
                    else
                    {
                        if (!TryGetRollingHigh(bar, lb, out var rh, out var rhBar))
                            continue;

                        var raw = rh + SlBufferPoints;
                        var src = $"RollingStop RH{lb}@{rh:0.00}(bar={rhBar})";
                        var ev = EvalCandidate(raw, src);

                        if (ev.IsValid) rollingEvals.Add(ev);
                        if (ev.InRange) { rollingSelected = ev; break; }
                    }
                }

                if (rollingSelected is not null)
                {
                    rollingBest = rollingSelected;
                }
                else if (rollingEvals.Count > 0)
                {
                    rollingBest = rollingEvals
                        .OrderBy(e => DistToRange(e.RiskTicks, minTicks, maxTicks))
                        .ThenBy(e => e.RiskTicks)
                        .First();
                }
            }

            if (swingEval is not null && swingEval.Value.InRange)
            {
                stop = swingEval.Value.Stop;
                stopSource = swingEval.Value.Source;
                riskTicks = swingEval.Value.RiskTicks;
                riskPoints = swingEval.Value.RiskPoints;
                return true;
            }

            if (rollingSelected is not null && rollingSelected.Value.InRange)
            {
                stop = rollingSelected.Value.Stop;
                stopSource = rollingSelected.Value.Source;
                riskTicks = rollingSelected.Value.RiskTicks;
                riskPoints = rollingSelected.Value.RiskPoints;
                return true;
            }

            if (zoneEval.InRange)
            {
                stop = zoneEval.Stop;
                stopSource = zoneEval.Source;
                riskTicks = zoneEval.RiskTicks;
                riskPoints = zoneEval.RiskPoints;
                return true;
            }

            var evals = new List<StopEval>(capacity: 3);
            if (swingEval is not null) evals.Add(swingEval.Value);
            if (rollingBest is not null) evals.Add(rollingBest.Value);
            if (zoneEval.IsValid) evals.Add(zoneEval);

            if (evals.Count == 0)
            {
                failReason = StopSelectFailReason.Invalid;
                failDetail = $"Stop选择失败 | min={minTicks}t max={maxTicks}t | entry={entry:0.00} | 无有效候选(stop在反面或过近)";
                return false;
            }

            var allTooSmall = evals.All(e => e.RiskTicks > 0 && e.RiskTicks < minTicks);
            var allTooLarge = evals.All(e => e.RiskTicks > 0 && e.RiskTicks > maxTicks);

            if (allTooSmall) failReason = StopSelectFailReason.TooSmall;
            else if (allTooLarge) failReason = StopSelectFailReason.TooLarge;
            else failReason = StopSelectFailReason.MixedOutOfRange;

            string fmt(StopEval e) => $"{e.Source}: stop={e.Stop:0.00} risk={(e.RiskTicks == 0 ? "-" : e.RiskTicks.ToString())}t({e.RiskPoints:0.00}pt)";
            failDetail =
                $"Stop选择失败 | min={minTicks}t max={maxTicks}t | entry={entry:0.00} | rollingTried={rollingTriedText} | {string.Join(" | ", evals.Select(fmt))}";

            return false;
        }

        // =========================
        // TradePlan / Execution
        // =========================
        private void UpdateTradePlanV0(int bar, IndicatorCandle cur, IReadOnlyList<TradingZone> active)
        {
            var halfTick = TickSizeNq / 2m;

            // ===== 退出（优先 SL）=====
            if (_planState == PlanState.InPosition && _activePlan is not null && _activePlan.ExitBar is null)
            {
                var plan = _activePlan;

                if (plan.Side == "LONG")
                {
                    if (cur.Low <= plan.Stop)
                        ExitPlan(bar, "SL Hit", exitPrice: plan.Stop);
                    else if (cur.High >= plan.Target)
                        ExitPlan(bar, "TP Hit", exitPrice: plan.Target);
                    else
                        MaybeMoveBreakEven(bar, cur, plan);
                }
                else
                {
                    if (cur.High >= plan.Stop)
                        ExitPlan(bar, "SL Hit", exitPrice: plan.Stop);
                    else if (cur.Low <= plan.Target)
                        ExitPlan(bar, "TP Hit", exitPrice: plan.Target);
                    else
                        MaybeMoveBreakEven(bar, cur, plan);
                }
            }

            if (_planState != PlanState.Flat)
            {
                SetBlock(bar, TriggerBlockReason.InPosition, "已有持仓/计划单（单仓位）");
                return;
            }

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

            // Q-Freeze：执行层不再动态否决，但保留“锁定时已通过”的硬检查
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

            // qLock：锁定时冻结分数
            var qLock = _lockedZoneQualityScore;

            // ==========================================================
            // Mitigated M1 处理：
            // - pre-armed/pending：仍可硬过滤（按用户参数）
            // - armed/pending：不砍单，只记录一次日志
            // ==========================================================
            if (DisallowMitigatedZones && zone.IsMitigated)
            {
                var armedOrPending = _ofArmed || _pendingEntry is not null;

                if (!armedOrPending)
                {
                    SetBlock(bar, TriggerBlockReason.ZoneQualityInsufficient, $"Mitigated且禁止交易(未Armed) | {zone.ToShortText()}");
                    ResetConfirm(bar, "Mitigated硬过滤(未Armed)");
                    _phase = ConfirmPhase.WaitZoneTouch;
                    return;
                }

                // armed/pending: keep, log once
                if (!_loggedMitigatedKeepOnce)
                {
                    _loggedMitigatedKeepOnce = true;
                    AppendLog($"ZONE_BECAME_MITIGATED bar={bar} phase={_phase} armed={_ofArmed} pending={(_pendingEntry is null ? "-" : _pendingEntry.LimitPrice.ToString("0.00"))} zone={zone.ToShortText()} -> keep setup");
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

                if (_lastOrderFlow.Score < OrderFlowMinScore)
                {
                    SetBlock(
                        bar,
                        TriggerBlockReason.OrderFlowScoreInsufficient,
                        $"订单流不足 score={_lastOrderFlow.Score}/4 < {OrderFlowMinScore} | {_lastOrderFlow.Text}");
                    return;
                }

                _ofArmed = true;
                _ofArmedAtBar = bar;
                _ofArmedScore = _lastOrderFlow.Score;
                _ofArmedText = _lastOrderFlow.Text;

                AppendLog($"OF_ARMED bar={bar} score={_ofArmedScore}/4 text={_ofArmedText} zone={zone.ToShortText()}");
            }

            // ===== Armed -> 执行 =====
            if (EntryMode == EntryExecutionMode.LimitAtZoneAnchor)
            {
                // 挂单创建距离闸门（在创建 pending 前检查）
                if (_pendingEntry is null && EnablePlaceDistanceGate)
                {
                    var th = Math.Max(0m, MaxPlaceDistanceFromZonePoints);
                    if (th > 0m)
                    {
                        var dist = GetDistanceToRangePoints(cur.Close, zone.Low, zone.High);
                        if (dist > th + halfTick)
                        {
                            var detail = $"挂单距离闸门：closeDistToZone={dist:0.00}pt > {th:0.##}pt | close={cur.Close:0.00} zone={zone.Low:0.00}-{zone.High:0.00} | skip+reset";
                            SetBlock(bar, TriggerBlockReason.EntryPlaceTooFarFromZone, detail);

                            AppendLog($"ENTRY_ORDER_SKIP bar={bar} reason=PlaceTooFar closeDist={dist:0.00}pt th={th:0.##}pt close={cur.Close:0.00} zone={zone.ToShortText()} -> reset confirm");

                            ResetConfirm(bar, "挂单距离过远(PlaceGate)");
                            _phase = ConfirmPhase.WaitZoneTouch;
                            return;
                        }
                    }
                }

                if (_pendingEntry is null)
                {
                    var (limit, anchorUsed, anchorText) = GetLimitEntryPriceDynamic(zone, isLong);
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
                        LockedQScore = qLock
                    };

                    AppendLog($"ENTRY_ORDER_CREATE bar={bar} side={side} limit={limit:0.00} anchor={anchorText} effWait={effWait} of={_ofArmedScore}/4 qLock={(qLock < 0 ? "-" : qLock.ToString())}/10 zone={FormatZoneKeyCN(_confirmZoneKey)}");
                }

                var waited = Math.Max(0, bar - _pendingEntry.CreatedBar);

                // pending 超时（先判断 Expired）
                var maxWait = Math.Max(0, _pendingEntry.EffectiveMaxWaitBars);
                if (maxWait > 0 && waited > maxWait)
                {
                    SetBlock(bar, TriggerBlockReason.EntryOrderExpired, $"限价单超时 waited={waited} > {maxWait} | limit={_pendingEntry.LimitPrice:0.00} -> cancel+reset");

                    AppendLog($"ENTRY_ORDER_CANCEL bar={bar} reason=Expired waited={waited} maxWait={maxWait} limit={_pendingEntry.LimitPrice:0.00} side={_pendingEntry.Side} zone={FormatZoneKeyCN(_pendingEntry.Zone)}");

                    ResetConfirm(bar, "限价入场超时");
                    _phase = ConfirmPhase.WaitZoneTouch;
                    return;
                }

                // 提前取消：runaway
                if (EnableEarlyCancelRunaway)
                {
                    var minWaitBeforeCancel = Math.Max(0, RunawayCancelMinWaitBars);
                    if (waited >= minWaitBeforeCancel)
                    {
                        var runawayDist = GetRunawayDistanceInBiasDirectionPoints(cur.Close, zone, isLong);
                        var runawayTh = Math.Max(0m, RunawayCancelDistancePoints);
                        var needBars = Math.Max(1, RunawayCancelConsecutiveBars);

                        if (runawayDist > runawayTh + halfTick)
                            _pendingEntry.RunawayBars++;
                        else
                            _pendingEntry.RunawayBars = 0;

                        if (_pendingEntry.RunawayBars >= needBars)
                        {
                            var detail = $"提前取消(runaway): dist={runawayDist:0.00}pt > {runawayTh:0.##}pt 连续{_pendingEntry.RunawayBars}bars | waited={waited}/{_pendingEntry.EffectiveMaxWaitBars} | limit={_pendingEntry.LimitPrice:0.00} anchor={_pendingEntry.AnchorText}";
                            SetBlock(bar, TriggerBlockReason.EntryOrderEarlyCanceled, detail);

                            AppendLog($"ENTRY_ORDER_CANCEL bar={bar} reason=Runaway runawayDist={runawayDist:0.00}pt th={runawayTh:0.##}pt nBars={_pendingEntry.RunawayBars} waited={waited}/{_pendingEntry.EffectiveMaxWaitBars} limit={_pendingEntry.LimitPrice:0.00} side={_pendingEntry.Side} zone={FormatZoneKeyCN(_pendingEntry.Zone)} -> reset confirm");

                            ResetConfirm(bar, "挂单提前取消(runaway)");
                            _phase = ConfirmPhase.WaitZoneTouch;
                            return;
                        }
                    }
                }

                // 触达成交（candle 高低模拟）
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
                        $"等待回撤成交 limit={limitPrice:0.00}({_pendingEntry.AnchorText}) waited={waited}/{maxWait} | closeDistToZone≈{dist:0.00}pt | OF(armed)={_pendingEntry.OfScore}/4 | QLock={(qLock < 0 ? "-" : qLock.ToString())}/10");

                    return;
                }

                var entry = limitPrice;

                // 安全阀：entry 意外离 zone 太远（anchor 设置错误）
                if (EnableAntiChaseFilter)
                {
                    var maxDist = Math.Max(0m, MaxEntryDistanceFromZonePoints);
                    if (maxDist > 0m)
                    {
                        var dist = GetDistanceToRangePoints(entry, zone.Low, zone.High);
                        if (dist > maxDist + halfTick)
                        {
                            SetBlock(bar, TriggerBlockReason.EntryTooFarFromZone, $"防追价(limit)：entry dist={dist:0.00}pt > {maxDist:0.##}pt | entry={entry:0.00} zone={zone.Low:0.00}-{zone.High:0.00}");
                            return;
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

                var target = isLong
                    ? entry + riskPoints * RiskRewardR
                    : entry - riskPoints * RiskRewardR;

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
                    OfText = _pendingEntry.OfText
                };

                _planState = PlanState.InPosition;

                AppendLog($"ENTRY_ORDER_FILLED bar={bar} side={side} entry={entry:0.00} limit={limitPrice:0.00} anchor={_pendingEntry.AnchorText} -> PLAN_CREATE");
                AppendLog($"PLAN_CREATE bar={bar} side={side} entry={entry:0.00} stop={stop:0.00} tp={target:0.00} riskTicks={riskTicks} of={_activePlan.OfScore}/4 zone={FormatZoneKeyCN(_activePlan.Zone)} src={stopSource} qLock={(qLock < 0 ? "-" : qLock.ToString())}/10");

                _lastTriggeredConfirmStartBar = _confirmStartBar;
                _lastTriggeredZoneKey = _confirmZoneKey;

                if (EnableCooldown)
                    _cooldownUntilBar = Math.Max(_cooldownUntilBar, bar + Math.Max(0, CooldownBars));

                SetBlock(
                    bar,
                    TriggerBlockReason.None,
                    $"OK: LIMIT成交 side={side} entry={entry:0.00} stop={stop:0.00} risk={riskTicks}t tp={target:0.00} | OF(armed)={_activePlan.OfScore}/4 | QLock={(qLock < 0 ? "-" : qLock.ToString())}/10 | anchor={_pendingEntry.AnchorText} | src={stopSource}");

                ResetConfirm(bar, "已成交入场(Limit)");
                _phase = ConfirmPhase.WaitZoneTouch;

                return;
            }

            // MarketClose（保留旧模式；当前主测 Limit）
            if (EntryMode == EntryExecutionMode.MarketClose)
            {
                var entry = cur.Close;

                if (EnableAntiChaseFilter)
                {
                    var maxDist = Math.Max(0m, MaxEntryDistanceFromZonePoints);
                    if (maxDist > 0m)
                    {
                        var dist = GetDistanceToRangePoints(entry, zone.Low, zone.High);

                        if (dist > maxDist + halfTick)
                        {
                            SetBlock(bar, TriggerBlockReason.EntryTooFarFromZone,
                                $"防追价：entry离区块过远 dist={dist:0.00}pt > max={maxDist:0.##}pt | entry={entry:0.00} zone={zone.Low:0.00}-{zone.High:0.00} | 等待回撤（不reset）");
                            AppendLog($"ANTI_CHASE_BLOCK bar={bar} entry={entry:0.00} dist={dist:0.00}pt max={maxDist:0.##}pt zone={zone.ToShortText()} -> skip (no reset)");
                            return;
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

                var target = isLong
                    ? entry + riskPoints * RiskRewardR
                    : entry - riskPoints * RiskRewardR;

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
                    OfText = _ofArmedText
                };

                _planState = PlanState.InPosition;

                AppendLog($"PLAN_CREATE bar={bar} side={side} entry={entry:0.00} stop={stop:0.00} tp={target:0.00} riskTicks={riskTicks} of={_activePlan.OfScore}/4 zone={FormatZoneKeyCN(_activePlan.Zone)} src={stopSource} qLock={(qLock < 0 ? "-" : qLock.ToString())}/10");

                _lastTriggeredConfirmStartBar = _confirmStartBar;
                _lastTriggeredZoneKey = _confirmZoneKey;

                if (EnableCooldown)
                    _cooldownUntilBar = Math.Max(_cooldownUntilBar, bar + Math.Max(0, CooldownBars));

                SetBlock(
                    bar,
                    TriggerBlockReason.None,
                    $"OK: 市价触发 side={side} entry={entry:0.00} stop={stop:0.00} risk={riskTicks}t tp={target:0.00} | OF(armed)={_activePlan.OfScore}/4 | QLock={(qLock < 0 ? "-" : qLock.ToString())}/10 | src={stopSource}");

                ResetConfirm(bar, "已成交入场(MarketClose)");
                _phase = ConfirmPhase.WaitZoneTouch;
            }
        }

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

            var atR = BreakEvenAtR <= 0m ? 1m : BreakEvenAtR;
            var trig = plan.InitialRiskPoints * atR;

            var plusTicks = Math.Max(0, BreakEvenPlusTicks);
            var plus = plusTicks * TickSizeNq;

            if (plan.Side == "LONG")
            {
                var beTrigger = plan.Entry + trig;
                if (cur.High >= beTrigger)
                {
                    var newStop = plan.Entry + plus;
                    if (newStop > plan.Stop)
                    {
                        plan.Stop = newStop;
                        plan.BreakEvenMoved = true;
                        AppendLog($"PLAN_BE_MOVE bar={bar} side=LONG trigger@{beTrigger:0.00} newSL={newStop:0.00}");
                    }
                }
            }
            else
            {
                var beTrigger = plan.Entry - trig;
                if (cur.Low <= beTrigger)
                {
                    var newStop = plan.Entry - plus;
                    if (newStop < plan.Stop)
                    {
                        plan.Stop = newStop;
                        plan.BreakEvenMoved = true;
                        AppendLog($"PLAN_BE_MOVE bar={bar} side=SHORT trigger@{beTrigger:0.00} newSL={newStop:0.00}");
                    }
                }
            }
        }

        private void ExitPlan(int bar, string reason, decimal exitPrice)
        {
            if (_activePlan is null)
                return;

            _activePlan.ExitBar = bar;
            _activePlan.ExitReason = reason;
            _activePlan.ExitPrice = exitPrice;

            AppendLog($"PLAN_EXIT bar={bar} reason={reason} exit={exitPrice:0.00} side={_activePlan.Side} entry={_activePlan.Entry:0.00} stop={_activePlan.Stop:0.00} tp={_activePlan.Target:0.00}");

            AddTradeRecord(_activePlan, bar, exitPrice, reason);

            _planState = PlanState.Flat;

            if (EnableCooldown)
                _cooldownUntilBar = Math.Max(_cooldownUntilBar, bar + Math.Max(0, CooldownBars));
        }

        private void AddTradeRecord(TradePlan plan, int exitBar, decimal exitPrice, string exitReason)
        {
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

            var riskTicks = risk / TickSizeNq;
            var riskDollar = riskTicks * tickValue * contracts;
            var pnlDollar = r * riskDollar;

            _netPnLDollar += pnlDollar;

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
                ExitReason = exitReason,
                OfScore = plan.OfScore,
                OfText = plan.OfText,
                Zone = plan.Zone
            });

            if (_tradeHistory.Count > 500)
                _tradeHistory.RemoveRange(0, _tradeHistory.Count - 500);
        }

        private static decimal GetFallbackStopFromZone(bool isLong, TradingZone zone)
            => isLong ? zone.Low - SlBufferPoints : zone.High + SlBufferPoints;

        private static decimal RoundToTick(decimal price)
        {
            var ticks = Math.Round(price / TickSizeNq, 0, MidpointRounding.AwayFromZero);
            return ticks * TickSizeNq;
        }

        private (decimal Price, ZoneEntryAnchor AnchorUsed, string AnchorText) GetLimitEntryPriceDynamic(TradingZone zone, bool isLong)
        {
            var width = Math.Abs(zone.High - zone.Low);
            var th = Math.Max(0m, DynamicAnchorWidthThresholdPoints);

            ZoneEntryAnchor anchor;

            if (EnableDynamicEntryAnchor && width > th)
            {
                anchor = ZoneEntryAnchor.Edge;
            }
            else
            {
                anchor = LimitEntryAnchor;

                if (EnableAutoInnerAnchorWhenMid && anchor == ZoneEntryAnchor.Mid)
                {
                    anchor = AutoInnerAnchor == InnerAnchorType.Third
                        ? ZoneEntryAnchor.Third
                        : ZoneEntryAnchor.Quarter;
                }
            }

            if (anchor == ZoneEntryAnchor.Mid)
                return (zone.Mid, ZoneEntryAnchor.Mid, "ZoneMid");

            if (anchor == ZoneEntryAnchor.Edge)
            {
                if (EnableDynamicEntryAnchor && width > th)
                {
                    return isLong
                        ? (zone.High, ZoneEntryAnchor.Edge, $"ZoneHigh(AutoEdge width>{th:0.##})")
                        : (zone.Low, ZoneEntryAnchor.Edge, $"ZoneLow(AutoEdge width>{th:0.##})");
                }

                return isLong
                    ? (zone.High, ZoneEntryAnchor.Edge, "ZoneHigh")
                    : (zone.Low, ZoneEntryAnchor.Edge, "ZoneLow");
            }

            decimal ratio = anchor == ZoneEntryAnchor.Third ? 0.3333333333m : 0.25m;

            var raw = isLong
                ? (zone.High - width * ratio)
                : (zone.Low + width * ratio);

            var p = RoundToTick(raw);

            var tag = anchor == ZoneEntryAnchor.Third ? "ZoneT33" : "ZoneQ25";

            if (EnableAutoInnerAnchorWhenMid && LimitEntryAnchor == ZoneEntryAnchor.Mid)
                tag += "(AutoFromMid)";

            return (p, anchor, tag);
        }

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

        private bool TryGetZoneByKeyWithShadow(IReadOnlyList<TradingZone> activeZones, ZoneKey key, out TradingZone zone, out bool usedShadow)
        {
            usedShadow = false;

            if (TryFindZone(activeZones, key, out zone))
            {
                _confirmZoneShadow = CloneZone(zone);
                _confirmZoneUsingShadow = false;
                _loggedShadowUseOnce = false;
                return true;
            }

            if (_confirmZoneShadow is not null && ZoneMatchesKey(_confirmZoneShadow, key))
            {
                zone = _confirmZoneShadow;
                usedShadow = true;

                _confirmZoneUsingShadow = true;

                if (!_loggedShadowUseOnce)
                {
                    _loggedShadowUseOnce = true;
                    AppendLog($"LOCKED_ZONE_NOT_IN_ACTIVE_VIEW -> using shadow: {zone.ToShortText()}");
                }

                return true;
            }

            zone = null!;
            return false;
        }

        private static TradingZone CloneZone(TradingZone z)
        {
            return new TradingZone
            {
                Type = z.Type,
                StartBar = z.StartBar,
                CreatedBar = z.CreatedBar,
                Low = z.Low,
                High = z.High,

                IsTouched = z.IsTouched,
                IsMitigated = z.IsMitigated,
                IsVpRejected = z.IsVpRejected,
                IsInvalidated = z.IsInvalidated,

                Text = z.Text
            };
        }

        private static bool ZoneMatchesKey(TradingZone z, ZoneKey k)
        {
            return z.Type == k.Type &&
                   z.StartBar == k.StartBar &&
                   z.CreatedBar == k.CreatedBar &&
                   z.Low == k.Low &&
                   z.High == k.High;
        }

        private static decimal GetDistanceToRangePoints(decimal price, decimal low, decimal high)
        {
            if (low > high) (low, high) = (high, low);
            if (price < low) return low - price;
            if (price > high) return price - high;
            return 0m;
        }

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

            // OF/Armed
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

            var entryLine = _pendingEntry is null
                ? "入场：-"
                : $"入场：Limit {_pendingEntry.Side} @{_pendingEntry.LimitPrice:0.00}({_pendingEntry.AnchorText}) waited={Math.Max(0, bar - _pendingEntry.CreatedBar)}/{_pendingEntry.EffectiveMaxWaitBars}";

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
                $"图例: T触碰 M回补50% RVP拒绝(灰色)"
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

            return $"记录: {total}笔 W{_tradeWins} L{_tradeLosses} NetR={_netR:0.00} Net$={_netPnLDollar:0.##} | 上一笔: {lastText} | {contracts}x @${tickValue:0.##}/tick";
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
                    return $"{dir}{reason} {t.R:0.00}R ${t.PnLDollar:0.##}";
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

                _ => "-"
            };
        }

        private void ResetBarBlock()
        {
            _barBlockReason = TriggerBlockReason.None;
            _barBlockDetail = "-";
        }

        private void SetBlock(int bar, TriggerBlockReason reason, string detail)
        {
            _barBlockReason = reason;
            _barBlockDetail = detail;

            if (reason != TriggerBlockReason.None)
            {
                _lastBlockReason = reason;
                _lastBlockDetail = detail;
                _lastBlockBar = bar;
            }
        }

        private string BuildVpRejectedDetail(IndicatorCandle cur, TradingZone zone)
        {
            if (_htfZones is not null && _htfZones.TryGetVpRejectInfo(zone, out var info))
            {
                var time = info.VpBucketTime.HasValue ? info.VpBucketTime.Value.ToString("yyyy-MM-dd HH:mm") : "-";
                return $"VP拒绝 | touch={info.TouchPrice:0.00} nearest={info.NearestRefName}@{info.NearestRefPrice:0.00} effDist={info.DistancePoints:0.00}pt > {info.MaxAllowedDistancePoints:0.##}pt | vpTime={time} | zone={zone.ToShortText()}";
            }

            var vp = _lastClosedVpCandle;
            var touchPrice = GetTouchPriceLikeZoneEngine(zone, cur);

            if (vp is null)
                return $"VP拒绝(无VP参考) | zone={zone.ToShortText()} | touch≈{touchPrice:0.00}";

            var (refName, refPrice, dist) = GetNearestVpRef(vp, touchPrice);
            var ok = dist <= VpMaxDistancePoints;

            return ok
                ? $"VP拒绝(flag已被标记但当前计算显示通过?) | touch≈{touchPrice:0.00} nearest={refName}@{refPrice:0.00} dist={dist:0.00}pt <= {VpMaxDistancePoints:0.##}pt | zone={zone.ToShortText()}"
                : $"VP拒绝 | touch≈{touchPrice:0.00} nearest={refName}@{refPrice:0.00} dist={dist:0.00}pt > {VpMaxDistancePoints:0.##}pt | zone={zone.ToShortText()}";
        }

        private static decimal GetTouchPriceLikeZoneEngine(TradingZone zone, IndicatorCandle candle)
        {
            return zone.Type switch
            {
                ZoneType.BullishOB or ZoneType.BullishFVG => Clamp(candle.Low, zone.Low, zone.High),
                ZoneType.BearishOB or ZoneType.BearishFVG => Clamp(candle.High, zone.Low, zone.High),
                _ => candle.Close
            };
        }

        private static (string RefName, decimal RefPrice, decimal Dist) GetNearestVpRef(HigherTimeframeAggregator.HtfCandle vp, decimal price)
        {
            var refs = new List<(string Name, decimal Price)>(4);

            if (vp.POC != 0m) refs.Add(("POC", vp.POC));
            if (vp.VAH != 0m) refs.Add(("VAH", vp.VAH));
            if (vp.VAL != 0m) refs.Add(("VAL", vp.VAL));
            if (vp.VWAP != 0m) refs.Add(("VWAP", vp.VWAP));

            if (refs.Count == 0)
                return ("-", 0m, 0m);

            var best = refs[0];
            var bestDist = Math.Abs(price - best.Price);

            for (var i = 1; i < refs.Count; i++)
            {
                var d = Math.Abs(price - refs[i].Price);
                if (d < bestDist)
                {
                    best = refs[i];
                    bestDist = d;
                }
            }

            return (best.Name, best.Price, bestDist);
        }

        private static decimal Clamp(decimal x, decimal min, decimal max)
        {
            if (min > max) (min, max) = (max, min);
            if (x < min) return min;
            if (x > max) return max;
            return x;
        }

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
            var halfTick = TickSizeNq / 2m;

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

            // candidates 用于“选新区块”阶段；锁定阶段不要用 candidates==0 去误杀会话
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

                // Mitigated M1：锁定后如果变 mitigated
                if (DisallowMitigatedZones && lockedZone.IsMitigated)
                {
                    var armedOrPending = _ofArmed || _pendingEntry is not null;

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
                    // 仅“选新区块阶段”才受 candidates 为空影响：不锁定、不进入确认
                    ResetConfirm(bar, "无可用区块(质量过滤后为空)");
                    _phase = ConfirmPhase.WaitZoneTouch;
                    return;
                }

                focusZone = candidates.OrderBy(z => Math.Abs(curBase.Close - z.Mid)).First();
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

                    // Q-Freeze：锁定时记录质量分（HUD/日志/执行均用锁定分）
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
                            _confirmRefText = $"Disp>{Math.Max(0m, ConfirmDisplacementPoints):0.##}pt";
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
                        var disp = Math.Max(0m, ConfirmDisplacementPoints);

                        var confirmed =
                            htfBias == TrendDirection.Bullish
                                ? (curBase.Close >= focusZone.High + disp)
                                : (curBase.Close <= focusZone.Low - disp);

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

                    // SwingBreak
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
            var maxDist = Math.Max(0m, RefMaxDistancePoints);

            if (refBar >= 0 && maxAge > 0 && (curBar - refBar) > maxAge)
                return true;

            if (maxDist <= 0m)
                return false;

            if (bias == TrendDirection.Bullish)
            {
                var dist = refPrice - curClose;
                if (dist > maxDist)
                    return true;
            }
            else if (bias == TrendDirection.Bearish)
            {
                var dist = curClose - refPrice;
                if (dist > maxDist)
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
            if (_pendingEntry is not null)
            {
                var waited = bar >= 0 ? Math.Max(0, bar - _pendingEntry.CreatedBar) : -1;
                var waitedText = waited >= 0 ? waited.ToString() : "?";

                AppendLog(
                    $"ENTRY_ORDER_CANCEL bar={bar} reason=ResetConfirm({reason}) waited={waitedText}/{_pendingEntry.EffectiveMaxWaitBars} " +
                    $"limit={_pendingEntry.LimitPrice:0.00} anchor={_pendingEntry.AnchorText} side={_pendingEntry.Side} qLock={(_pendingEntry.LockedQScore < 0 ? "-" : _pendingEntry.LockedQScore.ToString())}/10 " +
                    $"zone={FormatZoneKeyCN(_pendingEntry.Zone)}");
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
            _ofArmedText = "-";
            _pendingEntry = null;

            _lockedZoneQualityScore = -1;
            _lockedZoneQualityDetail = "-";

            _loggedMitigatedKeepOnce = false;
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
        // Replay reset helper
        // =========================
        private static DateTime? TryGetBaseCandleTime(IndicatorCandle candle)
        {
            _baseTimeProp ??= candle.GetType().GetProperty("Time")
                          ?? candle.GetType().GetProperty("OpenTime");

            if (_baseTimeProp is null)
                return null;

            var value = _baseTimeProp.GetValue(candle);

            return value switch
            {
                DateTime dt => dt,
                _ => null
            };
        }

        // =========================
        // File Logging
        // =========================
        private void EnsureLogInitialized()
        {
            if (!EnableFileLog)
                return;

            if (!string.IsNullOrWhiteSpace(_logPath))
                return;

            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var dir = Path.Combine(appData, "ATAS", string.IsNullOrWhiteSpace(LogFolderName) ? "StrategyLogs" : LogFolderName);

                Directory.CreateDirectory(dir);

                var file = string.IsNullOrWhiteSpace(LogFileName) ? "NQOrderFlowV1.log" : LogFileName;
                _logPath = Path.Combine(dir, file);

                AppendLog($"=== Strategy Start ===");
                AppendLog($"LogPath={_logPath}");
            }
            catch
            {
                _logPath = null;
            }
        }

        private void AppendLog(string message)
        {
            if (!EnableFileLog)
                return;

            if (string.IsNullOrWhiteSpace(_logPath))
                return;

            try
            {
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} | {message}{Environment.NewLine}";
                File.AppendAllText(_logPath!, line);
            }
            catch
            {
                // ignore
            }
        }

        private void MaybeLogPhaseChange(int bar, IndicatorCandle cur)
        {
            if (!EnableFileLog)
                return;

            if (_phase == _lastLoggedPhase)
                return;

            var t = TryGetBaseCandleTime(cur);
            var time = t.HasValue ? t.Value.ToString("yyyy-MM-dd HH:mm") : "-";

            var q = _lockedZoneQualityScore >= 0 ? $"{_lockedZoneQualityScore}/10" : "-";

            AppendLog($"PHASE bar={bar} time={time} phase={_phase} bias={_htfBias}({_htfBiasReason}) zone={(_confirmZoneKey is null ? "-" : FormatZoneKeyCN(_confirmZoneKey))}{(_confirmZoneUsingShadow ? "(shadow)" : "")} q={q} ref={_confirmRefText}{(_confirmRefIsFallback ? "(FB)" : "")} armed={_ofArmed} pending={(_pendingEntry is null ? "-" : _pendingEntry.LimitPrice.ToString("0.00"))} text={_confirmText}");

            _lastLoggedPhase = _phase;
        }

        private void MaybeLogTriggerBlock(int bar, IndicatorCandle cur)
        {
            if (!EnableFileLog)
                return;

            var sameReasonN = Math.Max(1, LogSameReasonEveryNBars);

            if (_barBlockReason == _lastLoggedBlockReason)
            {
                _sameReasonRun++;
                if (_sameReasonRun < sameReasonN)
                    return;

                _sameReasonRun = 0;
            }
            else
            {
                _sameReasonRun = 0;
            }

            if (_planState != _lastLoggedPlanState)
            {
                AppendLog($"PLAN_STATE bar={bar} state={_planState}");
                _lastLoggedPlanState = _planState;
            }

            if (_lastLoggedBar == bar && _barBlockReason == _lastLoggedBlockReason)
                return;

            var t = TryGetBaseCandleTime(cur);
            var time = t.HasValue ? t.Value.ToString("yyyy-MM-dd HH:mm") : "-";

            AppendLog($"BLOCK bar={bar} time={time} reason={_barBlockReason} detail={_barBlockDetail}");

            _lastLoggedBlockReason = _barBlockReason;
            _lastLoggedBar = bar;
        }
    }
}