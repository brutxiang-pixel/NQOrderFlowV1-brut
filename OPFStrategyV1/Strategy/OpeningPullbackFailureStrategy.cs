using ATAS.Indicators;
using ATAS.DataFeedsCore;
using ATAS.Strategies;
using ATAS.Strategies.Chart;
using OFT.Rendering.Context;
using OFT.Rendering.Tools;
using OPFStrategyV1.Core.MarketData;
using OPFStrategyV1.Core.Configuration;
using OPFStrategyV1.Core.Profiles;
using OPFStrategyV1.Core.Scoring;
using OPFStrategyV1.Core.Signals;
using OPFStrategyV1.Core.Snapshots;
using OPFStrategyV1.Core.Versions;
using OPFStrategyV1.Regime;
using OPFStrategyV1.Research;
using OPFStrategyV1.Zones;
using System.ComponentModel;
using System.Drawing;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OPFStrategyV1.Strategy;

public sealed class OpeningPullbackFailureStrategy : ChartStrategy
{
    private const decimal MaxStructureSwingRiskPoints = 25m;
    private const decimal MaxConfirmBarRiskPoints = 25m;
    private const decimal MinConfirmBarRiskPoints = 10m;
    private const decimal MinVolAdjustedRiskPoints = 12m;
    private const decimal DailyVolumeResearchFillerMaxRiskPoints = 11m;
    private const decimal MainlineVolumeFillerMinSetupQualityScore = 60m;
    private const decimal MainlineVolumeFillerMaxRiskPoints = 18m;
    private const decimal MainlineVolumeFillerMinEstimatedRr = 0.8m;
    private const decimal DailyVolumeFloorMinSetupQualityScore = 48m;
    private const decimal DailyVolumeFloorMaxRiskPoints = 18m;
    private const decimal DailyVolumeFloorMinEstimatedRr = 0.5m;
    private const decimal DailyVolumeQualityRescueMinSetupQualityScore = 70m;
    private const decimal DailyVolumeQualityRescueMaxRiskPoints = 22m;
    private const decimal DailyVolumeQualityRescueMinEstimatedRr = 0.8m;
    private const int DailyVolumeQualityRescueMaxTradesPerDay = 4;
    private const decimal ZoneBirthVolumeV122MinSetupQualityScore = 45m;
    private const decimal ZoneBirthVolumeV122MaxRiskPoints = 25m;
    private const decimal ZoneBirthVolumeV122MinEstimatedRr = 0.3m;
    private const decimal StrictVolumeV122MinSetupQualityScore = 45m;
    private const decimal StrictVolumeV122MaxRiskPoints = 25m;
    private const decimal StrictVolumeV122MinEstimatedRr = 0.5m;
    private const decimal BreakawayVolumeV128MinSetupQualityScore = 76m;
    private const decimal BreakawayVolumeV128LongMaxRiskPoints = 25m;
    private const decimal BreakawayVolumeV128ShortMaxRiskPoints = 30m;
    private const decimal BreakawayVolumeV128MinEstimatedRr = 1m;
    private const decimal BreakawayLongSelectiveV134MinSetupQualityScore = 95m;
    private const decimal BreakawayLongSelectiveV134MaxRiskPoints = 18m;
    private const decimal BreakawayLongSelectiveV134MinEstimatedRr = 1.5m;
    private const int PartialEntryFinalizeDelayMs = 250;
    private const decimal ObservationLongVolumeV123MinSetupQualityScore = 70m;
    private const decimal ObservationLongVolumeV123MaxRiskPoints = 18m;
    private const decimal ObservationLongVolumeV123MinEstimatedRr = 0.8m;
    private const decimal UnknownMicroRiskVolumeV126MinSetupQualityScore = 45m;
    private const decimal UnknownMicroRiskVolumeV126MaxRiskPoints = 8.5m;
    private const decimal UnknownMicroRiskVolumeV126MinEstimatedRr = 1.5m;
    private const decimal ObservationShortVolumeV124MinSetupQualityScore = 60m;
    private const decimal ObservationShortVolumeV124MaxRiskPoints = 18m;
    private const decimal ObservationShortVolumeV124MinEstimatedRr = 1m;
    private const decimal ObservationConfirmLongV137MaxRiskPoints = 12m;
    private const decimal ObservationConfirmWideStopVolumeV131LongMinSetupQualityScore = 70m;
    private const decimal ObservationConfirmWideStopVolumeV131ShortMinSetupQualityScore = 60m;
    private const decimal ObservationConfirmWideStopVolumeV131MaxRiskPoints = 22m;
    private const decimal ObservationConfirmWideStopVolumeV131MinEstimatedRr = 0.8m;
    private const decimal ObservationConfirmWideStopLongExpansionV157MaxRiskPoints = 25m;
    private const int ObservationConfirmWideStopLongExpansionV157MaxTradesPerDay = 1;
    private const decimal ObservationConfirmWideStopLowRiskV132MinSetupQualityScore = 56m;
    private const decimal ObservationConfirmWideStopLowRiskV132MaxRiskPoints = 18m;
    private const decimal ObservationConfirmWideStopLowRiskV132MinEstimatedRr = 0.5m;
    private const decimal ObservationConfirmRisk22V132MinSetupQualityScore = 70m;
    private const decimal ObservationConfirmRisk22V132MaxRiskPoints = 22m;
    private const decimal ObservationConfirmRisk22V132MinEstimatedRr = 0.8m;
    private const decimal SelectiveProfitTargetV145R = 2.0m;
    private const decimal EntryFillRiskDriftTolerancePoints = 1m;
    private const decimal ShortObservationMidRiskQualityCutMinRisk = 8m;
    private const decimal ShortObservationMidRiskQualityCutMaxRisk = 15m;
    private const decimal ShortObservationMidRiskQualityCutMinScore = 60m;
    private const decimal ZoneQualityThreshold = 70m;
    private const decimal SetupQualityThreshold = 80m;
    private const int MaxPullbackBars = 24;
    private const int StaleUnfilledEntryMaxBars = 2;
    private static readonly TimeSpan ReplayStopGuardStart = new(20, 40, 0);
    private static readonly TimeSpan FridayReplayStopGuardStart = new(16, 40, 0);
    private const string ReplayStopExitRole = "SESSION_FLATTEN";
    private readonly IZoneDetector _zoneDetector = new FvgZoneDetector(
        minGapPoints: 0.50m,
        maxGapPoints: 12m,
        maxSourceCandleRangePoints: 80m);
    private readonly TrendScoreEngine _trendScoreEngine = new();
    private readonly HashSet<string> _loggedZoneIds = new();
    private readonly HashSet<string> _candidateZoneIds = new();
    private readonly HashSet<string> _breakawayZoneIds = new();
    private readonly HashSet<string> _shadowCandidateZoneIds = new();
    private readonly HashSet<string> _observationZoneIds = new();
    private readonly HashSet<string> _structureConfirmShadowIds = new();
    private readonly HashSet<string> _failureReverseSignalIds = new();
    private readonly HashSet<string> _loggedEvaluationKeys = new();
    private readonly List<PendingCandidate> _pendingCandidates = new();
    private readonly List<PendingBreakawayRetest> _pendingBreakawayRetests = new();
    private readonly List<PendingMainlinePullbackRetest> _pendingMainlinePullbackRetests = new();
    private readonly List<PendingShadowCandidate> _pendingShadowCandidates = new();
    private readonly List<PendingObservationConfirm> _pendingObservationConfirms = new();
    private readonly List<PendingStructureConfirmShadow> _pendingStructureConfirmShadows = new();
    private readonly List<PendingConfirmBarWait1> _pendingConfirmBarWait1s = new();
    private readonly List<PendingConfirmedRetrace> _pendingConfirmedRetraces = new();
    private readonly List<PendingFailureReverseRetest> _pendingFailureReverseRetests = new();
    private readonly List<ResearchTracker> _researchTrackers = new();
    private readonly List<ReplayExecutionState> _recentReplayExecutions = new();
    private readonly Dictionary<string, ActualTradeOutcome> _actualTradeOutcomesBySignalPath = new();
    private readonly HashSet<string> _writtenExecutionTradeIds = new();
    private readonly HashSet<string> _writtenResearchOutcomeKeys = new();
    private readonly List<OpfCandle> _recentCandles = new();
    private readonly SemaphoreSlim _executionLock = new(1, 1);
    private PullbackEpisode? _activeBullPullback;
    private PullbackEpisode? _activeBearPullback;
    private decimal? _bullTrendHigh;
    private decimal? _bearTrendLow;
    private int _bullPullbackCountInRegime;
    private int _bearPullbackCountInRegime;
    private ResearchLogger? _researchLogger;
    private ConfigSnapshot? _snapshot;
    private readonly Dictionary<DateTime, RegimeDailyStats> _regimeDailyStatsByDate = new();
    private MarketRegime? _lastRegime;
    private readonly object _renderLock = new();
    private string _hudText = "OPF v0.1\nWaiting for data...";
    private string _lastRegimeChangeText = "-";
    private int _candidateCount;
    private int _confirmedCount;
    private int _researchOutcomeCount;
    private int _activeZoneCount;
    private int _lastSeenBar = -1;
    private int _lastProcessedBar = -1;
    private OpfCandle? _previousCandle;
    private OpfCandle? _lastResearchCandle;
    private ReplayExecutionState? _replayExecution;
    private DateTime _replayExecutionDate = DateTime.MinValue;
    private int _replayTradesToday;
    private int _replayExitsToday;
    private int _replayTpToday;
    private int _replaySlToday;
    private int _replayOtherExitToday;
    private int _observationConfirmFillerTradesToday;
    private int _observationConfirmQualityRescueTradesToday;
    private int _observationConfirmWideStopLongExpansionTradesToday;
    private int _replayFullLossTradesToday;
    private int _replayConsecutiveLossesToday;
    private decimal _replayDailyPnlDollars;
    private decimal _replayDailyR;
    private string _lastExecutionHudText = "-";
    private readonly Queue<string> _recentExecutionHudItems = new();
    private bool _actualRequireTrendRegime = true;
    private decimal _actualMinSetupQualityScore = 70m;
    private decimal _actualFailureReverseMinSetupQualityScore = 40m;
    private decimal _actualBreakawayMaxRiskPoints = 11m;
    private decimal _actualObservationConfirmMaxRiskPoints = 11m;
    private decimal _actualObservationConfirmVolumeMaxRiskPoints = 20m;
    private decimal _actualObservationConfirmMinSetupQualityScore = 56m;
    private decimal _actualObservationConfirmFillerMinSetupQualityScore = 46m;
    private int _actualObservationConfirmMaxFillerTradesPerDay = 5;
    private int _actualObservationConfirmFillerUntilDailyTrades = 7;
    private decimal _actualFailureRetestMaxRiskPoints = 11m;
    private decimal _actualFailureRetestMinSetupQualityScore = 56m;
    private bool _actualRequireFailureRetest = true;
    private bool _compactResearchLogging;
    private bool _isStoppingActualExecution;
    private bool _orphanPositionFlattenPending;
    private int _orphanPositionFlattenLastBar = -1;
    private DateTime _dailyAbnormalFillGuardDate = DateTime.MinValue;
    private int _dailyAbnormalFillGuardCount;
    private static PropertyInfo? _vwapProperty;

    [Category("OPF Profile")]
    [DisplayName("Instrument Profile")]
    public string InstrumentProfileName { get; set; } = "MNQ_0.1";

    [Category("OPF Profile")]
    [DisplayName("Execution Profile")]
    public string ExecutionProfileName { get; set; } = "MNQ_2Contract_Evidence";

    [Category("OPF Research")]
    [DisplayName("Enable Research Logging")]
    public bool EnableResearchLogging { get; set; } = true;

    [Category("OPF HUD")]
    [DisplayName("Show Research HUD")]
    public bool ShowResearchHud { get; set; } = true;

    [Category("OPF HUD")]
    [DisplayName("Show Actual Order Lines")]
    public bool ShowActualOrderLines { get; set; } = true;

    [Category("OPF HUD")]
    [DisplayName("Actual Line Lookback Bars")]
    public int ActualLineLookbackBars { get; set; } = 40;

    [Category("OPF HUD")]
    [DisplayName("Actual Max Visible Orders")]
    public int ActualMaxVisibleOrders { get; set; } = 3;

    [Category("OPF Execution")]
    [DisplayName("Enable Actual Orders")]
    public bool EnableReplayOrders { get; set; } = true;

    [Category("OPF Execution")]
    [DisplayName("Actual Execution Paths")]
    public string ReplayExecutionPath { get; set; } = "*";

    [Category("OPF Execution")]
    [DisplayName("Actual Allow Research Paths")]
    public bool ReplayAllowResearchPaths { get; set; } = false;

    [Category("OPF Execution")]
    [DisplayName("Actual Order Quantity")]
    public decimal ReplayOrderQuantity { get; set; } = 2m;

    [Category("OPF Execution")]
    [DisplayName("Actual Target R")]
    public decimal ReplayTargetR { get; set; } = 1.5m;

    [Category("OPF Execution")]
    [DisplayName("Actual Max Trades Per Day")]
    public int ReplayMaxTradesPerDay { get; set; } = 5;

    [Category("OPF Execution")]
    [DisplayName("Actual Use Full Loss Guard")]
    public bool ReplayUseFullLossGuard { get; set; } = false;

    [Category("OPF Execution")]
    [DisplayName("Actual Use Consecutive Loss Guard")]
    public bool ReplayUseConsecutiveLossGuard { get; set; } = false;

    [Category("OPF Execution")]
    [DisplayName("Actual TimeInForce")]
    public TimeInForce ReplayTimeInForce { get; set; } = TimeInForce.Day;

    [Category("OPF Execution")]
    [DisplayName("Actual Stop Trigger Type")]
    public TriggerPriceType ReplayStopTriggerPriceType { get; set; } = TriggerPriceType.Last;

    [Category("OPF Execution")]
    [DisplayName("Actual Min Estimated RR")]
    public decimal ActualMinEstimatedRr { get; set; } = 1.0m;

    [Category("OPF Execution")]
    [DisplayName("Actual Wide Stop Multiplier")]
    public decimal ActualWideStopMultiplier { get; set; } = 1.5m;

    [Category("OPF Execution")]
    [DisplayName("Actual Enable Wide Stop Execution")]
    public bool ActualEnableWideStopExecution { get; set; } = true;

    public OpeningPullbackFailureStrategy()
    {
        EnableCustomDrawing = true;
        SubscribeToDrawingEvents(DrawingLayouts.Final | DrawingLayouts.LatestBar);
    }

    protected override void OnStarted()
    {
        base.OnStarted();

        var actualExecutionSettings = ActualExecutionSettings.LoadOrCreateDefault(
            out var actualExecutionConfigPath,
            out var actualExecutionConfigStatus);
        ApplyActualExecutionSettings(actualExecutionSettings);

        var resolvedExecutionProfileName = ResolveExecutionProfileName(actualExecutionSettings);
        var profileSelection = ProfileCatalog.Select(InstrumentProfileName, resolvedExecutionProfileName);
        _snapshot = ConfigSnapshot.Create(
            profileSelection,
            actualExecutionConfigPath,
            actualExecutionConfigStatus,
            actualExecutionSettings);

        if (EnableResearchLogging)
        {
            _researchLogger = new ResearchLogger("OPFStrategyV1", actualExecutionSettings.ResearchLogMode);
            _researchLogger.WriteConfigSnapshot(_snapshot);
            _researchLogger.AppendSignalHeader(_snapshot.SnapshotId);
            _researchLogger.AppendInfo(
                _snapshot.SnapshotId,
                0,
                DateTime.UtcNow,
                $"ACTUAL_EXEC_CONFIG status={actualExecutionConfigStatus} path={actualExecutionConfigPath}");

            if (profileSelection.UsedInstrumentFallback)
            {
                _researchLogger.AppendInfo(
                    _snapshot.SnapshotId,
                    0,
                    DateTime.UtcNow,
                    $"UNKNOWN_INSTRUMENT_PROFILE requested={InstrumentProfileName} fallback={profileSelection.InstrumentProfile.Version} supported={ProfileCatalog.SupportedInstrumentProfileNames}");
            }

            if (profileSelection.UsedExecutionFallback)
            {
                _researchLogger.AppendInfo(
                    _snapshot.SnapshotId,
                    0,
                    DateTime.UtcNow,
                    $"UNKNOWN_EXECUTION_PROFILE requested={resolvedExecutionProfileName} fallback={profileSelection.ExecutionProfile.Name} supported={ProfileCatalog.SupportedExecutionProfileNames}");
            }
        }
    }

    private string ResolveExecutionProfileName(ActualExecutionSettings settings)
    {
        if (settings.ActualOrderQuantity == 2m &&
            string.Equals(settings.ResearchLogMode, "Compact", StringComparison.OrdinalIgnoreCase) &&
            settings.ActualTargetR == 1.5m &&
            !settings.ActualUseFullLossGuard &&
            !settings.ActualUseConsecutiveLossGuard)
        {
            return "MNQ_2Contract_Evidence";
        }

        return ExecutionProfileName;
    }

    private void ApplyActualExecutionSettings(ActualExecutionSettings settings)
    {
        EnableReplayOrders = settings.EnableActualOrders;
        ReplayExecutionPath = settings.ActualExecutionPaths;
        ReplayAllowResearchPaths = settings.ActualAllowResearchPaths;
        ReplayOrderQuantity = settings.ActualOrderQuantity;
        ReplayTargetR = settings.ActualTargetR;
        ReplayMaxTradesPerDay = settings.ActualMaxTradesPerDay;
        ReplayUseFullLossGuard = settings.ActualUseFullLossGuard;
        ReplayUseConsecutiveLossGuard = settings.ActualUseConsecutiveLossGuard;
        ActualMinEstimatedRr = settings.ActualMinEstimatedRr;
        _actualBreakawayMaxRiskPoints = settings.ActualBreakawayMaxRiskPoints;
        _actualObservationConfirmMaxRiskPoints = settings.ActualObservationConfirmMaxRiskPoints;
        _actualObservationConfirmVolumeMaxRiskPoints = settings.ActualObservationConfirmVolumeMaxRiskPoints;
        _actualObservationConfirmMinSetupQualityScore = settings.ActualObservationConfirmMinSetupQualityScore;
        _actualObservationConfirmFillerMinSetupQualityScore = settings.ActualObservationConfirmFillerMinSetupQualityScore;
        _actualObservationConfirmMaxFillerTradesPerDay = settings.ActualObservationConfirmMaxFillerTradesPerDay;
        _actualObservationConfirmFillerUntilDailyTrades = settings.ActualObservationConfirmFillerUntilDailyTrades;
        _actualFailureRetestMaxRiskPoints = settings.ActualFailureRetestMaxRiskPoints;
        _actualFailureRetestMinSetupQualityScore = settings.ActualFailureRetestMinSetupQualityScore;
        _actualRequireTrendRegime = settings.ActualRequireTrendRegime;
        _actualMinSetupQualityScore = settings.ActualMinSetupQualityScore;
        _actualFailureReverseMinSetupQualityScore = settings.ActualFailureReverseMinSetupQualityScore;
        _actualRequireFailureRetest = settings.ActualRequireFailureRetest;
        ActualWideStopMultiplier = settings.ActualWideStopMultiplier;
        ActualEnableWideStopExecution = settings.ActualEnableWideStopExecution;
        _compactResearchLogging = string.Equals(settings.ResearchLogMode, "Compact", StringComparison.OrdinalIgnoreCase);

        if (Enum.TryParse<TimeInForce>(settings.ActualTimeInForce, ignoreCase: true, out var timeInForce))
            ReplayTimeInForce = timeInForce;
        if (Enum.TryParse<TriggerPriceType>(settings.ActualStopTriggerType, ignoreCase: true, out var triggerPriceType))
            ReplayStopTriggerPriceType = triggerPriceType;
    }

    protected override void OnStopped()
    {
        _isStoppingActualExecution = true;
        ProcessClosedBar(_lastSeenBar);
        FinalizeActiveExecutionOnStop();
        WriteRegimeDailyStats();
        FlushResearchTrackers("ResearchStopped");
        base.OnStopped();
    }

    protected override void OnCalculate(int bar, decimal value)
    {
        _lastSeenBar = Math.Max(_lastSeenBar, bar);
        ProcessClosedBar(bar - 1);
    }

    private void ProcessClosedBar(int bar)
    {
        if (_snapshot is null || bar < 0 || bar == _lastProcessedBar)
            return;

        var candle = GetCandle(bar);
        if (candle is null)
            return;

        _lastProcessedBar = bar;

        var current = ToOpfCandle(bar, candle);
        _lastResearchCandle = current;
        UpdateActualExecutionExcursion(current);
        ScheduleStaleUnfilledEntryAbortIfNeeded(current);
        ScheduleReplayStopExitIfNeeded(current);
        ScheduleOrphanPositionFlattenIfNeeded(current);
        ScheduleProtectionCleanupIfNeeded("ClosedBar");
        var zones = _zoneDetector.Update(current);
        var regime = _trendScoreEngine.Update(current);

        if (!EnableResearchLogging)
            return;

        _researchLogger?.AppendScoreBreakdown(_snapshot.SnapshotId, current.Time, bar, "BULL", regime.BullTrendScore);
        _researchLogger?.AppendScoreBreakdown(_snapshot.SnapshotId, current.Time, bar, "BEAR", regime.BearTrendScore);
        LogNewZones(current, zones);
        RecordRegimeDailyStats(current, regime);

        if (_lastRegime != regime.Regime)
        {
            var from = _lastRegime?.ToString() ?? "-";
            _lastRegimeChangeText = $"{current.Time:HH:mm} {from}->{regime.Regime}";
            _researchLogger?.AppendRegimeChange(
                _snapshot.SnapshotId,
                current.Time,
                bar,
                regime.Regime,
                regime.BullTrendScore.TotalScore,
                regime.BearTrendScore.TotalScore);
            _lastRegime = regime.Regime;
        }

        _activeZoneCount = zones.Count;
        UpdateResearchTrackers(current);
        EvaluatePendingCandidates(current);
        EvaluateConfirmedRetraces(current);
        EvaluateMainlinePullbackRetests(current);
        EvaluateBreakawayRetests(current);
        EvaluateShadowCandidates(current);
        EvaluateObservationConfirms(current);
        EvaluateFailureReverseRetests(current);
        EvaluateStructureConfirmShadows(current);
        EvaluateConfirmBarWait1s(current);

        if (regime.Regime == MarketRegime.Unknown)
        {
            var signalId = $"{current.Time:yyyyMMdd-HHmm}-NONE-{bar:000000}";
            _researchLogger?.AppendNoTrade(_snapshot.SnapshotId, signalId, current.Time, bar, "Regime", UnknownRegimeReasons(regime));
        }

        LogCandidateSignals(current, regime, zones);
        UpdatePullbackEpisodes(current, regime.Regime);
        UpdateHud(current, regime);

        if (bar == 0)
            _researchLogger?.AppendInfo(_snapshot.SnapshotId, bar, current.Time, $"OPF setup scaffold started. zones={zones.Count}");

        _previousCandle = current;
        AddRecentCandle(current);
    }

    protected override void OnRender(RenderContext context, DrawingLayouts layout)
    {
        if (ChartInfo is null)
            return;

        if (ShowActualOrderLines)
            DrawActualExecutionLines(context);

        if (ShowResearchHud)
        {
            string hud;
            lock (_renderLock)
                hud = _hudText;

            DrawHud(context, hud);
        }
    }

    protected override void OnNewOrder(Order order)
    {
        base.OnNewOrder(order);
        AttachReplayOrder(order, "OnNewOrder");
    }

    protected override void OnOrderChanged(Order order)
    {
        base.OnOrderChanged(order);
        AttachReplayOrder(order, "OnOrderChanged");
        ScheduleProtectionCleanupIfNeeded("OnOrderChanged");
        ScheduleProtectionLossCheckIfNeeded("OnOrderChanged");
    }

    protected override void OnNewMyTrade(MyTrade myTrade)
    {
        base.OnNewMyTrade(myTrade);
        if (!EnableReplayOrders)
            return;

        var trade = myTrade.Clone();
        EnqueueExecutionAction("HandleReplayTrade", async () => await HandleReplayTradeAsync(trade));
    }

    protected override void OnOrderRegisterFailed(Order order, string message)
    {
        base.OnOrderRegisterFailed(order, message);
        LogExecutionInfo($"EXEC_REGISTER_FAILED role={ParseExecutionRole(order?.Comment)} ext={order?.ExtId} msg={message}");
        if (IsActiveExecutionOrder(order) &&
            (IsExitOrder(order) || string.Equals(ParseExecutionRole(order?.Comment), ReplayStopExitRole, StringComparison.OrdinalIgnoreCase)))
            EnqueueExecutionAction("FlattenOnProtectionRegisterFailed", async () => await SubmitEmergencyFlattenAsync(order?.QuantityToFill ?? 0m, "ProtectionRegisterFailed"));
    }

    protected override void OnOrderCancelFailed(Order order, string message)
    {
        base.OnOrderCancelFailed(order, message);
        if (IsExpectedOcoCancelAlreadyInactive(order, message))
        {
            LogExecutionInfo($"EXEC_CANCEL_ALREADY_INACTIVE role={ParseExecutionRole(order?.Comment)} ext={order?.ExtId} msg={message}");
            AppendOrderFailureEvent(order, "CANCEL_ALREADY_INACTIVE", message);
            return;
        }

        LogExecutionInfo($"EXEC_CANCEL_FAILED role={ParseExecutionRole(order?.Comment)} ext={order?.ExtId} msg={message}");
        AppendOrderFailureEvent(order, "CANCEL_FAIL", message);
    }

    private void AttachReplayOrder(Order? order, string source)
    {
        if (!EnableReplayOrders || order is null || _replayExecution is null)
            return;
        if (!TryParseExecutionComment(order.Comment, out var tradeId, out var role))
            return;
        if (!string.Equals(tradeId, _replayExecution.TradeId, StringComparison.OrdinalIgnoreCase))
            return;

        if (role == "ENTRY")
            _replayExecution.EntryOrder = order;
        else if (role == "SL")
            _replayExecution.StopOrder = order;
        else if (role == "TP")
            _replayExecution.TargetOrder = order;

        LogExecutionInfo($"EXEC_ORDER_ATTACH src={source} trade={tradeId} role={role} state={order.State} ext={order.ExtId} type={order.Type} dir={order.Direction} price={order.Price:0.########} trig={order.TriggerPrice:0.########} unfilled={order.Unfilled:0.########} qty={order.QuantityToFill:0.########}");
        if (IsFailedOrderState(order.State.ToString()))
        {
            var eventName = IsExpectedOcoSiblingInactive(order)
                ? "OCO_SIBLING_INACTIVE"
                : "ORDER_STATE_FAILED";
            AppendExecutionEvent(_replayExecution, eventName, role, order.Price > 0m ? order.Price : order.TriggerPrice, order.QuantityToFill, $"src={source}|state={order.State}|ext={order.ExtId}");
        }
    }

    private void LogNewZones(OpfCandle candle, IReadOnlyList<DetectedZone> zones)
    {
        if (_snapshot is null)
            return;

        foreach (var zone in zones)
        {
            if (!_loggedZoneIds.Add(zone.ZoneId))
                continue;

            _researchLogger?.AppendZone(_snapshot.SnapshotId, candle.Bar, candle.Time, zone);
        }
    }

    private void RecordRegimeDailyStats(OpfCandle candle, RegimeResult regime)
    {
        if (_snapshot is null)
            return;

        var date = candle.Time.Date;
        if (!_regimeDailyStatsByDate.TryGetValue(date, out var stats))
        {
            stats = new RegimeDailyStats(date);
            _regimeDailyStatsByDate.Add(date, stats);
        }

        stats.Record(regime.Regime, regime.BullTrendScore.TotalScore, regime.BearTrendScore.TotalScore);
        WriteRegimeDailyStats();
    }

    private void WriteRegimeDailyStats()
    {
        if (_snapshot is null || _regimeDailyStatsByDate.Count == 0)
            return;

        var summaries = _regimeDailyStatsByDate.Values
            .Where(x => x.TotalBars > 0)
            .Select(x => new ResearchLogger.RegimeDailySummary(
                _snapshot.SnapshotId,
                x.Date,
                x.TotalBars,
                x.BullTrendBars,
                x.BearTrendBars,
                x.UnknownBars,
                x.RegimeChangeCount,
                x.AvgBullScore,
                x.AvgBearScore))
            .ToArray();
        _researchLogger?.WriteRegimeDailySummaries(_snapshot.SnapshotId, summaries);
    }

    private void LogCandidateSignals(OpfCandle candle, RegimeResult regime, IReadOnlyList<DetectedZone> zones)
    {
        if (_snapshot is null)
            return;

        foreach (var zone in zones)
        {
            if (!IsZoneTouched(candle, zone))
                continue;

            var side = TryGetCandidateSide(regime.Regime, zone, candle);
            if (side is null)
            {
                var reason = CandidateSkipReason(regime.Regime, zone, candle);
                TryStartMainlinePullbackRetest(candle, regime.Regime, regime, zone, reason);
                TryStartBreakawayResearch(candle, regime.Regime, regime, zone);
                TryStartShadowCandidate(candle, regime.Regime, regime, zone, reason);
                TryStartObservationResearch(candle, regime.Regime, regime, zone, reason);
                LogCandidateEvaluation(candle, regime.Regime, zone, "Skipped", reason);
                continue;
            }

            TryStartMainlinePullbackCandidate(candle, regime.Regime, regime, zone, side.Value, "ZoneTouch");
        }
    }

    private bool TryStartMainlinePullbackCandidate(OpfCandle candle, MarketRegime marketRegime, RegimeResult regime, DetectedZone zone, TradeSide side, string sourceReason)
    {
        if (_snapshot is null)
            return false;

        var key = $"{marketRegime}:{zone.ZoneId}";
        if (!_candidateZoneIds.Add(key))
        {
            LogCandidateEvaluation(candle, marketRegime, zone, "Duplicate", "CandidateAlreadyLoggedForZone");
            return false;
        }

        var pullback = GetOrStartPullbackEpisode(candle, marketRegime, zone);
        if (pullback is not null && pullback.CountInRegime > 2)
        {
            _researchLogger?.AppendNoTrade(
                _snapshot.SnapshotId,
                BuildSignalId(candle, side, "PB", zone),
                candle.Time,
                candle.Bar,
                "TrendPullback",
                new[] { "ThirdPullback", $"PullbackCount={pullback.CountInRegime}", pullback.EpisodeId, sourceReason });
            LogCandidateEvaluation(candle, marketRegime, zone, "Skipped", "ThirdPullback");
            return false;
        }

        var signalId = BuildSignalId(candle, side, "PB", zone);
        var regimeScore = side == TradeSide.Long ? regime.BullTrendScore : regime.BearTrendScore;
        var setupScore = BuildSetupQualityScore(regimeScore, zone, "CandidateTouch", side, candle);
        var signal = new CandidateSignal(
            signalId,
            _snapshot.SnapshotId,
            candle.Time,
            candle.Bar,
            side,
            SetupType.TrendPullback,
            SignalStage.Candidate,
            zone,
            regimeScore,
            setupScore,
            new[] { sourceReason });

        _researchLogger?.AppendSignal(signal);
        _pendingCandidates.Add(new PendingCandidate(signal, candle.Bar + 2));
        AddStructureConfirmShadow(signal, candle.Bar + 12, "CandidateAfterRetest");
        var researchOnly = signal with { Stage = SignalStage.ResearchOnly };
        _researchLogger?.AppendSignal(researchOnly);
        StartResearchTracking(researchOnly, candle, "AlmostConfirmed");
        LogCandidateEvaluation(candle, marketRegime, zone, "Candidate", "WaitSecondConfirm");
        _candidateCount++;
        return true;
    }

    private void TryStartBreakawayResearch(OpfCandle candle, MarketRegime marketRegime, RegimeResult regime, DetectedZone zone)
    {
        if (_snapshot is null || candle.Bar != zone.CreatedBar)
            return;

        var side = TryGetTrendSide(marketRegime, zone);
        if (side is null)
            return;

        var key = $"{marketRegime}:{zone.ZoneId}";
        if (!_breakawayZoneIds.Add(key))
            return;

        var signalId = BuildSignalId(candle, side.Value, "BRK", zone);
        var regimeScore = side == TradeSide.Long ? regime.BullTrendScore : regime.BearTrendScore;
        var setupScore = BuildSetupQualityScore(regimeScore, zone, "Breakaway", side.Value, candle);
        var signal = new CandidateSignal(
            signalId,
            _snapshot.SnapshotId,
            candle.Time,
            candle.Bar,
            side.Value,
            SetupType.TrendPullback,
            SignalStage.ResearchOnly,
            zone,
            regimeScore,
            setupScore,
            new[] { "BreakawayFvgResearch" });

        _researchLogger?.AppendSignal(signal);
        StartResearchTracking(signal, candle, "BreakawayFvg");
        if (IsQualifiedBreakaway(signal, candle))
        {
            var qualified = signal with
            {
                SignalId = $"{signal.SignalId}-Q",
                SkipReasons = signal.SkipReasons.Concat(new[] { "BreakawayQualified" }).ToArray()
            };
            _researchLogger?.AppendSignal(qualified);
            StartResearchTracking(qualified, candle, "BreakawayFvg_Qualified");
        }
        _pendingBreakawayRetests.Add(new PendingBreakawayRetest(signal, candle.Bar + 6));
    }

