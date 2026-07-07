using System.Text.Json;

namespace OPFStrategyV1.Core.Configuration;

public sealed record ActualExecutionSettings(
    string Version,
    bool EnableActualOrders,
    string ActualExecutionPaths,
    bool ActualAllowResearchPaths,
    decimal ActualOrderQuantity,
    decimal ActualTargetR,
    int ActualMaxTradesPerDay,
    bool ActualUseFullLossGuard,
    bool ActualUseConsecutiveLossGuard,
    string ActualTimeInForce,
    string ActualStopTriggerType,
    decimal ActualMinEstimatedRr,
    decimal ActualBreakawayMaxRiskPoints,
    decimal ActualObservationConfirmMaxRiskPoints,
    decimal ActualObservationConfirmVolumeMaxRiskPoints,
    decimal ActualObservationConfirmMinSetupQualityScore,
    decimal ActualObservationConfirmFillerMinSetupQualityScore,
    int ActualObservationConfirmMaxFillerTradesPerDay,
    int ActualObservationConfirmFillerUntilDailyTrades,
    decimal ActualFailureRetestMaxRiskPoints,
    decimal ActualFailureRetestMinSetupQualityScore,
    bool ActualRequireTrendRegime,
    decimal ActualMinSetupQualityScore,
    decimal ActualFailureReverseMinSetupQualityScore,
    bool ActualRequireFailureRetest,
    bool ActualEnableWideStopExecution,
    decimal ActualWideStopMultiplier,
    string ActualRrTiers)
{
    public static ActualExecutionSettings Default()
    {
        return new ActualExecutionSettings(
            Version: "ACTUAL_EXEC_1.45",
            EnableActualOrders: true,
            ActualExecutionPaths: "ObservationConfirm|ObservationConfirm_WideStop1_5R|StructureConfirmShadow_ConfirmBarStop|StructureConfirmShadow_ConfirmBarStop_Min10|StructureConfirmShadow_ConfirmBarStop_Wait1|StructureConfirmShadow_SwingStop|TrendPullbackConfirmed|BreakawayFvg|BreakawayFvg_Qualified|FailureReverse_ObservationInvalidated|FailureReverse_LongQualified|FailureReverse_RetestFailed|FailureReverse_RetestFailed_WideStop1_5R",
            ActualAllowResearchPaths: false,
            ActualOrderQuantity: 1m,
            ActualTargetR: 1.5m,
            ActualMaxTradesPerDay: 20,
            ActualUseFullLossGuard: false,
            ActualUseConsecutiveLossGuard: false,
            ActualTimeInForce: "Day",
            ActualStopTriggerType: "Last",
            ActualMinEstimatedRr: 1.0m,
            ActualBreakawayMaxRiskPoints: 11m,
            ActualObservationConfirmMaxRiskPoints: 11m,
            ActualObservationConfirmVolumeMaxRiskPoints: 18m,
            ActualObservationConfirmMinSetupQualityScore: 56m,
            ActualObservationConfirmFillerMinSetupQualityScore: 48m,
            ActualObservationConfirmMaxFillerTradesPerDay: 3,
            ActualObservationConfirmFillerUntilDailyTrades: 5,
            ActualFailureRetestMaxRiskPoints: 11m,
            ActualFailureRetestMinSetupQualityScore: 56m,
            ActualRequireTrendRegime: true,
            ActualMinSetupQualityScore: 70m,
            ActualFailureReverseMinSetupQualityScore: 40m,
            ActualRequireFailureRetest: false,
            ActualEnableWideStopExecution: true,
            ActualWideStopMultiplier: 1.5m,
            ActualRrTiers: "1.0|1.2|1.5");
    }

    public static ActualExecutionSettings LoadOrCreateDefault(out string path, out string status)
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var directory = Path.Combine(root, "ATAS", "StrategyConfigs");
        Directory.CreateDirectory(directory);
        path = Path.Combine(directory, "OPFStrategyV1_actual_execution.json");

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        if (!File.Exists(path))
        {
            var created = Default();
            File.WriteAllText(path, JsonSerializer.Serialize(created, options));
            status = "createdDefault";
            return created;
        }

        try
        {
            var loaded = JsonSerializer.Deserialize<ActualExecutionSettings>(File.ReadAllText(path), options);
            if (loaded is not null)
            {
                if (!string.Equals(loaded.Version, Default().Version, StringComparison.OrdinalIgnoreCase))
                {
                    var upgraded = Default();
                    File.WriteAllText(path, JsonSerializer.Serialize(upgraded, options));
                    status = "upgradedDefault";
                    return upgraded;
                }

                status = "loaded";
                return Normalize(loaded);
            }
        }
        catch
        {
        }

        var fallback = Default();
        status = "fallbackDefault";
        return fallback;
    }

    private static ActualExecutionSettings Normalize(ActualExecutionSettings settings)
    {
        var fallback = Default();
        return settings with
        {
            ActualExecutionPaths = string.IsNullOrWhiteSpace(settings.ActualExecutionPaths) ? fallback.ActualExecutionPaths : settings.ActualExecutionPaths,
            ActualOrderQuantity = settings.ActualOrderQuantity <= 0m ? fallback.ActualOrderQuantity : settings.ActualOrderQuantity,
            ActualTargetR = settings.ActualTargetR <= 0m ? fallback.ActualTargetR : settings.ActualTargetR,
            ActualMaxTradesPerDay = settings.ActualMaxTradesPerDay <= 0 ? fallback.ActualMaxTradesPerDay : settings.ActualMaxTradesPerDay,
            ActualTimeInForce = string.IsNullOrWhiteSpace(settings.ActualTimeInForce) ? fallback.ActualTimeInForce : settings.ActualTimeInForce,
            ActualStopTriggerType = string.IsNullOrWhiteSpace(settings.ActualStopTriggerType) ? fallback.ActualStopTriggerType : settings.ActualStopTriggerType,
            ActualMinEstimatedRr = settings.ActualMinEstimatedRr <= 0m ? fallback.ActualMinEstimatedRr : settings.ActualMinEstimatedRr,
            ActualBreakawayMaxRiskPoints = settings.ActualBreakawayMaxRiskPoints <= 0m ? fallback.ActualBreakawayMaxRiskPoints : settings.ActualBreakawayMaxRiskPoints,
            ActualObservationConfirmMaxRiskPoints = settings.ActualObservationConfirmMaxRiskPoints <= 0m ? fallback.ActualObservationConfirmMaxRiskPoints : settings.ActualObservationConfirmMaxRiskPoints,
            ActualObservationConfirmVolumeMaxRiskPoints = settings.ActualObservationConfirmVolumeMaxRiskPoints <= 0m ? fallback.ActualObservationConfirmVolumeMaxRiskPoints : settings.ActualObservationConfirmVolumeMaxRiskPoints,
            ActualObservationConfirmMinSetupQualityScore = settings.ActualObservationConfirmMinSetupQualityScore <= 0m ? fallback.ActualObservationConfirmMinSetupQualityScore : settings.ActualObservationConfirmMinSetupQualityScore,
            ActualObservationConfirmFillerMinSetupQualityScore = settings.ActualObservationConfirmFillerMinSetupQualityScore <= 0m ? fallback.ActualObservationConfirmFillerMinSetupQualityScore : settings.ActualObservationConfirmFillerMinSetupQualityScore,
            ActualObservationConfirmMaxFillerTradesPerDay = settings.ActualObservationConfirmMaxFillerTradesPerDay <= 0 ? fallback.ActualObservationConfirmMaxFillerTradesPerDay : settings.ActualObservationConfirmMaxFillerTradesPerDay,
            ActualObservationConfirmFillerUntilDailyTrades = settings.ActualObservationConfirmFillerUntilDailyTrades <= 0 ? fallback.ActualObservationConfirmFillerUntilDailyTrades : settings.ActualObservationConfirmFillerUntilDailyTrades,
            ActualFailureRetestMaxRiskPoints = settings.ActualFailureRetestMaxRiskPoints <= 0m ? fallback.ActualFailureRetestMaxRiskPoints : settings.ActualFailureRetestMaxRiskPoints,
            ActualFailureRetestMinSetupQualityScore = settings.ActualFailureRetestMinSetupQualityScore <= 0m ? fallback.ActualFailureRetestMinSetupQualityScore : settings.ActualFailureRetestMinSetupQualityScore,
            ActualMinSetupQualityScore = settings.ActualMinSetupQualityScore <= 0m ? fallback.ActualMinSetupQualityScore : settings.ActualMinSetupQualityScore,
            ActualFailureReverseMinSetupQualityScore = settings.ActualFailureReverseMinSetupQualityScore <= 0m ? fallback.ActualFailureReverseMinSetupQualityScore : settings.ActualFailureReverseMinSetupQualityScore,
            ActualWideStopMultiplier = settings.ActualWideStopMultiplier <= 1m ? fallback.ActualWideStopMultiplier : settings.ActualWideStopMultiplier,
            ActualRrTiers = string.IsNullOrWhiteSpace(settings.ActualRrTiers) ? fallback.ActualRrTiers : settings.ActualRrTiers
        };
    }
}
