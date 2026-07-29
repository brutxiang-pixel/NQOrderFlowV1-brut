using OPFStrategyV1.Core.Snapshots;
using OPFStrategyV1.Core.Signals;
using OPFStrategyV1.Core.MarketData;
using OPFStrategyV1.Regime;
using OPFStrategyV1.Zones;
using System.Globalization;
using System.Text.Json;

namespace OPFStrategyV1.Research;

public sealed class ResearchLogger
{
    private readonly string _directory;
    private readonly Dictionary<string, ConfigSnapshot> _snapshots = new();
    private readonly bool _compact;
    private readonly object _fileWriteSync = new();
    private readonly Dictionary<string, List<string>> _bufferedShadowTradeRows = new();
    private readonly Dictionary<string, List<string>> _bufferedDecisionTapeCalibrationRows = new();
    private readonly Dictionary<string, List<string>> _bufferedDecisionTapeCalibrationBarRows = new();
    private readonly Dictionary<string, List<string>> _bufferedDecisionTapeMarketTurnRows = new();
    private readonly Dictionary<string, List<string>> _bufferedKnnShadowDecisionRows = new();
    private readonly Dictionary<string, List<string>> _bufferedRichBarFeatureRows = new();
    private readonly Dictionary<string, List<string>> _bufferedMicrostructureAuditRows = new();
    private readonly Dictionary<string, List<string>> _bufferedMicrostructureAuditSampleRows = new();
    private readonly Dictionary<string, List<string>> _bufferedFootprintFeatureRows = new();
    private readonly Dictionary<string, List<string>> _bufferedSweepReclaimCandidateRows = new();
    private readonly Dictionary<string, List<string>> _bufferedSweepReclaimOutcomeRows = new();

    public sealed record ActualOutcome(
        bool ActualVerified,
        string ActualTradeId,
        DateTime? ActualExitTime,
        int? ActualExitBar,
        decimal? ActualExitPrice,
        string ActualExitRole,
        decimal ActualPnLPoints,
        decimal ActualPnLDollars,
        decimal ActualPnLR,
        decimal ActualMFEPoints,
        decimal ActualMAEPoints,
        decimal ActualMFE_R,
        decimal ActualMAE_R);

    public sealed record RegimeDailySummary(
        string SnapshotId,
        DateTime Date,
        int TotalBars,
        int BullTrendBars,
        int BearTrendBars,
        int UnknownBars,
        int RegimeChangeCount,
        decimal AvgBullScore,
        decimal AvgBearScore);

    public sealed record RichBarFeature(
        DateTime Time,
        int Bar,
        string Regime,
        int RegimeBars,
        decimal Open,
        decimal High,
        decimal Low,
        decimal Close,
        decimal Volume,
        decimal Vwap,
        decimal AverageVolume20,
        decimal RelativeVolume20,
        decimal CloseMinusVwap,
        decimal Atr14,
        decimal VwapDistanceAtr,
        decimal BullScore,
        decimal BearScore,
        string BullComponents,
        string BearComponents);

    public sealed record MicrostructureAuditBar(
        int Bar,
        string Source,
        long FirstSequence,
        long LastSequence,
        long FirstSourceSequence,
        long LastSourceSequence,
        DateTime? FirstTime,
        DateTime? LastTime,
        int EventCount,
        decimal TotalVolume,
        decimal BidVolume,
        decimal AskVolume,
        decimal UnknownVolume,
        string DirectionSummary,
        string DataTypeSummary,
        int NonMonotonicTimeCount,
        decimal MinPrice,
        decimal MaxPrice,
        decimal MinOriginPrice,
        decimal MaxOriginPrice,
        int PriceOriginDifferenceCount,
        int NonPositivePriceCount,
        int NonPositiveOriginPriceCount,
        int QuoteReferenceCount,
        int QuoteMissingReferenceCount,
        int QuoteInBandCount,
        int QuoteOutOfBandCount,
        int QuoteSideConsistentCount);

    public sealed record MicrostructureAuditSample(
        int Bar,
        string Source,
        long ArrivalSequence,
        long SourceSequence,
        DateTime EventTime,
        decimal Price,
        decimal OriginPrice,
        decimal Volume,
        bool IsBid,
        bool IsAsk,
        string Direction,
        string DataType,
        string SampleKind);

    public sealed record FootprintCandidateFeature(
        string SignalId,
        DateTime DecisionTime,
        int DecisionBar,
        string Lane,
        string Side,
        string ResearchPath,
        string ZoneId,
        decimal ZoneLow,
        decimal ZoneHigh,
        long TickSequenceBoundary,
        DateTime? ReferenceTickTime,
        bool Tick60WindowComplete,
        bool FootprintHistory15mComplete,
        int Tick30Count,
        decimal BuyVolume30,
        decimal SellVolume30,
        decimal UnknownVolume30,
        int Tick60Count,
        decimal BuyVolume60,
        decimal SellVolume60,
        decimal UnknownVolume60,
        long? ZoneTouchSequence,
        DateTime? ZoneTouchTime,
        decimal ZoneTouchDelta,
        decimal ZoneTouchPriceChange,
        decimal ZoneTouchDeltaPerSecond,
        string PriceDeltaDivergence,
        decimal PocM5_1,
        decimal PocM5_2,
        decimal PocM5_3,
        decimal PocMigration1,
        decimal PocMigration2,
        decimal ZoneVolume,
        decimal OutsideZoneVolume,
        int ZoneObservedPriceLevels,
        int ZoneExpectedPriceLevels,
        decimal ZoneOccupiedLevelRatio,
        decimal ZoneMinObservedLevelVolume);

    public sealed record SweepReclaimCandidateFeature(
        string SignalId,
        DateTime DecisionTime,
        int DecisionBar,
        string Side,
        int LookbackBars,
        decimal RangeLow,
        decimal RangeHigh,
        decimal SweepExtreme,
        decimal SweepDepthPoints,
        decimal DecisionOpen,
        decimal DecisionHigh,
        decimal DecisionLow,
        decimal DecisionClose,
        decimal ReclaimDistancePoints,
        int PlannedEntryBar,
        decimal EstimatedEntry,
        decimal Stop,
        decimal EstimatedRiskPoints,
        bool EstimatedRiskInAuditRange,
        long TickSequenceBoundary,
        DateTime? ReferenceTickTime,
        bool Tick60WindowComplete,
        int Tick30Count,
        decimal BuyVolume30,
        decimal SellVolume30,
        decimal UnknownVolume30,
        int Tick60Count,
        decimal BuyVolume60,
        decimal SellVolume60,
        decimal UnknownVolume60);

    public sealed record SweepReclaimOutcome(
        string SignalId,
        DateTime? EntryTime,
        int? EntryBar,
        decimal Entry,
        decimal Stop,
        decimal Target,
        decimal RiskPoints,
        DateTime? ExitTime,
        int? ExitBar,
        string ExitReason,
        decimal ExitPrice,
        int BarsObserved,
        decimal MfePoints,
        decimal MaePoints,
        bool DeterministicOutcome);