    private void TryStartMainlinePullbackRetest(OpfCandle candle, MarketRegime marketRegime, RegimeResult regime, DetectedZone zone, string skipReason)
    {
        if (_snapshot is null || skipReason != "ZoneJustCreated" || candle.Bar != zone.CreatedBar)
            return;

        var side = TryGetTrendSide(marketRegime, zone);
        if (side is null)
            return;

        var key = $"{marketRegime}:{zone.ZoneId}";
        if (_pendingMainlinePullbackRetests.Any(x => x.Key == key))
            return;

        var regimeScore = side == TradeSide.Long ? regime.BullTrendScore : regime.BearTrendScore;
        _pendingMainlinePullbackRetests.Add(new PendingMainlinePullbackRetest(key, zone, side.Value, regimeScore, marketRegime, candle.Bar + 12));
    }

    private void EvaluateMainlinePullbackRetests(OpfCandle candle)
    {
        if (_snapshot is null || _pendingMainlinePullbackRetests.Count == 0)
            return;

        for (var i = _pendingMainlinePullbackRetests.Count - 1; i >= 0; i--)
        {
            var pending = _pendingMainlinePullbackRetests[i];
            if (candle.Bar <= pending.Zone.CreatedBar)
                continue;

            if (IsInvalidated(pending.Zone, candle))
            {
                _researchLogger?.AppendNoTrade(_snapshot.SnapshotId, BuildSignalId(candle, pending.Side, "PB", pending.Zone), candle.Time, candle.Bar, "TrendPullback", new[] { "MainlineRetestInvalidated" });
                _pendingMainlinePullbackRetests.RemoveAt(i);
                continue;
            }

            if (candle.Bar > pending.MaxRetestBar)
            {
                _researchLogger?.AppendNoTrade(_snapshot.SnapshotId, BuildSignalId(candle, pending.Side, "PB", pending.Zone), candle.Time, candle.Bar, "TrendPullback", new[] { "MainlineRetestExpired" });
                _pendingMainlinePullbackRetests.RemoveAt(i);
                continue;
            }

            if (!IsZoneTouched(candle, pending.Zone))
                continue;

            var regime = new RegimeResult(pending.MarketRegime, pending.RegimeScore, ScoreBreakdown.Empty("InactiveRegimeScore", pending.RegimeScore.Threshold));
            if (pending.Side == TradeSide.Short)
                regime = new RegimeResult(pending.MarketRegime, ScoreBreakdown.Empty("InactiveRegimeScore", pending.RegimeScore.Threshold), pending.RegimeScore);

            TryStartMainlinePullbackCandidate(candle, pending.MarketRegime, regime, pending.Zone, pending.Side, "MainlineRetest");
            _pendingMainlinePullbackRetests.RemoveAt(i);
        }
    }

    private void TryStartShadowCandidate(OpfCandle candle, MarketRegime marketRegime, RegimeResult regime, DetectedZone zone, string skipReason)
    {
        if (_snapshot is null || !IsShadowCandidateReason(skipReason))
            return;

        var side = TryGetTrendSide(marketRegime, zone);
        if (side is null)
            return;

        var key = $"{marketRegime}:{zone.ZoneId}:{skipReason}";
        if (!_shadowCandidateZoneIds.Add(key))
            return;

        var signalId = BuildSignalId(candle, side.Value, "SHD", zone);
        var regimeScore = side == TradeSide.Long ? regime.BullTrendScore : regime.BearTrendScore;
        var setupScore = BuildSetupQualityScore(regimeScore, zone, "Shadow", side.Value, candle);
        var signal = new CandidateSignal(
            signalId,
            _snapshot.SnapshotId,
            candle.Time,
            candle.Bar,
            side.Value,
            SetupType.TrendPullback,
            SignalStage.ResearchOnly,
            zone,
            regimeScore,
            setupScore,
            new[] { "ShadowCandidate", skipReason });

        _researchLogger?.AppendSignal(signal);
        _pendingShadowCandidates.Add(new PendingShadowCandidate(signal, candle.Bar + 12, skipReason));
    }

    private void TryStartObservationResearch(OpfCandle candle, MarketRegime marketRegime, RegimeResult regime, DetectedZone zone, string skipReason)
    {
        if (_snapshot is null || !IsObservationResearchReason(skipReason))
            return;

        var side = ZoneDirectionSide(zone);
        if (side is null)
            return;

        var path = skipReason == "RegimeUnknown"
            ? "UnknownRegimeZoneTouch"
            : "ZoneBirthResearch";
        var key = $"{path}:{zone.ZoneId}";
        if (!_observationZoneIds.Add(key))
            return;

        var signalId = BuildSignalId(candle, side.Value, "OBS", zone);
        var regimeScore = side == TradeSide.Long ? regime.BullTrendScore : regime.BearTrendScore;
        var setupScore = BuildSetupQualityScore(regimeScore, zone, "ObservationTouch", side.Value, candle);
        var signal = new CandidateSignal(
            signalId,
            _snapshot.SnapshotId,
            candle.Time,
            candle.Bar,
            side.Value,
            SetupType.TrendPullback,
            SignalStage.ResearchOnly,
            zone,
            regimeScore,
            setupScore,
            new[] { "ObservationResearch", skipReason, $"Regime={marketRegime}" });

        _researchLogger?.AppendSignal(signal);
        StartResearchTracking(signal, candle, path);
        _pendingObservationConfirms.Add(new PendingObservationConfirm(signal, candle.Bar + 3, path, skipReason));
    }

    private void EvaluateBreakawayRetests(OpfCandle candle)
    {
        if (_snapshot is null || _pendingBreakawayRetests.Count == 0)
            return;

        for (var i = _pendingBreakawayRetests.Count - 1; i >= 0; i--)
        {
            var pending = _pendingBreakawayRetests[i];
            var signal = pending.Signal;
            if (signal.Zone is null || candle.Bar <= signal.Bar)
                continue;

            if (IsInvalidated(signal.Zone, candle))
            {
                _researchLogger?.AppendNoTrade(_snapshot.SnapshotId, signal.SignalId, candle.Time, candle.Bar, "BreakawayRetest", new[] { "BreakawayZoneInvalidated" });
                _pendingBreakawayRetests.RemoveAt(i);
                continue;
            }

            var touched = IsZoneTouched(candle, signal.Zone);
            if (touched)
                pending.Touched = true;

            (bool Confirmed, string Reason) confirm = pending.Touched
                ? TryConfirm(signal, candle, _previousCandle)
                : (false, "WaitingRetestTouch");
            if (confirm.Confirmed)
            {
                var qualityReason = BreakawayRetestQualityRejectReason(signal, candle);
                if (qualityReason is not null)
                {
                    _researchLogger?.AppendNoTrade(_snapshot.SnapshotId, signal.SignalId, candle.Time, candle.Bar, "BreakawayRetest", new[] { qualityReason, confirm.Reason });
                    _pendingBreakawayRetests.RemoveAt(i);
                    continue;
                }

                var retestSignal = signal with
                {
                    SignalId = signal.SignalId.Replace("-BRK-", "-BRT-"),
                    Time = candle.Time,
                    Bar = candle.Bar,
                    Stage = SignalStage.ResearchOnly,
                    SetupQualityScore = BuildSetupQualityScore(signal.RegimeScore, signal.Zone, "BreakawayRetest", signal.Side, candle),
                    SkipReasons = new[] { "BreakawayRetestResearch", confirm.Reason }
                };

                _researchLogger?.AppendSignal(retestSignal);
                StartResearchTracking(retestSignal, candle, "BreakawayRetest");
                _pendingBreakawayRetests.RemoveAt(i);
                continue;
            }

            if (candle.Bar >= pending.MaxRetestBar)
            {
                _researchLogger?.AppendNoTrade(_snapshot.SnapshotId, signal.SignalId, candle.Time, candle.Bar, "BreakawayRetest", new[] { "BreakawayRetestExpired", confirm.Reason });
                _pendingBreakawayRetests.RemoveAt(i);
            }
        }
    }

    private void EvaluateShadowCandidates(OpfCandle candle)
    {
        if (_snapshot is null || _pendingShadowCandidates.Count == 0)
            return;

        for (var i = _pendingShadowCandidates.Count - 1; i >= 0; i--)
        {
            var pending = _pendingShadowCandidates[i];
            var signal = pending.Signal;
            if (signal.Zone is null || candle.Bar <= signal.Bar)
                continue;

            if (IsInvalidated(signal.Zone, candle))
            {
                _researchLogger?.AppendNoTrade(_snapshot.SnapshotId, signal.SignalId, candle.Time, candle.Bar, "ShadowCandidate", new[] { "ShadowZoneInvalidated", pending.SourceSkipReason });
                _pendingShadowCandidates.RemoveAt(i);
                continue;
            }

            if (IsZoneTouched(candle, signal.Zone))
            {
                pending.Touched = true;
                AddStructureConfirmShadow(signal, candle.Bar + 12, $"ShadowAfterRetest:{pending.SourceSkipReason}", candle.Bar);
            }

            (bool Confirmed, string Reason) confirm = pending.Touched
                ? TryConfirm(signal, candle, _previousCandle)
                : (false, "WaitingShadowTouch");
            if (confirm.Confirmed)
            {
                var shadowSignal = signal with
                {
                    Time = candle.Time,
                    Bar = candle.Bar,
                    Stage = SignalStage.ResearchOnly,
                    SkipReasons = new[] { "ShadowCandidateResearch", pending.SourceSkipReason, confirm.Reason }
                };

                _researchLogger?.AppendSignal(shadowSignal);
                StartResearchTracking(shadowSignal, candle, "ShadowCandidate");
                _pendingShadowCandidates.RemoveAt(i);
                continue;
            }

            if (candle.Bar >= pending.MaxConfirmBar)
            {
                _researchLogger?.AppendNoTrade(_snapshot.SnapshotId, signal.SignalId, candle.Time, candle.Bar, "ShadowCandidate", new[] { "ShadowCandidateExpired", pending.SourceSkipReason, confirm.Reason });
                _pendingShadowCandidates.RemoveAt(i);
            }
        }
    }

    private void EvaluateObservationConfirms(OpfCandle candle)
    {
        if (_snapshot is null || _pendingObservationConfirms.Count == 0)
            return;

        for (var i = _pendingObservationConfirms.Count - 1; i >= 0; i--)
        {
            var pending = _pendingObservationConfirms[i];
            var signal = pending.Signal;
            if (signal.Zone is null || candle.Bar <= signal.Bar)
                continue;

            if (IsInvalidated(signal.Zone, candle))
            {
                _researchLogger?.AppendNoTrade(
                    _snapshot.SnapshotId,
                    signal.SignalId,
                    candle.Time,
                    candle.Bar,
                    "ObservationConfirm",
                    new[] { "ObservationInvalidated", pending.SourcePath, pending.SourceReason });
                TryStartFailureReverseResearch(signal, candle, pending.SourcePath, pending.SourceReason);
                _pendingObservationConfirms.RemoveAt(i);
                continue;
            }

            var confirm = TryConfirm(signal, candle, _previousCandle);
            _researchLogger?.AppendConfirmationEvaluation(
                _snapshot.SnapshotId,
                signal.SignalId,
                candle.Time,
                candle.Bar,
                candle,
                signal.Zone,
                _previousCandle,
                "ObservationConfirm",
                confirm.Confirmed ? "Confirmed" : "Waiting",
                confirm.Reason);

            if (confirm.Confirmed)
            {
                var confirmed = signal with
                {
                    SignalId = $"{signal.SignalId}-OC",
                    Time = candle.Time,
                    Bar = candle.Bar,
                    Stage = SignalStage.ResearchOnly,
                    SetupQualityScore = BuildSetupQualityScore(signal.RegimeScore, signal.Zone, "ObservationConfirm", signal.Side, candle),
                    SkipReasons = signal.SkipReasons.Concat(new[] { "ObservationConfirmResearch", pending.SourcePath, confirm.Reason }).ToArray()
                };

                _researchLogger?.AppendSignal(confirmed);
                StartResearchTracking(confirmed, candle, "ObservationConfirm");
                TryStartObservationConfirmStrict(confirmed, candle, pending.SourcePath, confirm.Reason);
                _pendingObservationConfirms.RemoveAt(i);
                continue;
            }

            if (candle.Bar >= pending.MaxConfirmBar)
            {
                _researchLogger?.AppendNoTrade(
                    _snapshot.SnapshotId,
                    signal.SignalId,
                    candle.Time,
                    candle.Bar,
                    "ObservationConfirm",
                    new[] { "ObservationConfirmExpired", pending.SourcePath, pending.SourceReason, confirm.Reason });
                _researchLogger?.AppendConfirmationEvaluation(
                    _snapshot.SnapshotId,
                    signal.SignalId,
                    candle.Time,
                    candle.Bar,
                    candle,
                    signal.Zone,
                    _previousCandle,
                    "ObservationConfirm",
                    "Expired",
                    "ObservationConfirmExpired");
                _pendingObservationConfirms.RemoveAt(i);
            }
        }
    }

    private void EvaluateStructureConfirmShadows(OpfCandle candle)
    {
        if (_snapshot is null || _pendingStructureConfirmShadows.Count == 0)
            return;

        for (var i = _pendingStructureConfirmShadows.Count - 1; i >= 0; i--)
        {
            var pending = _pendingStructureConfirmShadows[i];
            var signal = pending.Signal;
            if (signal.Zone is null || candle.Bar <= signal.Bar)
                continue;
            if (pending.RetestBar.HasValue && candle.Bar <= pending.RetestBar.Value)
                continue;

            if (IsInvalidated(signal.Zone, candle))
            {
                _researchLogger?.AppendNoTrade(_snapshot.SnapshotId, signal.SignalId, candle.Time, candle.Bar, "StructureConfirmShadow", new[] { "StructureConfirmShadowInvalidated", pending.SourceReason });
                _pendingStructureConfirmShadows.RemoveAt(i);
                continue;
            }

            var confirm = TryConfirmMicroBos3(signal.Side, candle);
            if (confirm.Confirmed)
            {
                var structureSignal = signal with
                {
                    SignalId = $"{signal.SignalId}-SCB",
                    Time = candle.Time,
                    Bar = candle.Bar,
                    Stage = SignalStage.ResearchOnly,
                    SetupQualityScore = BuildSetupQualityScore(signal.RegimeScore, signal.Zone, "StructureConfirm", signal.Side, candle),
                    SkipReasons = new[] { "StructureConfirmShadowResearch", pending.SourceReason, confirm.Reason }
                };

                _researchLogger?.AppendSignal(structureSignal);
                StartResearchTracking(structureSignal, candle, "StructureConfirmShadow");
                TryStartStructureSwingStopResearch(structureSignal, candle);
                TryStartConfirmBarStopResearch(structureSignal, candle);
                TryStartConfirmBarMinRiskStopResearch(structureSignal, candle);
                TryStartConfirmBarWait1Research(structureSignal, candle);
                _pendingStructureConfirmShadows.RemoveAt(i);
                continue;
            }

            if (candle.Bar >= pending.MaxConfirmBar)
            {
                _researchLogger?.AppendNoTrade(_snapshot.SnapshotId, signal.SignalId, candle.Time, candle.Bar, "StructureConfirmShadow", new[] { "StructureConfirmShadowExpired", pending.SourceReason, confirm.Reason });
                _pendingStructureConfirmShadows.RemoveAt(i);
            }
        }
    }

    private void TryStartObservationConfirmStrict(CandidateSignal signal, OpfCandle candle, string sourcePath, string confirmReason)
    {
        if (_snapshot is null)
            return;

        var zoneQualityScore = signal.Zone is null
            ? ScoreBreakdown.Empty("ZoneQualityScore", ZoneQualityThreshold)
            : BuildZoneQualityScore(signal.Zone);
        _researchLogger?.AppendScoreBreakdown(
            _snapshot.SnapshotId,
            candle.Time,
            candle.Bar,
            signal.Side.ToString().ToUpperInvariant(),
            zoneQualityScore);

        var qualityReason = ObservationConfirmStrictRejectReason(signal, candle);
        if (qualityReason is not null)
        {
            _researchLogger?.AppendNoTrade(
                _snapshot.SnapshotId,
                signal.SignalId,
                candle.Time,
                candle.Bar,
                "ObservationConfirm_Strict",
                new[] { qualityReason, sourcePath, confirmReason });
            return;
        }

        var strictPath = IsBullFreshStrictZone(signal.Zone)
            ? "ObservationStrict_BullFresh"
            : "ObservationStrict_Other";
        var strictSignal = signal with
        {
            SignalId = $"{signal.SignalId}-STR",
            SetupQualityScore = BuildSetupQualityScore(signal.RegimeScore, signal.Zone, "ObservationConfirm", signal.Side, candle),
            SkipReasons = signal.SkipReasons.Concat(new[] { "ObservationConfirmStrictResearch", strictPath, sourcePath, confirmReason }).ToArray()
        };

        _researchLogger?.AppendSignal(strictSignal);
        StartResearchTracking(strictSignal, candle, strictPath);
    }

    private void TryStartFailureReverseResearch(CandidateSignal sourceSignal, OpfCandle candle, string sourcePath, string sourceReason)
    {
        if (_snapshot is null || sourceSignal.Zone is null)
            return;

        if (!_failureReverseSignalIds.Add(sourceSignal.SignalId))
            return;

        var side = sourceSignal.Side == TradeSide.Long ? TradeSide.Short : TradeSide.Long;
        var failureSignal = sourceSignal with
        {
            SignalId = $"{sourceSignal.SignalId}-FR",
            Time = candle.Time,
            Bar = candle.Bar,
            Side = side,
            SetupType = SetupType.FailureReverse,
            Stage = SignalStage.ResearchOnly,
            SetupQualityScore = BuildSetupQualityScore(sourceSignal.RegimeScore, sourceSignal.Zone, "FailureInvalidation", side, candle),
            SkipReasons = new[]
            {
                "FailureReverseResearch",
                $"FailureSourceSignalID={sourceSignal.SignalId}",
                $"FailureSource={sourceSignal.Zone.ZoneType}",
                $"FailureSourceZoneFreshness={sourceSignal.Zone.Freshness}",
                sourcePath,
                sourceReason
            }
        };

        _researchLogger?.AppendSignal(failureSignal);
        StartResearchTracking(failureSignal, candle, "FailureReverse_ObservationInvalidated");
        if (IsQualifiedFailureReverseLong(failureSignal, candle))
        {
            var risk = Math.Abs(candle.Close - (failureSignal.Zone.Low - 0.50m));
            var heat = candle.Close - candle.Low;
            var heatR = risk <= 0m ? 0m : Math.Round(heat / risk, 2);
            var qualified = failureSignal with
            {
                SignalId = $"{failureSignal.SignalId}-LQ",
                SkipReasons = failureSignal.SkipReasons.Concat(new[]
                {
                    "FailureReverseLongQualified",
                    $"Risk={risk:0.##}",
                    $"HeatR={heatR:0.##}"
                }).ToArray()
            };
            _researchLogger?.AppendSignal(qualified);
            StartResearchTracking(qualified, candle, "FailureReverse_LongQualified");
        }
        _pendingFailureReverseRetests.Add(new PendingFailureReverseRetest(failureSignal, candle.Bar + 6));
    }

    private void EvaluateFailureReverseRetests(OpfCandle candle)
    {
        if (_snapshot is null || _pendingFailureReverseRetests.Count == 0)
            return;

        for (var i = _pendingFailureReverseRetests.Count - 1; i >= 0; i--)
        {
            var pending = _pendingFailureReverseRetests[i];
            var signal = pending.Signal;
            if (signal.Zone is null || candle.Bar <= signal.Bar)
                continue;

            if (!pending.Touched)
            {
                if (IsZoneTouched(candle, signal.Zone))
                {
                    pending.Touched = true;
                    continue;
                }

                if (candle.Bar >= pending.MaxRetestBar)
                {
                    _researchLogger?.AppendNoTrade(_snapshot.SnapshotId, signal.SignalId, candle.Time, candle.Bar, "FailureReverse_RetestFailed", new[] { "FailureRetestExpired" });
                    _pendingFailureReverseRetests.RemoveAt(i);
                }

                continue;
            }

            var confirm = TryConfirm(signal, candle, _previousCandle);
            if (confirm.Confirmed)
            {
                var retestSignal = signal with
                {
                    SignalId = $"{signal.SignalId}-RT",
                    Time = candle.Time,
                    Bar = candle.Bar,
                    Stage = SignalStage.Triggered,
                    SetupQualityScore = BuildSetupQualityScore(signal.RegimeScore, signal.Zone, "FailureRetest", signal.Side, candle),
                    SkipReasons = signal.SkipReasons.Concat(new[] { "FailureRetestFailedTriggered", confirm.Reason }).ToArray()
                };

                _researchLogger?.AppendSignal(retestSignal);
                StartResearchTracking(retestSignal, candle, "FailureReverse_RetestFailed");
                _pendingFailureReverseRetests.RemoveAt(i);
                continue;
            }

            if (candle.Bar >= pending.MaxRetestBar)
            {
                _researchLogger?.AppendNoTrade(_snapshot.SnapshotId, signal.SignalId, candle.Time, candle.Bar, "FailureReverse_RetestFailed", new[] { "FailureRetestNoConfirm", confirm.Reason });
                _pendingFailureReverseRetests.RemoveAt(i);
            }
        }
    }

    private static ScoreBreakdown BuildZoneQualityScore(DetectedZone zone)
    {
        var components = new[]
        {
            ZoneFreshnessComponent(zone),
            ZoneTouchCountComponent(zone),
            ZoneMitigationComponent(zone),
            ZoneTypeComponent(zone)
        };
        var total = components.Sum(x => x.Contribution);
        return new ScoreBreakdown("ZoneQualityScore", total, ZoneQualityThreshold, total >= ZoneQualityThreshold, components);
    }

    private static ScoreBreakdown BuildSetupQualityScore(ScoreBreakdown regimeScore, DetectedZone? zone, string trigger, TradeSide side, OpfCandle candle)
    {
        var zoneScore = zone is null ? ScoreBreakdown.Empty("ZoneQualityScore", ZoneQualityThreshold) : BuildZoneQualityScore(zone);
        var components = new[]
        {
            SetupRegimeComponent(regimeScore),
            SetupTriggerComponent(trigger),
            SetupZoneComponent(zoneScore),
            SetupOrderFlowProxyComponent(side, candle)
        };
        var total = components.Sum(x => x.Contribution);
        return new ScoreBreakdown("SetupQualityScore", total, SetupQualityThreshold, total >= SetupQualityThreshold, components);
    }

    private static ScoreComponent SetupRegimeComponent(ScoreBreakdown regimeScore)
    {
        var passed = regimeScore.Passed;
        var contribution = passed ? 40m : regimeScore.TotalScore >= 50m ? 20m : 0m;
        return new ScoreComponent(
            "Regime",
            regimeScore.TotalScore.ToString("0.##"),
            passed,
            40m,
            contribution,
            passed ? "TrendRegime" : "NotTrendRegime");
    }

    private static ScoreComponent SetupTriggerComponent(string trigger)
    {
        var contribution = trigger switch
        {
            "Confirmed" => 30m,
            "ObservationConfirm" => 30m,
            "FailureRetest" => 30m,
            "BreakawayRetest" => 30m,
            "FailureInvalidation" => 20m,
            "Breakaway" => 25m,
            "StructureConfirm" => 25m,
            "CandidateTouch" => 10m,
            _ => 15m
        };
        return new ScoreComponent(
            "Trigger",
            trigger,
            contribution >= 25m,
            30m,
            contribution,
            trigger);
    }

    private static ScoreComponent SetupZoneComponent(ScoreBreakdown zoneScore)
    {
        var contribution = Math.Round(Math.Min(100m, Math.Max(0m, zoneScore.TotalScore)) * 0.20m, 2);
        return new ScoreComponent(
            "Zone",
            zoneScore.TotalScore.ToString("0.##"),
            zoneScore.Passed,
            20m,
            contribution,
            zoneScore.Passed ? "ZoneQualityPassed" : "ZoneQualityBelowThreshold");
    }

    private static ScoreComponent SetupOrderFlowProxyComponent(TradeSide side, OpfCandle candle)
    {
        var range = candle.High - candle.Low;
        if (range <= 0m)
            return new ScoreComponent("OrderFlow", "FlatBar", false, 10m, 3m, "PriceFlowProxyNeutral");

        var body = candle.Close - candle.Open;
        var bodyRatio = Math.Abs(body) / range;
        var closeLocation = (candle.Close - candle.Low) / range;
        var alignedBody = side == TradeSide.Long ? body > 0m : body < 0m;
        var alignedClose = side == TradeSide.Long ? closeLocation >= 0.60m : closeLocation <= 0.40m;
        var stronglyOpposed = side == TradeSide.Long
            ? body < 0m && closeLocation <= 0.40m && bodyRatio >= 0.35m
            : body > 0m && closeLocation >= 0.60m && bodyRatio >= 0.35m;

        var contribution = stronglyOpposed ? 0m : alignedBody && alignedClose ? 10m : alignedBody || alignedClose ? 6m : 3m;
        var passed = contribution >= 6m;
        var raw = $"body={body:0.##};closeLoc={closeLocation:0.##}";
        var reason = stronglyOpposed ? "PriceFlowProxyOpposed" : passed ? "PriceFlowProxyAligned" : "PriceFlowProxyMixed";
        return new ScoreComponent("OrderFlow", raw, passed, 10m, contribution, reason);
    }

    private static ScoreComponent ZoneFreshnessComponent(DetectedZone zone)
    {
        return zone.Freshness switch
        {
            "Fresh" => new ScoreComponent("ZoneFreshness", zone.Freshness, true, 40m, 40m, "Fresh"),
            "SecondTouch" => new ScoreComponent("ZoneFreshness", zone.Freshness, false, 40m, 20m, "SecondTouch"),
            _ => new ScoreComponent("ZoneFreshness", zone.Freshness, false, 40m, 0m, "Stale")
        };
    }

    private static ScoreComponent ZoneTouchCountComponent(DetectedZone zone)
    {
        var passed = zone.TouchCount <= 1;
        return new ScoreComponent(
            "TouchCount",
            zone.TouchCount.ToString(),
            passed,
            20m,
            passed ? 20m : 0m,
            passed ? "FirstTouch" : "MultipleTouches");
    }

    private static ScoreComponent ZoneMitigationComponent(DetectedZone zone)
    {
        return new ScoreComponent(
            "Mitigated",
            zone.Mitigated.ToString(),
            !zone.Mitigated,
            20m,
            zone.Mitigated ? 0m : 20m,
            zone.Mitigated ? "Mitigated" : "Unmitigated");
    }

    private static ScoreComponent ZoneTypeComponent(DetectedZone zone)
    {
        var passed = zone.ZoneType is "BullFVG" or "BearFVG";
        return new ScoreComponent(
            "ZoneType",
            zone.ZoneType,
            passed,
            20m,
            passed ? 20m : 0m,
            passed ? "FVG" : "OtherZone");
    }

    private static bool IsBullFreshStrictZone(DetectedZone? zone)
    {
        return zone is not null
            && zone.Direction == "Bull"
            && zone.Freshness == "Fresh"
            && !zone.Mitigated;
    }

    private static string? ObservationConfirmStrictRejectReason(CandidateSignal signal, OpfCandle candle)
    {
        if (signal.Zone is null)
            return "ObservationStrictNoZone";

        var entry = candle.Close;
        var stop = signal.Side == TradeSide.Long
            ? signal.Zone.Low - 0.50m
            : signal.Zone.High + 0.50m;
        var risk = Math.Abs(entry - stop);
        if (risk <= 0m)
            return "ObservationStrictInvalidRisk";

        var confirmDistance = signal.Side == TradeSide.Long
            ? candle.Close - signal.Zone.High
            : signal.Zone.Low - candle.Close;
        if (confirmDistance < 0.25m * risk)
            return "ObservationStrictConfirmTooClose";

        var heat = signal.Side == TradeSide.Long
            ? entry - candle.Low
            : candle.High - entry;
        if (heat > 0.50m * risk)
            return "ObservationStrictConfirmHeatTooHigh";

        return null;
    }

    private void EvaluatePendingCandidates(OpfCandle candle)
    {
        if (_snapshot is null || _pendingCandidates.Count == 0)
            return;

        for (var i = _pendingCandidates.Count - 1; i >= 0; i--)
        {
            var pending = _pendingCandidates[i];
            if (candle.Bar <= pending.Signal.Bar)
                continue;

            var confirm = TryConfirm(pending.Signal, candle, _previousCandle);
            if (confirm.Confirmed)
            {
                var confirmed = pending.Signal with
                {
                    Time = candle.Time,
                    Bar = candle.Bar,
                    Stage = SignalStage.Confirmed,
                    SetupQualityScore = BuildSetupQualityScore(pending.Signal.RegimeScore, pending.Signal.Zone, "Confirmed", pending.Signal.Side, candle),
                    SkipReasons = new[] { "TrendPullbackConfirmed", confirm.Reason }
                };
                _researchLogger?.AppendSignal(confirmed);
                _researchLogger?.AppendConfirmationEvaluation(_snapshot.SnapshotId, pending.Signal.SignalId, candle.Time, candle.Bar, candle, pending.Signal.Zone, _previousCandle, "Confirmed", "Confirmed", confirm.Reason);
                _pendingConfirmedRetraces.Add(new PendingConfirmedRetrace(confirmed, candle, candle.Bar + 2));
                _pendingCandidates.RemoveAt(i);
                _confirmedCount++;
                continue;
            }

            _researchLogger?.AppendConfirmationEvaluation(_snapshot.SnapshotId, pending.Signal.SignalId, candle.Time, candle.Bar, candle, pending.Signal.Zone, _previousCandle, "Candidate", "Waiting", confirm.Reason);

            if (candle.Bar >= pending.MaxConfirmBar)
            {
                _researchLogger?.AppendNoTrade(_snapshot.SnapshotId, pending.Signal.SignalId, candle.Time, candle.Bar, "Candidate", new[] { "NoSecondConfirmExpired" });
                _researchLogger?.AppendConfirmationEvaluation(_snapshot.SnapshotId, pending.Signal.SignalId, candle.Time, candle.Bar, candle, pending.Signal.Zone, _previousCandle, "Skipped", "Expired", "NoSecondConfirmExpired");
                _pendingCandidates.RemoveAt(i);
            }
        }
    }

    private void EvaluateConfirmedRetraces(OpfCandle candle)
    {
        if (_snapshot is null || _pendingConfirmedRetraces.Count == 0)
            return;

        for (var i = _pendingConfirmedRetraces.Count - 1; i >= 0; i--)
        {
            var pending = _pendingConfirmedRetraces[i];
            if (candle.Bar <= pending.ConfirmCandle.Bar)
                continue;

            if (pending.Signal.Zone is null)
            {
                _researchLogger?.AppendNoTrade(_snapshot.SnapshotId, pending.Signal.SignalId, candle.Time, candle.Bar, "TrendPullbackConfirmed", new[] { "NoRetraceNoZone" });
                _pendingConfirmedRetraces.RemoveAt(i);
                continue;
            }

            if (IsInvalidated(pending.Signal.Zone, candle))
            {
                _researchLogger?.AppendNoTrade(_snapshot.SnapshotId, pending.Signal.SignalId, candle.Time, candle.Bar, "TrendPullbackConfirmed", new[] { "RetraceInvalidatedAfterConfirm" });
                _pendingConfirmedRetraces.RemoveAt(i);
                continue;
            }

            if (HasConservativeRetrace(pending.Signal.Side, pending.Signal.Zone, candle))
            {
                var triggered = pending.Signal with
                {
                    SignalId = $"{pending.Signal.SignalId}-TRG",
                    Time = candle.Time,
                    Bar = candle.Bar,
                    Stage = SignalStage.Triggered,
                    SkipReasons = pending.Signal.SkipReasons.Concat(new[] { "TriggeredAfterConfirmRetrace", $"ConfirmBar={pending.ConfirmCandle.Bar}" }).ToArray()
                };
                _researchLogger?.AppendSignal(triggered);
                StartResearchTracking(triggered, candle, "TrendPullbackConfirmed");
                _pendingConfirmedRetraces.RemoveAt(i);
                continue;
            }

            _researchLogger?.AppendConfirmationEvaluation(_snapshot.SnapshotId, pending.Signal.SignalId, candle.Time, candle.Bar, candle, pending.Signal.Zone, _previousCandle, "Confirmed", "WaitingRetrace", "WaitingConservativeRetrace");

            if (candle.Bar >= pending.MaxRetraceBar)
            {
                _researchLogger?.AppendNoTrade(_snapshot.SnapshotId, pending.Signal.SignalId, candle.Time, candle.Bar, "TrendPullbackConfirmed", new[] { "NoRetraceAfterConfirm" });
                _researchLogger?.AppendConfirmationEvaluation(_snapshot.SnapshotId, pending.Signal.SignalId, candle.Time, candle.Bar, candle, pending.Signal.Zone, _previousCandle, "Skipped", "Expired", "NoRetraceAfterConfirm");
                _pendingConfirmedRetraces.RemoveAt(i);
            }
        }
    }

    private void EvaluateConfirmBarWait1s(OpfCandle candle)
    {
        if (_snapshot is null || _pendingConfirmBarWait1s.Count == 0)
            return;

        for (var i = _pendingConfirmBarWait1s.Count - 1; i >= 0; i--)
        {
            var pending = _pendingConfirmBarWait1s[i];
            if (candle.Bar <= pending.Signal.Bar)
                continue;

            var rejectReasons = ConfirmBarWait1RejectReasons(pending.Signal.Side, candle, pending.ConfirmCandle);
            if (rejectReasons.Count > 0)
            {
                _researchLogger?.AppendNoTrade(_snapshot.SnapshotId, pending.Signal.SignalId, candle.Time, candle.Bar, "StructureConfirmShadow_ConfirmBarStop_Wait1", rejectReasons);
                _pendingConfirmBarWait1s.RemoveAt(i);
                continue;
            }

            var risk = Math.Abs(candle.Close - pending.Stop);
            if (risk > MaxConfirmBarRiskPoints)
            {
                _researchLogger?.AppendNoTrade(_snapshot.SnapshotId, pending.Signal.SignalId, candle.Time, candle.Bar, "StructureConfirmShadow_ConfirmBarStop_Wait1", new[] { "ConfirmBarWait1RiskTooWide", $"Risk={risk:0.00}", $"Max={MaxConfirmBarRiskPoints:0.00}" });
                _pendingConfirmBarWait1s.RemoveAt(i);
                continue;
            }

            var waitSignal = pending.Signal with
            {
                SignalId = $"{pending.Signal.SignalId}-W1",
                Time = candle.Time,
                Bar = candle.Bar,
                SkipReasons = pending.Signal.SkipReasons.Concat(new[] { "ConfirmBarStopWait1Research", "Wait1HeldConfirmBar" }).ToArray()
            };

            _researchLogger?.AppendSignal(waitSignal);
            StartResearchTracking(waitSignal, candle, "StructureConfirmShadow_ConfirmBarStop_Wait1", pending.Stop);
            _pendingConfirmBarWait1s.RemoveAt(i);
        }
    }

