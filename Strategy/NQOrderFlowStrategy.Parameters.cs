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
using System.ComponentModel.DataAnnotations;
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
        // ===== 核心参数 =====
        // Legacy default tick size (保持兼容：历史代码/其它文件可能仍引用 TickSizeNq)
        private const decimal TickSizeNq = 0.01m;

        private const int HtfMinutes = 15;
        private const int PivotLength = 2;

        public int ObLookback { get; set; } = 10;
        public int MinFvgTicks { get; set; } = 4;

        // M5 确认等待的最长 bar 数（进入区块开始计时）
        public int MaxConfirmBars { get; set; } = 12;

        // 离开区块太久重置（M5 bar 数）——仅用于"确认前"的等待阶段
        public int OutOfZoneResetBars { get; set; } = 4;

        // ===== 虚拟执行层（TradePlan V0）=====
        [Category("Plan")]
        [DisplayName("Risk Reward R (初始止盈倍数)")]
        public decimal RiskRewardR { get; set; } = 3m;
        [Category("Plan")]
        [DisplayName("SL Buffer Ticks (止损到摆动点缓冲ticks)")]
        public int SlBufferTicks { get; set; } = 32;

        [Category("Plan")]
        [DisplayName("Config Version (当前配置方案版本号)")]
        public string ConfigVersion { get; set; } = "Default";
        // ===== OrderFlow 评分阈值（P2：参数化）=====
        [Category("Plan")]
        [DisplayName("OrderFlow Min Score Threshold (OF最低评分，0-10)")]
        public int OrderFlowMinScoreThreshold { get; set; } = 4;
        // =====================================================================
        // Instrument Rules (P0)
        // =====================================================================

        [Category("Instrument")]
        [DisplayName("Instrument Tick Size (points, e.g. MNQ/NQ=0.25)")]
        public decimal InstrumentTickSize { get; set; } = TickSizeNq;

        [Category("Instrument")]
        [DisplayName("Instrument Qty Step (e.g. MNQ/NQ=1)")]
        public decimal InstrumentQtyStep { get; set; } = 1m;

        [Category("Instrument")]
        [DisplayName("Instrument Min Qty (e.g. MNQ/NQ=1)")]
        public decimal InstrumentMinQty { get; set; } = 1m;

        [Category("Instrument")]
        [DisplayName("Use Decimal Entry Quantity (use EntryQuantity instead of Contracts)")]
        public bool UseDecimalEntryQuantity { get; set; } = false;

        [Category("Instrument")]
        [DisplayName("Entry Quantity (decimal, used when UseDecimalEntryQuantity=true)")]
        public decimal EntryQuantity { get; set; } = 1m;

        // =====================================================================
        // VP Filter (P0.5)
        // =====================================================================

        [Category("VP Filter")]
        [DisplayName("VP Max Distance (ticks, 0=use legacy points)")]
        public int VpMaxDistanceTicks { get; set; } = 0;

        [Category("VP Filter")]
        [DisplayName("LEGACY: VP Max Distance (points)")]
        public decimal VpMaxDistancePoints { get; set; } = 8m;

        // =====================================================================
        // Stage B Live Orders
        // =====================================================================

        [Category("Stage B Live")]
        [DisplayName("SelfTest Quantity (decimal, e.g. 0.01)")]
        public decimal SelfTestQuantity { get; set; } = 0.01m;

        [Category("Stage B Live")]
        [DisplayName("Enable Live SelfTest (启动后发一张测试单并撤单)")]
        public bool EnableLiveSelfTest { get; set; } = false;

        [Category("Stage B Live")]
        [DisplayName("SelfTest Side (Buy=true / Sell=false)")]
        public bool SelfTestBuy { get; set; } = true;

        [Category("Stage B Live")]
        [DisplayName("SelfTest Price Offset (points, 远离当前价，避免成交)")]
        public decimal SelfTestOffsetPoints { get; set; } = 200m;

        [Category("Stage B Live")]
        [DisplayName("SelfTest Cancel After Ms")]
        public int SelfTestCancelAfterMs { get; set; } = 1500;

        [Category("Stage B Live")]
        [DisplayName("Enable Live Orders (Stage B 实盘下单)")]
        public bool EnableLiveOrders { get; set; } = false;

        [Category("Stage B Live")]
        [DisplayName("Live TimeInForce")]
        public TimeInForce LiveTimeInForce { get; set; } = TimeInForce.Day;

        [Category("Stage B Live")]
        [DisplayName("Live TriggerPriceType (for Stop)")]
        public TriggerPriceType LiveTriggerPriceType { get; set; } = TriggerPriceType.Last;

        [Category("Stage B Live")]
        [DisplayName("Live Comment Prefix")]
        public string LiveCommentPrefix { get; set; } = "NQOF";

        [Category("Stage B Live")]
        [DisplayName("Live: Flatten on Critical Bracket Failure (SL/TP下单失败则市价平仓)")]
        public bool LiveFlattenOnCriticalBracketFailure { get; set; } = true;

        // ===== HTF Bias =====
        [Category("HTF Bias")]
        [DisplayName("Enable CHOCH Bias Hold (CHOCH后短时间延续上一Bias)")]
        public bool EnableChochBiasHold { get; set; } = false;  // CHOCH后立即清除旧偏差，防止趋势反转后下逆向单

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

        // ===== Confirm 优化：确认模式 =====
        public enum ConfirmMode
        {
            SwingBreak = 0,
            ZoneDisplacement = 1
        }

        [Category("Confirm")]
        [DisplayName("Confirm Mode (确认模式)")]
        public ConfirmMode ConfirmationMode { get; set; } = ConfirmMode.ZoneDisplacement;

        [Category("Confirm")]
        [DisplayName("Zone Displacement Confirm (ticks, 0=use legacy points)")]
        public int ConfirmDisplacementTicks { get; set; } = 0;

        [Category("Confirm")]
        [DisplayName("LEGACY: Zone Displacement Confirm (points) - 触碰后离开区块的最小位移")]
        public decimal ConfirmDisplacementPoints { get; set; } = 1m;

        // ===== Plan：执行模式 =====
        public enum EntryExecutionMode
        {
            MarketClose = 0,
            LimitAtZoneAnchor = 1
        }

        public enum ZoneEntryAnchor
        {
            Edge = 0,
            Mid = 1,
            Quarter = 2,
            Third = 3
        }

        public enum InnerAnchorType
        {
            Quarter = 0,
            Third = 1
        }

        [Display(Name = "CancelLiveOnM5CloseZoneInvalidation", GroupName = "P2.5 Reset", Order = 10)]
        public bool CancelLiveOnM5CloseZoneInvalidation { get; set; } = true;

        [Category("Plan")]
        [DisplayName("Entry Execution Mode (入场执行模式)")]
        public EntryExecutionMode EntryMode { get; set; } = EntryExecutionMode.LimitAtZoneAnchor;

        // ===== RetraceThenMarket 混合入场 =====
        [Category("Plan")]
        [DisplayName("Enable Retrace Entry (OF武装后先等回撤再入场)")]
        public bool EnableRetraceEntry { get; set; } = false;  // Limit模式禁用（改用挂单入场）

        [Category("Plan")]
        [DisplayName("Max Retrace Wait Bars (最多等几根K线回撤)")]
        public int MaxRetraceWaitBars { get; set; } = 2;

        [Category("Plan")]
        [DisplayName("Retrace Entry Max Distance Ticks (距区块多远以内才入场)")]
        public int RetraceEntryMaxDistanceTicks { get; set; } = 6;

        [Category("Plan")]
        [DisplayName("Limit Entry Anchor (区块限价锚点)")]
        public ZoneEntryAnchor LimitEntryAnchor { get; set; } = ZoneEntryAnchor.Edge;

        [Category("Plan")]
        [DisplayName("Enable Auto Inner Anchor When Mid (当选择Mid时，自动改为内侧锚点)")]
        public bool EnableAutoInnerAnchorWhenMid { get; set; } = true;

        [Category("Plan")]
        [DisplayName("Auto Inner Anchor Type (Quarter/Third) - Mid替换用哪个")]
        public InnerAnchorType AutoInnerAnchor { get; set; } = InnerAnchorType.Quarter;

        [Category("Plan")]
        [DisplayName("Entry Max Wait Bars After Armed (M5) - Armed后等待回撤成交最大bars(基础值)")]
        public int EntryMaxWaitBarsAfterArmed { get; set; } = 6;

        // =====================================================================
        // 动态锚点 / 自适应等待 / 提前取消
        // =====================================================================
        [Category("Plan")]
        [DisplayName("Enable Dynamic Entry Anchor (宽区块自动用Edge)")]
        public bool EnableDynamicEntryAnchor { get; set; } = true;

        [Category("Plan")]
        [DisplayName("Dynamic Anchor Width Threshold (ticks, 0=use legacy points)")]
        public int DynamicAnchorWidthThresholdTicks { get; set; } = 0;

        [Category("Plan")]
        [DisplayName("LEGACY: Dynamic Anchor Width Threshold (points) - zoneWidth > 该值用Edge")]
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
        [DisplayName("Early Cancel: Runaway Distance (ticks, 0=use legacy points)")]
        public int RunawayCancelDistanceTicks { get; set; } = 0;

        [Category("Plan")]
        [DisplayName("LEGACY: Early Cancel: Runaway Distance (points)")]
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
        [DisplayName("Max Place Distance From Zone (ticks, 0=use legacy points)")]
        public int MaxPlaceDistanceFromZoneTicks { get; set; } = 0;

        [Category("Plan")]
        [DisplayName("LEGACY: Max Place Distance From Zone (points) - 创建挂单时Close离区块最大距离")]
        public decimal MaxPlaceDistanceFromZonePoints { get; set; } = 60m;

        // ===== 防追价（Anti-Chase）=====
        [Category("Plan")]
        [DisplayName("Enable Anti-Chase Filter (防追价：入场离区块过远则不进)")]
        public bool EnableAntiChaseFilter { get; set; } = true;

        [Category("Plan")]
        [DisplayName("Max Entry Distance From Zone (ticks, 0=use legacy points)")]
        public int MaxEntryDistanceFromZoneTicks { get; set; } = 0;

        [Category("Plan")]
        [DisplayName("LEGACY: Max Entry Distance From Zone (points) - 入场离区块最大距离")]
        public decimal MaxEntryDistanceFromZonePoints { get; set; } = 14m;

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
        public int RollingStopMinLookbackBars { get; set; } = 4;

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
        public bool EnableBreakEvenMove { get; set; } = true;

        [Category("Plan")]
        [DisplayName("BreakEven At R (e.g. 1.0 = 到达1R后保本)")]
        public decimal BreakEvenAtR { get; set; } = 1.0m;  // 从0.8→1.0，让利润多跑一会再触发保本

        [Category("Plan")]
        [DisplayName("BreakEven Plus Ticks (保本上移/下移ticks，默认0)")]
        public int BreakEvenPlusTicks { get; set; } = 12;  // 从2→12，保本后给6$噪音缓冲
        [Category("Plan")]
        [DisplayName("BreakEven Plus R Ratio (0-1, R比例缓冲)")]
        [Description("R比例保本缓冲: BE后止损 = entry ± max(BreakEvenPlusTicks, riskTicks × this ratio)")]
        public int Contracts { get; set; } = 1;

        [Category("PnL ($)")]
        [DisplayName("Tick Value ($/tick/contract)")]
        public decimal TickValuePerContract { get; set; } = 0.5m;

        // ===== Trailing Stop（P3：跟踪移动止损）=====
        [Category("Plan")]
        [DisplayName("Enable Trailing Stop (突破保本后跟踪移动止损)")]
        public bool EnableTrailingStop { get; set; } = true;

        [Category("Plan")]
        [DisplayName("Trailing Stop Activation R (到达几R后开始跟踪)")]
        public decimal TrailingStopActivationR { get; set; } = 1.2m;

        [Category("Plan")]
        [DisplayName("Trailing Stop Buffer Ticks (止损到swing点外的缓冲ticks)")]
        public int TrailingStopBufferTicks { get; set; } = 4;

        [Category("Plan")]
        [DisplayName("Trail Stop Min Distance From Current (ticks, 0=不限制) - 防止追踪止损离当前价太近")]
        public int TrailStopMinDistFromCurrentTicks { get; set; } = 8;

        // ===== M5收盘模糊缓存 =====
        [Category("Plan")]
        [DisplayName("M5 Close Fuzzy Ticks (M5收盘模糊缓存ticks，边界附近不计入失效)")]
        public int M5CloseFuzzyTicks { get; set; } = 3;

        // ===== 动态止盈 (Dynamic TP Tightening) =====
        [Category("Plan")]
        [DisplayName("Enable Dynamic TP Tightening (BE触发后收紧TP)")]
        public bool EnableDynamicTPTightening { get; set; } = true;

        [Category("Plan")]
        [DisplayName("Dynamic TP Target R (BE触发后TP收紧到此R值)")]
        public decimal DynamicTPTargetR { get; set; } = 3m;

        [Category("Plan")]
        [DisplayName("Dynamic TP Trail Buffer R (Trailing激活后TP与止损之间保持的R倍数)")]
        public decimal DynamicTPTrailBufferR { get; set; } = 1m;

        // ===== 冷却设置 ====="
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
        public int RefFallbackLookbackBars { get; set; } = 8;

        [Category("Confirm")]
        [DisplayName("Ref Max Distance (ticks, 0=use legacy points)")]
        public int RefMaxDistanceTicks { get; set; } = 0;

        [Category("Confirm")]
        [DisplayName("LEGACY: Ref Max Distance (points) - 参考Swing过远阈值")]
        public decimal RefMaxDistancePoints { get; set; } = 15m;

        [Category("Confirm")]
        [DisplayName("Ref Max Age (bars) - 参考Swing过旧阈值")]
        public int RefMaxAgeBars { get; set; } = 40;

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
        [DisplayName("M5 Min Displacement Range (ticks, 0=use legacy points)")]
        public int M5MinDisplacementRangeTicks { get; set; } = 0;

        [Category("M5 Zones")]
        [DisplayName("LEGACY: M5 Min Displacement Range (points)")]
        public decimal M5MinDisplacementRangePoints { get; set; } = 2.0m;

        [Category("M5 Zones")]
        [DisplayName("M5 Max Zone Width (ticks, 0=use legacy points)")]
        public int M5MaxZoneWidthTicks { get; set; } = 0;

        [Category("M5 Zones")]
        [DisplayName("LEGACY: M5 Max Zone Width (points, 0 disables)")]
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
        public bool CompactHud { get; set; } = false;

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
        // ===== 配置持久化 =====
        [Category("Plan")]
        [DisplayName("Save Config to File (设为true则保存当前参数到JSON)")]
        public bool SaveConfig { get; set; } = false;

        private string GetConfigPath()
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ATAS", "StrategyLogs");
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, "NQOrderFlowV1_config.json");
        }

        private void SaveConfigToFile()
        {
            try
            {
                var config = new Dictionary<string, object?>();
                var props = GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                foreach (var prop in props)
                {
                    if (prop.Name is "SaveConfig") continue;
                    var cat = prop.GetCustomAttribute<System.ComponentModel.CategoryAttribute>();
                    if (cat is null) continue;
                    config[prop.Name] = prop.GetValue(this);
                }
                var json = System.Text.Json.JsonSerializer.Serialize(config, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(GetConfigPath(), json);
                AppendLog($"CONFIG_SAVED path={GetConfigPath()} props={config.Count}");
            }
            catch (Exception ex)
            {
                AppendLog($"CONFIG_SAVE_ERR err={ex.Message}");
            }
        }

        private void LoadConfigFromFile()
        {
            try
            {
                var path = GetConfigPath();
                if (!File.Exists(path))
                {
                    AppendLog("CONFIG_NO_FILE (using code defaults)");
                    return;
                }
                var json = File.ReadAllText(path);
                var config = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(json);
                if (config is null) return;

                var props = GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                var loaded = 0;
                foreach (var prop in props)
                {
                    if (!config.TryGetValue(prop.Name, out var element)) continue;
                    try
                    {
                        var value = System.Text.Json.JsonSerializer.Deserialize(element.GetRawText(), prop.PropertyType);
                        prop.SetValue(this, value);
                        loaded++;
                    }
                    catch { }
                }
                AppendLog($"CONFIG_LOADED path={path} loaded={loaded}/{config.Count}");
            }
            catch (Exception ex)
            {
                AppendLog($"CONFIG_LOAD_ERR err={ex.Message}");
            }
        }
    }
}