    public ResearchLogger(string strategyName, string logMode = "Full")
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _directory = Path.Combine(root, "ATAS", "StrategyLogs", strategyName);
        Directory.CreateDirectory(_directory);
        _compact = string.Equals(logMode, "Compact", StringComparison.OrdinalIgnoreCase);
    }

    public void WriteConfigSnapshot(ConfigSnapshot snapshot)
    {
        _snapshots[snapshot.SnapshotId] = snapshot;
        var path = Path.Combine(_directory, $"{snapshot.SnapshotId}_ConfigSnapshot.json");
        var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
        WriteText(path, json);
    }

    public void AppendSignalHeader(string snapshotId)
    {
        var path = Path.Combine(_directory, $"{snapshotId}_signals.csv");
        EnsureHeader(path, ContextHeader("SignalID,Time,Bar,Stage,Side,SetupType,ZoneID,ZoneType,ZoneFreshness,RegimeScore,SetupQualityScore,SkipReasons"));
    }

    public void AppendSignal(CandidateSignal signal)
    {
        var path = Path.Combine(_directory, $"{signal.SnapshotId}_signals.csv");
        EnsureHeader(path, ContextHeader("SignalID,Time,Bar,Stage,Side,SetupType,ZoneID,ZoneType,ZoneFreshness,RegimeScore,SetupQualityScore,SkipReasons"));
        AppendText(path,
            string.Join(",",
                ContextValues(signal.SnapshotId),
                Csv(signal.SignalId),
                Csv(signal.Time.ToString("O")),
                signal.Bar,
                Csv(signal.Stage.ToString()),
                Csv(signal.Side.ToString()),
                Csv(signal.SetupType.ToString()),
                Csv(signal.Zone?.ZoneId ?? string.Empty),
                Csv(signal.Zone?.ZoneType ?? string.Empty),
                Csv(signal.Zone?.Freshness ?? string.Empty),
                signal.RegimeScore.TotalScore,
                signal.SetupQualityScore.TotalScore,
                Csv(string.Join("|", signal.SkipReasons)))
            + Environment.NewLine);
    }

    public void AppendZone(string snapshotId, int bar, DateTime time, DetectedZone zone)
    {
        var path = Path.Combine(_directory, $"{snapshotId}_zones.csv");
        EnsureHeader(path, "SnapshotID,Time,Bar,ZoneID,ZoneType,Direction,Low,High,CreatedTime,CreatedBar,Freshness,TouchCount,Mitigated,Source,DetectorVersion");
        AppendText(path,
            string.Join(",",
                Csv(snapshotId),
                Csv(time.ToString("O")),
                bar,
                Csv(zone.ZoneId),
                Csv(zone.ZoneType),
                Csv(zone.Direction),
                zone.Low,
                zone.High,
                Csv(zone.CreatedTime.ToString("O")),
                zone.CreatedBar,
                Csv(zone.Freshness),
                zone.TouchCount,
                zone.Mitigated,
                Csv(zone.Source),
                Csv(zone.DetectorVersion))
            + Environment.NewLine);
    }

    public void AppendCandidateEvaluation(
        string snapshotId,
        DateTime time,
        int bar,
        OpfCandle candle,
        MarketRegime regime,
        DetectedZone zone,
        string result,
        string reason,
        string pullbackEpisodeId = "",
        int pullbackCountInRegime = 0,
        int pullbackStartBar = 0,
        string pullbackStatus = "")
    {
        var path = Path.Combine(_directory, $"{snapshotId}_candidate_evaluations.csv");
        EnsureHeader(path, "SnapshotID,Time,Bar,Open,High,Low,Close,Regime,ZoneID,ZoneType,ZoneDirection,ZoneLow,ZoneHigh,ZoneWidth,CreatedBar,ZoneFreshness,TouchCount,Mitigated,Result,Reason,PullbackEpisodeID,PullbackCountInRegime,PullbackStartBar,PullbackStatus");
        AppendText(path,
            string.Join(",",
                Csv(snapshotId),
                Csv(time.ToString("O")),
                bar,
                candle.Open,
                candle.High,
                candle.Low,
                candle.Close,
                Csv(regime.ToString()),
                Csv(zone.ZoneId),
                Csv(zone.ZoneType),
                Csv(zone.Direction),
                zone.Low,
                zone.High,
                zone.High - zone.Low,
                zone.CreatedBar,
                Csv(zone.Freshness),
                zone.TouchCount,
                zone.Mitigated,
                Csv(result),
                Csv(reason),
                Csv(pullbackEpisodeId),
                pullbackCountInRegime,
                pullbackStartBar,
                Csv(pullbackStatus))
            + Environment.NewLine);

        AppendFunnelEvent(snapshotId, time, bar, "ZoneTouch", string.Empty, string.Empty, zone.Direction, string.Empty, result, reason);
    }

    public void AppendConfirmationEvaluation(
        string snapshotId,
        string signalId,
        DateTime time,
        int bar,
        OpfCandle candle,
        DetectedZone? zone,
        OpfCandle? previous,
        string stage,
        string result,
        string reason)
    {
        var path = Path.Combine(_directory, $"{snapshotId}_confirmation_evaluations.csv");
        EnsureHeader(path, "SnapshotID,SignalID,Time,Bar,Open,High,Low,Close,ZoneLow,ZoneHigh,ZoneWidth,PrevHigh,PrevLow,Stage,Result,Reason");
        AppendText(path,
            string.Join(",",
                Csv(snapshotId),
                Csv(signalId),
                Csv(time.ToString("O")),
                bar,
                candle.Open,
                candle.High,
                candle.Low,
                candle.Close,
                zone?.Low.ToString() ?? string.Empty,
                zone?.High.ToString() ?? string.Empty,
                zone is null ? string.Empty : (zone.High - zone.Low).ToString(),
                previous?.High.ToString() ?? string.Empty,
                previous?.Low.ToString() ?? string.Empty,
                Csv(stage),
                Csv(result),
                Csv(reason))
            + Environment.NewLine);

        AppendFunnelEvent(snapshotId, time, bar, "Confirmation", signalId, string.Empty, string.Empty, string.Empty, result, reason);
    }

    public void AppendResearchOutcome(
        string snapshotId,
        string signalId,
        DateTime entryTime,
        int entryBar,
        DateTime exitTime,
        int exitBar,
        string side,
        decimal entry,
        decimal stop,
        decimal initialRiskPoints,
        decimal mfePoints,
        decimal maePoints,
        decimal mfeR,
        decimal maeR,
        bool hit1R,
        bool hit1_5R,
        bool hit2R,
        bool hit2_5R,
        bool hit3R,
        int barsTracked,
        string researchPath,
        int? firstStopBar,
        int? first1RBar,
        int? first1_5RBar,
        int? first2RBar,
        int? first2_5RBar,
        int? first3RBar,
        bool stopHitBefore1R,
        bool ambiguousStopAndTargetSameBar,
        int? timeTo1RMinutes,
        int? timeToMfeMinutes,
        decimal maxHeatBefore1R,
        string stopBasis,
        int entryDelayBars,
        string outcomeClass,
        string exitReason,
        ActualOutcome? actualOutcome = null)
    {
        var path = Path.Combine(_directory, $"{snapshotId}_research_outcomes.csv");
        var sizing = ResearchSizing(snapshotId, initialRiskPoints, mfePoints, maePoints);
        var execution = ExecutionResearch(snapshotId);
        actualOutcome ??= new ActualOutcome(false, string.Empty, null, null, null, string.Empty, 0m, 0m, 0m, 0m, 0m, 0m, 0m);
        var efficiencyMfeR = actualOutcome.ActualVerified ? actualOutcome.ActualMFE_R : mfeR;
        var efficiencyMfePoints = actualOutcome.ActualVerified ? actualOutcome.ActualMFEPoints : mfePoints;
        var exitEfficiency = actualOutcome.ActualVerified && efficiencyMfeR > 0m ? Math.Round(actualOutcome.ActualPnLR / efficiencyMfeR, 4) : 0m;
        var runupCapturePct = actualOutcome.ActualVerified && efficiencyMfePoints > 0m ? Math.Round(actualOutcome.ActualPnLPoints / efficiencyMfePoints * 100m, 2) : 0m;
        var adverseBeforeProfitR = initialRiskPoints <= 0m ? 0m : Math.Round(maxHeatBefore1R / initialRiskPoints, 4);
        var resolvedOutcomeClass = actualOutcome.ActualVerified ? ActualOutcomeClass(actualOutcome.ActualExitRole, actualOutcome.ActualPnLR) : outcomeClass;
        var outcomeSource = actualOutcome.ActualVerified ? "ActualTrade" : "ResearchOHLC";
        EnsureResearchOutcomeHeader(path);
        AppendText(path,
            string.Join(",",
                ContextValues(snapshotId),
                Csv(signalId),
                Csv(entryTime.ToString("O")),
                entryBar,
                Csv(exitTime.ToString("O")),
                exitBar,
                Csv(side),
                entry,
                stop,
                initialRiskPoints,
                sizing.PointValue,
                sizing.TickSize,
                sizing.TickValue,
                sizing.PlannedContracts,
                sizing.ActualContracts,
                sizing.RiskPerContractDollars,
                sizing.TotalInitialRiskDollars,
                mfePoints,
                maePoints,
                sizing.MfeDollars,
                sizing.MaeDollars,
                mfeR,
                maeR,
                hit1R,
                hit1_5R,
                hit2R,
                hit2_5R,
                hit3R,
                barsTracked,
                Csv(researchPath),
                firstStopBar?.ToString() ?? string.Empty,
                first1RBar?.ToString() ?? string.Empty,
                first1_5RBar?.ToString() ?? string.Empty,
                first2RBar?.ToString() ?? string.Empty,
                first2_5RBar?.ToString() ?? string.Empty,
                first3RBar?.ToString() ?? string.Empty,
                stopHitBefore1R,
                ambiguousStopAndTargetSameBar,
                ambiguousStopAndTargetSameBar,
                timeTo1RMinutes?.ToString() ?? string.Empty,
                timeToMfeMinutes?.ToString() ?? string.Empty,
                maxHeatBefore1R,
                Csv(stopBasis),
                entryDelayBars,
                Csv(outcomeClass),
                Csv(exitReason),
                exitEfficiency,
                runupCapturePct,
                adverseBeforeProfitR,
                actualOutcome.ActualVerified,
                Csv(actualOutcome.ActualTradeId),
                actualOutcome.ActualExitTime.HasValue ? Csv(actualOutcome.ActualExitTime.Value.ToString("O")) : Csv(string.Empty),
                actualOutcome.ActualExitBar?.ToString() ?? string.Empty,
                actualOutcome.ActualExitPrice?.ToString() ?? string.Empty,
                Csv(actualOutcome.ActualExitRole),
                actualOutcome.ActualPnLPoints,
                actualOutcome.ActualPnLDollars,
                actualOutcome.ActualPnLR,
                actualOutcome.ActualMFEPoints,
                actualOutcome.ActualMAEPoints,
                actualOutcome.ActualMFE_R,
                actualOutcome.ActualMAE_R,
                Csv(resolvedOutcomeClass),
                Csv(outcomeSource),
                execution.WouldTradeLive,
                execution.ResearchOnlySignal,
                execution.SkippedByDailyGuard,
                Csv(execution.ExecutionSkipReasons),
                execution.DailyTargetDollars,
                execution.DailyLossLimitDollars,
                execution.MaxContracts)
            + Environment.NewLine);

        AppendFunnelEvent(snapshotId, entryTime, entryBar, "ResearchOutcome", signalId, researchPath, side, string.Empty, outcomeClass, exitReason);
    }

    public void AppendEdgeAttribution(
        CandidateSignal signal,
        DateTime eventTime,
        int eventBar,
        string researchPath,
        string eventType,
        decimal entry,
        decimal stop,
        decimal initialRiskPoints,
        decimal estimatedRewardPoints,
        string rewardModel,
        decimal estimatedRr,
        string outcomeClass,
        string exitReason,
        bool actualVerified,
        string actualExitRole,
        decimal actualPnlR,
        decimal actualPnlDollars)
    {
        var path = Path.Combine(_directory, $"{signal.SnapshotId}_edge_attribution.csv");
        EnsureHeader(path, ContextHeader("SignalID,EventTime,EventBar,EventType,Side,SetupType,ResearchPath,RegimeScore,RegimeBucket,SetupQualityScore,SetupQualityBucket,ZoneID,ZoneType,ZoneFreshness,ZoneTouchCount,RiskPoints,RiskBucket,EstimatedRewardPoints,RewardModel,EstimatedRR,EstimatedRRBucket,TimeBucket,OutcomeClass,ExitReason,ActualVerified,ActualExitRole,ActualPnL_R,ActualPnLDollars,Entry,Stop,SkipReasons"));
        AppendText(path,
            string.Join(",",
                ContextValues(signal.SnapshotId),
                Csv(signal.SignalId),
                Csv(eventTime.ToString("O")),
                eventBar,
                Csv(eventType),
                Csv(signal.Side.ToString()),
                Csv(signal.SetupType.ToString()),
                Csv(researchPath),
                signal.RegimeScore.TotalScore,
                Csv(ScoreBucket(signal.RegimeScore.TotalScore, signal.RegimeScore.Threshold)),
                signal.SetupQualityScore.TotalScore,
                Csv(SetupQualityBucket(signal.SetupQualityScore.TotalScore)),
                Csv(signal.Zone?.ZoneId ?? string.Empty),
                Csv(signal.Zone?.ZoneType ?? string.Empty),
                Csv(signal.Zone?.Freshness ?? string.Empty),
                signal.Zone?.TouchCount.ToString() ?? string.Empty,
                initialRiskPoints,
                Csv(RiskBucket(initialRiskPoints)),
                estimatedRewardPoints,
                Csv(rewardModel),
                estimatedRr,
                Csv(RrBucket(estimatedRr)),
                Csv(TimeBucket(eventTime)),
                Csv(outcomeClass),
                Csv(exitReason),
                actualVerified,
                Csv(actualExitRole),
                actualPnlR,
                actualPnlDollars,
                entry,
                stop,
                Csv(string.Join("|", signal.SkipReasons)))
            + Environment.NewLine);
    }

    private void EnsureResearchOutcomeHeader(string path)
    {
        EnsureHeader(path, ContextHeader("SignalID,EntryTime,EntryBar,ExitTime,ExitBar,Side,Entry,Stop,InitialRiskPoints,PointValue,TickSize,TickValue,PlannedContracts,ActualContracts,RiskPerContractDollars,TotalInitialRiskDollars,MFEPoints,MAEPoints,MFE_Dollars,MAE_Dollars,MFE_R,MAE_R,Hit1R,Hit1_5R,Hit2R,Hit2_5R,Hit3R,BarsTracked,ResearchPath,FirstStopBar,First1RBar,First1_5RBar,First2RBar,First2_5RBar,First3RBar,StopHitBefore1R,AmbiguousStopAndTargetSameBar,IntraBarAmbiguous,TimeTo1RMinutes,TimeToMFEMinutes,MaxHeatBefore1R,StopBasis,EntryDelayBars,OutcomeClass,ExitReason,ExitEfficiency,RunupCapturePct,AdverseBeforeProfit_R,ActualVerified,ActualTradeID,ActualExitTime,ActualExitBar,ActualExitPrice,ActualExitRole,ActualPnLPoints,ActualPnLDollars,ActualPnL_R,ActualMFEPoints,ActualMAEPoints,ActualMFE_R,ActualMAE_R,ResolvedOutcomeClass,OutcomeSource,WouldTradeLive,ResearchOnlySignal,SkippedByDailyGuard,ExecutionSkipReasons,DailyTargetDollars,DailyLossLimitDollars,MaxContracts"));
    }

    private static string ActualOutcomeClass(string exitRole, decimal pnlR)
    {
        if (string.Equals(exitRole, "TP", StringComparison.OrdinalIgnoreCase) || pnlR >= 2m)
            return "Excellent";
        if (pnlR >= 1m)
            return "Good";
        if (pnlR >= 0m)
            return "Scratch";
        return string.Equals(exitRole, "SL", StringComparison.OrdinalIgnoreCase) ? "Catastrophic" : "Poor";
    }

    public void AppendScoreBreakdown(string snapshotId, DateTime time, int bar, string side, Core.Scoring.ScoreBreakdown score)
    {
        if (_compact)
            return;

        var path = Path.Combine(_directory, $"{snapshotId}_score_breakdown.csv");
        EnsureHeader(path, "SnapshotID,Time,Bar,Side,ScoreName,TotalScore,Threshold,Passed,Component,RawValue,ComponentPassed,Weight,Contribution,Reason");

        foreach (var c in score.Components)
        {
            AppendText(path,
                string.Join(",",
                    Csv(snapshotId),
                    Csv(time.ToString("O")),
                    bar,
                    Csv(side),
                    Csv(score.ScoreName),
                    score.TotalScore,
                    score.Threshold,
                    score.Passed,
                    Csv(c.Name),
                    Csv(c.RawValue),
                    c.Passed,
                    c.Weight,
                    c.Contribution,
                    Csv(c.Reason))
                + Environment.NewLine);
        }
    }

    public void AppendRiskEvaluation(
        string snapshotId,
        string signalId,
        DateTime time,
        int bar,
        string side,
        string setupType,
        string researchPath,
        decimal entry,
        decimal stop,
        decimal initialRiskPoints,
        decimal maxRiskPointsHard,
        decimal atr14,
        decimal atrRiskMultiplier,
        decimal maxAllowedRiskPoints,
        decimal estimatedRewardPoints,
        string rewardModel,
        decimal estimatedRr,
        decimal minEstimatedRr)
    {
        var path = Path.Combine(_directory, $"{snapshotId}_risk_evaluations.csv");
        EnsureHeader(path, ContextHeader("SignalID,Time,Bar,Side,SetupType,ResearchPath,Entry,Stop,InitialRiskPoints,MaxRiskPointsHard,ATR14,AtrRiskMultiplier,MaxAllowedRiskPoints,RiskPassed,EstimatedRewardPoints,RewardModel,EstimatedRR,MinEstimatedRR,EstimatedRRPassed,RR_GE_1_0,RR_GE_1_2,RR_GE_1_5,SkipReasonsIfApplied"));

        var riskPassed = maxAllowedRiskPoints <= 0m || initialRiskPoints <= maxAllowedRiskPoints;
        var rrPassed = estimatedRr >= minEstimatedRr;
        var rrGe1 = estimatedRr >= 1.0m;
        var rrGe1_2 = estimatedRr >= 1.2m;
        var rrGe1_5 = estimatedRr >= 1.5m;
        var skipReasons = new List<string>();
        if (maxRiskPointsHard > 0m && initialRiskPoints > maxRiskPointsHard)
            skipReasons.Add("RiskTooWideHard");
        if (!riskPassed)
            skipReasons.Add("RiskTooWideVolAdjusted");
        if (!rrPassed)
            skipReasons.Add("EstimatedRRTooLow");

        AppendText(path,
            string.Join(",",
                ContextValues(snapshotId),
                Csv(signalId),
                Csv(time.ToString("O")),
                bar,
                Csv(side),
                Csv(setupType),
                Csv(researchPath),
                entry,
                stop,
                initialRiskPoints,
                maxRiskPointsHard,
                atr14,
                atrRiskMultiplier,
                maxAllowedRiskPoints,
                riskPassed,
                estimatedRewardPoints,
                Csv(rewardModel),
                estimatedRr,
                minEstimatedRr,
                rrPassed,
                rrGe1,
                rrGe1_2,
                rrGe1_5,
                Csv(string.Join("|", skipReasons)))
            + Environment.NewLine);
    }

    public void AppendRegimeChange(string snapshotId, DateTime time, int bar, MarketRegime regime, decimal bullScore, decimal bearScore)
    {
        var path = Path.Combine(_directory, $"{snapshotId}_regime_changes.csv");
        EnsureHeader(path, "SnapshotID,Time,Bar,Regime,BullScore,BearScore");
        AppendText(path,
            string.Join(",", Csv(snapshotId), Csv(time.ToString("O")), bar, Csv(regime.ToString()), bullScore, bearScore)
            + Environment.NewLine);
    }

    public void AppendRegimeDailySummary(
        string snapshotId,
        DateTime date,
        int totalBars,
        int bullTrendBars,
        int bearTrendBars,
        int unknownBars,
        int regimeChangeCount,
        decimal avgBullScore,
        decimal avgBearScore)
    {
        var path = Path.Combine(_directory, $"{snapshotId}_regime_daily.csv");
        EnsureHeader(path, "SnapshotID,Date,TotalBars,BullTrendBars,BearTrendBars,UnknownBars,BullTrendPct,BearTrendPct,UnknownPct,RegimeChangeCount,AvgBullScore,AvgBearScore");
        var bullPct = Percent(bullTrendBars, totalBars);
        var bearPct = Percent(bearTrendBars, totalBars);
        var unknownPct = Percent(unknownBars, totalBars);

        AppendText(path,
            string.Join(",",
                Csv(snapshotId),
                Csv(date.ToString("yyyy-MM-dd")),
                totalBars,
                bullTrendBars,
                bearTrendBars,
                unknownBars,
                bullPct,
                bearPct,
                unknownPct,
                regimeChangeCount,
                avgBullScore,
                avgBearScore)
            + Environment.NewLine);
    }

    public void WriteRegimeDailySummaries(string snapshotId, IReadOnlyList<RegimeDailySummary> summaries)
    {
        var path = Path.Combine(_directory, $"{snapshotId}_regime_daily.csv");
        var header = "SnapshotID,Date,TotalBars,BullTrendBars,BearTrendBars,UnknownBars,BullTrendPct,BearTrendPct,UnknownPct,RegimeChangeCount,AvgBullScore,AvgBearScore";
        var lines = new List<string> { header };
        foreach (var summary in summaries.OrderBy(x => x.Date))
        {
            lines.Add(string.Join(",",
                Csv(summary.SnapshotId),
                Csv(summary.Date.ToString("yyyy-MM-dd")),
                summary.TotalBars,
                summary.BullTrendBars,
                summary.BearTrendBars,
                summary.UnknownBars,
                Percent(summary.BullTrendBars, summary.TotalBars),
                Percent(summary.BearTrendBars, summary.TotalBars),
                Percent(summary.UnknownBars, summary.TotalBars),
                summary.RegimeChangeCount,
                summary.AvgBullScore,
                summary.AvgBearScore));
        }

        WriteText(path, string.Join(Environment.NewLine, lines) + Environment.NewLine);
    }

    public void AppendNoTrade(string snapshotId, string signalId, DateTime time, int bar, string setupStage, IReadOnlyList<string> skipReasons)
    {
        var path = Path.Combine(_directory, $"{snapshotId}_no_trade.csv");
        EnsureHeader(path, ContextHeader("SignalID,Time,Bar,SetupStage,SkipReasons"));
        AppendText(path,
            string.Join(",",
                ContextValues(snapshotId),
                Csv(signalId),
                Csv(time.ToString("O")),
                bar,
                Csv(setupStage),
                Csv(string.Join("|", skipReasons)))
            + Environment.NewLine);

        AppendFunnelEvent(snapshotId, time, bar, setupStage, signalId, string.Empty, string.Empty, string.Empty, "Skipped", string.Join("|", skipReasons));
    }

    public void AppendInfo(string snapshotId, int bar, DateTime time, string message)
    {
        var path = Path.Combine(_directory, $"{snapshotId}_research.log");
        AppendText(path, $"{time:O} bar={bar} {message}{Environment.NewLine}");
    }

    public void AppendExecutionEvent(
        string snapshotId,
        string signalId,
        string tradeId,
        DateTime time,
        int bar,
        string eventName,
        string role,
        string side,
        string researchPath,
        decimal price,
        decimal quantity,
        string message)
    {
        var path = Path.Combine(_directory, $"{snapshotId}_execution_events.csv");
        EnsureHeader(path, ContextHeader("SignalID,TradeID,Time,Bar,Event,Role,Side,ResearchPath,Price,Quantity,Message"));
        AppendText(path,
            string.Join(",",
                ContextValues(snapshotId),
                Csv(signalId),
                Csv(tradeId),
                Csv(time.ToString("O")),
                bar,
                Csv(eventName),
                Csv(role),
                Csv(side),
                Csv(researchPath),
                price,
                quantity,
                Csv(message))
            + Environment.NewLine);
    }

    public void AppendLiveAccountPnl(
        string snapshotId,
        string tradeId,
        DateTime entryTime,
        DateTime exitTime,
        string side,
        string classification,
        decimal quantity,
        decimal entryPrice,
        decimal exitPrice,
        decimal grossPnlDollars,
        decimal commissionDollars,
        decimal netPnlDollars,
        decimal dailyNetPnlDollars,
        decimal rawGrossPnlDollars,
        string pnlSource)
    {
        var path = Path.Combine(_directory, $"{snapshotId}_live_account_pnl.csv");
        EnsureHeader(path, ContextHeader("TradeID,EntryTime,ExitTime,Side,Classification,Quantity,EntryPrice,ExitPrice,GrossPnLDollars,CommissionDollars,NetPnLDollars,DailyNetPnLDollars,RawGrossPnLDollars,PnLSource"));
        AppendText(path,
            string.Join(",",
                ContextValues(snapshotId),
                Csv(tradeId),
                Csv(entryTime.ToString("O")),
                Csv(exitTime.ToString("O")),
                Csv(side),
                Csv(classification),
                quantity,
                entryPrice,
                exitPrice,
                grossPnlDollars,
                commissionDollars,
                netPnlDollars,
                dailyNetPnlDollars,
                rawGrossPnlDollars,
                Csv(pnlSource))
            + Environment.NewLine);
    }

    public void AppendExecutionDecision(
        string snapshotId,
        string signalId,
        DateTime time,
        int bar,
        string decision,
        string reason,
        string side,
        string setupType,
        string researchPath,
        decimal regimeScore,
        decimal setupQualityScore,
        bool strategyEligible,
        decimal entry,
        decimal stop,
        decimal target,
        decimal initialRiskPoints,
        decimal estimatedRewardPoints,
        string rewardModel,
        decimal estimatedRr,
        decimal dailyPnlDollars,
        int dailyTradeCount,
        string tradeId = "")
    {
        var path = Path.Combine(_directory, $"{snapshotId}_execution_decisions.csv");
        var executionScope = ExecutionScope(decision, reason);
        EnsureHeader(path, ContextHeader("SignalID,Time,Bar,Decision,Reason,ExecutionScope,Side,SetupType,ResearchPath,RegimeScore,SetupQualityScore,StrategyEligible,Entry,Stop,Target,InitialRiskPoints,EstimatedRewardPoints,RewardModel,EstimatedRR,DailyPnlDollars,DailyTradeCount,TradeID"));
        AppendText(path,
            string.Join(",",
                ContextValues(snapshotId),
                Csv(signalId),
                Csv(time.ToString("O")),
                bar,
                Csv(decision),
                Csv(reason),
                Csv(executionScope),
                Csv(side),
                Csv(setupType),
                Csv(researchPath),
                regimeScore,
                setupQualityScore,
                strategyEligible,
                entry,
                stop,
                target,
                initialRiskPoints,
                estimatedRewardPoints,
                Csv(rewardModel),
                estimatedRr,
                dailyPnlDollars,
                dailyTradeCount,
                Csv(tradeId))
            + Environment.NewLine);

        AppendFunnelEvent(snapshotId, time, bar, "ExecutionDecision", signalId, researchPath, side, setupType, decision, reason, executionScope);
    }

    public void AppendKnnShadowDecision(
        string snapshotId,
        string signalId,
        DateTime time,
        int bar,
        string side,
        string researchPath,
        KnnShadowDecision decision,
        IReadOnlyList<string> numericFeatureNames,
        IReadOnlyList<double> numericValues,
        IReadOnlyList<string> categoricalFeatureNames,
        IReadOnlyList<string> categoricalValues)
    {
        var path = Path.Combine(_directory, $"{snapshotId}_knn_shadow_decisions.csv");
        var numeric = string.Join(";", numericFeatureNames.Select((name, index) => $"{name}={numericValues[index].ToString("0.########", CultureInfo.InvariantCulture)}"));
        var categorical = string.Join(";", categoricalFeatureNames.Select((name, index) => $"{name}={categoricalValues[index]}"));
        var row = string.Join(",",
            ContextValues(snapshotId),
            Csv(signalId),
            Csv(time.ToString("O")),
            bar,
            Csv(side),
            Csv(researchPath),
            Csv(decision.ModelVersion),
            Csv(decision.ModelSha256),
            Csv(decision.Observed.Policy),
            decision.Observed.ScoreR.ToString("R", CultureInfo.InvariantCulture),
            decision.ObservedGatePassed,
            decision.Observed.ReferenceCount,
            Csv(decision.Conservative.Policy),
            decision.Conservative.ScoreR.ToString("R", CultureInfo.InvariantCulture),
            decision.ConservativeGatePassed,
            decision.Conservative.ReferenceCount,
            Csv(decision.UnavailableReason),
            Csv(numeric),
            Csv(categorical));
        lock (_fileWriteSync)
        {
            if (!_bufferedKnnShadowDecisionRows.TryGetValue(path, out var rows))
            {
                rows = new List<string>();
                _bufferedKnnShadowDecisionRows[path] = rows;
            }
            rows.Add(row);
        }
    }

    public void FlushKnnShadowDecisions()
    {
        Dictionary<string, string[]> buffered;
        lock (_fileWriteSync)
        {
            buffered = _bufferedKnnShadowDecisionRows.ToDictionary(x => x.Key, x => x.Value.ToArray());
            _bufferedKnnShadowDecisionRows.Clear();
        }

        var header = ContextHeader("SignalID,Time,Bar,Side,ResearchPath,ModelVersion,ModelSHA256,ObservedPolicy,ObservedScoreR,ObservedGatePassed,ObservedReferenceCount,ConservativePolicy,ConservativeScoreR,ConservativeGatePassed,ConservativeReferenceCount,UnavailableReason,NumericFeatures,CategoricalFeatures");
        foreach (var item in buffered)
        {
            EnsureHeader(item.Key, header);
            AppendText(item.Key, string.Join(Environment.NewLine, item.Value) + Environment.NewLine);
        }
    }

    public void AppendRichBarFeature(string snapshotId, RichBarFeature feature)
    {
        var path = Path.Combine(_directory, $"{snapshotId}_rich_bar_features.csv");
        var row = string.Join(",",
            ContextValues(snapshotId),
            Csv(feature.Time.ToString("O")),
            feature.Bar,
            Csv(feature.Regime),
            feature.RegimeBars,
            feature.Open,
            feature.High,
            feature.Low,
            feature.Close,
            feature.Volume,
            feature.Vwap,
            feature.AverageVolume20,
            feature.RelativeVolume20,
            feature.CloseMinusVwap,
            feature.Atr14,
            feature.VwapDistanceAtr,
            feature.BullScore,
            feature.BearScore,
            Csv(feature.BullComponents),
            Csv(feature.BearComponents));
        lock (_fileWriteSync)
        {
            if (!_bufferedRichBarFeatureRows.TryGetValue(path, out var rows))
            {
                rows = new List<string>();
                _bufferedRichBarFeatureRows[path] = rows;
            }
            rows.Add(row);
        }
    }

    public void FlushRichBarFeatures()
    {
        Dictionary<string, string[]> buffered;
        lock (_fileWriteSync)
        {
            buffered = _bufferedRichBarFeatureRows.ToDictionary(x => x.Key, x => x.Value.ToArray());
            _bufferedRichBarFeatureRows.Clear();
        }

        var header = ContextHeader("Time,Bar,Regime,RegimeBars,Open,High,Low,Close,Volume,Vwap,AverageVolume20,RelativeVolume20,CloseMinusVwap,Atr14,VwapDistanceAtr,BullScore,BearScore,BullComponents,BearComponents");
        foreach (var item in buffered)
        {
            EnsureHeader(item.Key, header);
            AppendText(item.Key, string.Join(Environment.NewLine, item.Value) + Environment.NewLine);
        }
    }

    public void AppendExecutionTrade(
        string snapshotId,
        string signalId,
        string tradeId,
        DateTime entryTime,
        int entryBar,
        DateTime exitTime,
        int exitBar,
        string side,
        string researchPath,
        decimal quantity,
        decimal entryPrice,
        decimal exitPrice,
        decimal stopPrice,
        decimal targetPrice,
        decimal initialRiskPoints,
        decimal targetR,
        decimal plannedTargetR,
        decimal targetRDrift,
        decimal pointsR,
        string exitRole,
        decimal points,
        decimal dollars,
        decimal riskDollars,
        decimal targetDollars,
        decimal actualMfePoints,
        decimal actualMaePoints,
        decimal actualMfeR,
        decimal actualMaeR,
        decimal plannedRiskPoints,
        decimal filledRiskPoints,
        decimal riskDriftPoints,
        decimal dailyPnlAfterDollars,
        int dailyTradeCount,
        int dailyConsecutiveLosses,
        bool isAbnormalExecution,
        string abnormalReason,
        decimal expectedExitPrice,
        decimal exitPriceDriftPoints,
        decimal rawPoints,
        decimal rawDollars,
        decimal rawPointsR)
    {
        var path = Path.Combine(_directory, $"{snapshotId}_execution_trades.csv");
        EnsureHeader(path, ContextHeader("SignalID,TradeID,EntryTime,EntryBar,ExitTime,ExitBar,Side,ResearchPath,Quantity,EntryPrice,ExitPrice,StopPrice,TargetPrice,InitialRiskPoints,TargetR,PlannedTargetR,TargetRDrift,PointsR,ExitRole,Points,Dollars,RiskDollars,TargetDollars,ActualMFEPoints,ActualMAEPoints,ActualMFE_R,ActualMAE_R,PlannedRiskPoints,FilledRiskPoints,RiskDriftPoints,DailyPnlAfterDollars,DailyTradeCount,DailyConsecutiveLosses,IsAbnormalExecution,AbnormalReason,ExpectedExitPrice,ExitPriceDriftPoints,RawPoints,RawDollars,RawPointsR"));
        AppendText(path,
            string.Join(",",
                ContextValues(snapshotId),
                Csv(signalId),
                Csv(tradeId),
                Csv(entryTime.ToString("O")),
                entryBar,
                Csv(exitTime.ToString("O")),
                exitBar,
                Csv(side),
                Csv(researchPath),
                quantity,
                entryPrice,
                exitPrice,
                stopPrice,
                targetPrice,
                initialRiskPoints,
                targetR,
                plannedTargetR,
                targetRDrift,
                pointsR,
                Csv(exitRole),
                points,
                dollars,
                riskDollars,
                targetDollars,
                actualMfePoints,
                actualMaePoints,
                actualMfeR,
                actualMaeR,
                plannedRiskPoints,
                filledRiskPoints,
                riskDriftPoints,
                dailyPnlAfterDollars,
                dailyTradeCount,
                dailyConsecutiveLosses,
                isAbnormalExecution,
                Csv(abnormalReason),
                expectedExitPrice,
                exitPriceDriftPoints,
                rawPoints,
                rawDollars,
                rawPointsR)
            + Environment.NewLine);
    }

    public void AppendShadowTrade(
        string snapshotId,
        string signalId,
        DateTime entryTime,
        int entryBar,
        DateTime exitTime,
        int exitBar,
        string side,
        string researchPath,
        decimal entry,
        decimal stop,
        decimal initialRiskPoints,
        string policy,
        string exitReason,
        decimal exitPrice,
        decimal pnlPoints,
        decimal pnlR,
        decimal grossDollars,
        decimal commissionDollars,
        decimal netDollars,
        int barsHeld,
        bool ambiguous,
        string originalDecision,
        string originalReason,
        string originalTradeId)
    {
        var path = Path.Combine(_directory, $"{snapshotId}_shadow_trades.csv");
        var row = string.Join(",",
                ContextValues(snapshotId),
                Csv(signalId),
                Csv(entryTime.ToString("O")),
                entryBar,
                Csv(exitTime.ToString("O")),
                exitBar,
                Csv(side),
                Csv(researchPath),
                entry,
                stop,
                initialRiskPoints,
                Csv(policy),
                Csv(exitReason),
                exitPrice,
                pnlPoints,
                pnlR,
                grossDollars,
                commissionDollars,
                netDollars,
                barsHeld,
                ambiguous,
                Csv(originalDecision),
                Csv(originalReason),
                Csv(originalTradeId));
        lock (_fileWriteSync)
        {
            if (!_bufferedShadowTradeRows.TryGetValue(path, out var rows))
            {
                rows = new List<string>();
                _bufferedShadowTradeRows[path] = rows;
            }
            rows.Add(row);
        }
    }

    public void FlushShadowTrades()
    {
        Dictionary<string, string[]> buffered;
        lock (_fileWriteSync)
        {
            buffered = _bufferedShadowTradeRows.ToDictionary(x => x.Key, x => x.Value.ToArray());
            _bufferedShadowTradeRows.Clear();
        }

        var header = ContextHeader("SignalID,EntryTime,EntryBar,ExitTime,ExitBar,Side,ResearchPath,Entry,Stop,InitialRiskPoints,Policy,ExitReason,ExitPrice,PnLPoints,PnL_R,GrossDollars,CommissionDollars,NetDollars,BarsHeld,Ambiguous,OriginalDecision,OriginalReason,OriginalTradeID");
        foreach (var item in buffered)
        {
            EnsureHeader(item.Key, header);
            AppendText(item.Key, string.Join(Environment.NewLine, item.Value) + Environment.NewLine);
        }
    }

    public void AppendDecisionTapeCalibration(
        string snapshotId,
        string signalId,
        DateTime entryTime,
        int entryBar,
        DateTime resolveTime,
        int resolveBar,
        string side,
        string setupType,
        string researchPath,
        decimal regimeScore,
        decimal setupQualityScore,
        decimal entry,
        decimal stop,
        decimal initialRiskPoints,
        decimal entryBid,
        decimal entryAsk,
        bool entryQuoteValid,
        long entryMarketSequence,
        long resolveMarketSequence,
        decimal targetR,
        decimal mfePoints,
        decimal maePoints,
        int? firstStopBar,
        int? first0_75RBar,
        int? first1RBar,
        int? first1_5RBar,
        int? first2RBar,
        int? first2_5RBar,
        int? first3RBar,
        int? first4RBar,
        int? firstBreakEvenAfter0_75RBar,
        int? firstBreakEvenAfter1RBar,
        int? firstBreakEvenAfter1_5RBar,
        int? first1RLockAfter1_5RBar,
        int? firstBreakEvenAfter2_5RBar,
        int barsTracked,
        string completionReason,
        string originalDecision,
        string originalReason,
        string originalTradeId)
    {
        var path = Path.Combine(_directory, $"{snapshotId}_decision_tape_calibration.csv");
        var row = string.Join(",",
            ContextValues(snapshotId),
            Csv(signalId),
            Csv(entryTime.ToString("O")),
            entryBar,
            Csv(resolveTime.ToString("O")),
            resolveBar,
            Csv(side),
            Csv(setupType),
            Csv(researchPath),
            regimeScore,
            setupQualityScore,
            entry,
            stop,
            initialRiskPoints,
            entryBid,
            entryAsk,
            entryQuoteValid,
            entryMarketSequence,
            resolveMarketSequence,
            targetR,
            mfePoints,
            maePoints,
            NullableValue(firstStopBar),
            NullableValue(first0_75RBar),
            NullableValue(first1RBar),
            NullableValue(first1_5RBar),
            NullableValue(first2RBar),
            NullableValue(first2_5RBar),
            NullableValue(first3RBar),
            NullableValue(first4RBar),
            NullableValue(firstBreakEvenAfter0_75RBar),
            NullableValue(firstBreakEvenAfter1RBar),
            NullableValue(firstBreakEvenAfter1_5RBar),
            NullableValue(first1RLockAfter1_5RBar),
            NullableValue(firstBreakEvenAfter2_5RBar),
            barsTracked,
            Csv(completionReason),
            Csv(originalDecision),
            Csv(originalReason),
            Csv(originalTradeId));
        lock (_fileWriteSync)
        {
            if (!_bufferedDecisionTapeCalibrationRows.TryGetValue(path, out var rows))
            {
                rows = new List<string>();
                _bufferedDecisionTapeCalibrationRows[path] = rows;
            }
            rows.Add(row);
        }
    }

    public void AppendMicrostructureAudit(string snapshotId, MicrostructureAuditBar audit)
    {
        var path = Path.Combine(_directory, $"{snapshotId}_microstructure_audit_bars.csv");
        var row = string.Join(",",
            ContextValues(snapshotId),
            audit.Bar,
            Csv(audit.Source),
            audit.FirstSequence,
            audit.LastSequence,
            audit.FirstSourceSequence,
            audit.LastSourceSequence,
            Csv(audit.FirstTime?.ToString("O") ?? string.Empty),
            Csv(audit.LastTime?.ToString("O") ?? string.Empty),
            audit.EventCount,
            audit.TotalVolume,
            audit.BidVolume,
            audit.AskVolume,
            audit.UnknownVolume,
            Csv(audit.DirectionSummary),
            Csv(audit.DataTypeSummary),
            audit.NonMonotonicTimeCount,
            audit.MinPrice,
            audit.MaxPrice,
            audit.MinOriginPrice,
            audit.MaxOriginPrice,
            audit.PriceOriginDifferenceCount,
            audit.NonPositivePriceCount,
            audit.NonPositiveOriginPriceCount,
            audit.QuoteReferenceCount,
            audit.QuoteMissingReferenceCount,
            audit.QuoteInBandCount,
            audit.QuoteOutOfBandCount,
            audit.QuoteSideConsistentCount);
        lock (_fileWriteSync)
        {
            if (!_bufferedMicrostructureAuditRows.TryGetValue(path, out var rows))
            {
                rows = new List<string>();
                _bufferedMicrostructureAuditRows[path] = rows;
            }
            rows.Add(row);
        }
    }

    public void AppendMicrostructureAuditSample(string snapshotId, MicrostructureAuditSample sample)
    {
        var path = Path.Combine(_directory, $"{snapshotId}_microstructure_audit_samples.csv");
        var row = string.Join(",",
            ContextValues(snapshotId),
            sample.Bar,
            Csv(sample.Source),
            sample.ArrivalSequence,
            sample.SourceSequence,
            Csv(sample.EventTime.ToString("O")),
            sample.Price,
            sample.OriginPrice,
            sample.Volume,
            sample.IsBid,
            sample.IsAsk,
            Csv(sample.Direction),
            Csv(sample.DataType),
            Csv(sample.SampleKind));
        lock (_fileWriteSync)
        {
            if (!_bufferedMicrostructureAuditSampleRows.TryGetValue(path, out var rows))
            {
                rows = new List<string>();
                _bufferedMicrostructureAuditSampleRows[path] = rows;
            }
            rows.Add(row);
        }
    }

    public void FlushMicrostructureAudit()
    {
        Dictionary<string, string[]> buffered;
        Dictionary<string, string[]> bufferedSamples;
        lock (_fileWriteSync)
        {
            buffered = _bufferedMicrostructureAuditRows.ToDictionary(x => x.Key, x => x.Value.ToArray());
            _bufferedMicrostructureAuditRows.Clear();
            bufferedSamples = _bufferedMicrostructureAuditSampleRows.ToDictionary(x => x.Key, x => x.Value.ToArray());
            _bufferedMicrostructureAuditSampleRows.Clear();
        }

        var header = ContextHeader("Bar,Source,FirstSequence,LastSequence,FirstSourceSequence,LastSourceSequence,FirstTime,LastTime,EventCount,TotalVolume,BidVolume,AskVolume,UnknownVolume,DirectionSummary,DataTypeSummary,NonMonotonicTimeCount,MinPrice,MaxPrice,MinOriginPrice,MaxOriginPrice,PriceOriginDifferenceCount,NonPositivePriceCount,NonPositiveOriginPriceCount,QuoteReferenceCount,QuoteMissingReferenceCount,QuoteInBandCount,QuoteOutOfBandCount,QuoteSideConsistentCount");
        foreach (var item in buffered)
        {
            EnsureHeader(item.Key, header);
            AppendText(item.Key, string.Join(Environment.NewLine, item.Value) + Environment.NewLine);
        }

        var sampleHeader = ContextHeader("Bar,Source,ArrivalSequence,SourceSequence,EventTime,Price,OriginPrice,Volume,IsBid,IsAsk,Direction,DataType,SampleKind");
        foreach (var item in bufferedSamples)
        {
            EnsureHeader(item.Key, sampleHeader);
            AppendText(item.Key, string.Join(Environment.NewLine, item.Value) + Environment.NewLine);
        }
    }

    public void AppendFootprintFeature(string snapshotId, FootprintCandidateFeature feature)
    {
        var path = Path.Combine(_directory, $"{snapshotId}_footprint_candidate_features.csv");
        var row = string.Join(",",
            ContextValues(snapshotId),
            Csv(feature.SignalId),
            Csv(feature.DecisionTime.ToString("O")),
            feature.DecisionBar,
            Csv(feature.Lane),
            Csv(feature.Side),
            Csv(feature.ResearchPath),
            Csv(feature.ZoneId),
            feature.ZoneLow,
            feature.ZoneHigh,
            feature.TickSequenceBoundary,
            Csv(feature.ReferenceTickTime?.ToString("O") ?? string.Empty),
            feature.Tick60WindowComplete,
            feature.FootprintHistory15mComplete,
            feature.Tick30Count,
            feature.BuyVolume30,
            feature.SellVolume30,
            feature.UnknownVolume30,
            feature.Tick60Count,
            feature.BuyVolume60,
            feature.SellVolume60,
            feature.UnknownVolume60,
            feature.ZoneTouchSequence?.ToString() ?? string.Empty,
            Csv(feature.ZoneTouchTime?.ToString("O") ?? string.Empty),
            feature.ZoneTouchDelta,
            feature.ZoneTouchPriceChange,
            feature.ZoneTouchDeltaPerSecond,
            Csv(feature.PriceDeltaDivergence),
            feature.PocM5_1,
            feature.PocM5_2,
            feature.PocM5_3,
            feature.PocMigration1,
            feature.PocMigration2,
            feature.ZoneVolume,
            feature.OutsideZoneVolume,
            feature.ZoneObservedPriceLevels,
            feature.ZoneExpectedPriceLevels,
            feature.ZoneOccupiedLevelRatio,
            feature.ZoneMinObservedLevelVolume);
        lock (_fileWriteSync)
        {
            if (!_bufferedFootprintFeatureRows.TryGetValue(path, out var rows))
            {
                rows = new List<string>();
                _bufferedFootprintFeatureRows[path] = rows;
            }
            rows.Add(row);
        }
    }

    public void FlushFootprintFeatures()
    {
        Dictionary<string, string[]> buffered;
        lock (_fileWriteSync)
        {
            buffered = _bufferedFootprintFeatureRows.ToDictionary(x => x.Key, x => x.Value.ToArray());
            _bufferedFootprintFeatureRows.Clear();
        }

        var header = ContextHeader("SignalID,DecisionTime,DecisionBar,Lane,Side,ResearchPath,ZoneID,ZoneLow,ZoneHigh,TickSequenceBoundary,ReferenceTickTime,Tick60WindowComplete,FootprintHistory15mComplete,Tick30Count,BuyVolume30,SellVolume30,UnknownVolume30,Tick60Count,BuyVolume60,SellVolume60,UnknownVolume60,ZoneTouchSequence,ZoneTouchTime,ZoneTouchDelta,ZoneTouchPriceChange,ZoneTouchDeltaPerSecond,PriceDeltaDivergence,PocM5_1,PocM5_2,PocM5_3,PocMigration1,PocMigration2,ZoneVolume,OutsideZoneVolume,ZoneObservedPriceLevels,ZoneExpectedPriceLevels,ZoneOccupiedLevelRatio,ZoneMinObservedLevelVolume");
        foreach (var item in buffered)
        {
            EnsureHeader(item.Key, header);
            AppendText(item.Key, string.Join(Environment.NewLine, item.Value) + Environment.NewLine);
        }
    }

    public void AppendSweepReclaimCandidate(string snapshotId, SweepReclaimCandidateFeature feature)
    {
        var path = Path.Combine(_directory, $"{snapshotId}_sweep_reclaim_candidates.csv");
        var row = string.Join(",",
            ContextValues(snapshotId),
            Csv(feature.SignalId), Csv(feature.DecisionTime.ToString("O")), feature.DecisionBar, Csv(feature.Side), feature.LookbackBars,
            feature.RangeLow, feature.RangeHigh, feature.SweepExtreme, feature.SweepDepthPoints,
            feature.DecisionOpen, feature.DecisionHigh, feature.DecisionLow, feature.DecisionClose, feature.ReclaimDistancePoints,
            feature.PlannedEntryBar, feature.EstimatedEntry, feature.Stop, feature.EstimatedRiskPoints, feature.EstimatedRiskInAuditRange,
            feature.TickSequenceBoundary, Csv(feature.ReferenceTickTime?.ToString("O") ?? string.Empty), feature.Tick60WindowComplete,
            feature.Tick30Count, feature.BuyVolume30, feature.SellVolume30, feature.UnknownVolume30,
            feature.Tick60Count, feature.BuyVolume60, feature.SellVolume60, feature.UnknownVolume60);
        BufferSweepReclaimRow(_bufferedSweepReclaimCandidateRows, path, row);
    }

    public void AppendSweepReclaimOutcome(string snapshotId, SweepReclaimOutcome outcome)
    {
        var path = Path.Combine(_directory, $"{snapshotId}_sweep_reclaim_outcomes.csv");
        var row = string.Join(",",
            ContextValues(snapshotId),
            Csv(outcome.SignalId), Csv(outcome.EntryTime?.ToString("O") ?? string.Empty), outcome.EntryBar?.ToString() ?? string.Empty,
            outcome.Entry, outcome.Stop, outcome.Target, outcome.RiskPoints,
            Csv(outcome.ExitTime?.ToString("O") ?? string.Empty), outcome.ExitBar?.ToString() ?? string.Empty, Csv(outcome.ExitReason), outcome.ExitPrice,
            outcome.BarsObserved, outcome.MfePoints, outcome.MaePoints, outcome.DeterministicOutcome);
        BufferSweepReclaimRow(_bufferedSweepReclaimOutcomeRows, path, row);
    }

    public void FlushSweepReclaim()
    {
        FlushBufferedRows(
            _bufferedSweepReclaimCandidateRows,
            ContextHeader("SignalID,DecisionTime,DecisionBar,Side,LookbackBars,RangeLow,RangeHigh,SweepExtreme,SweepDepthPoints,DecisionOpen,DecisionHigh,DecisionLow,DecisionClose,ReclaimDistancePoints,PlannedEntryBar,EstimatedEntry,Stop,EstimatedRiskPoints,EstimatedRiskInAuditRange,TickSequenceBoundary,ReferenceTickTime,Tick60WindowComplete,Tick30Count,BuyVolume30,SellVolume30,UnknownVolume30,Tick60Count,BuyVolume60,SellVolume60,UnknownVolume60"));
        FlushBufferedRows(
            _bufferedSweepReclaimOutcomeRows,
            ContextHeader("SignalID,EntryTime,EntryBar,Entry,Stop,Target,RiskPoints,ExitTime,ExitBar,ExitReason,ExitPrice,BarsObserved,MfePoints,MaePoints,DeterministicOutcome"));
    }

    private void BufferSweepReclaimRow(Dictionary<string, List<string>> buffer, string path, string row)
    {
        lock (_fileWriteSync)
        {
            if (!buffer.TryGetValue(path, out var rows))
            {
                rows = new List<string>();
                buffer[path] = rows;
            }
            rows.Add(row);
        }
    }

    private void FlushBufferedRows(Dictionary<string, List<string>> buffer, string header)
    {
        Dictionary<string, string[]> buffered;
        lock (_fileWriteSync)
        {
            buffered = buffer.ToDictionary(x => x.Key, x => x.Value.ToArray());
            buffer.Clear();
        }
        foreach (var item in buffered)
        {
            EnsureHeader(item.Key, header);
            AppendText(item.Key, string.Join(Environment.NewLine, item.Value) + Environment.NewLine);
        }
    }

    public void FlushDecisionTapeCalibration()
    {
        Dictionary<string, string[]> buffered;
        Dictionary<string, string[]> bufferedBars;
        Dictionary<string, string[]> bufferedTurns;
        lock (_fileWriteSync)
        {
            buffered = _bufferedDecisionTapeCalibrationRows.ToDictionary(x => x.Key, x => x.Value.ToArray());
            _bufferedDecisionTapeCalibrationRows.Clear();
            bufferedBars = _bufferedDecisionTapeCalibrationBarRows.ToDictionary(x => x.Key, x => x.Value.ToArray());
            _bufferedDecisionTapeCalibrationBarRows.Clear();
            bufferedTurns = _bufferedDecisionTapeMarketTurnRows.ToDictionary(x => x.Key, x => x.Value.ToArray());
            _bufferedDecisionTapeMarketTurnRows.Clear();
        }

        var header = ContextHeader("SignalID,EntryTime,EntryBar,ResolveTime,ResolveBar,Side,SetupType,ResearchPath,RegimeScore,SetupQualityScore,Entry,Stop,InitialRiskPoints,EntryBid,EntryAsk,EntryQuoteValid,EntryMarketSequence,ResolveMarketSequence,TargetR,MFEPoints,MAEPoints,FirstStopBar,First0_75RBar,First1RBar,First1_5RBar,First2RBar,First2_5RBar,First3RBar,First4RBar,FirstBreakEvenAfter0_75RBar,FirstBreakEvenAfter1RBar,FirstBreakEvenAfter1_5RBar,First1RLockAfter1_5RBar,FirstBreakEvenAfter2_5RBar,BarsTracked,CompletionReason,OriginalDecision,OriginalReason,OriginalTradeID");
        foreach (var item in buffered)
        {
            EnsureHeader(item.Key, header);
            AppendText(item.Key, string.Join(Environment.NewLine, item.Value) + Environment.NewLine);
        }

        var barHeader = ContextHeader("SignalID,ResearchPath,EntryBar,Time,Bar,RelativeBar,Open,High,Low,Close");
        foreach (var item in bufferedBars)
        {
            EnsureHeader(item.Key, barHeader);
            AppendText(item.Key, string.Join(Environment.NewLine, item.Value) + Environment.NewLine);
        }

        var turnHeader = ContextHeader("Sequence,Bar,Price,Kind");
        foreach (var item in bufferedTurns)
        {
            EnsureHeader(item.Key, turnHeader);
            AppendText(item.Key, string.Join(Environment.NewLine, item.Value) + Environment.NewLine);
        }
    }

    public void AppendDecisionTapeMarketTurn(string snapshotId, long sequence, int bar, decimal price, string kind)
    {
        var path = Path.Combine(_directory, $"{snapshotId}_decision_tape_market_turns.csv");
        var row = string.Join(",", ContextValues(snapshotId), sequence, bar, price, Csv(kind));
        lock (_fileWriteSync)
        {
            if (!_bufferedDecisionTapeMarketTurnRows.TryGetValue(path, out var rows))
            {
                rows = new List<string>();
                _bufferedDecisionTapeMarketTurnRows[path] = rows;
            }
            rows.Add(row);
        }
    }

    public void AppendDecisionTapeCalibrationBar(
        string snapshotId,
        string signalId,
        string researchPath,
        int entryBar,
        DateTime time,
        int bar,
        decimal open,
        decimal high,
        decimal low,
        decimal close)
    {
        var path = Path.Combine(_directory, $"{snapshotId}_decision_tape_calibration_bars.csv");
        var row = string.Join(",",
            ContextValues(snapshotId),
            Csv(signalId),
            Csv(researchPath),
            entryBar,
            Csv(time.ToString("O")),
            bar,
            bar - entryBar,
            open,
            high,
            low,
            close);
        lock (_fileWriteSync)
        {
            if (!_bufferedDecisionTapeCalibrationBarRows.TryGetValue(path, out var rows))
            {
                rows = new List<string>();
                _bufferedDecisionTapeCalibrationBarRows[path] = rows;
            }
            rows.Add(row);
        }
    }

    public void AppendFunnelEvent(
        string snapshotId,
        DateTime time,
        int bar,
        string stage,
        string signalId,
        string researchPath,
        string side,
        string setupType,
        string result,
        string reason,
        string executionScope = "")
    {
        if (_compact && stage is not ("ExecutionDecision" or "ResearchOutcome"))
            return;

        var path = Path.Combine(_directory, $"{snapshotId}_funnel_events.csv");
        EnsureHeader(path, ContextHeader("Time,Bar,Stage,SignalID,ResearchPath,Side,SetupType,Result,Reason,ExecutionScope,TimeBucket"));
        AppendText(path,
            string.Join(",",
                ContextValues(snapshotId),
                Csv(time.ToString("O")),
                bar,
                Csv(stage),
                Csv(signalId),
                Csv(researchPath),
                Csv(side),
                Csv(setupType),
                Csv(result),
                Csv(reason),
                Csv(executionScope),
                Csv(TimeBucket(time)))
            + Environment.NewLine);
    }

    public void AppendExitPolicyEvaluation(
        string snapshotId,
        string signalId,
        DateTime entryTime,
        int entryBar,
        DateTime exitTime,
        int exitBar,
        string side,
        string researchPath,
        string exitPolicy,
        string exitReason,
        decimal entry,
        decimal stop,
        decimal target,
        decimal exitPrice,
        decimal initialRiskPoints,
        decimal targetR,
        decimal pnlPoints,
        decimal pnlR,
        int barsHeld,
        bool ambiguousStopAndTargetSameBar,
        int? firstStopBar,
        int? firstTargetBar,
        decimal mfeR,
        decimal maeR,
        int? policyExitBar)
    {
        var path = Path.Combine(_directory, $"{snapshotId}_exit_policy_evaluations.csv");
        EnsureHeader(path, ContextHeader("SignalID,EntryTime,EntryBar,ExitTime,ExitBar,Side,ResearchPath,ExitPolicy,ExitReason,Entry,Stop,Target,ExitPrice,InitialRiskPoints,TargetR,PnLPoints,PnL_R,PnLDollars,BarsHeld,AmbiguousStopAndTargetSameBar,FirstStopBar,FirstTargetBar,MFE_R,MAE_R,PolicyExitBar"));
        var hasSnapshot = _snapshots.TryGetValue(snapshotId, out var snapshot);
        var pointValue = hasSnapshot ? snapshot!.InstrumentProfile.PointValue : 0m;
        var contracts = hasSnapshot ? snapshot!.ExecutionProfile.FixedContracts : 1;
        var dollars = Math.Round(pnlPoints * pointValue * contracts, 2);

        AppendText(path,
            string.Join(",",
                ContextValues(snapshotId),
                Csv(signalId),
                Csv(entryTime.ToString("O")),
                entryBar,
                Csv(exitTime.ToString("O")),
                exitBar,
                Csv(side),
                Csv(researchPath),
                Csv(exitPolicy),
                Csv(exitReason),
                entry,
                stop,
                target,
                exitPrice,
                initialRiskPoints,
                targetR,
                pnlPoints,
                pnlR,
                dollars,
                barsHeld,
                ambiguousStopAndTargetSameBar,
                firstStopBar?.ToString() ?? string.Empty,
                firstTargetBar?.ToString() ?? string.Empty,
                mfeR,
                maeR,
                policyExitBar?.ToString() ?? string.Empty)
            + Environment.NewLine);
    }

    private void EnsureHeader(string path, string header)
    {
        lock (_fileWriteSync)
        {
            if (!File.Exists(path))
                File.AppendAllText(path, header + Environment.NewLine);
        }
    }

    private void AppendText(string path, string contents)
    {
        lock (_fileWriteSync)
            File.AppendAllText(path, contents);
    }

    private void WriteText(string path, string contents)
    {
        lock (_fileWriteSync)
            File.WriteAllText(path, contents);
    }

    private static string ContextHeader(string suffix)
    {
        return "SnapshotID,StrategyVersion,ResearchSchemaVersion,InstrumentProfileName,ExecutionProfileName,ProfileCatalogVersion," + suffix;
    }

    private string ContextValues(string snapshotId)
    {
        if (!_snapshots.TryGetValue(snapshotId, out var snapshot))
            return string.Join(",", Csv(snapshotId), Csv(string.Empty), Csv(string.Empty), Csv(string.Empty), Csv(string.Empty), Csv(string.Empty));

        return string.Join(",",
            Csv(snapshotId),
            Csv(snapshot.StrategyVersion),
            Csv(snapshot.ResearchSchemaVersion),
            Csv(snapshot.InstrumentProfile.Version),
            Csv(snapshot.ExecutionProfile.Name),
            Csv(snapshot.ProfileCatalogVersion));
    }

    private ResearchSizingValues ResearchSizing(string snapshotId, decimal initialRiskPoints, decimal mfePoints, decimal maePoints)
    {
        if (!_snapshots.TryGetValue(snapshotId, out var snapshot))
            return new ResearchSizingValues(0m, 0m, 0m, 0, 0, 0m, 0m, 0m, 0m);

        var pointValue = snapshot.InstrumentProfile.PointValue;
        var plannedContracts = snapshot.ExecutionProfile.FixedContracts;
        var actualContracts = plannedContracts;
        var riskPerContractDollars = Math.Round(initialRiskPoints * pointValue, 2);
        var totalInitialRiskDollars = Math.Round(riskPerContractDollars * actualContracts, 2);
        var mfeDollars = Math.Round(mfePoints * pointValue * actualContracts, 2);
        var maeDollars = Math.Round(maePoints * pointValue * actualContracts, 2);

        return new ResearchSizingValues(
            pointValue,
            snapshot.InstrumentProfile.TickSize,
            snapshot.InstrumentProfile.TickValue,
            plannedContracts,
            actualContracts,
            riskPerContractDollars,
            totalInitialRiskDollars,
            mfeDollars,
            maeDollars);
    }

    private sealed record ResearchSizingValues(
        decimal PointValue,
        decimal TickSize,
        decimal TickValue,
        int PlannedContracts,
        int ActualContracts,
        decimal RiskPerContractDollars,
        decimal TotalInitialRiskDollars,
        decimal MfeDollars,
        decimal MaeDollars);

    private ExecutionResearchValues ExecutionResearch(string snapshotId)
    {
        if (!_snapshots.TryGetValue(snapshotId, out var snapshot))
            return new ExecutionResearchValues(false, true, false, "ResearchOnlyMode", 0m, 0m, 0);

        return new ExecutionResearchValues(
            WouldTradeLive: false,
            ResearchOnlySignal: true,
            SkippedByDailyGuard: false,
            ExecutionSkipReasons: "ResearchOnlyMode",
            DailyTargetDollars: snapshot.ExecutionProfile.DailyTargetDollars,
            DailyLossLimitDollars: snapshot.ExecutionProfile.DailyLossLimitDollars,
            MaxContracts: snapshot.ExecutionProfile.MaxContracts);
    }

    private sealed record ExecutionResearchValues(
        bool WouldTradeLive,
        bool ResearchOnlySignal,
        bool SkippedByDailyGuard,
        string ExecutionSkipReasons,
        decimal DailyTargetDollars,
        decimal DailyLossLimitDollars,
        int MaxContracts);

    private static string Csv(string value)
    {
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static string NullableValue(int? value)
    {
        return value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static decimal Percent(int count, int total)
    {
        return total <= 0 ? 0m : Math.Round(100m * count / total, 2);
    }

    private static string ScoreBucket(decimal score, decimal threshold)
    {
        if (threshold > 0m && score >= threshold)
            return "Passed";
        if (score >= 50m)
            return "Weak50_69";
        return "WeakLT50";
    }

    private static string SetupQualityBucket(decimal score)
    {
        if (score >= 80m)
            return "Q80Plus";
        if (score >= 70m)
            return "Q70_79";
        if (score >= 50m)
            return "Q50_69";
        return "QLT50";
    }

    private static string RiskBucket(decimal risk)
    {
        if (risk <= 0m)
            return "None";
        if (risk <= 8.5m)
            return "RiskLE8_5";
        if (risk <= 11m)
            return "RiskLE11";
        if (risk <= 15m)
            return "RiskLE15";
        return "RiskGT15";
    }

    private static string RrBucket(decimal rr)
    {
        if (rr >= 1.5m)
            return "RR_GE_1_5";
        if (rr >= 1.2m)
            return "RR_GE_1_2";
        if (rr >= 1.0m)
            return "RR_GE_1_0";
        return "RR_LT_1_0";
    }

    private static string ExecutionScope(string decision, string reason)
    {
        if (string.Equals(decision, "Execute", StringComparison.OrdinalIgnoreCase))
            return "ActualExecuted";
        if (string.Equals(reason, "PathDisabled", StringComparison.OrdinalIgnoreCase)
            || string.Equals(reason, "ResearchOnlyPath", StringComparison.OrdinalIgnoreCase))
            return "ResearchOnly";
        return "ActualCandidate";
    }

    private static string TimeBucket(DateTime time)
    {
        var t = time.TimeOfDay;
        if (t < new TimeSpan(10, 0, 0))
            return "Pre10";
        if (t < new TimeSpan(11, 30, 0))
            return "Morning";
        if (t < new TimeSpan(13, 30, 0))
            return "Midday";
        if (t < new TimeSpan(15, 30, 0))
            return "Afternoon";
        return "Late";
    }
}