    private static (bool Confirmed, string Reason) TryConfirm(CandidateSignal signal, OpfCandle candle, OpfCandle? previous)
    {
        if (signal.Zone is null)
            return (false, "NoZone");

        if (signal.Side == TradeSide.Long)
        {
            if (candle.Close > signal.Zone.High)
                return (true, "ReclaimZoneHigh");
            if (previous is not null && candle.Close > previous.High)
                return (true, "BreakPrevHigh");
            return (false, "WaitingReclaimOrBreakPrevHigh");
        }

        if (candle.Close < signal.Zone.Low)
            return (true, "ReclaimZoneLow");
        if (previous is not null && candle.Close < previous.Low)
            return (true, "BreakPrevLow");
        return (false, "WaitingReclaimOrBreakPrevLow");
    }

    private static bool HasConservativeRetrace(TradeSide side, DetectedZone zone, OpfCandle candle)
    {
        return side == TradeSide.Long
            ? candle.Low <= zone.High && candle.Close > zone.Low
            : candle.High >= zone.Low && candle.Close < zone.High;
    }

    private void AddStructureConfirmShadow(CandidateSignal signal, int maxConfirmBar, string sourceReason, int? retestBar = null)
    {
        if (!_structureConfirmShadowIds.Add($"{signal.SignalId}:{sourceReason}"))
            return;

        _pendingStructureConfirmShadows.Add(new PendingStructureConfirmShadow(signal, maxConfirmBar, sourceReason, retestBar));
    }

    private (bool Confirmed, string Reason) TryConfirmMicroBos3(TradeSide side, OpfCandle candle)
    {
        var previousThree = _recentCandles.TakeLast(3).ToArray();
        if (previousThree.Length < 3)
            return (false, "WaitingMicroBos3History");

        if (side == TradeSide.Long)
        {
            var high = previousThree.Max(x => x.High);
            return candle.Close > high
                ? (true, "MicroBos3BreakHigh")
                : (false, "MicroBos3Failed");
        }

        var low = previousThree.Min(x => x.Low);
        return candle.Close < low
            ? (true, "MicroBos3BreakLow")
            : (false, "MicroBos3Failed");
    }

    private void AddRecentCandle(OpfCandle candle)
    {
        _recentCandles.Add(candle);
        if (_recentCandles.Count > 20)
            _recentCandles.RemoveAt(0);
    }

    private void StartResearchTracking(CandidateSignal signal, OpfCandle entryCandle, string researchPath)
    {
        if (signal.Zone is null)
            return;

        var entry = entryCandle.Close;
        var buffer = 0.50m;
        var stop = signal.Side == TradeSide.Long
            ? signal.Zone.Low - buffer
            : signal.Zone.High + buffer;
        StartResearchTracking(signal, entryCandle, researchPath, stop);
    }

    private void StartResearchTracking(CandidateSignal signal, OpfCandle entryCandle, string researchPath, decimal stop)
    {
        var entry = entryCandle.Close;
        var risk = Math.Abs(entry - stop);
        if (risk <= 0m)
            return;

        AddResearchTracker(signal, entryCandle, researchPath, stop, risk);

        if (ActualEnableWideStopExecution && ShouldTrackWideStopVariant(researchPath))
        {
            var wideMultiplier = ActualWideStopMultiplier <= 1m ? 1.5m : ActualWideStopMultiplier;
            var wideRisk = Math.Round(risk * wideMultiplier, 2);
            var wideStop = signal.Side == TradeSide.Long
                ? entry - wideRisk
                : entry + wideRisk;
            AddResearchTracker(signal, entryCandle, $"{researchPath}_WideStop{wideMultiplier:0.#}R".Replace(".", "_"), wideStop, wideRisk);
        }
    }

    private void AddResearchTracker(CandidateSignal signal, OpfCandle entryCandle, string researchPath, decimal stop, decimal risk)
    {
        var entry = entryCandle.Close;
        var reward = EstimateReward(signal, researchPath, entry, risk);
        var estimatedRr = reward.Points <= 0m ? 0m : Math.Round(reward.Points / risk, 4);
        var atr14 = CalculateAtr14(entryCandle);
        var maxAllowedRisk = MaxAllowedRiskPoints(_snapshot?.InstrumentProfile, atr14);
        _researchLogger?.AppendScoreBreakdown(
            _snapshot?.SnapshotId ?? signal.SnapshotId,
            entryCandle.Time,
            entryCandle.Bar,
            signal.Side.ToString().ToUpperInvariant(),
            signal.SetupQualityScore);
        _researchLogger?.AppendRiskEvaluation(
            _snapshot?.SnapshotId ?? signal.SnapshotId,
            signal.SignalId,
            entryCandle.Time,
            entryCandle.Bar,
            signal.Side.ToString(),
            signal.SetupType.ToString(),
            researchPath,
            entry,
            stop,
            risk,
            _snapshot?.InstrumentProfile.MaxRiskPointsHard ?? 0m,
            atr14,
            _snapshot?.InstrumentProfile.AtrRiskMultiplier ?? 0m,
            maxAllowedRisk,
            reward.Points,
            reward.Model,
            estimatedRr,
            ActualMinEstimatedRr > 0m ? ActualMinEstimatedRr : _snapshot?.InstrumentProfile.MinEstimatedRr ?? 0m);
        _researchLogger?.AppendEdgeAttribution(
            signal,
            entryCandle.Time,
            entryCandle.Bar,
            researchPath,
            "Tracked",
            entry,
            stop,
            risk,
            reward.Points,
            reward.Model,
            estimatedRr,
            string.Empty,
            string.Empty,
            false,
            string.Empty,
            0m,
            0m);

        _researchTrackers.Add(new ResearchTracker(
            signal,
            entryCandle.Time,
            entryCandle.Bar,
            entry,
            stop,
            risk,
            maxBars: 12,
            researchPath));

        TrySubmitReplayExecution(signal, entryCandle, researchPath, stop, risk);
    }

    private void TrySubmitReplayExecution(CandidateSignal signal, OpfCandle entryCandle, string researchPath, decimal stop, decimal risk)
    {
        if (!EnableReplayOrders || _snapshot is null)
            return;
        if (_isStoppingActualExecution)
        {
            AppendExecutionDecision(signal, entryCandle, "Skip", "StrategyStopping", researchPath, stop, risk);
            AppendExecutionEvent(signal, string.Empty, entryCandle, "SKIP_STRATEGY_STOPPING", "-", researchPath, entryCandle.Close, 0m, "StrategyStopping");
            return;
        }
        if (IsReplayStopGuardWindow(entryCandle.Time))
        {
            AppendExecutionDecision(signal, entryCandle, "Skip", "ReplayStopGuard", researchPath, stop, risk);
            AppendExecutionEvent(signal, string.Empty, entryCandle, "SKIP_REPLAY_STOP_GUARD", "-", researchPath, entryCandle.Close, 0m, "ReplayStopGuard");
            return;
        }
        ResetReplayExecutionDailyCounter(entryCandle.Time.Date);
        if (IsDailyAbnormalFillGuardActive(entryCandle.Time.Date))
        {
            var reason = $"DailyAbnormalFillGuard:count={_dailyAbnormalFillGuardCount}";
            AppendExecutionDecision(signal, entryCandle, "Skip", reason, researchPath, stop, risk);
            AppendExecutionEvent(signal, string.Empty, entryCandle, "SKIP_DAILY_ABNORMAL_FILL_GUARD", "-", researchPath, entryCandle.Close, 0m, reason);
            return;
        }
        if (!IsReplayExecutionPathEnabled(researchPath))
        {
            AppendExecutionDecision(signal, entryCandle, "Skip", "PathDisabled", researchPath, stop, risk);
            return;
        }
        if (!ReplayAllowResearchPaths && !IsExecutionEligiblePath(researchPath))
        {
            AppendExecutionDecision(signal, entryCandle, "Skip", "ResearchOnlyPath", researchPath, stop, risk);
            AppendExecutionEvent(signal, string.Empty, entryCandle, "SKIP_RESEARCH_ONLY_PATH", "-", researchPath, entryCandle.Close, 0m, "pathNotExecutionEligible");
            return;
        }
        var strategySkipReasons = ActualExecutionStrategySkipReasons(signal, researchPath);
        if (strategySkipReasons.Length > 0)
        {
            AppendExecutionDecision(signal, entryCandle, "Skip", string.Join("|", strategySkipReasons), researchPath, stop, risk);
            AppendExecutionEvent(signal, string.Empty, entryCandle, "SKIP_STRATEGY_QUALITY", "-", researchPath, entryCandle.Close, 0m, string.Join("|", strategySkipReasons));
            return;
        }
        var pathSkipReasons = ActualExecutionPathSkipReasons(signal, researchPath);
        if (pathSkipReasons.Length > 0)
        {
            AppendExecutionDecision(signal, entryCandle, "Skip", string.Join("|", pathSkipReasons), researchPath, stop, risk);
            AppendExecutionEvent(signal, string.Empty, entryCandle, "SKIP_EXECUTION_PATH_RULE", "-", researchPath, entryCandle.Close, 0m, string.Join("|", pathSkipReasons));
            return;
        }
        var skipReasons = ActualExecutionRiskSkipReasons(signal, researchPath, entryCandle, risk);
        if (skipReasons.Length > 0)
        {
            AppendExecutionDecision(signal, entryCandle, "Skip", string.Join("|", skipReasons), researchPath, stop, risk);
            AppendExecutionEvent(signal, string.Empty, entryCandle, "SKIP_RISK_RR", "-", researchPath, entryCandle.Close, 0m, string.Join("|", skipReasons));
            return;
        }
        var sameBarSkipReasons = ActualExecutionSameBarSkipReasons(signal, researchPath, entryCandle, stop, risk);
        var allowSameBarTargetOnly = IsDailyVolumeSameBarTargetOnlyAllowed(signal, researchPath, sameBarSkipReasons);
        if (sameBarSkipReasons.Length > 0 && !allowSameBarTargetOnly)
        {
            AppendExecutionDecision(signal, entryCandle, "Skip", string.Join("|", sameBarSkipReasons), researchPath, stop, risk);
            AppendExecutionEvent(signal, string.Empty, entryCandle, "SKIP_SAME_BAR_AMBIGUOUS", "-", researchPath, entryCandle.Close, 0m, string.Join("|", sameBarSkipReasons));
            return;
        }
        if (ReplayMaxTradesPerDay > 0 && _replayTradesToday >= ReplayMaxTradesPerDay)
        {
            AppendExecutionDecision(signal, entryCandle, "Skip", $"DailyTradeLimit:max={ReplayMaxTradesPerDay}", researchPath, stop, risk);
            AppendExecutionEvent(signal, string.Empty, entryCandle, "SKIP_DAILY_LIMIT", "-", researchPath, entryCandle.Close, 0m, $"max={ReplayMaxTradesPerDay}");
            return;
        }
        var executionProfile = _snapshot.ExecutionProfile;
        if (executionProfile.StopAfterDailyTarget && executionProfile.DailyTargetDollars > 0m && _replayDailyPnlDollars >= executionProfile.DailyTargetDollars)
        {
            AppendExecutionDecision(signal, entryCandle, "Skip", $"DailyTarget:pnl={_replayDailyPnlDollars:0.##},target={executionProfile.DailyTargetDollars:0.##}", researchPath, stop, risk);
            AppendExecutionEvent(signal, string.Empty, entryCandle, "SKIP_DAILY_TARGET", "-", researchPath, entryCandle.Close, 0m, $"pnl={_replayDailyPnlDollars:0.##}|target={executionProfile.DailyTargetDollars:0.##}");
            return;
        }
        if (executionProfile.DailyLossLimitDollars > 0m && _replayDailyPnlDollars <= -executionProfile.DailyLossLimitDollars)
        {
            AppendExecutionDecision(signal, entryCandle, "Skip", $"DailyLoss:pnl={_replayDailyPnlDollars:0.##},lossLimit={executionProfile.DailyLossLimitDollars:0.##}", researchPath, stop, risk);
            AppendExecutionEvent(signal, string.Empty, entryCandle, "SKIP_DAILY_LOSS", "-", researchPath, entryCandle.Close, 0m, $"pnl={_replayDailyPnlDollars:0.##}|lossLimit={executionProfile.DailyLossLimitDollars:0.##}");
            return;
        }
        if (ReplayUseFullLossGuard && executionProfile.MaxFullLossTradesPerDay > 0 && _replayFullLossTradesToday >= executionProfile.MaxFullLossTradesPerDay)
        {
            AppendExecutionDecision(signal, entryCandle, "Skip", $"FullLossLimit:fullLosses={_replayFullLossTradesToday},max={executionProfile.MaxFullLossTradesPerDay}", researchPath, stop, risk);
            AppendExecutionEvent(signal, string.Empty, entryCandle, "SKIP_DAILY_FULL_LOSS_LIMIT", "-", researchPath, entryCandle.Close, 0m, $"fullLosses={_replayFullLossTradesToday}|max={executionProfile.MaxFullLossTradesPerDay}");
            return;
        }
        if (ReplayUseConsecutiveLossGuard && executionProfile.MaxConsecutiveLossesPerDay > 0 && _replayConsecutiveLossesToday >= executionProfile.MaxConsecutiveLossesPerDay)
        {
            AppendExecutionDecision(signal, entryCandle, "Skip", $"ConsecutiveLossLimit:consecLosses={_replayConsecutiveLossesToday},max={executionProfile.MaxConsecutiveLossesPerDay}", researchPath, stop, risk);
            AppendExecutionEvent(signal, string.Empty, entryCandle, "SKIP_DAILY_CONSEC_LOSS_LIMIT", "-", researchPath, entryCandle.Close, 0m, $"consecLosses={_replayConsecutiveLossesToday}|max={executionProfile.MaxConsecutiveLossesPerDay}");
            return;
        }
        var activeExecution = _replayExecution;
        if (activeExecution is not null && IsReplayExecutionActive(activeExecution))
        {
            _researchLogger?.AppendInfo(_snapshot.SnapshotId, entryCandle.Bar, entryCandle.Time, $"EXEC_SKIP_ACTIVE signal={signal.SignalId} active={activeExecution.TradeId}");
            AppendExecutionDecision(signal, entryCandle, "Skip", $"ActiveTrade:{activeExecution.TradeId}", researchPath, stop, risk);
            AppendExecutionEvent(signal, activeExecution.TradeId, entryCandle, "SKIP_ACTIVE", "-", researchPath, entryCandle.Close, 0m, activeExecution.TradeId);
            return;
        }
        var currentPosition = CurrentPosition;
        if (currentPosition != 0m)
        {
            var reason = $"OrphanPosition:pos={currentPosition:0.########}";
            _researchLogger?.AppendInfo(_snapshot.SnapshotId, entryCandle.Bar, entryCandle.Time, $"EXEC_SKIP_ORPHAN_POSITION signal={signal.SignalId} pos={currentPosition:0.########}");
            AppendExecutionDecision(signal, entryCandle, "Skip", reason, researchPath, stop, risk);
            AppendExecutionEvent(signal, string.Empty, entryCandle, "SKIP_ORPHAN_POSITION", "-", researchPath, entryCandle.Close, Math.Abs(currentPosition), reason);
            return;
        }
        if (Portfolio is null || Security is null)
        {
            _researchLogger?.AppendInfo(_snapshot.SnapshotId, entryCandle.Bar, entryCandle.Time, "EXEC_SKIP_CONTEXT portfolioOrSecurityNull");
            AppendExecutionDecision(signal, entryCandle, "Skip", "ContextMissing:portfolioOrSecurityNull", researchPath, stop, risk);
            AppendExecutionEvent(signal, string.Empty, entryCandle, "SKIP_CONTEXT", "-", researchPath, entryCandle.Close, 0m, "portfolioOrSecurityNull");
            return;
        }

        var qty = Math.Max(0m, ReplayOrderQuantity);
        if (qty <= 0m)
        {
            _researchLogger?.AppendInfo(_snapshot.SnapshotId, entryCandle.Bar, entryCandle.Time, "EXEC_SKIP_QTY quantity<=0");
            AppendExecutionDecision(signal, entryCandle, "Skip", "QuantityInvalid", researchPath, stop, risk);
            AppendExecutionEvent(signal, string.Empty, entryCandle, "SKIP_QTY", "-", researchPath, entryCandle.Close, qty, "quantity<=0");
            return;
        }

        var entry = entryCandle.Close;
        var targetR = ActualTargetRFor(signal, researchPath, risk);
        var target = TargetFromRisk(signal.Side, entry, risk, targetR);
        var tradeId = $"{entryCandle.Time:yyyyMMdd-HHmm}-{signal.Side}-{entryCandle.Bar}";
        var maxAllowedRiskPoints = MaxAllowedActualRiskPoints(signal, researchPath, risk);
        var entryOrder = new Order
        {
            Portfolio = Portfolio,
            Security = Security,
            Type = OrderTypes.Market,
            Direction = signal.Side == TradeSide.Long ? OrderDirections.Buy : OrderDirections.Sell,
            QuantityToFill = qty,
            TimeInForce = ReplayTimeInForce,
            Comment = $"OPF|{tradeId}|ENTRY|{researchPath}|{signal.SignalId}",
            AutoCancel = false
        };

        _replayExecution = new ReplayExecutionState(
            tradeId,
            signal.SignalId,
            signal.Side,
            researchPath,
            entryCandle.Bar,
            entryCandle.Time,
            entry,
            qty,
            stop,
            target,
            risk,
            maxAllowedRiskPoints,
            targetR,
            entryOrder);
        TrackRecentReplayExecution(_replayExecution);

        _researchLogger?.AppendInfo(_snapshot.SnapshotId, entryCandle.Bar, entryCandle.Time, $"EXEC_ENTRY_SEND trade={tradeId} signal={signal.SignalId} path={researchPath} side={signal.Side} qty={qty} stop={stop:0.########} target={target:0.########}");
        _researchLogger?.AppendSignal(signal with { Stage = SignalStage.Executed, SkipReasons = signal.SkipReasons.Concat(new[] { "ReplayExecuted", researchPath, tradeId }).ToArray() });
        var executeReasons = new List<string> { $"TradeID:{tradeId}" };
        var isObservationFiller = IsObservationConfirmFiller(signal, researchPath);
        if (isObservationFiller)
        {
            executeReasons.Add($"ObservationFiller:score={signal.SetupQualityScore.TotalScore:0.##},count={_observationConfirmFillerTradesToday + 1}/{_actualObservationConfirmMaxFillerTradesPerDay}");
            if (signal.SetupQualityScore.TotalScore < 50m)
                executeReasons.Add("DailyVolumeFiller48");
            if (IsObservationConfirmFillerExpansionV147(signal, researchPath))
                executeReasons.Add($"OCFillerExpansionV147:score={signal.SetupQualityScore.TotalScore:0.##},count={_observationConfirmFillerTradesToday + 1},dailyTrades={_replayTradesToday}");
        }
        if (IsDailyVolumeResearchFillerAllowed(signal, researchPath))
            executeReasons.Add($"DailyVolumeResearchFiller:{researchPath},dailyTrades={_replayTradesToday}/{_actualObservationConfirmFillerUntilDailyTrades}");
        if (IsDailyVolumeFloorAllowed(signal, researchPath))
            executeReasons.Add($"DailyVolumeFloor:{researchPath},dailyTrades={_replayTradesToday}/{_actualObservationConfirmFillerUntilDailyTrades}");
        if (IsMainlineVolumeFiller(signal, researchPath))
            executeReasons.Add($"{MainlineVolumeFillerTag(researchPath)}:risk={risk:0.##},score={signal.SetupQualityScore.TotalScore:0.##},dailyTrades={_replayTradesToday}");
        if (IsObservationConfirmVolumeRiskExpansion(signal, researchPath, risk))
        {
            executeReasons.Add($"ObservationRiskExpansion:risk={risk:0.##},base={_actualObservationConfirmMaxRiskPoints:0.##},max={_actualObservationConfirmVolumeMaxRiskPoints:0.##},dailyTrades={_replayTradesToday}/{_actualObservationConfirmFillerUntilDailyTrades}");
            executeReasons.Add("DailyVolumeBaseRisk18");
            if (IsObservationConfirmRiskExpansionV147(signal, researchPath, risk))
                executeReasons.Add($"OCRiskExpansionV147:risk={risk:0.##},score={signal.SetupQualityScore.TotalScore:0.##},dailyTrades={_replayTradesToday}");
        }
        if (IsDailyVolumeQualityRescue(signal, researchPath, risk))
        {
            executeReasons.Add($"DailyVolumeQualityRescueV120:risk={risk:0.##},score={signal.SetupQualityScore.TotalScore:0.##},dailyTrades={_replayTradesToday}/{_actualObservationConfirmFillerUntilDailyTrades}");
            if (IsDailyVolumeQualityRescueExpansionV147(signal, researchPath, risk))
                executeReasons.Add($"DailyVolumeQualityRescueV147:risk={risk:0.##},score={signal.SetupQualityScore.TotalScore:0.##},count={_observationConfirmQualityRescueTradesToday + 1},dailyTrades={_replayTradesToday}");
        }
        if (IsZoneBirthVolumeExpansionV122(signal, researchPath))
            executeReasons.Add($"ZoneBirthVolumeV122:risk={risk:0.##},score={signal.SetupQualityScore.TotalScore:0.##},rr={EstimatedActualRr(signal, researchPath, entry, risk):0.####},dailyTrades={_replayTradesToday}");
        if (IsStrictVolumeExpansionV122(signal, researchPath))
        {
            var tag = string.Equals(researchPath, "ObservationStrict_BullFresh", StringComparison.OrdinalIgnoreCase)
                ? "StrictBullFreshVolumeV130"
                : "StrictObservationVolumeV122";
            executeReasons.Add($"{tag}:risk={risk:0.##},score={signal.SetupQualityScore.TotalScore:0.##},rr={EstimatedActualRr(signal, researchPath, entry, risk):0.####},dailyTrades={_replayTradesToday}");
        }
        if (IsBreakawayVolumeExpansionV128(signal, researchPath))
        {
            var tag = BreakawayVolumeV128Tag(signal, risk);
            executeReasons.Add($"{tag}:risk={risk:0.##},score={signal.SetupQualityScore.TotalScore:0.##},rr={EstimatedActualRr(signal, researchPath, entry, risk):0.####},dailyTrades={_replayTradesToday}");
            if (IsBreakawayVolumeExpansionV147(signal, researchPath))
                executeReasons.Add($"BreakawayVolumeV147:side={signal.Side},risk={risk:0.##},score={signal.SetupQualityScore.TotalScore:0.##}");
        }
        if (IsObservationLongVolumeExpansionV123(signal, researchPath, risk))
        {
            executeReasons.Add($"ObservationLongVolumeV123:risk={risk:0.##},score={signal.SetupQualityScore.TotalScore:0.##},rr={EstimatedActualRr(signal, researchPath, entry, risk):0.####},dailyTrades={_replayTradesToday}");
            if (IsObservationConfirmLongRisk12V137(signal, researchPath, risk))
                executeReasons.Add($"ObservationConfirmLongRisk12V137:risk={risk:0.##},score={signal.SetupQualityScore.TotalScore:0.##}");
        }
        if (IsUnknownMicroRiskVolumeExpansionV126(signal, researchPath))
            executeReasons.Add($"UnknownMicroRiskVolumeV126:risk={risk:0.##},score={signal.SetupQualityScore.TotalScore:0.##},rr={EstimatedActualRr(signal, researchPath, entry, risk):0.####},dailyTrades={_replayTradesToday}");
        if (IsObservationShortVolumeExpansionV124(signal, researchPath, risk))
            executeReasons.Add($"ObservationShortVolumeV124:risk={risk:0.##},score={signal.SetupQualityScore.TotalScore:0.##},rr={EstimatedActualRr(signal, researchPath, entry, risk):0.####},dailyTrades={_replayTradesToday}");
        if (IsObservationConfirmRisk22V132(signal, researchPath, risk))
            executeReasons.Add($"ObservationConfirmRisk22V132:risk={risk:0.##},score={signal.SetupQualityScore.TotalScore:0.##},rr={EstimatedActualRr(signal, researchPath, entry, risk):0.####},dailyTrades={_replayTradesToday}");
        if (IsObservationConfirmWideStopVolumeV131(signal, researchPath))
            executeReasons.Add($"{ObservationConfirmWideStopVolumeV131Tag(signal, risk)}:risk={risk:0.##},score={signal.SetupQualityScore.TotalScore:0.##},rr={EstimatedActualRr(signal, researchPath, entry, risk):0.####},dailyTrades={_replayTradesToday}");
        var isWideStopLongExpansionV157 = IsObservationConfirmWideStopLongExpansionV157(signal, researchPath, risk);
        if (isWideStopLongExpansionV157)
            executeReasons.Add($"OCWideStopLongExpansionV157:risk={risk:0.##},score={signal.SetupQualityScore.TotalScore:0.##},count={_observationConfirmWideStopLongExpansionTradesToday + 1}/{ObservationConfirmWideStopLongExpansionV157MaxTradesPerDay}");
        if (IsObservationConfirmWideStopLowRiskV132(signal, researchPath, risk))
            executeReasons.Add($"OCWideStopLowRiskV132:side={signal.Side},risk={risk:0.##},score={signal.SetupQualityScore.TotalScore:0.##},rr={EstimatedActualRr(signal, researchPath, entry, risk):0.####},dailyTrades={_replayTradesToday}");
        if (IsFailureRetestWideStopVolumeFiller(researchPath))
            executeReasons.Add($"FailureRetestWideFiller:dailyTrades={_replayTradesToday}/{_actualObservationConfirmFillerUntilDailyTrades}");
        if (IsPositiveExpansionV146(signal, researchPath))
            executeReasons.Add($"PositiveExpansionV146:path={researchPath},side={signal.Side},risk={risk:0.##},score={signal.SetupQualityScore.TotalScore:0.##}");
        var profitTargetTag = SelectiveProfitTargetV145Tag(signal.Side, researchPath);
        if (!string.IsNullOrEmpty(profitTargetTag))
            executeReasons.Add($"{profitTargetTag}:targetR={targetR:0.##},risk={risk:0.##}");
        if (targetR != ReplayTargetR)
            executeReasons.Add($"ActualTargetOverride:targetR={targetR:0.##},risk={risk:0.##},path={researchPath}");
        var executeReason = string.Join("|", executeReasons);
        AppendExecutionDecision(signal, entryCandle, "Execute", executeReason, researchPath, stop, risk, tradeId);
        AppendExecutionEvent(signal, tradeId, entryCandle, "ENTRY_SEND", "ENTRY", researchPath, entry, qty, $"stop={stop:0.########}|target={target:0.########}");
        _replayTradesToday++;
        if (isObservationFiller)
            _observationConfirmFillerTradesToday++;
        if (IsDailyVolumeQualityRescue(signal, researchPath, risk))
            _observationConfirmQualityRescueTradesToday++;
        if (isWideStopLongExpansionV157)
            _observationConfirmWideStopLongExpansionTradesToday++;
        EnqueueExecutionAction("OpenReplayEntry", async () => await OpenOrderAsync(entryOrder));
    }

    private void TrackRecentReplayExecution(ReplayExecutionState execution)
    {
        _recentReplayExecutions.Add(execution);
        var maxStored = Math.Max(ActualMaxVisibleOrders * 3, 12);
        if (_recentReplayExecutions.Count > maxStored)
            _recentReplayExecutions.RemoveRange(0, _recentReplayExecutions.Count - maxStored);
    }

    private void AppendExecutionDecision(CandidateSignal signal, OpfCandle entryCandle, string decision, string reason, string researchPath, decimal stop, decimal risk, string tradeId = "")
    {
        if (_snapshot is null)
            return;

        var entry = entryCandle.Close;
        var targetR = ActualTargetRFor(signal, researchPath, risk);
        var target = TargetFromRisk(signal.Side, entry, risk, targetR);
        var reward = EstimateActualExecutionReward(signal, researchPath, entry, risk);
        var estimatedRr = reward.Points <= 0m || risk <= 0m ? 0m : Math.Round(reward.Points / risk, 4);

        _researchLogger?.AppendExecutionDecision(
            _snapshot.SnapshotId,
            signal.SignalId,
            entryCandle.Time,
            entryCandle.Bar,
            decision,
            reason,
            signal.Side.ToString(),
            signal.SetupType.ToString(),
            researchPath,
            signal.RegimeScore.TotalScore,
            signal.SetupQualityScore.TotalScore,
            ActualExecutionStrategySkipReasons(signal, researchPath).Length == 0,
            entry,
            stop,
            target,
            risk,
            reward.Points,
            reward.Model,
            estimatedRr,
            _replayDailyPnlDollars,
            _replayTradesToday,
            tradeId);
    }

