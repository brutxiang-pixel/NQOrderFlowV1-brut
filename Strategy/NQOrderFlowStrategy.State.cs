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

        // =====================================================================
        // P0: Instrument rules runtime cache
        // =====================================================================
        private bool _instrumentRulesReady = false;
        private decimal _tickSize = 0m;
        private decimal _qtyStep = 0m;
        private decimal _minQty = 0m;

        private decimal _lastParamTickSize = decimal.MinValue;
        private decimal _lastParamQtyStep = decimal.MinValue;
        private decimal _lastParamMinQty = decimal.MinValue;
        private bool _lastParamUseDecQty = false;
        private decimal _lastParamEntryQty = decimal.MinValue;
        private int _instrumentRulesLastLogBar = -999999;

        private void EnsureInstrumentRulesInitialized(int bar, string src)
        {
            // normalize bar for logs
            if (bar < 0)
                bar = Math.Max(-1, _lastCalcBar);

            var pTick = InstrumentTickSize;
            var pStep = InstrumentQtyStep;
            var pMin = InstrumentMinQty;

            var pUseDec = UseDecimalEntryQuantity;
            var pEntryQty = EntryQuantity;

            var changed =
                !_instrumentRulesReady ||
                pTick != _lastParamTickSize ||
                pStep != _lastParamQtyStep ||
                pMin != _lastParamMinQty ||
                pUseDec != _lastParamUseDecQty ||
                pEntryQty != _lastParamEntryQty;

            if (!changed)
                return;

            _lastParamTickSize = pTick;
            _lastParamQtyStep = pStep;
            _lastParamMinQty = pMin;
            _lastParamUseDecQty = pUseDec;
            _lastParamEntryQty = pEntryQty;

            // tick
            var tick = pTick > 0m ? pTick : TickSizeNq; // fallback to legacy default
            if (tick <= 0m)
                tick = TickSizeNq;

            // qty step/min
            var step = pStep > 0m ? pStep : 1m;
            if (step <= 0m)
                step = 1m;

            var min = pMin > 0m ? pMin : step;
            if (min <= 0m)
                min = step;

            if (min < step)
                min = step;

            _tickSize = tick;
            _qtyStep = step;
            _minQty = min;

            _instrumentRulesReady = true;

            // log (降频：仅当变化时记录；或很久没记录也可补一次)
            if (bar - _instrumentRulesLastLogBar >= 1)
            {
                _instrumentRulesLastLogBar = bar;
                EnsureLogInitialized();
                AppendLog(
                    $"INSTRUMENT_RULES src={src} bar={bar} " +
                    $"tick={_tickSize:0.########} qtyStep={_qtyStep:0.########} minQty={_minQty:0.########} " +
                    $"entryQtyMode={(UseDecimalEntryQuantity ? "DEC" : "CONTRACTS")} " +
                    $"entryQty={(UseDecimalEntryQuantity ? EntryQuantity.ToString("0.########") : Math.Max(1, Contracts).ToString())}");
            }
        }

        private decimal GetTickSize(int bar = -1, string src = "GetTickSize")
        {
            EnsureInstrumentRulesInitialized(bar, src);
            return _tickSize;
        }

        private decimal GetHalfTick(int bar = -1, string src = "GetHalfTick")
            => GetTickSize(bar, src) / 2m;

        private decimal GetQtyStep(int bar = -1, string src = "GetQtyStep")
        {
            EnsureInstrumentRulesInitialized(bar, src);
            return _qtyStep;
        }

        private decimal GetMinQty(int bar = -1, string src = "GetMinQty")
        {
            EnsureInstrumentRulesInitialized(bar, src);
            return _minQty;
        }

        private decimal GetRawEntryQuantity()
        {
            // 主交易数量入口：
            // - futures: 默认 Contracts(int)
            // - crypto/XAU: 建议 UseDecimalEntryQuantity=true 用 EntryQuantity(decimal)
            if (UseDecimalEntryQuantity)
                return EntryQuantity;

            return Math.Max(1, Contracts);
        }

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
        // M5 consecutive invalidation counter (require 2 bars)
        // =========================
        private int _invalidCloseBars = 0;

        // =========================
        // OF Armed
        // =========================
        private bool _ofArmed = false;
        private int _ofArmedAtBar = -1;
        private bool _mktEntryScheduled = false;
        private int _mktEntryBar = -1;
        private int _ofArmedScore = 0;
        private int _ofArmedBullWeight = 0;
        private int _ofArmedBearWeight = 0;
        private bool _ofDirMismatchLogged = false;
        private int _mismatchCount = 0;
        private string _mismatchDir = "";  // "BULL"/"BEAR" - OF方向 during consecutive mismatch
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

            // BUGFIX #1: prevent duplicate cancel logs (Expired/Runaway already logged)
            public bool CancelLogged { get; set; } = false;
        }

        private PendingEntry? _pendingEntry;

        // =====================================================================
        // Stage B live order state
        // =====================================================================
        private sealed class LiveOrderState
        {
            public string TradeId { get; init; } = "-";
            public string Side { get; init; } = "LONG";
            public bool IsLong { get; init; }

            public ZoneKey ZoneKey { get; init; } = new(ZoneType.BullishFVG, 0, 0, 0, 0);
            public TradingZone ZoneShadow { get; init; } = new();

            public int CreatedBar { get; init; }
            public int EffectiveMaxWaitBars { get; init; }
            public int RunawayBars { get; set; }

            public decimal EntryLimit { get; init; }
            public string AnchorText { get; init; } = "-";
            public ZoneEntryAnchor AnchorUsed { get; init; } = ZoneEntryAnchor.Mid;

            public int OfScore { get; init; }
            public string OfText { get; init; } = "-";
            public int LockedQScore { get; init; }

            public Order? EntryOrder { get; set; }
            public decimal EntryFilledQty { get; set; } = 0m;

            public string? OcoGroup { get; set; }
            public Order? StopOrder { get; set; }
            public Order? TargetOrder { get; set; }

            public decimal BracketQty { get; set; } = 0m;
            public decimal ExitFilledQty { get; set; } = 0m;

            public decimal StopPrice { get; set; } = 0m;
            public decimal TargetPrice { get; set; } = 0m;

            public bool BracketSubmitted { get; set; } = false;
            public bool ExitCompleted { get; set; } = false;
        }

        private LiveOrderState? _live;

        // ===== Tick alignment helpers (direction-safe) + SelfTest state =====
        private Order? _selfTestOrder;

        private bool _selfTestRequested = false;
        private bool _selfTestSubmitted = false;
        private int _selfTestLastLogBar = -999999;

        private string? _selfTestTag;
        private long _selfTestExtId = 0;
        private OrderStates? _selfTestLastState = null;

        // serializes order actions triggered by callbacks / OnCalculate
        private readonly SemaphoreSlim _orderActionLock = new(1, 1);

        private int _lastCalcBar = -1;
        private DateTime _lastCandleTime;
        private int _lastHudTradeCount = -1;

        // =========================
        // TradePlan
        // =========================
        private enum PlanState
        {
            Flat,
            InPosition
        }
        private const string ResetReason_M5CloseZoneInvalid = "锁定区块M5收盘失效";
        private PlanState _planState = PlanState.Flat;
        private TradePlan? _activePlan;

        private int _cooldownUntilBar = -1;
        private int _lastTriggeredConfirmStartBar = -1;
        private ZoneKey? _lastTriggeredZoneKey;
        //private int _retraceEntryBar = -1;
        private int _htfBosHtfIndex = -1;

        private sealed class TradePlan
        {
            public int CreatedBar { get; init; }
            public string Side { get; init; } = "LONG"; // LONG / SHORT

            public decimal Entry { get; init; }
            public decimal Stop { get; set; }
            public decimal Target { get; set; }

            public decimal InitialStop { get; init; }
            public decimal InitialRiskPoints { get; init; }

            public bool BreakEvenMoved { get; set; } = false;
            public bool DynamicTPAdjusted { get; set; } = false;

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
            public DateTime ExitTime { get; init; }

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
            RiskTicksTooLarge,

            LiveOrderPending,
            LiveOrderError
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
    }
}
