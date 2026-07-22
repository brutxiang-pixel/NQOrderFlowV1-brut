using OPFStrategyV1.Core.Snapshots;
using OPFStrategyV1.Core.Signals;
using OPFStrategyV1.Core.MarketData;
using OPFStrategyV1.Regime;
using OPFStrategyV1.Zones;
using System.Text.Json;

namespace OPFStrategyV1.Research;

public sealed class ResearchLogger
{
    private readonly string _directory;
    private readonly Dictionary<string, ConfigSnapshot> _snapshots = new();
    private readonly bool _compact;
    private readonly object _fileWriteSync = new();
    private readonly Dictionary<string, List<string>> _bufferedShadowTradeRows = new();

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
