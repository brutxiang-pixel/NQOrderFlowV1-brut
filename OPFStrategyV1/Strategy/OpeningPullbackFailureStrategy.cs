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
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OPFStrategyV1.Strategy;

public sealed partial class OpeningPullbackFailureStrategy : ChartStrategy
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
    private const decimal PrimaryZoneBirthMinSetupQualityV221 = 45m;
    private const int PrimaryObservationConfirmMinRegimeBarsV223 = 8;
    private const decimal PrimaryBreakawayMinSetupQualityV223 = 88m;
    private const decimal PrimaryBreakawayMinRiskPointsV223 = 12m;
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
    private const int ObservationConfirmWideStopLongExpansionV163MaxTradesPerDay = 1;
    private const decimal ObservationConfirmWideStopLowRiskV132MinSetupQualityScore = 56m;
    private const decimal ObservationConfirmWideStopLowRiskV132MaxRiskPoints = 18m;
    private const decimal ObservationConfirmWideStopLowRiskV132MinEstimatedRr = 0.5m;
    private const decimal ObservationConfirmRisk22V132MinSetupQualityScore = 70m;
    private const decimal ObservationConfirmRisk22V132MaxRiskPoints = 22m;
    private const decimal ObservationConfirmRisk22V132MinEstimatedRr = 0.8m;
    private const decimal SelectiveProfitTargetV145R = 2.0m;
    private const decimal ObservationConfirmLongTarget2RV161MinRiskPoints = 8m;
    private const decimal ObservationConfirmLongTarget2RV161MaxRiskPoints = 11m;
    private const decimal ObservationConfirmLongTarget2RV161R = 2m;
    private const decimal AggressiveExpansionV164MaxRiskPoints = 25m;
    private const decimal AggressiveExpansionV164TargetR = 3m;
    private const decimal DynamicExpansionV168TargetR = 2.5m;
    private const decimal ZoneBirthShortV170LowRiskMaxPoints = 8m;
    private const decimal ZoneBirthShortV170MidRiskMinExclusivePoints = 12m;
    private const decimal ZoneBirthShortV170MidRiskMaxPoints = 18m;
    private const decimal ZoneBirthSplitRunnerV172TotalQuantity = 3m;
    private const decimal ZoneBirthSplitRunnerV172BaseQuantity = 2m;
    private const decimal ZoneBirthSplitRunnerV172RunnerQuantity = 1m;
    private const decimal ZoneBirthSplitRunnerV173TargetR = 4m;
    private const decimal StrictEntryExcludedRiskMinExclusiveV208 = 13.25m;
    private const decimal StrictEntryExcludedRiskMaxInclusiveV208 = 16m;
    private const bool EnableDeferredContinuationV209 = false;
    private const decimal DeferredContinuationTargetRV208 = 3m;
    private const int DeferredContinuationMaxBarsV208 = 18;
    private const decimal BreakawayShortTargetRV209 = 4m;
    private const decimal BreakawayShortBreakEvenTriggerRV209 = 1.5m;
    private const int LongPolicyTimeStopBarsV209 = 36;
    private const int ShortPolicyTimeStopBarsV209 = 36;
    private const int FastPolicyTimeStopBarsV209 = 12;
    private const int DecisionTapeCalibrationMaxBars = 36;
    private const decimal ProtectBreakEvenTriggerRV186 = 1m;
    private const decimal ProtectBreakEvenTargetRV186 = 3m;
    private static readonly bool UseLegacyV174StrategyPolicyV204 = true;
    private const decimal EntryFillRiskDriftTolerancePoints = 1m;
    private const decimal ShortObservationMidRiskQualityCutMinRisk = 8m;
    private const decimal ShortObservationMidRiskQualityCutMaxRisk = 15m;
    private const decimal ShortObservationMidRiskQualityCutMinScore = 60m;
    private const decimal ZoneQualityThreshold = 70m;
    private const decimal SetupQualityThreshold = 80m;
    private const int MaxPullbackBars = 24;
    private const int StaleUnfilledEntryMaxBars = 2;
    private static readonly TimeSpan GlobexCloseoutStartEastern = new(16, 50, 0);
    private static readonly TimeSpan GlobexReopenEastern = new(18, 0, 0);
    private static readonly TimeSpan SummerUsOpenBlockStartUtc = new(13, 30, 0);
    private static readonly TimeSpan WinterUsOpenBlockStartUtc = new(14, 30, 0);
    private static readonly TimeSpan UsOpenBlockDuration = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan MaxLiveMarketDataLatency = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxLiveOrdersLatency = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxLiveMarketDataSilence = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LiveLatencyRecoveryPeriod = TimeSpan.FromSeconds(10);
    private static readonly TimeZoneInfo EasternTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
    private const string ReplayStopExitRole = "SESSION_FLATTEN";
    private const int RichBoundaryBackfillBars = 72;
    private const string SignificantZoneFirstTouchLongPath = "SignificantZoneFirstTouchLong";
    private const string SignificantZoneFirstTouchShortPath = "SignificantZoneFirstTouchShort";
    private const decimal FailureReversePreEntryWideShortMinPenetrationR = 0.25m;
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
    private readonly List<PendingAggressiveExpansionWait1> _pendingAggressiveExpansionWait1s = new();
    private readonly List<PendingConfirmedRetrace> _pendingConfirmedRetraces = new();
    private readonly List<PendingFailureReverseRetest> _pendingFailureReverseRetests = new();
    private readonly List<PendingFailureReversePreEntryWideShort> _pendingFailureReversePreEntryWideShorts = new();
    private readonly SignificantZoneFirstTouchEngine _significantZoneFirstTouchEngine = new();
    private readonly List<PendingSignificantZoneFirstTouch> _pendingSignificantZoneFirstTouches = new();
    private readonly object _pendingProtectedSecondarySyncV216 = new();
    private PendingProtectedSecondaryV216? _pendingProtectedSecondaryV216;
    private readonly List<ResearchTracker> _researchTrackers = new();
    private readonly List<ShadowTradeTracker> _shadowTradeTrackers = new();
    private readonly Dictionary<string, ShadowTradeTracker> _shadowTradeTrackersByKey = new();
    private readonly List<DecisionTapeCalibrationTracker> _decisionTapeCalibrationTrackers = new();
    private readonly Dictionary<string, DecisionTapeCalibrationTracker> _decisionTapeCalibrationTrackersByKey = new();
    private readonly List<DecisionTapeMarketTurn> _decisionTapeMarketTurns = new();
    private long _decisionTapeMarketSequence;
    private int _decisionTapeTurnBar = -1;
    private decimal _decisionTapeTurnPrice;
    private long _decisionTapeTurnSequence;
    private int _decisionTapeTurnDirection;
    private readonly List<ReplayExecutionState> _recentReplayExecutions = new();
    private readonly Dictionary<string, ReplayExecutionState> _replayExecutionsByTradeId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ActualTradeOutcome> _actualTradeOutcomesBySignalPath = new();
    private readonly HashSet<string> _writtenExecutionTradeIds = new();
    private readonly HashSet<string> _writtenLiveAccountPnlTradeIds = new();
    private readonly HashSet<string> _writtenResearchOutcomeKeys = new();
    private readonly HashSet<string> _liveNotificationKeys = new();
    private readonly List<OpfCandle> _recentCandles = new();
    private readonly List<RichBarFeatureInput> _richBarFeatures = new();
    private readonly SemaphoreSlim _executionLock = new(1, 1);
    private PullbackEpisode? _activeBullPullback;
    private PullbackEpisode? _activeBearPullback;
    private decimal? _bullTrendHigh;
    private decimal? _bearTrendLow;
    private int _bullPullbackCountInRegime;
    private int _bearPullbackCountInRegime;
    private ResearchLogger? _researchLogger;
    private KnnShadowModel? _knnShadowModel;
    private readonly object _knnShadowInputSync = new();
    private readonly List<KnnShadowInput> _knnShadowInputs = new();
    private ConfigSnapshot? _snapshot;
    private readonly Dictionary<DateTime, RegimeDailyStats> _regimeDailyStatsByDate = new();
    private MarketRegime? _lastRegime;
    private int _lastRegimeChangeBar = -1;
    private decimal _lastRegimeChangeBullScore;
    private decimal _lastRegimeChangeBearScore;
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
    private decimal _latestCalculatedMarketPrice;
    private ReplayExecutionState? _replayExecution;
    private readonly List<DeferredContinuationCandidate> _deferredContinuations = new();
    private long _deferredCaptureSequence;
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
    private decimal _liveAccountDailyNetPnlDollars;
    private DateTime _liveAccountPnlDate = DateTime.MinValue;
    private decimal _liveAccountWeeklyNetPnlDollars;
    private decimal _liveLongWeeklyNetPnlDollars;
    private DateTime _liveAccountPnlWeekStart = DateTime.MinValue;
    private bool _weeklyLongLossGateTriggered;
    private string _weeklyLongLossGateTrigger = string.Empty;
    private int _replayAbnormalEntryToday;
    private string _lastAbnormalEntryHudText = "-";
    private int _replayAbnormalProtectiveFillToday;
    private string _lastAbnormalProtectiveFillHudText = "-";
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
    private decimal _actualDailyLossLimitDollars = 200m;
    private decimal _actualWeeklyLongLossLimitDollars = 500m;
    private decimal _actualCommissionPerContractRoundTrip = 1.2m;
    private bool _actualRequireFailureRetest = true;
    private bool _compactResearchLogging;
    private bool _manualAlertEnabled;
    private readonly HashSet<string> _manualAlertKeys = new(StringComparer.Ordinal);
    private bool _isStoppingActualExecution;
    private bool _liveReadinessBlocked;
    private string _liveReadinessBlockReason = "Starting";
    private IDataFeedConnector? _subscribedConnector;
    private bool _latencyGateActive;
    private DateTime? _latencyHealthySinceUtc;
    private bool _orphanPositionFlattenPending;
    private int _orphanPositionFlattenLastBar = -1;
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
    [DisplayName("Manual Alert Mode")]
    public bool ManualAlertMode { get; set; }

    [Category("OPF Research")]
    [DisplayName("Rich Bar Data Collection Only")]
    public bool RichBarDataCollectionOnly { get; set; }

    [Category("OPF Research")]
    [DisplayName("Decision Tape Calibration Collection")]
    public bool DecisionTapeCalibrationCollection { get; set; } = true;

    [Category("OPF Research")]
    [DisplayName("Market Execution Tape Data Only")]
    public bool MarketExecutionTapeDataOnly { get; set; }

    [Category("OPF Research")]
    [DisplayName("Candidate Scenario Tape Data Only")]
    public bool CandidateScenarioTapeDataOnly { get; set; }

    [Category("OPF Research")]
    [DisplayName("Microstructure Audit Collection Only")]
    public bool MicrostructureAuditCollectionOnly { get; set; }

    [Category("OPF Research")]
    [DisplayName("Footprint Data Collection Only")]
    public bool FootprintDataCollectionOnly { get; set; }

    [Category("OPF Research")]
    [DisplayName("Sweep-Reclaim Data Collection Only")]
    public bool SweepReclaimDataCollectionOnly { get; set; }

    [Category("OPF Research")]
    [DisplayName("Zone Behavior Ledger Data Collection Only")]
    public bool ZoneBehaviorLedgerDataOnly { get; set; }

    private bool ActualOrdersEnabled => EnableReplayOrders && !RichBarDataCollectionOnly && !MicrostructureAuditCollectionOnly && !FootprintDataCollectionOnly && !SweepReclaimDataCollectionOnly && !ZoneBehaviorLedgerDataOnly && !MarketExecutionTapeDataOnly && !CandidateScenarioTapeDataOnly;

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

        _pendingFailureReversePreEntryWideShorts.Clear();
        _decisionTapeMarketTurns.Clear();
        _decisionTapeMarketSequence = 0;
        _decisionTapeTurnBar = -1;
        _decisionTapeTurnPrice = 0m;
        _decisionTapeTurnSequence = 0;
        _decisionTapeTurnDirection = 0;

        var actualExecutionSettings = ActualExecutionSettings.LoadOrCreateDefault(
            out var actualExecutionConfigPath,
            out var actualExecutionConfigStatus);
        ApplyActualExecutionSettings(actualExecutionSettings);
        ApplyRunMode(actualExecutionSettings.RunMode);
        ApplyChartExecutionModeOverride();
        InitializeMicrostructureAudit();
        InitializeFootprintCollection();
        InitializeSweepReclaimCollection();
        InitializeZoneBehaviorLedger();
        RestoreHistoricalSignificantZones();
        InitializeMarketExecutionTape();

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
            _researchLogger.AppendInfo(
                _snapshot.SnapshotId,
                0,
                DateTime.UtcNow,
                $"RUN_PROFILE id={actualExecutionSettings.RunProfileId} mode={actualExecutionSettings.RunMode} actualOrders={ActualOrdersEnabled.ToString().ToLowerInvariant()}");
            if (RichBarDataCollectionOnly)
            {
                _researchLogger.AppendInfo(
                    _snapshot.SnapshotId,
                    0,
                    DateTime.UtcNow,
                    "RICH_BAR_DATA_COLLECTION_ONLY enabled=true actualOrders=false");
            }
            if (MicrostructureAuditCollectionOnly)
            {
                _researchLogger.AppendInfo(
                    _snapshot.SnapshotId,
                    0,
                    DateTime.UtcNow,
                    "MICROSTRUCTURE_AUDIT_COLLECTION_ONLY enabled=true actualOrders=false sources=OnNewTrade|OnNewTrades|MarketDepthChanged|MarketDepthsChanged|OnBestBidAskChanged");
            }
            if (FootprintDataCollectionOnly)
            {
                _researchLogger.AppendInfo(
                    _snapshot.SnapshotId,
                    0,
                    DateTime.UtcNow,
                    "FOOTPRINT_DATA_COLLECTION_ONLY enabled=true actualOrders=false source=OnNewTrade candidateLane=ResearchCandidate join=baselineExecutionEvidence");
            }
            if (SweepReclaimDataCollectionOnly)
            {
                _researchLogger.AppendInfo(
                    _snapshot.SnapshotId,
                    0,
                    DateTime.UtcNow,
                    "SWEEP_RECLAIM_DATA_COLLECTION_ONLY enabled=true actualOrders=false source=ClosedM5+OnNewTrade label=nextBarOpen/1.5R/12Bars");
            }
            if (ZoneBehaviorLedgerDataOnly)
            {
                _researchLogger.AppendInfo(
                    _snapshot.SnapshotId,
                    0,
                    DateTime.UtcNow,
                    "ZONE_BEHAVIOR_LEDGER_DATA_COLLECTION_ONLY enabled=true actualOrders=false source=ClosedM5+OnNewTrade dom=false");
            }
            if (MarketExecutionTapeDataOnly)
            {
                _researchLogger.AppendInfo(
                    _snapshot.SnapshotId,
                    0,
                    DateTime.UtcNow,
                    "MARKET_EXECUTION_TAPE_DATA_ONLY enabled=true actualOrders=false source=OnNewTrade+candidateScenario+zoneContext");
            }
            if (CandidateScenarioTapeDataOnly)
            {
                _researchLogger.AppendInfo(
                    _snapshot.SnapshotId,
                    0,
                    DateTime.UtcNow,
                    "CANDIDATE_SCENARIO_TAPE_DATA_ONLY enabled=true actualOrders=false source=OnNewTrade+candidateScenario+zoneContext tickOutput=false zoneOutput=false");
            }
            if (DecisionTapeCalibrationCollection)
            {
                _researchLogger.AppendInfo(
                    _snapshot.SnapshotId,
                    0,
                    DateTime.UtcNow,
                    $"DECISION_TAPE_CALIBRATION_COLLECTION enabled=true actualOrders={ActualOrdersEnabled.ToString().ToLowerInvariant()} maxBars={DecisionTapeCalibrationMaxBars}");
            }

            try
            {
                _knnShadowModel = KnnShadowModel.LoadEmbedded();
                _researchLogger.AppendInfo(
                    _snapshot.SnapshotId,
                    0,
                    DateTime.UtcNow,
                    $"KNN_SHADOW_MODEL_LOADED version={_knnShadowModel.ModelVersion} sha256={_knnShadowModel.Sha256}");
            }
            catch (Exception ex)
            {
                _researchLogger.AppendInfo(
                    _snapshot.SnapshotId,
                    0,
                    DateTime.UtcNow,
                    $"KNN_SHADOW_MODEL_LOAD_FAILED type={ex.GetType().Name} message={ex.Message}");
            }

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

        SubscribeConnectorEvents();
        ReconcileLiveExecutionState("Started");
        var restoreTime = _lastResearchCandle?.Time ?? DateTime.UtcNow;
        var restoreTradingDay = GlobexTradingDayKey(restoreTime);
        RestoreLiveDailyNetPnl(restoreTradingDay);
        RestoreLiveWeeklyNetPnl(restoreTradingDay);
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
        _manualAlertEnabled = settings.ManualAlertEnabled;
        ReplayExecutionPath = settings.ActualExecutionPaths;
        ReplayAllowResearchPaths = settings.ActualAllowResearchPaths;
        ReplayOrderQuantity = settings.ActualOrderQuantity;
        ReplayTargetR = settings.ActualTargetR;
        ReplayMaxTradesPerDay = settings.ActualMaxTradesPerDay;
        _actualDailyLossLimitDollars = settings.ActualDailyLossLimitDollars;
        _actualWeeklyLongLossLimitDollars = settings.ActualWeeklyLongLossLimitDollars;
        _actualCommissionPerContractRoundTrip = settings.ActualCommissionPerContractRoundTrip;
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

    private void ApplyChartExecutionModeOverride()
    {
        if (!ManualAlertMode)
            return;

        EnableReplayOrders = false;
        _manualAlertEnabled = true;
    }

    private void ApplyRunMode(string runMode)
    {
        RichBarDataCollectionOnly = false;
        MicrostructureAuditCollectionOnly = false;
        FootprintDataCollectionOnly = false;
        SweepReclaimDataCollectionOnly = false;
        ZoneBehaviorLedgerDataOnly = false;
        MarketExecutionTapeDataOnly = false;
        CandidateScenarioTapeDataOnly = false;
        DecisionTapeCalibrationCollection = false;

        if (string.Equals(runMode, "FootprintDataOnly", StringComparison.OrdinalIgnoreCase))
        {
            FootprintDataCollectionOnly = true;
            EnableReplayOrders = false;
            return;
        }

        if (string.Equals(runMode, "SweepReclaimDataOnly", StringComparison.OrdinalIgnoreCase))
        {
            SweepReclaimDataCollectionOnly = true;
            EnableReplayOrders = false;
            return;
        }

        if (string.Equals(runMode, "ZoneBehaviorLedgerDataOnly", StringComparison.OrdinalIgnoreCase))
        {
            ZoneBehaviorLedgerDataOnly = true;
            EnableReplayOrders = false;
            return;
        }

        if (string.Equals(runMode, "MarketExecutionTapeDataOnly", StringComparison.OrdinalIgnoreCase))
        {
            MarketExecutionTapeDataOnly = true;
            EnableReplayOrders = false;
            return;
        }

        if (string.Equals(runMode, "CandidateScenarioTapeDataOnly", StringComparison.OrdinalIgnoreCase))
        {
            CandidateScenarioTapeDataOnly = true;
            EnableReplayOrders = false;
            return;
        }

        if (!string.Equals(runMode, "ActualExecution", StringComparison.OrdinalIgnoreCase))
            EnableReplayOrders = false;
    }

    private void SubscribeConnectorEvents()
    {
        if (Connector is null)
        {
            SetLiveReadinessBlocked("ConnectorMissing", "Connector is unavailable; new Actual entries are blocked.");
            return;
        }

        _subscribedConnector = Connector;
        _subscribedConnector.Connected += OnConnectorConnected;
        _subscribedConnector.Disconnected += OnConnectorDisconnected;
        LogLiveReadinessInfo($"LIVE_CONNECTOR subscribed connected={_subscribedConnector.IsConnected} serverOco={_subscribedConnector.IsSupportedServerOCO}");
        if (!_subscribedConnector.IsSupportedServerOCO)
            NotifyLiveIssue("ServerOcoUnsupported", "Connector does not report server-side OCO support. Do not enter unattended live trading.");
    }

    private void UnsubscribeConnectorEvents()
    {
        if (_subscribedConnector is null)
            return;

        _subscribedConnector.Connected -= OnConnectorConnected;
        _subscribedConnector.Disconnected -= OnConnectorDisconnected;
        _subscribedConnector = null;
    }

    private void OnConnectorConnected(IDataFeedConnector connector)
    {
        SetLiveReadinessBlocked("ReconnectReconciling", "Connector restored; OPF is reconciling position and working orders before allowing new entries.");
        EnqueueExecutionAction("ReconnectReconcile", async () =>
        {
            await Task.Delay(500);
            ReconcileLiveExecutionState("Reconnected");
        });
    }

    private void OnConnectorDisconnected(IDataFeedConnector connector)
    {
        SetLiveReadinessBlocked("Disconnected", "Connector disconnected. New Actual entries are blocked until reconciliation succeeds.");
        AppendStandaloneExecutionEvent("CONNECTOR_DISCONNECTED", "-", _lastResearchCandle, 0m, 0m, connector.ConnectionState.ToString());
    }

    private void ReconcileLiveExecutionState(string source)
    {
        if (!ActualOrdersEnabled)
        {
            ClearLiveReadinessBlock(source);
            return;
        }
        if (Connector is null || !Connector.IsConnected)
        {
            SetLiveReadinessBlocked("Disconnected", $"{source}: connector is not connected.");
            return;
        }

        try
        {
            var workingOrders = Connector.Orders
                .Where(IsWorkingOpfOrderForCurrentContext)
                .ToArray();
            var activeExecutions = ActiveReplayExecutions();
            if (activeExecutions.Count > 0)
            {
                foreach (var order in workingOrders)
                    AttachReplayOrder(order, $"Reconcile:{source}");

                var expectedPosition = activeExecutions.Sum(execution =>
                    (execution.Side == TradeSide.Long ? 1m : -1m) * RemainingExecutionQuantity(execution));
                var actualPosition = GetCurrentAccountPosition();
                if (Math.Abs(actualPosition - expectedPosition) > 0.0000001m)
                {
                    SetLiveReadinessBlocked("ManagedPositionMismatch", $"{source}: managed position mismatch. actual={actualPosition:0.########}, expected={expectedPosition:0.########}.");
                    return;
                }

                var unprotected = activeExecutions.Where(execution => !HasRequiredWorkingStops(execution)).ToArray();
                if (actualPosition != 0m && unprotected.Length > 0)
                {
                    SetLiveReadinessBlocked("ProtectionMissing", $"{source}: active position has no valid OPF stop protection.");
                    ScheduleProtectionLossCheckIfNeeded($"Reconcile:{source}");
                    return;
                }

                ClearLiveReadinessBlock(source);
                foreach (var execution in activeExecutions)
                    AppendExecutionEvent(execution, "LIVE_RECONCILE_OK", "-", 0m, RemainingExecutionQuantity(execution), $"source={source}|working={workingOrders.Length}|activeSlots={activeExecutions.Count}");
                return;
            }

            if (GetCurrentAccountPosition() != 0m || workingOrders.Length > 0)
            {
                var reason = $"{source}: unmanaged position={GetCurrentAccountPosition():0.########}, workingOpfOrders={workingOrders.Length}. Manual intervention is required.";
                SetLiveReadinessBlocked("UnmanagedAccountState", reason);
                AppendStandaloneExecutionEvent("LIVE_RECONCILE_BLOCKED", "-", _lastResearchCandle, 0m, Math.Abs(GetCurrentAccountPosition()), reason);
                return;
            }

            ClearLiveReadinessBlock(source);
            AppendStandaloneExecutionEvent("LIVE_RECONCILE_OK", "-", _lastResearchCandle, 0m, 0m, $"source={source}|flat=true|working=0");
        }
        catch (Exception ex)
        {
            SetLiveReadinessBlocked("ReconcileFailed", $"{source}: reconciliation failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private bool IsWorkingOpfOrderForCurrentContext(Order order)
    {
        if (!IsWorkingExecutionOrder(order) || !TryParseExecutionComment(order.Comment, out _, out _))
            return false;
        return Equals(order.Portfolio, Portfolio) && Equals(order.Security, Security);
    }

    private void SetLiveReadinessBlocked(string reason, string message)
    {
        _liveReadinessBlocked = true;
        _liveReadinessBlockReason = reason;
        LogLiveReadinessInfo($"LIVE_READINESS_BLOCK reason={reason} msg={message}");
        NotifyLiveIssue($"LiveBlock:{reason}", message);
    }

    private void ClearLiveReadinessBlock(string source)
    {
        var wasBlocked = _liveReadinessBlocked;
        var previousReason = _liveReadinessBlockReason;
        _liveReadinessBlocked = false;
        _liveReadinessBlockReason = "-";
        if (!wasBlocked)
            return;

        _liveNotificationKeys.Remove($"LiveBlock:{previousReason}");
        LogLiveReadinessInfo($"LIVE_READINESS_RECOVERED source={source}");
        RaiseShowNotification($"Live readiness reconciliation passed ({source}). New Actual entries are enabled.", "OPFStrategyV1");
    }

    private void NotifyLiveIssue(string key, string message)
    {
        if (!_liveNotificationKeys.Add(key))
            return;
        RaiseShowNotification(message, "OPFStrategyV1");
    }

    private void LogLiveReadinessInfo(string message)
    {
        if (_snapshot is null)
            return;
        var candle = _lastResearchCandle;
        _researchLogger?.AppendInfo(_snapshot.SnapshotId, candle?.Bar ?? 0, candle?.Time ?? DateTime.UtcNow, message);
    }

    protected override void OnStopped()
    {
        _isStoppingActualExecution = true;
        _pendingSignificantZoneFirstTouches.Clear();
        _pendingFailureReversePreEntryWideShorts.Clear();
        ProcessClosedBar(_lastSeenBar);
        ClearPendingProtectedSecondaryV216("StrategyStopped");
        FinalizeActiveExecutionOnStop();
        WriteRegimeDailyStats();
        FlushResearchTrackers("ResearchStopped");
        FlushShadowTradeTrackers("StrategyStopped");
        FlushDecisionTapeCalibrationTrackers("StrategyStopped");
        FlushDecisionTapeMarketTurns();
        FlushMicrostructureAudit();
        _researchLogger?.FlushMicrostructureAudit();
        FlushFootprintCollection();
        _researchLogger?.FlushFootprintFeatures();
        FinalizeSweepReclaimCollection();
        _researchLogger?.FlushSweepReclaim();
        FinalizeZoneBehaviorLedger();
        _researchLogger?.FlushZoneBehaviorLedger();
        FlushMarketExecutionScenarios();
        _researchLogger?.FlushMarketExecutionTape();
        _researchLogger?.FlushShadowTrades();
        _researchLogger?.FlushDecisionTapeCalibration();
        FlushKnnShadowInputs();
        _researchLogger?.FlushKnnShadowDecisions();
        FlushRichBarFeatures();
        _researchLogger?.FlushRichBarFeatures();
        UnsubscribeConnectorEvents();
        base.OnStopped();
    }

    protected override void OnCalculate(int bar, decimal value)
    {
        CaptureDecisionTapeMarketTurn(bar, value);
        var isNewBar = bar > _lastSeenBar;
        _lastSeenBar = Math.Max(_lastSeenBar, bar);
        if (isNewBar)
            CaptureMicrostructureDepthSnapshot(bar);
        if (value > 0m)
            _latestCalculatedMarketPrice = value;
        UpdateActiveProtectionObservation(value);
        ScheduleProtectBreakEvenV186(value);
        ScheduleHistoricalVirtualTargetsV220();
        ProcessClosedBar(bar - 1);
        if (isNewBar)
            TryActivateSignificantZoneFirstTouches(bar);
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
        RollGlobexTradingDayIfNeeded(current);
        AllowHistoricalReplayConnectorBypass(current);
        UpdateActualExecutionExcursion(current);
        ScheduleProtectBreakEvenV186(current.Close);
        ScheduleHistoricalVirtualTargetsV220();
        ScheduleHistoricalReplayAccountFlatReconciliationIfNeeded(current);
        ScheduleStaleUnfilledEntryAbortIfNeeded(current);
        SchedulePolicyTimeStopV209(current);
        ScheduleGlobexCloseoutIfNeeded(current);
        ScheduleOrphanPositionFlattenIfNeeded(current);
        ScheduleProtectionCleanupIfNeeded("ClosedBar");
        ExpirePendingProtectedSecondaryV216(current);
        var zones = _zoneDetector.Update(current);
        var zoneLifecycleEvents = (_zoneDetector as IZoneLifecycleEventSource)?.DrainLifecycleEvents() ?? Array.Empty<ZoneLifecycleEvent>();
        var regime = _trendScoreEngine.Update(current);

        UpdateSweepReclaimCollection(current);
        UpdateZoneBehaviorLedger(current, zones, zoneLifecycleEvents);
        UpdateSignificantZones(current, regime, zones);

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
            _lastRegimeChangeBullScore = regime.BullTrendScore.TotalScore;
            _lastRegimeChangeBearScore = regime.BearTrendScore.TotalScore;
            _lastRegimeChangeBar = bar;
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
        EvaluateFailureReversePreEntryWideShorts(current);
        EvaluateFailureReverseRetests(current);
        EvaluateStructureConfirmShadows(current);
        EvaluateConfirmBarWait1s(current);
        EvaluateAggressiveExpansionWait1s(current);
        UpdateShadowTradeTrackers(current);
        UpdateDecisionTapeCalibrationTrackers(current);

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
        BufferRichBarFeature(current, regime);
        AddRecentCandle(current);
    }

    protected override void OnRender(RenderContext context, DrawingLayouts layout)
    {
        if (ChartInfo is null)
            return;

        if (ShowActualOrderLines)
            DrawActualExecutionLines(context);

        DrawSignificantZones(context);

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
        if (!ActualOrdersEnabled)
            return;

        var trade = myTrade.Clone();
        EnqueueExecutionAction("HandleReplayTrade", async () => await HandleReplayTradeAsync(trade));
    }

    protected override void OnOrderRegisterFailed(Order order, string message)
    {
        base.OnOrderRegisterFailed(order, message);
        var role = ParseExecutionRole(order?.Comment);
        var execution = ExecutionForOrder(order);
        LogExecutionInfo($"EXEC_REGISTER_FAILED role={role} ext={order?.ExtId} msg={message}");
        AppendOrderFailureEvent(order, "REGISTER_FAIL", message);
        NotifyLiveIssue($"RegisterFail:{order?.ExtId}:{role}", $"Order register failed. role={role}, message={message}");
        if (IsReplayExecutionActive(execution) &&
            (IsExitOrder(order) || string.Equals(role, "TIME_STOP", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(role, ReplayStopExitRole, StringComparison.OrdinalIgnoreCase)))
            EnqueueExecutionAction("FlattenOnProtectionRegisterFailed", async () => await SubmitEmergencyFlattenAsync(execution!, order?.QuantityToFill ?? 0m, "ProtectionRegisterFailed"));

        if (IsReplayExecutionActive(execution) && string.Equals(role, "FLATTEN", StringComparison.OrdinalIgnoreCase))
            SetLiveReadinessBlocked("EmergencyFlattenFailed", $"Emergency flatten order registration failed: {message}. Manual intervention is required immediately.");
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
        NotifyLiveIssue($"CancelFail:{order?.ExtId}", $"Order cancel failed. role={ParseExecutionRole(order?.Comment)}, message={message}");
    }

    protected override void OnOrderModifyFailed(Order order, Order newOrder, string message)
    {
        base.OnOrderModifyFailed(order, newOrder, message);
        var notifiedOrder = newOrder ?? order;
        NotifyLiveIssue($"ModifyFail:{notifiedOrder?.ExtId}", $"Order modify failed. role={ParseExecutionRole(notifiedOrder?.Comment)}, message={message}");
        var failedOrder = newOrder ?? order;
        var execution = ExecutionForOrder(failedOrder);
        if (!ActualOrdersEnabled || execution is null)
            return;

        var failedRole = ParseExecutionRole(failedOrder?.Comment);
        if (execution.ProtectBreakEvenPendingV186 && string.Equals(failedRole, "SL", StringComparison.OrdinalIgnoreCase))
        {
            execution.ProtectBreakEvenPendingV186 = false;
            var eventName = ProtectBreakEvenEventName(execution, "MODIFY_FAILED");
            LogExecutionInfo($"EXEC_{eventName} trade={execution.TradeId} msg={message}");
            AppendExecutionEvent(execution, eventName, "SL", execution.ProtectPendingStopV186, execution.BracketQty, message);
            return;
        }
        if (!execution.RunnerBreakEvenPending)
            return;
        if (!IsActiveExecutionOrder(failedOrder) ||
            !string.Equals(failedRole, "RUNNER_SL", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        execution.RunnerBreakEvenPending = false;
        LogExecutionInfo($"EXEC_ZONEBIRTH_RUNNER_V173_MODIFY_FAILED trade={execution.TradeId} msg={message}");
        AppendExecutionEvent(execution, "ZONEBIRTH_RUNNER_V173_MODIFY_FAILED", "RUNNER_SL", execution.RunnerPendingStop, ZoneBirthSplitRunnerV172RunnerQuantity, message);
    }

    private void AttachReplayOrder(Order? order, string source)
    {
        if (!ActualOrdersEnabled || order is null)
            return;
        if (!TryParseExecutionComment(order.Comment, out var tradeId, out var role))
            return;
        var execution = FindReplayExecution(tradeId);
        if (execution is null)
            return;

        if (ShouldIgnoreStaleHistoricalTargetOrderV183(execution, role, order, out var staleTargetReason))
        {
            LogExecutionInfo($"EXEC_HISTORICAL_DORMANT_TP_STALE_ORDER_IGNORED_V183 trade={tradeId} role={role} {staleTargetReason} src={source}");
            AppendExecutionEvent(execution, "HISTORICAL_DORMANT_TP_STALE_ORDER_IGNORED_V183", role, order.Price, order.QuantityToFill, $"{staleTargetReason}|src={source}");
            return;
        }
        if (ShouldIgnoreStaleProtectBreakEvenOrderV186(execution, role, order, out var staleStopReason))
        {
            LogExecutionInfo($"EXEC_PROTECT_BE1R_STALE_ORDER_IGNORED_V186 trade={tradeId} role={role} {staleStopReason} src={source}");
            AppendExecutionEvent(execution, "PROTECT_BE1R_STALE_ORDER_IGNORED_V186", role, order.TriggerPrice, order.QuantityToFill, $"{staleStopReason}|src={source}");
            return;
        }

        if (role == "ENTRY")
            execution.EntryOrder = order;
        else if (role == "SL")
        {
            execution.StopOrder = order;
            TryMarkProtectBreakEvenV186Applied(execution, order, source);
        }
        else if (role == "TP")
            execution.TargetOrder = order;
        else if (role == "BASE_SL")
            execution.StopOrder = order;
        else if (role == "BASE_TP")
            execution.TargetOrder = order;
        else if (role == "RUNNER_SL")
        {
            if (execution.RunnerBreakEvenApplied &&
                _snapshot is not null &&
                execution.RunnerStopOrder is not null &&
                order.ExtId != execution.RunnerStopOrder.ExtId &&
                Math.Abs(order.TriggerPrice - execution.RunnerStop) > _snapshot.InstrumentProfile.TickSize / 2m)
            {
                LogExecutionInfo($"EXEC_ZONEBIRTH_RUNNER_V173_STALE_ORDER_IGNORED trade={tradeId} ext={order.ExtId} trigger={order.TriggerPrice:0.########} currentExt={execution.RunnerStopOrder.ExtId} currentStop={execution.RunnerStop:0.########} src={source}");
                AppendExecutionEvent(execution, "ZONEBIRTH_RUNNER_V173_STALE_ORDER_IGNORED", "RUNNER_SL", order.TriggerPrice, order.QuantityToFill, $"ext={order.ExtId}|currentExt={execution.RunnerStopOrder.ExtId}|src={source}");
                return;
            }
            execution.RunnerStopOrder = order;
            TryMarkZoneBirthRunnerBreakEvenV173Applied(execution, order, source);
        }
        else if (role == "RUNNER_TP")
            execution.RunnerTargetOrder = order;
        else if (role == "TIME_STOP")
            execution.TimeStopOrderV208 = order;
        else if (role is "FLATTEN" or ReplayStopExitRole)
        {
            execution.EmergencyFlattenOrder = order;
            if (!IsWorkingExecutionOrder(order) && !execution.ExitCompleted &&
                execution.EntryFilledQty > execution.ExitFilledQty)
            {
                ScheduleEmergencyFlattenResidualCheck(execution, $"OrderInactive:{role}");
            }
        }

        MarkProtectiveExitCallbackPendingV187(execution, role, order, source);

        LogExecutionInfo($"EXEC_ORDER_ATTACH src={source} trade={tradeId} role={role} state={order.State} ext={order.ExtId} type={order.Type} dir={order.Direction} price={order.Price:0.########} trig={order.TriggerPrice:0.########} unfilled={order.Unfilled:0.########} qty={order.QuantityToFill:0.########}");
        if (IsFailedOrderState(order.State.ToString()))
        {
            var eventName = IsExpectedOcoSiblingInactive(order)
                ? "OCO_SIBLING_INACTIVE"
                : "ORDER_STATE_FAILED";
            AppendExecutionEvent(execution, eventName, role, order.Price > 0m ? order.Price : order.TriggerPrice, order.QuantityToFill, $"src={source}|state={order.State}|ext={order.ExtId}");
            if (!string.Equals(eventName, "OCO_SIBLING_INACTIVE", StringComparison.Ordinal))
            {
                NotifyLiveIssue($"OrderStateFailed:{order.ExtId}:{role}", $"Order entered a failed state. role={role}, state={order.State}, ext={order.ExtId}");
                if (IsStopExitRole(role))
                    EnqueueExecutionAction("FlattenOnStopStateFailed", async () => await SubmitEmergencyFlattenAsync(execution, RemainingExecutionQuantity(execution), $"StopStateFailed:{role}"));
            }
        }
    }

    private static bool ShouldIgnoreStaleHistoricalTargetOrderV183(
        ReplayExecutionState execution,
        string role,
        Order order,
        out string reason)
    {
        reason = string.Empty;
        if (!IsTargetExitRole(role))
            return false;

        var current = TargetOrderForRoleV180(execution, role);
        if (current is null || current.ExtId == order.ExtId)
            return false;

        if (execution.HistoricalObservedTargetExitRolesV206.Contains(role) &&
            current.Type == OrderTypes.Market &&
            order.Type != OrderTypes.Market)
        {
            reason = $"observedTargetMarketExt={current.ExtId}|staleDormantExt={order.ExtId}|staleDormantPrice={order.Price:0.########}";
            return true;
        }

        var intendedTarget = ExpectedExitPrice(execution, role);
        if (intendedTarget <= 0m)
            return false;

        var currentDrift = Math.Abs(current.Price - intendedTarget);
        var incomingDrift = Math.Abs(order.Price - intendedTarget);
        if (currentDrift >= incomingDrift)
            return false;

        reason = $"currentExt={current.ExtId}|staleExt={order.ExtId}|currentPrice={current.Price:0.########}|stalePrice={order.Price:0.########}|intended={intendedTarget:0.########}";
        return true;
    }

    private bool ShouldIgnoreStaleProtectBreakEvenOrderV186(
        ReplayExecutionState execution,
        string role,
        Order order,
        out string reason)
    {
        reason = string.Empty;
        if (!string.Equals(role, "SL", StringComparison.OrdinalIgnoreCase) ||
            !execution.ProtectBreakEvenAppliedV186 ||
            execution.StopOrder is null ||
            execution.StopOrder.ExtId == order.ExtId)
        {
            return false;
        }

        var tick = _snapshot?.InstrumentProfile.TickSize ?? 0.25m;
        if (Math.Abs(order.TriggerPrice - execution.Stop) <= tick / 2m)
            return false;

        reason = $"currentExt={execution.StopOrder.ExtId}|staleExt={order.ExtId}|currentStop={execution.Stop:0.########}|staleStop={order.TriggerPrice:0.########}";
        return true;
    }

    private void MarkProtectiveExitCallbackPendingV187(ReplayExecutionState execution, string role, Order order, string source)
    {
        if (!IsProtectiveExitRole(role) || execution.ExitCompleted || !IsFilledExecutionOrder(order))
            return;

        if (!execution.ProtectiveExitCallbackPendingV187)
        {
            execution.ProtectiveExitCallbackPendingV187 = true;
            LogExecutionInfo($"EXEC_PROTECTIVE_EXIT_CALLBACK_PENDING_V187 trade={execution.TradeId} role={role} ext={order.ExtId} src={source}");
            AppendExecutionEvent(execution, "PROTECTIVE_EXIT_CALLBACK_PENDING_V187", role, order.Price > 0m ? order.Price : order.TriggerPrice, order.QuantityToFill, $"ext={order.ExtId}|src={source}");
        }

        if (execution.ProtectiveExitCallbackCheckScheduledV187)
            return;

        execution.ProtectiveExitCallbackCheckScheduledV187 = true;
        _ = VerifyProtectiveExitCallbackAfterGraceV187Async(execution, role, order, source);
    }

    private async Task VerifyProtectiveExitCallbackAfterGraceV187Async(ReplayExecutionState execution, string role, Order order, string source)
    {
        await Task.Delay(2000);
        EnqueueExecutionAction("ProtectiveExitCallbackGraceV187", async () =>
        {
            execution.ProtectiveExitCallbackCheckScheduledV187 = false;
            if (execution.ExitCompleted)
            {
                execution.ProtectiveExitCallbackPendingV187 = false;
                return;
            }

            if (IsFilledExecutionOrder(order))
            {
                if (execution.ZoneBirthSplitRunnerV172)
                {
                    RecoverHistoricalReplaySplitLegIfFilled(execution, role, order, ExpectedExitPrice(execution, role));
                }
                else
                {
                    var missingQty = Math.Min(RemainingExecutionQuantity(execution), order.QuantityToFill);
                    if (missingQty > 0m)
                    {
                        var recoveredPrice = ExpectedExitPrice(execution, role);
                        var oldExitValue = execution.ExitAvgPrice * execution.ExitFilledQty;
                        execution.ExitFilledQty += missingQty;
                        execution.ExitAvgPrice = (oldExitValue + recoveredPrice * missingQty) / execution.ExitFilledQty;
                        AppendExecutionEvent(execution, "PROTECTIVE_EXIT_CALLBACK_RECOVERED_V215", role, recoveredPrice, missingQty, $"ext={order.ExtId}|state={order.State}|src={source}");
                    }
                }

                var completeQty = execution.BracketQty > 0m ? execution.BracketQty : execution.Quantity;
                if (execution.ExitFilledQty + 0.0000001m >= completeQty)
                {
                    execution.ProtectiveExitCallbackPendingV187 = false;
                    var recoveredPrice = execution.ExitAvgPrice > 0m ? execution.ExitAvgPrice : ExpectedExitPrice(execution, role);
                    await FinalizeCompletedExecutionExitAsync(execution, recoveredPrice, role);
                    return;
                }
            }

            var position = GetCurrentAccountPosition();
            if (position == 0m)
            {
                AppendExecutionEvent(execution, "PROTECTIVE_EXIT_ACCOUNT_FLAT_CALLBACK_PENDING_V187", role, 0m, 0m, $"ext={order.ExtId}|src={source}");
                return;
            }
            if (HasRequiredWorkingStops(execution))
            {
                execution.ProtectiveExitCallbackPendingV187 = false;
                return;
            }


            var expectedPosition = ActiveReplayExecutions().Sum(active =>
                (active.Side == TradeSide.Long ? 1m : -1m) * RemainingExecutionQuantity(active));
            if (Math.Abs(position) + 0.0000001m < Math.Abs(expectedPosition))
            {
                AppendExecutionEvent(execution, "PROTECTIVE_EXIT_CALLBACK_POSITION_REDUCED_V215", role, 0m, RemainingExecutionQuantity(execution), $"position={position:0.########}|expected={expectedPosition:0.########}|ext={order.ExtId}|src={source}|noCrossSlotFlatten");
                return;
            }

            execution.ProtectiveExitCallbackPendingV187 = false;
            execution.EmergencyFlattenSubmitted = true;
            var remainingQty = RemainingExecutionQuantity(execution);
            LogExecutionInfo($"EXEC_PROTECTIVE_EXIT_CALLBACK_TIMEOUT_V187 trade={execution.TradeId} role={role} position={position:0.########} remaining={remainingQty:0.########}");
            AppendExecutionEvent(execution, "PROTECTIVE_EXIT_CALLBACK_TIMEOUT_V187", role, 0m, remainingQty, $"position={position:0.########}|ext={order.ExtId}|src={source}");
            NotifyLiveIssue($"ProtectiveExitCallbackTimeout:{execution.TradeId}", $"Protective order became terminal but its fill callback did not complete within the grace window. trade={execution.TradeId}, role={role}. Emergency flatten is being submitted.");
            await SubmitEmergencyFlattenAsync(execution, remainingQty, $"ProtectiveExitCallbackTimeout:{role}");
        });
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

        var date = GlobexTradingDayKey(candle.Time);
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
        _pendingBreakawayRetests.Add(new PendingBreakawayRetest(signal, candle.Bar + 6, "BreakawayRetest", 6, false));
        foreach (var windowBars in new[] { 12, 18 })
        {
            var retestResearch = signal with
            {
                SignalId = $"{signal.SignalId}-RT{windowBars}",
                SkipReasons = signal.SkipReasons.Concat(new[] { $"BreakawayRetest{windowBars}Research" }).ToArray()
            };
            _pendingBreakawayRetests.Add(new PendingBreakawayRetest(
                retestResearch,
                candle.Bar + windowBars,
                $"BreakawayRetest{windowBars}Research",
                windowBars,
                true));
        }
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
                _researchLogger?.AppendNoTrade(_snapshot.SnapshotId, signal.SignalId, candle.Time, candle.Bar, pending.ResearchPath, BreakawayRetestLogReasons(pending, "BreakawayZoneInvalidated"));
                _pendingBreakawayRetests.RemoveAt(i);
                continue;
            }

            var touched = IsZoneTouched(candle, signal.Zone);
            if (touched)
                pending.Touched = true;
            else if (pending.ResearchOnly && !pending.WaitingTouchLogged)
            {
                _researchLogger?.AppendNoTrade(_snapshot.SnapshotId, signal.SignalId, candle.Time, candle.Bar, pending.ResearchPath, new[] { "WaitingRetestTouch", $"WindowBars={pending.WindowBars}" });
                pending.WaitingTouchLogged = true;
            }

            (bool Confirmed, string Reason) confirm = pending.Touched
                ? TryConfirm(signal, candle, _previousCandle)
                : (false, "WaitingRetestTouch");
            if (confirm.Confirmed)
            {
                var qualityReason = BreakawayRetestQualityRejectReason(signal, candle);
                if (qualityReason is not null)
                {
                    _researchLogger?.AppendNoTrade(_snapshot.SnapshotId, signal.SignalId, candle.Time, candle.Bar, pending.ResearchPath, BreakawayRetestLogReasons(pending, qualityReason, confirm.Reason));
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
                    SkipReasons = pending.ResearchOnly
                        ? new[] { pending.ResearchPath, confirm.Reason, $"WindowBars={pending.WindowBars}" }
                        : new[] { "BreakawayRetestResearch", confirm.Reason }
                };

                _researchLogger?.AppendSignal(retestSignal);
                StartResearchTracking(retestSignal, candle, pending.ResearchPath, submitReplayExecution: !pending.ResearchOnly);
                _pendingBreakawayRetests.RemoveAt(i);
                continue;
            }

            if (candle.Bar >= pending.MaxRetestBar)
            {
                _researchLogger?.AppendNoTrade(_snapshot.SnapshotId, signal.SignalId, candle.Time, candle.Bar, pending.ResearchPath, BreakawayRetestLogReasons(pending, "BreakawayRetestExpired", confirm.Reason));
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
        TryArmFailureReversePreEntryWideShort(sourceSignal, failureSignal, candle);
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

    private void TryArmFailureReversePreEntryWideShort(CandidateSignal sourceSignal, CandidateSignal failureSignal, OpfCandle candle)
    {
        if (sourceSignal.Zone is null || sourceSignal.Zone.ZoneType != "BullFVG" || failureSignal.Side != TradeSide.Short)
            return;

        var baseRisk = Math.Abs(candle.Close - (sourceSignal.Zone.High + 0.50m));
        var wideMultiplier = ActualWideStopMultiplier <= 1m ? 1.5m : ActualWideStopMultiplier;
        var wideRisk = Math.Round(baseRisk * wideMultiplier, 2);
        if (wideRisk <= 0m)
            return;

        var penetrationR = Math.Round(Math.Max(0m, sourceSignal.Zone.Low - candle.Close) / wideRisk, 4);
        if (penetrationR < FailureReversePreEntryWideShortMinPenetrationR)
            return;

        var legacyPath = $"FailureReverse_ObservationInvalidated_WideStop{wideMultiplier:0.#}R".Replace(".", "_");
        var admission = TryEvaluateLegacyFailureReverseWideStopAdmission(
            failureSignal, candle, legacyPath, candle.Close + wideRisk, wideRisk);
        if (!admission.LegacyWouldSubmit)
        {
            _researchLogger?.AppendInfo(
                _snapshot?.SnapshotId ?? failureSignal.SnapshotId,
                candle.Bar,
                candle.Time,
                $"FR_PREENTRY_LEGACY_REJECTED signal={failureSignal.SignalId} path={legacyPath} reasons={string.Join("|", admission.Reasons)}");
            return;
        }

        _pendingFailureReversePreEntryWideShorts.Add(
            new PendingFailureReversePreEntryWideShort(failureSignal, candle, penetrationR, admission));
        _researchLogger?.AppendInfo(
            _snapshot?.SnapshotId ?? failureSignal.SnapshotId,
            candle.Bar,
            candle.Time,
            $"FAILURE_REVERSE_PREENTRY_ARMED signal={failureSignal.SignalId} path={legacyPath} originBar={candle.Bar} penetrationR={penetrationR:0.####} wideRisk={wideRisk:0.##} legacyPath={admission.LegacyResearchPath} legacyRisk={admission.PlannedRisk:0.##}");
    }

    private LegacyFailureReverseWideStopAdmission TryEvaluateLegacyFailureReverseWideStopAdmission(
        CandidateSignal signal,
        OpfCandle entryCandle,
        string researchPath,
        decimal stop,
        decimal risk)
    {
        LegacyFailureReverseWideStopAdmission Reject(params string[] reasons) =>
            new(false, reasons, researchPath, entryCandle.Close, stop, risk);

        if (!ActualOrdersEnabled || _snapshot is null)
            return Reject("ActualOrdersDisabledOrSnapshotMissing");
        if (_isStoppingActualExecution)
            return Reject("StrategyStopping");
        if (IsGlobexCloseoutLockWindow(entryCandle.Time, out var globexReason))
            return Reject(globexReason);
        if (IsUsCashOpenBlackout(entryCandle.Time, out var openReason))
            return Reject(openReason);
        if (IsLiveLatencyBlocked(entryCandle.Time, out var latencyReason))
            return Reject(latencyReason);
        if (_liveReadinessBlocked)
            return Reject($"LiveReadinessBlocked:{_liveReadinessBlockReason}");

        var baselinePathEnabled = string.Equals(researchPath, "FailureReverse_ObservationInvalidated_WideStop1_5R", StringComparison.OrdinalIgnoreCase) &&
            IsReplayExecutionPathEnabled("FailureReverse_PreEntryConfirmedWideStopShort");
        if (!baselinePathEnabled && !IsReplayExecutionPathEnabled(researchPath))
            return Reject("PathDisabled");
        if (!ReplayAllowResearchPaths && !IsExecutionEligiblePath(researchPath))
            return Reject("ResearchOnlyPath");

        var strategyReasons = ActualExecutionStrategySkipReasons(signal, researchPath);
        if (strategyReasons.Length > 0)
            return Reject(strategyReasons);
        var pathReasons = ActualExecutionPathSkipReasons(signal, researchPath);
        if (pathReasons.Length > 0)
            return Reject(pathReasons);
        var riskReasons = ActualExecutionRiskSkipReasons(signal, researchPath, entryCandle, risk);
        if (riskReasons.Length > 0)
            return Reject(riskReasons);
        var sameBarReasons = ActualExecutionSameBarSkipReasons(signal, researchPath, entryCandle, stop, risk);
        if (sameBarReasons.Length > 0 && !IsDailyVolumeSameBarTargetOnlyAllowed(signal, researchPath, sameBarReasons))
            return Reject(sameBarReasons);

        if (_actualDailyLossLimitDollars > 0m && _liveAccountDailyNetPnlDollars <= -_actualDailyLossLimitDollars)
            return Reject($"LiveDailyLoss:net={_liveAccountDailyNetPnlDollars:0.##},limit={_actualDailyLossLimitDollars:0.##}");
        if (signal.Side == TradeSide.Long && IsWeeklyLongLossGateActive(out var weeklyReason))
            return Reject(weeklyReason);
        if (ReplayMaxTradesPerDay > 0 && _replayTradesToday >= ReplayMaxTradesPerDay)
            return Reject($"DailyTradeLimit:max={ReplayMaxTradesPerDay}");

        var profile = _snapshot.ExecutionProfile;
        if (profile.StopAfterDailyTarget && profile.DailyTargetDollars > 0m && _replayDailyPnlDollars >= profile.DailyTargetDollars)
            return Reject($"DailyTarget:pnl={_replayDailyPnlDollars:0.##},target={profile.DailyTargetDollars:0.##}");
        if (profile.DailyLossLimitDollars > 0m && _replayDailyPnlDollars <= -profile.DailyLossLimitDollars)
            return Reject($"DailyLoss:pnl={_replayDailyPnlDollars:0.##},lossLimit={profile.DailyLossLimitDollars:0.##}");
        if (ReplayUseFullLossGuard && profile.MaxFullLossTradesPerDay > 0 && _replayFullLossTradesToday >= profile.MaxFullLossTradesPerDay)
            return Reject($"FullLossLimit:fullLosses={_replayFullLossTradesToday},max={profile.MaxFullLossTradesPerDay}");
        if (ReplayUseConsecutiveLossGuard && profile.MaxConsecutiveLossesPerDay > 0 && _replayConsecutiveLossesToday >= profile.MaxConsecutiveLossesPerDay)
            return Reject($"ConsecutiveLossLimit:consecLosses={_replayConsecutiveLossesToday},max={profile.MaxConsecutiveLossesPerDay}");

        var activeExecutions = ActiveReplayExecutions();
        if (activeExecutions.Count >= 2)
            return Reject($"ActiveTradeLimit:{string.Join(",", activeExecutions.Select(x => x.TradeId))}");
        if (activeExecutions.Count == 1)
        {
            var primary = activeExecutions[0];
            var quantity = Math.Max(0m, ReplayOrderQuantity);
            var combinedRisk = primary.InitialRiskPoints * _snapshot.InstrumentProfile.PointValue * RemainingExecutionQuantity(primary) +
                risk * _snapshot.InstrumentProfile.PointValue * quantity;
            if (primary.Side != signal.Side)
                return Reject($"SecondaryDirectionMismatch:primary={primary.Side}|candidate={signal.Side}");
            if (!IsStaticSecondaryEligibleV215(signal.Side, researchPath, risk))
                return Reject($"SecondaryStaticG2Blocked:path={researchPath}|side={signal.Side}|band={StaticRiskBandV215(risk)}");
            if (combinedRisk > 300m)
                return Reject($"SecondaryRiskCap:combined={combinedRisk:0.##}|max=300");
            if (!primary.BracketSubmitted || primary.EntryFilledQty <= primary.ExitFilledQty)
                return Reject($"SecondaryPrimaryNotProtected:primary={primary.TradeId}");
        }

        var currentPosition = GetCurrentAccountPosition();
        if (activeExecutions.Count == 0 && currentPosition != 0m)
            return Reject($"OrphanPosition:pos={currentPosition:0.########}");
        if (Portfolio is null || Security is null)
            return Reject("ContextMissing:portfolioOrSecurityNull");
        if (ReplayOrderQuantity <= 0m)
            return Reject("QuantityInvalid");

        return new LegacyFailureReverseWideStopAdmission(true, Array.Empty<string>(), researchPath, entryCandle.Close, stop, risk);
    }

    private void EvaluateFailureReversePreEntryWideShorts(OpfCandle candle)
    {
        if (_snapshot is null || _pendingFailureReversePreEntryWideShorts.Count == 0)
            return;

        for (var i = _pendingFailureReversePreEntryWideShorts.Count - 1; i >= 0; i--)
        {
            var pending = _pendingFailureReversePreEntryWideShorts[i];
            if (candle.Bar != pending.OriginCandle.Bar + 1)
            {
                if (candle.Bar > pending.OriginCandle.Bar + 1)
                {
                    _researchLogger?.AppendNoTrade(_snapshot.SnapshotId, pending.Signal.SignalId, candle.Time, candle.Bar,
                        "FailureReverse_PreEntryConfirmedWideStopShort", new[] { "FailureReversePreEntryConfirmationMissed" });
                    _pendingFailureReversePreEntryWideShorts.RemoveAt(i);
                }
                continue;
            }

            if (!pending.LegacyAdmission.LegacyWouldSubmit)
            {
                _researchLogger?.AppendNoTrade(_snapshot.SnapshotId, pending.Signal.SignalId, candle.Time, candle.Bar,
                    "FailureReverse_PreEntryConfirmedWideStopShort", new[] { "FR_PREENTRY_TOKEN_INVALID" });
                _pendingFailureReversePreEntryWideShorts.RemoveAt(i);
                continue;
            }
            if (pending.Signal.Zone is null || candle.Close >= pending.Signal.Zone.Low)
            {
                _researchLogger?.AppendNoTrade(_snapshot.SnapshotId, pending.Signal.SignalId, candle.Time, candle.Bar,
                    "FailureReverse_PreEntryConfirmedWideStopShort", new[] { "FailureReversePreEntryCloseReturnedIntoSourceZone" });
                _pendingFailureReversePreEntryWideShorts.RemoveAt(i);
                continue;
            }

            const string researchPath = "FailureReverse_PreEntryConfirmedWideStopShort";
            var multiplier = ActualWideStopMultiplier <= 1m ? 1.5m : ActualWideStopMultiplier;
            var baseRisk = Math.Abs(candle.Close - (pending.Signal.Zone.High + 0.50m));
            var wideRisk = Math.Round(baseRisk * multiplier, 2);
            if (wideRisk <= 0m)
            {
                _researchLogger?.AppendNoTrade(_snapshot.SnapshotId, pending.Signal.SignalId, candle.Time, candle.Bar,
                    researchPath, new[] { "FailureReversePreEntryInvalidConfirmationRisk" });
                _pendingFailureReversePreEntryWideShorts.RemoveAt(i);
                continue;
            }

            var confirmed = pending.Signal with
            {
                SignalId = $"{pending.Signal.SignalId}-PE1",
                Time = candle.Time,
                Bar = candle.Bar,
                Stage = SignalStage.Triggered,
                SkipReasons = pending.Signal.SkipReasons.Concat(new[]
                {
                    "FailureReversePreEntryConfirmed",
                    $"OriginBar={pending.OriginCandle.Bar}",
                    $"PenetrationR={pending.PenetrationR:0.####}",
                    $"LegacySignalId={pending.LegacySignalId}",
                    $"LegacyPath={pending.LegacyAdmission.LegacyResearchPath}",
                    $"LegacyEntry={pending.LegacyAdmission.PlannedEntry:0.########}",
                    $"LegacyStop={pending.LegacyAdmission.PlannedStop:0.########}",
                    $"LegacyRisk={pending.LegacyAdmission.PlannedRisk:0.########}"
                }).ToArray()
            };
            var wideStop = candle.Close + wideRisk;
            _researchLogger?.AppendSignal(confirmed);
            _researchLogger?.AppendInfo(_snapshot.SnapshotId, candle.Bar, candle.Time,
                $"FR_PREENTRY_CONFIRM signal={confirmed.SignalId} LegacySignalId={pending.LegacySignalId} LegacyPath={pending.LegacyAdmission.LegacyResearchPath} legacyEntry={pending.LegacyAdmission.PlannedEntry:0.########} legacyStop={pending.LegacyAdmission.PlannedStop:0.########} legacyRisk={pending.LegacyAdmission.PlannedRisk:0.########} confirmEntry={candle.Close:0.########} confirmRisk={wideRisk:0.########}");
            AddResearchTracker(confirmed, candle, researchPath, wideStop, wideRisk, submitReplayExecution: true);
            _pendingFailureReversePreEntryWideShorts.RemoveAt(i);
        }
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

    private void EvaluateAggressiveExpansionWait1s(OpfCandle candle)
    {
        if (_snapshot is null || _pendingAggressiveExpansionWait1s.Count == 0)
            return;

        for (var i = _pendingAggressiveExpansionWait1s.Count - 1; i >= 0; i--)
        {
            var pending = _pendingAggressiveExpansionWait1s[i];
            if (candle.Bar <= pending.OriginCandle.Bar)
                continue;

            _pendingAggressiveExpansionWait1s.RemoveAt(i);
            var waitSignal = pending.Signal with
            {
                SignalId = $"{pending.Signal.SignalId}-AEW1",
                Time = candle.Time,
                Bar = candle.Bar,
                Stage = SignalStage.Triggered,
                SkipReasons = pending.Signal.SkipReasons.Concat(new[]
                {
                    $"AggressiveExpansionWait1V165:originBar={pending.OriginCandle.Bar}",
                    "AggressiveExpansionWait1ConfirmBarStopV166",
                    $"OriginPath={pending.ResearchPath}"
                }).ToArray()
            };

            if (candle.Bar != pending.OriginCandle.Bar + 1)
            {
                var reason = $"AggressiveExpansionWait1ExpiredV165:originBar={pending.OriginCandle.Bar},bar={candle.Bar}";
                AppendExecutionDecision(waitSignal, candle, "Skip", reason, pending.ResearchPath, pending.OriginalStop, pending.OriginalRisk);
                AppendExecutionEvent(waitSignal, string.Empty, candle, "EXPANSION_WAIT1_REJECTED", "-", pending.ResearchPath, candle.Close, 0m, reason);
                continue;
            }

            var confirmationReason = AggressiveExpansionWait1ConfirmationRejectReason(waitSignal.Side, candle, pending.OriginCandle);
            if (!string.IsNullOrEmpty(confirmationReason))
            {
                AppendExecutionDecision(waitSignal, candle, "Skip", confirmationReason, pending.ResearchPath, pending.OriginalStop, pending.OriginalRisk);
                AppendExecutionEvent(waitSignal, string.Empty, candle, "EXPANSION_WAIT1_REJECTED", "-", pending.ResearchPath, candle.Close, 0m, confirmationReason);
                continue;
            }

            if (!TryGetAggressiveExpansionWait1Stop(waitSignal, pending.ResearchPath, candle, out var stop, out var stopReason))
            {
                AppendExecutionDecision(waitSignal, candle, "Skip", stopReason, pending.ResearchPath, pending.OriginalStop, pending.OriginalRisk);
                AppendExecutionEvent(waitSignal, string.Empty, candle, "EXPANSION_WAIT1_REJECTED", "-", pending.ResearchPath, candle.Close, 0m, stopReason);
                continue;
            }

            var risk = Math.Abs(candle.Close - stop);
            _researchLogger?.AppendSignal(waitSignal);
            _researchLogger?.AppendInfo(_snapshot.SnapshotId, candle.Bar, candle.Time, $"EXPANSION_WAIT1_RETRY_V166 signal={waitSignal.SignalId} path={pending.ResearchPath} originBar={pending.OriginCandle.Bar} risk={risk:0.##} stopModel=ConfirmBar");
            _researchLogger?.AppendInfo(_snapshot.SnapshotId, candle.Bar, candle.Time, $"EXPANSION_WAIT1_RESEARCH_ONLY_V167 signal={waitSignal.SignalId} path={pending.ResearchPath}");
            AddResearchTracker(waitSignal, candle, pending.ResearchPath, stop, risk, allowDelayedExpansion: false, submitReplayExecution: false);
        }
    }

    private bool TryScheduleAggressiveExpansionWait1(
        CandidateSignal signal,
        OpfCandle entryCandle,
        string researchPath,
        decimal stop,
        decimal risk,
        IReadOnlyList<string> sameBarSkipReasons)
    {
        if (!TryGetAggressiveExpansionV164Rules(signal.Side, researchPath, out _, out _, out _))
            return false;
        if (sameBarSkipReasons.Count == 0 || sameBarSkipReasons.Any(x => !x.StartsWith("EntryBar", StringComparison.Ordinal)))
            return false;
        if (_pendingAggressiveExpansionWait1s.Any(x =>
            string.Equals(x.Signal.SignalId, signal.SignalId, StringComparison.Ordinal) &&
            string.Equals(x.ResearchPath, researchPath, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        _pendingAggressiveExpansionWait1s.Add(new PendingAggressiveExpansionWait1(signal, entryCandle, researchPath, stop, risk));
        var reason = $"AggressiveExpansionWait1ScheduledV165:{string.Join("|", sameBarSkipReasons)}";
        AppendExecutionDecision(signal, entryCandle, "Skip", reason, researchPath, stop, risk);
        AppendExecutionEvent(signal, string.Empty, entryCandle, "EXPANSION_WAIT1_SCHEDULED", "-", researchPath, entryCandle.Close, 0m, reason);
        return true;
    }

    private static string AggressiveExpansionWait1ConfirmationRejectReason(TradeSide side, OpfCandle waitCandle, OpfCandle originCandle)
    {
        if (side == TradeSide.Long && waitCandle.Close < originCandle.Close)
            return $"AggressiveExpansionWait1LongFollowThroughFailedV165:close={waitCandle.Close:0.########},originClose={originCandle.Close:0.########}";
        if (side == TradeSide.Short && waitCandle.Close > originCandle.Close)
            return $"AggressiveExpansionWait1ShortFollowThroughFailedV165:close={waitCandle.Close:0.########},originClose={originCandle.Close:0.########}";

        return string.Empty;
    }

    private bool TryGetAggressiveExpansionWait1Stop(CandidateSignal signal, string researchPath, OpfCandle candle, out decimal stop, out string reason)
    {
        stop = 0m;
        reason = string.Empty;
        var confirmBarStop = TryGetConfirmBarStop(signal.Side, candle);
        if (confirmBarStop.Stop is null)
        {
            reason = $"AggressiveExpansionWait1ConfirmBarStopInvalidV166:{confirmBarStop.Reason}";
            return false;
        }

        var baseStop = confirmBarStop.Stop.Value;
        var baseRisk = Math.Abs(candle.Close - baseStop);
        if (baseRisk <= 0m)
        {
            reason = "AggressiveExpansionWait1InvalidConfirmBarRiskV166";
            return false;
        }

        if (researchPath.EndsWith("_WideStop1_5R", StringComparison.OrdinalIgnoreCase))
        {
            var wideMultiplier = ActualWideStopMultiplier <= 1m ? 1.5m : ActualWideStopMultiplier;
            var wideRisk = Math.Round(baseRisk * wideMultiplier, 2);
            stop = signal.Side == TradeSide.Long
                ? candle.Close - wideRisk
                : candle.Close + wideRisk;
        }
        else
        {
            stop = baseStop;
        }

        var valid = signal.Side == TradeSide.Long ? stop < candle.Close : stop > candle.Close;
        if (!valid)
        {
            reason = $"AggressiveExpansionWait1InvalidStopV166:entry={candle.Close:0.########},stop={stop:0.########}";
            return false;
        }

        return true;
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

    private void StartResearchTracking(CandidateSignal signal, OpfCandle entryCandle, string researchPath, bool submitReplayExecution = true)
    {
        if (signal.Zone is null)
            return;

        var entry = entryCandle.Close;
        var buffer = 0.50m;
        var stop = signal.Side == TradeSide.Long
            ? signal.Zone.Low - buffer
            : signal.Zone.High + buffer;
        StartResearchTracking(signal, entryCandle, researchPath, stop, submitReplayExecution);
    }

    private void StartResearchTracking(CandidateSignal signal, OpfCandle entryCandle, string researchPath, decimal stop, bool submitReplayExecution = true)
    {
        var entry = entryCandle.Close;
        var risk = Math.Abs(entry - stop);
        if (risk <= 0m)
            return;

        AddResearchTracker(signal, entryCandle, researchPath, stop, risk, submitReplayExecution: submitReplayExecution);

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

    private void AddResearchTracker(
        CandidateSignal signal,
        OpfCandle entryCandle,
        string researchPath,
        decimal stop,
        decimal risk,
        bool allowDelayedExpansion = true,
        bool submitReplayExecution = true)
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

        CaptureFootprintCandidate(signal, entryCandle, researchPath, stop, risk);

        if (submitReplayExecution)
            TrySubmitReplayExecution(signal, entryCandle, researchPath, stop, risk, allowDelayedExpansion);
    }

    private void CaptureDeferredContinuationV209(
        CandidateSignal signal,
        OpfCandle entryCandle,
        string researchPath,
        decimal risk,
        ReplayExecutionState parent)
    {
        var shadowKey = ShadowTradeKey(signal.SignalId, researchPath, entryCandle.Bar);
        if (_deferredContinuations.Any(x => string.Equals(x.ShadowKey, shadowKey, StringComparison.Ordinal)))
            return;

        var candidate = new DeferredContinuationCandidate(
            signal,
            researchPath,
            risk,
            parent.TradeId,
            shadowKey,
            ++_deferredCaptureSequence);
        _deferredContinuations.Add(candidate);

        var parentCount = _deferredContinuations.Count(x => string.Equals(x.ParentTradeId, parent.TradeId, StringComparison.Ordinal));
        var message = $"parent={parent.TradeId}|risk={risk:0.##}|shadowKey={shadowKey}|parentCandidates={parentCount}|sequence={candidate.CaptureSequence}";
        AppendExecutionEvent(signal, parent.TradeId, entryCandle, "DEFERRED_CANDIDATE_QUEUED_V209", "-", researchPath, entryCandle.Close, 0m, message);
    }

    private void TryActivateDeferredContinuationV209(int bar)
    {
        if (_snapshot is null || _deferredContinuations.Count == 0 || bar < 0 || ActiveReplayExecutions().Count > 0)
            return;

        var entryCandle = CurrentBarOpenCandleV208(bar);
        if (entryCandle is null)
            return;

        var parentTradeId = _deferredContinuations
            .OrderByDescending(x => x.CaptureSequence)
            .Select(x => x.ParentTradeId)
            .First();
        var parentCandidates = _deferredContinuations
            .Where(x => string.Equals(x.ParentTradeId, parentTradeId, StringComparison.Ordinal))
            .ToArray();
        _deferredContinuations.RemoveAll(x => string.Equals(x.ParentTradeId, parentTradeId, StringComparison.Ordinal));

        var pending = parentCandidates
            .Where(x => _shadowTradeTrackersByKey.ContainsKey(x.ShadowKey))
            .OrderByDescending(x => x.Signal.Time)
            .ThenByDescending(x => x.CaptureSequence)
            .FirstOrDefault();
        foreach (var expired in parentCandidates.Where(x => pending is null || !string.Equals(x.ShadowKey, pending.ShadowKey, StringComparison.Ordinal)))
        {
            var state = _shadowTradeTrackersByKey.ContainsKey(expired.ShadowKey) ? "SupersededByLaterLiveCandidate" : "ShadowLifecycleCompleted";
            AppendExecutionEvent(expired.Signal, expired.ParentTradeId, entryCandle, "DEFERRED_CANDIDATE_REJECTED_V209", "-", expired.ResearchPath, entryCandle.Open, 0m, $"state={state}|shadowKey={expired.ShadowKey}|activationBar={bar}");
        }
        if (pending is null)
        {
            AppendExecutionEvent(parentCandidates[^1].Signal, parentTradeId, entryCandle, "DEFERRED_NO_LIVE_CANDIDATE_V209", "-", parentCandidates[^1].ResearchPath, entryCandle.Open, 0m, $"candidates={parentCandidates.Length}|activationBar={bar}");
            return;
        }

        var signal = pending.Signal with { Time = entryCandle.Time, Bar = entryCandle.Bar };
        var stop = signal.Side == TradeSide.Long
            ? entryCandle.Open - pending.Risk
            : entryCandle.Open + pending.Risk;
        AppendExecutionEvent(signal, pending.ParentTradeId, entryCandle, "DEFERRED_ACTIVATION_ATTEMPT_V209", "ENTRY", pending.ResearchPath, entryCandle.Open, ReplayOrderQuantity, $"parent={pending.ParentTradeId}|risk={pending.Risk:0.##}|shadowKey={pending.ShadowKey}|candidates={parentCandidates.Length}");

        TrySubmitReplayExecution(
            signal,
            entryCandle,
            pending.ResearchPath,
            stop,
            pending.Risk,
            allowDelayedExpansion: false,
            isDeferredContinuation: true,
            deferredParentTradeId: pending.ParentTradeId);

        if (_replayExecution is not null && _replayExecution.IsDeferredContinuation &&
            string.Equals(_replayExecution.DeferredParentTradeId, pending.ParentTradeId, StringComparison.Ordinal))
        {
            AppendExecutionEvent(signal, _replayExecution.TradeId, entryCandle, "DEFERRED_ACTIVATED_V209", "ENTRY", pending.ResearchPath, entryCandle.Open, _replayExecution.Quantity, $"parent={pending.ParentTradeId}|targetR={DeferredContinuationTargetRV208:0.##}|timeStopBars={DeferredContinuationMaxBarsV208}|shadowKey={pending.ShadowKey}");
            return;
        }

        AppendExecutionEvent(signal, pending.ParentTradeId, entryCandle, "DEFERRED_GATE_REJECTED_V209", "-", pending.ResearchPath, entryCandle.Open, 0m, "See execution_decisions.csv for the exact recheck reason");
    }

    private OpfCandle? CurrentBarOpenCandleV208(int bar)
    {
        var source = GetCandle(bar);
        if (source is null)
            return null;
        var candle = ToOpfCandle(bar, source);
        return candle with { High = candle.Open, Low = candle.Open, Close = candle.Open };
    }

    private void TryQueueSignificantZoneFirstTouch(SignificantZone zone, OpfCandle candle, RegimeResult regime)
    {
        if (_snapshot is null || _pendingSignificantZoneFirstTouches.Any(x => x.Entry.ZoneId == zone.ZoneId))
            return;

        var low = Math.Min(zone.InnerBoundary, zone.OuterBoundary);
        var high = Math.Max(zone.InnerBoundary, zone.OuterBoundary);
        if (candle.High < low || candle.Low > high)
            return;

        var participation = CaptureSignificantZoneParticipation(candle, low, high);
        var evaluation = _significantZoneFirstTouchEngine.Evaluate(zone, candle, participation.HasObservedTrades);
        if (evaluation.Outcome != SignificantZoneFirstTouchOutcome.Confirmed || evaluation.Entry is null)
            return;

        var score = zone.Side == TradeSide.Long ? regime.BullTrendScore : regime.BearTrendScore;
        var signal = new CandidateSignal(
            $"{candle.Time:yyyyMMdd-HHmm}-SZFT-{candle.Bar:000000}-{zone.ZoneId}",
            _snapshot.SnapshotId,
            candle.Time,
            candle.Bar,
            zone.Side,
            SetupType.TrendPullback,
            SignalStage.Confirmed,
            null,
            score,
            score,
            new[] { $"SignificantZoneFirstTouch:{zone.ZoneId}", evaluation.Reason });
        _pendingSignificantZoneFirstTouches.Add(new PendingSignificantZoneFirstTouch(signal, evaluation.Entry));
        _researchLogger?.AppendInfo(_snapshot.SnapshotId, candle.Bar, candle.Time, $"SIGNIFICANT_ZONE_FIRST_TOUCH_QUEUED zone={zone.ZoneId} side={zone.Side} nextBar={evaluation.Entry.NextEntryBar} stop={evaluation.Entry.InitialStop:0.########}");
    }

    private void TryActivateSignificantZoneFirstTouches(int bar)
    {
        if (_snapshot is null || _pendingSignificantZoneFirstTouches.Count == 0)
            return;

        var entryCandle = CurrentBarOpenCandleV208(bar);
        if (entryCandle is null)
            return;

        for (var i = _pendingSignificantZoneFirstTouches.Count - 1; i >= 0; i--)
        {
            var pending = _pendingSignificantZoneFirstTouches[i];
            if (bar < pending.Entry.NextEntryBar)
                continue;

            _pendingSignificantZoneFirstTouches.RemoveAt(i);
            if (bar != pending.Entry.NextEntryBar)
                continue;

            var signal = pending.Signal with { Time = entryCandle.Time, Bar = entryCandle.Bar };
            var path = signal.Side == TradeSide.Long ? SignificantZoneFirstTouchLongPath : SignificantZoneFirstTouchShortPath;
            var risk = Math.Abs(entryCandle.Open - pending.Entry.InitialStop);
            if (risk <= 0m)
            {
                AppendExecutionDecision(signal, entryCandle, "Skip", "FirstTouchInvalidRisk", path, pending.Entry.InitialStop, risk);
                continue;
            }

            TrySubmitReplayExecution(signal, entryCandle, path, pending.Entry.InitialStop, risk, allowDelayedExpansion: false);
        }
    }

    private void TrySubmitReplayExecution(
        CandidateSignal signal,
        OpfCandle entryCandle,
        string researchPath,
        decimal stop,
        decimal risk,
        bool allowDelayedExpansion = true,
        bool isDeferredContinuation = false,
        string deferredParentTradeId = "")
    {
        var marketExecutionScenarioId = CaptureMarketExecutionScenario(signal, entryCandle, researchPath, stop, risk);
        if (TryEmitManualAlert(signal, entryCandle, researchPath, stop, risk))
        {
            ResolveMarketExecutionScenario(marketExecutionScenarioId, "ManualAlert", "NoOrderSubmission");
            return;
        }
        if (!ActualOrdersEnabled || _snapshot is null)
        {
            ResolveMarketExecutionScenario(marketExecutionScenarioId, "DataOnlyObserved", "ActualOrdersDisabled");
            return;
        }
        if (_isStoppingActualExecution)
        {
            AppendExecutionDecision(signal, entryCandle, "Skip", "StrategyStopping", researchPath, stop, risk);
            AppendExecutionEvent(signal, string.Empty, entryCandle, "SKIP_STRATEGY_STOPPING", "-", researchPath, entryCandle.Close, 0m, "StrategyStopping");
            return;
        }
        if (IsGlobexCloseoutLockWindow(entryCandle.Time, out var globexCloseoutReason))
        {
            AppendExecutionDecision(signal, entryCandle, "Skip", globexCloseoutReason, researchPath, stop, risk);
            AppendExecutionEvent(signal, string.Empty, entryCandle, "SKIP_GLOBEX_CLOSEOUT_LOCK", "-", researchPath, entryCandle.Close, 0m, globexCloseoutReason);
            return;
        }
        if (IsUsCashOpenBlackout(entryCandle.Time, out var openBlockReason))
        {
            AppendExecutionDecision(signal, entryCandle, "Skip", openBlockReason, researchPath, stop, risk);
            AppendExecutionEvent(signal, string.Empty, entryCandle, "SKIP_US_OPEN_BLACKOUT", "-", researchPath, entryCandle.Close, 0m, openBlockReason);
            return;
        }
        if (IsLiveLatencyBlocked(entryCandle.Time, out var latencyReason))
        {
            AppendExecutionDecision(signal, entryCandle, "Skip", latencyReason, researchPath, stop, risk);
            AppendExecutionEvent(signal, string.Empty, entryCandle, "SKIP_LIVE_LATENCY", "-", researchPath, entryCandle.Close, 0m, latencyReason);
            return;
        }
        ResetReplayExecutionDailyCounter(GlobexTradingDayKey(entryCandle.Time));
        StartDecisionTapeCalibrationTracking(signal, entryCandle, researchPath, stop, risk);
        if (_liveReadinessBlocked)
        {
            var reason = $"LiveReadinessBlocked:{_liveReadinessBlockReason}";
            AppendExecutionDecision(signal, entryCandle, "Skip", reason, researchPath, stop, risk);
            AppendExecutionEvent(signal, string.Empty, entryCandle, "SKIP_LIVE_READINESS_BLOCKED", "-", researchPath, entryCandle.Close, 0m, reason);
            return;
        }
        if (!IsReplayExecutionPathEnabled(researchPath))
        {
            AppendExecutionDecision(signal, entryCandle, "Skip", "PathDisabled", researchPath, stop, risk);
            return;
        }
        if (!IsRestoredPriorityPathAllowed(signal.Side, researchPath))
        {
            AppendExecutionDecision(signal, entryCandle, "Skip", "PathDirectionDisabled", researchPath, stop, risk);
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
            if (allowDelayedExpansion && TryScheduleAggressiveExpansionWait1(signal, entryCandle, researchPath, stop, risk, sameBarSkipReasons))
                return;

            AppendExecutionDecision(signal, entryCandle, "Skip", string.Join("|", sameBarSkipReasons), researchPath, stop, risk);
            AppendExecutionEvent(signal, string.Empty, entryCandle, "SKIP_SAME_BAR_AMBIGUOUS", "-", researchPath, entryCandle.Close, 0m, string.Join("|", sameBarSkipReasons));
            return;
        }
        if (!isDeferredContinuation)
            StartShadowTradeTracking(signal, entryCandle, researchPath, stop, risk);
        if (_actualDailyLossLimitDollars > 0m && _liveAccountDailyNetPnlDollars <= -_actualDailyLossLimitDollars)
        {
            var reason = $"LiveDailyLoss:net={_liveAccountDailyNetPnlDollars:0.##},limit={_actualDailyLossLimitDollars:0.##}";
            AppendExecutionDecision(signal, entryCandle, "Skip", reason, researchPath, stop, risk);
            AppendExecutionEvent(signal, string.Empty, entryCandle, "SKIP_LIVE_DAILY_LOSS", "-", researchPath, entryCandle.Close, 0m, reason);
            return;
        }
        if (signal.Side == TradeSide.Long && IsWeeklyLongLossGateActive(out var weeklyLongLossReason))
        {
            AppendExecutionDecision(signal, entryCandle, "Skip", weeklyLongLossReason, researchPath, stop, risk);
            AppendExecutionEvent(signal, string.Empty, entryCandle, "SKIP_WEEKLY_LONG_LOSS_V219", "-", researchPath, entryCandle.Close, 0m, weeklyLongLossReason);
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
        var activeExecutions = ActiveReplayExecutions();
        var isSecondaryExecution = activeExecutions.Count == 1;
        if (activeExecutions.Count >= 2)
        {
            var activeIds = string.Join(",", activeExecutions.Select(x => x.TradeId));
            AppendExecutionDecision(signal, entryCandle, "Skip", $"ActiveTradeLimit:{activeIds}", researchPath, stop, risk);
            AppendExecutionEvent(signal, activeIds, entryCandle, "SKIP_ACTIVE_LIMIT_V215", "-", researchPath, entryCandle.Close, 0m, activeIds);
            return;
        }
        if (!isSecondaryExecution &&
            string.Equals(researchPath, "ZoneBirthResearch", StringComparison.OrdinalIgnoreCase) &&
            signal.SetupQualityScore.TotalScore < PrimaryZoneBirthMinSetupQualityV221)
        {
            var reason = $"PrimaryZoneBirthQualityV221:score={signal.SetupQualityScore.TotalScore:0.##}|min={PrimaryZoneBirthMinSetupQualityV221:0.##}";
            AppendExecutionDecision(signal, entryCandle, "Skip", reason, researchPath, stop, risk);
            AppendExecutionEvent(signal, string.Empty, entryCandle, "SKIP_PRIMARY_ZONE_BIRTH_QUALITY_V221", "-", researchPath, entryCandle.Close, 0m, reason);
            return;
        }
        if (!isSecondaryExecution &&
            signal.Side == TradeSide.Long &&
            string.Equals(researchPath, "ObservationConfirm", StringComparison.OrdinalIgnoreCase) &&
            RegimeBars(entryCandle.Bar) < PrimaryObservationConfirmMinRegimeBarsV223)
        {
            var regimeBars = RegimeBars(entryCandle.Bar);
            var reason = $"PrimaryObservationConfirmRegimeBarsV223:bars={regimeBars}|min={PrimaryObservationConfirmMinRegimeBarsV223}";
            AppendExecutionDecision(signal, entryCandle, "Skip", reason, researchPath, stop, risk);
            AppendExecutionEvent(signal, string.Empty, entryCandle, "SKIP_PRIMARY_OC_REGIME_BARS_V223", "-", researchPath, entryCandle.Close, 0m, reason);
            return;
        }
        if (!isSecondaryExecution &&
            signal.Side == TradeSide.Short &&
            string.Equals(researchPath, "BreakawayFvg", StringComparison.OrdinalIgnoreCase) &&
            (signal.SetupQualityScore.TotalScore < PrimaryBreakawayMinSetupQualityV223 ||
             risk < PrimaryBreakawayMinRiskPointsV223 ||
             !HasPassedRegimeComponent(signal.RegimeScore, "VWAPSide")))
        {
            var vwapSidePassed = HasPassedRegimeComponent(signal.RegimeScore, "VWAPSide");
            var reason = $"PrimaryBreakawayRichV223:score={signal.SetupQualityScore.TotalScore:0.##}|minScore={PrimaryBreakawayMinSetupQualityV223:0.##}|risk={risk:0.##}|minRisk={PrimaryBreakawayMinRiskPointsV223:0.##}|vwapSidePassed={vwapSidePassed}";
            AppendExecutionDecision(signal, entryCandle, "Skip", reason, researchPath, stop, risk);
            AppendExecutionEvent(signal, string.Empty, entryCandle, "SKIP_PRIMARY_BREAKAWAY_RICH_V223", "-", researchPath, entryCandle.Close, 0m, reason);
            return;
        }
        if (isSecondaryExecution)
        {
            var primary = activeExecutions[0];
            var secondaryQty = Math.Max(0m, ReplayOrderQuantity);
            var pointValue = _snapshot.InstrumentProfile.PointValue;
            var combinedRisk = primary.InitialRiskPoints * pointValue * RemainingExecutionQuantity(primary) +
                risk * pointValue * secondaryQty;
            var secondaryReason = string.Empty;
            if (isDeferredContinuation)
                secondaryReason = "DeferredSecondaryDisabled";
            else if (primary.Side != signal.Side)
                secondaryReason = $"SecondaryDirectionMismatch:primary={primary.Side}|candidate={signal.Side}";
            else if (!IsStaticSecondaryEligibleV215(signal.Side, researchPath, risk))
                secondaryReason = $"SecondaryStaticG2Blocked:path={researchPath}|side={signal.Side}|band={StaticRiskBandV215(risk)}";
            else if (combinedRisk > 300m)
                secondaryReason = $"SecondaryRiskCap:combined={combinedRisk:0.##}|max=300";
            else if (!primary.BracketSubmitted || primary.EntryFilledQty <= primary.ExitFilledQty)
            {
                if (TryQueuePendingProtectedSecondaryV216(primary, signal, entryCandle, researchPath, stop, risk, out var pendingReason))
                {
                    AppendExecutionEvent(signal, primary.TradeId, entryCandle, "SECONDARY_WAITING_FOR_PROTECTION_V216", "-", researchPath, entryCandle.Close, 0m, pendingReason);
                    return;
                }

                secondaryReason = pendingReason;
            }
            if (!string.IsNullOrEmpty(secondaryReason))
            {
                AppendExecutionDecision(signal, entryCandle, "Skip", secondaryReason, researchPath, stop, risk);
                AppendExecutionEvent(signal, primary.TradeId, entryCandle, "SKIP_SECONDARY_GATE_V215", "-", researchPath, entryCandle.Close, 0m, secondaryReason);
                return;
            }
        }
        var currentPosition = GetCurrentAccountPosition();
        if (activeExecutions.Count == 0 && currentPosition != 0m)
        {
            var reason = $"OrphanPosition:pos={currentPosition:0.########}";
            _researchLogger?.AppendInfo(_snapshot.SnapshotId, entryCandle.Bar, entryCandle.Time, $"EXEC_SKIP_ORPHAN_POSITION signal={signal.SignalId} pos={currentPosition:0.########}");
            AppendExecutionDecision(signal, entryCandle, "Skip", reason, researchPath, stop, risk);
            AppendExecutionEvent(signal, string.Empty, entryCandle, "SKIP_ORPHAN_POSITION", "-", researchPath, entryCandle.Close, Math.Abs(currentPosition), reason);
            return;
        }
        if (activeExecutions.Count == 1)
        {
            var primary = activeExecutions[0];
            var expectedPosition = (primary.Side == TradeSide.Long ? 1m : -1m) * RemainingExecutionQuantity(primary);
            if (Math.Sign(currentPosition) != Math.Sign(expectedPosition) ||
                Math.Abs(Math.Abs(currentPosition) - Math.Abs(expectedPosition)) > 0.0000001m)
            {
                var reason = $"ManagedPositionMismatch:actual={currentPosition:0.########}|expected={expectedPosition:0.########}|primary={primary.TradeId}";
                AppendExecutionDecision(signal, entryCandle, "Skip", reason, researchPath, stop, risk);
                AppendExecutionEvent(signal, primary.TradeId, entryCandle, "SKIP_MANAGED_POSITION_MISMATCH_V215", "-", researchPath, entryCandle.Close, Math.Abs(currentPosition), reason);
                return;
            }
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
        var targetR = isDeferredContinuation ? DeferredContinuationTargetRV208 : ActualTargetRFor(signal, researchPath, risk);
        var targetDistance = risk * targetR;
        var target = signal.Side == TradeSide.Long ? entry + targetDistance : entry - targetDistance;
        var lane = isSecondaryExecution ? "Secondary" : "Primary";
        var tradeId = $"{entryCandle.Time:yyyyMMdd-HHmm}-{signal.Side}-{entryCandle.Bar}{(isSecondaryExecution ? "-S2" : string.Empty)}{(isDeferredContinuation ? "-DEF" : string.Empty)}";
        var maxAllowedRiskPoints = MaxAllowedActualRiskPoints(signal, researchPath, risk);
        var entryOrder = new Order
        {
            Portfolio = Portfolio,
            Security = Security,
            Type = OrderTypes.Market,
            Direction = signal.Side == TradeSide.Long ? OrderDirections.Buy : OrderDirections.Sell,
            Price = 0m,
            QuantityToFill = qty,
            TimeInForce = ReplayTimeInForce,
            Comment = $"OPF|{tradeId}|ENTRY|{researchPath}|{signal.SignalId}",
            AutoCancel = false
        };

        var execution = new ReplayExecutionState(
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
        execution.Lane = lane;
        execution.IsDeferredContinuation = isDeferredContinuation;
        execution.DeferredParentTradeId = deferredParentTradeId;
        _replayExecution = execution;
        TrackRecentReplayExecution(execution);

        _researchLogger?.AppendInfo(_snapshot.SnapshotId, entryCandle.Bar, entryCandle.Time, $"EXEC_ENTRY_SEND trade={tradeId} signal={signal.SignalId} path={researchPath} side={signal.Side} qty={qty} stop={stop:0.########} target={target:0.########}");
        _researchLogger?.AppendSignal(signal with { Stage = SignalStage.Executed, SkipReasons = signal.SkipReasons.Concat(new[] { "ReplayExecuted", researchPath, tradeId }).ToArray() });
        var executeReasons = new List<string> { $"TradeID:{tradeId}" };
        if (isDeferredContinuation)
            executeReasons.Add($"DeferredContinuationV208:parent={deferredParentTradeId}|targetR={DeferredContinuationTargetRV208:0.##}|timeStopBars={DeferredContinuationMaxBarsV208}");
        if (UseLegacyV174StrategyPolicyV204)
            executeReasons.Add($"LegacyV174PolicyV204:targetR={targetR:0.##}|dailyCap={ReplayMaxTradesPerDay}");
        else if (UsesProtectBreakEvenV186(signal.Side, researchPath))
            executeReasons.Add($"ProtectBE1RThen3RV186:triggerR={ProtectBreakEvenTriggerRV186:0.##},targetR={ProtectBreakEvenTargetRV186:0.##}");
        if (!isDeferredContinuation && TryGetActualExitPolicyV209(signal.Side, researchPath, out var policyTargetR, out var policyBreakEvenTriggerR, out var policyTimeStopBars, risk))
            executeReasons.Add($"ActualExitPolicyV209:targetR={policyTargetR:0.##}|beTriggerR={policyBreakEvenTriggerR:0.##}|timeStopBars={policyTimeStopBars}");
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
        {
            executeReasons.Add($"OCWideStopLongExpansionV157:risk={risk:0.##},score={signal.SetupQualityScore.TotalScore:0.##},count={_observationConfirmWideStopLongExpansionTradesToday + 1}/{ObservationConfirmWideStopLongExpansionV163MaxTradesPerDay}");
        }
        if (IsObservationConfirmWideStopLowRiskV132(signal, researchPath, risk))
            executeReasons.Add($"OCWideStopLowRiskV132:side={signal.Side},risk={risk:0.##},score={signal.SetupQualityScore.TotalScore:0.##},rr={EstimatedActualRr(signal, researchPath, entry, risk):0.####},dailyTrades={_replayTradesToday}");
        if (IsFailureRetestWideStopVolumeFiller(researchPath))
            executeReasons.Add($"FailureRetestWideFiller:dailyTrades={_replayTradesToday}/{_actualObservationConfirmFillerUntilDailyTrades}");
        if (IsPositiveExpansionV146(signal, researchPath))
            executeReasons.Add($"PositiveExpansionV146:path={researchPath},side={signal.Side},risk={risk:0.##},score={signal.SetupQualityScore.TotalScore:0.##}");
        if (TryGetAggressiveExpansionV164Rules(signal.Side, researchPath, out _, out _, out var aggressiveExpansionTag))
        {
            executeReasons.Add($"AggressiveExpansionV164:{aggressiveExpansionTag},risk={risk:0.##},score={signal.SetupQualityScore.TotalScore:0.##},eligibilityRr={EstimatedActualRr(signal, researchPath, entry, risk):0.####},targetR={targetR:0.##}");
            if (aggressiveExpansionTag.EndsWith("V167", StringComparison.Ordinal))
                executeReasons.Add($"BoldExpansionV167:{aggressiveExpansionTag}");
            if (aggressiveExpansionTag.EndsWith("V168", StringComparison.Ordinal) || targetR == DynamicExpansionV168TargetR)
                executeReasons.Add($"DynamicExpansionV168:{aggressiveExpansionTag},targetR={targetR:0.##}");
            if (IsZoneBirthShortExpansionV168(signal, researchPath))
            {
                executeReasons.Add($"ZoneBirthShortDualRiskBandV170:{ZoneBirthShortV170RiskBandTag(risk)},risk={risk:0.##}");
                executeReasons.Add($"ZoneBirthSplitRunnerV208:baseQty={ZoneBirthSplitRunnerV172BaseQuantity:0},runnerQty={ZoneBirthSplitRunnerV172RunnerQuantity:0},baseTargetR={targetR:0.##},runnerTargetR={ZoneBirthSplitRunnerV173TargetR:0.##},runnerBeAfter=BaseTP");
            }
        }
        var wait1Tag = signal.SkipReasons.FirstOrDefault(x => x.StartsWith("AggressiveExpansionWait1V165:", StringComparison.Ordinal));
        if (!string.IsNullOrEmpty(wait1Tag))
            executeReasons.Add(wait1Tag);
        if (signal.SkipReasons.Contains("AggressiveExpansionWait1ConfirmBarStopV166", StringComparer.Ordinal))
            executeReasons.Add("AggressiveExpansionWait1ConfirmBarStopV166");
        var profitTargetTag = SelectiveProfitTargetV145Tag(signal.Side, researchPath);
        if (!string.IsNullOrEmpty(profitTargetTag))
            executeReasons.Add($"{profitTargetTag}:targetR={targetR:0.##},risk={risk:0.##}");
        var observationLongTargetTag = ObservationConfirmLongTarget2RV161Tag(signal.Side, researchPath, risk);
        if (!string.IsNullOrEmpty(observationLongTargetTag))
            executeReasons.Add($"{observationLongTargetTag}:targetR={targetR:0.##},plannedRisk={risk:0.##}");
        if (targetR != ReplayTargetR)
            executeReasons.Add($"ActualTargetOverride:targetR={targetR:0.##},risk={risk:0.##},path={researchPath}");
        var executeReason = string.Join("|", executeReasons);
        execution.CountedAsObservationFiller = isObservationFiller;
        execution.CountedAsQualityRescue = IsDailyVolumeQualityRescue(signal, researchPath, risk);
        execution.CountedAsWideStopLongExpansion = isWideStopLongExpansionV157;
        AppendExecutionDecision(signal, entryCandle, "Execute", executeReason, researchPath, stop, risk, tradeId);
        AppendExecutionEvent(signal, tradeId, entryCandle, "ENTRY_SEND", "ENTRY", researchPath, entry, qty, $"stop={stop:0.########}|target={target:0.########}");
        _replayTradesToday++;
        if (isObservationFiller)
            _observationConfirmFillerTradesToday++;
        if (IsDailyVolumeQualityRescue(signal, researchPath, risk))
            _observationConfirmQualityRescueTradesToday++;
        if (isWideStopLongExpansionV157)
            _observationConfirmWideStopLongExpansionTradesToday++;
        EnqueueExecutionAction("OpenReplayEntry", async () => await OpenReplayEntryAfterQuotePreflightAsync(execution));
    }

    private bool TryEmitManualAlert(
        CandidateSignal signal,
        OpfCandle entryCandle,
        string researchPath,
        decimal stop,
        decimal risk)
    {
        if (!_manualAlertEnabled || _snapshot is null ||
            !IsRestoredPriorityPathAllowed(signal.Side, researchPath) ||
            !IsReplayExecutionPathEnabled(researchPath))
            return false;
        var entry = entryCandle.Close;
        var invalidStopSide = signal.Side == TradeSide.Long ? stop >= entry : stop <= entry;
        if (risk <= 0m || invalidStopSide ||
            (_snapshot.InstrumentProfile.MaxRiskPointsHard > 0m && risk > _snapshot.InstrumentProfile.MaxRiskPointsHard) ||
            IsGlobexCloseoutLockWindow(entryCandle.Time, out _) ||
            IsUsCashOpenBlackout(entryCandle.Time, out _) ||
            IsLiveLatencyBlocked(entryCandle.Time, out _) || _liveReadinessBlocked ||
            ActualExecutionStrategySkipReasons(signal, researchPath).Length > 0 ||
            ActualExecutionRiskSkipReasons(signal, researchPath, entryCandle, risk).Length > 0 ||
            ActualExecutionSameBarSkipReasons(signal, researchPath, entryCandle, stop, risk).Length > 0 ||
            GetCurrentAccountPosition() != 0m || ActiveReplayExecutions().Count != 0)
            return false;

        var key = $"{entryCandle.Time:O}|{signal.Side}|{entry:0.########}";
        if (!_manualAlertKeys.Add(key))
            return true;

        var targetR = ReplayTargetR > 0m ? ReplayTargetR : 1.5m;
        var target = TargetFromRisk(signal.Side, entry, risk, targetR);
        var quantity = Math.Max(0m, ReplayOrderQuantity);
        var dollars = risk * _snapshot.InstrumentProfile.PointValue * quantity;
        var zoneType = signal.Zone?.ZoneType ?? "None";
        var message = $"MANUAL_ALERT path={researchPath}|side={signal.Side}|market={entry:0.##}|sl={stop:0.##}|tp={target:0.##}|qty={quantity:0.##}|risk={risk:0.##}pt/${dollars:0.##}|score={signal.SetupQualityScore.TotalScore:0.##}|regime={signal.RegimeScore.TotalScore:0.##}|zone={zoneType}";
        _lastExecutionHudText = message;
        _recentExecutionHudItems.Enqueue(message);
        while (_recentExecutionHudItems.Count > 3)
            _recentExecutionHudItems.Dequeue();
        AppendExecutionDecision(signal, entryCandle, "ManualAlert", "ManualAlertOnly", researchPath, stop, risk);
        AppendExecutionEvent(signal, string.Empty, entryCandle, "MANUAL_ALERT", "ENTRY", researchPath, entry, quantity, message);
        RaiseShowNotification(message, "OPFStrategyV1");
        return true;
    }

    private bool TryQueuePendingProtectedSecondaryV216(
        ReplayExecutionState primary,
        CandidateSignal signal,
        OpfCandle entryCandle,
        string researchPath,
        decimal stop,
        decimal risk,
        out string reason)
    {
        lock (_pendingProtectedSecondarySyncV216)
        {
            if (_pendingProtectedSecondaryV216 is not null)
            {
                reason = $"PendingProtectedSecondaryExistsV216:primary={_pendingProtectedSecondaryV216.PrimaryTradeId}|signal={_pendingProtectedSecondaryV216.Signal.SignalId}";
                return false;
            }

            _pendingProtectedSecondaryV216 = new PendingProtectedSecondaryV216(
                primary.TradeId,
                signal,
                entryCandle,
                researchPath,
                stop,
                risk);
        }

        reason = $"PrimaryNotProtectedDeferredV216:primary={primary.TradeId}|signal={signal.SignalId}|bar={entryCandle.Bar}";
        return true;
    }

    private void TryActivatePendingProtectedSecondaryV216(ReplayExecutionState primary)
    {
        PendingProtectedSecondaryV216? pending;
        lock (_pendingProtectedSecondarySyncV216)
        {
            if (_pendingProtectedSecondaryV216 is null ||
                !string.Equals(_pendingProtectedSecondaryV216.PrimaryTradeId, primary.TradeId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            pending = _pendingProtectedSecondaryV216;
            _pendingProtectedSecondaryV216 = null;
        }

        if (primary.ExitCompleted || !primary.BracketSubmitted || !HasRequiredWorkingStops(primary) ||
            primary.EntryFilledQty <= primary.ExitFilledQty)
        {
            RejectPendingProtectedSecondaryV216(pending, "PrimaryProtectionUnavailableAfterCallbackV216");
            return;
        }
        if (_lastSeenBar > pending.EntryCandle.Bar + 1)
        {
            RejectPendingProtectedSecondaryV216(pending, $"PendingSecondaryExpiredV216:currentBar={_lastSeenBar}|entryBar={pending.EntryCandle.Bar}");
            return;
        }

        var reference = CurrentExecutionMarketReference(pending.EntryCandle.Close);
        if (!TryValidateExecutionQuotePair(reference, out var quoteReason))
        {
            RejectPendingProtectedSecondaryV216(pending, $"PendingSecondaryQuoteInvalidV216:{quoteReason}");
            return;
        }

        var quote = ExecutionQuoteSnapshot();
        var entry = pending.Signal.Side == TradeSide.Long ? quote.Ask : quote.Bid;
        if (entry <= 0m ||
            (pending.Signal.Side == TradeSide.Long && pending.Stop >= entry) ||
            (pending.Signal.Side == TradeSide.Short && pending.Stop <= entry))
        {
            RejectPendingProtectedSecondaryV216(pending, $"PendingSecondaryInvalidFreshRiskV216:entry={entry:0.########}|stop={pending.Stop:0.########}");
            return;
        }

        var risk = Math.Abs(entry - pending.Stop);
        var freshCandle = pending.EntryCandle with
        {
            Open = entry,
            High = Math.Max(pending.EntryCandle.High, entry),
            Low = Math.Min(pending.EntryCandle.Low, entry),
            Close = entry
        };
        AppendExecutionEvent(
            pending.Signal,
            primary.TradeId,
            freshCandle,
            "SECONDARY_PROTECTION_READY_RETRY_V216",
            "ENTRY",
            pending.ResearchPath,
            entry,
            ReplayOrderQuantity,
            $"primary={primary.TradeId}|plannedRisk={pending.Risk:0.##}|freshRisk={risk:0.##}|bid={quote.Bid:0.########}|ask={quote.Ask:0.########}");
        TrySubmitReplayExecution(
            pending.Signal,
            freshCandle,
            pending.ResearchPath,
            pending.Stop,
            risk,
            allowDelayedExpansion: false);
    }

    private void ExpirePendingProtectedSecondaryV216(OpfCandle candle)
    {
        PendingProtectedSecondaryV216? pending = null;
        lock (_pendingProtectedSecondarySyncV216)
        {
            if (_pendingProtectedSecondaryV216 is not null && candle.Bar > _pendingProtectedSecondaryV216.EntryCandle.Bar)
            {
                pending = _pendingProtectedSecondaryV216;
                _pendingProtectedSecondaryV216 = null;
            }
        }

        if (pending is not null)
            RejectPendingProtectedSecondaryV216(pending, $"PendingSecondaryExpiredV216:closedBar={candle.Bar}|entryBar={pending.EntryCandle.Bar}");
    }

    private void RejectPendingProtectedSecondaryV216(PendingProtectedSecondaryV216 pending, string reason)
    {
        AppendExecutionDecision(pending.Signal, pending.EntryCandle, "Skip", reason, pending.ResearchPath, pending.Stop, pending.Risk);
        AppendExecutionEvent(pending.Signal, pending.PrimaryTradeId, pending.EntryCandle, "SECONDARY_PROTECTION_RETRY_REJECTED_V216", "-", pending.ResearchPath, pending.EntryCandle.Close, 0m, reason);
    }

    private void ClearPendingProtectedSecondaryV216(string reason)
    {
        PendingProtectedSecondaryV216? pending;
        lock (_pendingProtectedSecondarySyncV216)
        {
            pending = _pendingProtectedSecondaryV216;
            _pendingProtectedSecondaryV216 = null;
        }

        if (pending is not null)
            RejectPendingProtectedSecondaryV216(pending, reason);
    }

    private async Task OpenReplayEntryAfterQuotePreflightAsync(ReplayExecutionState execution)
    {
        if (!IsRegisteredReplayExecution(execution) || execution.ExitCompleted || execution.EntryOrder is null)
            return;

        var reference = CurrentExecutionMarketReference(execution.CreatedPrice);
        var tolerance = ExecutionFillTolerance();
        var barLag = Math.Max(0, _lastSeenBar - (execution.CreatedBar + 1));
        var plannedDrift = Math.Abs(reference - execution.CreatedPrice);
        var quoteValid = TryValidateExecutionQuotePair(reference, out var quoteReason);
        if (barLag > 0 || plannedDrift > tolerance || !quoteValid)
        {
            var reason = $"EntryQuotePreflightFailed:barLag={barLag}|reference={reference:0.########}|planned={execution.CreatedPrice:0.########}|drift={plannedDrift:0.########}|max={tolerance:0.########}|{quoteReason}";
            AbortEntrySubmissionV178(execution, reference, reason);
            return;
        }
        var quote = ExecutionQuoteSnapshot();
        var estimatedFill = execution.Side == TradeSide.Long ? quote.Ask : quote.Bid;
        var estimatedRisk = EstimatedRepricedRiskPointsV216(execution, estimatedFill);
        if (IsStrictEntryExcludedRiskV208(estimatedRisk))
        {
            var reason = $"EntryQuoteStrictRiskBandExcludedV216:risk={estimatedRisk:0.##}|excluded=({StrictEntryExcludedRiskMinExclusiveV208:0.##},{StrictEntryExcludedRiskMaxInclusiveV208:0.##}]|planned={execution.PlannedRiskPoints:0.##}|bid={quote.Bid:0.########}|ask={quote.Ask:0.########}";
            AppendExecutionEvent(execution, "ENTRY_QUOTE_STRICT_RISK_BAND_EXCLUDED_V216", "ENTRY", estimatedFill, execution.Quantity, reason);
            AbortEntrySubmissionV178(execution, estimatedFill, reason);
            return;
        }
        if (execution.MaxAllowedRiskPoints > 0m && estimatedRisk > execution.MaxAllowedRiskPoints &&
            !IsEntryRiskDriftAccepted(execution, estimatedRisk))
        {
            var reason = $"EntryQuoteRiskExceededV216:risk={estimatedRisk:0.##}|max={execution.MaxAllowedRiskPoints:0.##}|planned={execution.PlannedRiskPoints:0.##}|bid={quote.Bid:0.########}|ask={quote.Ask:0.########}";
            AppendExecutionEvent(execution, "ENTRY_QUOTE_RISK_EXCEEDED_V216", "ENTRY", estimatedFill, execution.Quantity, reason);
            AbortEntrySubmissionV178(execution, estimatedFill, reason);
            return;
        }
        if (string.Equals(execution.Lane, "Secondary", StringComparison.Ordinal))
        {
            var pointValue = _snapshot!.InstrumentProfile.PointValue;
            var existingRisk = ActiveReplayExecutions()
                .Where(other => !ReferenceEquals(other, execution))
                .Sum(other => other.InitialRiskPoints * pointValue * RemainingExecutionQuantity(other));
            var combinedRisk = existingRisk + estimatedRisk * pointValue * execution.Quantity;
            if (combinedRisk > 300m)
            {
                var reason = $"SecondaryEntryQuoteRiskCapExceededV216:combined={combinedRisk:0.##}|max=300|existing={existingRisk:0.##}|secondary={estimatedRisk * pointValue * execution.Quantity:0.##}";
                AppendExecutionEvent(execution, "SECONDARY_ENTRY_QUOTE_RISK_CAP_EXCEEDED_V216", "ENTRY", estimatedFill, execution.Quantity, reason);
                AbortEntrySubmissionV178(execution, estimatedFill, reason);
                return;
            }
        }
        if (_decisionTapeCalibrationTrackersByKey.TryGetValue(
                ShadowTradeKey(execution.SignalId, execution.ResearchPath, execution.CreatedBar),
                out var calibration))
        {
            calibration.SetEntryQuote(quote.Bid, quote.Ask);
            calibration.SetEntryMarketSequence(_decisionTapeMarketSequence);
        }
        LogExecutionInfo($"EXEC_ENTRY_QUOTE_PREFLIGHT_OK trade={execution.TradeId} reference={reference:0.########} bid={quote.Bid:0.########} ask={quote.Ask:0.########} barLag={barLag}");
        AppendExecutionEvent(execution, "ENTRY_QUOTE_PREFLIGHT_OK_V178", "ENTRY", reference, execution.Quantity, $"bid={quote.Bid:0.########}|ask={quote.Ask:0.########}|barLag={barLag}|marketSequence={_decisionTapeMarketSequence}");
        await OpenOrderAsync(execution.EntryOrder);
    }

    private void AbortEntrySubmissionV178(ReplayExecutionState execution, decimal reference, string reason)
    {
        if (execution.EntrySubmissionAborted || execution.EntryFilledQty > 0m)
            return;

        execution.EntrySubmissionAborted = true;
        execution.ExitCompleted = true;
        execution.ExitBar = _lastResearchCandle?.Bar;
        execution.ExitPrice = reference;
        execution.ExitRole = "ENTRY_SUBMISSION_ABORTED";
        RollBackNormalTradeCounters(execution);
        LogExecutionInfo($"EXEC_ENTRY_SUBMISSION_ABORTED_V178 trade={execution.TradeId} reason={reason}");
        AppendExecutionEvent(execution, "ENTRY_SUBMISSION_ABORTED_V178", "ENTRY", reference, execution.Quantity, reason);
        RaiseShowNotification($"Entry preflight rejected before order submission: {execution.TradeId}. {reason}. Strategy remains eligible for later signals.", "OPFStrategyV1");
    }

    private void TrackRecentReplayExecution(ReplayExecutionState execution)
    {
        _replayExecutionsByTradeId[execution.TradeId] = execution;
        _recentReplayExecutions.Add(execution);
        var maxStored = Math.Max(ActualMaxVisibleOrders * 3, 12);
        if (_recentReplayExecutions.Count > maxStored)
            _recentReplayExecutions.RemoveRange(0, _recentReplayExecutions.Count - maxStored);
    }

    private void AppendExecutionDecision(CandidateSignal signal, OpfCandle entryCandle, string decision, string reason, string researchPath, decimal stop, decimal risk, string tradeId = "")
    {
        if (_snapshot is null)
            return;

        if (_shadowTradeTrackersByKey.TryGetValue(ShadowTradeKey(signal.SignalId, researchPath, entryCandle.Bar), out var shadow))
            shadow.SetOriginalDecision(decision, reason, tradeId);
        if (_decisionTapeCalibrationTrackersByKey.TryGetValue(ShadowTradeKey(signal.SignalId, researchPath, entryCandle.Bar), out var calibration))
        {
            var quote = ExecutionQuoteSnapshot();
            calibration.SetEntryQuote(quote.Bid, quote.Ask);
            calibration.SetEntryMarketSequence(_decisionTapeMarketSequence);
            calibration.SetOriginalDecision(decision, reason, tradeId);
        }

        var entry = entryCandle.Close;
        var targetR = ActualTargetRFor(signal, researchPath, risk);
        var target = TargetFromRisk(signal.Side, entry, risk, targetR);
        var reward = EstimateActualExecutionReward(signal, researchPath, entry, risk);
        var estimatedRr = reward.Points <= 0m || risk <= 0m ? 0m : Math.Round(reward.Points / risk, 4);

        BufferKnnShadowInput(signal, entryCandle, decision, researchPath, risk, estimatedRr, tradeId);

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

    private void BufferKnnShadowInput(
        CandidateSignal signal,
        OpfCandle entryCandle,
        string executionDecision,
        string researchPath,
        decimal risk,
        decimal estimatedRr,
        string tradeId)
    {
        if (_knnShadowModel is null || _researchLogger is null || _snapshot is null)
            return;

        try
        {
            var features = BuildKnnShadowFeatures(signal, entryCandle, executionDecision, researchPath, risk, estimatedRr, tradeId);
            var input = new KnnShadowInput(
                _snapshot.SnapshotId,
                signal.SignalId,
                entryCandle.Time,
                entryCandle.Bar,
                signal.Side.ToString(),
                researchPath,
                features.Numeric,
                features.Categorical);
            lock (_knnShadowInputSync)
                _knnShadowInputs.Add(input);
        }
        catch (Exception ex)
        {
            _researchLogger.AppendInfo(
                _snapshot.SnapshotId,
                entryCandle.Bar,
                entryCandle.Time,
                $"KNN_SHADOW_SCORE_FAILED signal={signal.SignalId} path={researchPath} type={ex.GetType().Name} message={ex.Message}");
        }
    }

    private void FlushKnnShadowInputs()
    {
        if (_knnShadowModel is null || _researchLogger is null || _snapshot is null)
            return;

        KnnShadowInput[] inputs;
        lock (_knnShadowInputSync)
        {
            inputs = _knnShadowInputs.ToArray();
            _knnShadowInputs.Clear();
        }

        foreach (var input in inputs)
        {
            try
            {
                var decision = _knnShadowModel.Score(input.Side, input.ResearchPath, input.Numeric, input.Categorical);
                _researchLogger.AppendKnnShadowDecision(
                    input.SnapshotId,
                    input.SignalId,
                    input.Time,
                    input.Bar,
                    input.Side,
                    input.ResearchPath,
                    decision,
                    _knnShadowModel.NumericFeatures,
                    input.Numeric,
                    _knnShadowModel.CategoricalFeatures,
                    input.Categorical);
            }
            catch (Exception ex)
            {
                _researchLogger.AppendInfo(
                    input.SnapshotId,
                    input.Bar,
                    input.Time,
                    $"KNN_SHADOW_SCORE_FAILED signal={input.SignalId} path={input.ResearchPath} type={ex.GetType().Name} message={ex.Message}");
            }
        }
    }

    private void BufferRichBarFeature(OpfCandle candle, RegimeResult regime)
    {
        if (ActualOrdersEnabled)
            return;

        if (RichBarDataCollectionOnly && _richBarFeatures.Count == 0 && candle.Bar > 0)
        {
            BufferRichBarBoundaryFeatures(candle.Bar);
            return;
        }

        BufferRichBarFeature(candle, regime, _recentCandles, RegimeBars(candle.Bar));
    }

    private void BufferRichBarBoundaryFeatures(int currentBar)
    {
        var engine = new TrendScoreEngine();
        var recentCandles = new List<OpfCandle>();
        MarketRegime? lastRegime = null;
        var lastRegimeChangeBar = -1;

        for (var bar = 0; bar <= currentBar; bar++)
        {
            var source = GetCandle(bar);
            if (source is null)
                continue;

            var candle = ToOpfCandle(bar, source);
            var regime = engine.Update(candle);
            if (lastRegime != regime.Regime)
            {
                lastRegime = regime.Regime;
                lastRegimeChangeBar = bar;
            }

            if (bar >= Math.Max(0, currentBar - RichBoundaryBackfillBars))
            {
                var regimeBars = lastRegimeChangeBar < 0 ? 0 : Math.Max(1, bar - lastRegimeChangeBar + 1);
                BufferRichBarFeature(candle, regime, recentCandles, regimeBars);
            }

            recentCandles.Add(candle);
            if (recentCandles.Count > 20)
                recentCandles.RemoveAt(0);
        }
    }

    private int RegimeBars(int bar) => _lastRegimeChangeBar < 0 ? 0 : Math.Max(1, bar - _lastRegimeChangeBar + 1);

    private static bool HasPassedRegimeComponent(ScoreBreakdown score, string componentName)
    {
        return score.Components.Any(component =>
            string.Equals(component.Name, componentName, StringComparison.OrdinalIgnoreCase) && component.Passed);
    }

    private void BufferRichBarFeature(
        OpfCandle candle,
        RegimeResult regime,
        IReadOnlyCollection<OpfCandle> recentCandles,
        int regimeBars)
    {
        var averageVolume20 = recentCandles.Count == 0 ? 0m : recentCandles.Average(x => x.Volume);
        var relativeVolume20 = averageVolume20 <= 0m ? 0m : Math.Round(candle.Volume / averageVolume20, 6);
        var closeMinusVwap = candle.Vwap <= 0m ? 0m : candle.Close - candle.Vwap;
        var atr14 = CalculateAtr14(candle, recentCandles);
        _richBarFeatures.Add(new RichBarFeatureInput(
            candle,
            regime,
            regimeBars,
            averageVolume20,
            relativeVolume20,
            closeMinusVwap,
            atr14,
            atr14 <= 0m ? 0m : Math.Round(closeMinusVwap / atr14, 6)));
    }

    private void FlushRichBarFeatures()
    {
        if (_researchLogger is null || _snapshot is null)
            return;

        foreach (var feature in _richBarFeatures)
        {
            _researchLogger.AppendRichBarFeature(
                _snapshot.SnapshotId,
                new ResearchLogger.RichBarFeature(
                    feature.Candle.Time,
                    feature.Candle.Bar,
                    feature.Regime.Regime.ToString(),
                    feature.RegimeBars,
                    feature.Candle.Open,
                    feature.Candle.High,
                    feature.Candle.Low,
                    feature.Candle.Close,
                    feature.Candle.Volume,
                    feature.Candle.Vwap,
                    feature.AverageVolume20,
                    feature.RelativeVolume20,
                    feature.CloseMinusVwap,
                    feature.Atr14,
                    feature.VwapDistanceAtr,
                    feature.Regime.BullTrendScore.TotalScore,
                    feature.Regime.BearTrendScore.TotalScore,
                    SerializeTrendComponents(feature.Regime.BullTrendScore),
                    SerializeTrendComponents(feature.Regime.BearTrendScore)));
        }
        _richBarFeatures.Clear();
    }

    private static string SerializeTrendComponents(ScoreBreakdown score)
    {
        return JsonSerializer.Serialize(score.Components);
    }

    private KnnShadowFeatures BuildKnnShadowFeatures(
        CandidateSignal signal,
        OpfCandle candle,
        string executionDecision,
        string researchPath,
        decimal risk,
        decimal estimatedRr,
        string tradeId)
    {
        var range = Math.Max(0m, candle.High - candle.Low);
        var bodyRatio = range <= 0m ? 0m : Math.Abs(candle.Close - candle.Open) / range;
        var closeLocation = range <= 0m ? 0.5m : (candle.Close - candle.Low) / range;
        var bullScore = _lastRegimeChangeBullScore;
        var bearScore = _lastRegimeChangeBearScore;
        var trendStrength = Math.Max(bullScore, bearScore);
        var alignment = signal.Side == TradeSide.Long ? bullScore - bearScore : bearScore - bullScore;
        var confirmAvailable = signal.SignalId.EndsWith("-OC", StringComparison.Ordinal)
            || signal.SignalId.EndsWith("-OC-STR", StringComparison.Ordinal);
        var confirmReason = confirmAvailable
            ? signal.SkipReasons.LastOrDefault(IsConfirmationReason) ?? "Unknown"
            : "Unknown";
        var confirmRange = confirmAvailable ? range : 0m;
        var confirmBodyRatio = confirmAvailable ? bodyRatio : 0m;
        var confirmCloseLocation = confirmAvailable ? closeLocation : 0.5m;
        var confirmAlignment = confirmAvailable
            ? signal.Side == TradeSide.Long ? candle.Close - candle.Open : candle.Open - candle.Close
            : 0m;
        var confirmPrevBreak = confirmAvailable && _previousCandle is not null
            ? signal.Side == TradeSide.Long
                ? candle.Close - _previousCandle.High
                : _previousCandle.Low - candle.Close
            : 0m;
        var confirmZoneReclaim = confirmAvailable && signal.Zone is not null
            ? signal.Side == TradeSide.Long
                ? candle.Close - signal.Zone.High
                : signal.Zone.Low - candle.Close
            : 0m;
        var hour = candle.Time.Hour + candle.Time.Minute / 60d;
        var radians = 2d * Math.PI * hour / 24d;
        var pullbackCount = signal.Side == TradeSide.Long
            ? _activeBullPullback?.CountInRegime ?? 0
            : _activeBearPullback?.CountInRegime ?? 0;

        var numeric = new[]
        {
            (double)signal.RegimeScore.TotalScore,
            (double)signal.SetupQualityScore.TotalScore,
            (double)risk,
            (double)estimatedRr,
            (double)CalculateAtr14(candle),
            (double)(signal.Zone is null ? 0m : signal.Zone.High - signal.Zone.Low),
            signal.Zone?.TouchCount ?? 0,
            pullbackCount,
            (double)range,
            (double)bodyRatio,
            (double)closeLocation,
            signal.Zone is null ? 0 : Math.Max(0, candle.Bar - signal.Zone.CreatedBar),
            (double)bullScore,
            (double)bearScore,
            (double)trendStrength,
            (double)alignment,
            (double)confirmRange,
            (double)confirmBodyRatio,
            (double)confirmCloseLocation,
            (double)confirmAlignment,
            (double)confirmPrevBreak,
            (double)confirmZoneReclaim,
            Math.Sin(radians),
            Math.Cos(radians),
            ((int)candle.Time.DayOfWeek + 6) % 7
        };

        var featureSkipReasons = executionDecision == "Execute"
            ? signal.SkipReasons.Concat(new[] { "ReplayExecuted", researchPath, tradeId })
            : signal.SkipReasons;
        var normalizedSkipReasons = featureSkipReasons
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(NormalizeKnnSkipReason)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        var categorical = new[]
        {
            candle.Time.Month >= 10 ? "Q4" : "H1",
            KnnSession(candle.Time.Hour),
            EmptyAsUnknown(signal.Zone?.ZoneType),
            EmptyAsUnknown(signal.Zone?.Freshness),
            KnnRegimeBucket(signal.RegimeScore.TotalScore, signal.RegimeScore.Threshold),
            KnnSetupQualityBucket(signal.SetupQualityScore.TotalScore),
            KnnRiskBucket(risk),
            KnnRrBucket(estimatedRr),
            KnnTimeBucket(candle.Time),
            normalizedSkipReasons.Length == 0 ? "None" : string.Join("+", normalizedSkipReasons),
            HasKnnToken(normalizedSkipReasons, "ZoneBirthResearch").ToString(),
            HasKnnToken(normalizedSkipReasons, "ObservationConfirmResearch").ToString(),
            HasKnnToken(normalizedSkipReasons, "ObservationConfirmStrictResearch").ToString(),
            HasKnnToken(normalizedSkipReasons, "FailureReverseResearch").ToString(),
            HasKnnToken(normalizedSkipReasons, "FailureRetestFailedTriggered").ToString(),
            HasKnnToken(normalizedSkipReasons, "BreakawayQualified").ToString(),
            confirmAvailable.ToString(),
            confirmReason
        };
        return new KnnShadowFeatures(numeric, categorical);
    }

    private static bool IsConfirmationReason(string value)
    {
        return value is "ReclaimZoneHigh" or "ReclaimZoneLow" or "BreakPrevHigh" or "BreakPrevLow";
    }

    private static string NormalizeKnnSkipReason(string value)
    {
        var equals = value.IndexOf('=');
        var colon = value.IndexOf(':');
        var end = new[] { equals, colon }.Where(x => x >= 0).DefaultIfEmpty(value.Length).Min();
        return value[..end];
    }

    private static bool HasKnnToken(IEnumerable<string> values, string token) => values.Contains(token, StringComparer.Ordinal);

    private static string EmptyAsUnknown(string? value) => string.IsNullOrWhiteSpace(value) ? "Unknown" : value;

    private static string KnnSession(int hour) => hour <= 5 ? "Asia" : hour <= 12 ? "Europe" : hour <= 19 ? "US" : "Late";

    private static string KnnRegimeBucket(decimal score, decimal threshold)
    {
        if (threshold > 0m && score >= threshold)
            return "Passed";
        return score >= 50m ? "Weak50_69" : "WeakLT50";
    }

    private static string KnnSetupQualityBucket(decimal score)
    {
        if (score >= 80m)
            return "Q80Plus";
        if (score >= 70m)
            return "Q70_79";
        return score >= 50m ? "Q50_69" : "QLT50";
    }

    private static string KnnRiskBucket(decimal risk)
    {
        if (risk <= 0m)
            return "None";
        if (risk <= 8.5m)
            return "RiskLE8_5";
        if (risk <= 11m)
            return "RiskLE11";
        return risk <= 15m ? "RiskLE15" : "RiskGT15";
    }

    private static string KnnRrBucket(decimal rr)
    {
        if (rr >= 1.5m)
            return "RR_GE_1_5";
        if (rr >= 1.2m)
            return "RR_GE_1_2";
        return rr >= 1m ? "RR_GE_1_0" : "RR_LT_1_0";
    }

    private static string KnnTimeBucket(DateTime time)
    {
        var value = time.TimeOfDay;
        if (value < new TimeSpan(10, 0, 0))
            return "Pre10";
        if (value < new TimeSpan(11, 30, 0))
            return "Morning";
        if (value < new TimeSpan(13, 30, 0))
            return "Midday";
        return value < new TimeSpan(15, 30, 0) ? "Afternoon" : "Late";
    }

    private sealed record KnnShadowFeatures(double[] Numeric, string[] Categorical);

    private sealed record KnnShadowInput(
        string SnapshotId,
        string SignalId,
        DateTime Time,
        int Bar,
        string Side,
        string ResearchPath,
        double[] Numeric,
        string[] Categorical);

    private sealed record RichBarFeatureInput(
        OpfCandle Candle,
        RegimeResult Regime,
        int RegimeBars,
        decimal AverageVolume20,
        decimal RelativeVolume20,
        decimal CloseMinusVwap,
        decimal Atr14,
        decimal VwapDistanceAtr);

    private bool IsReplayExecutionPathEnabled(string researchPath)
    {
        var configured = ReplayExecutionPath.Split(new[] { '|', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return configured.Any(x => x == "*" || string.Equals(x, researchPath, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsExecutionEligiblePath(string researchPath)
    {
        return researchPath is
            "ObservationConfirm" or
            "ObservationConfirm_WideStop1_5R" or
            "BreakawayFvg" or
            "BreakawayFvg_Qualified" or
            "FailureReverse_ObservationInvalidated_WideStop1_5R" or
            "FailureReverse_RetestFailed" or
            "SignificantZoneFirstTouchLong" or
            "SignificantZoneFirstTouchShort";
    }

    private static bool IsRestoredPriorityPathAllowed(TradeSide side, string researchPath)
    {
        return researchPath switch
        {
            "ObservationConfirm" or "ObservationConfirm_WideStop1_5R" or "BreakawayFvg_Qualified" => true,
            "BreakawayFvg" => side == TradeSide.Short,
            "FailureReverse_ObservationInvalidated_WideStop1_5R" or "FailureReverse_RetestFailed" => side == TradeSide.Short,
            "SignificantZoneFirstTouchLong" => side == TradeSide.Long,
            "SignificantZoneFirstTouchShort" => side == TradeSide.Short,
            _ => false
        };
    }

    private static bool IsSignificantZoneFirstTouchPath(string researchPath)
    {
        return researchPath is SignificantZoneFirstTouchLongPath or SignificantZoneFirstTouchShortPath;
    }

    private string[] ActualExecutionStrategySkipReasons(CandidateSignal signal, string researchPath)
    {
        var reasons = new List<string>();
        var isSignificantZoneFirstTouch = IsSignificantZoneFirstTouchPath(researchPath);
        var isImmediateFailureReverse = IsImmediateFailureReversePath(signal, researchPath);
        var isFailureReversePreEntry = IsFailureReversePreEntryWideShortPath(signal, researchPath);
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
        var isAggressiveExpansionV164 = TryGetAggressiveExpansionV164Rules(signal.Side, researchPath, out var aggressiveMinScore, out _, out _);
        if (_actualRequireTrendRegime && !signal.RegimeScore.Passed && !isSignificantZoneFirstTouch && !isImmediateFailureReverse && !isFailureReversePreEntry && !isObservationConfirm && !isFailureRetest && !isDailyVolumeFloor && !isMainlineVolumeFiller && !isV122VolumeExpansion && !isBreakawayVolumeExpansion && !isUnknownMicroRiskVolumeExpansion && !isObservationConfirmWideStopVolume && !isObservationConfirmWideStopLowRisk && !isObservationConfirmRisk22 && !isAggressiveExpansionV164)
            reasons.Add($"StrategyRegimeNotTrend:score={signal.RegimeScore.TotalScore:0.##}");

        var minSetupQuality = isSignificantZoneFirstTouch
            ? 0m
            : isDailyVolumeFloor
            ? DailyVolumeFloorMinSetupQualityScore
            : isMainlineVolumeFiller
            ? MainlineVolumeFillerMinSetupQualityScore
            : isAggressiveExpansionV164
            ? aggressiveMinScore
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
            : isImmediateFailureReverse || isFailureReversePreEntry
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
        if (IsZoneBirthShortExpansionV168(signal, researchPath))
            return risk <= ZoneBirthShortV170LowRiskMaxPoints
                ? ZoneBirthShortV170LowRiskMaxPoints
                : ZoneBirthShortV170MidRiskMaxPoints;
        if (TryGetAggressiveExpansionV164Rules(signal.Side, researchPath, out _, out _, out _))
            return AggressiveExpansionV164MaxRiskPoints;
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
        if (IsObservationConfirmWideStopVolumeV131(signal, researchPath))
            return ObservationConfirmWideStopVolumeV131MaxRiskPoints;
        if (IsObservationConfirmWideStopLowRiskV132Quality(signal, researchPath))
            return ObservationConfirmWideStopLowRiskV132MaxRiskPoints;
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
        if (IsDisabledActualPathV209(signal.Side, researchPath))
            return new[] { $"ActualPathDisabledV209:side={signal.Side}|path={researchPath}" };

        if (signal.Side == TradeSide.Long &&
            string.Equals(researchPath, "FailureReverse_RetestFailed", StringComparison.OrdinalIgnoreCase))
            return new[] { "LongFailureReverseRetestFailedDisabledV208" };

        if (IsBreakawayPath(researchPath) &&
            signal.Side == TradeSide.Long)
            return new[] { "BreakawayLongActualDisabledV135" };

        var isAggressiveExpansionV164 = TryGetAggressiveExpansionV164Rules(signal.Side, researchPath, out _, out _, out _);
        if (IsAggressiveExpansionV164Path(researchPath) && !isAggressiveExpansionV164)
            return new[] { $"AggressiveExpansionV164SideDisabled:path={researchPath},side={signal.Side}" };

        if (IsEvidenceFrozenActualPath(researchPath) && !isAggressiveExpansionV164)
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

        if (string.Equals(researchPath, "AlmostConfirmed", StringComparison.OrdinalIgnoreCase) && !isAggressiveExpansionV164)
            return new[] { "AlmostConfirmedActualDisabledV131" };

        if (string.Equals(researchPath, "ObservationStrict_BullFresh", StringComparison.OrdinalIgnoreCase))
            return new[] { "StrictBullFreshActualDisabledV131" };

        if (IsZoneBirthVolumeExpansionPath(researchPath) && signal.Side == TradeSide.Short && !isAggressiveExpansionV164)
            return new[] { "ZoneBirthShortActualDisabledV123" };

        if (IsBreakawayPath(researchPath) && !IsBreakawayVolumeExpansionV128(signal, researchPath))
            return new[] { $"BreakawayVolumeV128QualityTooLow:score={signal.SetupQualityScore.TotalScore:0.##},min={BreakawayVolumeV128MinSetupQualityScore:0.##}" };

        if (IsZoneBirthVolumeExpansionPath(researchPath) && !isAggressiveExpansionV164 && !IsZoneBirthVolumeExpansionV122(signal, researchPath))
            return new[] { $"ZoneBirthVolumeV122QualityTooLow:score={signal.SetupQualityScore.TotalScore:0.##},min={ZoneBirthVolumeV122MinSetupQualityScore:0.##}" };

        if (IsUnknownMicroRiskVolumeExpansionPath(researchPath) && !isAggressiveExpansionV164)
            return new[] { "UnknownMicroRiskActualDisabledV127" };

        if (IsStrictVolumeExpansionPath(researchPath) && !isAggressiveExpansionV164 && !IsStrictVolumeExpansionV122(signal, researchPath))
            return new[] { $"StrictObservationVolumeV122QualityTooLow:score={signal.SetupQualityScore.TotalScore:0.##},min={StrictVolumeV122MinSetupQualityScore:0.##}" };

        if (IsDailyVolumeResearchFillerPath(researchPath) && !IsZoneBirthVolumeExpansionPath(researchPath) && !IsDailyVolumeResearchFillerAllowed(signal, researchPath))
            return new[] { $"DailyVolumeResearchFillerOnlyBeforeDailyTarget:trades={_replayTradesToday},target={_actualObservationConfirmFillerUntilDailyTrades}" };

        if (IsMainlineVolumeFillerPath(researchPath) && !isAggressiveExpansionV164 && !IsMainlineVolumeFiller(signal, researchPath))
            return new[] { $"MainlineVolumeFillerQualityTooLow:score={signal.SetupQualityScore.TotalScore:0.##},min={MainlineVolumeFillerMinSetupQualityScore:0.##}" };

        if (IsDailyVolumeFloorPath(researchPath) && !isAggressiveExpansionV164 && !IsObservationConfirmWideStopVolumeV131(signal, researchPath) && !IsDailyVolumeFloorAllowed(signal, researchPath))
            return new[] { $"DailyVolumeFloorOnlyBeforeDailyTarget:trades={_replayTradesToday},target={_actualObservationConfirmFillerUntilDailyTrades},score={signal.SetupQualityScore.TotalScore:0.##},min={DailyVolumeFloorMinSetupQualityScore:0.##}" };

        if (string.Equals(researchPath, "FailureReverse_ObservationInvalidated", StringComparison.OrdinalIgnoreCase) &&
            signal.Side == TradeSide.Long &&
            !isAggressiveExpansionV164)
            return new[] { "FailureImmediateLongDisabled" };

        if (IsFailureRetestWideStopPath(researchPath) && !isAggressiveExpansionV164)
            return new[] { "FailureRetestWideStopActualDisabledV136" };

        if (IsFailureRetestWideStopPath(researchPath) && !isAggressiveExpansionV164 && !IsFailureRetestWideStopVolumeFiller(researchPath))
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

    private static bool IsFailureReversePreEntryWideShortPath(CandidateSignal signal, string researchPath)
    {
        return signal.SetupType == SetupType.FailureReverse && signal.Side == TradeSide.Short &&
            string.Equals(researchPath, "FailureReverse_PreEntryConfirmedWideStopShort", StringComparison.OrdinalIgnoreCase);
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

    private static bool IsAggressiveExpansionV164Path(string researchPath)
    {
        return string.Equals(researchPath, "ObservationStrict_Other", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(researchPath, "ObservationStrict_Other_WideStop1_5R", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(researchPath, "ObservationStrict_BullFresh_WideStop1_5R", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(researchPath, "TrendPullbackConfirmed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(researchPath, "FailureReverse_RetestFailed_WideStop1_5R", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(researchPath, "FailureReverse_ObservationInvalidated_WideStop1_5R", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(researchPath, "FailureReverse_ObservationInvalidated", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(researchPath, "UnknownRegimeZoneTouch", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(researchPath, "ShadowCandidate", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(researchPath, "AlmostConfirmed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(researchPath, "ZoneBirthResearch", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetAggressiveExpansionV164Rules(TradeSide side, string researchPath, out decimal minScore, out decimal minEstimatedRr, out string tag)
    {
        minScore = 0m;
        minEstimatedRr = 0m;
        tag = string.Empty;

        if (side == TradeSide.Long && string.Equals(researchPath, "ObservationStrict_Other", StringComparison.OrdinalIgnoreCase))
            (minScore, minEstimatedRr, tag) = (35m, 0.25m, "ObservationStrictOtherLongV164");
        else if (side == TradeSide.Long && string.Equals(researchPath, "ObservationStrict_Other_WideStop1_5R", StringComparison.OrdinalIgnoreCase))
            (minScore, minEstimatedRr, tag) = (35m, 0.25m, "ObservationStrictOtherWideStopLongV164");
        else if (side == TradeSide.Long && string.Equals(researchPath, "ObservationStrict_BullFresh_WideStop1_5R", StringComparison.OrdinalIgnoreCase))
            (minScore, minEstimatedRr, tag) = (45m, 0.5m, "ObservationStrictBullFreshWideStopLongV164");
        else if (side == TradeSide.Short && string.Equals(researchPath, "TrendPullbackConfirmed", StringComparison.OrdinalIgnoreCase))
            (minScore, minEstimatedRr, tag) = (35m, 0.25m, "TrendPullbackConfirmedShortV164");
        else if (side == TradeSide.Short && string.Equals(researchPath, "FailureReverse_RetestFailed_WideStop1_5R", StringComparison.OrdinalIgnoreCase))
            (minScore, minEstimatedRr, tag) = (35m, 0.25m, "FailureRetestFailedWideStopShortV164");
        else if (side == TradeSide.Long && string.Equals(researchPath, "FailureReverse_ObservationInvalidated_WideStop1_5R", StringComparison.OrdinalIgnoreCase))
            (minScore, minEstimatedRr, tag) = (45m, 0.5m, "FailureObservationInvalidatedWideStopLongV164");
        else if (side == TradeSide.Short && string.Equals(researchPath, "FailureReverse_ObservationInvalidated_WideStop1_5R", StringComparison.OrdinalIgnoreCase))
            (minScore, minEstimatedRr, tag) = (35m, 0.25m, "FailureObservationInvalidatedWideStopShortV164");
        else if (side == TradeSide.Short && string.Equals(researchPath, "UnknownRegimeZoneTouch", StringComparison.OrdinalIgnoreCase))
            (minScore, minEstimatedRr, tag) = (45m, 0.25m, "UnknownRegimeZoneTouchShortV164");
        else if (side == TradeSide.Short && string.Equals(researchPath, "FailureReverse_ObservationInvalidated", StringComparison.OrdinalIgnoreCase))
            (minScore, minEstimatedRr, tag) = (35m, 0.25m, "FailureObservationInvalidatedShortV167");
        else if (side == TradeSide.Short && string.Equals(researchPath, "ShadowCandidate", StringComparison.OrdinalIgnoreCase))
            (minScore, minEstimatedRr, tag) = (35m, 0.25m, "ShadowCandidateShortV167");
        else if (side == TradeSide.Long && string.Equals(researchPath, "AlmostConfirmed", StringComparison.OrdinalIgnoreCase))
            (minScore, minEstimatedRr, tag) = (35m, 0.25m, "AlmostConfirmedLongV167");
        else if (side == TradeSide.Long && string.Equals(researchPath, "FailureReverse_ObservationInvalidated", StringComparison.OrdinalIgnoreCase))
            (minScore, minEstimatedRr, tag) = (35m, 0.25m, "FailureObservationInvalidatedLongV168");
        else if (side == TradeSide.Short && string.Equals(researchPath, "ZoneBirthResearch", StringComparison.OrdinalIgnoreCase))
            (minScore, minEstimatedRr, tag) = (35m, 0.25m, "ZoneBirthShortV168");

        return !string.IsNullOrEmpty(tag);
    }

    private static bool IsZoneBirthShortExpansionV168(CandidateSignal signal, string researchPath)
    {
        return signal.Side == TradeSide.Short &&
            string.Equals(researchPath, "ZoneBirthResearch", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsZoneBirthShortV170RiskAllowed(decimal risk)
    {
        return risk <= ZoneBirthShortV170LowRiskMaxPoints ||
            (risk > ZoneBirthShortV170MidRiskMinExclusivePoints && risk <= ZoneBirthShortV170MidRiskMaxPoints);
    }

    private static string ZoneBirthShortV170RiskBandTag(decimal risk)
    {
        return risk <= ZoneBirthShortV170LowRiskMaxPoints ? "LowRiskLe8" : "MidRiskGt12Le18";
    }

    private static bool UsesZoneBirthSplitRunnerV172(TradeSide side, string researchPath)
    {
        return side == TradeSide.Short &&
            string.Equals(researchPath, "ZoneBirthResearch", StringComparison.OrdinalIgnoreCase);
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
            _observationConfirmWideStopLongExpansionTradesToday < ObservationConfirmWideStopLongExpansionV163MaxTradesPerDay;
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
        var isSignificantZoneFirstTouch = IsSignificantZoneFirstTouchPath(researchPath);
        if (IsStrictEntryExcludedRiskV208(risk))
            reasons.Add($"StrictEntryRiskBandExcludedV208:risk={risk:0.##},excluded=({StrictEntryExcludedRiskMinExclusiveV208:0.##},{StrictEntryExcludedRiskMaxInclusiveV208:0.##}]");
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
        var isAggressiveExpansionV164 = TryGetAggressiveExpansionV164Rules(signal.Side, researchPath, out _, out var aggressiveMinEstimatedRr, out _);
        if (IsZoneBirthShortExpansionV168(signal, researchPath) && !IsZoneBirthShortV170RiskAllowed(risk))
            reasons.Add($"ZoneBirthShortV170RiskBandExcluded:risk={risk:0.##},allowed=<=8|>12<=18");
        if (isAggressiveExpansionV164 && risk > AggressiveExpansionV164MaxRiskPoints)
            reasons.Add($"AggressiveExpansionV164RiskCapExceeded:risk={risk:0.##},max={AggressiveExpansionV164MaxRiskPoints:0.##}");
        if (isDailyVolumeFloor && !isAggressiveExpansionV164 && !isObservationConfirmWideStopVolume && risk > DailyVolumeFloorMaxRiskPoints)
            reasons.Add($"DailyVolumeFloorRiskCapExceeded:risk={risk:0.##},max={DailyVolumeFloorMaxRiskPoints:0.##}");
        if (isMainlineVolumeFiller && !isAggressiveExpansionV164 && risk > MainlineVolumeFillerMaxRiskPoints)
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
            reasons.Add($"OCWideStopLongExpansionV163DailyCap:count={_observationConfirmWideStopLongExpansionTradesToday},max={ObservationConfirmWideStopLongExpansionV163MaxTradesPerDay}");
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
        if (isUnknownMicroRiskVolumeExpansion && !isAggressiveExpansionV164 && risk > UnknownMicroRiskVolumeV126MaxRiskPoints)
            reasons.Add($"UnknownMicroRiskVolumeV126RiskCapExceeded:risk={risk:0.##},max={UnknownMicroRiskVolumeV126MaxRiskPoints:0.##}");
        if (IsZoneBirthVolumeExpansionV122(signal, researchPath) && !isAggressiveExpansionV164 && risk > ZoneBirthVolumeV122MaxRiskPoints)
            reasons.Add($"ZoneBirthVolumeV122RiskCapExceeded:risk={risk:0.##},max={ZoneBirthVolumeV122MaxRiskPoints:0.##}");
        if (IsStrictVolumeExpansionV122(signal, researchPath) && !isAggressiveExpansionV164 && risk > StrictVolumeV122MaxRiskPoints)
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
        if (IsFailureRetestPath(researchPath) && !isAggressiveExpansionV164 && !isDailyVolumeFloor && _actualFailureRetestMaxRiskPoints > 0m && risk > _actualFailureRetestMaxRiskPoints)
            reasons.Add($"FailureRetestRiskCapExceeded:risk={risk:0.##},max={_actualFailureRetestMaxRiskPoints:0.##}");
        if (IsDailyVolumeResearchFillerPath(researchPath) && !isAggressiveExpansionV164 && !IsZoneBirthVolumeExpansionV122(signal, researchPath) && risk > DailyVolumeResearchFillerMaxRiskPoints)
            reasons.Add($"DailyVolumeResearchFillerRiskCapExceeded:risk={risk:0.##},max={DailyVolumeResearchFillerMaxRiskPoints:0.##}");

        if (!isBreakawayVolumeExpansion && instrument.MaxRiskPointsHard > 0m && risk > instrument.MaxRiskPointsHard)
            reasons.Add($"RiskTooWideHard:risk={risk:0.##},max={instrument.MaxRiskPointsHard:0.##}");

        var atr14 = CalculateAtr14(entryCandle);
        var maxAllowedRisk = MaxAllowedRiskPoints(instrument, atr14);
        if (!isDailyVolumeFloor && !isMainlineVolumeFiller && !isObservationVolumeRiskBand && !isDailyVolumeQualityRescue && !isV122VolumeExpansion && !isBreakawayVolumeExpansion && !isObservationLongVolumeExpansion && !isObservationShortVolumeExpansion && !isUnknownMicroRiskVolumeExpansion && !isObservationConfirmWideStopVolume && !isObservationConfirmWideStopLowRisk && !isObservationConfirmRisk22 && !isAggressiveExpansionV164 && maxAllowedRisk > 0m && risk > maxAllowedRisk)
            reasons.Add($"RiskTooWideVolAdjusted:risk={risk:0.##},max={maxAllowedRisk:0.##},atr14={atr14:0.##}");

        var reward = EstimateActualExecutionReward(signal, researchPath, entryCandle.Close, risk);
        var estimatedRr = EstimatedActualRr(signal, researchPath, entryCandle.Close, risk);
        var minEstimatedRr = isSignificantZoneFirstTouch
            ? 0m
            : isAggressiveExpansionV164
            ? aggressiveMinEstimatedRr
            : isObservationConfirmWideStopLowRisk
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

    private static bool IsStrictEntryExcludedRiskV208(decimal risk)
    {
        return risk > StrictEntryExcludedRiskMinExclusiveV208 && risk <= StrictEntryExcludedRiskMaxInclusiveV208;
    }

    private decimal EstimatedActualRr(CandidateSignal signal, string researchPath, decimal entry, decimal risk)
    {
        var reward = EstimateActualExecutionReward(signal, researchPath, entry, risk);
        return reward.Points <= 0m || risk <= 0m ? 0m : Math.Round(reward.Points / risk, 4);
    }

    private RewardEstimate EstimateActualExecutionReward(CandidateSignal signal, string researchPath, decimal entry, decimal risk)
    {
        var targetR = ActualTargetRFor(signal, researchPath, risk);
        if (TryGetAggressiveExpansionV164Rules(signal.Side, researchPath, out _, out _, out _))
        {
            var eligibilityReward = EstimateReward(signal, researchPath, entry, risk);
            return new RewardEstimate(eligibilityReward.Points, $"AggressiveExpansionV164Eligibility:{eligibilityReward.Model}");
        }
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
        if (TryGetActualExitPolicyV209(side, researchPath, out var targetR, out _, out _, risk))
            return targetR;

        if (UsesProtectBreakEvenV186(side, researchPath))
            return ProtectBreakEvenTargetRV186;

        if (TryGetAggressiveExpansionV164Rules(side, researchPath, out _, out _, out _))
            return AggressiveExpansionTargetR(side, researchPath);

        if (!string.IsNullOrEmpty(ObservationConfirmLongTarget2RV161Tag(side, researchPath, risk)))
            return ObservationConfirmLongTarget2RV161R;

        if (!string.IsNullOrEmpty(SelectiveProfitTargetV145Tag(side, researchPath)))
            return SelectiveProfitTargetV145R;

        return ReplayTargetR;
    }

    private static bool UsesProtectBreakEvenV186(TradeSide side, string researchPath)
    {
        if (TryGetActualExitPolicyV209(side, researchPath, out _, out var breakEvenTriggerR, out _))
            return breakEvenTriggerR > 0m;

        return !UseLegacyV174StrategyPolicyV204 &&
            IsExecutionEligiblePath(researchPath) &&
            !UsesZoneBirthSplitRunnerV172(side, researchPath);
    }

    private static bool IsDisabledActualPathV209(TradeSide side, string researchPath)
    {
        return (side == TradeSide.Long &&
                (string.Equals(researchPath, "FailureReverse_RetestFailed", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(researchPath, "ObservationStrict_BullFresh_WideStop1_5R", StringComparison.OrdinalIgnoreCase))) ||
            (side == TradeSide.Short &&
                (string.Equals(researchPath, "FailureReverse_ObservationInvalidated", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(researchPath, "FailureReverse_RetestFailed_WideStop1_5R", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(researchPath, "ShadowCandidate", StringComparison.OrdinalIgnoreCase)));
    }

    private static bool TryGetActualExitPolicyV209(
        TradeSide side,
        string researchPath,
        out decimal targetR,
        out decimal breakEvenTriggerR,
        out int timeStopBars,
        decimal risk = 0m)
    {
        targetR = 0m;
        breakEvenTriggerR = 0m;
        timeStopBars = 0;

        if (IsStaticDynamicExitV215(side, researchPath, risk))
            (targetR, timeStopBars) = (1.5m, 12);
        else if (side == TradeSide.Short && string.Equals(researchPath, "BreakawayFvg", StringComparison.OrdinalIgnoreCase))
            (targetR, breakEvenTriggerR, timeStopBars) = (BreakawayShortTargetRV209, BreakawayShortBreakEvenTriggerRV209, ShortPolicyTimeStopBarsV209);
        else if (side == TradeSide.Long && string.Equals(researchPath, "ObservationConfirm", StringComparison.OrdinalIgnoreCase))
            (targetR, timeStopBars) = (4m, LongPolicyTimeStopBarsV209);
        else if (side == TradeSide.Long && string.Equals(researchPath, "ObservationConfirm_WideStop1_5R", StringComparison.OrdinalIgnoreCase))
            (targetR, timeStopBars) = (3m, FastPolicyTimeStopBarsV209);
        else if (side == TradeSide.Long && string.Equals(researchPath, "ObservationStrict_Other", StringComparison.OrdinalIgnoreCase))
            (targetR, timeStopBars) = (3m, FastPolicyTimeStopBarsV209);
        else if (side == TradeSide.Long && string.Equals(researchPath, "AlmostConfirmed", StringComparison.OrdinalIgnoreCase))
            (targetR, timeStopBars) = (4m, LongPolicyTimeStopBarsV209);
        else if (side == TradeSide.Short && string.Equals(researchPath, "FailureReverse_ObservationInvalidated_WideStop1_5R", StringComparison.OrdinalIgnoreCase))
            (targetR, timeStopBars) = (4m, ShortPolicyTimeStopBarsV209);
        else if (side == TradeSide.Short && string.Equals(researchPath, "FailureReverse_PreEntryConfirmedWideStopShort", StringComparison.OrdinalIgnoreCase))
            (targetR, timeStopBars) = (4m, ShortPolicyTimeStopBarsV209);

        return targetR > 0m;
    }

    private static bool IsStaticDynamicExitV215(TradeSide side, string researchPath, decimal risk)
    {
        if (risk <= 0m || risk > 20m)
            return false;
        if (side == TradeSide.Long && risk <= 10m)
            return string.Equals(researchPath, "ObservationConfirm", StringComparison.OrdinalIgnoreCase);
        return side == TradeSide.Short &&
            string.Equals(researchPath, "ZoneBirthResearch", StringComparison.OrdinalIgnoreCase);
    }

    private static string StaticRiskBandV215(decimal risk)
    {
        if (risk <= 10m)
            return "R10";
        if (risk <= 20m)
            return "R20";
        if (risk <= 30m)
            return "R30";
        return "RHigh";
    }

    private static bool IsStaticSecondaryEligibleV215(TradeSide side, string researchPath, decimal risk)
    {
        var key = $"{side}|{researchPath}|{StaticRiskBandV215(risk)}";
        return key is
            "Short|FailureReverse_RetestFailed_WideStop1_5R|R10" or
            "Short|TrendPullbackConfirmed|R30" or
            "Short|ObservationConfirm_WideStop1_5R|R20" or
            "Long|ObservationConfirm_WideStop1_5R|R10" or
            "Short|UnknownRegimeZoneTouch|R10" or
            "Short|FailureReverse_RetestFailed|R20" or
            "Long|FailureReverse_ObservationInvalidated_WideStop1_5R|R10" or
            "Long|BreakawayFvg_Qualified|R10" or
            "Short|ShadowCandidate|R30" or
            "Long|AlmostConfirmed|R10" or
            "Short|ObservationConfirm_WideStop1_5R|R10" or
            "Short|UnknownRegimeZoneTouch|R30" or
            "Short|FailureReverse_ObservationInvalidated_WideStop1_5R|R10" or
            "Long|BreakawayRetest|R10" or
            "Long|BreakawayRetest|R20" or
            "Long|ObservationConfirm|R20" or
            "Long|ObservationStrict_Other_WideStop1_5R|R20" or
            "Long|AlmostConfirmed|R30" or
            "Long|FailureReverse_ObservationInvalidated_WideStop1_5R|R20" or
            "Short|ZoneBirthResearch|R10" or
            "Long|AlmostConfirmed|R20" or
            "Long|ObservationConfirm_WideStop1_5R|R20" or
            "Long|ObservationConfirm_WideStop1_5R|R30" or
            "Short|FailureReverse_ObservationInvalidated|R20" or
            "Long|ObservationStrict_Other|R30" or
            "Short|FailureReverse_ObservationInvalidated_WideStop1_5R|R30" or
            "Short|ObservationConfirm|R30" or
            "Short|FailureReverse_ObservationInvalidated_WideStop1_5R|R20" or
            "Short|BreakawayFvg|R30";
    }

    private static decimal AggressiveExpansionTargetR(TradeSide side, string researchPath)
    {
        if (string.Equals(researchPath, "FailureReverse_ObservationInvalidated", StringComparison.OrdinalIgnoreCase) ||
            (side == TradeSide.Long && string.Equals(researchPath, "AlmostConfirmed", StringComparison.OrdinalIgnoreCase)) ||
            (side == TradeSide.Short && string.Equals(researchPath, "ZoneBirthResearch", StringComparison.OrdinalIgnoreCase)))
        {
            return DynamicExpansionV168TargetR;
        }

        return AggressiveExpansionV164TargetR;
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

    private static string ObservationConfirmLongTarget2RV161Tag(TradeSide side, string researchPath, decimal plannedRisk)
    {
        return side == TradeSide.Long &&
            string.Equals(researchPath, "ObservationConfirm", StringComparison.OrdinalIgnoreCase) &&
            plannedRisk > ObservationConfirmLongTarget2RV161MinRiskPoints &&
            plannedRisk <= ObservationConfirmLongTarget2RV161MaxRiskPoints
                ? "ObservationConfirmLongTarget2RV161"
                : string.Empty;
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
        RollLiveWeeklyPnlIfNeeded(date);
        if (_replayExecutionDate == date)
            return;

        _deferredContinuations.Clear();
        _pendingSignificantZoneFirstTouches.Clear();
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
        if (_liveAccountPnlDate != date)
        {
            _liveAccountPnlDate = date;
            _liveAccountDailyNetPnlDollars = 0m;
        }
        _replayAbnormalEntryToday = 0;
        _lastAbnormalEntryHudText = "-";
        _replayAbnormalProtectiveFillToday = 0;
        _lastAbnormalProtectiveFillHudText = "-";
        _lastExecutionHudText = "-";
        _recentExecutionHudItems.Clear();
    }

    private void RollGlobexTradingDayIfNeeded(OpfCandle candle)
    {
        var tradingDay = GlobexTradingDayKey(candle.Time);
        if (_replayExecutionDate == tradingDay)
            return;

        var previous = _replayExecutionDate;
        ResetReplayExecutionDailyCounter(tradingDay);
        var message = $"previous={(previous == DateTime.MinValue ? "-" : previous.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))}|current={tradingDay:yyyy-MM-dd}";
        LogExecutionInfo($"EXEC_GLOBEX_TRADING_DAY_ROLLOVER {message}");
        AppendStandaloneExecutionEvent("GLOBEX_TRADING_DAY_ROLLOVER", "-", candle, candle.Close, 0m, message);
    }

    private void QuarantineAbnormalEntryV174(ReplayExecutionState execution, string reason)
    {
        if (execution.AbnormalEntryQuarantined)
            return;

        execution.AbnormalEntryQuarantined = true;
        _replayAbnormalEntryToday++;
        var drift = Math.Abs(execution.EntryAvgPrice - execution.CreatedPrice);
        _lastAbnormalEntryHudText = $"{execution.CreatedTime:HH:mm} {execution.TradeId} planned={execution.CreatedPrice:0.##} fill={execution.EntryAvgPrice:0.##} drift={drift:0.##} qty={execution.EntryFilledQty:0.##}";
        RollBackNormalTradeCounters(execution);

        LogExecutionInfo($"EXEC_ENTRY_FILL_QUARANTINED_V174 trade={execution.TradeId} reason={reason}");
        AppendExecutionEvent(execution, "ENTRY_FILL_QUARANTINED_V174", "ENTRY", execution.EntryAvgPrice, execution.EntryFilledQty, reason);
        RaiseShowNotification($"Abnormal entry quarantined: {_lastAbnormalEntryHudText}. Strategy will continue after flatten and cleanup.", "OPFStrategyV1");
    }

    private void QuarantineAbnormalProtectiveFillV177(ReplayExecutionState execution, string role, decimal fillPrice, string reason)
    {
        if (execution.AbnormalProtectiveFillQuarantined)
            return;

        execution.AbnormalProtectiveFillQuarantined = true;
        execution.AbnormalProtectiveFillReason = reason;
        execution.EmergencyFlattenSubmitted = true;
        execution.HistoricalVirtualTargetRolesV220.Clear();
        execution.HistoricalVirtualTargetActivationPendingV220.Clear();
        _replayAbnormalProtectiveFillToday++;
        _lastAbnormalProtectiveFillHudText = $"{execution.CreatedTime:HH:mm} {execution.TradeId} {role} fill={fillPrice:0.##}";
        RollBackNormalTradeCounters(execution);
        if (execution.ZoneBirthSplitRunnerV172)
        {
            execution.SplitIsAbnormal = true;
            AppendSplitValidationReasonV172(execution, reason);
        }

        LogExecutionInfo($"EXEC_PROTECTIVE_FILL_QUARANTINED_V177 trade={execution.TradeId} role={role} reason={reason}");
        AppendExecutionEvent(execution, "PROTECTIVE_FILL_QUARANTINED_V177", role, fillPrice, execution.ExitFilledQty, reason);
        RaiseShowNotification($"Unreachable Replay protective fill quarantined: {_lastAbnormalProtectiveFillHudText}. It is excluded from normal statistics and later signals remain eligible.", "OPFStrategyV1");
    }

    private void QuarantineProtectionSetupV178(ReplayExecutionState execution, decimal reference, string reason)
    {
        if (execution.AbnormalProtectiveFillQuarantined)
            return;

        execution.AbnormalProtectiveFillQuarantined = true;
        execution.AbnormalProtectiveFillReason = reason;
        _replayAbnormalProtectiveFillToday++;
        _lastAbnormalProtectiveFillHudText = $"{execution.CreatedTime:HH:mm} {execution.TradeId} setup reference={reference:0.##}";
        RollBackNormalTradeCounters(execution);
        LogExecutionInfo($"EXEC_PROTECTION_SETUP_QUARANTINED_V178 trade={execution.TradeId} reason={reason}");
        AppendExecutionEvent(execution, "PROTECTION_SETUP_QUARANTINED_V178", "-", reference, execution.EntryFilledQty, reason);
        RaiseShowNotification($"Protection was not submitted because execution quotes were not synchronized: {execution.TradeId}. Emergency flatten is being submitted and the trade is excluded from normal statistics.", "OPFStrategyV1");
    }

    private void RollBackNormalTradeCounters(ReplayExecutionState execution)
    {
        if (execution.DailyCountersRolledBack || _replayExecutionDate != GlobexTradingDayKey(execution.CreatedTime))
            return;

        _replayTradesToday = Math.Max(0, _replayTradesToday - 1);
        if (execution.CountedAsObservationFiller)
            _observationConfirmFillerTradesToday = Math.Max(0, _observationConfirmFillerTradesToday - 1);
        if (execution.CountedAsQualityRescue)
            _observationConfirmQualityRescueTradesToday = Math.Max(0, _observationConfirmQualityRescueTradesToday - 1);
        if (execution.CountedAsWideStopLongExpansion)
            _observationConfirmWideStopLongExpansionTradesToday = Math.Max(0, _observationConfirmWideStopLongExpansionTradesToday - 1);
        execution.DailyCountersRolledBack = true;
    }

    private void IsolateAbnormalSafetyFlattenV182(ReplayExecutionState execution, string reason)
    {
        if (execution.AbnormalSafetyFlattenV182)
            return;

        execution.AbnormalSafetyFlattenV182 = true;
        execution.AbnormalSafetyFlattenReasonV182 = reason;
        RollBackNormalTradeCounters(execution);
        LogExecutionInfo($"EXEC_ABNORMAL_SAFETY_FLATTEN_ISOLATED_V182 trade={execution.TradeId} reason={reason}");
        AppendExecutionEvent(execution, "ABNORMAL_SAFETY_FLATTEN_ISOLATED_V182", "FLATTEN", execution.EntryAvgPrice, execution.EntryFilledQty, reason);
        RaiseShowNotification($"Abnormal safety flatten isolated: {execution.TradeId}. It is excluded from normal trade statistics and capacity; account impact remains logged. Strategy will continue after cleanup.", "OPFStrategyV1");
    }

    private static bool IsExpectedTradeOrder(ReplayExecutionState execution, string role, Order order, out string reason)
    {
        reason = string.Empty;
        var expected = role switch
        {
            "ENTRY" => execution.EntryOrder,
            "SL" => execution.StopOrder,
            "TP" => execution.TargetOrder,
            "BASE_SL" => execution.StopOrder,
            "BASE_TP" => execution.TargetOrder,
            "RUNNER_SL" => execution.RunnerStopOrder,
            "RUNNER_TP" => execution.RunnerTargetOrder,
            "TIME_STOP" => execution.TimeStopOrderV208,
            "FLATTEN" => null,
            ReplayStopExitRole => null,
            _ => null
        };

        if (role is "FLATTEN" or ReplayStopExitRole)
            return true;

        if (role == "RUNNER_SL" && execution.RunnerBreakEvenApplied && order.TriggerPrice == execution.RunnerStop)
            return true;
        if (role == "SL" && execution.ProtectBreakEvenAppliedV186 && order.TriggerPrice == execution.Stop)
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
        if (trade.Order is null)
            return;
        if (!TryParseExecutionComment(trade.Order.Comment, out var tradeId, out var role))
            return;
        var execution = FindReplayExecution(tradeId);
        if (execution is null)
            return;
        if (!IsExpectedTradeOrder(execution, role, trade.Order, out var mismatchReason))
        {
            LogExecutionInfo($"EXEC_TRADE_ORDER_MISMATCH trade={tradeId} role={role} {mismatchReason}");
            AppendExecutionEvent(execution, "TRADE_ORDER_MISMATCH", role, trade.Price, trade.Volume, mismatchReason);
            return;
        }

        var tradeOrder = trade.Order;
        LogExecutionInfo($"EXEC_MYTRADE trade={tradeId} role={role} price={trade.Price:0.########} volume={trade.Volume:0.########} orderExt={tradeOrder.ExtId} orderState={tradeOrder.State} orderType={tradeOrder.Type} orderDir={tradeOrder.Direction} orderPrice={tradeOrder.Price:0.########} orderTrig={tradeOrder.TriggerPrice:0.########} orderUnfilled={tradeOrder.Unfilled:0.########}");
        AppendExecutionEvent(execution, "MYTRADE", role, trade.Price, trade.Volume, $"orderExt={tradeOrder.ExtId}|orderState={tradeOrder.State}|orderType={tradeOrder.Type}|orderDir={tradeOrder.Direction}|orderPrice={tradeOrder.Price:0.########}|orderTrig={tradeOrder.TriggerPrice:0.########}|orderUnfilled={tradeOrder.Unfilled:0.########}");

        if (execution.ExitCompleted && role == "ENTRY")
        {
            var fillQty = Math.Max(0m, trade.Volume);
            var reason = $"LateEntryAfterCompleted:exitRole={execution.ExitRole ?? "-"}";
            LogExecutionInfo($"EXEC_LATE_ENTRY_AFTER_COMPLETED trade={tradeId} price={trade.Price:0.########} volume={fillQty:0.########} reason={reason}");
            AppendExecutionEvent(execution, "LATE_ENTRY_AFTER_COMPLETED", "ENTRY", trade.Price, fillQty, reason);
            await SubmitEmergencyFlattenAsync(execution, fillQty, reason);
            return;
        }

        if (role == "ENTRY")
        {
            var fillQty = Math.Max(0m, trade.Volume);
            if (fillQty > 0m)
            {
                var oldQty = execution.EntryFilledQty;
                var oldRawValue = execution.RawEntryAvgPrice * oldQty;
                execution.EntryFilledQty = oldQty + fillQty;
                execution.RawEntryAvgPrice = (oldRawValue + trade.Price * fillQty) / execution.EntryFilledQty;
                execution.EntryAvgPrice = execution.RawEntryAvgPrice;
            }

            var tolerance = ExecutionFillTolerance();
            var rawEntryDrift = Math.Abs(execution.RawEntryAvgPrice - execution.CreatedPrice);
            var historicalFavorableFill = IsHistoricalReplayTime(_lastResearchCandle?.Time ?? execution.CreatedTime) &&
                rawEntryDrift > tolerance &&
                (execution.Side == TradeSide.Long
                    ? execution.RawEntryAvgPrice < execution.CreatedPrice
                    : execution.RawEntryAvgPrice > execution.CreatedPrice);
            if (historicalFavorableFill)
            {
                execution.EntryAvgPrice = execution.CreatedPrice;
                var reason = $"HistoricalFavorableEntryNormalizedV180:raw={execution.RawEntryAvgPrice:0.########}|planned={execution.CreatedPrice:0.########}|improvement={rawEntryDrift:0.########}|max={tolerance:0.########}";
                LogExecutionInfo($"EXEC_HISTORICAL_FAVORABLE_ENTRY_NORMALIZED_V180 trade={tradeId} {reason}");
                AppendExecutionEvent(execution, "HISTORICAL_FAVORABLE_ENTRY_NORMALIZED_V180", "ENTRY", execution.RawEntryAvgPrice, execution.EntryFilledQty, reason);
                if (!execution.HistoricalFavorableEntryNormalizedV180)
                    RaiseShowNotification($"Historical Replay returned a favorable out-of-range entry for {tradeId}. Strategy accounting is normalized to the planned entry and the position remains active.", "OPFStrategyV1");
                execution.HistoricalFavorableEntryNormalizedV180 = true;
            }

            var entryDrift = Math.Abs(execution.EntryAvgPrice - execution.CreatedPrice);
            if (entryDrift > tolerance)
            {
                var reason = $"EntryFillOutOfRange:fill={execution.EntryAvgPrice:0.########}|planned={execution.CreatedPrice:0.########}|drift={entryDrift:0.########}|max={tolerance:0.########}";
                LogExecutionInfo($"EXEC_ENTRY_FILL_REJECTED trade={tradeId} {reason}");
                AppendExecutionEvent(execution, "ENTRY_FILL_REJECTED", "ENTRY", trade.Price, trade.Volume, reason);
                QuarantineAbnormalEntryV174(execution, reason);
                execution.EmergencyFlattenSubmitted = true;
                var flattenQty = Math.Max(0m, execution.EntryFilledQty - execution.EmergencyFlattenSubmittedQty);
                if (flattenQty > 0m)
                {
                    execution.EmergencyFlattenSubmittedQty += flattenQty;
                    await SubmitEmergencyFlattenAsync(execution, flattenQty, reason);
                }
                return;
            }

            if (execution.EntryAbortPending)
            {
                var flattenQty = Math.Max(0m, execution.EntryFilledQty - execution.EmergencyFlattenSubmittedQty);
                if (flattenQty > 0m)
                {
                    var reason = $"LatePartialEntryFillAfterAbort:filled={execution.EntryFilledQty:0.########}|covered={execution.EmergencyFlattenSubmittedQty:0.########}";
                    execution.EmergencyFlattenSubmittedQty += flattenQty;
                    await SubmitEmergencyFlattenAsync(execution, flattenQty, reason);
                }
                return;
            }

            if (execution.EntryFilledQty + 0.0000001m < execution.Quantity)
            {
                var reason = $"filled={execution.EntryFilledQty:0.########}|requested={execution.Quantity:0.########}|unfilled={tradeOrder.Unfilled:0.########}";
                LogExecutionInfo($"EXEC_ENTRY_PARTIAL_FILL_WAITING trade={tradeId} {reason}");
                AppendExecutionEvent(execution, "ENTRY_PARTIAL_FILL_WAITING", "ENTRY", execution.EntryAvgPrice, execution.EntryFilledQty, reason);
                SchedulePartialEntryFinalizeIfNeeded(execution);
                return;
            }

            if (!execution.BracketSubmitted)
                await SubmitReplayBracketAsync(execution);
            return;
        }

        if (IsProtectiveExitRole(role) || role is "FLATTEN" or "TIME_STOP" or ReplayStopExitRole)
        {
            if (IsProtectiveExitRole(role) && execution.ProtectiveExitCallbackPendingV187)
            {
                execution.ProtectiveExitCallbackPendingV187 = false;
                AppendExecutionEvent(execution, "PROTECTIVE_EXIT_CALLBACK_RESOLVED_V187", role, trade.Price, trade.Volume, $"orderExt={trade.Order.ExtId}");
            }
            var fillQty = Math.Max(0m, trade.Volume);
            var remainingBeforeFill = RemainingExecutionQuantity(execution);
            var acceptedFillQty = Math.Min(fillQty, remainingBeforeFill);
            var overfillQty = Math.Max(0m, fillQty - acceptedFillQty);
            if (execution.ExitCompleted)
            {
                LogExecutionInfo($"EXEC_DUPLICATE_EXIT_FILL trade={tradeId} role={role} price={trade.Price:0.########} volume={fillQty:0.########}");
                var duplicateReason = role is "FLATTEN" or "TIME_STOP" or ReplayStopExitRole
                    ? "duplicateFlattenIgnored"
                    : "duplicateProtectiveExit";
                AppendExecutionEvent(execution, "DUPLICATE_EXIT_FILL", role, trade.Price, fillQty, duplicateReason);
                MarkProtectionCleanupPending(execution, $"DuplicateExit:{role}");
                await CleanupProtectionOrdersAsync(execution, $"DuplicateExit:{role}");
                if (IsProtectiveExitRole(role))
                    await SubmitDuplicateExitFlattenIfNeededAsync(execution, fillQty, role);
                return;
            }

            RefreshActiveProtectionObservationFromCurrentBar(execution);
            if (IsHistoricalReplayProtectiveFillUnreachable(execution, role, trade.Price, out var unreachableReason))
                QuarantineAbnormalProtectiveFillV177(execution, role, trade.Price, unreachableReason);

            var oldExitQty = execution.ExitFilledQty;
            var oldExitValue = execution.ExitAvgPrice * oldExitQty;
            execution.ExitFilledQty += acceptedFillQty;
            if (acceptedFillQty > 0m && execution.ExitFilledQty > 0m)
                execution.ExitAvgPrice = (oldExitValue + trade.Price * acceptedFillQty) / execution.ExitFilledQty;

            if (execution.ZoneBirthSplitRunnerV172)
                AccumulateZoneBirthSplitExitV172(execution, role, trade.Price, acceptedFillQty);

            LogExecutionInfo($"EXEC_EXIT_FILLED trade={tradeId} role={role} price={trade.Price:0.########}");
            AppendExecutionEvent(execution, "EXIT_FILLED", role, trade.Price, trade.Volume, string.Empty);
            if (overfillQty > 0m)
            {
                var reason = $"ExitFillExceedsRemainingV220:fill={fillQty:0.########}|accepted={acceptedFillQty:0.########}|overfill={overfillQty:0.########}|entry={execution.EntryFilledQty:0.########}|logicalExit={execution.ExitFilledQty:0.########}";
                AppendExecutionEvent(execution, "EXIT_FILL_EXCEEDS_REMAINING_V220", role, trade.Price, overfillQty, reason);
                MarkProtectionCleanupPending(execution, reason);
                await CleanupProtectionOrdersAsync(execution, reason);
                SetLiveReadinessBlocked("ExitOverfill", $"Protective exits exceeded the filled entry quantity for {execution.TradeId}. {reason}. Verify and flatten the account manually.");
                NotifyLiveIssue($"ExitOverfill:{execution.TradeId}", $"Protective exits exceeded the entry quantity. trade={execution.TradeId}, role={role}, overfill={overfillQty:0.########}. Trading is blocked pending account verification.");
                return;
            }
            if (execution.AbnormalProtectiveFillQuarantined && RemainingExecutionQuantity(execution) > 0m)
            {
                var reason = $"AbnormalProtectiveFillResidualV220:{role}|remaining={RemainingExecutionQuantity(execution):0.########}";
                MarkProtectionCleanupPending(execution, reason);
                await CleanupProtectionOrdersAsync(execution, reason);
                await SubmitEmergencyFlattenAsync(execution, RemainingExecutionQuantity(execution), reason);
                return;
            }
            if (execution.HistoricalObservedTargetExitRolesV206.Contains(role))
            {
                var expectedTarget = ExpectedExitPrice(execution, role);
                LogExecutionInfo($"EXEC_HISTORICAL_OBSERVED_TP_EXIT_FILLED_V206 trade={tradeId} role={role} raw={trade.Price:0.########} expected={expectedTarget:0.########}");
                AppendExecutionEvent(execution, "HISTORICAL_OBSERVED_TP_EXIT_FILLED_V206", role, trade.Price, trade.Volume, $"expected={expectedTarget:0.########}|normalizedByHistoricalReplayValidation");

                var completedLegStop = role == "BASE_TP"
                    ? execution.StopOrder
                    : role == "RUNNER_TP"
                        ? execution.RunnerStopOrder
                        : null;
                if (IsWorkingExecutionOrder(completedLegStop))
                {
                    var workingLegStop = completedLegStop!;
                    LogExecutionInfo($"EXEC_HISTORICAL_SPLIT_TARGET_LEG_STOP_CANCEL_SENT_V224 trade={tradeId} role={role} stopExt={workingLegStop.ExtId}");
                    AppendExecutionEvent(execution, "HISTORICAL_SPLIT_TARGET_LEG_STOP_CANCEL_SENT_V224", role, workingLegStop.TriggerPrice, workingLegStop.QuantityToFill, $"stopExt={workingLegStop.ExtId}|stopState={workingLegStop.State}");
                    await TryCancelExecutionOrderAsync(workingLegStop, $"HistoricalSplitTargetLeg:{role}");
                }
            }
            if (execution.ZoneBirthSplitRunnerV172 && role == "BASE_TP")
                ScheduleZoneBirthRunnerBreakEvenAfterBaseTargetV173(execution, trade.Price);
            await TryCancelExecutionOrderAsync(execution.EntryOrder, "ExitFilledCancelEntry");

            var completeQty = execution.AbnormalEntryQuarantined ||
                execution.EntryAbortPending ||
                execution.AbnormalSafetyFlattenV182
                ? execution.EntryFilledQty
                : execution.BracketQty > 0m ? execution.BracketQty : execution.Quantity;
            if (execution.ExitFilledQty + 0.0000001m < completeQty)
            {
                if (execution.AbnormalEntryQuarantined && role == "FLATTEN")
                {
                    AppendExecutionEvent(execution, "QUARANTINE_FLATTEN_PARTIAL_WAIT", "FLATTEN", trade.Price, completeQty - execution.ExitFilledQty, "existingFlattenOrderRetained");
                    ScheduleEmergencyFlattenResidualCheck(execution, "PartialFlattenFill");
                }
                return;
            }

            if (execution.EntryAbortPending &&
                (execution.EntryOrder is null || !IsDoneOrder(execution.EntryOrder)))
                return;

            var finalExitPrice = execution.ExitAvgPrice > 0m ? execution.ExitAvgPrice : trade.Price;
            await FinalizeCompletedExecutionExitAsync(execution, finalExitPrice, role);
        }
    }

    private async Task FinalizeCompletedExecutionExitAsync(ReplayExecutionState execution, decimal finalExitPrice, string role)
    {
        var finalRole = role;
        var dailyRole = finalRole;
        ExecutionFillValidation validation;
        if (execution.ZoneBirthSplitRunnerV172)
        {
            var split = FinalizeZoneBirthSplitExitV172(execution);
            finalExitPrice = split.ExitPrice;
            finalRole = split.ExitRole;
            dailyRole = split.DailyRole;
            validation = split.Validation;
        }
        else
        {
            validation = ValidateExecutionFill(execution, finalExitPrice, finalRole);
        }
        if (finalRole.Contains(ReplayStopExitRole, StringComparison.OrdinalIgnoreCase) && IsNormalizedReplayExitFill(validation))
        {
            LogExecutionInfo($"EXEC_HISTORICAL_SESSION_FLATTEN_FILL_NORMALIZED_V184 trade={execution.TradeId} role={finalRole} expected={validation.ExpectedExitPrice:0.########} drift={validation.ExitPriceDriftPoints:0.########} rawDollars={validation.RawDollars:0.##} normalizedDollars={validation.NormalDollars:0.##}");
            AppendExecutionEvent(execution, "HISTORICAL_SESSION_FLATTEN_FILL_NORMALIZED_V184", finalRole, validation.ExpectedExitPrice, execution.ExitFilledQty, $"drift={validation.ExitPriceDriftPoints:0.########}|rawDollars={validation.RawDollars:0.##}|normalizedDollars={validation.NormalDollars:0.##}");
        }
        if (validation.IsAbnormal)
        {
            LogExecutionInfo($"EXEC_ABNORMAL trade={execution.TradeId} role={finalRole} reason={validation.Reason} exit={finalExitPrice:0.########} expected={validation.ExpectedExitPrice:0.########} drift={validation.ExitPriceDriftPoints:0.########}");
            AppendExecutionEvent(execution, "ABNORMAL_EXECUTION", finalRole, finalExitPrice, execution.ExitFilledQty, $"{validation.Reason}|expected={validation.ExpectedExitPrice:0.########}|drift={validation.ExitPriceDriftPoints:0.########}");
            if (!execution.AbnormalEntryQuarantined && !execution.AbnormalProtectiveFillQuarantined)
                IsolateAbnormalSafetyFlattenV182(execution, validation.Reason);
        }
        var isQuarantined = execution.AbnormalEntryQuarantined || execution.AbnormalProtectiveFillQuarantined;
        var isAbnormalSafetyFlatten = execution.AbnormalSafetyFlattenV182 || validation.IsAbnormal;
        var excludedFromNormalStatistics = isQuarantined || isAbnormalSafetyFlatten;
        if (!excludedFromNormalStatistics)
            UpdateReplayExecutionDailyResult(GlobexTradingDayKey(execution.CreatedTime), validation.NormalDollars, dailyRole);
        var accountClassification = isQuarantined ? "Quarantine" : isAbnormalSafetyFlatten ? "Abnormal" : "Normal";
        var replayNormalizedGross = excludedFromNormalStatistics ? 0m : validation.NormalDollars;
        var useRawReplayGross = !isQuarantined && isAbnormalSafetyFlatten;
        var replayRawGrossOverride = execution.ZoneBirthSplitRunnerV172 && IsNormalizedReplayExitFill(validation)
            ? validation.RawDollars
            : (decimal?)null;
        RecordLiveAccountPnl(execution, finalExitPrice, accountClassification, replayNormalizedGross, useRawReplayGross, replayRawGrossOverride);
        var loggedExitPrice = !excludedFromNormalStatistics && (execution.ZoneBirthSplitRunnerV172 || IsNormalizedReplayExitFill(validation))
            ? validation.ExpectedExitPrice
            : finalExitPrice;
        execution.ExitCompleted = true;
        execution.ExitBar = _lastResearchCandle?.Bar;
        execution.ExitPrice = loggedExitPrice;
        execution.ExitRole = finalRole;

        if (isQuarantined)
        {
            if (execution.AbnormalEntryQuarantined)
            {
                LogExecutionInfo($"EXEC_ENTRY_FILL_QUARANTINE_COMPLETE_V174 trade={execution.TradeId} exit={finalExitPrice:0.########} filled={execution.EntryFilledQty:0.########} flattened={execution.ExitFilledQty:0.########}");
                AppendExecutionEvent(execution, "ENTRY_FILL_QUARANTINE_COMPLETE_V174", finalRole, finalExitPrice, execution.ExitFilledQty, $"filled={execution.EntryFilledQty:0.########}|flattened={execution.ExitFilledQty:0.########}");
            }
        }
        else if (isAbnormalSafetyFlatten)
        {
            LogExecutionInfo($"EXEC_ABNORMAL_SAFETY_FLATTEN_COMPLETE_V182 trade={execution.TradeId} exit={finalExitPrice:0.########} filled={execution.EntryFilledQty:0.########} flattened={execution.ExitFilledQty:0.########}");
            AppendExecutionEvent(execution, "ABNORMAL_SAFETY_FLATTEN_COMPLETE_V182", finalRole, finalExitPrice, execution.ExitFilledQty, execution.AbnormalSafetyFlattenReasonV182);
        }
        else
        {
            AppendExecutionTrade(execution, loggedExitPrice, finalRole, validation, dailyRole);
            if (!execution.AbnormalProtectiveFillQuarantined)
                TryWriteActualResearchOutcomeOnExit(execution, finalRole);
        }
        MarkProtectionCleanupPending(execution, $"ExitFilled:{finalRole}");

        if (execution.ProtectionCleanupPending)
            await CleanupProtectionOrdersAsync(execution, $"ExitFilled:{finalRole}");
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

    private void RecordLiveAccountPnl(
        ReplayExecutionState execution,
        decimal exitPrice,
        string classification,
        decimal replayNormalizedGrossDollars,
        bool useRawReplayGross,
        decimal? replayRawGrossOverride)
    {
        if (_snapshot is null || _lastResearchCandle is null || !_writtenLiveAccountPnlTradeIds.Add(execution.TradeId))
            return;

        var quantity = execution.EntryFilledQty > 0m ? execution.EntryFilledQty : execution.Quantity;
        var entryPrice = execution.EntryAvgPrice > 0m ? execution.EntryAvgPrice : execution.CreatedPrice;
        var points = execution.Side == TradeSide.Long ? exitPrice - entryPrice : entryPrice - exitPrice;
        var rawGross = replayRawGrossOverride ?? Math.Round(points * _snapshot.InstrumentProfile.PointValue * quantity, 2);
        var historicalReplay = IsHistoricalReplayTime(_lastResearchCandle.Time);
        var abnormalFlattenReference = execution.EmergencyFlattenReferencePriceV205;
        var normalizeAbnormalFlatten = historicalReplay &&
            string.Equals(classification, "Abnormal", StringComparison.OrdinalIgnoreCase) &&
            abnormalFlattenReference > 0m &&
            Math.Abs(exitPrice - abnormalFlattenReference) > ExecutionFillTolerance();
        var normalizedAbnormalPoints = execution.Side == TradeSide.Long
            ? abnormalFlattenReference - entryPrice
            : entryPrice - abnormalFlattenReference;
        var normalizedAbnormalGross = Math.Round(normalizedAbnormalPoints * _snapshot.InstrumentProfile.PointValue * quantity, 2);
        var gross = historicalReplay
            ? normalizeAbnormalFlatten
                ? normalizedAbnormalGross
                : useRawReplayGross ? rawGross : Math.Round(replayNormalizedGrossDollars, 2)
            : rawGross;
        var pnlSource = historicalReplay
            ? classification == "Quarantine"
                ? "HistoricalReplayQuarantineCommissionOnly"
                : classification == "Abnormal"
                    ? normalizeAbnormalFlatten
                        ? "HistoricalReplayAbnormalSafetyFlattenNormalizedV205"
                        : "HistoricalReplayAbnormalSafetyFlattenActualFill"
                    : useRawReplayGross ? "HistoricalReplayProtectiveFlattenActualFill" : "HistoricalReplayNormalized"
            : "LiveActualFill";
        if (normalizeAbnormalFlatten)
        {
            var drift = Math.Abs(exitPrice - abnormalFlattenReference);
            AppendExecutionEvent(
                execution,
                "HISTORICAL_ABNORMAL_SAFETY_FLATTEN_NORMALIZED_V205",
                "FLATTEN",
                abnormalFlattenReference,
                quantity,
                $"raw={exitPrice:0.########}|reference={abnormalFlattenReference:0.########}|drift={drift:0.########}|rawGross={rawGross:0.##}|normalizedGross={normalizedAbnormalGross:0.##}");
        }
        var commission = Math.Round(quantity * _actualCommissionPerContractRoundTrip, 2);
        var net = gross - commission;
        var tradingDay = GlobexTradingDayKey(execution.CreatedTime);
        RollLiveWeeklyPnlIfNeeded(tradingDay);
        var weeklyGateWasActive = IsWeeklyLongLossGateActive(out _);
        _liveAccountDailyNetPnlDollars += net;
        _liveAccountWeeklyNetPnlDollars += net;
        if (execution.Side == TradeSide.Long)
            _liveLongWeeklyNetPnlDollars += net;
        TriggerWeeklyLongLossGateIfNeeded();

        _researchLogger?.AppendLiveAccountPnl(
            _snapshot.SnapshotId,
            execution.TradeId,
            execution.CreatedTime,
            _lastResearchCandle.Time,
            execution.Side.ToString(),
            classification,
            quantity,
            entryPrice,
            exitPrice,
            gross,
            commission,
            net,
            _liveAccountDailyNetPnlDollars,
            rawGross,
            pnlSource);
        AppendExecutionEvent(execution, "LIVE_ACCOUNT_PNL", classification, exitPrice, quantity, $"gross={gross:0.##}|rawGross={rawGross:0.##}|source={pnlSource}|commission={commission:0.##}|net={net:0.##}|dailyNet={_liveAccountDailyNetPnlDollars:0.##}");

        if (_actualDailyLossLimitDollars > 0m && _liveAccountDailyNetPnlDollars <= -_actualDailyLossLimitDollars)
        {
            NotifyLiveIssue($"DailyLoss:{tradingDay:yyyyMMdd}", $"Trading-day net loss limit reached: {_liveAccountDailyNetPnlDollars:0.##} USD (limit -{_actualDailyLossLimitDollars:0.##}). New Actual entries are blocked for {tradingDay:yyyy-MM-dd}.");
        }
        if (!weeklyGateWasActive && IsWeeklyLongLossGateActive(out var weeklyLongLossReason))
        {
            AppendExecutionEvent(execution, "WEEKLY_LONG_LOSS_GATE_TRIGGERED_V219", "-", exitPrice, quantity, weeklyLongLossReason);
            NotifyLiveIssue(
                $"WeeklyLongLoss:{_liveAccountPnlWeekStart:yyyyMMdd}",
                $"Weekly Long gate triggered for {_liveAccountPnlWeekStart:yyyy-MM-dd}: account net {_liveAccountWeeklyNetPnlDollars:0.##} USD, Long net {_liveLongWeeklyNetPnlDollars:0.##} USD, limit -{_actualWeeklyLongLossLimitDollars:0.##}. Existing positions remain protected; new Long entries are blocked until next week while Short entries remain eligible.");
        }
    }

    private bool IsWeeklyLongLossGateActive(out string reason)
    {
        reason = _weeklyLongLossGateTriggered
            ? $"WeeklyLongLossV219:week={_liveAccountPnlWeekStart:yyyy-MM-dd}|accountNet={_liveAccountWeeklyNetPnlDollars:0.##}|longNet={_liveLongWeeklyNetPnlDollars:0.##}|limit={_actualWeeklyLongLossLimitDollars:0.##}|trigger={_weeklyLongLossGateTrigger}"
            : string.Empty;
        return _weeklyLongLossGateTriggered;
    }

    private void TriggerWeeklyLongLossGateIfNeeded()
    {
        if (_weeklyLongLossGateTriggered || _actualWeeklyLongLossLimitDollars <= 0m)
            return;

        var accountHit = _liveAccountWeeklyNetPnlDollars <= -_actualWeeklyLongLossLimitDollars;
        var longHit = _liveLongWeeklyNetPnlDollars <= -_actualWeeklyLongLossLimitDollars;
        if (!accountHit && !longHit)
            return;

        _weeklyLongLossGateTriggered = true;
        _weeklyLongLossGateTrigger = accountHit && longHit ? "Both" : accountHit ? "Account" : "Long";
    }

    private void RollLiveWeeklyPnlIfNeeded(DateTime tradingDay)
    {
        var weekStart = TradingWeekStart(tradingDay);
        if (_liveAccountPnlWeekStart == weekStart)
            return;

        var previous = _liveAccountPnlWeekStart;
        RestoreLiveWeeklyNetPnl(tradingDay);
        LogLiveReadinessInfo($"LIVE_TRADING_WEEK_ROLLOVER previous={(previous == DateTime.MinValue ? "-" : previous.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))} current={weekStart:yyyy-MM-dd}");
    }

    private void RestoreLiveWeeklyNetPnl(DateTime tradingDay)
    {
        _liveAccountPnlWeekStart = TradingWeekStart(tradingDay);
        _liveAccountWeeklyNetPnlDollars = 0m;
        _liveLongWeeklyNetPnlDollars = 0m;
        _weeklyLongLossGateTriggered = false;
        _weeklyLongLossGateTrigger = string.Empty;
        try
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var directory = Path.Combine(root, "ATAS", "StrategyLogs", "OPFStrategyV1");
            if (Directory.Exists(directory))
            {
                var restoredTradeIds = new HashSet<string>(StringComparer.Ordinal);
                var restoredTrades = new List<(DateTime ExitTime, int Sequence, string Side, decimal Net)>();
                var sequence = 0;
                foreach (var file in Directory.EnumerateFiles(directory, "*_live_account_pnl.csv"))
                {
                    foreach (var line in File.ReadLines(file).Skip(1))
                    {
                        var fields = line.Split(',');
                        var tradeId = fields.Length > 6 ? fields[6].Trim('"') : string.Empty;
                        if (fields.Length < 18 ||
                            string.IsNullOrWhiteSpace(tradeId) ||
                            !restoredTradeIds.Add(tradeId) ||
                            !DateTime.TryParse(fields[8].Trim('"'), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var exitTime) ||
                            TradingWeekStart(GlobexTradingDayKey(exitTime)) != _liveAccountPnlWeekStart ||
                            !decimal.TryParse(fields[16], NumberStyles.Number, CultureInfo.InvariantCulture, out var net))
                        {
                            continue;
                        }

                        restoredTrades.Add((exitTime, sequence++, fields[9].Trim('"'), net));
                    }
                }

                foreach (var trade in restoredTrades.OrderBy(x => x.ExitTime).ThenBy(x => x.Sequence))
                {
                    _liveAccountWeeklyNetPnlDollars += trade.Net;
                    if (string.Equals(trade.Side, "Long", StringComparison.OrdinalIgnoreCase))
                        _liveLongWeeklyNetPnlDollars += trade.Net;
                    TriggerWeeklyLongLossGateIfNeeded();
                }
            }

            _liveAccountWeeklyNetPnlDollars = Math.Round(_liveAccountWeeklyNetPnlDollars, 2);
            _liveLongWeeklyNetPnlDollars = Math.Round(_liveLongWeeklyNetPnlDollars, 2);
            LogLiveReadinessInfo($"LIVE_TRADING_WEEK_NET_RESTORED week={_liveAccountPnlWeekStart:yyyy-MM-dd} accountNet={_liveAccountWeeklyNetPnlDollars:0.##} longNet={_liveLongWeeklyNetPnlDollars:0.##}");
            if (IsWeeklyLongLossGateActive(out _))
            {
                NotifyLiveIssue(
                    $"WeeklyLongLoss:{_liveAccountPnlWeekStart:yyyyMMdd}",
                    $"Restored weekly state blocks new Long entries: account net {_liveAccountWeeklyNetPnlDollars:0.##} USD, Long net {_liveLongWeeklyNetPnlDollars:0.##} USD, limit -{_actualWeeklyLongLossLimitDollars:0.##}. Short entries remain eligible.");
            }
        }
        catch (Exception ex)
        {
            SetLiveReadinessBlocked("WeeklyPnlRestoreFailed", $"Could not restore this week's live account PnL: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static DateTime TradingWeekStart(DateTime tradingDay)
    {
        var daysFromMonday = ((int)tradingDay.DayOfWeek + 6) % 7;
        return tradingDay.Date.AddDays(-daysFromMonday);
    }

    private void RestoreLiveDailyNetPnl(DateTime date)
    {
        _replayExecutionDate = date;
        _replayTradesToday = 0;
        _replayExitsToday = 0;
        _liveAccountPnlDate = date;
        _liveAccountDailyNetPnlDollars = 0m;
        try
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var directory = Path.Combine(root, "ATAS", "StrategyLogs", "OPFStrategyV1");
            if (!Directory.Exists(directory))
                return;

            foreach (var file in Directory.EnumerateFiles(directory, "*_live_account_pnl.csv"))
            {
                foreach (var line in File.ReadLines(file).Skip(1))
                {
                    var fields = line.Split(',');
                    if (fields.Length < 18 ||
                        !DateTime.TryParse(fields[8].Trim('"'), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var exitTime) ||
                        GlobexTradingDayKey(exitTime) != date ||
                        !decimal.TryParse(fields[16], NumberStyles.Number, CultureInfo.InvariantCulture, out var net))
                    {
                        continue;
                    }

                    _liveAccountDailyNetPnlDollars += net;
                    if (string.Equals(fields[10].Trim('"'), "Normal", StringComparison.OrdinalIgnoreCase))
                    {
                        _replayTradesToday++;
                        _replayExitsToday++;
                    }
                }
            }

            _liveAccountDailyNetPnlDollars = Math.Round(_liveAccountDailyNetPnlDollars, 2);
            LogLiveReadinessInfo($"LIVE_TRADING_DAY_NET_RESTORED tradingDay={date:yyyy-MM-dd} net={_liveAccountDailyNetPnlDollars:0.##} normalTrades={_replayTradesToday}");
            if (_actualDailyLossLimitDollars > 0m && _liveAccountDailyNetPnlDollars <= -_actualDailyLossLimitDollars)
                NotifyLiveIssue($"DailyLoss:{date:yyyyMMdd}", $"Restored daily net loss is {_liveAccountDailyNetPnlDollars:0.##} USD, at or below the -{_actualDailyLossLimitDollars:0.##} limit. New Actual entries remain blocked.");
        }
        catch (Exception ex)
        {
            SetLiveReadinessBlocked("DailyPnlRestoreFailed", $"Could not restore today's live account PnL: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void AccumulateZoneBirthSplitExitV172(ReplayExecutionState execution, string role, decimal exitPrice, decimal fillQty)
    {
        if (_snapshot is null || fillQty <= 0m)
            return;

        RecordZoneBirthSplitLegExitV172(execution, role, fillQty);
        var entry = execution.EntryAvgPrice > 0m ? execution.EntryAvgPrice : execution.CreatedPrice;
        var rawPoints = execution.Side == TradeSide.Long ? exitPrice - entry : entry - exitPrice;
        var normalizedExit = exitPrice;
        var expectedExit = ExpectedExitPrice(execution, role);
        var tolerance = ExecutionFillTolerance();
        var entryDrift = Math.Abs(entry - execution.CreatedPrice);

        if (entryDrift > tolerance)
        {
            execution.SplitIsAbnormal = true;
            AppendSplitValidationReasonV172(execution, $"AbnormalEntryFill:drift={entryDrift:0.##},max={tolerance:0.##}");
        }
        else if (execution.AbnormalProtectiveFillQuarantined)
        {
            execution.SplitIsAbnormal = true;
            AppendSplitValidationReasonV172(execution, execution.AbnormalProtectiveFillReason);
        }
        else if ((IsProtectiveExitRole(role) || string.Equals(role, ReplayStopExitRole, StringComparison.OrdinalIgnoreCase)) && expectedExit > 0m)
        {
            var drift = Math.Abs(exitPrice - expectedExit);
            var historicalReplay = _lastResearchCandle is not null && IsHistoricalReplayTime(_lastResearchCandle.Time);
            if (historicalReplay && drift > tolerance)
            {
                normalizedExit = expectedExit;
                AppendSplitValidationReasonV172(execution, $"NormalizedReplayExitFill:{role}:raw={exitPrice:0.########},expected={expectedExit:0.########}");
            }
        }
        else if (IsProtectiveExitRole(role))
        {
            execution.SplitIsAbnormal = true;
            AppendSplitValidationReasonV172(execution, $"AbnormalExitRole:{role}:expectedPriceMissing");
        }

        var normalizedPoints = execution.Side == TradeSide.Long ? normalizedExit - entry : entry - normalizedExit;
        execution.SplitNormalPointsQuantity += normalizedPoints * fillQty;
        execution.SplitRawPointsQuantity += rawPoints * fillQty;
        execution.SplitNormalDollars += Math.Round(normalizedPoints * _snapshot.InstrumentProfile.PointValue * fillQty, 2);
        execution.SplitRawDollars += Math.Round(rawPoints * _snapshot.InstrumentProfile.PointValue * fillQty, 2);
        execution.SplitLoggedExitValue += normalizedExit * fillQty;
        execution.SplitRawExitValue += exitPrice * fillQty;
    }

    private static void RecordZoneBirthSplitLegExitV172(ReplayExecutionState execution, string role, decimal fillQty)
    {
        if (role is "BASE_SL" or "BASE_TP")
        {
            execution.BaseExitFilledQty += fillQty;
            execution.BaseExitRole ??= role == "BASE_TP" ? "TP" : "SL";
            return;
        }

        if (role is "RUNNER_SL" or "RUNNER_TP")
        {
            execution.RunnerExitFilledQty += fillQty;
            execution.RunnerExitRole ??= role == "RUNNER_TP"
                ? "TP"
                : execution.RunnerBreakEvenApplied ? "BE" : "SL";
            return;
        }

        var remaining = fillQty;
        var baseRemaining = Math.Max(0m, ZoneBirthSplitRunnerV172BaseQuantity - execution.BaseExitFilledQty);
        if (baseRemaining > 0m)
        {
            var baseQty = Math.Min(baseRemaining, remaining);
            execution.BaseExitFilledQty += baseQty;
            execution.BaseExitRole ??= role;
            remaining -= baseQty;
        }

        if (remaining > 0m)
        {
            execution.RunnerExitFilledQty += remaining;
            execution.RunnerExitRole ??= role;
        }
    }

    private static void AppendSplitValidationReasonV172(ReplayExecutionState execution, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || execution.SplitValidationReason.Contains(reason, StringComparison.Ordinal))
            return;
        execution.SplitValidationReason = string.IsNullOrEmpty(execution.SplitValidationReason)
            ? reason
            : $"{execution.SplitValidationReason}|{reason}";
    }

    private static SplitExecutionFinalization FinalizeZoneBirthSplitExitV172(ReplayExecutionState execution)
    {
        var quantity = execution.ExitFilledQty > 0m ? execution.ExitFilledQty : execution.BracketQty;
        if (quantity <= 0m)
            quantity = execution.Quantity;

        var normalPoints = quantity <= 0m ? 0m : Math.Round(execution.SplitNormalPointsQuantity / quantity, 4);
        var rawPoints = quantity <= 0m ? 0m : Math.Round(execution.SplitRawPointsQuantity / quantity, 4);
        var normalPointsR = execution.InitialRiskPoints <= 0m ? 0m : Math.Round(normalPoints / execution.InitialRiskPoints, 4);
        var rawPointsR = execution.InitialRiskPoints <= 0m ? 0m : Math.Round(rawPoints / execution.InitialRiskPoints, 4);
        var loggedExit = quantity <= 0m ? execution.ExitAvgPrice : execution.SplitLoggedExitValue / quantity;
        var rawExit = quantity <= 0m ? execution.ExitAvgPrice : execution.SplitRawExitValue / quantity;
        var exitRole = $"SPLIT_BASE_{execution.BaseExitRole ?? "UNKNOWN"}_RUNNER_{execution.RunnerExitRole ?? "UNKNOWN"}";
        var dailyRole = execution.BaseExitRole == "TP" && execution.RunnerExitRole == "TP"
            ? "TP"
            : execution.BaseExitRole == "SL" && execution.RunnerExitRole == "SL"
                ? "SL"
                : exitRole;
        var validation = new ExecutionFillValidation(
            execution.SplitIsAbnormal,
            execution.SplitValidationReason,
            loggedExit,
            Math.Abs(rawExit - loggedExit),
            execution.SplitIsAbnormal ? 0m : execution.SplitNormalDollars,
            execution.SplitIsAbnormal ? 0m : normalPoints,
            execution.SplitIsAbnormal ? 0m : normalPointsR,
            rawPoints,
            execution.SplitRawDollars,
            rawPointsR);
        return new SplitExecutionFinalization(loggedExit, exitRole, dailyRole, validation);
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

        if (execution.AbnormalProtectiveFillQuarantined)
            reasons.Add(execution.AbnormalProtectiveFillReason);

        var entryDrift = Math.Abs(entry - execution.CreatedPrice);
        if (entryDrift > tolerance)
            reasons.Add($"AbnormalEntryFill:drift={entryDrift:0.##},max={tolerance:0.##}");

        if (string.Equals(exitRole, "TP", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(exitRole, "SL", StringComparison.OrdinalIgnoreCase))
        {
            if (drift > tolerance)
                reasons.Add($"Abnormal{exitRole}Fill:drift={drift:0.##},max={tolerance:0.##}");
        }
        else if (!string.Equals(exitRole, ReplayStopExitRole, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(exitRole, "TIME_STOP", StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add($"AbnormalExitRole:{exitRole}");
        }

        var entryIsAbnormal = entryDrift > tolerance;
        var isExitRoleNormal = string.Equals(exitRole, "TP", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(exitRole, "SL", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(exitRole, "TIME_STOP", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(exitRole, ReplayStopExitRole, StringComparison.OrdinalIgnoreCase);
        var canNormalizeReplayExit = !entryIsAbnormal &&
            !execution.AbnormalProtectiveFillQuarantined &&
            isExitRoleNormal &&
            expectedExit > 0m &&
            drift > tolerance &&
            _lastResearchCandle is not null &&
            IsHistoricalReplayTime(_lastResearchCandle.Time);
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
        var preserveProtectiveFlattenPnl = !entryIsAbnormal &&
            string.Equals(exitRole, "FLATTEN", StringComparison.OrdinalIgnoreCase);
        return new ExecutionFillValidation(
            isAbnormal,
            string.Join("|", reasons),
            expectedExit,
            drift,
            isAbnormal && !preserveProtectiveFlattenPnl ? 0m : rawDollars,
            isAbnormal && !preserveProtectiveFlattenPnl ? 0m : rawPoints,
            isAbnormal && !preserveProtectiveFlattenPnl ? 0m : rawPointsR,
            rawPoints,
            rawDollars,
            rawPointsR);
    }

    private static decimal ExpectedExitPrice(ReplayExecutionState execution, string exitRole)
    {
        if (exitRole == "RUNNER_TP")
            return execution.RunnerTarget;
        if (IsTargetExitRole(exitRole))
            return execution.Target;
        if (exitRole is "SL" or "BASE_SL")
            return execution.Stop;
        if (exitRole == "RUNNER_SL")
            return execution.RunnerStop > 0m ? execution.RunnerStop : execution.Stop;
        if (string.Equals(exitRole, ReplayStopExitRole, StringComparison.OrdinalIgnoreCase))
            return execution.GlobexCloseoutReferencePriceV184;
        if (string.Equals(exitRole, "TIME_STOP", StringComparison.OrdinalIgnoreCase))
            return execution.TimeStopReferencePriceV209;
        return 0m;
    }

    private static bool IsNormalizedReplayExitFill(ExecutionFillValidation validation)
    {
        return validation.Reason.Contains("NormalizedReplayExitFill", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsHistoricalReplayProtectiveFillUnreachable(ReplayExecutionState execution, string role, decimal fillPrice, out string reason)
    {
        reason = string.Empty;
        if (_lastResearchCandle is null ||
            !IsHistoricalReplayTime(_lastResearchCandle.Time) ||
            !IsProtectiveExitRole(role))
        {
            return false;
        }

        var expectedExit = ExpectedExitPrice(execution, role);
        var tolerance = ExecutionFillTolerance();
        var drift = expectedExit > 0m ? Math.Abs(fillPrice - expectedExit) : 0m;
        if (expectedExit <= 0m || drift <= tolerance || execution.WasProtectivePriceObserved(role, expectedExit, tolerance))
            return false;

        reason = $"ReplayProtectiveFillUnreachable:{role}:raw={fillPrice:0.########}|expected={expectedExit:0.########}|observedLow={execution.ProtectiveObservedLow:0.########}|observedHigh={execution.ProtectiveObservedHigh:0.########}|drift={drift:0.########}|max={tolerance:0.########}";
        return true;
    }

    private decimal CurrentExecutionMarketReference(decimal fallback)
    {
        if (_latestCalculatedMarketPrice > 0m)
            return _latestCalculatedMarketPrice;
        if (_lastResearchCandle is not null && _lastResearchCandle.Close > 0m)
            return _lastResearchCandle.Close;
        return fallback;
    }

    private (decimal Bid, decimal Ask) ExecutionQuoteSnapshot()
    {
        try
        {
            return (BestBid?.Price ?? 0m, BestAsk?.Price ?? 0m);
        }
        catch
        {
            return (0m, 0m);
        }
    }

    private bool TryValidateExecutionQuotePair(decimal reference, out string reason)
    {
        var quote = ExecutionQuoteSnapshot();
        var tolerance = ExecutionFillTolerance();
        if (reference <= 0m || quote.Bid <= 0m || quote.Ask <= 0m)
        {
            reason = $"QuoteMissing:reference={reference:0.########}|bid={quote.Bid:0.########}|ask={quote.Ask:0.########}";
            return false;
        }

        var bidDrift = Math.Abs(quote.Bid - reference);
        var askDrift = Math.Abs(quote.Ask - reference);
        var spread = quote.Ask - quote.Bid;
        if (spread < 0m || spread > tolerance || bidDrift > tolerance || askDrift > tolerance)
        {
            reason = $"QuoteOutOfSync:reference={reference:0.########}|bid={quote.Bid:0.########}|ask={quote.Ask:0.########}|bidDrift={bidDrift:0.########}|askDrift={askDrift:0.########}|spread={spread:0.########}|max={tolerance:0.########}";
            return false;
        }

        reason = $"QuoteSynchronized:reference={reference:0.########}|bid={quote.Bid:0.########}|ask={quote.Ask:0.########}|spread={spread:0.########}";
        return true;
    }

    private void UpdateActiveProtectionObservation(decimal marketPrice)
    {
        if (marketPrice <= 0m)
            return;

        foreach (var execution in ActiveReplayExecutions())
        {
            execution.ObserveProtectivePrice(marketPrice);
            execution.UpdateActualExcursion(marketPrice);
        }
    }

    private void RefreshActiveProtectionObservationFromCurrentBar(ReplayExecutionState execution)
    {
        if (execution.ExitCompleted || _lastSeenBar < 0)
            return;

        var candle = GetCandle(_lastSeenBar);
        if (candle is not null)
            execution.ObserveProtectiveRange(candle.Low, candle.High);
    }

    private void ScheduleHistoricalReplayAccountFlatReconciliationIfNeeded(OpfCandle candle)
    {
        if (!IsHistoricalReplayTime(candle.Time) || GetCurrentAccountPosition() != 0m)
            return;

        foreach (var execution in ActiveReplayExecutions())
            ScheduleHistoricalReplayAccountFlatReconciliationIfNeeded(execution, candle);
    }

    private void ScheduleHistoricalReplayAccountFlatReconciliationIfNeeded(ReplayExecutionState execution, OpfCandle candle)
    {
        if (execution.AccountFlatReconciliationPending ||
            execution.EntryFilledQty <= execution.ExitFilledQty ||
            CountWorkingProtectionOrders(execution) > 0)
        {
            return;
        }

        execution.AccountFlatReconciliationPending = true;
        EnqueueExecutionAction("HistoricalReplayAccountFlatReconcile", async () =>
        {
            await Task.Delay(250);
            if (execution.ExitCompleted || GetCurrentAccountPosition() != 0m || CountWorkingProtectionOrders(execution) > 0)
            {
                execution.AccountFlatReconciliationPending = false;
                return;
            }

            if (execution.ZoneBirthSplitRunnerV172)
            {
                RecoverHistoricalReplaySplitLegIfFilled(execution, "BASE_TP", execution.TargetOrder, execution.TargetOrder?.Price ?? 0m);
                RecoverHistoricalReplaySplitLegIfFilled(execution, "BASE_SL", execution.StopOrder, execution.StopOrder?.TriggerPrice ?? 0m);
                RecoverHistoricalReplaySplitLegIfFilled(execution, "RUNNER_TP", execution.RunnerTargetOrder, execution.RunnerTargetOrder?.Price ?? 0m);
                RecoverHistoricalReplaySplitLegIfFilled(execution, "RUNNER_SL", execution.RunnerStopOrder, execution.RunnerStopOrder?.TriggerPrice ?? 0m);
            }

            var completeQty = execution.AbnormalEntryQuarantined ||
                execution.EntryAbortPending ||
                execution.AbnormalSafetyFlattenV182
                ? execution.EntryFilledQty
                : execution.BracketQty > 0m ? execution.BracketQty : execution.Quantity;
            if (execution.ExitFilledQty + 0.0000001m >= completeQty)
            {
                var finalExitPrice = execution.ExitAvgPrice > 0m ? execution.ExitAvgPrice : execution.EntryAvgPrice;
                LogExecutionInfo($"EXEC_ACCOUNT_FLAT_EXIT_RECONCILED trade={execution.TradeId} filled={execution.ExitFilledQty:0.########}|required={completeQty:0.########}");
                AppendExecutionEvent(execution, "ACCOUNT_FLAT_EXIT_RECONCILED", "-", finalExitPrice, execution.ExitFilledQty, $"filled={execution.ExitFilledQty:0.########}|required={completeQty:0.########}");
                NotifyLiveIssue($"AccountFlatExitReconciled:{execution.TradeId}", $"Historical Replay exit callbacks were incomplete for {execution.TradeId}. Filled protection orders were reconciled and the strategy will continue.");
                await FinalizeCompletedExecutionExitAsync(execution, finalExitPrice, "ACCOUNT_FLAT_RECONCILED");
                execution.AccountFlatReconciliationPending = false;
                return;
            }

            var reason = $"filled={execution.ExitFilledQty:0.########}|required={completeQty:0.########}|working=0|position=0";
            LogExecutionInfo($"EXEC_ACCOUNT_FLAT_EXIT_RECONCILE_UNRESOLVED trade={execution.TradeId} {reason}");
            AppendExecutionEvent(execution, "ACCOUNT_FLAT_EXIT_RECONCILE_UNRESOLVED", "-", 0m, completeQty - execution.ExitFilledQty, reason);
            NotifyLiveIssue($"AccountFlatExitUnresolved:{execution.TradeId}", $"Account is flat but exit callbacks could not be reconstructed for {execution.TradeId}. The trade is excluded from statistics and the strategy will continue. {reason}");
            execution.ExitCompleted = true;
            execution.ExitBar = _lastResearchCandle?.Bar;
            execution.ExitRole = "ACCOUNT_FLAT_RECONCILED_UNKNOWN";
            execution.AccountFlatReconciliationPending = false;
            MarkProtectionCleanupPending(execution, "AccountFlatReconcileUnresolved");
            if (execution.ProtectionCleanupPending)
                await CleanupProtectionOrdersAsync(execution, "AccountFlatReconcileUnresolved");
        });
    }

    private void RecoverHistoricalReplaySplitLegIfFilled(ReplayExecutionState execution, string role, Order? order, decimal exitPrice)
    {
        if (!IsFilledExecutionOrder(order) || exitPrice <= 0m)
            return;

        var legAlreadyFilled = role.StartsWith("BASE_", StringComparison.Ordinal)
            ? execution.BaseExitFilledQty
            : execution.RunnerExitFilledQty;
        var requiredQty = role.StartsWith("BASE_", StringComparison.Ordinal)
            ? ZoneBirthSplitRunnerV172BaseQuantity
            : ZoneBirthSplitRunnerV172RunnerQuantity;
        var missingQty = Math.Max(0m, requiredQty - legAlreadyFilled);
        missingQty = Math.Min(missingQty, Math.Max(0m, execution.EntryFilledQty - execution.ExitFilledQty));
        if (missingQty <= 0m)
            return;

        var oldExitQty = execution.ExitFilledQty;
        var oldExitValue = execution.ExitAvgPrice * oldExitQty;
        execution.ExitFilledQty += missingQty;
        execution.ExitAvgPrice = (oldExitValue + exitPrice * missingQty) / execution.ExitFilledQty;
        AccumulateZoneBirthSplitExitV172(execution, role, exitPrice, missingQty);
        LogExecutionInfo($"EXEC_ACCOUNT_FLAT_EXIT_LEG_RECOVERED trade={execution.TradeId} role={role} price={exitPrice:0.########} qty={missingQty:0.########} ext={order!.ExtId}");
        AppendExecutionEvent(execution, "ACCOUNT_FLAT_EXIT_LEG_RECOVERED", role, exitPrice, missingQty, $"ext={order!.ExtId}|state={order.State}|unfilled={order.Unfilled:0.########}");
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

    private async Task<bool> OpenProtectionOrderAsync(ReplayExecutionState execution, Order order, string role)
    {
        try
        {
            await OpenOrderAsync(order);
            return true;
        }
        catch (Exception ex)
        {
            var message = $"role={role}|err={ex.GetType().Name}:{ex.Message}";
            LogExecutionInfo($"EXEC_PROTECTION_SUBMIT_FAIL trade={execution.TradeId} {message}");
            AppendExecutionEvent(execution, "PROTECTION_SUBMIT_FAIL", role, order.Price > 0m ? order.Price : order.TriggerPrice, order.QuantityToFill, message);
            NotifyLiveIssue($"ProtectionSubmitFail:{execution.TradeId}:{role}", $"Protection order submission failed for {execution.TradeId}. role={role}. Emergency flatten is being submitted.");
            await SubmitEmergencyFlattenAsync(execution, RemainingExecutionQuantity(execution), $"ProtectionSubmitFailed:{role}");
            return false;
        }
    }

    private async Task SubmitReplayBracketAsync(ReplayExecutionState execution)
    {
        var qty = execution.EntryFilledQty > 0m ? execution.EntryFilledQty : execution.Quantity;
        if (qty <= 0m)
            return;

        var reference = CurrentExecutionMarketReference(execution.EntryAvgPrice > 0m ? execution.EntryAvgPrice : execution.CreatedPrice);
        var tolerance = ExecutionFillTolerance();
        var entry = execution.EntryAvgPrice > 0m ? execution.EntryAvgPrice : execution.CreatedPrice;
        var barLag = Math.Max(0, _lastSeenBar - (execution.CreatedBar + 1));
        var entryDrift = Math.Abs(reference - entry);
        var quoteValid = TryValidateExecutionQuotePair(reference, out var quoteReason);
        if (barLag > 0 || entryDrift > tolerance || !quoteValid)
        {
            var reason = $"ProtectionQuotePreflightFailed:barLag={barLag}|reference={reference:0.########}|entry={entry:0.########}|drift={entryDrift:0.########}|max={tolerance:0.########}|{quoteReason}";
            QuarantineProtectionSetupV178(execution, reference, reason);
            execution.EmergencyFlattenSubmitted = true;
            await SubmitEmergencyFlattenAsync(execution, qty, reason);
            return;
        }

        var quote = ExecutionQuoteSnapshot();
        LogExecutionInfo($"EXEC_PROTECTION_QUOTE_PREFLIGHT_OK trade={execution.TradeId} reference={reference:0.########} bid={quote.Bid:0.########} ask={quote.Ask:0.########} barLag={barLag}");
        AppendExecutionEvent(execution, "PROTECTION_QUOTE_PREFLIGHT_OK_V178", "-", reference, qty, $"bid={quote.Bid:0.########}|ask={quote.Ask:0.########}|barLag={barLag}");

        RepriceExecutionBracketFromFill(execution);
        if (string.Equals(execution.Lane, "Secondary", StringComparison.Ordinal))
        {
            var pointValue = _snapshot!.InstrumentProfile.PointValue;
            var existingRisk = ActiveReplayExecutions()
                .Where(other => !ReferenceEquals(other, execution))
                .Sum(other => other.InitialRiskPoints * pointValue * RemainingExecutionQuantity(other));
            var combinedRisk = existingRisk + execution.InitialRiskPoints * pointValue * qty;
            if (combinedRisk > 300m)
            {
                var reason = $"SecondaryActualRiskCapExceededV215:combined={combinedRisk:0.##}|max=300|existing={existingRisk:0.##}|secondary={execution.InitialRiskPoints * pointValue * qty:0.##}";
                LogExecutionInfo($"EXEC_SECONDARY_ACTUAL_RISK_CAP_EXCEEDED_V215 trade={execution.TradeId} {reason}");
                AppendExecutionEvent(execution, "SECONDARY_ACTUAL_RISK_CAP_EXCEEDED_V215", "ENTRY", execution.EntryAvgPrice, qty, reason);
                IsolateAbnormalSafetyFlattenV182(execution, reason);
                execution.EmergencyFlattenSubmitted = true;
                await SubmitEmergencyFlattenAsync(execution, qty, reason);
                return;
            }
        }
        if (IsStrictEntryExcludedRiskV208(execution.InitialRiskPoints))
        {
            var reason = $"EntryFilledStrictRiskBandExcludedV208:risk={execution.InitialRiskPoints:0.##}|excluded=({StrictEntryExcludedRiskMinExclusiveV208:0.##},{StrictEntryExcludedRiskMaxInclusiveV208:0.##}]|planned={execution.PlannedRiskPoints:0.##}";
            LogExecutionInfo($"EXEC_ENTRY_FILLED_STRICT_RISK_BAND_EXCLUDED_V208 trade={execution.TradeId} {reason}");
            AppendExecutionEvent(execution, "ENTRY_FILLED_STRICT_RISK_BAND_EXCLUDED_V208", "ENTRY", execution.EntryAvgPrice, qty, reason);
            IsolateAbnormalSafetyFlattenV182(execution, reason);
            execution.EmergencyFlattenSubmitted = true;
            await SubmitEmergencyFlattenAsync(execution, qty, reason);
            return;
        }
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
                IsolateAbnormalSafetyFlattenV182(execution, reason);
                execution.EmergencyFlattenSubmitted = true;
                await SubmitEmergencyFlattenAsync(execution, qty, reason);
                return;
            }
        }

        execution.StartProtectionObservation(execution.EntryAvgPrice > 0m ? execution.EntryAvgPrice : execution.CreatedPrice);
        if (!execution.IsDeferredContinuation && UsesZoneBirthSplitRunnerV172(execution.Side, execution.ResearchPath))
        {
            if (qty == ZoneBirthSplitRunnerV172TotalQuantity)
            {
                await SubmitZoneBirthSplitBracketV172Async(execution);
                return;
            }

            LogExecutionInfo($"EXEC_ZONEBIRTH_SPLIT_V173_FALLBACK trade={execution.TradeId} filledQty={qty:0.########} required={ZoneBirthSplitRunnerV172TotalQuantity:0.########}");
            AppendExecutionEvent(execution, "ZONEBIRTH_SPLIT_V173_FALLBACK", "-", execution.EntryAvgPrice, qty, $"required={ZoneBirthSplitRunnerV172TotalQuantity:0.########}");
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
        if (!await OpenProtectionOrderAsync(execution, sl, "SL"))
            return;
        LogExecutionInfo($"EXEC_SL_SENT trade={execution.TradeId} stop={execution.Stop:0.########} qty={qty}");
        AppendExecutionEvent(execution, "SL_SENT", "SL", execution.Stop, qty, execution.OcoGroup ?? string.Empty);
        if (IsDoneOrder(sl))
        {
            LogExecutionInfo($"EXEC_TP_SUPPRESSED_AFTER_IMMEDIATE_SL trade={execution.TradeId} state={sl.State} unfilled={sl.Unfilled:0.########}");
            AppendExecutionEvent(execution, "TP_SUPPRESSED", "TP", execution.Target, qty, $"slState={sl.State}|slUnfilled={sl.Unfilled:0.########}");
            execution.BracketSubmitted = true;
            ScheduleProtectionLossCheckIfNeeded("BracketSubmittedAfterImmediateSL");
            TryActivatePendingProtectedSecondaryV216(execution);
            return;
        }
        if (IsHistoricalReplayTime(_lastResearchCandle?.Time ?? execution.CreatedTime))
        {
            var intendedTarget = execution.Target;
            RegisterHistoricalVirtualTargetV220(execution, "TP", intendedTarget, tp);
            execution.BracketSubmitted = true;
            ScheduleProtectionLossCheckIfNeeded("BracketSubmittedHistoricalVirtualTargetV220");
            TryActivatePendingProtectedSecondaryV216(execution);
            return;
        }
        if (!await OpenProtectionOrderAsync(execution, tp, "TP"))
            return;
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
        TryActivatePendingProtectedSecondaryV216(execution);
    }

    private async Task SubmitZoneBirthSplitBracketV172Async(ReplayExecutionState execution)
    {
        execution.ZoneBirthSplitRunnerV172 = true;
        execution.OcoGroup = $"OPF-{execution.TradeId}-BASE";
        execution.RunnerOcoGroup = $"OPF-{execution.TradeId}-RUNNER";
        execution.RunnerStop = execution.Stop;
        var entry = execution.EntryAvgPrice > 0m ? execution.EntryAvgPrice : execution.CreatedPrice;
        execution.RunnerTarget = AlignToTick(
            TargetFromRisk(execution.Side, entry, execution.InitialRiskPoints, ZoneBirthSplitRunnerV173TargetR),
            _snapshot!.InstrumentProfile.TickSize);
        execution.BracketQty = ZoneBirthSplitRunnerV172TotalQuantity;

        if (!await SubmitZoneBirthSplitLegV172Async(execution, false))
            return;
        if (!await SubmitZoneBirthSplitLegV172Async(execution, true))
            return;

        execution.BracketSubmitted = true;
        LogExecutionInfo($"EXEC_ZONEBIRTH_SPLIT_V173_READY trade={execution.TradeId} baseOco={execution.OcoGroup} runnerOco={execution.RunnerOcoGroup} stop={execution.Stop:0.########} baseTarget={execution.Target:0.########} runnerTarget={execution.RunnerTarget:0.########}");
        AppendExecutionEvent(execution, "ZONEBIRTH_SPLIT_V173_READY", "-", execution.EntryAvgPrice, execution.BracketQty, $"baseOco={execution.OcoGroup}|runnerOco={execution.RunnerOcoGroup}|stop={execution.Stop:0.########}|baseTarget={execution.Target:0.########}|runnerTarget={execution.RunnerTarget:0.########}");
        ScheduleProtectionLossCheckIfNeeded("SplitBracketSubmittedV173");
        TryActivatePendingProtectedSecondaryV216(execution);
    }

    private async Task<bool> SubmitZoneBirthSplitLegV172Async(ReplayExecutionState execution, bool runner)
    {
        var exitDirection = execution.Side == TradeSide.Long ? OrderDirections.Sell : OrderDirections.Buy;
        var prefix = runner ? "RUNNER" : "BASE";
        var oco = runner ? execution.RunnerOcoGroup : execution.OcoGroup;
        var stopRole = $"{prefix}_SL";
        var targetRole = $"{prefix}_TP";
        var targetPrice = runner ? execution.RunnerTarget : execution.Target;
        var legQuantity = runner ? ZoneBirthSplitRunnerV172RunnerQuantity : ZoneBirthSplitRunnerV172BaseQuantity;
        var stop = new Order
        {
            Portfolio = Portfolio,
            Security = Security,
            Type = OrderTypes.Stop,
            Direction = exitDirection,
            TriggerPriceType = ReplayStopTriggerPriceType,
            TriggerPrice = execution.Stop,
            QuantityToFill = legQuantity,
            TimeInForce = ReplayTimeInForce,
            OCOGroup = oco,
            Comment = $"OPF|{execution.TradeId}|{stopRole}|{execution.SignalId}",
            AutoCancel = false
        };
        var target = new Order
        {
            Portfolio = Portfolio,
            Security = Security,
            Type = OrderTypes.Limit,
            Direction = exitDirection,
            Price = targetPrice,
            QuantityToFill = legQuantity,
            TimeInForce = ReplayTimeInForce,
            OCOGroup = oco,
            Comment = $"OPF|{execution.TradeId}|{targetRole}|{execution.SignalId}",
            AutoCancel = false
        };

        if (runner)
        {
            execution.RunnerStopOrder = stop;
            execution.RunnerTargetOrder = target;
        }
        else
        {
            execution.StopOrder = stop;
            execution.TargetOrder = target;
        }

        if (!await OpenProtectionOrderAsync(execution, stop, stopRole))
            return false;
        LogExecutionInfo($"EXEC_{stopRole}_SENT trade={execution.TradeId} stop={execution.Stop:0.########} qty={legQuantity:0.########}");
        AppendExecutionEvent(execution, $"{stopRole}_SENT", stopRole, execution.Stop, legQuantity, oco ?? string.Empty);
        if (IsDoneOrder(stop))
        {
            LogExecutionInfo($"EXEC_{targetRole}_SUPPRESSED trade={execution.TradeId} stopState={stop.State}");
            AppendExecutionEvent(execution, $"{targetRole}_SUPPRESSED", targetRole, targetPrice, legQuantity, $"stopState={stop.State}");
            return true;
        }

        if (IsHistoricalReplayTime(_lastResearchCandle?.Time ?? execution.CreatedTime))
        {
            RegisterHistoricalVirtualTargetV220(execution, targetRole, targetPrice, target);
            return true;
        }

        if (!await OpenProtectionOrderAsync(execution, target, targetRole))
            return false;
        LogExecutionInfo($"EXEC_{targetRole}_SENT trade={execution.TradeId} target={targetPrice:0.########} qty={legQuantity:0.########}");
        AppendExecutionEvent(execution, $"{targetRole}_SENT", targetRole, targetPrice, legQuantity, oco ?? string.Empty);
        if (IsDoneOrder(target))
            await TryCancelExecutionOrderAsync(stop, $"Immediate{targetRole}");
        return true;
    }

    private void RegisterHistoricalVirtualTargetV220(ReplayExecutionState execution, string role, decimal intendedTarget, Order order)
    {
        if (!execution.HistoricalVirtualTargetRolesV220.Add(role))
            return;

        LogExecutionInfo($"EXEC_HISTORICAL_VIRTUAL_TP_ARMED_V220 trade={execution.TradeId} role={role} intended={intendedTarget:0.########} qty={order.QuantityToFill:0.########}");
        AppendExecutionEvent(execution, $"{role}_ARMED_V220", role, intendedTarget, order.QuantityToFill, "HistoricalVirtualTargetV220|noRestingLimit");
        AppendExecutionEvent(execution, "HISTORICAL_VIRTUAL_TP_ARMED_V220", role, intendedTarget, order.QuantityToFill, "activateMarketExitAfterObservedTargetTouch|noRestingLimit");
    }

    private void ScheduleHistoricalVirtualTargetsV220()
    {
        foreach (var execution in ActiveReplayExecutions())
            ScheduleHistoricalVirtualTargetsV220(execution);
    }

    private void ScheduleHistoricalVirtualTargetsV220(ReplayExecutionState execution)
    {
        if (execution.ExitCompleted || !execution.BracketSubmitted ||
            execution.HistoricalVirtualTargetRolesV220.Count == 0 ||
            !IsHistoricalReplayTime(_lastResearchCandle?.Time ?? execution.CreatedTime))
        {
            return;
        }

        foreach (var role in execution.HistoricalVirtualTargetRolesV220.ToArray())
        {
            if (execution.HistoricalVirtualTargetActivationPendingV220.Contains(role))
                continue;

            var targetPrice = ExpectedExitPrice(execution, role);
            if (!HistoricalTargetObservedV180(execution, role, targetPrice))
                continue;

            execution.HistoricalVirtualTargetActivationPendingV220.Add(role);
            EnqueueExecutionAction($"HistoricalVirtualTargetActivateV220:{role}", async () =>
            {
                try
                {
                    if (execution.ExitCompleted || !execution.HistoricalVirtualTargetRolesV220.Contains(role))
                        return;

                    var intendedTarget = ExpectedExitPrice(execution, role);
                    if (execution.ProtectiveExitCallbackPendingV187)
                    {
                        LogExecutionInfo($"EXEC_HISTORICAL_OBSERVED_TP_EXIT_DEFERRED_V207 trade={execution.TradeId} role={role} target={intendedTarget:0.########} reason=ProtectiveExitCallbackPending");
                        AppendExecutionEvent(execution, "HISTORICAL_OBSERVED_TP_EXIT_DEFERRED_V207", role, intendedTarget, TargetOrderForRoleV180(execution, role)?.QuantityToFill ?? 0m, "ProtectiveExitCallbackPendingV187");
                        return;
                    }

                    var currentCandle = _lastResearchCandle;
                    var sameLegStop = role == "RUNNER_TP" ? execution.RunnerStop : execution.Stop;
                    var sameBarStopTouched = currentCandle is not null && sameLegStop > 0m &&
                        (execution.Side == TradeSide.Long
                            ? currentCandle.Low <= sameLegStop
                            : currentCandle.High >= sameLegStop);
                    if (sameBarStopTouched)
                    {
                        execution.HistoricalVirtualTargetRolesV220.Remove(role);
                        LogExecutionInfo($"EXEC_HISTORICAL_OBSERVED_TP_EXIT_SUPPRESSED_STOP_TOUCH_V207 trade={execution.TradeId} role={role} target={intendedTarget:0.########} stop={sameLegStop:0.########} low={currentCandle!.Low:0.########} high={currentCandle.High:0.########}");
                        AppendExecutionEvent(execution, "HISTORICAL_OBSERVED_TP_EXIT_SUPPRESSED_STOP_TOUCH_V207", role, intendedTarget, TargetOrderForRoleV180(execution, role)?.QuantityToFill ?? 0m, $"stop={sameLegStop:0.########}|low={currentCandle.Low:0.########}|high={currentCandle.High:0.########}|StopFirst");
                        return;
                    }

                    var target = TargetOrderForRoleV180(execution, role);
                    var exitQuantity = RemainingTargetRoleQuantityV220(execution, role);
                    if (target is null || exitQuantity <= 0m)
                    {
                        execution.HistoricalVirtualTargetRolesV220.Remove(role);
                        return;
                    }

                    var observedTargetExit = new Order
                    {
                        Portfolio = Portfolio,
                        Security = Security,
                        Type = OrderTypes.Market,
                        Direction = execution.Side == TradeSide.Long ? OrderDirections.Sell : OrderDirections.Buy,
                        QuantityToFill = exitQuantity,
                        TimeInForce = ReplayTimeInForce,
                        OCOGroup = role == "RUNNER_TP" ? execution.RunnerOcoGroup : execution.OcoGroup,
                        Comment = target.Comment,
                        AutoCancel = false
                    };
                    LogExecutionInfo($"EXEC_HISTORICAL_OBSERVED_TP_EXIT_SEND_V220 trade={execution.TradeId} role={role} target={intendedTarget:0.########} qty={observedTargetExit.QuantityToFill:0.########}");
                    AppendExecutionEvent(execution, "HISTORICAL_OBSERVED_TP_EXIT_SEND_V220", role, intendedTarget, observedTargetExit.QuantityToFill, "ObservedTargetTouch|orderType=Market|noRestingLimit");
                    SetTargetOrderForRoleV181(execution, role, observedTargetExit);
                    execution.HistoricalObservedTargetExitRolesV206.Add(role);
                    execution.HistoricalVirtualTargetRolesV220.Remove(role);
                    if (!await OpenProtectionOrderAsync(execution, observedTargetExit, role))
                        return;
                    LogExecutionInfo($"EXEC_HISTORICAL_OBSERVED_TP_EXIT_SENT_V220 trade={execution.TradeId} role={role} target={intendedTarget:0.########} ext={observedTargetExit.ExtId}");
                    AppendExecutionEvent(execution, "HISTORICAL_OBSERVED_TP_EXIT_SENT_V220", role, intendedTarget, observedTargetExit.QuantityToFill, $"ext={observedTargetExit.ExtId}|ObservedTargetTouch|orderType=Market|noRestingLimit");
                }
                catch (Exception ex)
                {
                    var intendedTarget = ExpectedExitPrice(execution, role);
                    LogExecutionInfo($"EXEC_HISTORICAL_OBSERVED_TP_EXIT_FAILED_V206 trade={execution.TradeId} role={role} target={intendedTarget:0.########} err={ex.GetType().Name}:{ex.Message}");
                    AppendExecutionEvent(execution, "HISTORICAL_OBSERVED_TP_EXIT_FAILED_V206", role, intendedTarget, TargetOrderForRoleV180(execution, role)?.QuantityToFill ?? 0m, $"{ex.GetType().Name}:{ex.Message}");
                    NotifyLiveIssue($"ObservedTargetExit:{execution.TradeId}:{role}", $"Historical Replay observed target exit failed. trade={execution.TradeId}, role={role}, error={ex.Message}");
                }
                finally
                {
                    execution.HistoricalVirtualTargetActivationPendingV220.Remove(role);
                }
            });
        }
    }

    private static decimal RemainingTargetRoleQuantityV220(ReplayExecutionState execution, string role)
    {
        return role switch
        {
            "BASE_TP" => Math.Max(0m, ZoneBirthSplitRunnerV172BaseQuantity - execution.BaseExitFilledQty),
            "RUNNER_TP" => Math.Max(0m, ZoneBirthSplitRunnerV172RunnerQuantity - execution.RunnerExitFilledQty),
            _ => RemainingExecutionQuantity(execution)
        };
    }

    private static bool HistoricalTargetObservedV180(ReplayExecutionState execution, string role, decimal targetPrice)
    {
        if (targetPrice <= 0m || !IsTargetExitRole(role) || !execution.ProtectionObservationStarted)
            return false;

        return execution.Side == TradeSide.Long
            ? execution.ProtectiveObservedHigh >= targetPrice
            : execution.ProtectiveObservedLow <= targetPrice;
    }

    private static Order? TargetOrderForRoleV180(ReplayExecutionState execution, string role)
    {
        return role switch
        {
            "TP" or "BASE_TP" => execution.TargetOrder,
            "RUNNER_TP" => execution.RunnerTargetOrder,
            _ => null
        };
    }

    private static void SetTargetOrderForRoleV181(ReplayExecutionState execution, string role, Order order)
    {
        if (role is "TP" or "BASE_TP")
            execution.TargetOrder = order;
        else if (role == "RUNNER_TP")
            execution.RunnerTargetOrder = order;
    }

    private static bool IsEntryFillRiskDriftAccepted(ReplayExecutionState execution)
    {
        return IsEntryRiskDriftAccepted(execution, execution.InitialRiskPoints);
    }

    private static bool IsEntryRiskDriftAccepted(ReplayExecutionState execution, decimal risk)
    {
        if (IsBreakawayPath(execution.ResearchPath))
            return false;
        if (IsObservationConfirmPath(execution.ResearchPath) && execution.Side == TradeSide.Long)
            return false;
        return execution.PlannedRiskPoints <= execution.MaxAllowedRiskPoints &&
            risk <= execution.MaxAllowedRiskPoints + EntryFillRiskDriftTolerancePoints;
    }

    private decimal EstimatedRepricedRiskPointsV216(ReplayExecutionState execution, decimal entry)
    {
        if (_snapshot is null || entry <= 0m)
            return execution.PlannedRiskPoints;

        var minRisk = Math.Max(_snapshot.InstrumentProfile.MinStopPoints, _snapshot.InstrumentProfile.TickSize);
        var stop = execution.Stop;
        if (execution.Side == TradeSide.Long)
        {
            if (stop >= entry || entry - stop < minRisk)
                stop = entry - minRisk;
        }
        else if (stop <= entry || stop - entry < minRisk)
        {
            stop = entry + minRisk;
        }

        stop = AlignToTick(stop, _snapshot.InstrumentProfile.TickSize);
        return Math.Abs(entry - stop);
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
        execution.TargetR = ActualTargetRFor(execution.Side, execution.ResearchPath, execution.PlannedRiskPoints);
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
            NotifyLiveIssue($"ExecutionActionFail:{tag}", $"Execution action failed. action={tag}, error={ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _executionLock.Release();
        }
    }

    private void ScheduleProtectionCleanupIfNeeded(string reason)
    {
        foreach (var execution in _replayExecutionsByTradeId.Values.Where(NeedsProtectionCleanup).ToArray())
        {
            EnqueueExecutionAction($"ProtectionCleanup:{execution.TradeId}:{reason}", async () =>
            {
                if (NeedsProtectionCleanup(execution))
                    await CleanupProtectionOrdersAsync(execution, reason);
            });
        }
    }

    private void ScheduleZoneBirthRunnerBreakEvenAfterBaseTargetV173(ReplayExecutionState execution, decimal baseTargetFillPrice)
    {
        if (_snapshot is null || !execution.ZoneBirthSplitRunnerV172 || execution.ExitCompleted ||
            execution.RunnerBreakEvenEvaluated || !execution.BracketSubmitted || execution.BaseExitRole != "TP" ||
            IsZoneBirthRunnerLegExitedV172(execution) || !IsWorkingExecutionOrder(execution.RunnerStopOrder))
        {
            return;
        }

        execution.RunnerBreakEvenEvaluated = true;
        execution.RunnerBreakEvenTriggerBar = _lastResearchCandle?.Bar;
        var entry = execution.EntryAvgPrice > 0m ? execution.EntryAvgPrice : execution.CreatedPrice;
        var breakEvenStop = AlignToTick(entry, _snapshot.InstrumentProfile.TickSize);
        var marketPrice = _lastResearchCandle?.Close ?? baseTargetFillPrice;
        AppendExecutionEvent(execution, "ZONEBIRTH_RUNNER_V173_BASE_TP_TRIGGERED", "RUNNER_SL", breakEvenStop, ZoneBirthSplitRunnerV172RunnerQuantity, $"bar={execution.RunnerBreakEvenTriggerBar}|baseTargetFill={baseTargetFillPrice:0.########}|market={marketPrice:0.########}");

        var tick = _snapshot.InstrumentProfile.TickSize;
        var marketValid = execution.Side == TradeSide.Long
            ? marketPrice - breakEvenStop >= tick
            : breakEvenStop - marketPrice >= tick;
        if (!marketValid)
        {
            LogExecutionInfo($"EXEC_ZONEBIRTH_RUNNER_V173_BE_SKIPPED trade={execution.TradeId} market={marketPrice:0.########} stop={breakEvenStop:0.########}");
            AppendExecutionEvent(execution, "ZONEBIRTH_RUNNER_V173_BE_SKIPPED_MARKET_CROSSED", "RUNNER_SL", breakEvenStop, ZoneBirthSplitRunnerV172RunnerQuantity, $"market={marketPrice:0.########}|tick={tick:0.########}");
            return;
        }

        if (execution.Side == TradeSide.Long ? breakEvenStop <= execution.RunnerStop : breakEvenStop >= execution.RunnerStop)
        {
            AppendExecutionEvent(execution, "ZONEBIRTH_RUNNER_V173_BE_NOOP", "RUNNER_SL", breakEvenStop, ZoneBirthSplitRunnerV172RunnerQuantity, $"current={execution.RunnerStop:0.########}");
            return;
        }

        execution.RunnerBreakEvenPending = true;
        execution.RunnerPendingStop = breakEvenStop;
        EnqueueExecutionAction("ZoneBirthRunnerBreakEvenV173", async () =>
        {
            if (execution.ExitCompleted || !execution.RunnerBreakEvenPending ||
                IsZoneBirthRunnerLegExitedV172(execution) || !IsWorkingExecutionOrder(execution.RunnerStopOrder))
            {
                execution.RunnerBreakEvenPending = false;
                return;
            }

            var oldStop = execution.RunnerStopOrder!;
            var newStop = oldStop.Clone();
            newStop.TriggerPrice = execution.RunnerPendingStop;
            newStop.TriggerPriceType = ReplayStopTriggerPriceType;
            LogExecutionInfo($"EXEC_ZONEBIRTH_RUNNER_V173_BE_MODIFY_SEND trade={execution.TradeId} old={oldStop.TriggerPrice:0.########} new={newStop.TriggerPrice:0.########} triggerBar={execution.RunnerBreakEvenTriggerBar}");
            AppendExecutionEvent(execution, "ZONEBIRTH_RUNNER_V173_BE_MODIFY_SEND", "RUNNER_SL", newStop.TriggerPrice, ZoneBirthSplitRunnerV172RunnerQuantity, $"old={oldStop.TriggerPrice:0.########}|triggerBar={execution.RunnerBreakEvenTriggerBar}");
            try
            {
                await ModifyOrderAsync(oldStop, newStop);
                TryMarkZoneBirthRunnerBreakEvenV173Applied(execution, newStop, "ModifyOrderAsync");
            }
            catch (Exception ex)
            {
                execution.RunnerBreakEvenPending = false;
                LogExecutionInfo($"EXEC_ZONEBIRTH_RUNNER_V173_BE_MODIFY_FAIL trade={execution.TradeId} err={ex.GetType().Name}:{ex.Message}");
                AppendExecutionEvent(execution, "ZONEBIRTH_RUNNER_V173_BE_MODIFY_FAIL", "RUNNER_SL", breakEvenStop, ZoneBirthSplitRunnerV172RunnerQuantity, $"{ex.GetType().Name}:{ex.Message}");
                throw;
            }
        });
    }

    private void ScheduleProtectBreakEvenV186(decimal marketPrice)
    {
        foreach (var execution in ActiveReplayExecutions())
            ScheduleProtectBreakEvenV186(execution, marketPrice);
    }

    private void ScheduleProtectBreakEvenV186(ReplayExecutionState execution, decimal marketPrice)
    {
        var currentBar = Math.Max(_lastSeenBar, _lastResearchCandle?.Bar ?? -1);
        if (_snapshot is null || execution.ExitCompleted || execution.ZoneBirthSplitRunnerV172 ||
            (!execution.IsDeferredContinuation &&
             (!UsesProtectBreakEvenV186(execution.Side, execution.ResearchPath) ||
              IsStaticDynamicExitV215(execution.Side, execution.ResearchPath, execution.PlannedRiskPoints))) || !execution.BracketSubmitted ||
            execution.ProtectBreakEvenEvaluatedV186 || execution.ProtectBreakEvenPendingV186 ||
            execution.ProtectBreakEvenAppliedV186 || execution.ProtectBreakEvenLastMarketCrossedBarV187 == currentBar ||
            !IsWorkingExecutionOrder(execution.StopOrder))
        {
            return;
        }

        var entry = execution.EntryAvgPrice > 0m ? execution.EntryAvgPrice : execution.CreatedPrice;
        var triggerR = ProtectBreakEvenTriggerRFor(execution);
        var triggerPrice = execution.Side == TradeSide.Long
            ? entry + execution.InitialRiskPoints * triggerR
            : entry - execution.InitialRiskPoints * triggerR;
        var triggerObserved = execution.ProtectionObservationStarted &&
            (execution.Side == TradeSide.Long
                ? execution.ProtectiveObservedHigh >= triggerPrice
                : execution.ProtectiveObservedLow <= triggerPrice);
        if (!triggerObserved)
            return;

        if (RequiresNextBarBreakEvenV209(execution) && !execution.ProtectBreakEvenTriggerBarV186.HasValue)
        {
            execution.ProtectBreakEvenTriggerBarV186 = currentBar;
            AppendExecutionEvent(execution, "PROTECT_BE_ARMED_NEXT_BAR_V209", "SL", entry, execution.BracketQty, $"triggerR={triggerR:0.##}|trigger={triggerPrice:0.########}|triggerBar={currentBar}");
            return;
        }
        if (RequiresNextBarBreakEvenV209(execution) && execution.ProtectBreakEvenTriggerBarV186.HasValue &&
            currentBar <= execution.ProtectBreakEvenTriggerBarV186.Value)
            return;

        execution.ProtectBreakEvenTriggerBarV186 ??= currentBar;
        var breakEvenStop = AlignToTick(entry, _snapshot.InstrumentProfile.TickSize);
        var reference = CurrentExecutionMarketReference(marketPrice);
        AppendExecutionEvent(execution, ProtectBreakEvenEventName(execution, "TRIGGERED"), "SL", breakEvenStop, execution.BracketQty, $"triggerR={triggerR:0.##}|trigger={triggerPrice:0.########}|reference={reference:0.########}|bar={execution.ProtectBreakEvenTriggerBarV186}");

        var tick = _snapshot.InstrumentProfile.TickSize;
        var marketValid = execution.Side == TradeSide.Long
            ? reference - breakEvenStop >= tick
            : breakEvenStop - reference >= tick;
        if (!marketValid)
        {
            execution.ProtectBreakEvenLastMarketCrossedBarV187 = currentBar;
            AppendExecutionEvent(execution, ProtectBreakEvenEventName(execution, "SKIPPED_MARKET_CROSSED"), "SL", breakEvenStop, execution.BracketQty, $"reference={reference:0.########}|tick={tick:0.########}");
            return;
        }

        execution.ProtectBreakEvenEvaluatedV186 = true;
        if (execution.Side == TradeSide.Long ? breakEvenStop <= execution.Stop : breakEvenStop >= execution.Stop)
        {
            AppendExecutionEvent(execution, ProtectBreakEvenEventName(execution, "NOOP"), "SL", breakEvenStop, execution.BracketQty, $"current={execution.Stop:0.########}");
            return;
        }

        execution.ProtectBreakEvenPendingV186 = true;
        execution.ProtectPendingStopV186 = breakEvenStop;
        EnqueueExecutionAction("ProtectBE1RV186", async () =>
        {
            if (execution.ExitCompleted || !execution.ProtectBreakEvenPendingV186 || !IsWorkingExecutionOrder(execution.StopOrder))
            {
                execution.ProtectBreakEvenPendingV186 = false;
                return;
            }

            var oldStop = execution.StopOrder!;
            var newStop = oldStop.Clone();
            newStop.TriggerPrice = execution.ProtectPendingStopV186;
            newStop.TriggerPriceType = ReplayStopTriggerPriceType;
            var modifySendEvent = ProtectBreakEvenEventName(execution, "MODIFY_SEND");
            LogExecutionInfo($"EXEC_{modifySendEvent} trade={execution.TradeId} old={oldStop.TriggerPrice:0.########} new={newStop.TriggerPrice:0.########} triggerBar={execution.ProtectBreakEvenTriggerBarV186}");
            AppendExecutionEvent(execution, modifySendEvent, "SL", newStop.TriggerPrice, execution.BracketQty, $"old={oldStop.TriggerPrice:0.########}|triggerBar={execution.ProtectBreakEvenTriggerBarV186}");
            try
            {
                await ModifyOrderAsync(oldStop, newStop);
                TryMarkProtectBreakEvenV186Applied(execution, newStop, "ModifyOrderAsync");
            }
            catch (Exception ex)
            {
                execution.ProtectBreakEvenPendingV186 = false;
                var modifyFailedEvent = ProtectBreakEvenEventName(execution, "MODIFY_FAILED");
                LogExecutionInfo($"EXEC_{modifyFailedEvent} trade={execution.TradeId} err={ex.GetType().Name}:{ex.Message}");
                AppendExecutionEvent(execution, modifyFailedEvent, "SL", breakEvenStop, execution.BracketQty, $"{ex.GetType().Name}:{ex.Message}");
                throw;
            }
        });
    }

    private static decimal ProtectBreakEvenTriggerRFor(ReplayExecutionState execution)
    {
        if (TryGetActualExitPolicyV209(execution.Side, execution.ResearchPath, out _, out var triggerR, out _) && triggerR > 0m)
            return triggerR;
        return ProtectBreakEvenTriggerRV186;
    }

    private static bool RequiresNextBarBreakEvenV209(ReplayExecutionState execution)
    {
        return execution.IsDeferredContinuation ||
            (execution.Side == TradeSide.Short &&
             string.Equals(execution.ResearchPath, "BreakawayFvg", StringComparison.OrdinalIgnoreCase));
    }

    private static string ProtectBreakEvenEventName(ReplayExecutionState execution, string action)
    {
        return execution.Side == TradeSide.Short &&
               string.Equals(execution.ResearchPath, "BreakawayFvg", StringComparison.OrdinalIgnoreCase)
            ? $"PROTECT_BE1_5R_{action}_V209"
            : $"PROTECT_BE1R_{action}_V186";
    }

    private void TryMarkProtectBreakEvenV186Applied(ReplayExecutionState execution, Order order, string source)
    {
        if (_snapshot is null || !execution.ProtectBreakEvenPendingV186 || execution.ProtectBreakEvenAppliedV186)
            return;
        if (Math.Abs(order.TriggerPrice - execution.ProtectPendingStopV186) > _snapshot.InstrumentProfile.TickSize / 2m)
            return;

        execution.Stop = execution.ProtectPendingStopV186;
        execution.StopOrder = order;
        execution.ProtectBreakEvenPendingV186 = false;
        execution.ProtectBreakEvenAppliedV186 = true;
        var appliedEvent = ProtectBreakEvenEventName(execution, "APPLIED");
        LogExecutionInfo($"EXEC_{appliedEvent} trade={execution.TradeId} stop={execution.Stop:0.########} src={source}");
        AppendExecutionEvent(execution, appliedEvent, "SL", execution.Stop, execution.BracketQty, $"src={source}|triggerBar={execution.ProtectBreakEvenTriggerBarV186}");
    }

    private void TryMarkZoneBirthRunnerBreakEvenV173Applied(ReplayExecutionState execution, Order order, string source)
    {
        if (_snapshot is null || !execution.RunnerBreakEvenPending || execution.RunnerBreakEvenApplied)
            return;
        if (Math.Abs(order.TriggerPrice - execution.RunnerPendingStop) > _snapshot.InstrumentProfile.TickSize / 2m)
            return;

        execution.RunnerStop = execution.RunnerPendingStop;
        execution.RunnerStopOrder = order;
        execution.RunnerBreakEvenPending = false;
        execution.RunnerBreakEvenApplied = true;
        LogExecutionInfo($"EXEC_ZONEBIRTH_RUNNER_V173_BE_APPLIED trade={execution.TradeId} stop={execution.RunnerStop:0.########} src={source}");
        AppendExecutionEvent(execution, "ZONEBIRTH_RUNNER_V173_BE_APPLIED", "RUNNER_SL", execution.RunnerStop, ZoneBirthSplitRunnerV172RunnerQuantity, $"src={source}|triggerBar={execution.RunnerBreakEvenTriggerBar}");
    }

    private void SchedulePolicyTimeStopV209(OpfCandle candle)
    {
        foreach (var execution in ActiveReplayExecutions())
            SchedulePolicyTimeStopV209(execution, candle);
    }

    private void SchedulePolicyTimeStopV209(ReplayExecutionState execution, OpfCandle candle)
    {
        if (execution.IsDeferredContinuation || execution.ZoneBirthSplitRunnerV172 || execution.ExitCompleted ||
            execution.TimeStopExitPendingV208 || !execution.BracketSubmitted || execution.EntryFilledQty <= 0m ||
            !TryGetActualExitPolicyV209(execution.Side, execution.ResearchPath, out _, out _, out var maxBars, execution.PlannedRiskPoints) ||
            candle.Bar - execution.CreatedBar + 1 < maxBars ||
            IsPolicyProtectiveExitTouchedV209(execution, candle))
        {
            return;
        }

        execution.TimeStopExitPendingV208 = true;
        execution.TimeStopReferencePriceV209 = candle.Close;
        AppendExecutionEvent(execution, "POLICY_TIMESTOP_TRIGGERED_V209", "TIME_STOP", candle.Close, RemainingExecutionQuantity(execution), $"bars={candle.Bar - execution.CreatedBar + 1}|max={maxBars}|path={execution.ResearchPath}");
        EnqueueExecutionAction("PolicyTimeStopV209", async () =>
        {
            if (execution.ExitCompleted)
                return;

            await TryCancelExecutionOrderAsync(execution.StopOrder, "PolicyTimeStopV209");
            await TryCancelExecutionOrderAsync(execution.TargetOrder, "PolicyTimeStopV209");
            if (execution.ExitCompleted)
                return;

            var position = GetCurrentAccountPosition();
            var qty = Math.Min(Math.Abs(position), RemainingExecutionQuantity(execution));
            if (qty <= 0m || Portfolio is null || Security is null)
            {
                execution.TimeStopExitPendingV208 = false;
                AppendExecutionEvent(execution, "POLICY_TIMESTOP_SUPPRESSED_V209", "TIME_STOP", candle.Close, 0m, $"position={position:0.########}|contextReady={Portfolio is not null && Security is not null}");
                return;
            }

            var order = new Order
            {
                Portfolio = Portfolio,
                Security = Security,
                Type = OrderTypes.Market,
                Direction = execution.Side == TradeSide.Long ? OrderDirections.Sell : OrderDirections.Buy,
                QuantityToFill = qty,
                TimeInForce = ReplayTimeInForce,
                Comment = $"OPF|{execution.TradeId}|TIME_STOP|Policy{maxBars}BarsV209",
                AutoCancel = false
            };
            execution.TimeStopOrderV208 = order;
            AppendExecutionEvent(execution, "POLICY_TIMESTOP_SEND_V209", "TIME_STOP", candle.Close, qty, $"bars={candle.Bar - execution.CreatedBar + 1}|max={maxBars}");
            await OpenOrderAsync(order);
        });
    }

    private static bool IsPolicyProtectiveExitTouchedV209(ReplayExecutionState execution, OpfCandle candle)
    {
        return execution.Side == TradeSide.Long
            ? candle.Low <= execution.Stop || candle.High >= execution.Target
            : candle.High >= execution.Stop || candle.Low <= execution.Target;
    }

    private void ScheduleStaleUnfilledEntryAbortIfNeeded(OpfCandle candle)
    {
        foreach (var execution in ActiveReplayExecutions())
            ScheduleStaleUnfilledEntryAbortIfNeeded(execution, candle);
    }

    private void ScheduleStaleUnfilledEntryAbortIfNeeded(ReplayExecutionState execution, OpfCandle candle)
    {
        if (!IsStaleUnfilledEntry(execution, candle.Bar))
            return;

        execution.EntryAbortPending = true;
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
            if (!IsRegisteredReplayExecution(execution) ||
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
            IsolateAbnormalSafetyFlattenV182(execution, reason);
            await TryCancelExecutionOrderAsync(execution.EntryOrder, "PartialEntryFillAbort");

            execution.BracketQty = filledQty;
            execution.EmergencyFlattenSubmitted = true;
            execution.EmergencyFlattenSubmittedQty += filledQty;
            await SubmitEmergencyFlattenAsync(execution, filledQty, reason);
        });
    }

    private void ScheduleGlobexCloseoutIfNeeded(OpfCandle candle)
    {
        if (!ActualOrdersEnabled ||
            !IsGlobexCloseoutLockWindow(candle.Time, out var closeoutReason))
        {
            return;
        }

        foreach (var execution in ActiveReplayExecutions())
            ScheduleGlobexCloseoutIfNeeded(execution, candle, closeoutReason);
    }

    private void ScheduleGlobexCloseoutIfNeeded(ReplayExecutionState execution, OpfCandle candle, string closeoutReason)
    {
        if (
            execution.ExitCompleted ||
            execution.ReplayStopExitPending)
        {
            return;
        }

        execution.ReplayStopExitPending = true;
        EnqueueExecutionAction("GlobexCloseout", async () =>
        {
            if (!IsRegisteredReplayExecution(execution) || execution.ExitCompleted)
                return;

            var reason = $"{closeoutReason}|remaining={RemainingExecutionQuantity(execution):0.########}";
            LogExecutionInfo($"EXEC_GLOBEX_CLOSEOUT_PENDING trade={execution.TradeId} {reason}");
            AppendExecutionEvent(execution, "GLOBEX_CLOSEOUT_PENDING", ReplayStopExitRole, candle.Close, RemainingExecutionQuantity(execution), reason);

            await TryCancelExecutionOrderAsync(execution.EntryOrder, "GlobexCloseout:Entry");
            await TryCancelExecutionOrderAsync(execution.StopOrder, "GlobexCloseout:SL");
            await TryCancelExecutionOrderAsync(execution.TargetOrder, "GlobexCloseout:TP");
            await TryCancelExecutionOrderAsync(execution.RunnerStopOrder, "GlobexCloseout:RunnerSL");
            await TryCancelExecutionOrderAsync(execution.RunnerTargetOrder, "GlobexCloseout:RunnerTP");

            if (execution.ExitCompleted)
                return;

            var position = GetCurrentAccountPosition();
            if (position == 0m)
            {
                execution.ReplayStopExitPending = false;
                if (execution.EntryFilledQty <= 0m)
                {
                    execution.ExitCompleted = true;
                    execution.ExitBar = candle.Bar;
                    execution.ExitPrice = execution.CreatedPrice;
                    execution.ExitRole = "GLOBEX_CLOSEOUT_NO_FILL";
                    MarkProtectionCleanupPending(execution, "GlobexCloseoutNoFill");
                }
                LogExecutionInfo($"EXEC_GLOBEX_CLOSEOUT_POSITION_FLAT trade={execution.TradeId} {reason}");
                AppendExecutionEvent(execution, "GLOBEX_CLOSEOUT_POSITION_FLAT", ReplayStopExitRole, candle.Close, 0m, reason);
                return;
            }

            if (Portfolio is null || Security is null)
            {
                execution.ReplayStopExitPending = false;
                return;
            }

            var qty = Math.Min(Math.Abs(position), RemainingExecutionQuantity(execution));
            var direction = execution.Side == TradeSide.Long ? OrderDirections.Sell : OrderDirections.Buy;
            if (qty <= 0m)
            {
                execution.ReplayStopExitPending = false;
                return;
            }
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

            execution.GlobexCloseoutReferencePriceV184 = candle.Close;
            LogExecutionInfo($"EXEC_GLOBEX_CLOSEOUT_FLATTEN_SEND trade={execution.TradeId} dir={direction} qty={qty:0.########} reference={execution.GlobexCloseoutReferencePriceV184:0.########} {reason}");
            AppendExecutionEvent(execution, "GLOBEX_CLOSEOUT_FLATTEN_SEND", ReplayStopExitRole, candle.Close, qty, $"dir={direction}|{reason}");
            await OpenOrderAsync(order);
            ScheduleGlobexCloseoutConfirmationV216(execution, candle);
        });
    }

    private void ScheduleGlobexCloseoutConfirmationV216(ReplayExecutionState execution, OpfCandle candle)
    {
        _ = DelayGlobexCloseoutConfirmationV216(execution, candle);
    }

    private async Task DelayGlobexCloseoutConfirmationV216(ReplayExecutionState execution, OpfCandle candle)
    {
        await Task.Delay(5000);
        EnqueueExecutionAction($"GlobexCloseoutConfirmV216:{execution.TradeId}", async () =>
        {
            if (!IsRegisteredReplayExecution(execution) || execution.ExitCompleted)
                return;

            var remainingPosition = GetCurrentAccountPosition();
            if (remainingPosition == 0m)
            {
                var pendingReason = $"trade={execution.TradeId}|position=0|executionRemaining={RemainingExecutionQuantity(execution):0.########}|exitCallback=pending";
                LogExecutionInfo($"EXEC_GLOBEX_CLOSEOUT_ACCOUNT_FLAT_CALLBACK_PENDING_V216 {pendingReason}");
                AppendExecutionEvent(execution, "GLOBEX_CLOSEOUT_ACCOUNT_FLAT_CALLBACK_PENDING_V216", ReplayStopExitRole, candle.Close, 0m, pendingReason);
                if (IsHistoricalReplayTime(candle.Time))
                    await ReconcileHistoricalGlobexCloseoutV216(execution, candle);
                else
                    SetLiveReadinessBlocked("GlobexCloseoutCallbackMissing", $"Globex closeout flattened the account but no fill callback was received for {execution.TradeId}. Verify account PnL and working orders manually.");
                return;
            }

            var unconfirmedReason = $"trade={execution.TradeId}|position={remainingPosition:0.########}|executionRemaining={RemainingExecutionQuantity(execution):0.########}|exitCallback=false";
            LogExecutionInfo($"EXEC_GLOBEX_CLOSEOUT_UNCONFIRMED {unconfirmedReason}");
            AppendExecutionEvent(execution, "GLOBEX_CLOSEOUT_UNCONFIRMED", ReplayStopExitRole, candle.Close, Math.Abs(remainingPosition), unconfirmedReason);
            SetLiveReadinessBlocked("GlobexCloseoutUnconfirmed", $"Globex closeout was not confirmed for {execution.TradeId}. Account position={remainingPosition:0.########}. Verify the account and working orders manually.");
        });
    }

    private async Task ReconcileHistoricalGlobexCloseoutV216(ReplayExecutionState execution, OpfCandle candle)
    {
        if (execution.ExitCompleted || GetCurrentAccountPosition() != 0m)
            return;

        var missingQty = RemainingExecutionQuantity(execution);
        var closeoutOrder = execution.EmergencyFlattenOrder;
        if (missingQty <= 0m || !IsFilledExecutionOrder(closeoutOrder))
        {
            var reason = $"trade={execution.TradeId}|remaining={missingQty:0.########}|orderState={closeoutOrder?.State}|orderUnfilled={closeoutOrder?.Unfilled:0.########}";
            LogExecutionInfo($"EXEC_GLOBEX_CLOSEOUT_RECONCILE_UNRESOLVED_V216 {reason}");
            AppendExecutionEvent(execution, "GLOBEX_CLOSEOUT_RECONCILE_UNRESOLVED_V216", ReplayStopExitRole, candle.Close, missingQty, reason);
            SetLiveReadinessBlocked("GlobexCloseoutReconcileUnresolved", $"Historical Replay account is flat but the Globex closeout order could not be reconciled for {execution.TradeId}.");
            return;
        }

        var reference = execution.GlobexCloseoutReferencePriceV184 > 0m
            ? execution.GlobexCloseoutReferencePriceV184
            : candle.Close;
        var oldExitQty = execution.ExitFilledQty;
        var oldExitValue = execution.ExitAvgPrice * oldExitQty;
        execution.ExitFilledQty += missingQty;
        execution.ExitAvgPrice = (oldExitValue + reference * missingQty) / execution.ExitFilledQty;
        LogExecutionInfo($"EXEC_GLOBEX_CLOSEOUT_RECONCILED_V216 trade={execution.TradeId} reference={reference:0.########} qty={missingQty:0.########}");
        AppendExecutionEvent(execution, "GLOBEX_CLOSEOUT_RECONCILED_V216", ReplayStopExitRole, reference, missingQty, $"orderExt={closeoutOrder!.ExtId}|state={closeoutOrder.State}");
        await FinalizeCompletedExecutionExitAsync(execution, execution.ExitAvgPrice, ReplayStopExitRole);
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
        if (!ActualOrdersEnabled || Portfolio is null || Security is null)
            return;

        var position = GetCurrentAccountPosition();
        if (position == 0m)
        {
            _orphanPositionFlattenPending = false;
            return;
        }

        if (ActiveReplayExecutions().Count > 0)
            return;

        if (_orphanPositionFlattenPending)
            return;

        _orphanPositionFlattenPending = true;
        _orphanPositionFlattenLastBar = candle.Bar;
        var reason = $"pos={position:0.########}|bar={candle.Bar}|manualInterventionRequired";
        LogExecutionInfo($"EXEC_ORPHAN_POSITION_BLOCKED {reason}");
        AppendStandaloneExecutionEvent("ORPHAN_POSITION_BLOCKED", "-", candle, candle.Close, Math.Abs(position), reason);
        SetLiveReadinessBlocked("UnmanagedAccountState", $"Unmanaged position detected ({position:0.########}). OPF will not flatten it automatically; cancel stale orders/flatten manually, then restart or reconnect the strategy.");
    }

    private void ScheduleProtectionLossCheckIfNeeded(string reason)
    {
        foreach (var execution in ActiveReplayExecutions().Where(IsUnprotectedOpenExecution))
        {
            EnqueueExecutionAction($"ProtectionLoss:{execution.TradeId}:{reason}", async () =>
            {
                await Task.Delay(250);
                if (!IsUnprotectedOpenExecution(execution) || GetCurrentAccountPosition() == 0m)
                    return;

                execution.EmergencyFlattenSubmitted = true;
                var remainingQty = RemainingExecutionQuantity(execution);
                LogExecutionInfo($"EXEC_PROTECTION_LOST_BEFORE_EXIT trade={execution.TradeId} reason={reason} remaining={remainingQty:0.########}");
                AppendExecutionEvent(execution, "PROTECTION_LOST_BEFORE_EXIT", "-", 0m, remainingQty, reason);
                NotifyLiveIssue($"ProtectionLost:{execution.TradeId}", $"Stop protection was lost for {execution.TradeId}. An idempotent emergency flatten is being submitted.");
                await SubmitEmergencyFlattenAsync(execution, remainingQty, $"ProtectionLost:{reason}");
            });
        }
    }

    private static bool NeedsProtectionCleanup(ReplayExecutionState? execution)
    {
        return execution is not null && execution.ProtectionCleanupPending && !execution.ProtectionCleanupDone && !execution.ProtectionCleanupInProgress;
    }

    private static bool IsUnprotectedOpenExecution(ReplayExecutionState? execution)
    {
        if (execution is null || !execution.BracketSubmitted || execution.ExitCompleted ||
            execution.ReplayStopExitPending || execution.EmergencyFlattenSubmitted ||
            execution.TimeStopExitPendingV208 ||
            execution.ProtectiveExitCallbackPendingV187 ||
            RemainingExecutionQuantity(execution) <= 0m)
        {
            return false;
        }

        if (!execution.ZoneBirthSplitRunnerV172)
            return !IsWorkingExecutionOrder(execution.StopOrder);

        var baseUnprotected = !IsZoneBirthBaseLegExitedV172(execution) &&
            !IsWorkingExecutionOrder(execution.StopOrder);
        var runnerUnprotected = !IsZoneBirthRunnerLegExitedV172(execution) &&
            !IsWorkingExecutionOrder(execution.RunnerStopOrder);
        return baseUnprotected || runnerUnprotected;
    }

    private static bool HasRequiredWorkingStops(ReplayExecutionState execution)
    {
        if (execution.EntryFilledQty <= 0m || execution.ExitCompleted)
            return true;
        if (execution.ProtectiveExitCallbackPendingV187 || execution.TimeStopExitPendingV208)
            return true;
        if (!execution.ZoneBirthSplitRunnerV172)
            return IsWorkingExecutionOrder(execution.StopOrder);

        var baseProtected = IsZoneBirthBaseLegExitedV172(execution) || IsWorkingExecutionOrder(execution.StopOrder);
        var runnerProtected = IsZoneBirthRunnerLegExitedV172(execution) || IsWorkingExecutionOrder(execution.RunnerStopOrder);
        return baseProtected && runnerProtected;
    }

    private static decimal RemainingExecutionQuantity(ReplayExecutionState execution)
    {
        if (execution.EntryFilledQty <= 0m)
            return 0m;
        return Math.Max(0m, execution.EntryFilledQty - execution.ExitFilledQty);
    }

    private void MarkProtectionCleanupPending(ReplayExecutionState execution, string reason)
    {
        if (execution.ProtectionCleanupDone)
            return;

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
            await TryCancelExecutionOrderAsync(execution.RunnerStopOrder, $"ProtectionCleanup:{reason}:RunnerSL");
            await TryCancelExecutionOrderAsync(execution.RunnerTargetOrder, $"ProtectionCleanup:{reason}:RunnerTP");

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
                NotifyLiveIssue($"ProtectionStale:{execution.TradeId}", $"Protection cleanup remains stale for {execution.TradeId}. workingOrders={workingAfter}. Manual verification is required.");
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
        var execution = ExecutionForOrder(workingOrder);
        try
        {
            await CancelOrderAsync(workingOrder);
            LogExecutionInfo($"EXEC_CANCEL_SENT reason={reason} ext={workingOrder.ExtId}");
            if (execution is not null)
                AppendExecutionEvent(execution, "CANCEL_SENT", ParseExecutionRole(workingOrder.Comment), workingOrder.Price > 0m ? workingOrder.Price : workingOrder.TriggerPrice, workingOrder.QuantityToFill, reason);
        }
        catch (Exception ex)
        {
            if (IsExpectedOcoCancelAlreadyInactive(workingOrder, ex.Message))
            {
                LogExecutionInfo($"EXEC_CANCEL_ALREADY_INACTIVE reason={reason} ext={workingOrder.ExtId} msg={ex.Message}");
                if (execution is not null)
                    AppendExecutionEvent(execution, "CANCEL_ALREADY_INACTIVE", ParseExecutionRole(workingOrder.Comment), workingOrder.Price > 0m ? workingOrder.Price : workingOrder.TriggerPrice, workingOrder.QuantityToFill, $"reason={reason}|msg={ex.Message}");
                return;
            }

            LogExecutionInfo($"EXEC_CANCEL_FAIL reason={reason} ext={workingOrder.ExtId} err={ex.GetType().Name}:{ex.Message}");
            if (execution is not null)
                AppendExecutionEvent(execution, "CANCEL_FAIL", ParseExecutionRole(workingOrder.Comment), workingOrder.Price > 0m ? workingOrder.Price : workingOrder.TriggerPrice, workingOrder.QuantityToFill, $"reason={reason}|err={ex.GetType().Name}:{ex.Message}");
            NotifyLiveIssue($"CancelException:{workingOrder.ExtId}", $"Order cancel threw an exception. role={ParseExecutionRole(workingOrder.Comment)}, error={ex.GetType().Name}: {ex.Message}");
        }
    }

    private async Task SubmitDuplicateExitFlattenIfNeededAsync(ReplayExecutionState execution, decimal fillQty, string role)
    {
        if (execution.DuplicateExitFlattenSubmitted)
        {
            AppendExecutionEvent(execution, "DUPLICATE_EXIT_FLATTEN_SUPPRESSED", role, 0m, fillQty, "alreadySubmitted");
            return;
        }

        var position = GetCurrentAccountPosition();
        var qty = Math.Min(Math.Abs(position), RemainingExecutionQuantity(execution));
        if (qty <= 0m)
        {
            AppendExecutionEvent(execution, "DUPLICATE_EXIT_FLATTEN_SUPPRESSED", role, 0m, fillQty, "positionFlat");
            return;
        }

        execution.DuplicateExitFlattenSubmitted = true;
        await SubmitEmergencyFlattenAsync(execution, qty, $"DuplicateExit:{role}|pos={position:0.########}");
    }

    private void ScheduleEmergencyFlattenResidualCheck(ReplayExecutionState execution, string reason)
    {
        if (execution.EmergencyFlattenResidualCheckPending)
            return;

        execution.EmergencyFlattenResidualCheckPending = true;
        _ = RecheckEmergencyFlattenResidualAsync(execution, reason);
    }

    private async Task RecheckEmergencyFlattenResidualAsync(ReplayExecutionState execution, string reason)
    {
        await Task.Delay(250);
        EnqueueExecutionAction("EmergencyFlattenResidualCheck", async () =>
        {
            execution.EmergencyFlattenResidualCheckPending = false;
            if (!IsRegisteredReplayExecution(execution) || execution.ExitCompleted ||
                IsWorkingExecutionOrder(execution.EmergencyFlattenOrder))
            {
                return;
            }

            var accountPosition = Math.Abs(GetCurrentAccountPosition());
            var remainingQty = Math.Min(accountPosition, RemainingExecutionQuantity(execution));
            if (remainingQty <= 0m)
                return;

            execution.EmergencyFlattenOrderSubmitted = false;
            LogExecutionInfo($"EXEC_EMERGENCY_FLATTEN_RESIDUAL trade={execution.TradeId} reason={reason} qty={remainingQty:0.########}");
            AppendExecutionEvent(execution, "EMERGENCY_FLATTEN_RESIDUAL", "FLATTEN", 0m, remainingQty, reason);
            await SubmitEmergencyFlattenAsync(execution, remainingQty, $"Residual:{reason}");
        });
    }

    private async Task SubmitEmergencyFlattenAsync(ReplayExecutionState execution, decimal quantity, string reason)
    {
        if (Portfolio is null || Security is null)
            return;
        if (execution.EmergencyFlattenOrderSubmitted)
        {
            LogExecutionInfo($"EXEC_EMERGENCY_FLATTEN_DUPLICATE_SUPPRESSED_V207 trade={execution.TradeId} reason={reason} qty={quantity:0.########}");
            AppendExecutionEvent(execution, "EMERGENCY_FLATTEN_DUPLICATE_SUPPRESSED_V207", "FLATTEN", 0m, quantity, $"alreadySubmitted|reason={reason}");
            return;
        }

        var position = GetCurrentAccountPosition();
        if (position == 0m)
        {
            AppendExecutionEvent(execution, "EMERGENCY_FLATTEN_SUPPRESSED", "FLATTEN", 0m, 0m, $"positionFlat|reason={reason}");
            return;
        }

        var requestedQty = quantity > 0m ? quantity : RemainingExecutionQuantity(execution);
        var qty = Math.Min(Math.Abs(position), Math.Min(requestedQty, RemainingExecutionQuantity(execution)));
        if (qty <= 0m)
            return;

        var expectedSign = execution.Side == TradeSide.Long ? 1 : -1;
        if (Math.Sign(position) != expectedSign)
        {
            var mismatch = $"positionDirectionMismatch|position={position:0.########}|side={execution.Side}|reason={reason}";
            AppendExecutionEvent(execution, "EMERGENCY_FLATTEN_BLOCKED", "FLATTEN", 0m, qty, mismatch);
            SetLiveReadinessBlocked("ManagedPositionMismatch", $"Emergency flatten for {execution.TradeId} was blocked because the account position direction does not match the execution. {mismatch}");
            return;
        }

        var direction = execution.Side == TradeSide.Long ? OrderDirections.Sell : OrderDirections.Buy;
        var order = new Order
        {
            Portfolio = Portfolio,
            Security = Security,
            Type = OrderTypes.Market,
            Direction = direction,
            QuantityToFill = qty,
            TimeInForce = ReplayTimeInForce,
            Comment = $"OPF|{execution.TradeId}|FLATTEN|{reason}",
            AutoCancel = false
        };

        execution.EmergencyFlattenSubmitted = true;
        execution.EmergencyFlattenOrderSubmitted = true;
        execution.EmergencyFlattenOrder = order;
        execution.EmergencyFlattenReferencePriceV205 = _lastResearchCandle?.Close ??
            (execution.EntryAvgPrice > 0m ? execution.EntryAvgPrice : execution.CreatedPrice);
        LogExecutionInfo($"EXEC_EMERGENCY_FLATTEN_SEND trade={execution.TradeId} reason={reason} dir={direction} qty={qty:0.########} reference={execution.EmergencyFlattenReferencePriceV205:0.########}");
        AppendExecutionEvent(execution, "EMERGENCY_FLATTEN_SEND", "FLATTEN", execution.EmergencyFlattenReferencePriceV205, qty, $"{reason}|reference={execution.EmergencyFlattenReferencePriceV205:0.########}");
        await OpenOrderAsync(order);
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

    private void AppendStandaloneExecutionEvent(string eventName, string role, OpfCandle? candle, decimal price, decimal quantity, string message)
    {
        if (_snapshot is null || candle is null)
            return;

        _researchLogger?.AppendExecutionEvent(
            _snapshot.SnapshotId,
            string.Empty,
            "LIVE-SAFETY",
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

    private void AppendExecutionTrade(
        ReplayExecutionState execution,
        decimal exit,
        string exitRole,
        ExecutionFillValidation? validation = null,
        string? hudExitRole = null)
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
        var normalizedExit = execution.Side == TradeSide.Long
            ? entry + points
            : entry - points;
        execution.UpdateActualExcursion(normalizedExit);
        if (!execution.AbnormalEntryQuarantined && !execution.AbnormalProtectiveFillQuarantined)
            UpdateExecutionHudStats(execution, validation.IsAbnormal ? "ABNORMAL" : hudExitRole ?? exitRole, dollars, pointsR);

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
        if (_lastResearchCandle is null)
            return;

        foreach (var execution in ActiveReplayExecutions())
            FinalizeActiveExecutionOnStop(execution);
    }

    private void FinalizeActiveExecutionOnStop(ReplayExecutionState execution)
    {
        if (_lastResearchCandle is null || execution.ExitCompleted)
            return;

        if (execution.ReplayStopExitPending && GetCurrentAccountPosition() == 0m)
        {
            AppendExecutionEvent(execution, "STOP_WAITING_FOR_GLOBEX_CALLBACK_V216", ReplayStopExitRole, _lastResearchCandle.Close, RemainingExecutionQuantity(execution), "positionFlat|callbackPending");
            var callbackDeadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < callbackDeadline && !execution.ExitCompleted)
                Task.Delay(100).GetAwaiter().GetResult();
            if (execution.ExitCompleted)
                return;
        }

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
                SubmitEmergencyFlattenAsync(execution, remainingQty, "StrategyStopped").GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                LogExecutionInfo($"EXEC_STOP_FLATTEN_FAIL trade={execution.TradeId} err={ex.GetType().Name}:{ex.Message}");
                AppendExecutionEvent(execution, "STOP_FLATTEN_FAIL", "FLATTEN", _lastResearchCandle.Close, remainingQty, ex.Message);
                NotifyLiveIssue($"StopFlattenFail:{execution.TradeId}", $"Strategy stop flatten failed for {execution.TradeId}: {ex.Message}. Manual intervention is required immediately.");
            }
        }

        var confirmDeadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < confirmDeadline && !execution.ExitCompleted)
        {
            Task.Delay(100).GetAwaiter().GetResult();
        }

        if (!execution.ExitCompleted)
        {
            var reason = $"position={GetCurrentAccountPosition():0.########}|exitCallback=false";
            LogExecutionInfo($"EXEC_STOP_FLATTEN_UNCONFIRMED trade={execution.TradeId} {reason}");
            AppendExecutionEvent(execution, "STOP_FLATTEN_UNCONFIRMED", "FLATTEN", 0m, Math.Abs(GetCurrentAccountPosition()), reason);
            NotifyLiveIssue($"StopFlattenUnconfirmed:{execution.TradeId}", $"Strategy stopped without a confirmed flatten fill for {execution.TradeId}. AccountPosition={GetCurrentAccountPosition():0.########}. Verify the account and working orders manually.");
            if (GetCurrentAccountPosition() != 0m)
                return;
        }

        MarkProtectionCleanupPending(execution, "StrategyStoppedConfirmedFlat");
        try
        {
            CleanupProtectionOrdersOnStop(execution, "StrategyStoppedConfirmedFlat");
        }
        catch (Exception ex)
        {
            LogExecutionInfo($"EXEC_STOP_CLEANUP_FAIL trade={execution.TradeId} err={ex.GetType().Name}:{ex.Message}");
            AppendExecutionEvent(execution, "STOP_CLEANUP_FAIL", "-", 0m, CountWorkingProtectionOrders(execution), ex.Message);
            NotifyLiveIssue($"StopCleanupFail:{execution.TradeId}", $"Stop-time order cleanup failed for {execution.TradeId}: {ex.Message}. Verify working orders manually.");
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

    private static bool IsProtectiveExitRole(string role)
    {
        return role is "SL" or "TP" or "BASE_SL" or "BASE_TP" or "RUNNER_SL" or "RUNNER_TP";
    }

    private static bool IsStopExitRole(string role)
    {
        return role is "SL" or "BASE_SL" or "RUNNER_SL";
    }

    private static bool IsTargetExitRole(string role)
    {
        return role is "TP" or "BASE_TP" or "RUNNER_TP";
    }

    private bool IsActiveExecutionOrder(Order? order)
    {
        if (order is null)
            return false;
        return TryParseExecutionComment(order.Comment, out var tradeId, out _) &&
            IsReplayExecutionActive(FindReplayExecution(tradeId));
    }

    private static bool IsReplayExecutionActive(ReplayExecutionState? execution)
    {
        return execution is not null && (!execution.ExitCompleted || execution.ProtectionCleanupPending);
    }

    private static DateTime GlobexTradingDayKey(DateTime time)
    {
        var eastern = ToEasternTime(time);
        return eastern.TimeOfDay >= GlobexReopenEastern
            ? eastern.Date.AddDays(1)
            : eastern.Date;
    }

    private static bool IsGlobexCloseoutLockWindow(DateTime time, out string reason)
    {
        var eastern = ToEasternTime(time);
        var day = eastern.DayOfWeek;
        var t = eastern.TimeOfDay;
        var locked = day == DayOfWeek.Saturday ||
            (day == DayOfWeek.Sunday && t < GlobexReopenEastern) ||
            (day == DayOfWeek.Friday && t >= GlobexCloseoutStartEastern) ||
            (day is >= DayOfWeek.Monday and <= DayOfWeek.Thursday &&
                t >= GlobexCloseoutStartEastern && t < GlobexReopenEastern);
        reason = locked
            ? $"GlobexCloseoutLock:Eastern={eastern:ddd HH:mm}|TradingDay={GlobexTradingDayKey(time):yyyy-MM-dd}"
            : string.Empty;
        return locked;
    }

    private static DateTime ToEasternTime(DateTime time)
    {
        var utc = time.Kind == DateTimeKind.Utc
            ? time
            : DateTime.SpecifyKind(time, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(utc, EasternTimeZone);
    }

    private static bool IsUsCashOpenBlackout(DateTime time, out string reason)
    {
        var date = time.Date;
        var easternNoon = DateTime.SpecifyKind(date.AddHours(12), DateTimeKind.Unspecified);
        var daylightSaving = EasternTimeZone.IsDaylightSavingTime(easternNoon);
        var start = daylightSaving
            ? SummerUsOpenBlockStartUtc
            : WinterUsOpenBlockStartUtc;
        var end = start + UsOpenBlockDuration;
        var blocked = time.TimeOfDay >= start && time.TimeOfDay < end;
        var chinaStart = daylightSaving ? new TimeSpan(21, 30, 0) : new TimeSpan(22, 30, 0);
        var chinaEnd = chinaStart + UsOpenBlockDuration;
        reason = blocked
            ? $"UsCashOpenBlackout:China={chinaStart:hh\\:mm}-{chinaEnd:hh\\:mm}|UTC={start:hh\\:mm}-{end:hh\\:mm}"
            : string.Empty;
        return blocked;
    }

    private void AllowHistoricalReplayConnectorBypass(OpfCandle candle)
    {
        if (!IsHistoricalReplayTime(candle.Time) || !_liveReadinessBlocked ||
            _liveReadinessBlockReason is not ("ConnectorMissing" or "Disconnected" or "ReconnectReconciling"))
        {
            return;
        }

        var previousReason = _liveReadinessBlockReason;
        _liveReadinessBlocked = false;
        _liveReadinessBlockReason = "-";
        _liveNotificationKeys.Remove($"LiveBlock:{previousReason}");
        LogLiveReadinessInfo($"HISTORICAL_REPLAY_CONNECTOR_BYPASS previous={previousReason}");
        AppendStandaloneExecutionEvent("HISTORICAL_REPLAY_CONNECTOR_BYPASS", "-", candle, candle.Close, 0m, previousReason);
    }

    private static bool IsHistoricalReplayTime(DateTime candleTime)
    {
        return Math.Abs((DateTime.Now - candleTime).TotalMinutes) > 10d;
    }

    private bool IsLiveLatencyBlocked(DateTime candleTime, out string reason)
    {
        reason = string.Empty;
        if (IsHistoricalReplayTime(candleTime))
            return false;

        var latency = Connector?.LatencyManager;
        if (latency is null)
        {
            reason = "LiveLatencyUnavailable";
            ActivateLatencyGate(reason);
            return true;
        }

        var market = latency.MarketDataLatency;
        var orders = latency.OrdersLatency;
        TimeSpan? silence = latency.LastMarketDataReceptionTimeUtc.HasValue
            ? DateTime.UtcNow - latency.LastMarketDataReceptionTimeUtc.Value
            : null;
        var metricsAvailable = market.HasValue || silence.HasValue;
        var unhealthy = !metricsAvailable ||
            (market.HasValue && market.Value > MaxLiveMarketDataLatency) ||
            (orders.HasValue && orders.Value > MaxLiveOrdersLatency) ||
            (silence.HasValue && silence.Value > MaxLiveMarketDataSilence);
        if (unhealthy)
        {
            reason = $"LiveLatencyExceeded:marketMs={LatencyMilliseconds(market)}|orderMs={LatencyMilliseconds(orders)}|silenceMs={LatencyMilliseconds(silence)}";
            ActivateLatencyGate(reason);
            return true;
        }

        if (!_latencyGateActive)
            return false;

        _latencyHealthySinceUtc ??= DateTime.UtcNow;
        var stableFor = DateTime.UtcNow - _latencyHealthySinceUtc.Value;
        if (stableFor < LiveLatencyRecoveryPeriod)
        {
            reason = $"LiveLatencyRecovering:stableMs={stableFor.TotalMilliseconds:0}|requiredMs={LiveLatencyRecoveryPeriod.TotalMilliseconds:0}";
            return true;
        }

        _latencyGateActive = false;
        _latencyHealthySinceUtc = null;
        LogLiveReadinessInfo("LIVE_LATENCY_RECOVERED");
        RaiseShowNotification("Live latency has remained healthy for 10 seconds. New Actual entries are enabled.", "OPFStrategyV1");
        return false;
    }

    private void ActivateLatencyGate(string reason)
    {
        _latencyHealthySinceUtc = null;
        if (_latencyGateActive)
            return;

        _latencyGateActive = true;
        LogLiveReadinessInfo($"LIVE_LATENCY_BLOCK {reason}");
        RaiseShowNotification($"Severe live latency detected. New Actual entries are blocked. {reason}", "OPFStrategyV1");
    }

    private static string LatencyMilliseconds(TimeSpan? latency)
    {
        return latency.HasValue ? latency.Value.TotalMilliseconds.ToString("0") : "-";
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
        if (IsWorkingExecutionOrder(execution.RunnerStopOrder))
            count++;
        if (IsWorkingExecutionOrder(execution.RunnerTargetOrder))
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

    private decimal GetCurrentAccountPosition()
    {
        try
        {
            if (Connector is not null && Portfolio is not null && Security is not null)
                return Connector.GetPosition(Portfolio, Security, null)?.Volume ?? CurrentPosition;
        }
        catch
        {
        }
        return CurrentPosition;
    }

    private static bool IsFilledExecutionOrder(Order? order)
    {
        return order is not null && order.Unfilled <= 0m;
    }

    private static bool IsZoneBirthBaseLegExitedV172(ReplayExecutionState execution)
    {
        return execution.BaseExitFilledQty >= ZoneBirthSplitRunnerV172BaseQuantity ||
            IsFilledExecutionOrder(execution.StopOrder) ||
            IsFilledExecutionOrder(execution.TargetOrder);
    }

    private static bool IsZoneBirthRunnerLegExitedV172(ReplayExecutionState execution)
    {
        return execution.RunnerExitFilledQty >= ZoneBirthSplitRunnerV172RunnerQuantity ||
            IsFilledExecutionOrder(execution.RunnerStopOrder) ||
            IsFilledExecutionOrder(execution.RunnerTargetOrder);
    }

    private static bool IsExitOrder(Order? order)
    {
        if (order is null)
            return false;
        return TryParseExecutionComment(order.Comment, out _, out var role) && IsProtectiveExitRole(role);
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
            !TryParseExecutionComment(order.Comment, out var tradeId, out var role))
        {
            return false;
        }

        var execution = FindReplayExecution(tradeId);
        return execution is not null &&
            ((execution.ExitCompleted && execution.ProtectionCleanupPending) || IsCompletedSplitLegRoleV172(execution, role));
    }

    private bool IsExpectedOcoCancelAlreadyInactive(Order? order, string? message)
    {
        if (order is null ||
            string.IsNullOrWhiteSpace(message) ||
            !message.Contains("for cancel not found", StringComparison.OrdinalIgnoreCase) ||
            !IsExitOrder(order) ||
            !TryParseExecutionComment(order.Comment, out var tradeId, out var role))
        {
            return false;
        }

        var execution = FindReplayExecution(tradeId);
        return execution is not null &&
            ((execution.ExitCompleted && execution.ProtectionCleanupPending) || IsCompletedSplitLegRoleV172(execution, role));
    }

    private static bool IsCompletedSplitLegRoleV172(ReplayExecutionState execution, string role)
    {
        if (!execution.ZoneBirthSplitRunnerV172)
            return false;
        if (role == "BASE_SL")
            return IsZoneBirthBaseLegExitedV172(execution);
        if (role == "BASE_TP")
            return IsZoneBirthBaseLegExitedV172(execution);
        if (role == "RUNNER_SL")
            return IsZoneBirthRunnerLegExitedV172(execution);
        if (role == "RUNNER_TP")
            return IsZoneBirthRunnerLegExitedV172(execution);
        return false;
    }

    private ReplayExecutionState? FindReplayExecution(string tradeId)
    {
        if (_replayExecutionsByTradeId.TryGetValue(tradeId, out var registered))
            return registered;
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

    private ReplayExecutionState? ExecutionForOrder(Order? order)
    {
        return order is not null &&
            TryParseExecutionComment(order.Comment, out var tradeId, out _)
            ? FindReplayExecution(tradeId)
            : null;
    }

    private List<ReplayExecutionState> ActiveReplayExecutions()
    {
        return _replayExecutionsByTradeId.Values
            .Where(IsReplayExecutionActive)
            .OrderBy(x => x.CreatedTime)
            .ThenBy(x => x.TradeId, StringComparer.Ordinal)
            .ToList();
    }

    private bool IsRegisteredReplayExecution(ReplayExecutionState execution)
    {
        return _replayExecutionsByTradeId.TryGetValue(execution.TradeId, out var registered) &&
            ReferenceEquals(registered, execution);
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

        if (researchPath is "BreakawayFvg" or "BreakawayFvg_Qualified" || IsBreakawayRetestResearchPath(researchPath))
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

    private decimal CalculateAtr14(OpfCandle current) => CalculateAtr14(current, _recentCandles);

    private static decimal CalculateAtr14(OpfCandle current, IEnumerable<OpfCandle> recentCandles)
    {
        var candles = recentCandles.Concat(new[] { current }).TakeLast(15).ToArray();
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

    private void StartShadowTradeTracking(CandidateSignal signal, OpfCandle entryCandle, string researchPath, decimal stop, decimal risk)
    {
        if (_snapshot is null || _researchLogger is null || risk <= 0m)
            return;

        var key = ShadowTradeKey(signal.SignalId, researchPath, entryCandle.Bar);
        if (_shadowTradeTrackersByKey.ContainsKey(key))
            return;

        var tracker = new ShadowTradeTracker(
            key,
            new ResearchTracker(signal, entryCandle.Time, entryCandle.Bar, entryCandle.Close, stop, risk, int.MaxValue, researchPath));
        _shadowTradeTrackers.Add(tracker);
        _shadowTradeTrackersByKey[key] = tracker;
    }

    private void StartDecisionTapeCalibrationTracking(CandidateSignal signal, OpfCandle entryCandle, string researchPath, decimal stop, decimal risk)
    {
        if (!DecisionTapeCalibrationCollection || _snapshot is null || _researchLogger is null || risk <= 0m)
            return;

        var key = ShadowTradeKey(signal.SignalId, researchPath, entryCandle.Bar);
        if (_decisionTapeCalibrationTrackersByKey.ContainsKey(key))
            return;

        var researchTracker = new ResearchTracker(
            signal,
            entryCandle.Time,
            entryCandle.Bar,
            entryCandle.Close,
            stop,
            risk,
            DecisionTapeCalibrationMaxBars,
            researchPath);
        researchTracker.Update(entryCandle);
        var quote = ExecutionQuoteSnapshot();
        var tracker = new DecisionTapeCalibrationTracker(key, researchTracker, entryCandle, quote.Bid, quote.Ask);
        tracker.SetEntryMarketSequence(_decisionTapeMarketSequence);
        _decisionTapeCalibrationTrackers.Add(tracker);
        _decisionTapeCalibrationTrackersByKey[key] = tracker;
    }

    private void UpdateShadowTradeTrackers(OpfCandle candle)
    {
        if (_snapshot is null || _shadowTradeTrackers.Count == 0)
            return;

        for (var i = _shadowTradeTrackers.Count - 1; i >= 0; i--)
        {
            var shadow = _shadowTradeTrackers[i];
            if (candle.Bar <= shadow.Tracker.EntryBar)
                continue;

            shadow.Tracker.Update(candle);
            if (TryResolveShadowTrade(shadow.Tracker, candle, false, string.Empty, out var result) ||
                (IsGlobexCloseoutLockWindow(candle.Time, out _) &&
                 TryResolveShadowTrade(shadow.Tracker, candle, true, "GlobexCloseout", out result)))
            {
                WriteShadowTrade(shadow, candle, result);
                RemoveShadowTradeTrackerAt(i);
            }
        }
    }

    private void UpdateDecisionTapeCalibrationTrackers(OpfCandle candle)
    {
        if (_snapshot is null || _decisionTapeCalibrationTrackers.Count == 0)
            return;

        for (var i = _decisionTapeCalibrationTrackers.Count - 1; i >= 0; i--)
        {
            var calibration = _decisionTapeCalibrationTrackers[i];
            if (candle.Bar <= calibration.Tracker.EntryBar)
                continue;

            calibration.AppendBar(candle);
            calibration.Tracker.Update(candle);
            if (calibration.Tracker.IsComplete(candle.Bar) || IsGlobexCloseoutLockWindow(candle.Time, out _))
            {
                WriteDecisionTapeCalibration(calibration, candle, calibration.Tracker.IsComplete(candle.Bar) ? "MaxBars" : "GlobexCloseout");
                RemoveDecisionTapeCalibrationTrackerAt(i);
            }
        }
    }

    private void FlushShadowTradeTrackers(string reason)
    {
        if (_snapshot is null || _lastResearchCandle is null)
            return;

        for (var i = _shadowTradeTrackers.Count - 1; i >= 0; i--)
        {
            var shadow = _shadowTradeTrackers[i];
            if (TryResolveShadowTrade(shadow.Tracker, _lastResearchCandle, true, reason, out var result))
                WriteShadowTrade(shadow, _lastResearchCandle, result);
            RemoveShadowTradeTrackerAt(i);
        }
    }

    private void FlushDecisionTapeCalibrationTrackers(string reason)
    {
        if (_snapshot is null || _lastResearchCandle is null)
            return;

        for (var i = _decisionTapeCalibrationTrackers.Count - 1; i >= 0; i--)
        {
            WriteDecisionTapeCalibration(_decisionTapeCalibrationTrackers[i], _lastResearchCandle, reason);
            RemoveDecisionTapeCalibrationTrackerAt(i);
        }
    }

    private void RemoveShadowTradeTrackerAt(int index)
    {
        var shadow = _shadowTradeTrackers[index];
        _shadowTradeTrackers.RemoveAt(index);
        _shadowTradeTrackersByKey.Remove(shadow.Key);
    }

    private void RemoveDecisionTapeCalibrationTrackerAt(int index)
    {
        var calibration = _decisionTapeCalibrationTrackers[index];
        _decisionTapeCalibrationTrackers.RemoveAt(index);
        _decisionTapeCalibrationTrackersByKey.Remove(calibration.Key);
    }

    private bool TryResolveShadowTrade(ResearchTracker tracker, OpfCandle candle, bool forceExit, string forceReason, out ShadowTradeResult result)
    {
        if (UsesZoneBirthSplitRunnerV172(tracker.Signal.Side, tracker.ResearchPath))
        {
            var baseTargetR = DynamicExpansionV168TargetR;
            var baseTargetBar = tracker.First2_5RBar;
            var stopBar = tracker.FirstStopBar;
            var stopVsBaseAmbiguous = stopBar.HasValue && baseTargetBar.HasValue && stopBar.Value == baseTargetBar.Value;
            if (stopBar.HasValue && (!baseTargetBar.HasValue || stopBar.Value <= baseTargetBar.Value))
            {
                result = ShadowResultFromR(tracker, "ZoneBirthSplit2_5R_4R_BEAfterBase", "Base:Stop|Runner:Stop", -1m, stopBar.Value, stopVsBaseAmbiguous);
                return true;
            }

            if (baseTargetBar.HasValue)
            {
                var runnerTargetBar = tracker.First4RBar;
                var runnerBreakEvenBar = tracker.FirstBreakEvenAfter2_5RBar;
                var runnerAmbiguous = runnerTargetBar.HasValue && runnerBreakEvenBar.HasValue && runnerTargetBar.Value == runnerBreakEvenBar.Value;
                if (runnerTargetBar.HasValue && (!runnerBreakEvenBar.HasValue || runnerTargetBar.Value < runnerBreakEvenBar.Value))
                {
                    result = ShadowResultFromR(tracker, "ZoneBirthSplit2_5R_4R_BEAfterBase", "Base:Target|Runner:Target", (baseTargetR + ZoneBirthSplitRunnerV173TargetR) / 2m, runnerTargetBar.Value, runnerAmbiguous);
                    return true;
                }
                if (runnerBreakEvenBar.HasValue && (!runnerTargetBar.HasValue || runnerBreakEvenBar.Value <= runnerTargetBar.Value))
                {
                    result = ShadowResultFromR(tracker, "ZoneBirthSplit2_5R_4R_BEAfterBase", "Base:Target|Runner:ProtectBE", baseTargetR / 2m, runnerBreakEvenBar.Value, runnerAmbiguous);
                    return true;
                }
            }
        }
        else
        {
            var stopBar = tracker.FirstStopBar;
            var triggerBar = tracker.First1RBar;
            var stopVsTriggerAmbiguous = stopBar.HasValue && triggerBar.HasValue && stopBar.Value == triggerBar.Value;
            if (stopBar.HasValue && (!triggerBar.HasValue || stopBar.Value <= triggerBar.Value))
            {
                result = ShadowResultFromR(tracker, "ProtectBE1R_Then3R", "Stop", -1m, stopBar.Value, stopVsTriggerAmbiguous);
                return true;
            }

            if (triggerBar.HasValue)
            {
                var targetBar = tracker.First3RBar;
                var breakEvenBar = tracker.FirstBreakEvenAfter1RBar;
                var protectedAmbiguous = targetBar.HasValue && breakEvenBar.HasValue && targetBar.Value == breakEvenBar.Value;
                if (targetBar.HasValue && (!breakEvenBar.HasValue || targetBar.Value < breakEvenBar.Value))
                {
                    result = ShadowResultFromR(tracker, "ProtectBE1R_Then3R", "Target", ProtectBreakEvenTargetRV186, targetBar.Value, protectedAmbiguous);
                    return true;
                }
                if (breakEvenBar.HasValue && (!targetBar.HasValue || breakEvenBar.Value <= targetBar.Value))
                {
                    result = ShadowResultFromR(tracker, "ProtectBE1R_Then3R", "ProtectBE", 0m, breakEvenBar.Value, protectedAmbiguous);
                    return true;
                }
            }
        }

        if (!forceExit)
        {
            result = default;
            return false;
        }

        var points = tracker.Signal.Side == TradeSide.Long
            ? candle.Close - tracker.Entry
            : tracker.Entry - candle.Close;
        if (UsesZoneBirthSplitRunnerV172(tracker.Signal.Side, tracker.ResearchPath) && tracker.First2_5RBar.HasValue)
            points = (tracker.InitialRiskPoints * DynamicExpansionV168TargetR + points) / 2m;
        var pnlR = tracker.InitialRiskPoints <= 0m ? 0m : Math.Round(points / tracker.InitialRiskPoints, 4);
        var syntheticExitPrice = tracker.Signal.Side == TradeSide.Long
            ? tracker.Entry + points
            : tracker.Entry - points;
        result = new ShadowTradeResult(
            UsesZoneBirthSplitRunnerV172(tracker.Signal.Side, tracker.ResearchPath) ? "ZoneBirthSplit2_5R_4R_BEAfterBase" : "ProtectBE1R_Then3R",
            forceReason,
            syntheticExitPrice,
            points,
            pnlR,
            candle.Bar,
            false);
        return true;
    }

    private static ShadowTradeResult ShadowResultFromR(ResearchTracker tracker, string policy, string exitReason, decimal pnlR, int exitBar, bool ambiguous)
    {
        var points = tracker.InitialRiskPoints * pnlR;
        var exitPrice = tracker.Signal.Side == TradeSide.Long
            ? tracker.Entry + points
            : tracker.Entry - points;
        return new ShadowTradeResult(policy, exitReason, exitPrice, points, pnlR, exitBar, ambiguous);
    }

    private void CaptureDecisionTapeMarketTurn(int bar, decimal price)
    {
        if (!DecisionTapeCalibrationCollection || _snapshot is null || price <= 0m)
            return;

        _decisionTapeMarketSequence++;
        if (_decisionTapeTurnBar < 0)
        {
            _decisionTapeTurnBar = bar;
            _decisionTapeTurnPrice = price;
            _decisionTapeTurnSequence = _decisionTapeMarketSequence;
            _decisionTapeTurnDirection = 0;
            AddDecisionTapeMarketTurn("Start", _decisionTapeTurnSequence, bar, price);
            return;
        }

        if (bar != _decisionTapeTurnBar)
        {
            AddDecisionTapeMarketTurn("BarEnd", _decisionTapeTurnSequence, _decisionTapeTurnBar, _decisionTapeTurnPrice);
            _decisionTapeTurnBar = bar;
            _decisionTapeTurnPrice = price;
            _decisionTapeTurnSequence = _decisionTapeMarketSequence;
            _decisionTapeTurnDirection = 0;
            AddDecisionTapeMarketTurn("BarStart", _decisionTapeTurnSequence, bar, price);
            return;
        }

        var direction = price.CompareTo(_decisionTapeTurnPrice);
        if (direction == 0)
            return;

        if (_decisionTapeTurnDirection != 0 && direction != _decisionTapeTurnDirection)
        {
            AddDecisionTapeMarketTurn(
                _decisionTapeTurnDirection > 0 ? "TurnHigh" : "TurnLow",
                _decisionTapeTurnSequence,
                _decisionTapeTurnBar,
                _decisionTapeTurnPrice);
        }

        _decisionTapeTurnDirection = direction;
        _decisionTapeTurnPrice = price;
        _decisionTapeTurnSequence = _decisionTapeMarketSequence;
    }

    private void AddDecisionTapeMarketTurn(string kind, long sequence, int bar, decimal price)
    {
        if (_decisionTapeMarketTurns.Count > 0 && _decisionTapeMarketTurns[^1].Sequence == sequence)
            return;
        _decisionTapeMarketTurns.Add(new DecisionTapeMarketTurn(sequence, bar, price, kind));
    }

    private void FlushDecisionTapeMarketTurns()
    {
        if (_snapshot is null || _researchLogger is null)
            return;

        if (_decisionTapeTurnBar >= 0)
            AddDecisionTapeMarketTurn("End", _decisionTapeMarketSequence, _decisionTapeTurnBar, _decisionTapeTurnPrice);

        foreach (var turn in _decisionTapeMarketTurns)
        {
            _researchLogger.AppendDecisionTapeMarketTurn(
                _snapshot.SnapshotId,
                turn.Sequence,
                turn.Bar,
                turn.Price,
                turn.Kind);
        }
    }

    private void WriteDecisionTapeCalibration(DecisionTapeCalibrationTracker calibration, OpfCandle resolveCandle, string completionReason)
    {
        if (_snapshot is null || _researchLogger is null)
            return;

        var tracker = calibration.Tracker;
        _researchLogger.AppendDecisionTapeCalibration(
            _snapshot.SnapshotId,
            tracker.Signal.SignalId,
            tracker.EntryTime,
            tracker.EntryBar,
            resolveCandle.Time,
            resolveCandle.Bar,
            tracker.Signal.Side.ToString(),
            tracker.Signal.SetupType.ToString(),
            tracker.ResearchPath,
            tracker.Signal.RegimeScore.TotalScore,
            tracker.Signal.SetupQualityScore.TotalScore,
            tracker.Entry,
            tracker.Stop,
            tracker.InitialRiskPoints,
            calibration.EntryBid,
            calibration.EntryAsk,
            calibration.EntryBid > 0m && calibration.EntryAsk > 0m,
            calibration.EntryMarketSequence,
            _decisionTapeMarketSequence,
            ActualTargetRFor(tracker.Signal, tracker.ResearchPath, tracker.InitialRiskPoints),
            tracker.MfePoints,
            tracker.MaePoints,
            tracker.FirstStopBar,
            tracker.First0_75RBar,
            tracker.First1RBar,
            tracker.First1_5RBar,
            tracker.First2RBar,
            tracker.First2_5RBar,
            tracker.First3RBar,
            tracker.First4RBar,
            tracker.FirstBreakEvenAfter0_75RBar,
            tracker.FirstBreakEvenAfter1RBar,
            tracker.FirstBreakEvenAfter1_5RBar,
            tracker.First1RLockAfter1_5RBar,
            tracker.FirstBreakEvenAfter2_5RBar,
            Math.Max(0, resolveCandle.Bar - tracker.EntryBar),
            completionReason,
            calibration.OriginalDecision,
            calibration.OriginalReason,
            calibration.OriginalTradeId);

        foreach (var candle in calibration.Bars)
        {
            _researchLogger.AppendDecisionTapeCalibrationBar(
                _snapshot.SnapshotId,
                tracker.Signal.SignalId,
                tracker.ResearchPath,
                tracker.EntryBar,
                candle.Time,
                candle.Bar,
                candle.Open,
                candle.High,
                candle.Low,
                candle.Close);
        }
    }

    private void WriteShadowTrade(ShadowTradeTracker shadow, OpfCandle exitCandle, ShadowTradeResult result)
    {
        if (_snapshot is null || _researchLogger is null)
            return;

        var tracker = shadow.Tracker;
        var quantity = _snapshot.ExecutionProfile.FixedContracts;
        var gross = Math.Round(result.PnlPoints * _snapshot.InstrumentProfile.PointValue * quantity, 2);
        var commission = Math.Round(quantity * _actualCommissionPerContractRoundTrip, 2);
        _researchLogger.AppendShadowTrade(
            _snapshot.SnapshotId,
            tracker.Signal.SignalId,
            tracker.EntryTime,
            tracker.EntryBar,
            exitCandle.Time,
            result.ExitBar,
            tracker.Signal.Side.ToString(),
            tracker.ResearchPath,
            tracker.Entry,
            tracker.Stop,
            tracker.InitialRiskPoints,
            result.Policy,
            result.ExitReason,
            result.ExitPrice,
            result.PnlPoints,
            result.PnlR,
            gross,
            commission,
            gross - commission,
            Math.Max(0, result.ExitBar - tracker.EntryBar),
            result.Ambiguous,
            shadow.OriginalDecision,
            shadow.OriginalReason,
            shadow.OriginalTradeId);
    }

    private static string ShadowTradeKey(string signalId, string researchPath, int entryBar)
    {
        return $"{signalId}|{researchPath}|{entryBar}";
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
        return ActiveReplayExecutions().Any(execution =>
            string.Equals(execution.SignalId, tracker.Signal.SignalId, StringComparison.Ordinal) &&
            string.Equals(execution.ResearchPath, tracker.ResearchPath, StringComparison.Ordinal));
    }

    private void AppendExitPolicyEvaluations(ResearchTracker tracker, OpfCandle exitCandle)
    {
        if (_snapshot is null)
            return;
        if (_compactResearchLogging && !ShouldWriteCompactExitPolicyTracker(tracker))
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
        AppendProtectedExtensionPolicyEvaluation(tracker, exitCandle, "ProtectBE0_75R_Then2_5R", 2.5m, tracker.First2_5RBar, 0m, tracker.First0_75RBar, tracker.FirstBreakEvenAfter0_75RBar);
        AppendProtectedExtensionPolicyEvaluation(tracker, exitCandle, "ProtectBE1R_Then2_5R", 2.5m, tracker.First2_5RBar, 0m, tracker.First1RBar, tracker.FirstBreakEvenAfter1RBar);
        AppendProtectedExtensionPolicyEvaluation(tracker, exitCandle, "ProtectBE1R_Then3R", 3m, tracker.First3RBar, 0m, tracker.First1RBar, tracker.FirstBreakEvenAfter1RBar);
        AppendProtectedExtensionPolicyEvaluation(tracker, exitCandle, "Protect1RAfter1_5R_Then2_5R", 2.5m, tracker.First2_5RBar, 1m, tracker.First1_5RBar, tracker.First1RLockAfter1_5RBar);
        AppendProtectedExtensionPolicyEvaluation(tracker, exitCandle, "Protect1RAfter1_5R_Then3R", 3m, tracker.First3RBar, 1m, tracker.First1_5RBar, tracker.First1RLockAfter1_5RBar);
        if (_compactResearchLogging)
            return;

        AppendProtectedExtensionPolicyEvaluation(tracker, exitCandle, "ProtectBE_Then2_5R", 2.5m, tracker.First2_5RBar, 0m, tracker.First1_5RBar, tracker.FirstBreakEvenAfter1_5RBar);
        AppendProtectedExtensionPolicyEvaluation(tracker, exitCandle, "Protect1R_Then2_5R", 2.5m, tracker.First2_5RBar, 1m, tracker.First1_5RBar, tracker.First1RLockAfter1_5RBar);
        AppendProtectedExtensionPolicyEvaluation(tracker, exitCandle, "ProtectBE_Then3R", 3m, tracker.First3RBar, 0m, tracker.First1_5RBar, tracker.FirstBreakEvenAfter1_5RBar);
        AppendProtectedExtensionPolicyEvaluation(tracker, exitCandle, "Protect1R_Then3R", 3m, tracker.First3RBar, 1m, tracker.First1_5RBar, tracker.First1RLockAfter1_5RBar);
    }

    private bool ShouldWriteCompactExitPolicyTracker(ResearchTracker tracker)
    {
        var researchPath = tracker.ResearchPath;
        if (string.Equals(researchPath, "ObservationConfirm", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(researchPath, "ObservationConfirm_WideStop1_5R", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(researchPath, "BreakawayFvg", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(researchPath, "BreakawayFvg_Qualified", StringComparison.OrdinalIgnoreCase) ||
            IsBreakawayRetestResearchPath(researchPath) ||
            string.Equals(researchPath, "FailureReverse_ObservationInvalidated", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(researchPath, "FailureReverse_RetestFailed", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return TryGetAggressiveExpansionV164Rules(tracker.Signal.Side, researchPath, out var minScore, out var minEstimatedRr, out _) &&
            tracker.Signal.SetupQualityScore.TotalScore >= minScore &&
            tracker.InitialRiskPoints <= AggressiveExpansionV164MaxRiskPoints &&
            EstimatedActualRr(tracker.Signal, researchPath, tracker.Entry, tracker.InitialRiskPoints) >= minEstimatedRr;
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
            tracker.MaeR,
            result.ExitBar);
    }

    private void AppendProtectedExtensionPolicyEvaluation(
        ResearchTracker tracker,
        OpfCandle exitCandle,
        string exitPolicy,
        decimal targetR,
        int? firstTargetBar,
        decimal lockR,
        int? firstTriggerBar,
        int? firstProtectStopBar)
    {
        if (_snapshot is null)
            return;

        var target = tracker.Signal.Side == TradeSide.Long
            ? tracker.Entry + tracker.InitialRiskPoints * targetR
            : tracker.Entry - tracker.InitialRiskPoints * targetR;
        var result = ResolveProtectedExtensionPolicy(tracker, exitCandle, targetR, firstTargetBar, lockR, firstTriggerBar, firstProtectStopBar);

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
            tracker.MaeR,
            result.ExitBar);
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
            tracker.MaeR,
            Math.Max(baseResult.ExitBar, runnerResult.ExitBar));
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
            return TargetExit(tracker, targetR, ambiguous, firstTargetBar.Value);
        if (firstStopBar.HasValue && (!firstTargetBar.HasValue || firstStopBar.Value <= firstTargetBar.Value))
            return StopExit(tracker, ambiguous, firstStopBar.Value);

        var points = tracker.Signal.Side == TradeSide.Long
            ? exitCandle.Close - tracker.Entry
            : tracker.Entry - exitCandle.Close;
        var pnlR = tracker.InitialRiskPoints <= 0m ? 0m : Math.Round(points / tracker.InitialRiskPoints, 4);
        return new ExitPolicyResult("TimeStop", exitCandle.Close, points, pnlR, false, exitCandle.Bar);
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
            return firstStopBar.HasValue ? StopExit(tracker, false, firstStopBar.Value) : TimeStopExit(tracker, exitCandle, "TimeStop");
        if (firstStopBar.HasValue && firstStopBar.Value <= firstTriggerBar.Value)
            return StopExit(tracker, false, firstStopBar.Value);

        var ambiguous = firstProtectStopBar.HasValue && firstTargetBar.HasValue && firstProtectStopBar.Value == firstTargetBar.Value;
        if (firstTargetBar.HasValue && (!firstProtectStopBar.HasValue || firstTargetBar.Value < firstProtectStopBar.Value))
            return TargetExit(tracker, targetR, ambiguous, firstTargetBar.Value);
        if (firstProtectStopBar.HasValue && (!firstTargetBar.HasValue || firstProtectStopBar.Value <= firstTargetBar.Value))
            return ProtectedStopExit(tracker, lockR, ambiguous, firstProtectStopBar.Value);

        return TimeStopExit(tracker, exitCandle, "TimeStopAfterProtectionTrigger");
    }

    private static ExitPolicyResult TargetExit(ResearchTracker tracker, decimal targetR, bool ambiguous, int exitBar)
    {
        var points = tracker.InitialRiskPoints * targetR;
        var exitPrice = tracker.Signal.Side == TradeSide.Long
            ? tracker.Entry + points
            : tracker.Entry - points;
        return new ExitPolicyResult("Target", exitPrice, points, targetR, ambiguous, exitBar);
    }

    private static ExitPolicyResult TimeStopExit(ResearchTracker tracker, OpfCandle exitCandle, string reason)
    {
        var points = tracker.Signal.Side == TradeSide.Long
            ? exitCandle.Close - tracker.Entry
            : tracker.Entry - exitCandle.Close;
        var pnlR = tracker.InitialRiskPoints <= 0m ? 0m : Math.Round(points / tracker.InitialRiskPoints, 4);
        return new ExitPolicyResult(reason, exitCandle.Close, points, pnlR, false, exitCandle.Bar);
    }

    private static ExitPolicyResult StopExit(ResearchTracker tracker, bool ambiguous, int exitBar)
    {
        var points = -tracker.InitialRiskPoints;
        return new ExitPolicyResult("Stop", tracker.Stop, points, -1m, ambiguous, exitBar);
    }

    private static ExitPolicyResult ProtectedStopExit(ResearchTracker tracker, decimal lockR, bool ambiguous, int exitBar)
    {
        var points = tracker.InitialRiskPoints * lockR;
        var exitPrice = tracker.Signal.Side == TradeSide.Long
            ? tracker.Entry + points
            : tracker.Entry - points;
        var reason = lockR <= 0m ? "ProtectBE" : $"Protect{lockR:0.##}R";
        return new ExitPolicyResult(reason, exitPrice, points, lockR, ambiguous, exitBar);
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
        foreach (var execution in ActiveReplayExecutions())
        {
            execution.ObserveProtectiveRange(candle.Low, candle.High);
            execution.UpdateActualExcursion(candle);
        }
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
        hud.AppendLine($"RunProfile: {_snapshot?.ActualExecutionSettings.RunProfileId ?? "-"} ({_snapshot?.ActualExecutionSettings.RunMode ?? "-"})");
        hud.AppendLine($"Snapshot: {shortSnapshot}");
        hud.AppendLine($"Bar: {candle.Bar}  Time: {candle.Time:MM-dd HH:mm}");
        hud.AppendLine($"Regime: {regime.Regime}");
        hud.AppendLine($"BullScore: {regime.BullTrendScore.TotalScore:0}  BearScore: {regime.BearTrendScore.TotalScore:0}");
        hud.AppendLine($"Bull Top: {FormatPassedComponents(regime.BullTrendScore)}");
        hud.AppendLine($"Bear Top: {FormatPassedComponents(regime.BearTrendScore)}");
        hud.AppendLine($"Last Change: {_lastRegimeChangeText}");
        hud.AppendLine($"Zones: {_activeZoneCount}  Candidates: {_candidateCount}  Confirmed: {_confirmedCount}");
        hud.AppendLine($"Research: active={_researchTrackers.Count} done={_researchOutcomeCount}");
        var activeExecutions = ActiveReplayExecutions();
        var cleanupCount = _replayExecutionsByTradeId.Values.Sum(CountWorkingProtectionOrders);
        var tradingDay = GlobexTradingDayKey(candle.Time);
        var globexLocked = IsGlobexCloseoutLockWindow(candle.Time, out _);
        hud.AppendLine($"TradingDay: {tradingDay:yyyy-MM-dd}  GlobexGate: {(globexLocked ? "LOCKED" : "OPEN")}");
        hud.AppendLine($"ActualExec: {(ActualOrdersEnabled ? "ON" : "OFF")} dataOnly={RichBarDataCollectionOnly || MicrostructureAuditCollectionOnly || FootprintDataCollectionOnly || SweepReclaimDataCollectionOnly || ZoneBehaviorLedgerDataOnly || MarketExecutionTapeDataOnly || CandidateScenarioTapeDataOnly} sent={_replayTradesToday}/{ReplayMaxTradesPerDay} exits={_replayExitsToday} slots={activeExecutions.Count}/2 pos={GetCurrentAccountPosition():0.##} cleanup={cleanupCount}");
        var dailyTarget = _snapshot?.ExecutionProfile.DailyTargetDollars ?? 0m;
        hud.AppendLine($"Today: TP={_replayTpToday} SL={_replaySlToday} Other={_replayOtherExitToday} NetR={_replayDailyR:0.00} Gross=${_replayDailyPnlDollars:0.##} AccountNet=${_liveAccountDailyNetPnlDollars:0.##} target=${dailyTarget:0.##} loss=${_actualDailyLossLimitDollars:0.##}");
        hud.AppendLine($"Week: {_liveAccountPnlWeekStart:MM-dd} AccountNet=${_liveAccountWeeklyNetPnlDollars:0.##} LongNet=${_liveLongWeeklyNetPnlDollars:0.##} longGate={(IsWeeklyLongLossGateActive(out _) ? "BLOCKED" : "OPEN")} limit=-${_actualWeeklyLongLossLimitDollars:0.##}");
        var latency = Connector?.LatencyManager;
        TimeSpan? lastMarketDataSilence = latency?.LastMarketDataReceptionTimeUtc is DateTime lastMarketDataUtc
            ? DateTime.UtcNow - lastMarketDataUtc
            : null;
        hud.AppendLine($"LiveGate: {(_liveReadinessBlocked ? $"BLOCKED({_liveReadinessBlockReason})" : "READY")} latency={(_latencyGateActive ? "BLOCKED" : "OK")} mdMs={LatencyMilliseconds(latency?.MarketDataLatency)} ordMs={LatencyMilliseconds(latency?.OrdersLatency)} silentMs={LatencyMilliseconds(lastMarketDataSilence)} serverOco={Connector?.IsSupportedServerOCO}");
        if (_replayAbnormalEntryToday > 0)
            hud.AppendLine($"AbnormalEntry: count={_replayAbnormalEntryToday} last={_lastAbnormalEntryHudText}");
        if (_replayAbnormalProtectiveFillToday > 0)
            hud.AppendLine($"AbnormalProtective: count={_replayAbnormalProtectiveFillToday} last={_lastAbnormalProtectiveFillHudText}");
        hud.AppendLine($"ExecLosses: full={_replayFullLossTradesToday} consec={_replayConsecutiveLossesToday}");
        if (activeExecutions.Count == 0)
            hud.AppendLine("ActiveOrders: -");
        else
            foreach (var execution in activeExecutions)
                hud.AppendLine($"{execution.Lane}: {BuildActiveExecutionHudLine(execution)}");
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

        return $"{execution.TradeId} {side} {execution.ResearchPath} E={entry:0.00} SL={execution.Stop:0.00} TP={execution.Target:0.00} rem={RemainingExecutionQuantity(execution):0.##}/{execution.Quantity:0.##}";
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
        const int padX = 10;
        const int padY = 8;
        const int minBoxW = 560;
        var maxBoxW = Math.Max(1, ChartArea.Width - padX * 2);
        var boundedHud = WrapHudText(context, font, hud, maxBoxW - padX * 2);
        var size = context.MeasureString(boundedHud, font);
        var boxW = Math.Min(maxBoxW, Math.Max(Math.Min(minBoxW, maxBoxW), (int)Math.Ceiling((double)size.Width) + padX * 2));
        var boxH = (int)Math.Ceiling((double)size.Height) + padY * 2;
        var x = ChartArea.X + (ChartArea.Width - boxW) / 2;
        var y = ChartArea.Y + 8;
        var rect = new Rectangle(x, y, boxW, boxH);

        context.FillRectangle(Color.FromArgb(125, 0, 0, 0), rect);
        context.DrawRectangle(new RenderPen(Color.FromArgb(170, 70, 70, 70), 1), rect);
        context.DrawString(boundedHud, font, Color.DeepSkyBlue, x + padX, y + padY);
    }

    private static string WrapHudText(RenderContext context, RenderFont font, string value, int maxLineWidth)
    {
        if (string.IsNullOrEmpty(value) || maxLineWidth <= 0)
            return value;

        var lines = new List<string>();
        foreach (var line in value.Replace("\r", string.Empty).Split('\n'))
        {
            if (line.Length == 0)
            {
                lines.Add(string.Empty);
                continue;
            }

            for (var start = 0; start < line.Length;)
            {
                var length = line.Length - start;
                while (length > 1)
                {
                    var candidate = line.Substring(start, length);
                    if (context.MeasureString(candidate, font).Width > maxLineWidth)
                    {
                        length--;
                        continue;
                    }

                    break;
                }

                lines.Add(line.Substring(start, length));
                start += length;
            }
        }

        return string.Join(Environment.NewLine, lines);
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

    private sealed record DeferredContinuationCandidate(
        CandidateSignal Signal,
        string ResearchPath,
        decimal Risk,
        string ParentTradeId,
        string ShadowKey,
        long CaptureSequence);

    private static bool IsBreakawayRetestResearchPath(string researchPath)
    {
        return researchPath is "BreakawayRetest" or "BreakawayRetest12Research" or "BreakawayRetest18Research";
    }

    private static string[] BreakawayRetestLogReasons(PendingBreakawayRetest pending, params string[] reasons)
    {
        return pending.ResearchOnly
            ? reasons.Concat(new[] { $"WindowBars={pending.WindowBars}" }).ToArray()
            : reasons;
    }

    private sealed record PendingBreakawayRetest(
        CandidateSignal Signal,
        int MaxRetestBar,
        string ResearchPath,
        int WindowBars,
        bool ResearchOnly)
    {
        public bool Touched { get; set; }
        public bool WaitingTouchLogged { get; set; }
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

    private sealed record PendingAggressiveExpansionWait1(
        CandidateSignal Signal,
        OpfCandle OriginCandle,
        string ResearchPath,
        decimal OriginalStop,
        decimal OriginalRisk);

    private sealed record PendingConfirmedRetrace(CandidateSignal Signal, OpfCandle ConfirmCandle, int MaxRetraceBar);

    private sealed record PendingFailureReverseRetest(CandidateSignal Signal, int MaxRetestBar)
    {
        public bool Touched { get; set; }
    }

    private sealed record PendingSignificantZoneFirstTouch(
        CandidateSignal Signal,
        SignificantZoneFirstTouchEntry Entry);

    private sealed record PendingFailureReversePreEntryWideShort(
        CandidateSignal Signal,
        OpfCandle OriginCandle,
        decimal PenetrationR,
        LegacyFailureReverseWideStopAdmission LegacyAdmission)
    {
        public string LegacySignalId => Signal.SignalId;
    }

    private sealed record LegacyFailureReverseWideStopAdmission(
        bool LegacyWouldSubmit,
        string[] Reasons,
        string LegacyResearchPath,
        decimal PlannedEntry,
        decimal PlannedStop,
        decimal PlannedRisk);

    private sealed record PendingProtectedSecondaryV216(
        string PrimaryTradeId,
        CandidateSignal Signal,
        OpfCandle EntryCandle,
        string ResearchPath,
        decimal Stop,
        decimal Risk);

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

    private sealed record SplitExecutionFinalization(
        decimal ExitPrice,
        string ExitRole,
        string DailyRole,
        ExecutionFillValidation Validation);

    private sealed record ExitPolicyResult(
        string ExitReason,
        decimal ExitPrice,
        decimal PnlPoints,
        decimal PnlR,
        bool Ambiguous,
        int ExitBar);

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
        public string Lane { get; set; } = "Primary";
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
        public decimal RawEntryAvgPrice { get; set; }
        public bool HistoricalFavorableEntryNormalizedV180 { get; set; }
        public bool EntrySubmissionAborted { get; set; }
        public bool AbnormalEntryQuarantined { get; set; }
        public bool AbnormalProtectiveFillQuarantined { get; set; }
        public string AbnormalProtectiveFillReason { get; set; } = string.Empty;
        public bool AbnormalSafetyFlattenV182 { get; set; }
        public string AbnormalSafetyFlattenReasonV182 { get; set; } = string.Empty;
        public bool DailyCountersRolledBack { get; set; }
        public bool CountedAsObservationFiller { get; set; }
        public bool CountedAsQualityRescue { get; set; }
        public bool CountedAsWideStopLongExpansion { get; set; }
        public string? OcoGroup { get; set; }
        public Order? StopOrder { get; set; }
        public Order? TargetOrder { get; set; }
        public bool ZoneBirthSplitRunnerV172 { get; set; }
        public bool IsDeferredContinuation { get; set; }
        public string DeferredParentTradeId { get; set; } = string.Empty;
        public string? RunnerOcoGroup { get; set; }
        public Order? RunnerStopOrder { get; set; }
        public Order? RunnerTargetOrder { get; set; }
        public HashSet<string> HistoricalVirtualTargetRolesV220 { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> HistoricalVirtualTargetActivationPendingV220 { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> HistoricalObservedTargetExitRolesV206 { get; } = new(StringComparer.OrdinalIgnoreCase);
        public decimal BaseExitFilledQty { get; set; }
        public decimal RunnerExitFilledQty { get; set; }
        public string? BaseExitRole { get; set; }
        public string? RunnerExitRole { get; set; }
        public decimal SplitNormalPointsQuantity { get; set; }
        public decimal SplitRawPointsQuantity { get; set; }
        public decimal SplitNormalDollars { get; set; }
        public decimal SplitRawDollars { get; set; }
        public decimal SplitLoggedExitValue { get; set; }
        public decimal SplitRawExitValue { get; set; }
        public bool SplitIsAbnormal { get; set; }
        public string SplitValidationReason { get; set; } = string.Empty;
        public decimal RunnerStop { get; set; }
        public decimal RunnerTarget { get; set; }
        public bool RunnerBreakEvenEvaluated { get; set; }
        public bool RunnerBreakEvenPending { get; set; }
        public bool RunnerBreakEvenApplied { get; set; }
        public int? RunnerBreakEvenTriggerBar { get; set; }
        public decimal RunnerPendingStop { get; set; }
        public bool AccountFlatReconciliationPending { get; set; }
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
        public decimal GlobexCloseoutReferencePriceV184 { get; set; }
        public bool ProtectBreakEvenEvaluatedV186 { get; set; }
        public bool ProtectBreakEvenPendingV186 { get; set; }
        public bool ProtectBreakEvenAppliedV186 { get; set; }
        public bool DeferredBreakEvenArmedV208 { get; set; }
        public int? ProtectBreakEvenTriggerBarV186 { get; set; }
        public decimal ProtectPendingStopV186 { get; set; }
        public int ProtectBreakEvenLastMarketCrossedBarV187 { get; set; } = -1;
        public bool ProtectiveExitCallbackPendingV187 { get; set; }
        public bool ProtectiveExitCallbackCheckScheduledV187 { get; set; }
        public int ProtectionCleanupAttempts { get; set; }
        public int LastProtectionCleanupBar { get; set; } = -1;
        public string ProtectionCleanupReason { get; set; } = string.Empty;
        public bool EmergencyFlattenSubmitted { get; set; }
        public bool TimeStopExitPendingV208 { get; set; }
        public Order? TimeStopOrderV208 { get; set; }
        public decimal TimeStopReferencePriceV209 { get; set; }
        public bool EmergencyFlattenOrderSubmitted { get; set; }
        public Order? EmergencyFlattenOrder { get; set; }
        public decimal EmergencyFlattenReferencePriceV205 { get; set; }
        public bool EmergencyFlattenResidualCheckPending { get; set; }
        public decimal EmergencyFlattenSubmittedQty { get; set; }
        public bool DuplicateExitFlattenSubmitted { get; set; }
        public decimal ActualMfePoints { get; private set; }
        public decimal ActualMaePoints { get; private set; }
        public bool ProtectionObservationStarted { get; private set; }
        public decimal ProtectiveObservedLow { get; private set; }
        public decimal ProtectiveObservedHigh { get; private set; }
        public decimal ActualMfeR => InitialRiskPoints <= 0m ? 0m : Math.Round(ActualMfePoints / InitialRiskPoints, 4);
        public decimal ActualMaeR => InitialRiskPoints <= 0m ? 0m : Math.Round(ActualMaePoints / InitialRiskPoints, 4);

        public void StartProtectionObservation(decimal marketPrice)
        {
            if (ProtectionObservationStarted || marketPrice <= 0m)
                return;

            ProtectionObservationStarted = true;
            ProtectiveObservedLow = marketPrice;
            ProtectiveObservedHigh = marketPrice;
        }

        public void ObserveProtectivePrice(decimal marketPrice)
        {
            if (!ProtectionObservationStarted || marketPrice <= 0m)
                return;

            ProtectiveObservedLow = Math.Min(ProtectiveObservedLow, marketPrice);
            ProtectiveObservedHigh = Math.Max(ProtectiveObservedHigh, marketPrice);
        }

        public void ObserveProtectiveRange(decimal low, decimal high)
        {
            if (!ProtectionObservationStarted || low <= 0m || high <= 0m)
                return;

            ProtectiveObservedLow = Math.Min(ProtectiveObservedLow, low);
            ProtectiveObservedHigh = Math.Max(ProtectiveObservedHigh, high);
        }

        public bool WasProtectivePriceObserved(string role, decimal expectedPrice, decimal tolerance)
        {
            if (!ProtectionObservationStarted || expectedPrice <= 0m)
                return false;

            if (IsTargetExitRole(role))
                return Side == TradeSide.Long
                    ? ProtectiveObservedHigh + tolerance >= expectedPrice
                    : ProtectiveObservedLow - tolerance <= expectedPrice;

            if (IsStopExitRole(role))
                return Side == TradeSide.Long
                    ? ProtectiveObservedLow - tolerance <= expectedPrice
                    : ProtectiveObservedHigh + tolerance >= expectedPrice;

            return false;
        }

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

    private sealed class ShadowTradeTracker
    {
        public ShadowTradeTracker(string key, ResearchTracker tracker)
        {
            Key = key;
            Tracker = tracker;
        }

        public string Key { get; }
        public ResearchTracker Tracker { get; }
        public string OriginalDecision { get; private set; } = string.Empty;
        public string OriginalReason { get; private set; } = string.Empty;
        public string OriginalTradeId { get; private set; } = string.Empty;

        public void SetOriginalDecision(string decision, string reason, string tradeId)
        {
            OriginalDecision = decision;
            OriginalReason = reason;
            OriginalTradeId = tradeId;
        }
    }

    private sealed class DecisionTapeCalibrationTracker
    {
        private readonly List<OpfCandle> _bars = new();

        public DecisionTapeCalibrationTracker(
            string key,
            ResearchTracker tracker,
            OpfCandle entryCandle,
            decimal entryBid,
            decimal entryAsk)
        {
            Key = key;
            Tracker = tracker;
            EntryBid = entryBid;
            EntryAsk = entryAsk;
            _bars.Add(entryCandle);
        }

        public string Key { get; }
        public ResearchTracker Tracker { get; }
        public decimal EntryBid { get; private set; }
        public decimal EntryAsk { get; private set; }
        public long EntryMarketSequence { get; private set; }
        public IReadOnlyList<OpfCandle> Bars => _bars;
        public string OriginalDecision { get; private set; } = string.Empty;
        public string OriginalReason { get; private set; } = string.Empty;
        public string OriginalTradeId { get; private set; } = string.Empty;

        public void SetOriginalDecision(string decision, string reason, string tradeId)
        {
            OriginalDecision = decision;
            OriginalReason = reason;
            OriginalTradeId = tradeId;
        }

        public void SetEntryQuote(decimal bid, decimal ask)
        {
            EntryBid = bid;
            EntryAsk = ask;
        }

        public void SetEntryMarketSequence(long sequence)
        {
            EntryMarketSequence = sequence;
        }

        public void AppendBar(OpfCandle candle)
        {
            if (_bars.Count > 0 && _bars[^1].Bar == candle.Bar)
                _bars[^1] = candle;
            else
                _bars.Add(candle);
        }
    }

    private readonly record struct ShadowTradeResult(
        string Policy,
        string ExitReason,
        decimal ExitPrice,
        decimal PnlPoints,
        decimal PnlR,
        int ExitBar,
        bool Ambiguous);

    private readonly record struct DecisionTapeMarketTurn(
        long Sequence,
        int Bar,
        decimal Price,
        string Kind);

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
        public int? First4RBar { get; private set; }
        public int? FirstBreakEvenAfter1_5RBar { get; private set; }
        public int? First1RLockAfter1_5RBar { get; private set; }
        public int? FirstBreakEvenAfter0_75RBar { get; private set; }
        public int? FirstBreakEvenAfter1RBar { get; private set; }
        public int? FirstBreakEvenAfter2_5RBar { get; private set; }
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
            var had2_5RBeforeThisBar = First2_5RBar.HasValue && candle.Bar > First2_5RBar.Value;
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
                if (had2_5RBeforeThisBar && candle.Low <= Entry)
                    FirstBreakEvenAfter2_5RBar ??= candle.Bar;
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
                if (had2_5RBeforeThisBar && candle.High >= Entry)
                    FirstBreakEvenAfter2_5RBar ??= candle.Bar;
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

            if (currentMfe >= 4m * InitialRiskPoints)
                First4RBar ??= candle.Bar;

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
