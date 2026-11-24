namespace OPFStrategyV1.Core.Versions;

public static class StrategyVersions
{
#if OPF_ATAS_X
    public const string StrategyVersion = "OPF_X_3.0.0-preview.12";
    internal const string PlatformDataDirectory = "ATAS X";
#else
    public const string StrategyVersion = "OPF_INTRADAY_2.23";
    internal const string PlatformDataDirectory = "ATAS";
#endif
    public const string ResearchSchemaVersion = "OPF_RESEARCH_2.34";
    public const string ScoringConfigVersion = "OPF_SCORING_0.2";
    public const string ZoneDetectorVersion = "OPF_ZONE_0.1";
    public const string OrderFlowVersion = "OPF_OF_0.2";
    public const string ProfileCatalogVersion = "OPF_PROFILE_0.1";
}