    private bool IsReplayExecutionPathEnabled(string researchPath)
    {
        var configured = ReplayExecutionPath.Split(new[] { '|', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return configured.Any(x => x == "*" || string.Equals(x, researchPath, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsExecutionEligiblePath(string researchPath)
    {
        return researchPath is
            "TrendPullbackConfirmed" or
            "StructureConfirmShadow_ConfirmBarStop" or
            "StructureConfirmShadow_ConfirmBarStop_Min10" or
            "StructureConfirmShadow_ConfirmBarStop_Wait1" or
            "StructureConfirmShadow_SwingStop" or
            "StructureConfirmShadow" or
            "BreakawayFvg" or
            "BreakawayFvg_Qualified" or
            "BreakawayRetest" or
            "AlmostConfirmed" or
            "ObservationConfirm" or
            "ObservationConfirm_WideStop1_5R" or
            "ObservationStrict_BullFresh" or
            "ObservationStrict_BullFresh_WideStop1_5R" or
            "ObservationStrict_Other" or
            "ObservationStrict_Other_WideStop1_5R" or
            "ShadowCandidate" or
            "UnknownRegimeZoneTouch" or
            "ZoneBirthResearch" or
            "FailureReverse_ObservationInvalidated" or
            "FailureReverse_LongQualified" or
            "FailureReverse_RetestFailed" or
            "FailureReverse_RetestFailed_WideStop1_5R";
    }

    private string[] ActualExecutionStrategySkipReasons(CandidateSignal signal, string researchPath)
    {
        var reasons = new List<string>();
        var isImmediateFailureReverse = IsImmediateFailureReversePath(signal, researchPath);
        var isObservationConfirm = IsObservationConfirmPath(researchPath);
        var isFailureRetest = IsFailureRetestPath(researchPath);
        var isDailyVolumeFloor = IsDailyVolumeFloorAllowed(signal, researchPath);
        var isMainlineVolumeFiller = IsMainlineVolumeFiller(signal, researchPath);
        var isV122VolumeExpansion = IsV122VolumeExpansionAllowed(signal, researchPath);
        var isBreakawayVolumeExpansion = IsBreakawayVolumeExpansionV128(signal, researchPath);
        var isUnknownMicroRiskVolumeExpansion = IsUnknownMicroRiskVolumeExpansionV126(signal, researchPath);
        var isObservationConfirmWideStopVolume = IsObservationConfirmWideStopVolumeV131(signal, researchPath);
        var isObservationConfirmWideStopLowRisk = IsObservationConfirmWideStopLowRiskV132Quality(signal, researchPath);
        var isObservationConfirmRisk22 = IsObservationConfirmRisk22V132Quality(signal, researchPath);
        if (_actualRequireTrendRegime && !signal.RegimeScore.Passed && !isImmediateFailureReverse && !isObservationConfirm && !isFailureRetest && !isDailyVolumeFloor && !isMainlineVolumeFiller && !isV122VolumeExpansion && !isBreakawayVolumeExpansion && !isUnknownMicroRiskVolumeExpansion && !isObservationConfirmWideStopVolume && !isObservationConfirmWideStopLowRisk && !isObservationConfirmRisk22)
            reasons.Add($"StrategyRegimeNotTrend:score={signal.RegimeScore.TotalScore:0.##}");

        var minSetupQuality = isDailyVolumeFloor
            ? DailyVolumeFloorMinSetupQualityScore
            : isMainlineVolumeFiller
            ? MainlineVolumeFillerMinSetupQualityScore
            : isBreakawayVolumeExpansion
            ? BreakawayVolumeV128MinSetupQualityScore
            : isUnknownMicroRiskVolumeExpansion
            ? UnknownMicroRiskVolumeV126MinSetupQualityScore
            : isObservationConfirmWideStopVolume
            ? ObservationConfirmWideStopVolumeV131MinSetupQualityScore(signal)
            : isObservationConfirmWideStopLowRisk
            ? ObservationConfirmWideStopLowRiskV132MinSetupQualityScore
            : isObservationConfirmRisk22
            ? ObservationConfirmRisk22V132MinSetupQualityScore
            : IsZoneBirthVolumeExpansionPath(researchPath)
            ? ZoneBirthVolumeV122MinSetupQualityScore
            : IsStrictVolumeExpansionPath(researchPath)
            ? StrictVolumeV122MinSetupQualityScore
            : isObservationConfirm
            ? _actualObservationConfirmMinSetupQualityScore
            : isFailureRetest
                ? _actualFailureRetestMinSetupQualityScore
            : isImmediateFailureReverse
                ? _actualFailureReverseMinSetupQualityScore
                : _actualMinSetupQualityScore;
        if (minSetupQuality > 0m && signal.SetupQualityScore.TotalScore < minSetupQuality)
        {
            if (IsObservationConfirmFillerAllowed(signal, researchPath))
                return reasons.ToArray();

            reasons.Add($"SetupQualityTooLow:score={signal.SetupQualityScore.TotalScore:0.##},min={minSetupQuality:0.##}");
        }
        return reasons.ToArray();
    }

    private bool IsObservationConfirmFillerAllowed(CandidateSignal signal, string researchPath)
    {
        if (!IsObservationConfirmFiller(signal, researchPath))
            return false;

        if (_replayTradesToday >= _actualObservationConfirmFillerUntilDailyTrades)
            return false;

        return _observationConfirmFillerTradesToday < _actualObservationConfirmMaxFillerTradesPerDay;
    }

    private bool IsObservationConfirmFiller(CandidateSignal signal, string researchPath)
    {
        if (!IsObservationConfirmPath(researchPath))
            return false;

        var score = signal.SetupQualityScore.TotalScore;
        return score < _actualObservationConfirmMinSetupQualityScore &&
            score >= _actualObservationConfirmFillerMinSetupQualityScore;
    }

    private bool IsObservationConfirmFillerExpansionV147(CandidateSignal signal, string researchPath)
    {
        if (!IsObservationConfirmFiller(signal, researchPath))
            return false;

        return signal.SetupQualityScore.TotalScore < 48m ||
            _observationConfirmFillerTradesToday >= 3 ||
            _replayTradesToday >= 5;
    }

    private bool IsDailyVolumeSameBarTargetOnlyAllowed(CandidateSignal signal, string researchPath, string[] sameBarSkipReasons)
    {
        return false;
    }

    private decimal ObservationConfirmRiskCap(CandidateSignal signal, string researchPath)
    {
        if (IsObservationConfirmPath(researchPath) && signal.Side == TradeSide.Long)
            return ObservationConfirmLongV137MaxRiskPoints;
        if (IsObservationConfirmRisk22V132Quality(signal, researchPath))
            return Math.Max(_actualObservationConfirmMaxRiskPoints, ObservationConfirmRisk22V132MaxRiskPoints);
        if (IsObservationLongVolumeExpansionAllowedV123(signal, researchPath))
            return Math.Max(_actualObservationConfirmMaxRiskPoints, ObservationLongVolumeV123MaxRiskPoints);
        if (IsObservationShortVolumeExpansionAllowedV124(signal, researchPath))
            return Math.Max(_actualObservationConfirmMaxRiskPoints, ObservationShortVolumeV124MaxRiskPoints);
        if (IsDailyVolumeQualityRescueAllowed(signal, researchPath))
            return Math.Max(_actualObservationConfirmMaxRiskPoints, DailyVolumeQualityRescueMaxRiskPoints);
        if (IsObservationConfirmVolumeRiskExpansionAllowed(signal, researchPath))
            return Math.Max(_actualObservationConfirmMaxRiskPoints, _actualObservationConfirmVolumeMaxRiskPoints);

        return _actualObservationConfirmMaxRiskPoints;
    }

    private decimal MaxAllowedActualRiskPoints(CandidateSignal signal, string researchPath, decimal risk)
    {
        if (IsBreakawayLongSelectiveV134Quality(signal, researchPath))
            return BreakawayLongSelectiveV134MaxRiskPoints;
        if (IsBreakawayVolumeExpansionV128(signal, researchPath))
            return BreakawayVolumeV128MaxRiskPoints(signal);
        if (IsUnknownMicroRiskVolumeExpansionV126(signal, researchPath))
            return UnknownMicroRiskVolumeV126MaxRiskPoints;
        if (IsZoneBirthVolumeExpansionV122(signal, researchPath))
            return ZoneBirthVolumeV122MaxRiskPoints;
        if (IsStrictVolumeExpansionV122(signal, researchPath))
            return StrictVolumeV122MaxRiskPoints;
        if (IsObservationConfirmWideStopLongExpansionV157(signal, researchPath, risk))
            return ObservationConfirmWideStopLongExpansionV157MaxRiskPoints;
        if (IsObservationConfirmWideStopLowRiskV132Quality(signal, researchPath))
            return ObservationConfirmWideStopLowRiskV132MaxRiskPoints;
        if (IsObservationConfirmWideStopVolumeV131(signal, researchPath))
            return ObservationConfirmWideStopVolumeV131MaxRiskPoints;
        if (IsDailyVolumeFloorPath(researchPath))
            return DailyVolumeFloorMaxRiskPoints;
        if (IsBreakawayPath(researchPath))
            return _actualBreakawayMaxRiskPoints;
        if (IsObservationConfirmPath(researchPath))
            return ObservationConfirmRiskCap(signal, researchPath);
        if (IsFailureRetestPath(researchPath))
            return _actualFailureRetestMaxRiskPoints;
        if (IsMainlineVolumeFillerPath(researchPath))
            return MainlineVolumeFillerMaxRiskPoints;
        if (IsDailyVolumeResearchFillerPath(researchPath))
            return DailyVolumeResearchFillerMaxRiskPoints;

        return 0m;
    }

    private bool IsObservationConfirmVolumeRiskExpansion(CandidateSignal signal, string researchPath, decimal risk)
    {
        return IsObservationConfirmVolumeRiskExpansionAllowed(signal, researchPath) &&
            risk > _actualObservationConfirmMaxRiskPoints &&
            risk <= _actualObservationConfirmVolumeMaxRiskPoints;
    }

    private bool IsObservationConfirmRiskExpansionV147(CandidateSignal signal, string researchPath, decimal risk)
    {
        if (!IsObservationConfirmVolumeRiskExpansion(signal, researchPath, risk))
            return false;

        return risk > 18m || _replayTradesToday >= 5;
    }

    private bool IsObservationConfirmVolumeRiskExpansionAllowed(CandidateSignal signal, string researchPath)
    {
        if (!IsObservationConfirmPath(researchPath))
            return false;

        if (_actualObservationConfirmVolumeMaxRiskPoints <= _actualObservationConfirmMaxRiskPoints)
            return false;

        if (_replayTradesToday >= _actualObservationConfirmFillerUntilDailyTrades)
            return false;

        return signal.SetupQualityScore.TotalScore >= _actualObservationConfirmFillerMinSetupQualityScore;
    }

    private bool IsDailyVolumeQualityRescue(CandidateSignal signal, string researchPath, decimal risk)
    {
        return IsDailyVolumeQualityRescueAllowed(signal, researchPath) &&
            risk > _actualObservationConfirmVolumeMaxRiskPoints &&
            risk <= DailyVolumeQualityRescueMaxRiskPoints;
    }

    private bool IsDailyVolumeQualityRescueExpansionV147(CandidateSignal signal, string researchPath, decimal risk)
    {
        if (!IsDailyVolumeQualityRescue(signal, researchPath, risk))
            return false;

        return _observationConfirmQualityRescueTradesToday >= 2 || _replayTradesToday >= 5;
    }

    private bool IsDailyVolumeQualityRescueAllowed(CandidateSignal signal, string researchPath)
    {
        return IsObservationConfirmPath(researchPath) &&
            _replayTradesToday < _actualObservationConfirmFillerUntilDailyTrades &&
            _observationConfirmQualityRescueTradesToday < DailyVolumeQualityRescueMaxTradesPerDay &&
            signal.SetupQualityScore.TotalScore >= DailyVolumeQualityRescueMinSetupQualityScore;
    }

    private static bool IsImmediateFailureReversePath(CandidateSignal signal, string researchPath)
    {
        return signal.SetupType == SetupType.FailureReverse &&
            (string.Equals(researchPath, "FailureReverse_ObservationInvalidated", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(researchPath, "FailureReverse_LongQualified", StringComparison.OrdinalIgnoreCase));
    }

    private string[] ActualExecutionPathSkipReasons(CandidateSignal signal, string researchPath)
    {
        if (IsBreakawayPath(researchPath) &&
            signal.Side == TradeSide.Long)
            return new[] { "BreakawayLongActualDisabledV135" };

        if (IsEvidenceFrozenActualPath(researchPath))
            return new[] { $"EvidenceFrozenActualPathV135:{researchPath}" };

        if (string.Equals(researchPath, "TrendPullbackConfirmed", StringComparison.OrdinalIgnoreCase) &&
            signal.Side == TradeSide.Long)
            return new[] { "TrendPullbackLongDisabledV146" };

        if (string.Equals(researchPath, "StructureConfirmShadow_SwingStop", StringComparison.OrdinalIgnoreCase) &&
            signal.Side == TradeSide.Short)
            return new[] { "StructureSwingShortDisabledV146" };

        if (IsZoneBirthVolumeExpansionPath(researchPath) && signal.Side == TradeSide.Long)
            return new[] { "ZoneBirthLongActualDisabledV133" };

        if (IsObservationConfirmWideStopPath(researchPath) &&
            !IsObservationConfirmWideStopVolumeV131(signal, researchPath) &&
            !IsObservationConfirmWideStopLowRiskV132Quality(signal, researchPath))
            return new[] { $"ObservationConfirmWideStopQualityTooLowV132:side={signal.Side},score={signal.SetupQualityScore.TotalScore:0.##},min={ObservationConfirmWideStopLowRiskV132MinSetupQualityScore:0.##}" };

        if (string.Equals(researchPath, "AlmostConfirmed", StringComparison.OrdinalIgnoreCase))
            return new[] { "AlmostConfirmedActualDisabledV131" };

        if (string.Equals(researchPath, "ObservationStrict_BullFresh", StringComparison.OrdinalIgnoreCase))
            return new[] { "StrictBullFreshActualDisabledV131" };

        if (IsZoneBirthVolumeExpansionPath(researchPath) && signal.Side == TradeSide.Short)
            return new[] { "ZoneBirthShortActualDisabledV123" };

        if (IsBreakawayPath(researchPath) && !IsBreakawayVolumeExpansionV128(signal, researchPath))
            return new[] { $"BreakawayVolumeV128QualityTooLow:score={signal.SetupQualityScore.TotalScore:0.##},min={BreakawayVolumeV128MinSetupQualityScore:0.##}" };

        if (IsZoneBirthVolumeExpansionPath(researchPath) && !IsZoneBirthVolumeExpansionV122(signal, researchPath))
            return new[] { $"ZoneBirthVolumeV122QualityTooLow:score={signal.SetupQualityScore.TotalScore:0.##},min={ZoneBirthVolumeV122MinSetupQualityScore:0.##}" };

        if (IsUnknownMicroRiskVolumeExpansionPath(researchPath))
            return new[] { "UnknownMicroRiskActualDisabledV127" };

        if (IsStrictVolumeExpansionPath(researchPath) && !IsStrictVolumeExpansionV122(signal, researchPath))
            return new[] { $"StrictObservationVolumeV122QualityTooLow:score={signal.SetupQualityScore.TotalScore:0.##},min={StrictVolumeV122MinSetupQualityScore:0.##}" };

        if (IsDailyVolumeResearchFillerPath(researchPath) && !IsZoneBirthVolumeExpansionPath(researchPath) && !IsDailyVolumeResearchFillerAllowed(signal, researchPath))
            return new[] { $"DailyVolumeResearchFillerOnlyBeforeDailyTarget:trades={_replayTradesToday},target={_actualObservationConfirmFillerUntilDailyTrades}" };

        if (IsMainlineVolumeFillerPath(researchPath) && !IsMainlineVolumeFiller(signal, researchPath))
            return new[] { $"MainlineVolumeFillerQualityTooLow:score={signal.SetupQualityScore.TotalScore:0.##},min={MainlineVolumeFillerMinSetupQualityScore:0.##}" };

        if (IsDailyVolumeFloorPath(researchPath) && !IsObservationConfirmWideStopVolumeV131(signal, researchPath) && !IsDailyVolumeFloorAllowed(signal, researchPath))
            return new[] { $"DailyVolumeFloorOnlyBeforeDailyTarget:trades={_replayTradesToday},target={_actualObservationConfirmFillerUntilDailyTrades},score={signal.SetupQualityScore.TotalScore:0.##},min={DailyVolumeFloorMinSetupQualityScore:0.##}" };

        if (string.Equals(researchPath, "FailureReverse_ObservationInvalidated", StringComparison.OrdinalIgnoreCase) &&
            signal.Side == TradeSide.Long)
            return new[] { "FailureImmediateLongDisabled" };

        if (IsFailureRetestWideStopPath(researchPath))
            return new[] { "FailureRetestWideStopActualDisabledV136" };

        if (IsFailureRetestWideStopPath(researchPath) && !IsFailureRetestWideStopVolumeFiller(researchPath))
            return new[] { $"FailureRetestWideOnlyBeforeDailyTarget:trades={_replayTradesToday},target={_actualObservationConfirmFillerUntilDailyTrades}" };

        if (string.Equals(researchPath, "BreakawayFvg", StringComparison.OrdinalIgnoreCase) && !IsBreakawayVolumeExpansionV128(signal, researchPath))
            return new[] { "BreakawayBroadDisabledV110" };

        if (!_actualRequireFailureRetest || signal.SetupType != SetupType.FailureReverse)
            return Array.Empty<string>();

        return researchPath == "FailureReverse_RetestFailed"
            ? Array.Empty<string>()
            : new[] { $"FailureRequiresRetest:path={researchPath}" };
    }

    private static bool IsBreakawayPath(string researchPath)
    {
        return string.Equals(researchPath, "BreakawayFvg", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsObservationConfirmPath(string researchPath)
    {
        return string.Equals(researchPath, "ObservationConfirm", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUnknownMicroRiskVolumeExpansionPath(string researchPath)
    {
        return string.Equals(researchPath, "UnknownRegimeZoneTouch", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsObservationConfirmWideStopPath(string researchPath)
    {
        return string.Equals(researchPath, "ObservationConfirm_WideStop1_5R", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFailureRetestPath(string researchPath)
    {
        return string.Equals(researchPath, "FailureReverse_RetestFailed", StringComparison.OrdinalIgnoreCase) ||
            IsFailureRetestWideStopPath(researchPath);
    }

    private static bool IsFailureRetestWideStopPath(string researchPath)
    {
        return string.Equals(researchPath, "FailureReverse_RetestFailed_WideStop1_5R", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDailyVolumeResearchFillerPath(string researchPath)
    {
        return string.Equals(researchPath, "StructureConfirmShadow", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(researchPath, "ZoneBirthResearch", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMainlineVolumeFillerPath(string researchPath)
    {
        return string.Equals(researchPath, "AlmostConfirmed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(researchPath, "ShadowCandidate", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsZoneBirthVolumeExpansionPath(string researchPath)
    {
        return string.Equals(researchPath, "ZoneBirthResearch", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsStrictVolumeExpansionPath(string researchPath)
    {
        return string.Equals(researchPath, "ObservationStrict_BullFresh", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(researchPath, "ObservationStrict_Other", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDailyVolumeFloorPath(string researchPath)
    {
        return IsObservationConfirmWideStopPath(researchPath) ||
            IsFailureRetestWideStopPath(researchPath);
    }

    private bool IsDailyVolumeResearchFillerAllowed(CandidateSignal signal, string researchPath)
    {
        return IsDailyVolumeResearchFillerPath(researchPath) &&
            _replayTradesToday < _actualObservationConfirmFillerUntilDailyTrades &&
            signal.SetupQualityScore.TotalScore >= _actualMinSetupQualityScore;
    }

    private bool IsDailyVolumeFloorAllowed(CandidateSignal signal, string researchPath)
    {
        return IsDailyVolumeFloorPath(researchPath) &&
            _replayTradesToday < _actualObservationConfirmFillerUntilDailyTrades &&
            signal.SetupQualityScore.TotalScore >= DailyVolumeFloorMinSetupQualityScore;
    }

    private bool IsMainlineVolumeFiller(CandidateSignal signal, string researchPath)
    {
        return IsMainlineVolumeFillerPath(researchPath) &&
            signal.SetupQualityScore.TotalScore >= MainlineVolumeFillerMinSetupQualityScore;
    }

    private bool IsV122VolumeExpansionAllowed(CandidateSignal signal, string researchPath)
    {
        return IsZoneBirthVolumeExpansionV122(signal, researchPath) || IsStrictVolumeExpansionV122(signal, researchPath);
    }

    private bool IsZoneBirthVolumeExpansionV122(CandidateSignal signal, string researchPath)
    {
        return IsZoneBirthVolumeExpansionPath(researchPath) &&
            signal.SetupQualityScore.TotalScore >= ZoneBirthVolumeV122MinSetupQualityScore;
    }

    private bool IsStrictVolumeExpansionV122(CandidateSignal signal, string researchPath)
    {
        return IsStrictVolumeExpansionPath(researchPath) &&
            signal.SetupQualityScore.TotalScore >= StrictVolumeV122MinSetupQualityScore;
    }

    private bool IsBreakawayVolumeExpansionV128(CandidateSignal signal, string researchPath)
    {
        return IsBreakawayPath(researchPath) &&
            signal.SetupQualityScore.TotalScore >= BreakawayVolumeV128MinSetupQualityScore;
    }

    private static bool IsBreakawayVolumeExpansionV147(CandidateSignal signal, string researchPath)
    {
        return IsBreakawayPath(researchPath) &&
            signal.SetupQualityScore.TotalScore < 80m &&
            signal.SetupQualityScore.TotalScore >= BreakawayVolumeV128MinSetupQualityScore;
    }

    private static bool IsBreakawayLongSelectiveV134Quality(CandidateSignal signal, string researchPath)
    {
        return IsBreakawayPath(researchPath) &&
            signal.Side == TradeSide.Long &&
            signal.SetupQualityScore.TotalScore >= BreakawayLongSelectiveV134MinSetupQualityScore;
    }

    private static bool IsEvidenceFrozenActualPath(string researchPath)
    {
        return string.Equals(researchPath, "ShadowCandidate", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(researchPath, "ObservationStrict_Other", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(researchPath, "StructureConfirmShadow_ConfirmBarStop", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(researchPath, "StructureConfirmShadow_ConfirmBarStop_Min10", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(researchPath, "StructureConfirmShadow_ConfirmBarStop_Wait1", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPositiveExpansionV146(CandidateSignal signal, string researchPath)
    {
        if (string.Equals(researchPath, "BreakawayRetest", StringComparison.OrdinalIgnoreCase))
            return true;

        if (string.Equals(researchPath, "TrendPullbackConfirmed", StringComparison.OrdinalIgnoreCase))
            return signal.Side == TradeSide.Short;

        if (string.Equals(researchPath, "StructureConfirmShadow_SwingStop", StringComparison.OrdinalIgnoreCase))
            return signal.Side == TradeSide.Long;

        return false;
    }

    private static decimal BreakawayVolumeV128MaxRiskPoints(CandidateSignal signal)
    {
        return signal.Side == TradeSide.Short
            ? BreakawayVolumeV128ShortMaxRiskPoints
            : BreakawayVolumeV128LongMaxRiskPoints;
    }

    private static string BreakawayVolumeV128Tag(CandidateSignal signal, decimal risk)
    {
        if (signal.Side == TradeSide.Long)
            return "BreakawayLongSelectiveV134";

        return signal.Side == TradeSide.Short && risk > BreakawayVolumeV128LongMaxRiskPoints
            ? "BreakawayShortWideV128"
            : "BreakawayVolumeV128";
    }

    private bool IsUnknownMicroRiskVolumeExpansionV126(CandidateSignal signal, string researchPath)
    {
        return IsUnknownMicroRiskVolumeExpansionPath(researchPath) &&
            signal.SetupQualityScore.TotalScore >= UnknownMicroRiskVolumeV126MinSetupQualityScore;
    }

    private bool IsObservationLongVolumeExpansionAllowedV123(CandidateSignal signal, string researchPath)
    {
        return IsObservationConfirmPath(researchPath) &&
            signal.Side == TradeSide.Long &&
            signal.SetupQualityScore.TotalScore >= ObservationLongVolumeV123MinSetupQualityScore;
    }

    private bool IsObservationLongVolumeExpansionV123(CandidateSignal signal, string researchPath, decimal risk)
    {
        return IsObservationLongVolumeExpansionAllowedV123(signal, researchPath) &&
            risk > _actualObservationConfirmMaxRiskPoints &&
            risk <= ObservationLongVolumeV123MaxRiskPoints;
    }

    private static bool IsObservationConfirmLongRisk12V137(CandidateSignal signal, string researchPath, decimal risk)
    {
        return IsObservationConfirmPath(researchPath) &&
            signal.Side == TradeSide.Long &&
            risk > 11m &&
            risk <= ObservationConfirmLongV137MaxRiskPoints;
    }

    private static bool IsObservationConfirmLongWideRiskCutV137(CandidateSignal signal, string researchPath, decimal risk)
    {
        return IsObservationConfirmPath(researchPath) &&
            signal.Side == TradeSide.Long &&
            risk > ObservationConfirmLongV137MaxRiskPoints;
    }

    private bool IsObservationShortVolumeExpansionAllowedV124(CandidateSignal signal, string researchPath)
    {
        return IsObservationConfirmPath(researchPath) &&
            signal.Side == TradeSide.Short &&
            signal.SetupQualityScore.TotalScore >= ObservationShortVolumeV124MinSetupQualityScore;
    }

    private bool IsObservationShortVolumeExpansionV124(CandidateSignal signal, string researchPath, decimal risk)
    {
        return IsObservationShortVolumeExpansionAllowedV124(signal, researchPath) &&
            risk > _actualObservationConfirmMaxRiskPoints &&
            risk <= ObservationShortVolumeV124MaxRiskPoints;
    }

    private static bool IsObservationConfirmRisk22V132Quality(CandidateSignal signal, string researchPath)
    {
        return IsObservationConfirmPath(researchPath) &&
            signal.SetupQualityScore.TotalScore >= ObservationConfirmRisk22V132MinSetupQualityScore;
    }

    private static bool IsObservationConfirmRisk22V132(CandidateSignal signal, string researchPath, decimal risk)
    {
        return IsObservationConfirmRisk22V132Quality(signal, researchPath) &&
            risk > ObservationLongVolumeV123MaxRiskPoints &&
            risk <= ObservationConfirmRisk22V132MaxRiskPoints;
    }

    private static decimal ObservationConfirmWideStopVolumeV131MinSetupQualityScore(CandidateSignal signal)
    {
        return signal.Side == TradeSide.Long
            ? ObservationConfirmWideStopVolumeV131LongMinSetupQualityScore
            : ObservationConfirmWideStopVolumeV131ShortMinSetupQualityScore;
    }

    private static bool IsObservationConfirmWideStopVolumeV131(CandidateSignal signal, string researchPath)
    {
        return IsObservationConfirmWideStopPath(researchPath) &&
            signal.SetupQualityScore.TotalScore >= ObservationConfirmWideStopVolumeV131MinSetupQualityScore(signal);
    }

    private static bool IsObservationConfirmWideStopLongExpansionCandidateV157(CandidateSignal signal, string researchPath, decimal risk)
    {
        return IsObservationConfirmWideStopVolumeV131(signal, researchPath) &&
            signal.Side == TradeSide.Long &&
            risk > ObservationConfirmWideStopVolumeV131MaxRiskPoints &&
            risk <= ObservationConfirmWideStopLongExpansionV157MaxRiskPoints;
    }

    private bool IsObservationConfirmWideStopLongExpansionV157(CandidateSignal signal, string researchPath, decimal risk)
    {
        return IsObservationConfirmWideStopLongExpansionCandidateV157(signal, researchPath, risk) &&
            _observationConfirmWideStopLongExpansionTradesToday < ObservationConfirmWideStopLongExpansionV157MaxTradesPerDay;
    }

    private static string ObservationConfirmWideStopVolumeV131Tag(CandidateSignal signal, decimal risk)
    {
        var side = signal.Side == TradeSide.Long ? "Long" : "Short";
        var bucket = risk <= 18m ? "Risk18" : "Risk22";
        return $"OCWideStop{side}{bucket}V131";
    }

    private static bool IsObservationConfirmWideStopLowRiskV132Quality(CandidateSignal signal, string researchPath)
    {
        return IsObservationConfirmWideStopPath(researchPath) &&
            signal.SetupQualityScore.TotalScore >= ObservationConfirmWideStopLowRiskV132MinSetupQualityScore;
    }

    private static bool IsObservationConfirmWideStopLowRiskV132(CandidateSignal signal, string researchPath, decimal risk)
    {
        return !IsObservationConfirmWideStopVolumeV131(signal, researchPath) &&
            IsObservationConfirmWideStopLowRiskV132Quality(signal, researchPath) &&
            risk <= ObservationConfirmWideStopLowRiskV132MaxRiskPoints;
    }

    private bool IsFailureRetestWideStopVolumeFiller(string researchPath)
    {
        return IsFailureRetestWideStopPath(researchPath) &&
            _replayTradesToday < _actualObservationConfirmFillerUntilDailyTrades;
    }

    private static string MainlineVolumeFillerTag(string researchPath)
    {
        if (string.Equals(researchPath, "AlmostConfirmed", StringComparison.OrdinalIgnoreCase))
            return "AlmostConfirmedVolumeV130";

        return string.Equals(researchPath, "ShadowCandidate", StringComparison.OrdinalIgnoreCase)
            ? "ShadowCandidateFillerV122"
            : "MainlineVolumeFillerV122";
    }

    private static bool IsTrendPullbackConfirmedPath(string researchPath)
    {
        return string.Equals(researchPath, "TrendPullbackConfirmed", StringComparison.OrdinalIgnoreCase);
    }

    private string[] ActualExecutionRiskSkipReasons(CandidateSignal signal, string researchPath, OpfCandle entryCandle, decimal risk)
    {
        if (_snapshot is null)
            return Array.Empty<string>();

        var reasons = new List<string>();
        var instrument = _snapshot.InstrumentProfile;
        var isDailyVolumeFloor = IsDailyVolumeFloorAllowed(signal, researchPath);
        var isMainlineVolumeFiller = IsMainlineVolumeFiller(signal, researchPath);
        var isObservationVolumeRiskBand = IsObservationConfirmVolumeRiskExpansion(signal, researchPath, risk);
        var isDailyVolumeQualityRescue = IsDailyVolumeQualityRescue(signal, researchPath, risk);
        var isV122VolumeExpansion = IsV122VolumeExpansionAllowed(signal, researchPath);
        var isBreakawayVolumeExpansion = IsBreakawayVolumeExpansionV128(signal, researchPath);
        var isBreakawayLongSelective = IsBreakawayLongSelectiveV134Quality(signal, researchPath);
        var isObservationLongVolumeExpansion = IsObservationLongVolumeExpansionV123(signal, researchPath, risk);
        var isObservationShortVolumeExpansion = IsObservationShortVolumeExpansionV124(signal, researchPath, risk);
        var isUnknownMicroRiskVolumeExpansion = IsUnknownMicroRiskVolumeExpansionV126(signal, researchPath);
        var isObservationConfirmWideStopVolume = IsObservationConfirmWideStopVolumeV131(signal, researchPath);
        var isObservationConfirmWideStopLowRiskQuality = IsObservationConfirmWideStopLowRiskV132Quality(signal, researchPath);
        var isObservationConfirmWideStopLowRisk = IsObservationConfirmWideStopLowRiskV132(signal, researchPath, risk);
        var isObservationConfirmRisk22 = IsObservationConfirmRisk22V132(signal, researchPath, risk);
        if (isDailyVolumeFloor && !isObservationConfirmWideStopVolume && risk > DailyVolumeFloorMaxRiskPoints)
            reasons.Add($"DailyVolumeFloorRiskCapExceeded:risk={risk:0.##},max={DailyVolumeFloorMaxRiskPoints:0.##}");
        if (isMainlineVolumeFiller && risk > MainlineVolumeFillerMaxRiskPoints)
            reasons.Add($"MainlineVolumeFillerRiskCapExceeded:risk={risk:0.##},max={MainlineVolumeFillerMaxRiskPoints:0.##}");
        if (isObservationConfirmWideStopLowRiskQuality && !isObservationConfirmWideStopVolume && risk > ObservationConfirmWideStopLowRiskV132MaxRiskPoints)
            reasons.Add($"OCWideStopLowRiskV132RiskCapExceeded:risk={risk:0.##},max={ObservationConfirmWideStopLowRiskV132MaxRiskPoints:0.##},side={signal.Side}");
        var isWideStopLongExpansionV157 = IsObservationConfirmWideStopLongExpansionV157(signal, researchPath, risk);
        var isWideStopLongExpansionCandidateV157 = IsObservationConfirmWideStopLongExpansionCandidateV157(signal, researchPath, risk);
        if (isObservationConfirmWideStopVolume &&
            risk > ObservationConfirmWideStopVolumeV131MaxRiskPoints &&
            !isWideStopLongExpansionCandidateV157)
            reasons.Add($"ObservationConfirmWideStopVolumeV131RiskCapExceeded:risk={risk:0.##},max={ObservationConfirmWideStopVolumeV131MaxRiskPoints:0.##},side={signal.Side}");
        if (isWideStopLongExpansionCandidateV157 && !isWideStopLongExpansionV157)
            reasons.Add($"OCWideStopLongExpansionV157DailyCap:count={_observationConfirmWideStopLongExpansionTradesToday},max={ObservationConfirmWideStopLongExpansionV157MaxTradesPerDay}");
        if (isBreakawayVolumeExpansion)
        {
            var breakawayMaxRisk = isBreakawayLongSelective
                ? BreakawayLongSelectiveV134MaxRiskPoints
                : BreakawayVolumeV128MaxRiskPoints(signal);
            if (risk > breakawayMaxRisk)
            {
                var reason = isBreakawayLongSelective
                    ? "BreakawayLongSelectiveV134RiskCapExceeded"
                    : "BreakawayVolumeV128RiskCapExceeded";
                reasons.Add($"{reason}:risk={risk:0.##},max={breakawayMaxRisk:0.##},side={signal.Side}");
            }
        }
        if (isUnknownMicroRiskVolumeExpansion && risk > UnknownMicroRiskVolumeV126MaxRiskPoints)
            reasons.Add($"UnknownMicroRiskVolumeV126RiskCapExceeded:risk={risk:0.##},max={UnknownMicroRiskVolumeV126MaxRiskPoints:0.##}");
        if (IsZoneBirthVolumeExpansionV122(signal, researchPath) && risk > ZoneBirthVolumeV122MaxRiskPoints)
            reasons.Add($"ZoneBirthVolumeV122RiskCapExceeded:risk={risk:0.##},max={ZoneBirthVolumeV122MaxRiskPoints:0.##}");
        if (IsStrictVolumeExpansionV122(signal, researchPath) && risk > StrictVolumeV122MaxRiskPoints)
            reasons.Add($"StrictObservationVolumeV122RiskCapExceeded:risk={risk:0.##},max={StrictVolumeV122MaxRiskPoints:0.##}");
        if (IsBreakawayPath(researchPath) && !isBreakawayVolumeExpansion && _actualBreakawayMaxRiskPoints > 0m && risk > _actualBreakawayMaxRiskPoints)
            reasons.Add($"BreakawayRiskCapExceeded:risk={risk:0.##},max={_actualBreakawayMaxRiskPoints:0.##}");
        if (IsObservationConfirmPath(researchPath))
        {
            if (IsObservationConfirmLongWideRiskCutV137(signal, researchPath, risk))
                reasons.Add($"LongObservationWideRiskCutV137:risk={risk:0.##},max={ObservationConfirmLongV137MaxRiskPoints:0.##}");

            var maxObservationRisk = ObservationConfirmRiskCap(signal, researchPath);
            if (maxObservationRisk > 0m && risk > maxObservationRisk)
                reasons.Add($"ObservationConfirmRiskCapExceeded:risk={risk:0.##},max={maxObservationRisk:0.##}");

            if (IsLongObservationRiskExpansionDisabled(signal, risk) && !isObservationLongVolumeExpansion)
                reasons.Add($"LongObservationRiskExpansionDisabledV111:risk={risk:0.##}");
            if (IsShortObservationMidRiskQualityCut(signal, risk) && !isObservationShortVolumeExpansion)
                reasons.Add($"ShortObservationMidRiskQualityCutV112:score={signal.SetupQualityScore.TotalScore:0.##},min={ShortObservationMidRiskQualityCutMinScore:0.##},risk={risk:0.##}");
        }
        if (IsFailureRetestPath(researchPath) && !isDailyVolumeFloor && _actualFailureRetestMaxRiskPoints > 0m && risk > _actualFailureRetestMaxRiskPoints)
            reasons.Add($"FailureRetestRiskCapExceeded:risk={risk:0.##},max={_actualFailureRetestMaxRiskPoints:0.##}");
        if (IsDailyVolumeResearchFillerPath(researchPath) && !IsZoneBirthVolumeExpansionV122(signal, researchPath) && risk > DailyVolumeResearchFillerMaxRiskPoints)
            reasons.Add($"DailyVolumeResearchFillerRiskCapExceeded:risk={risk:0.##},max={DailyVolumeResearchFillerMaxRiskPoints:0.##}");

        if (!isBreakawayVolumeExpansion && instrument.MaxRiskPointsHard > 0m && risk > instrument.MaxRiskPointsHard)
            reasons.Add($"RiskTooWideHard:risk={risk:0.##},max={instrument.MaxRiskPointsHard:0.##}");

        var atr14 = CalculateAtr14(entryCandle);
        var maxAllowedRisk = MaxAllowedRiskPoints(instrument, atr14);
        if (!isDailyVolumeFloor && !isMainlineVolumeFiller && !isObservationVolumeRiskBand && !isDailyVolumeQualityRescue && !isV122VolumeExpansion && !isBreakawayVolumeExpansion && !isObservationLongVolumeExpansion && !isObservationShortVolumeExpansion && !isUnknownMicroRiskVolumeExpansion && !isObservationConfirmWideStopVolume && !isObservationConfirmWideStopLowRisk && !isObservationConfirmRisk22 && maxAllowedRisk > 0m && risk > maxAllowedRisk)
            reasons.Add($"RiskTooWideVolAdjusted:risk={risk:0.##},max={maxAllowedRisk:0.##},atr14={atr14:0.##}");

        var reward = EstimateActualExecutionReward(signal, researchPath, entryCandle.Close, risk);
        var estimatedRr = EstimatedActualRr(signal, researchPath, entryCandle.Close, risk);
        var minEstimatedRr = isObservationConfirmWideStopLowRisk
            ? ObservationConfirmWideStopLowRiskV132MinEstimatedRr
            : isObservationConfirmRisk22
            ? ObservationConfirmRisk22V132MinEstimatedRr
            : isObservationConfirmWideStopVolume
            ? ObservationConfirmWideStopVolumeV131MinEstimatedRr
            : isDailyVolumeFloor
            ? DailyVolumeFloorMinEstimatedRr
            : isMainlineVolumeFiller
                ? MainlineVolumeFillerMinEstimatedRr
            : isBreakawayVolumeExpansion
                ? isBreakawayLongSelective
                    ? BreakawayLongSelectiveV134MinEstimatedRr
                    : BreakawayVolumeV128MinEstimatedRr
            : isObservationLongVolumeExpansion
                ? ObservationLongVolumeV123MinEstimatedRr
            : isObservationShortVolumeExpansion
                ? ObservationShortVolumeV124MinEstimatedRr
            : isUnknownMicroRiskVolumeExpansion
                ? UnknownMicroRiskVolumeV126MinEstimatedRr
            : IsZoneBirthVolumeExpansionV122(signal, researchPath)
                ? ZoneBirthVolumeV122MinEstimatedRr
            : IsStrictVolumeExpansionV122(signal, researchPath)
                ? StrictVolumeV122MinEstimatedRr
            : isDailyVolumeQualityRescue
                ? DailyVolumeQualityRescueMinEstimatedRr
            : ActualMinEstimatedRr > 0m ? ActualMinEstimatedRr : instrument.MinEstimatedRr;
        if (minEstimatedRr > 0m && estimatedRr < minEstimatedRr)
            reasons.Add($"EstimatedRRTooLow:rr={estimatedRr:0.####},min={minEstimatedRr:0.##},reward={reward.Points:0.##},model={reward.Model}");

        return reasons.ToArray();
    }

    private decimal EstimatedActualRr(CandidateSignal signal, string researchPath, decimal entry, decimal risk)
    {
        var reward = EstimateActualExecutionReward(signal, researchPath, entry, risk);
        return reward.Points <= 0m || risk <= 0m ? 0m : Math.Round(reward.Points / risk, 4);
    }

    private RewardEstimate EstimateActualExecutionReward(CandidateSignal signal, string researchPath, decimal entry, decimal risk)
    {
        var targetR = ActualTargetRFor(signal, researchPath, risk);
        if (IsObservationConfirmWideStopLongExpansionCandidateV157(signal, researchPath, risk))
            return new RewardEstimate(risk * targetR, $"OCWideStopLongExpansionV159TargetR:{targetR:0.##}");

        if (IsUnknownMicroRiskVolumeExpansionV126(signal, researchPath))
            return new RewardEstimate(risk * targetR, $"UnknownMicroRiskVolumeV126TargetR:{targetR:0.##}");

        if (targetR != ReplayTargetR)
            return new RewardEstimate(Math.Round(risk * targetR, 2), $"ActualTargetOverride:{targetR:0.##}");

        return EstimateReward(signal, researchPath, entry, risk);
    }

    private bool IsLongObservationRiskExpansionDisabled(CandidateSignal signal, decimal risk)
    {
        return signal.Side == TradeSide.Long &&
            risk > _actualObservationConfirmMaxRiskPoints &&
            risk <= _actualObservationConfirmVolumeMaxRiskPoints;
    }

    private static bool IsShortObservationMidRiskQualityCut(CandidateSignal signal, decimal risk)
    {
        return signal.Side == TradeSide.Short &&
            risk > ShortObservationMidRiskQualityCutMinRisk &&
            risk <= ShortObservationMidRiskQualityCutMaxRisk &&
            signal.SetupQualityScore.TotalScore < ShortObservationMidRiskQualityCutMinScore;
    }

    private decimal ActualTargetRFor(CandidateSignal signal, string researchPath, decimal risk)
    {
        return ActualTargetRFor(signal.Side, researchPath, risk);
    }

    private decimal ActualTargetRFor(TradeSide side, string researchPath, decimal risk)
    {
        if (!string.IsNullOrEmpty(SelectiveProfitTargetV145Tag(side, researchPath)))
            return SelectiveProfitTargetV145R;

        return ReplayTargetR;
    }

    private static string SelectiveProfitTargetV145Tag(TradeSide side, string researchPath)
    {
        if (side == TradeSide.Short &&
            (string.Equals(researchPath, "BreakawayFvg", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(researchPath, "BreakawayFvg_Qualified", StringComparison.OrdinalIgnoreCase)))
        {
            return "BreakawayShortTarget2RV145";
        }

        return string.Empty;
    }

    private static decimal TargetFromRisk(TradeSide side, decimal entry, decimal risk, decimal targetR)
    {
        return side == TradeSide.Long
            ? entry + risk * targetR
            : entry - risk * targetR;
    }

    private string[] ActualExecutionSameBarSkipReasons(CandidateSignal signal, string researchPath, OpfCandle entryCandle, decimal stop, decimal risk)
    {
        var reasons = new List<string>();
        var entry = entryCandle.Close;
        var target = TargetFromRisk(signal.Side, entry, risk, ActualTargetRFor(signal, researchPath, risk));

        if (signal.Side == TradeSide.Long)
        {
            if (entryCandle.Low <= stop)
                reasons.Add($"EntryBarStopTouched:low={entryCandle.Low:0.########},stop={stop:0.########}");
            if (entryCandle.High >= target)
                reasons.Add($"EntryBarTargetTouched:high={entryCandle.High:0.########},target={target:0.########}");
        }
        else
        {
            if (entryCandle.High >= stop)
                reasons.Add($"EntryBarStopTouched:high={entryCandle.High:0.########},stop={stop:0.########}");
            if (entryCandle.Low <= target)
                reasons.Add($"EntryBarTargetTouched:low={entryCandle.Low:0.########},target={target:0.########}");
        }

        return reasons.ToArray();
    }

    private void ResetReplayExecutionDailyCounter(DateTime date)
    {
        if (_replayExecutionDate == date)
            return;

        _replayExecutionDate = date;
        _replayTradesToday = 0;
        _replayExitsToday = 0;
        _replayTpToday = 0;
        _replaySlToday = 0;
        _replayOtherExitToday = 0;
        _observationConfirmFillerTradesToday = 0;
        _observationConfirmQualityRescueTradesToday = 0;
        _observationConfirmWideStopLongExpansionTradesToday = 0;
        _replayFullLossTradesToday = 0;
        _replayConsecutiveLossesToday = 0;
        _replayDailyPnlDollars = 0m;
        _replayDailyR = 0m;
        _lastExecutionHudText = "-";
        _recentExecutionHudItems.Clear();
    }

    private bool IsDailyAbnormalFillGuardActive(DateTime date)
    {
        return _dailyAbnormalFillGuardDate == date && _dailyAbnormalFillGuardCount > 0;
    }

    private void ActivateDailyAbnormalFillGuard(ReplayExecutionState execution, string reason)
    {
        var date = execution.CreatedTime.Date;
        if (_dailyAbnormalFillGuardDate != date)
        {
            _dailyAbnormalFillGuardDate = date;
            _dailyAbnormalFillGuardCount = 0;
        }

        _dailyAbnormalFillGuardCount++;
        LogExecutionInfo($"EXEC_DAILY_ABNORMAL_FILL_GUARD_ON date={date:yyyy-MM-dd} trade={execution.TradeId} count={_dailyAbnormalFillGuardCount} reason={reason}");
        AppendExecutionEvent(execution, "DAILY_ABNORMAL_FILL_GUARD_ON", "ENTRY", execution.EntryAvgPrice, execution.EntryFilledQty, $"count={_dailyAbnormalFillGuardCount}|{reason}");
    }

    private static bool IsExpectedTradeOrder(ReplayExecutionState execution, string role, Order order, out string reason)
    {
        reason = string.Empty;
        var expected = role switch
        {
            "ENTRY" => execution.EntryOrder,
            "SL" => execution.StopOrder,
            "TP" => execution.TargetOrder,
            "FLATTEN" => null,
            ReplayStopExitRole => null,
            _ => null
        };

        if (role is "FLATTEN" or ReplayStopExitRole)
            return true;

        if (expected is null)
        {
            reason = $"expectedOrderMissing|actualExt={order.ExtId}|actualState={order.State}|actualType={order.Type}|actualDir={order.Direction}";
            return false;
        }

        if (expected.ExtId != 0 && order.ExtId != 0 && expected.ExtId != order.ExtId)
        {
            reason = $"expectedExt={expected.ExtId}|actualExt={order.ExtId}|expectedState={expected.State}|actualState={order.State}|actualType={order.Type}|actualDir={order.Direction}";
            return false;
        }

        return true;
    }

    private async Task HandleReplayTradeAsync(MyTrade trade)
    {
        if (_replayExecution is null || trade.Order is null)
            return;
        if (!TryParseExecutionComment(trade.Order.Comment, out var tradeId, out var role))
            return;
        if (!string.Equals(tradeId, _replayExecution.TradeId, StringComparison.OrdinalIgnoreCase))
            return;
        if (!IsExpectedTradeOrder(_replayExecution, role, trade.Order, out var mismatchReason))
        {
            LogExecutionInfo($"EXEC_TRADE_ORDER_MISMATCH trade={tradeId} role={role} {mismatchReason}");
            AppendExecutionEvent(_replayExecution, "TRADE_ORDER_MISMATCH", role, trade.Price, trade.Volume, mismatchReason);
            return;
        }

        var tradeOrder = trade.Order;
        LogExecutionInfo($"EXEC_MYTRADE trade={tradeId} role={role} price={trade.Price:0.########} volume={trade.Volume:0.########} orderExt={tradeOrder.ExtId} orderState={tradeOrder.State} orderType={tradeOrder.Type} orderDir={tradeOrder.Direction} orderPrice={tradeOrder.Price:0.########} orderTrig={tradeOrder.TriggerPrice:0.########} orderUnfilled={tradeOrder.Unfilled:0.########}");
        AppendExecutionEvent(_replayExecution, "MYTRADE", role, trade.Price, trade.Volume, $"orderExt={tradeOrder.ExtId}|orderState={tradeOrder.State}|orderType={tradeOrder.Type}|orderDir={tradeOrder.Direction}|orderPrice={tradeOrder.Price:0.########}|orderTrig={tradeOrder.TriggerPrice:0.########}|orderUnfilled={tradeOrder.Unfilled:0.########}");

        if (_replayExecution.ExitCompleted && role == "ENTRY")
        {
            var fillQty = Math.Max(0m, trade.Volume);
            var reason = $"LateEntryAfterCompleted:exitRole={_replayExecution.ExitRole ?? "-"}";
            LogExecutionInfo($"EXEC_LATE_ENTRY_AFTER_COMPLETED trade={tradeId} price={trade.Price:0.########} volume={fillQty:0.########} reason={reason}");
            AppendExecutionEvent(_replayExecution, "LATE_ENTRY_AFTER_COMPLETED", "ENTRY", trade.Price, fillQty, reason);
            await SubmitEmergencyFlattenAsync(fillQty, reason, OppositeDirection(trade.Order.Direction));
            return;
        }

        if (role == "ENTRY")
        {
            var fillQty = Math.Max(0m, trade.Volume);
            if (fillQty > 0m)
            {
                var oldQty = _replayExecution.EntryFilledQty;
                var oldValue = _replayExecution.EntryAvgPrice * oldQty;
                _replayExecution.EntryFilledQty = oldQty + fillQty;
                _replayExecution.EntryAvgPrice = (oldValue + trade.Price * fillQty) / _replayExecution.EntryFilledQty;
            }

            var entryDrift = Math.Abs(_replayExecution.EntryAvgPrice - _replayExecution.CreatedPrice);
            var tolerance = ExecutionFillTolerance();
            if (entryDrift > tolerance)
            {
                var reason = $"EntryFillOutOfRange:fill={_replayExecution.EntryAvgPrice:0.########}|planned={_replayExecution.CreatedPrice:0.########}|drift={entryDrift:0.########}|max={tolerance:0.########}";
                LogExecutionInfo($"EXEC_ENTRY_FILL_REJECTED trade={tradeId} {reason}");
                AppendExecutionEvent(_replayExecution, "ENTRY_FILL_REJECTED", "ENTRY", trade.Price, trade.Volume, reason);
                ActivateDailyAbnormalFillGuard(_replayExecution, reason);
                _replayExecution.EmergencyFlattenSubmitted = true;
                var flattenQty = Math.Max(0m, _replayExecution.EntryFilledQty - _replayExecution.EmergencyFlattenSubmittedQty);
                if (flattenQty > 0m)
                {
                    _replayExecution.EmergencyFlattenSubmittedQty += flattenQty;
                    await SubmitEmergencyFlattenAsync(flattenQty, reason, OppositeDirection(trade.Order.Direction));
                }
                return;
            }

            if (_replayExecution.EntryAbortPending)
            {
                var flattenQty = Math.Max(0m, _replayExecution.EntryFilledQty - _replayExecution.EmergencyFlattenSubmittedQty);
                if (flattenQty > 0m)
                {
                    var reason = $"LatePartialEntryFillAfterAbort:filled={_replayExecution.EntryFilledQty:0.########}|covered={_replayExecution.EmergencyFlattenSubmittedQty:0.########}";
                    _replayExecution.EmergencyFlattenSubmittedQty += flattenQty;
                    await SubmitEmergencyFlattenAsync(flattenQty, reason, OppositeDirection(trade.Order.Direction));
                }
                return;
            }

            if (_replayExecution.EntryFilledQty + 0.0000001m < _replayExecution.Quantity)
            {
                var reason = $"filled={_replayExecution.EntryFilledQty:0.########}|requested={_replayExecution.Quantity:0.########}|unfilled={tradeOrder.Unfilled:0.########}";
                LogExecutionInfo($"EXEC_ENTRY_PARTIAL_FILL_WAITING trade={tradeId} {reason}");
                AppendExecutionEvent(_replayExecution, "ENTRY_PARTIAL_FILL_WAITING", "ENTRY", _replayExecution.EntryAvgPrice, _replayExecution.EntryFilledQty, reason);
                SchedulePartialEntryFinalizeIfNeeded(_replayExecution);
                return;
            }

            if (!_replayExecution.BracketSubmitted)
                await SubmitReplayBracketAsync(_replayExecution);
            return;
        }

        if (role is "SL" or "TP" or "FLATTEN" or ReplayStopExitRole)
        {
            var fillQty = Math.Max(0m, trade.Volume);
            if (_replayExecution.ExitCompleted)
            {
                LogExecutionInfo($"EXEC_DUPLICATE_EXIT_FILL trade={tradeId} role={role} price={trade.Price:0.########} volume={fillQty:0.########}");
                var duplicateReason = role is "FLATTEN" or ReplayStopExitRole
                    ? "duplicateFlattenIgnored"
                    : "duplicateProtectiveExit";
                AppendExecutionEvent(_replayExecution, "DUPLICATE_EXIT_FILL", role, trade.Price, fillQty, duplicateReason);
                MarkProtectionCleanupPending(_replayExecution, $"DuplicateExit:{role}");
                await CleanupProtectionOrdersAsync(_replayExecution, $"DuplicateExit:{role}");
                if (role is "SL" or "TP")
                    await SubmitDuplicateExitFlattenIfNeededAsync(_replayExecution, fillQty, role);
                return;
            }

            var oldExitQty = _replayExecution.ExitFilledQty;
            var oldExitValue = _replayExecution.ExitAvgPrice * oldExitQty;
            _replayExecution.ExitFilledQty += fillQty;
            if (fillQty > 0m && _replayExecution.ExitFilledQty > 0m)
                _replayExecution.ExitAvgPrice = (oldExitValue + trade.Price * fillQty) / _replayExecution.ExitFilledQty;

            LogExecutionInfo($"EXEC_EXIT_FILLED trade={tradeId} role={role} price={trade.Price:0.########}");
            AppendExecutionEvent(_replayExecution, "EXIT_FILLED", role, trade.Price, trade.Volume, string.Empty);
            _replayExecution.UpdateActualExcursion(trade.Price);
            await TryCancelExecutionOrderAsync(_replayExecution.EntryOrder, "ExitFilledCancelEntry");

            var completeQty = _replayExecution.BracketQty > 0m ? _replayExecution.BracketQty : _replayExecution.Quantity;
            if (_replayExecution.ExitFilledQty + 0.0000001m < completeQty)
                return;

            var finalExitPrice = _replayExecution.ExitAvgPrice > 0m ? _replayExecution.ExitAvgPrice : trade.Price;
            var validation = ValidateExecutionFill(_replayExecution, finalExitPrice, role);
            if (validation.IsAbnormal)
            {
                LogExecutionInfo($"EXEC_ABNORMAL trade={tradeId} role={role} reason={validation.Reason} exit={finalExitPrice:0.########} expected={validation.ExpectedExitPrice:0.########} drift={validation.ExitPriceDriftPoints:0.########}");
                AppendExecutionEvent(_replayExecution, "ABNORMAL_EXECUTION", role, finalExitPrice, _replayExecution.ExitFilledQty, $"{validation.Reason}|expected={validation.ExpectedExitPrice:0.########}|drift={validation.ExitPriceDriftPoints:0.########}");
            }
            UpdateReplayExecutionDailyResult(_replayExecution.CreatedTime.Date, validation.NormalDollars, role);
            var loggedExitPrice = IsNormalizedReplayExitFill(validation)
                ? validation.ExpectedExitPrice
                : finalExitPrice;
            _replayExecution.ExitCompleted = true;
            _replayExecution.ExitBar = _lastResearchCandle?.Bar;
            _replayExecution.ExitPrice = loggedExitPrice;
            _replayExecution.ExitRole = role;

            AppendExecutionTrade(_replayExecution, loggedExitPrice, role, validation);
            TryWriteActualResearchOutcomeOnExit(_replayExecution, role);
            MarkProtectionCleanupPending(_replayExecution, $"ExitFilled:{role}");

            if (_replayExecution.ProtectionCleanupPending)
                await CleanupProtectionOrdersAsync(_replayExecution, $"ExitFilled:{role}");
        }
    }

    private decimal CalculateExecutionDollars(ReplayExecutionState execution, decimal exitPrice)
    {
        if (_snapshot is null)
            return 0m;

        var entry = execution.EntryAvgPrice > 0m ? execution.EntryAvgPrice : execution.CreatedPrice;
        var points = execution.Side == TradeSide.Long
            ? exitPrice - entry
            : entry - exitPrice;
        return Math.Round(points * _snapshot.InstrumentProfile.PointValue * execution.Quantity, 2);
    }

    private ExecutionFillValidation ValidateExecutionFill(ReplayExecutionState execution, decimal exitPrice, string exitRole)
    {
        if (_snapshot is null)
            return new ExecutionFillValidation(false, string.Empty, 0m, 0m, CalculateExecutionDollars(execution, exitPrice), 0m, 0m, 0m, 0m, 0m);

        var entry = execution.EntryAvgPrice > 0m ? execution.EntryAvgPrice : execution.CreatedPrice;
        var rawPoints = execution.Side == TradeSide.Long
            ? exitPrice - entry
            : entry - exitPrice;
        var rawPointsR = execution.InitialRiskPoints <= 0m ? 0m : Math.Round(rawPoints / execution.InitialRiskPoints, 4);
        var rawDollars = Math.Round(rawPoints * _snapshot.InstrumentProfile.PointValue * execution.Quantity, 2);
        var tolerance = ExecutionFillTolerance();
        var expectedExit = ExpectedExitPrice(execution, exitRole);
        var drift = expectedExit > 0m ? Math.Abs(exitPrice - expectedExit) : 0m;
        var reasons = new List<string>();

        var entryDrift = Math.Abs(entry - execution.CreatedPrice);
        if (entryDrift > tolerance)
            reasons.Add($"AbnormalEntryFill:drift={entryDrift:0.##},max={tolerance:0.##}");

        if (string.Equals(exitRole, "TP", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(exitRole, "SL", StringComparison.OrdinalIgnoreCase))
        {
            if (drift > tolerance)
                reasons.Add($"Abnormal{exitRole}Fill:drift={drift:0.##},max={tolerance:0.##}");
        }
        else if (!string.Equals(exitRole, ReplayStopExitRole, StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add($"AbnormalExitRole:{exitRole}");
        }

        var entryIsAbnormal = entryDrift > tolerance;
        var isExitRoleNormal = string.Equals(exitRole, "TP", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(exitRole, "SL", StringComparison.OrdinalIgnoreCase);
        var canNormalizeReplayExit = !entryIsAbnormal &&
            isExitRoleNormal &&
            expectedExit > 0m &&
            drift > tolerance;
        if (canNormalizeReplayExit)
        {
            var normalizedPoints = execution.Side == TradeSide.Long
                ? expectedExit - entry
                : entry - expectedExit;
            var normalizedPointsR = execution.InitialRiskPoints <= 0m
                ? 0m
                : Math.Round(normalizedPoints / execution.InitialRiskPoints, 4);
            var normalizedDollars = Math.Round(normalizedPoints * _snapshot.InstrumentProfile.PointValue * execution.Quantity, 2);
            reasons.Add($"NormalizedReplayExitFill:{exitRole}:raw={exitPrice:0.########},expected={expectedExit:0.########}");
            return new ExecutionFillValidation(
                false,
                string.Join("|", reasons),
                expectedExit,
                drift,
                normalizedDollars,
                normalizedPoints,
                normalizedPointsR,
                rawPoints,
                rawDollars,
                rawPointsR);
        }

        var isAbnormal = reasons.Count > 0;
        return new ExecutionFillValidation(
            isAbnormal,
            string.Join("|", reasons),
            expectedExit,
            drift,
            isAbnormal ? 0m : rawDollars,
            isAbnormal ? 0m : rawPoints,
            isAbnormal ? 0m : rawPointsR,
            rawPoints,
            rawDollars,
            rawPointsR);
    }

    private static decimal ExpectedExitPrice(ReplayExecutionState execution, string exitRole)
    {
        if (string.Equals(exitRole, "TP", StringComparison.OrdinalIgnoreCase))
            return execution.Target;
        if (string.Equals(exitRole, "SL", StringComparison.OrdinalIgnoreCase))
            return execution.Stop;
        return 0m;
    }

    private static bool IsNormalizedReplayExitFill(ExecutionFillValidation validation)
    {
        return validation.Reason.Contains("NormalizedReplayExitFill", StringComparison.OrdinalIgnoreCase);
    }

    private decimal ExecutionFillTolerance()
    {
        if (_snapshot is null)
            return 4m;

        return Math.Max(_snapshot.InstrumentProfile.TickSize * 16m, 4m);
    }

    private void UpdateReplayExecutionDailyResult(DateTime date, decimal dollars, string exitRole)
    {
        ResetReplayExecutionDailyCounter(date);
        _replayDailyPnlDollars += dollars;

        if (dollars < 0m)
            _replayConsecutiveLossesToday++;
        else if (dollars > 0m)
            _replayConsecutiveLossesToday = 0;

        if (exitRole == "SL")
            _replayFullLossTradesToday++;
    }

    private void UpdateExecutionHudStats(ReplayExecutionState execution, string exitRole, decimal dollars, decimal pointsR)
    {
        _replayExitsToday++;
        _replayDailyR += pointsR;

        if (exitRole == "TP")
            _replayTpToday++;
        else if (exitRole == "SL")
            _replaySlToday++;
        else
            _replayOtherExitToday++;

        var side = execution.Side == TradeSide.Long ? "L" : "S";
        _lastExecutionHudText = $"{side} {exitRole} {pointsR:0.00}R ${dollars:0.##} {execution.ResearchPath}";
        _recentExecutionHudItems.Enqueue(_lastExecutionHudText);
        while (_recentExecutionHudItems.Count > 3)
            _recentExecutionHudItems.Dequeue();
    }

    private async Task SubmitReplayBracketAsync(ReplayExecutionState execution)
    {
        var qty = execution.EntryFilledQty > 0m ? execution.EntryFilledQty : execution.Quantity;
        if (qty <= 0m)
            return;

        RepriceExecutionBracketFromFill(execution);
        if (execution.MaxAllowedRiskPoints > 0m && execution.InitialRiskPoints > execution.MaxAllowedRiskPoints)
        {
            if (IsEntryFillRiskDriftAccepted(execution))
            {
                var acceptedReason = $"RiskDriftAcceptedV120:risk={execution.InitialRiskPoints:0.##}|max={execution.MaxAllowedRiskPoints:0.##}|planned={execution.PlannedRiskPoints:0.##}|tolerance={EntryFillRiskDriftTolerancePoints:0.##}";
                LogExecutionInfo($"EXEC_ENTRY_FILLED_RISK_DRIFT_ACCEPTED trade={execution.TradeId} {acceptedReason}");
                AppendExecutionEvent(execution, "ENTRY_FILLED_RISK_DRIFT_ACCEPTED", "ENTRY", execution.EntryAvgPrice, qty, acceptedReason);
            }
            else
            {
            var reason = $"EntryFilledRiskExceeded:risk={execution.InitialRiskPoints:0.##}|max={execution.MaxAllowedRiskPoints:0.##}|planned={execution.PlannedRiskPoints:0.##}";
            LogExecutionInfo($"EXEC_ENTRY_FILLED_RISK_EXCEEDED trade={execution.TradeId} {reason}");
            AppendExecutionEvent(execution, "ENTRY_FILLED_RISK_EXCEEDED", "ENTRY", execution.EntryAvgPrice, qty, reason);
            execution.EmergencyFlattenSubmitted = true;
            await SubmitEmergencyFlattenAsync(qty, reason);
            return;
            }
        }

        execution.OcoGroup = $"OPF-{execution.TradeId}";
        var exitDirection = execution.Side == TradeSide.Long ? OrderDirections.Sell : OrderDirections.Buy;
        var sl = new Order
        {
            Portfolio = Portfolio,
            Security = Security,
            Type = OrderTypes.Stop,
            Direction = exitDirection,
            TriggerPriceType = ReplayStopTriggerPriceType,
            TriggerPrice = execution.Stop,
            QuantityToFill = qty,
            TimeInForce = ReplayTimeInForce,
            OCOGroup = execution.OcoGroup,
            Comment = $"OPF|{execution.TradeId}|SL|{execution.SignalId}",
            AutoCancel = false
        };
        var tp = new Order
        {
            Portfolio = Portfolio,
            Security = Security,
            Type = OrderTypes.Limit,
            Direction = exitDirection,
            Price = execution.Target,
            QuantityToFill = qty,
            TimeInForce = ReplayTimeInForce,
            OCOGroup = execution.OcoGroup,
            Comment = $"OPF|{execution.TradeId}|TP|{execution.SignalId}",
            AutoCancel = false
        };

        execution.StopOrder = sl;
        execution.TargetOrder = tp;
        execution.BracketQty = qty;
        await OpenOrderAsync(sl);
        LogExecutionInfo($"EXEC_SL_SENT trade={execution.TradeId} stop={execution.Stop:0.########} qty={qty}");
        AppendExecutionEvent(execution, "SL_SENT", "SL", execution.Stop, qty, execution.OcoGroup ?? string.Empty);
        if (IsDoneOrder(sl))
        {
            LogExecutionInfo($"EXEC_TP_SUPPRESSED_AFTER_IMMEDIATE_SL trade={execution.TradeId} state={sl.State} unfilled={sl.Unfilled:0.########}");
            AppendExecutionEvent(execution, "TP_SUPPRESSED", "TP", execution.Target, qty, $"slState={sl.State}|slUnfilled={sl.Unfilled:0.########}");
            execution.BracketSubmitted = true;
            ScheduleProtectionLossCheckIfNeeded("BracketSubmittedAfterImmediateSL");
            return;
        }
        await OpenOrderAsync(tp);
        LogExecutionInfo($"EXEC_TP_SENT trade={execution.TradeId} target={execution.Target:0.########} qty={qty}");
        AppendExecutionEvent(execution, "TP_SENT", "TP", execution.Target, qty, execution.OcoGroup ?? string.Empty);
        if (IsDoneOrder(tp))
        {
            LogExecutionInfo($"EXEC_SL_CLEANUP_AFTER_IMMEDIATE_TP trade={execution.TradeId} state={tp.State} unfilled={tp.Unfilled:0.########}");
            AppendExecutionEvent(execution, "SL_CLEANUP_AFTER_IMMEDIATE_TP", "SL", execution.Stop, qty, $"tpState={tp.State}|tpUnfilled={tp.Unfilled:0.########}");
            MarkProtectionCleanupPending(execution, "ImmediateTP");
            await CleanupProtectionOrdersAsync(execution, "ImmediateTP");
        }
        execution.BracketSubmitted = true;
        ScheduleProtectionLossCheckIfNeeded("BracketSubmitted");
    }

    private static bool IsEntryFillRiskDriftAccepted(ReplayExecutionState execution)
    {
        if (IsBreakawayPath(execution.ResearchPath))
            return false;
        if (IsObservationConfirmPath(execution.ResearchPath) && execution.Side == TradeSide.Long)
            return false;

        return execution.PlannedRiskPoints <= execution.MaxAllowedRiskPoints &&
            execution.InitialRiskPoints <= execution.MaxAllowedRiskPoints + EntryFillRiskDriftTolerancePoints;
    }

    private void RepriceExecutionBracketFromFill(ReplayExecutionState execution)
    {
        if (_snapshot is null)
            return;

        var entry = execution.EntryAvgPrice > 0m ? execution.EntryAvgPrice : execution.CreatedPrice;
        var minRisk = Math.Max(_snapshot.InstrumentProfile.MinStopPoints, _snapshot.InstrumentProfile.TickSize);
        var stop = execution.Stop;
        if (execution.Side == TradeSide.Long)
        {
            if (stop >= entry || entry - stop < minRisk)
                stop = entry - minRisk;
        }
        else
        {
            if (stop <= entry || stop - entry < minRisk)
                stop = entry + minRisk;
        }

        stop = AlignToTick(stop, _snapshot.InstrumentProfile.TickSize);
        var risk = Math.Abs(entry - stop);
        var oldTargetR = execution.TargetR;
        execution.TargetR = ActualTargetRFor(execution.Side, execution.ResearchPath, risk);
        var target = execution.Side == TradeSide.Long
            ? entry + risk * execution.TargetR
            : entry - risk * execution.TargetR;

        execution.Stop = stop;
        execution.InitialRiskPoints = risk;
        execution.Target = AlignToTick(target, _snapshot.InstrumentProfile.TickSize);
        var targetRReason = oldTargetR == execution.TargetR ? string.Empty : $" targetRChanged={oldTargetR:0.##}->{execution.TargetR:0.##}";
        LogExecutionInfo($"EXEC_BRACKET_REPRICE trade={execution.TradeId} fillEntry={entry:0.########} stop={execution.Stop:0.########} target={execution.Target:0.########} risk={risk:0.########} targetR={execution.TargetR:0.##}{targetRReason}");
    }

    private void EnqueueExecutionAction(string tag, Func<Task> action)
    {
        _ = ExecuteExecutionActionAsync(tag, action);
    }

    private async Task ExecuteExecutionActionAsync(string tag, Func<Task> action)
    {
        await _executionLock.WaitAsync();
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            LogExecutionInfo($"EXEC_ACTION_FAIL tag={tag} err={ex.GetType().Name}:{ex.Message}");
        }
        finally
        {
            _executionLock.Release();
        }
    }

    private void ScheduleProtectionCleanupIfNeeded(string reason)
    {
        var execution = _replayExecution;
        if (!NeedsProtectionCleanup(execution))
            return;

        EnqueueExecutionAction($"ProtectionCleanup:{reason}", async () =>
        {
            if (NeedsProtectionCleanup(execution))
                await CleanupProtectionOrdersAsync(execution!, reason);
        });
    }

    private void ScheduleStaleUnfilledEntryAbortIfNeeded(OpfCandle candle)
    {
        var execution = _replayExecution;
        if (!IsStaleUnfilledEntry(execution, candle.Bar))
            return;

        execution!.EntryAbortPending = true;
        var barsSinceSend = candle.Bar - execution.CreatedBar;
        EnqueueExecutionAction("AbortStaleUnfilledEntry", async () =>
        {
            if (execution.EntryFilledQty > 0m || execution.ExitCompleted)
                return;

            var entryStateText = execution.EntryOrder?.State.ToString() ?? "-";
            var entryUnfilledText = execution.EntryOrder is null ? "-" : execution.EntryOrder.Unfilled.ToString("0.########");
            var reason = $"bars={barsSinceSend}|entryState={entryStateText}|entryUnfilled={entryUnfilledText}";
            LogExecutionInfo($"EXEC_ENTRY_STALE_NO_FILL trade={execution.TradeId} {reason}");
            AppendExecutionEvent(execution, "ENTRY_STALE_NO_FILL", "ENTRY", execution.CreatedPrice, execution.Quantity, reason);
            await TryCancelExecutionOrderAsync(execution.EntryOrder, "StaleNoEntryFill");

            execution.ExitCompleted = true;
            execution.ExitBar = candle.Bar;
            execution.ExitPrice = execution.CreatedPrice;
            execution.ExitRole = "NO_ENTRY_FILL";
            AppendExecutionEvent(execution, "EXIT_ABORTED_NO_ENTRY", "NO_ENTRY_FILL", execution.CreatedPrice, 0m, "StaleUnfilledEntry");
            MarkProtectionCleanupPending(execution, "StaleNoEntryFill");
            await CleanupProtectionOrdersAsync(execution, "StaleNoEntryFill");
        });
    }

    private void SchedulePartialEntryFinalizeIfNeeded(ReplayExecutionState execution)
    {
        if (execution.PartialEntryFinalizeScheduled)
            return;

        execution.PartialEntryFinalizeScheduled = true;
        _ = FinalizePartialEntryAfterDelayAsync(execution);
    }

    private async Task FinalizePartialEntryAfterDelayAsync(ReplayExecutionState execution)
    {
        await Task.Delay(PartialEntryFinalizeDelayMs);
        EnqueueExecutionAction("FinalizePartialEntry", async () =>
        {
            execution.PartialEntryFinalizeScheduled = false;
            if (!ReferenceEquals(_replayExecution, execution) ||
                execution.ExitCompleted ||
                execution.BracketSubmitted ||
                execution.EntryAbortPending ||
                execution.EntryFilledQty <= 0m)
            {
                return;
            }

            if (execution.EntryFilledQty + 0.0000001m >= execution.Quantity)
            {
                await SubmitReplayBracketAsync(execution);
                return;
            }

            execution.EntryAbortPending = true;
            var filledQty = execution.EntryFilledQty;
            var reason = $"PartialEntryFillTimeout:filled={filledQty:0.########}|requested={execution.Quantity:0.########}|delayMs={PartialEntryFinalizeDelayMs}";
            LogExecutionInfo($"EXEC_ENTRY_PARTIAL_FILL_ABORT trade={execution.TradeId} {reason}");
            AppendExecutionEvent(execution, "ENTRY_PARTIAL_FILL_ABORT", "ENTRY", execution.EntryAvgPrice, filledQty, reason);
            await TryCancelExecutionOrderAsync(execution.EntryOrder, "PartialEntryFillAbort");

            execution.BracketQty = filledQty;
            execution.EmergencyFlattenSubmitted = true;
            execution.EmergencyFlattenSubmittedQty += filledQty;
            await SubmitEmergencyFlattenAsync(filledQty, reason);
        });
    }

    private void ScheduleReplayStopExitIfNeeded(OpfCandle candle)
    {
        var execution = _replayExecution;
        if (!EnableReplayOrders ||
            !IsReplayStopGuardWindow(candle.Time) ||
            execution is null ||
            execution.ExitCompleted ||
            execution.ReplayStopExitPending ||
            execution.EntryFilledQty <= 0m)
        {
            return;
        }

        execution.ReplayStopExitPending = true;
        EnqueueExecutionAction("ReplayStopExit", async () =>
        {
            if (!ReferenceEquals(_replayExecution, execution) || execution.ExitCompleted)
                return;

            var reason = $"guard={candle.Time:HH:mm}|remaining={RemainingExecutionQuantity(execution):0.########}";
            LogExecutionInfo($"EXEC_REPLAY_STOP_EXIT_PENDING trade={execution.TradeId} {reason}");
            AppendExecutionEvent(execution, "REPLAY_STOP_EXIT_PENDING", ReplayStopExitRole, candle.Close, RemainingExecutionQuantity(execution), reason);

            await TryCancelExecutionOrderAsync(execution.EntryOrder, "ReplayStopExit:Entry");
            await TryCancelExecutionOrderAsync(execution.StopOrder, "ReplayStopExit:SL");
            await TryCancelExecutionOrderAsync(execution.TargetOrder, "ReplayStopExit:TP");

            if (execution.ExitCompleted)
                return;

            var position = CurrentPosition;
            if (position == 0m)
            {
                execution.ReplayStopExitPending = false;
                LogExecutionInfo($"EXEC_REPLAY_STOP_POSITION_FLAT trade={execution.TradeId} {reason}");
                AppendExecutionEvent(execution, "REPLAY_STOP_POSITION_FLAT", ReplayStopExitRole, candle.Close, 0m, reason);
                return;
            }

            if (Portfolio is null || Security is null)
            {
                execution.ReplayStopExitPending = false;
                return;
            }

            var qty = Math.Abs(position);
            var direction = position > 0m ? OrderDirections.Sell : OrderDirections.Buy;
            var order = new Order
            {
                Portfolio = Portfolio,
                Security = Security,
                Type = OrderTypes.Market,
                Direction = direction,
                QuantityToFill = qty,
                TimeInForce = ReplayTimeInForce,
                Comment = $"OPF|{execution.TradeId}|{ReplayStopExitRole}|{reason}",
                AutoCancel = false
            };

            LogExecutionInfo($"EXEC_REPLAY_STOP_FLATTEN_SEND trade={execution.TradeId} dir={direction} qty={qty:0.########} {reason}");
            AppendExecutionEvent(execution, "REPLAY_STOP_FLATTEN_SEND", ReplayStopExitRole, candle.Close, qty, $"dir={direction}|{reason}");
            await OpenOrderAsync(order);
        });
    }

    private static bool IsStaleUnfilledEntry(ReplayExecutionState? execution, int currentBar)
    {
        if (execution is null ||
            execution.ExitCompleted ||
            execution.EntryAbortPending ||
            execution.EntryFilledQty > 0m)
        {
            return false;
        }

        var entryState = execution.EntryOrder?.State.ToString();
        if (!string.IsNullOrWhiteSpace(entryState) && IsInactiveOrderState(entryState))
            return true;

        return currentBar - execution.CreatedBar >= StaleUnfilledEntryMaxBars;
    }

    private void ScheduleOrphanPositionFlattenIfNeeded(OpfCandle candle)
    {
        if (!EnableReplayOrders || Portfolio is null || Security is null)
            return;

        var position = CurrentPosition;
        if (position == 0m)
        {
            _orphanPositionFlattenPending = false;
            return;
        }

        if (IsReplayExecutionActive(_replayExecution))
            return;

        if (_orphanPositionFlattenPending)
            return;

        _orphanPositionFlattenPending = true;
        _orphanPositionFlattenLastBar = candle.Bar;
        EnqueueExecutionAction("FlattenOrphanPosition", async () =>
        {
            var latestPosition = CurrentPosition;
            if (latestPosition == 0m || IsReplayExecutionActive(_replayExecution) || Portfolio is null || Security is null)
                return;

            var qty = Math.Abs(latestPosition);
            var direction = latestPosition > 0m ? OrderDirections.Sell : OrderDirections.Buy;
            var reason = $"pos={latestPosition:0.########}|bar={candle.Bar}";
            LogExecutionInfo($"EXEC_ORPHAN_POSITION_DETECTED {reason}");
            AppendStandaloneExecutionEvent("ORPHAN_POSITION_DETECTED", "FLATTEN", candle, candle.Close, qty, reason);

            var order = new Order
            {
                Portfolio = Portfolio,
                Security = Security,
                Type = OrderTypes.Market,
                Direction = direction,
                QuantityToFill = qty,
                TimeInForce = ReplayTimeInForce,
                Comment = $"OPF|ORPHAN-POSITION|FLATTEN|{reason}",
                AutoCancel = false
            };

            AppendStandaloneExecutionEvent("ORPHAN_FLATTEN_SEND", "FLATTEN", candle, candle.Close, qty, $"dir={direction}|{reason}");
            await OpenOrderAsync(order);
        });
    }

    private void ScheduleProtectionLossCheckIfNeeded(string reason)
    {
        var execution = _replayExecution;
        if (!IsUnprotectedOpenExecution(execution))
            return;

        EnqueueExecutionAction($"ProtectionLoss:{reason}", async () =>
        {
            await Task.Delay(250);
            if (!IsUnprotectedOpenExecution(execution))
                return;

            execution!.EmergencyFlattenSubmitted = true;
            var remainingQty = RemainingExecutionQuantity(execution);
            LogExecutionInfo($"EXEC_PROTECTION_LOST_BEFORE_EXIT trade={execution.TradeId} reason={reason} remaining={remainingQty:0.########}");
            AppendExecutionEvent(execution, "PROTECTION_LOST_BEFORE_EXIT", "-", 0m, remainingQty, reason);
            await SubmitEmergencyFlattenAsync(remainingQty, $"ProtectionLost:{reason}");
        });
    }

    private static bool NeedsProtectionCleanup(ReplayExecutionState? execution)
    {
        return execution is not null && execution.ProtectionCleanupPending && !execution.ProtectionCleanupDone && !execution.ProtectionCleanupInProgress;
    }

    private static bool IsUnprotectedOpenExecution(ReplayExecutionState? execution)
    {
        return execution is not null &&
            execution.BracketSubmitted &&
            !execution.ExitCompleted &&
            !execution.ReplayStopExitPending &&
            !execution.EmergencyFlattenSubmitted &&
            RemainingExecutionQuantity(execution) > 0m &&
            !IsWorkingExecutionOrder(execution.StopOrder) &&
            !IsWorkingExecutionOrder(execution.TargetOrder);
    }

    private static decimal RemainingExecutionQuantity(ReplayExecutionState execution)
    {
        if (execution.EntryFilledQty <= 0m)
            return 0m;
        return Math.Max(0m, execution.EntryFilledQty - execution.ExitFilledQty);
    }

    private void MarkProtectionCleanupPending(ReplayExecutionState execution, string reason)
    {
        if (!execution.ProtectionCleanupPending)
        {
            LogExecutionInfo($"EXEC_PROTECTION_CLEANUP_PENDING trade={execution.TradeId} reason={reason}");
            AppendExecutionEvent(execution, "PROTECTION_CLEANUP_PENDING", "-", 0m, 0m, reason);
        }

        execution.ProtectionCleanupPending = true;
        execution.ProtectionCleanupReason = reason;
    }

    private async Task CleanupProtectionOrdersAsync(ReplayExecutionState execution, string reason)
    {
        if (execution.ProtectionCleanupDone || execution.ProtectionCleanupInProgress)
            return;

        execution.ProtectionCleanupInProgress = true;
        var workingBefore = CountWorkingProtectionOrders(execution);
        try
        {
            if (workingBefore == 0)
            {
                CompleteProtectionCleanup(execution, reason);
                return;
            }

            execution.ProtectionCleanupAttempts++;
            execution.LastProtectionCleanupBar = _lastResearchCandle?.Bar ?? _lastSeenBar;
            LogExecutionInfo($"EXEC_PROTECTION_CLEANUP_CANCEL_SENT trade={execution.TradeId} reason={reason} working={workingBefore} attempt={execution.ProtectionCleanupAttempts}");
            AppendExecutionEvent(execution, "PROTECTION_CLEANUP_CANCEL_SENT", "-", 0m, workingBefore, $"reason={reason}|attempt={execution.ProtectionCleanupAttempts}");

            await TryCancelExecutionOrderAsync(execution.EntryOrder, $"ProtectionCleanup:{reason}:Entry");
            await TryCancelExecutionOrderAsync(execution.StopOrder, $"ProtectionCleanup:{reason}:SL");
            await TryCancelExecutionOrderAsync(execution.TargetOrder, $"ProtectionCleanup:{reason}:TP");

            await Task.Delay(250);
            var workingAfter = CountWorkingProtectionOrders(execution);
            if (workingAfter == 0)
                CompleteProtectionCleanup(execution, reason);
            else if (execution.ProtectionCleanupAttempts == 1)
            {
                LogExecutionInfo($"EXEC_PROTECTION_CLEANUP_RETRY_PENDING trade={execution.TradeId} reason={reason} working={workingAfter} attempt={execution.ProtectionCleanupAttempts}");
                AppendExecutionEvent(execution, "PROTECTION_CLEANUP_RETRY_PENDING", "-", 0m, workingAfter, $"reason={reason}|attempt={execution.ProtectionCleanupAttempts}");
            }
            else
            {
                LogExecutionInfo($"EXEC_PROTECTION_STALE_AFTER_EXIT trade={execution.TradeId} reason={reason} working={workingAfter} attempt={execution.ProtectionCleanupAttempts}");
                AppendExecutionEvent(execution, "PROTECTION_STALE_AFTER_EXIT", "-", 0m, workingAfter, $"reason={reason}|attempt={execution.ProtectionCleanupAttempts}");
            }
        }
        finally
        {
            execution.ProtectionCleanupInProgress = false;
        }
    }

    private void CompleteProtectionCleanup(ReplayExecutionState execution, string reason)
    {
        execution.ProtectionCleanupDone = true;
        execution.ProtectionCleanupPending = false;
        LogExecutionInfo($"EXEC_PROTECTION_CLEANUP_DONE trade={execution.TradeId} reason={reason}");
        AppendExecutionEvent(execution, "PROTECTION_CLEANUP_DONE", "-", 0m, 0m, reason);
    }

    private async Task TryCancelExecutionOrderAsync(Order? order, string reason)
    {
        if (!IsWorkingExecutionOrder(order))
            return;

        var workingOrder = order!;
        try
        {
            await CancelOrderAsync(workingOrder);
            LogExecutionInfo($"EXEC_CANCEL_SENT reason={reason} ext={workingOrder.ExtId}");
            if (_replayExecution is not null)
                AppendExecutionEvent(_replayExecution, "CANCEL_SENT", ParseExecutionRole(workingOrder.Comment), workingOrder.Price > 0m ? workingOrder.Price : workingOrder.TriggerPrice, workingOrder.QuantityToFill, reason);
        }
        catch (Exception ex)
        {
            if (IsExpectedOcoCancelAlreadyInactive(workingOrder, ex.Message))
            {
                LogExecutionInfo($"EXEC_CANCEL_ALREADY_INACTIVE reason={reason} ext={workingOrder.ExtId} msg={ex.Message}");
                if (_replayExecution is not null)
                    AppendExecutionEvent(_replayExecution, "CANCEL_ALREADY_INACTIVE", ParseExecutionRole(workingOrder.Comment), workingOrder.Price > 0m ? workingOrder.Price : workingOrder.TriggerPrice, workingOrder.QuantityToFill, $"reason={reason}|msg={ex.Message}");
                return;
            }

            LogExecutionInfo($"EXEC_CANCEL_FAIL reason={reason} ext={workingOrder.ExtId} err={ex.GetType().Name}:{ex.Message}");
            if (_replayExecution is not null)
                AppendExecutionEvent(_replayExecution, "CANCEL_FAIL", ParseExecutionRole(workingOrder.Comment), workingOrder.Price > 0m ? workingOrder.Price : workingOrder.TriggerPrice, workingOrder.QuantityToFill, $"reason={reason}|err={ex.GetType().Name}:{ex.Message}");
        }
    }

    private async Task SubmitDuplicateExitFlattenIfNeededAsync(ReplayExecutionState execution, decimal fillQty, string role)
    {
        if (execution.DuplicateExitFlattenSubmitted)
        {
            AppendExecutionEvent(execution, "DUPLICATE_EXIT_FLATTEN_SUPPRESSED", role, 0m, fillQty, "alreadySubmitted");
            return;
        }

        var position = CurrentPosition;
        var qty = Math.Abs(position);
        if (qty <= 0m)
        {
            AppendExecutionEvent(execution, "DUPLICATE_EXIT_FLATTEN_SUPPRESSED", role, 0m, fillQty, "positionFlat");
            return;
        }

        execution.DuplicateExitFlattenSubmitted = true;
        var direction = position > 0m ? OrderDirections.Sell : OrderDirections.Buy;
        await SubmitEmergencyFlattenAsync(qty, $"DuplicateExit:{role}|pos={position:0.########}", direction);
    }

    private async Task SubmitEmergencyFlattenAsync(decimal quantity, string reason, OrderDirections? directionOverride = null)
    {
        if (_replayExecution is null || Portfolio is null || Security is null)
            return;

        var qty = quantity > 0m ? quantity : _replayExecution.Quantity;
        if (qty <= 0m)
            return;

        var direction = directionOverride ?? (_replayExecution.Side == TradeSide.Long ? OrderDirections.Sell : OrderDirections.Buy);
        var order = new Order
        {
            Portfolio = Portfolio,
            Security = Security,
            Type = OrderTypes.Market,
            Direction = direction,
            QuantityToFill = qty,
            TimeInForce = ReplayTimeInForce,
            Comment = $"OPF|{_replayExecution.TradeId}|FLATTEN|{reason}",
            AutoCancel = false
        };

        LogExecutionInfo($"EXEC_EMERGENCY_FLATTEN_SEND trade={_replayExecution.TradeId} reason={reason} dir={direction} qty={qty:0.########}");
        AppendExecutionEvent(_replayExecution, "EMERGENCY_FLATTEN_SEND", "FLATTEN", 0m, qty, reason);
        await OpenOrderAsync(order);
    }

    private static OrderDirections OppositeDirection(OrderDirections direction)
    {
        return direction == OrderDirections.Buy ? OrderDirections.Sell : OrderDirections.Buy;
    }

    private void LogExecutionInfo(string message)
    {
        if (_snapshot is null || _lastResearchCandle is null)
            return;
        _researchLogger?.AppendInfo(_snapshot.SnapshotId, _lastResearchCandle.Bar, _lastResearchCandle.Time, message);
    }

    private void AppendExecutionEvent(CandidateSignal signal, string tradeId, OpfCandle candle, string eventName, string role, string researchPath, decimal price, decimal quantity, string message)
    {
        if (_snapshot is null)
            return;

        _researchLogger?.AppendExecutionEvent(
            _snapshot.SnapshotId,
            signal.SignalId,
            tradeId,
            candle.Time,
            candle.Bar,
            eventName,
            role,
            signal.Side.ToString(),
            researchPath,
            price,
            quantity,
            message);
    }

    private void AppendStandaloneExecutionEvent(string eventName, string role, OpfCandle candle, decimal price, decimal quantity, string message)
    {
        if (_snapshot is null)
            return;

        _researchLogger?.AppendExecutionEvent(
            _snapshot.SnapshotId,
            string.Empty,
            "ORPHAN-POSITION",
            candle.Time,
            candle.Bar,
            eventName,
            role,
            "-",
            "-",
            price,
            quantity,
            message);
    }

    private void AppendOrderFailureEvent(Order? order, string eventName, string message)
    {
        if (order is null || !TryParseExecutionComment(order.Comment, out var tradeId, out var role))
            return;

        var execution = FindReplayExecution(tradeId);
        if (execution is null)
            return;

        var price = order.Price > 0m ? order.Price : order.TriggerPrice;
        AppendExecutionEvent(execution, eventName, role, price, order.QuantityToFill, $"ext={order.ExtId}|msg={message}");
    }

    private void AppendExecutionTrade(ReplayExecutionState execution, MyTrade exitTrade, string exitRole, ExecutionFillValidation validation)
    {
        AppendExecutionTrade(execution, exitTrade.Price, exitRole, validation);
    }

    private void AppendExecutionTrade(ReplayExecutionState execution, decimal exit, string exitRole, ExecutionFillValidation? validation = null)
    {
        if (_snapshot is null || _lastResearchCandle is null)
            return;
        if (!_writtenExecutionTradeIds.Add(execution.TradeId))
        {
            LogExecutionInfo($"EXEC_TRADE_DUPLICATE_SUPPRESSED trade={execution.TradeId} role={exitRole} exit={exit:0.########}");
            AppendExecutionEvent(execution, "TRADE_DUPLICATE_SUPPRESSED", exitRole, exit, 0m, "ExecutionTradeAlreadyWritten");
            return;
        }

        var entry = execution.EntryAvgPrice > 0m ? execution.EntryAvgPrice : execution.CreatedPrice;
        var rawPoints = execution.Side == TradeSide.Long
            ? exit - entry
            : entry - exit;
        var rawPointsR = execution.InitialRiskPoints <= 0m ? 0m : Math.Round(rawPoints / execution.InitialRiskPoints, 4);
        var rawDollars = Math.Round(rawPoints * _snapshot.InstrumentProfile.PointValue * execution.Quantity, 2);
        validation ??= new ExecutionFillValidation(false, string.Empty, ExpectedExitPrice(execution, exitRole), 0m, rawDollars, rawPoints, rawPointsR, rawPoints, rawDollars, rawPointsR);
        var points = validation.NormalPoints;
        var pointsR = validation.NormalPointsR;
        var dollars = validation.NormalDollars;
        var pointValue = _snapshot.InstrumentProfile.PointValue;
        var riskDollars = Math.Round(execution.InitialRiskPoints * pointValue * execution.Quantity, 2);
        var targetDollars = Math.Round(execution.InitialRiskPoints * execution.TargetR * pointValue * execution.Quantity, 2);
        execution.UpdateActualExcursion(exit);
        UpdateExecutionHudStats(execution, validation.IsAbnormal ? "ABNORMAL" : exitRole, dollars, pointsR);

        _researchLogger?.AppendExecutionTrade(
            _snapshot.SnapshotId,
            execution.SignalId,
            execution.TradeId,
            execution.CreatedTime,
            execution.CreatedBar,
            _lastResearchCandle.Time,
            _lastResearchCandle.Bar,
            execution.Side.ToString(),
            execution.ResearchPath,
            execution.Quantity,
            entry,
            exit,
            execution.Stop,
            execution.Target,
            execution.InitialRiskPoints,
            execution.TargetR,
            execution.PlannedTargetR,
            Math.Round(execution.TargetR - execution.PlannedTargetR, 4),
            pointsR,
            exitRole,
            points,
            dollars,
            riskDollars,
            targetDollars,
            execution.ActualMfePoints,
            execution.ActualMaePoints,
            execution.ActualMfeR,
            execution.ActualMaeR,
            execution.PlannedRiskPoints,
            execution.InitialRiskPoints,
            Math.Round(execution.InitialRiskPoints - execution.PlannedRiskPoints, 4),
            _replayDailyPnlDollars,
            _replayTradesToday,
            _replayConsecutiveLossesToday,
            validation.IsAbnormal,
            validation.Reason,
            validation.ExpectedExitPrice,
            validation.ExitPriceDriftPoints,
            validation.RawPoints,
            validation.RawDollars,
            validation.RawPointsR);

        var actualOutcomeRole = validation.IsAbnormal ? $"ABNORMAL_{exitRole}" : exitRole;
        _actualTradeOutcomesBySignalPath[ActualOutcomeKey(execution.SignalId, execution.ResearchPath)] = new ActualTradeOutcome(
            execution.TradeId,
            _lastResearchCandle.Time,
            _lastResearchCandle.Bar,
            exit,
            actualOutcomeRole,
            points,
            dollars,
            pointsR,
            execution.ActualMfePoints,
            execution.ActualMaePoints,
            execution.ActualMfeR,
            execution.ActualMaeR);
    }

    private void FinalizeActiveExecutionOnStop()
    {
        var execution = _replayExecution;
        if (execution is null || _lastResearchCandle is null || execution.ExitCompleted)
            return;

        var remainingQty = RemainingExecutionQuantity(execution);
        LogExecutionInfo($"EXEC_ACTIVE_ON_STOP trade={execution.TradeId} remaining={remainingQty:0.########}");
        AppendExecutionEvent(execution, "ACTIVE_ON_STOP", "-", _lastResearchCandle.Close, remainingQty, "StrategyStopped");

        if (execution.EntryFilledQty <= 0m)
        {
            AppendExecutionEvent(execution, "EXIT_ABORTED_NO_ENTRY", "STOPPED_NO_ENTRY", execution.CreatedPrice, 0m, "StrategyStoppedBeforeEntryFill");

            execution.ExitCompleted = true;
            execution.ExitBar = _lastResearchCandle.Bar;
            execution.ExitPrice = execution.CreatedPrice;
            execution.ExitRole = "STOPPED_NO_ENTRY";
            MarkProtectionCleanupPending(execution, "StrategyStoppedNoEntryFill");

            try
            {
                CleanupProtectionOrdersOnStop(execution, "StrategyStoppedNoEntryFill");
            }
            catch (Exception ex)
            {
                LogExecutionInfo($"EXEC_STOP_CLEANUP_FAIL trade={execution.TradeId} err={ex.GetType().Name}:{ex.Message}");
                AppendExecutionEvent(execution, "STOP_CLEANUP_FAIL", "-", 0m, CountWorkingProtectionOrders(execution), ex.Message);
            }
            return;
        }

        if (remainingQty > 0m)
        {
            try
            {
                SubmitEmergencyFlattenAsync(remainingQty, "StrategyStopped").GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                LogExecutionInfo($"EXEC_STOP_FLATTEN_FAIL trade={execution.TradeId} err={ex.GetType().Name}:{ex.Message}");
                AppendExecutionEvent(execution, "STOP_FLATTEN_FAIL", "FLATTEN", _lastResearchCandle.Close, remainingQty, ex.Message);
            }
        }

        var dollars = CalculateExecutionDollars(execution, _lastResearchCandle.Close);
        UpdateReplayExecutionDailyResult(execution.CreatedTime.Date, dollars, "STOPPED");
        AppendExecutionEvent(execution, "EXIT_FILLED", "STOPPED", _lastResearchCandle.Close, remainingQty, "StrategyStoppedSynthetic");
        AppendExecutionTrade(execution, _lastResearchCandle.Close, "STOPPED");
        TryWriteActualResearchOutcomeOnExit(execution, "STOPPED");

        execution.ExitCompleted = true;
        execution.ExitBar = _lastResearchCandle.Bar;
        execution.ExitPrice = _lastResearchCandle.Close;
        execution.ExitRole = "STOPPED";
        MarkProtectionCleanupPending(execution, "StrategyStopped");

        try
        {
            CleanupProtectionOrdersOnStop(execution, "StrategyStopped");
        }
        catch (Exception ex)
        {
            LogExecutionInfo($"EXEC_STOP_CLEANUP_FAIL trade={execution.TradeId} err={ex.GetType().Name}:{ex.Message}");
            AppendExecutionEvent(execution, "STOP_CLEANUP_FAIL", "-", 0m, CountWorkingProtectionOrders(execution), ex.Message);
        }
    }

    private void TryWriteActualResearchOutcomeOnExit(ReplayExecutionState execution, string exitRole)
    {
        if (_snapshot is null || _lastResearchCandle is null)
            return;

        var key = ActualOutcomeKey(execution.SignalId, execution.ResearchPath);
        for (var i = _researchTrackers.Count - 1; i >= 0; i--)
        {
            var tracker = _researchTrackers[i];
            if (!string.Equals(ActualOutcomeKey(tracker.Signal.SignalId, tracker.ResearchPath), key, StringComparison.Ordinal))
                continue;

            tracker.Update(_lastResearchCandle);
            var exitBar = _lastResearchCandle.Bar;
            if (!tracker.IsComplete(exitBar))
                return;

            if (!TryMarkResearchOutcomeWritten(tracker, exitBar))
            {
                _researchTrackers.RemoveAt(i);
                return;
            }

            AppendExitPolicyEvaluations(tracker, _lastResearchCandle);
            _researchLogger?.AppendResearchOutcome(
                _snapshot.SnapshotId,
                tracker.Signal.SignalId,
                tracker.EntryTime,
                tracker.EntryBar,
                _lastResearchCandle.Time,
                exitBar,
                tracker.Signal.Side.ToString(),
                tracker.Entry,
                tracker.Stop,
                tracker.InitialRiskPoints,
                tracker.MfePoints,
                tracker.MaePoints,
                tracker.MfeR,
                tracker.MaeR,
                tracker.Hit1R,
                tracker.Hit1_5R,
                tracker.Hit2R,
                tracker.Hit2_5R,
                tracker.Hit3R,
                Math.Max(0, exitBar - tracker.EntryBar),
                tracker.ResearchPath,
                tracker.FirstStopBar,
                tracker.First1RBar,
                tracker.First1_5RBar,
                tracker.First2RBar,
                tracker.First2_5RBar,
                tracker.First3RBar,
                tracker.StopHitBefore1R,
                tracker.AmbiguousStopAndTargetSameBar,
                tracker.TimeTo1RMinutes,
                tracker.TimeToMfeMinutes,
                tracker.MaxHeatBefore1R,
                tracker.StopBasis,
                tracker.EntryDelayBars,
                tracker.OutcomeClass,
                $"ActualExit:{exitRole}",
                ActualOutcomeFor(tracker));
            AppendEdgeAttributionOutcome(tracker, _lastResearchCandle, $"ActualExit:{exitRole}");
            _researchTrackers.RemoveAt(i);
            _researchOutcomeCount++;
            return;
        }
    }

    private void AppendExecutionEvent(ReplayExecutionState execution, string eventName, string role, decimal price, decimal quantity, string message)
    {
        if (_snapshot is null || _lastResearchCandle is null)
            return;

        _researchLogger?.AppendExecutionEvent(
            _snapshot.SnapshotId,
            execution.SignalId,
            execution.TradeId,
            _lastResearchCandle.Time,
            _lastResearchCandle.Bar,
            eventName,
            role,
            execution.Side.ToString(),
            execution.ResearchPath,
            price,
            quantity,
            message);
    }

    private static bool TryParseExecutionComment(string? comment, out string tradeId, out string role)
    {
        tradeId = string.Empty;
        role = string.Empty;
        if (string.IsNullOrWhiteSpace(comment))
            return false;

        var parts = comment.Split('|');
        if (parts.Length < 3 || parts[0] != "OPF")
            return false;

        tradeId = parts[1];
        role = parts[2];
        return true;
    }

    private bool IsActiveExecutionOrder(Order? order)
    {
        if (order is null || _replayExecution is null)
            return false;
        return TryParseExecutionComment(order.Comment, out var tradeId, out _) &&
            string.Equals(tradeId, _replayExecution.TradeId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsReplayExecutionActive(ReplayExecutionState? execution)
    {
        return execution is not null && (!execution.ExitCompleted || execution.ProtectionCleanupPending);
    }

    private static bool IsReplayStopGuardWindow(DateTime time)
    {
        var t = time.TimeOfDay;
        return t >= ReplayStopGuardStart ||
            (time.DayOfWeek == DayOfWeek.Friday && t >= FridayReplayStopGuardStart);
    }

    private static int CountWorkingProtectionOrders(ReplayExecutionState execution)
    {
        var count = 0;
        if (IsWorkingExecutionOrder(execution.EntryOrder))
            count++;
        if (IsWorkingExecutionOrder(execution.StopOrder))
            count++;
        if (IsWorkingExecutionOrder(execution.TargetOrder))
            count++;
        return count;
    }

    private static bool IsWorkingExecutionOrder(Order? order)
    {
        if (order is null)
            return false;
        if (order.Unfilled <= 0m)
            return false;

        var state = order.State.ToString();
        return !IsInactiveOrderState(state);
    }

    private static bool IsExitOrder(Order? order)
    {
        if (order is null)
            return false;
        return TryParseExecutionComment(order.Comment, out _, out var role) && role is "SL" or "TP";
    }

    private static bool IsDoneOrder(Order order)
    {
        return string.Equals(order.State.ToString(), "Done", StringComparison.OrdinalIgnoreCase) || order.Unfilled <= 0m;
    }

    private static bool IsInactiveOrderState(string state)
    {
        return state.Equals("Done", StringComparison.OrdinalIgnoreCase) ||
            state.Equals("Canceled", StringComparison.OrdinalIgnoreCase) ||
            state.Equals("Cancelled", StringComparison.OrdinalIgnoreCase) ||
            state.Equals("Rejected", StringComparison.OrdinalIgnoreCase) ||
            state.Equals("Failed", StringComparison.OrdinalIgnoreCase);
    }

    private void CleanupProtectionOrdersOnStop(ReplayExecutionState execution, string reason)
    {
        for (var attempt = 0; attempt < 3 && NeedsProtectionCleanup(execution); attempt++)
        {
            CleanupProtectionOrdersAsync(execution, reason).GetAwaiter().GetResult();
            if (NeedsProtectionCleanup(execution))
                Task.Delay(250).GetAwaiter().GetResult();
        }
    }

    private static bool IsFailedOrderState(string state)
    {
        return state.Equals("Failed", StringComparison.OrdinalIgnoreCase) ||
            state.Equals("Rejected", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsExpectedOcoSiblingInactive(Order? order)
    {
        if (order is null ||
            !string.Equals(order.State.ToString(), "Failed", StringComparison.OrdinalIgnoreCase) ||
            !IsExitOrder(order) ||
            !TryParseExecutionComment(order.Comment, out var tradeId, out _))
        {
            return false;
        }

        var execution = FindReplayExecution(tradeId);
        return execution is not null && execution.ExitCompleted && execution.ProtectionCleanupPending;
    }

    private bool IsExpectedOcoCancelAlreadyInactive(Order? order, string? message)
    {
        if (order is null ||
            string.IsNullOrWhiteSpace(message) ||
            !message.Contains("for cancel not found", StringComparison.OrdinalIgnoreCase) ||
            !IsExitOrder(order) ||
            !TryParseExecutionComment(order.Comment, out var tradeId, out _))
        {
            return false;
        }

        var execution = FindReplayExecution(tradeId);
        return execution is not null && execution.ExitCompleted && execution.ProtectionCleanupPending;
    }

    private ReplayExecutionState? FindReplayExecution(string tradeId)
    {
        if (_replayExecution is not null && string.Equals(_replayExecution.TradeId, tradeId, StringComparison.OrdinalIgnoreCase))
            return _replayExecution;

        for (var i = _recentReplayExecutions.Count - 1; i >= 0; i--)
        {
            var execution = _recentReplayExecutions[i];
            if (string.Equals(execution.TradeId, tradeId, StringComparison.OrdinalIgnoreCase))
                return execution;
        }

        return null;
    }

    private static decimal AlignToTick(decimal value, decimal tick)
    {
        if (tick <= 0m)
            return value;
        return Math.Round(value / tick, 0, MidpointRounding.AwayFromZero) * tick;
    }

    private static string ParseExecutionRole(string? comment)
    {
        return TryParseExecutionComment(comment, out _, out var role) ? role : "-";
    }

    private static bool ShouldTrackWideStopVariant(string researchPath)
    {
        return researchPath is
            "FailureReverse_ObservationInvalidated" or
            "FailureReverse_RetestFailed" or
            "ObservationConfirm" or
            "ObservationStrict_BullFresh" or
            "ObservationStrict_Other";
    }

    private RewardEstimate EstimateReward(CandidateSignal signal, string researchPath, decimal entry, decimal risk)
    {
        var nearest = EstimateNearestStructureRewardPoints(signal.Side, entry);
        if (researchPath == "TrendPullbackConfirmed")
            return new RewardEstimate(Math.Round(risk * ReplayTargetR, 2), $"TrendPullbackTargetR:{ReplayTargetR:0.##}");

        if (researchPath is "StructureConfirmShadow_ConfirmBarStop" or "StructureConfirmShadow_ConfirmBarStop_Min10" or "StructureConfirmShadow_ConfirmBarStop_Wait1" or "StructureConfirmShadow_SwingStop")
            return new RewardEstimate(Math.Round(risk * ReplayTargetR, 2), $"StructureConfirmTargetR:{ReplayTargetR:0.##}");

        if (researchPath is "BreakawayFvg" or "BreakawayFvg_Qualified" or "BreakawayRetest")
            return new RewardEstimate(Math.Round(risk * ReplayTargetR, 2), $"BreakawayTargetR:{ReplayTargetR:0.##}");

        if (researchPath == "ShadowCandidate")
            return new RewardEstimate(Math.Round(risk * ReplayTargetR, 2), $"ShadowCandidateTargetR:{ReplayTargetR:0.##}");

        if (researchPath == "FailureReverse_LongQualified")
            return new RewardEstimate(Math.Round(risk * ReplayTargetR, 2), $"FailureLongQualifiedTargetR:{ReplayTargetR:0.##}");

        if (researchPath == "FailureReverse_RetestFailed")
        {
            var planned = Math.Round(risk * 1.20m, 2);
            return nearest >= planned
                ? new RewardEstimate(nearest, "FailureRetestNearestStructure")
                : new RewardEstimate(planned, "FailureRetestMin1_2R");
        }

        if (researchPath == "ObservationStrict_BullFresh")
        {
            var planned = Math.Round(risk * 1.20m, 2);
            return nearest >= planned
                ? new RewardEstimate(nearest, "StrictBullFreshNearestStructure")
                : new RewardEstimate(planned, "StrictBullFreshMin1_2R");
        }

        if (researchPath == "ObservationConfirm")
            return new RewardEstimate(Math.Round(risk * ReplayTargetR, 2), $"ObservationConfirmTargetR:{ReplayTargetR:0.##}");

        return new RewardEstimate(nearest, "NearestStructure");
    }

    private decimal EstimateNearestStructureRewardPoints(TradeSide side, decimal entry)
    {
        if (side == TradeSide.Long)
        {
            var resistance = _recentCandles
                .Select(x => x.High)
                .Where(x => x > entry)
                .OrderBy(x => x)
                .FirstOrDefault();
            return resistance <= 0m ? 0m : Math.Round(resistance - entry, 2);
        }

        var support = _recentCandles
            .Select(x => x.Low)
            .Where(x => x < entry)
            .OrderByDescending(x => x)
            .FirstOrDefault();
        return support <= 0m ? 0m : Math.Round(entry - support, 2);
    }

    private decimal CalculateAtr14(OpfCandle current)
    {
        var candles = _recentCandles.Concat(new[] { current }).TakeLast(15).ToArray();
        if (candles.Length < 2)
            return 0m;

        var trueRanges = new List<decimal>();
        for (var i = 1; i < candles.Length; i++)
        {
            var high = candles[i].High;
            var low = candles[i].Low;
            var prevClose = candles[i - 1].Close;
            trueRanges.Add(Math.Max(high - low, Math.Max(Math.Abs(high - prevClose), Math.Abs(low - prevClose))));
        }

        return trueRanges.Count == 0 ? 0m : Math.Round(trueRanges.Average(), 4);
    }

    private static decimal MaxAllowedRiskPoints(InstrumentProfile? instrument, decimal atr14)
    {
        if (instrument is null)
            return 0m;

        var hard = instrument.MaxRiskPointsHard;
        var volatilityLimit = atr14 <= 0m || instrument.AtrRiskMultiplier <= 0m
            ? hard
            : Math.Max(MinVolAdjustedRiskPoints, atr14 * instrument.AtrRiskMultiplier);
        if (hard <= 0m)
            return Math.Round(volatilityLimit, 2);

        return Math.Round(Math.Min(hard, volatilityLimit), 2);
    }

    private sealed record RewardEstimate(decimal Points, string Model);

    private void TryStartStructureSwingStopResearch(CandidateSignal signal, OpfCandle entryCandle)
    {
        if (_snapshot is null)
            return;

        var result = TryGetStructureSwingStop(signal.Side, entryCandle);
        if (result.Stop is null)
        {
            _researchLogger?.AppendNoTrade(_snapshot.SnapshotId, signal.SignalId, entryCandle.Time, entryCandle.Bar, "StructureConfirmShadow_SwingStop", new[] { result.Reason });
            return;
        }

        var risk = Math.Abs(entryCandle.Close - result.Stop.Value);
        if (risk > MaxStructureSwingRiskPoints)
        {
            _researchLogger?.AppendNoTrade(_snapshot.SnapshotId, signal.SignalId, entryCandle.Time, entryCandle.Bar, "StructureConfirmShadow_SwingStop", new[] { "StructureSwingRiskTooWide", $"Risk={risk:0.00}", $"Max={MaxStructureSwingRiskPoints:0.00}" });
            return;
        }

        var swingSignal = signal with
        {
            SignalId = $"{signal.SignalId}-SWG",
            SkipReasons = signal.SkipReasons.Concat(new[] { "SwingStopResearch", result.Reason }).ToArray()
        };

        _researchLogger?.AppendSignal(swingSignal);
        StartResearchTracking(swingSignal, entryCandle, "StructureConfirmShadow_SwingStop", result.Stop.Value);
    }

    private void TryStartConfirmBarStopResearch(CandidateSignal signal, OpfCandle entryCandle)
    {
        if (_snapshot is null)
            return;

        var result = TryGetConfirmBarStop(signal.Side, entryCandle);
        if (result.Stop is null)
        {
            _researchLogger?.AppendNoTrade(_snapshot.SnapshotId, signal.SignalId, entryCandle.Time, entryCandle.Bar, "StructureConfirmShadow_ConfirmBarStop", new[] { result.Reason });
            return;
        }

        var risk = Math.Abs(entryCandle.Close - result.Stop.Value);
        if (risk > MaxConfirmBarRiskPoints)
        {
            _researchLogger?.AppendNoTrade(_snapshot.SnapshotId, signal.SignalId, entryCandle.Time, entryCandle.Bar, "StructureConfirmShadow_ConfirmBarStop", new[] { "ConfirmBarRiskTooWide", $"Risk={risk:0.00}", $"Max={MaxConfirmBarRiskPoints:0.00}" });
            return;
        }

        var confirmBarSignal = signal with
        {
            SignalId = $"{signal.SignalId}-CBR",
            SkipReasons = signal.SkipReasons.Concat(new[] { "ConfirmBarStopResearch", result.Reason }).ToArray()
        };

        _researchLogger?.AppendSignal(confirmBarSignal);
        StartResearchTracking(confirmBarSignal, entryCandle, "StructureConfirmShadow_ConfirmBarStop", result.Stop.Value);
    }

    private void TryStartConfirmBarMinRiskStopResearch(CandidateSignal signal, OpfCandle entryCandle)
    {
        if (_snapshot is null)
            return;

        var result = TryGetConfirmBarStop(signal.Side, entryCandle);
        if (result.Stop is null)
        {
            _researchLogger?.AppendNoTrade(_snapshot.SnapshotId, signal.SignalId, entryCandle.Time, entryCandle.Bar, "StructureConfirmShadow_ConfirmBarStop_Min10", new[] { result.Reason });
            return;
        }

        var stop = ApplyMinimumRiskStop(signal.Side, entryCandle.Close, result.Stop.Value, MinConfirmBarRiskPoints);
        var risk = Math.Abs(entryCandle.Close - stop);
        if (risk > MaxConfirmBarRiskPoints)
        {
            _researchLogger?.AppendNoTrade(_snapshot.SnapshotId, signal.SignalId, entryCandle.Time, entryCandle.Bar, "StructureConfirmShadow_ConfirmBarStop_Min10", new[] { "ConfirmBarMinRiskTooWide", $"Risk={risk:0.00}", $"Max={MaxConfirmBarRiskPoints:0.00}" });
            return;
        }

        var minRiskSignal = signal with
        {
            SignalId = $"{signal.SignalId}-CBM",
            SkipReasons = signal.SkipReasons.Concat(new[] { "ConfirmBarStopMin10Research", result.Reason }).ToArray()
        };

        _researchLogger?.AppendSignal(minRiskSignal);
        StartResearchTracking(minRiskSignal, entryCandle, "StructureConfirmShadow_ConfirmBarStop_Min10", stop);
    }

    private void TryStartConfirmBarWait1Research(CandidateSignal signal, OpfCandle confirmCandle)
    {
        if (_snapshot is null)
            return;

        var result = TryGetConfirmBarStop(signal.Side, confirmCandle);
        if (result.Stop is null)
        {
            _researchLogger?.AppendNoTrade(_snapshot.SnapshotId, signal.SignalId, confirmCandle.Time, confirmCandle.Bar, "StructureConfirmShadow_ConfirmBarStop_Wait1", new[] { result.Reason });
            return;
        }

        _pendingConfirmBarWait1s.Add(new PendingConfirmBarWait1(signal, confirmCandle, result.Stop.Value));
    }

    private (decimal? Stop, string Reason) TryGetStructureSwingStop(TradeSide side, OpfCandle entryCandle)
    {
        var swingCandles = _recentCandles.TakeLast(5).ToArray();
        if (swingCandles.Length < 3)
            return (null, "StructureSwingStopInsufficientHistory");

        const decimal buffer = 0.50m;
        if (side == TradeSide.Long)
        {
            var stop = swingCandles.Min(x => x.Low) - buffer;
            if (stop >= entryCandle.Close)
                return (null, "StructureSwingStopInvalidLong");
            return (stop, "SwingLow5");
        }

        var shortStop = swingCandles.Max(x => x.High) + buffer;
        if (shortStop <= entryCandle.Close)
            return (null, "StructureSwingStopInvalidShort");
        return (shortStop, "SwingHigh5");
    }

    private static (decimal? Stop, string Reason) TryGetConfirmBarStop(TradeSide side, OpfCandle entryCandle)
    {
        const decimal buffer = 0.50m;
        if (side == TradeSide.Long)
        {
            var stop = entryCandle.Low - buffer;
            if (stop >= entryCandle.Close)
                return (null, "ConfirmBarStopInvalidLong");
            return (stop, "ConfirmBarLow");
        }

        var shortStop = entryCandle.High + buffer;
        if (shortStop <= entryCandle.Close)
            return (null, "ConfirmBarStopInvalidShort");
        return (shortStop, "ConfirmBarHigh");
    }

    private static decimal ApplyMinimumRiskStop(TradeSide side, decimal entry, decimal stop, decimal minRisk)
    {
        var risk = Math.Abs(entry - stop);
        if (risk >= minRisk)
            return stop;

        return side == TradeSide.Long
            ? entry - minRisk
            : entry + minRisk;
    }

    private static IReadOnlyList<string> ConfirmBarWait1RejectReasons(TradeSide side, OpfCandle waitCandle, OpfCandle confirmCandle)
    {
        var reasons = new List<string>();
        if (side == TradeSide.Long)
        {
            if (waitCandle.Low < confirmCandle.Low)
                reasons.Add("Wait1BreakConfirmLow");
            if (waitCandle.Close < confirmCandle.Close)
                reasons.Add("Wait1CloseBelowConfirmClose");
        }
        else
        {
            if (waitCandle.High > confirmCandle.High)
                reasons.Add("Wait1BreakConfirmHigh");
            if (waitCandle.Close > confirmCandle.Close)
                reasons.Add("Wait1CloseAboveConfirmClose");
        }

        return reasons;
    }

    private void UpdateResearchTrackers(OpfCandle candle)
    {
        if (_snapshot is null || _researchTrackers.Count == 0)
            return;

        for (var i = _researchTrackers.Count - 1; i >= 0; i--)
        {
            var tracker = _researchTrackers[i];
            if (candle.Bar <= tracker.EntryBar)
                continue;

            tracker.Update(candle);
            if (IsTrackerWaitingForActualExit(tracker))
                continue;

            if (!tracker.IsComplete(candle.Bar))
                continue;

            if (!TryMarkResearchOutcomeWritten(tracker, candle.Bar))
            {
                _researchTrackers.RemoveAt(i);
                continue;
            }

            AppendExitPolicyEvaluations(tracker, candle);
            _researchLogger?.AppendResearchOutcome(
                _snapshot.SnapshotId,
                tracker.Signal.SignalId,
                tracker.EntryTime,
                tracker.EntryBar,
                candle.Time,
                candle.Bar,
                tracker.Signal.Side.ToString(),
                tracker.Entry,
                tracker.Stop,
                tracker.InitialRiskPoints,
                tracker.MfePoints,
                tracker.MaePoints,
                tracker.MfeR,
                tracker.MaeR,
                tracker.Hit1R,
                tracker.Hit1_5R,
                tracker.Hit2R,
                tracker.Hit2_5R,
                tracker.Hit3R,
                candle.Bar - tracker.EntryBar,
                tracker.ResearchPath,
                tracker.FirstStopBar,
                tracker.First1RBar,
                tracker.First1_5RBar,
                tracker.First2RBar,
                tracker.First2_5RBar,
                tracker.First3RBar,
                tracker.StopHitBefore1R,
                tracker.AmbiguousStopAndTargetSameBar,
                tracker.TimeTo1RMinutes,
                tracker.TimeToMfeMinutes,
                tracker.MaxHeatBefore1R,
                tracker.StopBasis,
                tracker.EntryDelayBars,
                tracker.OutcomeClass,
                "ResearchWindowEnd",
                ActualOutcomeFor(tracker));
            AppendEdgeAttributionOutcome(tracker, candle, "ResearchWindowEnd");
            _researchTrackers.RemoveAt(i);
            _researchOutcomeCount++;
        }
    }

    private void FlushResearchTrackers(string exitReason)
    {
        if (_snapshot is null || _lastResearchCandle is null || _researchTrackers.Count == 0)
            return;

        var exitCandle = _lastResearchCandle;
        for (var i = _researchTrackers.Count - 1; i >= 0; i--)
        {
            var tracker = _researchTrackers[i];
            if (!TryMarkResearchOutcomeWritten(tracker, exitCandle.Bar))
            {
                _researchTrackers.RemoveAt(i);
                continue;
            }

            AppendExitPolicyEvaluations(tracker, exitCandle);
            _researchLogger?.AppendResearchOutcome(
                _snapshot.SnapshotId,
                tracker.Signal.SignalId,
                tracker.EntryTime,
                tracker.EntryBar,
                exitCandle.Time,
                exitCandle.Bar,
                tracker.Signal.Side.ToString(),
                tracker.Entry,
                tracker.Stop,
                tracker.InitialRiskPoints,
                tracker.MfePoints,
                tracker.MaePoints,
                tracker.MfeR,
                tracker.MaeR,
                tracker.Hit1R,
                tracker.Hit1_5R,
                tracker.Hit2R,
                tracker.Hit2_5R,
                tracker.Hit3R,
                Math.Max(0, exitCandle.Bar - tracker.EntryBar),
                tracker.ResearchPath,
                tracker.FirstStopBar,
                tracker.First1RBar,
                tracker.First1_5RBar,
                tracker.First2RBar,
                tracker.First2_5RBar,
                tracker.First3RBar,
                tracker.StopHitBefore1R,
                tracker.AmbiguousStopAndTargetSameBar,
                tracker.TimeTo1RMinutes,
                tracker.TimeToMfeMinutes,
                tracker.MaxHeatBefore1R,
                tracker.StopBasis,
                tracker.EntryDelayBars,
                tracker.OutcomeClass,
                exitReason,
                ActualOutcomeFor(tracker));
            AppendEdgeAttributionOutcome(tracker, exitCandle, exitReason);
            _researchTrackers.RemoveAt(i);
            _researchOutcomeCount++;
        }
    }

    private void AppendEdgeAttributionOutcome(ResearchTracker tracker, OpfCandle exitCandle, string exitReason)
    {
        if (_researchLogger is null)
            return;

        var reward = EstimateReward(tracker.Signal, tracker.ResearchPath, tracker.Entry, tracker.InitialRiskPoints);
        var estimatedRr = reward.Points <= 0m || tracker.InitialRiskPoints <= 0m ? 0m : Math.Round(reward.Points / tracker.InitialRiskPoints, 4);
        var actual = ActualOutcomeFor(tracker);
        _researchLogger.AppendEdgeAttribution(
            tracker.Signal,
            exitCandle.Time,
            exitCandle.Bar,
            tracker.ResearchPath,
            "Outcome",
            tracker.Entry,
            tracker.Stop,
            tracker.InitialRiskPoints,
            reward.Points,
            reward.Model,
            estimatedRr,
            tracker.OutcomeClass,
            exitReason,
            actual?.ActualVerified ?? false,
            actual?.ActualExitRole ?? string.Empty,
            actual?.ActualPnLR ?? 0m,
            actual?.ActualPnLDollars ?? 0m);
    }

    private bool IsTrackerWaitingForActualExit(ResearchTracker tracker)
    {
        return _replayExecution is not null
            && !_replayExecution.ExitCompleted
            && string.Equals(_replayExecution.SignalId, tracker.Signal.SignalId, StringComparison.Ordinal)
            && string.Equals(_replayExecution.ResearchPath, tracker.ResearchPath, StringComparison.Ordinal);
    }

    private void AppendExitPolicyEvaluations(ResearchTracker tracker, OpfCandle exitCandle)
    {
        if (_snapshot is null)
            return;
        if (_compactResearchLogging && !ShouldWriteCompactExitPolicyPath(tracker.ResearchPath))
            return;

        AppendExitPolicyEvaluation(tracker, exitCandle, "Fixed1_5R", 1.5m, tracker.First1_5RBar);
        AppendExitPolicyEvaluation(tracker, exitCandle, "Fixed2R", 2m, tracker.First2RBar);
        AppendExitPolicyEvaluation(tracker, exitCandle, "Fixed2_5R", 2.5m, tracker.First2_5RBar);
        AppendExitPolicyEvaluation(tracker, exitCandle, "Fixed3R", 3m, tracker.First3RBar);
        var baseTargetR = ActualTargetRFor(tracker.Signal, tracker.ResearchPath, tracker.InitialRiskPoints);
        var firstBaseTargetBar = FirstTargetBarFor(tracker, baseTargetR);
        AppendSplitRunnerPolicyEvaluation(tracker, exitCandle, "SplitBase_Runner2_5R_BE0_75R", baseTargetR, firstBaseTargetBar, 2.5m, tracker.First2_5RBar, tracker.First0_75RBar, tracker.FirstBreakEvenAfter0_75RBar);
        AppendSplitRunnerPolicyEvaluation(tracker, exitCandle, "SplitBase_Runner2_5R_BE1R", baseTargetR, firstBaseTargetBar, 2.5m, tracker.First2_5RBar, tracker.First1RBar, tracker.FirstBreakEvenAfter1RBar);
        AppendSplitRunnerPolicyEvaluation(tracker, exitCandle, "SplitBase_Runner3R_BE0_75R", baseTargetR, firstBaseTargetBar, 3m, tracker.First3RBar, tracker.First0_75RBar, tracker.FirstBreakEvenAfter0_75RBar);
        AppendSplitRunnerPolicyEvaluation(tracker, exitCandle, "SplitBase_Runner3R_BE1R", baseTargetR, firstBaseTargetBar, 3m, tracker.First3RBar, tracker.First1RBar, tracker.FirstBreakEvenAfter1RBar);
        if (_compactResearchLogging)
            return;

        AppendProtectedExtensionPolicyEvaluation(tracker, exitCandle, "ProtectBE_Then2_5R", 2.5m, tracker.First2_5RBar, 0m, tracker.FirstBreakEvenAfter1_5RBar);
        AppendProtectedExtensionPolicyEvaluation(tracker, exitCandle, "Protect1R_Then2_5R", 2.5m, tracker.First2_5RBar, 1m, tracker.First1RLockAfter1_5RBar);
        AppendProtectedExtensionPolicyEvaluation(tracker, exitCandle, "ProtectBE_Then3R", 3m, tracker.First3RBar, 0m, tracker.FirstBreakEvenAfter1_5RBar);
        AppendProtectedExtensionPolicyEvaluation(tracker, exitCandle, "Protect1R_Then3R", 3m, tracker.First3RBar, 1m, tracker.First1RLockAfter1_5RBar);
    }

    private static bool ShouldWriteCompactExitPolicyPath(string researchPath)
    {
        return string.Equals(researchPath, "ObservationConfirm", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(researchPath, "ObservationConfirm_WideStop1_5R", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(researchPath, "BreakawayFvg", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(researchPath, "BreakawayFvg_Qualified", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(researchPath, "FailureReverse_ObservationInvalidated", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(researchPath, "FailureReverse_RetestFailed", StringComparison.OrdinalIgnoreCase);
    }

    private void AppendExitPolicyEvaluation(ResearchTracker tracker, OpfCandle exitCandle, string exitPolicy, decimal targetR, int? firstTargetBar)
    {
        if (_snapshot is null)
            return;

        var target = tracker.Signal.Side == TradeSide.Long
            ? tracker.Entry + tracker.InitialRiskPoints * targetR
            : tracker.Entry - tracker.InitialRiskPoints * targetR;
        var result = ResolveExitPolicy(tracker, exitCandle, targetR, firstTargetBar);

        _researchLogger?.AppendExitPolicyEvaluation(
            _snapshot.SnapshotId,
            tracker.Signal.SignalId,
            tracker.EntryTime,
            tracker.EntryBar,
            exitCandle.Time,
            exitCandle.Bar,
            tracker.Signal.Side.ToString(),
            tracker.ResearchPath,
            exitPolicy,
            result.ExitReason,
            tracker.Entry,
            tracker.Stop,
            target,
            result.ExitPrice,
            tracker.InitialRiskPoints,
            targetR,
            result.PnlPoints,
            result.PnlR,
            Math.Max(0, exitCandle.Bar - tracker.EntryBar),
            result.Ambiguous,
            tracker.FirstStopBar,
            firstTargetBar,
            tracker.MfeR,
            tracker.MaeR);
    }

    private void AppendProtectedExtensionPolicyEvaluation(
        ResearchTracker tracker,
        OpfCandle exitCandle,
        string exitPolicy,
        decimal targetR,
        int? firstTargetBar,
        decimal lockR,
        int? firstProtectStopBar)
    {
        if (_snapshot is null)
            return;

        var target = tracker.Signal.Side == TradeSide.Long
            ? tracker.Entry + tracker.InitialRiskPoints * targetR
            : tracker.Entry - tracker.InitialRiskPoints * targetR;
        var result = ResolveProtectedExtensionPolicy(tracker, exitCandle, targetR, firstTargetBar, lockR, tracker.First1_5RBar, firstProtectStopBar);

        _researchLogger?.AppendExitPolicyEvaluation(
            _snapshot.SnapshotId,
            tracker.Signal.SignalId,
            tracker.EntryTime,
            tracker.EntryBar,
            exitCandle.Time,
            exitCandle.Bar,
            tracker.Signal.Side.ToString(),
            tracker.ResearchPath,
            exitPolicy,
            result.ExitReason,
            tracker.Entry,
            tracker.Stop,
            target,
            result.ExitPrice,
            tracker.InitialRiskPoints,
            targetR,
            result.PnlPoints,
            result.PnlR,
            Math.Max(0, exitCandle.Bar - tracker.EntryBar),
            result.Ambiguous,
            tracker.FirstStopBar,
            firstTargetBar,
            tracker.MfeR,
            tracker.MaeR);
    }

    private void AppendSplitRunnerPolicyEvaluation(
        ResearchTracker tracker,
        OpfCandle exitCandle,
        string exitPolicy,
        decimal baseTargetR,
        int? firstBaseTargetBar,
        decimal runnerTargetR,
        int? firstRunnerTargetBar,
        int? firstTriggerBar,
        int? firstProtectStopBar)
    {
        if (_snapshot is null)
            return;

        var baseResult = ResolveExitPolicy(tracker, exitCandle, baseTargetR, firstBaseTargetBar);
        var runnerResult = ResolveProtectedExtensionPolicy(tracker, exitCandle, runnerTargetR, firstRunnerTargetBar, 0m, firstTriggerBar, firstProtectStopBar);
        var pnlPoints = Math.Round((baseResult.PnlPoints + runnerResult.PnlPoints) / 2m, 4);
        var pnlR = Math.Round((baseResult.PnlR + runnerResult.PnlR) / 2m, 4);
        var exitPrice = tracker.Signal.Side == TradeSide.Long
            ? tracker.Entry + pnlPoints
            : tracker.Entry - pnlPoints;
        var runnerTarget = TargetFromRisk(tracker.Signal.Side, tracker.Entry, tracker.InitialRiskPoints, runnerTargetR);

        _researchLogger?.AppendExitPolicyEvaluation(
            _snapshot.SnapshotId,
            tracker.Signal.SignalId,
            tracker.EntryTime,
            tracker.EntryBar,
            exitCandle.Time,
            exitCandle.Bar,
            tracker.Signal.Side.ToString(),
            tracker.ResearchPath,
            exitPolicy,
            $"Base:{baseResult.ExitReason}|Runner:{runnerResult.ExitReason}",
            tracker.Entry,
            tracker.Stop,
            runnerTarget,
            exitPrice,
            tracker.InitialRiskPoints,
            runnerTargetR,
            pnlPoints,
            pnlR,
            Math.Max(0, exitCandle.Bar - tracker.EntryBar),
            baseResult.Ambiguous || runnerResult.Ambiguous,
            tracker.FirstStopBar,
            firstRunnerTargetBar,
            tracker.MfeR,
            tracker.MaeR);
    }

    private static int? FirstTargetBarFor(ResearchTracker tracker, decimal targetR)
    {
        if (targetR == 1m)
            return tracker.First1RBar;
        if (targetR == 1.5m)
            return tracker.First1_5RBar;
        if (targetR == 2m)
            return tracker.First2RBar;
        if (targetR == 2.5m)
            return tracker.First2_5RBar;
        if (targetR == 3m)
            return tracker.First3RBar;
        return null;
    }

    private static ExitPolicyResult ResolveExitPolicy(ResearchTracker tracker, OpfCandle exitCandle, decimal targetR, int? firstTargetBar)
    {
        var firstStopBar = tracker.FirstStopBar;
        var ambiguous = firstStopBar.HasValue && firstTargetBar.HasValue && firstStopBar.Value == firstTargetBar.Value;
        if (firstTargetBar.HasValue && (!firstStopBar.HasValue || firstTargetBar.Value < firstStopBar.Value))
            return TargetExit(tracker, targetR, ambiguous);
        if (firstStopBar.HasValue && (!firstTargetBar.HasValue || firstStopBar.Value <= firstTargetBar.Value))
            return StopExit(tracker, ambiguous);

        var points = tracker.Signal.Side == TradeSide.Long
            ? exitCandle.Close - tracker.Entry
            : tracker.Entry - exitCandle.Close;
        var pnlR = tracker.InitialRiskPoints <= 0m ? 0m : Math.Round(points / tracker.InitialRiskPoints, 4);
        return new ExitPolicyResult("TimeStop", exitCandle.Close, points, pnlR, false);
    }

    private static ExitPolicyResult ResolveProtectedExtensionPolicy(
        ResearchTracker tracker,
        OpfCandle exitCandle,
        decimal targetR,
        int? firstTargetBar,
        decimal lockR,
        int? firstTriggerBar,
        int? firstProtectStopBar)
    {
        var firstStopBar = tracker.FirstStopBar;
        if (!firstTriggerBar.HasValue)
            return firstStopBar.HasValue ? StopExit(tracker, false) : TimeStopExit(tracker, exitCandle, "TimeStop");
        if (firstStopBar.HasValue && firstStopBar.Value <= firstTriggerBar.Value)
            return StopExit(tracker, false);

        var ambiguous = firstProtectStopBar.HasValue && firstTargetBar.HasValue && firstProtectStopBar.Value == firstTargetBar.Value;
        if (firstTargetBar.HasValue && (!firstProtectStopBar.HasValue || firstTargetBar.Value < firstProtectStopBar.Value))
            return TargetExit(tracker, targetR, ambiguous);
        if (firstProtectStopBar.HasValue && (!firstTargetBar.HasValue || firstProtectStopBar.Value <= firstTargetBar.Value))
            return ProtectedStopExit(tracker, lockR, ambiguous);

        return TimeStopExit(tracker, exitCandle, "TimeStopAfter1_5R");
    }

    private static ExitPolicyResult TargetExit(ResearchTracker tracker, decimal targetR, bool ambiguous)
    {
        var points = tracker.InitialRiskPoints * targetR;
        var exitPrice = tracker.Signal.Side == TradeSide.Long
            ? tracker.Entry + points
            : tracker.Entry - points;
        return new ExitPolicyResult("Target", exitPrice, points, targetR, ambiguous);
    }

    private static ExitPolicyResult TimeStopExit(ResearchTracker tracker, OpfCandle exitCandle, string reason)
    {
        var points = tracker.Signal.Side == TradeSide.Long
            ? exitCandle.Close - tracker.Entry
            : tracker.Entry - exitCandle.Close;
        var pnlR = tracker.InitialRiskPoints <= 0m ? 0m : Math.Round(points / tracker.InitialRiskPoints, 4);
        return new ExitPolicyResult(reason, exitCandle.Close, points, pnlR, false);
    }

    private static ExitPolicyResult StopExit(ResearchTracker tracker, bool ambiguous)
    {
        var points = -tracker.InitialRiskPoints;
        return new ExitPolicyResult("Stop", tracker.Stop, points, -1m, ambiguous);
    }

    private static ExitPolicyResult ProtectedStopExit(ResearchTracker tracker, decimal lockR, bool ambiguous)
    {
        var points = tracker.InitialRiskPoints * lockR;
        var exitPrice = tracker.Signal.Side == TradeSide.Long
            ? tracker.Entry + points
            : tracker.Entry - points;
        var reason = lockR <= 0m ? "ProtectBE" : $"Protect{lockR:0.##}R";
        return new ExitPolicyResult(reason, exitPrice, points, lockR, ambiguous);
    }

    private ResearchLogger.ActualOutcome? ActualOutcomeFor(ResearchTracker tracker)
    {
        return _actualTradeOutcomesBySignalPath.TryGetValue(ActualOutcomeKey(tracker.Signal.SignalId, tracker.ResearchPath), out var actual)
            ? new ResearchLogger.ActualOutcome(
                true,
                actual.TradeId,
                actual.ExitTime,
                actual.ExitBar,
                actual.ExitPrice,
                actual.ExitRole,
                actual.Points,
                actual.Dollars,
                actual.PointsR,
                actual.ActualMfePoints,
                actual.ActualMaePoints,
                actual.ActualMfeR,
                actual.ActualMaeR)
            : null;
    }

    private void UpdateActualExecutionExcursion(OpfCandle candle)
    {
        if (_replayExecution is null || _replayExecution.ExitCompleted)
            return;

        _replayExecution.UpdateActualExcursion(candle);
    }

    private static string ActualOutcomeKey(string signalId, string researchPath)
    {
        return $"{signalId}|{researchPath}";
    }

    private bool TryMarkResearchOutcomeWritten(ResearchTracker tracker, int exitBar)
    {
        var actual = ActualOutcomeFor(tracker);
        if (actual?.ActualVerified == true && !string.IsNullOrWhiteSpace(actual.ActualTradeId))
            return _writtenResearchOutcomeKeys.Add($"ActualTrade|{actual.ActualTradeId}");

        return _writtenResearchOutcomeKeys.Add($"{tracker.Signal.SignalId}|{tracker.ResearchPath}|{tracker.EntryBar}|{exitBar}");
    }

    private static string BuildSignalId(OpfCandle candle, TradeSide side, string token, DetectedZone zone)
    {
        return $"{candle.Time:yyyyMMdd-HHmm}-{(side == TradeSide.Long ? "BULL" : "BEAR")}-{token}-{candle.Bar:000000}-{ZoneSignalTag(zone)}";
    }

    private static string ZoneSignalTag(DetectedZone zone)
    {
        var parts = zone.ZoneId.Split('-', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? $"Z{zone.CreatedBar:000000}" : $"Z{parts[^1]}";
    }

    private static TradeSide? TryGetCandidateSide(MarketRegime regime, DetectedZone zone, OpfCandle candle)
    {
        if (!IsZoneTouched(candle, zone))
            return null;
        if (candle.Bar == zone.CreatedBar)
            return null;
        if (!IsTradableFreshness(zone))
            return null;
        if (zone.Mitigated)
            return null;

        if (regime == MarketRegime.BullTrend && zone.Direction == "Bull")
            return TradeSide.Long;
        if (regime == MarketRegime.BearTrend && zone.Direction == "Bear")
            return TradeSide.Short;

        return null;
    }

    private static TradeSide? TryGetTrendSide(MarketRegime regime, DetectedZone zone)
    {
        if (regime == MarketRegime.BullTrend && zone.Direction == "Bull")
            return TradeSide.Long;
        if (regime == MarketRegime.BearTrend && zone.Direction == "Bear")
            return TradeSide.Short;
        return null;
    }

    private static TradeSide? ZoneDirectionSide(DetectedZone zone)
    {
        return zone.Direction switch
        {
            "Bull" => TradeSide.Long,
            "Bear" => TradeSide.Short,
            _ => null
        };
    }

    private void LogCandidateEvaluation(OpfCandle candle, MarketRegime regime, DetectedZone zone, string result, string reason)
    {
        if (_snapshot is null)
            return;

        var key = $"{zone.ZoneId}:{zone.TouchCount}:{result}:{reason}";
        if (!_loggedEvaluationKeys.Add(key))
            return;

        var pullback = GetOrStartPullbackEpisode(candle, regime, zone);
        _researchLogger?.AppendCandidateEvaluation(
            _snapshot.SnapshotId,
            candle.Time,
            candle.Bar,
            candle,
            regime,
            zone,
            result,
            reason,
            pullback?.EpisodeId ?? string.Empty,
            pullback?.CountInRegime ?? 0,
            pullback?.StartBar ?? 0,
            pullback is null ? string.Empty : "Active");
    }

    private PullbackEpisode? GetOrStartPullbackEpisode(OpfCandle candle, MarketRegime regime, DetectedZone zone)
    {
        if (regime == MarketRegime.BullTrend && zone.Direction == "Bull")
        {
            _activeBullPullback ??= StartPullbackEpisode(candle, TradeSide.Long, ++_bullPullbackCountInRegime, _bullTrendHigh ?? candle.High);
            return _activeBullPullback;
        }

        if (regime == MarketRegime.BearTrend && zone.Direction == "Bear")
        {
            _activeBearPullback ??= StartPullbackEpisode(candle, TradeSide.Short, ++_bearPullbackCountInRegime, _bearTrendLow ?? candle.Low);
            return _activeBearPullback;
        }

        return null;
    }

    private static PullbackEpisode StartPullbackEpisode(OpfCandle candle, TradeSide side, int countInRegime, decimal breakPrice)
    {
        var direction = side == TradeSide.Long ? "BULL" : "BEAR";
        return new PullbackEpisode(
            $"{candle.Time:yyyyMMdd-HHmm}-{direction}-PB-{countInRegime:00}",
            side,
            countInRegime,
            candle.Bar,
            breakPrice);
    }

    private void UpdatePullbackEpisodes(OpfCandle candle, MarketRegime regime)
    {
        if (regime != MarketRegime.BullTrend)
        {
            _activeBullPullback = null;
            _bullTrendHigh = null;
            _bullPullbackCountInRegime = 0;
        }
        else
        {
            if (_activeBullPullback is null)
                _bullTrendHigh = _bullTrendHigh.HasValue ? Math.Max(_bullTrendHigh.Value, candle.High) : candle.High;
            else if (candle.Close > _activeBullPullback.BreakPrice || candle.Bar - _activeBullPullback.StartBar >= MaxPullbackBars)
            {
                _activeBullPullback = null;
                _bullTrendHigh = candle.High;
            }
        }

        if (regime != MarketRegime.BearTrend)
        {
            _activeBearPullback = null;
            _bearTrendLow = null;
            _bearPullbackCountInRegime = 0;
        }
        else
        {
            if (_activeBearPullback is null)
                _bearTrendLow = _bearTrendLow.HasValue ? Math.Min(_bearTrendLow.Value, candle.Low) : candle.Low;
            else if (candle.Close < _activeBearPullback.BreakPrice || candle.Bar - _activeBearPullback.StartBar >= MaxPullbackBars)
            {
                _activeBearPullback = null;
                _bearTrendLow = candle.Low;
            }
        }
    }

    private static bool IsZoneTouched(OpfCandle candle, DetectedZone zone)
    {
        return candle.High >= zone.Low && candle.Low <= zone.High;
    }

    private static bool IsInvalidated(DetectedZone zone, OpfCandle candle)
    {
        return zone.Direction == "Bull"
            ? candle.Close < zone.Low
            : candle.Close > zone.High;
    }

    private static string? BreakawayRetestQualityRejectReason(CandidateSignal signal, OpfCandle candle)
    {
        if (signal.Zone is null)
            return "BreakawayRetestNoZone";

        var entry = candle.Close;
        var stop = signal.Side == TradeSide.Long
            ? signal.Zone.Low - 0.50m
            : signal.Zone.High + 0.50m;
        var risk = Math.Abs(entry - stop);
        if (risk <= 0m)
            return "BreakawayRetestInvalidRisk";

        var confirmDistance = signal.Side == TradeSide.Long
            ? candle.Close - signal.Zone.High
            : signal.Zone.Low - candle.Close;
        if (confirmDistance < 0.25m * risk)
            return "BreakawayRetestConfirmTooClose";

        var heat = signal.Side == TradeSide.Long
            ? entry - candle.Low
            : candle.High - entry;
        if (heat > 0.50m * risk)
            return "BreakawayRetestConfirmHeatTooHigh";

        return null;
    }

    private static string CandidateSkipReason(MarketRegime regime, DetectedZone zone)
    {
        if (regime == MarketRegime.Unknown)
            return "RegimeUnknown";
        if (regime == MarketRegime.BullTrend && zone.Direction != "Bull")
            return "ZoneDirectionMismatch";
        if (regime == MarketRegime.BearTrend && zone.Direction != "Bear")
            return "ZoneDirectionMismatch";
        if (zone.Mitigated)
            return "ZoneMitigated";
        if (!IsTradableFreshness(zone))
            return "ZoneTouchTooLate";
        return "NoCandidateRule";
    }

    private static string CandidateSkipReason(MarketRegime regime, DetectedZone zone, OpfCandle candle)
    {
        if (candle.Bar == zone.CreatedBar)
            return "ZoneJustCreated";
        return CandidateSkipReason(regime, zone);
    }

    private static bool IsTradableFreshness(DetectedZone zone)
    {
        return zone.Freshness is "Fresh" or "SecondTouch";
    }

    private static bool IsQualifiedBreakaway(CandidateSignal signal, OpfCandle candle)
    {
        if (signal.Zone is null)
            return false;
        if (signal.Side != TradeSide.Long)
            return false;
        if (signal.Zone.ZoneType != "BullFVG" || signal.Zone.Freshness != "Fresh" || signal.Zone.Mitigated)
            return false;
        if (!signal.RegimeScore.Passed || signal.SetupQualityScore.TotalScore < 80m)
            return false;

        var risk = Math.Abs(candle.Close - (signal.Zone.Low - 0.50m));
        if (risk <= 0m || risk > 8.50m)
            return false;

        var heat = candle.Close - candle.Low;
        return heat <= 0.50m * risk;
    }

    private static bool IsQualifiedFailureReverseLong(CandidateSignal signal, OpfCandle candle)
    {
        if (signal.Zone is null || signal.Side != TradeSide.Long)
            return false;
        if (signal.SetupType != SetupType.FailureReverse)
            return false;

        var risk = Math.Abs(candle.Close - (signal.Zone.Low - 0.50m));
        if (risk <= 0m || risk > 8.50m)
            return false;

        var heat = candle.Close - candle.Low;
        var heatR = heat / risk;
        return heatR >= 1.00m && heatR <= 2.00m;
    }

    private static bool IsShadowCandidateReason(string reason)
    {
        return reason is "ZoneJustCreated" or "ZoneMitigated" or "ZoneTouchTooLate";
    }

    private static bool IsObservationResearchReason(string reason)
    {
        return reason is "RegimeUnknown" or "ZoneJustCreated";
    }

    private static string[] UnknownRegimeReasons(RegimeResult regime)
    {
        if (regime.Regime != MarketRegime.Unknown)
            return new[] { "RegimeKnown" };

        var reasons = new List<string> { "RegimeUnknown" };
        AddFailedTrendComponents(reasons, "Bull", regime.BullTrendScore);
        AddFailedTrendComponents(reasons, "Bear", regime.BearTrendScore);
        return reasons.ToArray();
    }

    private static void AddFailedTrendComponents(List<string> reasons, string side, ScoreBreakdown score)
    {
        foreach (var component in score.Components.Where(x => !x.Passed))
            reasons.Add($"{side}{component.Name}Failed");
    }

    private void UpdateHud(OpfCandle candle, RegimeResult regime)
    {
        var snapshotId = _snapshot?.SnapshotId ?? "-";
        var shortSnapshot = snapshotId.Length <= 18 ? snapshotId : snapshotId[^18..];
        var noTrade = regime.Regime == MarketRegime.Unknown ? "RegimeUnknown" : "-";

        var hud = new StringBuilder();
        hud.AppendLine("OPF v0.1 Research");
        hud.AppendLine($"Schema: {StrategyVersions.ResearchSchemaVersion}");
        hud.AppendLine($"Snapshot: {shortSnapshot}");
        hud.AppendLine($"Bar: {candle.Bar}  Time: {candle.Time:MM-dd HH:mm}");
        hud.AppendLine($"Regime: {regime.Regime}");
        hud.AppendLine($"BullScore: {regime.BullTrendScore.TotalScore:0}  BearScore: {regime.BearTrendScore.TotalScore:0}");
        hud.AppendLine($"Bull Top: {FormatPassedComponents(regime.BullTrendScore)}");
        hud.AppendLine($"Bear Top: {FormatPassedComponents(regime.BearTrendScore)}");
        hud.AppendLine($"Last Change: {_lastRegimeChangeText}");
        hud.AppendLine($"Zones: {_activeZoneCount}  Candidates: {_candidateCount}  Confirmed: {_confirmedCount}");
        hud.AppendLine($"Research: active={_researchTrackers.Count} done={_researchOutcomeCount}");
        var cleanupCount = _replayExecution is null ? 0 : CountWorkingProtectionOrders(_replayExecution);
        var abnormalGuard = IsDailyAbnormalFillGuardActive(candle.Time.Date) ? $" abnormalGuard={_dailyAbnormalFillGuardCount}" : string.Empty;
        hud.AppendLine($"ActualExec: {(EnableReplayOrders ? "ON" : "OFF")} sent={_replayTradesToday}/{ReplayMaxTradesPerDay} exits={_replayExitsToday} active={IsReplayExecutionActive(_replayExecution)} pos={CurrentPosition:0.##} cleanup={cleanupCount}{abnormalGuard}");
        var dailyTarget = _snapshot?.ExecutionProfile.DailyTargetDollars ?? 0m;
        var dailyLoss = _snapshot?.ExecutionProfile.DailyLossLimitDollars ?? 0m;
        hud.AppendLine($"Today: TP={_replayTpToday} SL={_replaySlToday} Other={_replayOtherExitToday} NetR={_replayDailyR:0.00} PnL=${_replayDailyPnlDollars:0.##} target=${dailyTarget:0.##} loss=${dailyLoss:0.##}");
        hud.AppendLine($"ExecLosses: full={_replayFullLossTradesToday} consec={_replayConsecutiveLossesToday}");
        hud.AppendLine($"ActiveOrder: {BuildActiveExecutionHudLine(_replayExecution)}");
        hud.AppendLine($"LastOrder: {_lastExecutionHudText}");
        if (_recentExecutionHudItems.Count > 0)
            hud.AppendLine($"RecentOrders: {string.Join(" | ", _recentExecutionHudItems)}");
        hud.Append($"NoTrade: {noTrade}");

        lock (_renderLock)
            _hudText = hud.ToString();
    }

    private static string BuildActiveExecutionHudLine(ReplayExecutionState? execution)
    {
        if (execution is null)
            return "-";

        if (execution.ExitCompleted)
            return $"closed {execution.ExitRole ?? "-"} @{execution.ExitPrice?.ToString("0.00") ?? "-"} {execution.ResearchPath}";

        var side = execution.Side == TradeSide.Long ? "L" : "S";
        var entry = execution.EntryAvgPrice > 0m ? execution.EntryAvgPrice : execution.CreatedPrice;
        if (execution.EntryFilledQty <= 0m)
            return $"pending {side} {execution.ResearchPath} E={entry:0.00} fill=0/{execution.Quantity:0.##}";

        return $"{side} {execution.ResearchPath} E={entry:0.00} SL={execution.Stop:0.00} TP={execution.Target:0.00} fill={execution.EntryFilledQty:0.##}/{execution.Quantity:0.##}";
    }

    private static string FormatPassedComponents(ScoreBreakdown score)
    {
        var parts = score.Components
            .Where(x => x.Passed && x.Contribution > 0m)
            .OrderByDescending(x => x.Contribution)
            .Take(3)
            .Select(x => $"{ShortComponentName(x.Name)} {x.Contribution:0}")
            .ToArray();

        return parts.Length == 0 ? "-" : string.Join(" | ", parts);
    }

    private static string ShortComponentName(string name)
    {
        return name switch
        {
            "SwingProgression" => "Swing",
            "OpeningRangeSide" => "OR",
            "DirectionalDisplacement" => "Disp",
            "VwapCrossCount" => "VWAPx",
            _ => name.Replace("VWAP", "VWAP")
        };
    }

    private void DrawHud(RenderContext context, string hud)
    {
        if (string.IsNullOrWhiteSpace(hud))
            return;

        var font = new RenderFont("Consolas", 11);
        var size = context.MeasureString(hud, font);
        const int padX = 10;
        const int padY = 8;

        const int minBoxW = 560;
        var boxW = Math.Max(minBoxW, (int)Math.Ceiling((double)size.Width) + padX * 2);
        var boxH = (int)Math.Ceiling((double)size.Height) + padY * 2;
        var x = ChartArea.X + (ChartArea.Width - boxW) / 2;
        var y = ChartArea.Y + 8;
        var rect = new Rectangle(x, y, boxW, boxH);

        context.FillRectangle(Color.FromArgb(125, 0, 0, 0), rect);
        context.DrawRectangle(new RenderPen(Color.FromArgb(170, 70, 70, 70), 1), rect);
        context.DrawString(hud, font, Color.DeepSkyBlue, x + padX, y + padY);
    }

    private void DrawActualExecutionLines(RenderContext context)
    {
        if (ChartInfo is null || _recentReplayExecutions.Count == 0)
            return;

        var maxVisible = ActualMaxVisibleOrders <= 0 ? 1 : ActualMaxVisibleOrders;
        var drawn = 0;
        foreach (var execution in _recentReplayExecutions.AsEnumerable().Reverse())
        {
            if (drawn >= maxVisible)
                break;

            if (!ShouldDrawActualExecution(execution))
                continue;

            DrawActualExecutionLines(context, execution, drawn * 4);
            drawn++;
        }
    }

    private bool ShouldDrawActualExecution(ReplayExecutionState execution)
    {
        if (execution.ExitCompleted && execution.EntryFilledQty <= 0m)
            return false;
        if (execution.ExitCompleted && execution.ExitBar.HasValue && ActualLineLookbackBars > 0 && _lastSeenBar - execution.ExitBar.Value > ActualLineLookbackBars)
            return false;
        return true;
    }

    private void DrawActualExecutionLines(RenderContext context, ReplayExecutionState execution, int labelSlotOffset)
    {
        var entry = execution.EntryAvgPrice > 0m ? execution.EntryAvgPrice : 0m;
        if (entry <= 0m && execution.EntryOrder is not null)
            entry = execution.EntryOrder.Price > 0m ? execution.EntryOrder.Price : execution.CreatedPrice;
        if (entry <= 0m)
            entry = execution.CreatedPrice;

        var isActive = IsReplayExecutionActive(execution);
        var alpha = isActive ? 230 : 90;
        var isPendingEntry = execution.EntryFilledQty <= 0m && !execution.ExitCompleted;
        var suffix = isPendingEntry ? "PENDING_ENTRY" : execution.ProtectionCleanupPending ? "CLEANUP" : isActive ? "ACTIVE" : $"EXIT {execution.ExitRole ?? "-"}";
        var x1 = isActive ? ChartArea.X : GetActualExecutionStartX(execution);
        var x2 = isActive ? ChartArea.X + ChartArea.Width : GetActualExecutionEndX(execution);

        DrawActualHLine(context, entry, Color.FromArgb(alpha, Color.DeepSkyBlue), 2, $"ACT {execution.Side} ENTRY {entry:0.00} {suffix}", x1, x2, labelSlot: labelSlotOffset);
        if (isPendingEntry)
            return;

        DrawActualHLine(context, execution.Stop, Color.FromArgb(alpha, Color.OrangeRed), 2, $"SL {execution.Stop:0.00}", x1, x2, labelSlot: labelSlotOffset + 1);
        DrawActualHLine(context, execution.Target, Color.FromArgb(alpha, Color.LimeGreen), 2, $"TP {execution.Target:0.00} ({execution.TargetR:0.0}R)", x1, x2, labelSlot: labelSlotOffset + 2);

        if (execution.ExitCompleted && execution.ExitPrice.HasValue)
            DrawActualHLine(context, execution.ExitPrice.Value, Color.FromArgb(190, Color.Gold), 1, $"EXIT {execution.ExitRole} {execution.ExitPrice.Value:0.00}", x1, x2, labelSlot: labelSlotOffset + 3);
    }

    private int GetActualExecutionStartX(ReplayExecutionState execution)
    {
        if (ChartInfo is null)
            return ChartArea.X;

        var x = ChartInfo.GetXByBar(execution.CreatedBar, true);
        return Math.Max(ChartArea.X, x);
    }

    private int GetActualExecutionEndX(ReplayExecutionState execution)
    {
        if (ChartInfo is null)
            return ChartArea.X + ChartArea.Width;

        var endBar = execution.ExitBar ?? Math.Min(_lastSeenBar, execution.CreatedBar + 12);
        var rightBar = Math.Min(_lastSeenBar, endBar + 4);
        var x = ChartInfo.GetXByBar(rightBar, false);
        return Math.Min(ChartArea.X + ChartArea.Width, Math.Max(GetActualExecutionStartX(execution) + 24, x));
    }

    private void DrawActualHLine(RenderContext context, decimal price, Color color, int width, string label, int x1, int x2, int labelSlot)
    {
        if (ChartInfo is null)
            return;

        var y = (int)ChartInfo.GetYByPrice(price, false);
        context.DrawLine(new RenderPen(color, width), x1, y, x2, y);

        var font = new RenderFont("Consolas", 11);
        var size = context.MeasureString(label, font);
        var labelX = x1 + 8;
        var labelY = y - (int)size.Height - 4 + labelSlot * 2;
        var rect = new Rectangle(labelX - 4, labelY - 2, (int)Math.Ceiling((double)size.Width) + 8, (int)Math.Ceiling((double)size.Height) + 4);

        context.FillRectangle(Color.FromArgb(135, 0, 0, 0), rect);
        context.DrawString(label, font, color, labelX, labelY);
    }

    private static OpfCandle ToOpfCandle(int bar, IndicatorCandle candle)
    {
        return new OpfCandle(
            Bar: bar,
            Time: candle.Time,
            Open: candle.Open,
            High: candle.High,
            Low: candle.Low,
            Close: candle.Close,
            Volume: candle.Volume,
            Vwap: TryGetVwap(candle));
    }

    private static decimal TryGetVwap(IndicatorCandle candle)
    {
        _vwapProperty ??= candle.GetType().GetProperty("VWAP") ?? candle.GetType().GetProperty("Vwap");
        var value = _vwapProperty?.GetValue(candle);
        return value is decimal d ? d : 0m;
    }

    private sealed record PendingCandidate(CandidateSignal Signal, int MaxConfirmBar);

    private sealed record PendingBreakawayRetest(CandidateSignal Signal, int MaxRetestBar)
    {
        public bool Touched { get; set; }
    }

    private sealed record PendingMainlinePullbackRetest(
        string Key,
        DetectedZone Zone,
        TradeSide Side,
        ScoreBreakdown RegimeScore,
        MarketRegime MarketRegime,
        int MaxRetestBar);

    private sealed record PendingShadowCandidate(CandidateSignal Signal, int MaxConfirmBar, string SourceSkipReason)
    {
        public bool Touched { get; set; }
    }

    private sealed record PendingObservationConfirm(CandidateSignal Signal, int MaxConfirmBar, string SourcePath, string SourceReason);

    private sealed record PendingStructureConfirmShadow(CandidateSignal Signal, int MaxConfirmBar, string SourceReason, int? RetestBar);

    private sealed record PendingConfirmBarWait1(CandidateSignal Signal, OpfCandle ConfirmCandle, decimal Stop);

    private sealed record PendingConfirmedRetrace(CandidateSignal Signal, OpfCandle ConfirmCandle, int MaxRetraceBar);

    private sealed record PendingFailureReverseRetest(CandidateSignal Signal, int MaxRetestBar)
    {
        public bool Touched { get; set; }
    }

    private sealed record ActualTradeOutcome(
        string TradeId,
        DateTime ExitTime,
        int ExitBar,
        decimal ExitPrice,
        string ExitRole,
        decimal Points,
        decimal Dollars,
        decimal PointsR,
        decimal ActualMfePoints,
        decimal ActualMaePoints,
        decimal ActualMfeR,
        decimal ActualMaeR);

    private sealed record ExecutionFillValidation(
        bool IsAbnormal,
        string Reason,
        decimal ExpectedExitPrice,
        decimal ExitPriceDriftPoints,
        decimal NormalDollars,
        decimal NormalPoints,
        decimal NormalPointsR,
        decimal RawPoints,
        decimal RawDollars,
        decimal RawPointsR);

    private sealed record ExitPolicyResult(
        string ExitReason,
        decimal ExitPrice,
        decimal PnlPoints,
        decimal PnlR,
        bool Ambiguous);

    private sealed class ReplayExecutionState
    {
        public ReplayExecutionState(
            string tradeId,
            string signalId,
            TradeSide side,
            string researchPath,
            int createdBar,
            DateTime createdTime,
            decimal createdPrice,
            decimal quantity,
            decimal stop,
            decimal target,
            decimal initialRiskPoints,
            decimal maxAllowedRiskPoints,
            decimal targetR,
            Order entryOrder)
        {
            TradeId = tradeId;
            SignalId = signalId;
            Side = side;
            ResearchPath = researchPath;
            CreatedBar = createdBar;
            CreatedTime = createdTime;
            CreatedPrice = createdPrice;
            Quantity = quantity;
            Stop = stop;
            Target = target;
            InitialRiskPoints = initialRiskPoints;
            PlannedRiskPoints = initialRiskPoints;
            MaxAllowedRiskPoints = maxAllowedRiskPoints;
            PlannedTargetR = targetR;
            TargetR = targetR;
            EntryOrder = entryOrder;
        }

        public string TradeId { get; }
        public string SignalId { get; }
        public TradeSide Side { get; }
        public string ResearchPath { get; }
        public int CreatedBar { get; }
        public DateTime CreatedTime { get; }
        public decimal CreatedPrice { get; }
        public decimal Quantity { get; }
        public decimal Stop { get; set; }
        public decimal Target { get; set; }
        public decimal InitialRiskPoints { get; set; }
        public decimal PlannedRiskPoints { get; }
        public decimal MaxAllowedRiskPoints { get; }
        public decimal PlannedTargetR { get; }
        public decimal TargetR { get; set; }
        public Order? EntryOrder { get; set; }
        public decimal EntryFilledQty { get; set; }
        public decimal EntryAvgPrice { get; set; }
        public string? OcoGroup { get; set; }
        public Order? StopOrder { get; set; }
        public Order? TargetOrder { get; set; }
        public decimal BracketQty { get; set; }
        public decimal ExitFilledQty { get; set; }
        public decimal ExitAvgPrice { get; set; }
        public bool BracketSubmitted { get; set; }
        public bool ExitCompleted { get; set; }
        public int? ExitBar { get; set; }
        public decimal? ExitPrice { get; set; }
        public string? ExitRole { get; set; }
        public bool ProtectionCleanupPending { get; set; }
        public bool ProtectionCleanupDone { get; set; }
        public bool ProtectionCleanupInProgress { get; set; }
        public bool EntryAbortPending { get; set; }
        public bool PartialEntryFinalizeScheduled { get; set; }
        public bool ReplayStopExitPending { get; set; }
        public int ProtectionCleanupAttempts { get; set; }
        public int LastProtectionCleanupBar { get; set; } = -1;
        public string ProtectionCleanupReason { get; set; } = string.Empty;
        public bool EmergencyFlattenSubmitted { get; set; }
        public decimal EmergencyFlattenSubmittedQty { get; set; }
        public bool DuplicateExitFlattenSubmitted { get; set; }
        public decimal ActualMfePoints { get; private set; }
        public decimal ActualMaePoints { get; private set; }
        public decimal ActualMfeR => InitialRiskPoints <= 0m ? 0m : Math.Round(ActualMfePoints / InitialRiskPoints, 4);
        public decimal ActualMaeR => InitialRiskPoints <= 0m ? 0m : Math.Round(ActualMaePoints / InitialRiskPoints, 4);

        public void UpdateActualExcursion(OpfCandle candle)
        {
            if (EntryFilledQty <= 0m || ExitCompleted)
                return;

            var entry = EntryAvgPrice > 0m ? EntryAvgPrice : CreatedPrice;
            if (Side == TradeSide.Long)
            {
                ActualMfePoints = Math.Max(ActualMfePoints, candle.High - entry);
                ActualMaePoints = Math.Max(ActualMaePoints, entry - candle.Low);
            }
            else
            {
                ActualMfePoints = Math.Max(ActualMfePoints, entry - candle.Low);
                ActualMaePoints = Math.Max(ActualMaePoints, candle.High - entry);
            }
        }

        public void UpdateActualExcursion(decimal exitPrice)
        {
            if (EntryFilledQty <= 0m)
                return;

            var entry = EntryAvgPrice > 0m ? EntryAvgPrice : CreatedPrice;
            var points = Side == TradeSide.Long ? exitPrice - entry : entry - exitPrice;
            if (points >= 0m)
                ActualMfePoints = Math.Max(ActualMfePoints, points);
            else
                ActualMaePoints = Math.Max(ActualMaePoints, Math.Abs(points));
        }
    }

    private sealed record PullbackEpisode(string EpisodeId, TradeSide Side, int CountInRegime, int StartBar, decimal BreakPrice);

    private sealed class RegimeDailyStats
    {
        private decimal _bullScoreSum;
        private decimal _bearScoreSum;
        private MarketRegime? _lastRegime;

        public RegimeDailyStats(DateTime date)
        {
            Date = date;
        }

        public DateTime Date { get; }
        public int TotalBars { get; private set; }
        public int BullTrendBars { get; private set; }
        public int BearTrendBars { get; private set; }
        public int UnknownBars { get; private set; }
        public int RegimeChangeCount { get; private set; }
        public decimal AvgBullScore => TotalBars == 0 ? 0m : Math.Round(_bullScoreSum / TotalBars, 2);
        public decimal AvgBearScore => TotalBars == 0 ? 0m : Math.Round(_bearScoreSum / TotalBars, 2);

        public void Record(MarketRegime regime, decimal bullScore, decimal bearScore)
        {
            if (_lastRegime.HasValue && _lastRegime.Value != regime)
                RegimeChangeCount++;
            _lastRegime = regime;

            TotalBars++;
            _bullScoreSum += bullScore;
            _bearScoreSum += bearScore;

            if (regime == MarketRegime.BullTrend)
                BullTrendBars++;
            else if (regime == MarketRegime.BearTrend)
                BearTrendBars++;
            else
                UnknownBars++;
        }
    }

    private sealed class ResearchTracker
    {
        private readonly int _maxBars;

        public ResearchTracker(
            CandidateSignal signal,
            DateTime entryTime,
            int entryBar,
            decimal entry,
            decimal stop,
            decimal initialRiskPoints,
            int maxBars,
            string researchPath)
        {
            Signal = signal;
            EntryTime = entryTime;
            EntryBar = entryBar;
            Entry = entry;
            Stop = stop;
            InitialRiskPoints = initialRiskPoints;
            _maxBars = maxBars;
            ResearchPath = researchPath;
        }

        public CandidateSignal Signal { get; }
        public DateTime EntryTime { get; }
        public int EntryBar { get; }
        public decimal Entry { get; }
        public decimal Stop { get; }
        public decimal InitialRiskPoints { get; }
        public string ResearchPath { get; }
        public decimal MfePoints { get; private set; }
        public decimal MaePoints { get; private set; }
        public int? FirstStopBar { get; private set; }
        public int? First0_75RBar { get; private set; }
        public int? First1RBar { get; private set; }
        public int? First1_5RBar { get; private set; }
        public int? First2RBar { get; private set; }
        public int? First2_5RBar { get; private set; }
        public int? First3RBar { get; private set; }
        public int? FirstBreakEvenAfter1_5RBar { get; private set; }
        public int? First1RLockAfter1_5RBar { get; private set; }
        public int? FirstBreakEvenAfter0_75RBar { get; private set; }
        public int? FirstBreakEvenAfter1RBar { get; private set; }
        public int? TimeTo1RMinutes { get; private set; }
        public int? TimeToMfeMinutes { get; private set; }
        public decimal MaxHeatBefore1R { get; private set; }
        public bool StopHitBefore1R => FirstStopBar.HasValue && (!First1RBar.HasValue || FirstStopBar.Value <= First1RBar.Value);
        public bool AmbiguousStopAndTargetSameBar => FirstStopBar.HasValue && First1RBar.HasValue && FirstStopBar.Value == First1RBar.Value;
        public decimal MfeR => InitialRiskPoints <= 0m ? 0m : Math.Round(MfePoints / InitialRiskPoints, 4);
        public decimal MaeR => InitialRiskPoints <= 0m ? 0m : Math.Round(MaePoints / InitialRiskPoints, 4);
        public bool Hit1R => MfeR >= 1m;
        public bool Hit1_5R => MfeR >= 1.5m;
        public bool Hit2R => MfeR >= 2m;
        public bool Hit2_5R => MfeR >= 2.5m;
        public bool Hit3R => MfeR >= 3m;
        public string StopBasis => ResearchPath switch
        {
            "StructureConfirmShadow_ConfirmBarStop" => "ConfirmBar",
            "StructureConfirmShadow_ConfirmBarStop_Min10" => "ConfirmBarMin10",
            "StructureConfirmShadow_ConfirmBarStop_Wait1" => "ConfirmBarWait1",
            "StructureConfirmShadow_SwingStop" => "Swing5",
            _ => "Zone"
        };
        public int EntryDelayBars => ResearchPath == "StructureConfirmShadow_ConfirmBarStop_Wait1" ? 1 : 0;
        public string OutcomeClass
        {
            get
            {
                if (StopHitBefore1R)
                    return "Catastrophic";
                if (Hit2R)
                    return "Excellent";
                if (Hit1R)
                    return "Good";
                if (MfeR >= 0.5m)
                    return "Scratch";
                return "Poor";
            }
        }

        public void Update(OpfCandle candle)
        {
            var priorMfe = MfePoints;
            var had0_75RBeforeThisBar = First0_75RBar.HasValue && candle.Bar > First0_75RBar.Value;
            var had1RBeforeThisBar = First1RBar.HasValue && candle.Bar > First1RBar.Value;
            var had1_5RBeforeThisBar = First1_5RBar.HasValue && candle.Bar > First1_5RBar.Value;
            decimal currentMfe;
            decimal currentMae;

            if (Signal.Side == TradeSide.Long)
            {
                currentMfe = candle.High - Entry;
                currentMae = Entry - candle.Low;
                if (candle.Low <= Stop)
                    FirstStopBar ??= candle.Bar;
                if (had0_75RBeforeThisBar && candle.Low <= Entry)
                    FirstBreakEvenAfter0_75RBar ??= candle.Bar;
                if (had1RBeforeThisBar && candle.Low <= Entry)
                    FirstBreakEvenAfter1RBar ??= candle.Bar;
                if (had1_5RBeforeThisBar && candle.Low <= Entry)
                    FirstBreakEvenAfter1_5RBar ??= candle.Bar;
                if (had1_5RBeforeThisBar && candle.Low <= Entry + InitialRiskPoints)
                    First1RLockAfter1_5RBar ??= candle.Bar;
            }
            else
            {
                currentMfe = Entry - candle.Low;
                currentMae = candle.High - Entry;
                if (candle.High >= Stop)
                    FirstStopBar ??= candle.Bar;
                if (had0_75RBeforeThisBar && candle.High >= Entry)
                    FirstBreakEvenAfter0_75RBar ??= candle.Bar;
                if (had1RBeforeThisBar && candle.High >= Entry)
                    FirstBreakEvenAfter1RBar ??= candle.Bar;
                if (had1_5RBeforeThisBar && candle.High >= Entry)
                    FirstBreakEvenAfter1_5RBar ??= candle.Bar;
                if (had1_5RBeforeThisBar && candle.High >= Entry - InitialRiskPoints)
                    First1RLockAfter1_5RBar ??= candle.Bar;
            }

            MfePoints = Math.Max(MfePoints, currentMfe);
            MaePoints = Math.Max(MaePoints, currentMae);

            if (!First1RBar.HasValue)
                MaxHeatBefore1R = Math.Max(MaxHeatBefore1R, MaePoints);

            if (currentMfe >= 0.75m * InitialRiskPoints)
                First0_75RBar ??= candle.Bar;

            if (currentMfe >= InitialRiskPoints)
            {
                First1RBar ??= candle.Bar;
                TimeTo1RMinutes ??= MinutesFromEntry(candle.Time);
            }

            if (currentMfe >= 1.5m * InitialRiskPoints)
                First1_5RBar ??= candle.Bar;

            if (currentMfe >= 2m * InitialRiskPoints)
                First2RBar ??= candle.Bar;

            if (currentMfe >= 2.5m * InitialRiskPoints)
                First2_5RBar ??= candle.Bar;

            if (currentMfe >= 3m * InitialRiskPoints)
                First3RBar ??= candle.Bar;

            if (MfePoints > priorMfe)
                TimeToMfeMinutes = MinutesFromEntry(candle.Time);
        }

        public bool IsComplete(int bar)
        {
            return bar - EntryBar >= _maxBars;
        }

        private int MinutesFromEntry(DateTime time)
        {
            return Math.Max(0, (int)Math.Round((time - EntryTime).TotalMinutes));
        }
    }
}
