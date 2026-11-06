using OPFStrategyV1.Core.Profiles;
using OPFStrategyV1.Core.Configuration;
using OPFStrategyV1.Core.Versions;

namespace OPFStrategyV1.Core.Snapshots;

public sealed record ConfigSnapshot(
    string SnapshotId,
    DateTime CreatedAt,
    string StrategyVersion,
    string ResearchSchemaVersion,
    string ScoringConfigVersion,
    string ZoneDetectorVersion,
    string OrderFlowVersion,
    string ProfileCatalogVersion,
    string RequestedInstrumentProfileName,
    string RequestedExecutionProfileName,
    bool UsedInstrumentProfileFallback,
    bool UsedExecutionProfileFallback,
    InstrumentProfile InstrumentProfile,
    ExecutionProfile ExecutionProfile,
    string ActualExecutionConfigPath,
    string ActualExecutionConfigStatus,
    ActualExecutionSettings ActualExecutionSettings)
{
    public string AccountScope { get; init; } = "unresolved";
    public string OpeningRangeDefinition { get; init; } = Regime.TrendScoreEngine.OpeningRangeDefinition;
    public string EntryExitPolicyVersion { get; init; } = Strategy.EntryExitPolicyParameters.Version;
    public string ConfigFileSha256 { get; init; } = string.Empty;
    public string EffectiveSettingsSha256 { get; init; } = string.Empty;
    public static ConfigSnapshot Create(
        ProfileSelection profileSelection,
        string actualExecutionConfigPath,
        string actualExecutionConfigStatus,
        ActualExecutionSettings actualExecutionSettings,
        string accountScope = "unresolved")
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        return new ConfigSnapshot(
            SnapshotId: $"OPF-{timestamp}-{Guid.NewGuid():N}",
            CreatedAt: DateTime.UtcNow,
            StrategyVersion: StrategyVersions.StrategyVersion,
            ResearchSchemaVersion: StrategyVersions.ResearchSchemaVersion,
            ScoringConfigVersion: StrategyVersions.ScoringConfigVersion,
            ZoneDetectorVersion: StrategyVersions.ZoneDetectorVersion,
            OrderFlowVersion: StrategyVersions.OrderFlowVersion,
            ProfileCatalogVersion: StrategyVersions.ProfileCatalogVersion,
            RequestedInstrumentProfileName: profileSelection.RequestedInstrumentProfileName,
            RequestedExecutionProfileName: profileSelection.RequestedExecutionProfileName,
            UsedInstrumentProfileFallback: profileSelection.UsedInstrumentFallback,
            UsedExecutionProfileFallback: profileSelection.UsedExecutionFallback,
            InstrumentProfile: profileSelection.InstrumentProfile,
            ExecutionProfile: profileSelection.ExecutionProfile,
            ActualExecutionConfigPath: actualExecutionConfigPath,
            ActualExecutionConfigStatus: actualExecutionConfigStatus,
            ActualExecutionSettings: actualExecutionSettings with { FeishuWebhookUrl = string.IsNullOrEmpty(actualExecutionSettings.FeishuWebhookUrl) ? string.Empty : "REDACTED" })
        {
            AccountScope = accountScope,
            ConfigFileSha256 = File.Exists(actualExecutionConfigPath)
                ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(actualExecutionConfigPath))) : string.Empty,
            EffectiveSettingsSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(actualExecutionSettings with { FeishuWebhookUrl = string.Empty })))
        };
    }
}
