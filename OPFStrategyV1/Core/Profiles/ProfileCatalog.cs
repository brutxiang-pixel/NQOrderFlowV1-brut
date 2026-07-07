namespace OPFStrategyV1.Core.Profiles;

public static class ProfileCatalog
{
    public const string Version = Core.Versions.StrategyVersions.ProfileCatalogVersion;

    private static readonly Dictionary<string, InstrumentProfile> InstrumentProfiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["MNQ_0.1"] = InstrumentProfile.MnqDefault(),
        ["NQ_0.1"] = new InstrumentProfile(
            Instrument: "NQ",
            Version: "NQ_0.1",
            TickSize: 0.25m,
            TickValue: 5m,
            PointValue: 20m,
            Currency: "USD",
            OpeningRangeMinutes: 30,
            MinStopPoints: 2m,
            MaxRiskPointsHard: 25m,
            AtrRiskMultiplier: 0.60m,
            MinEstimatedRr: 1.50m,
            DefaultContracts: 1),
        ["ES_0.1"] = new InstrumentProfile(
            Instrument: "ES",
            Version: "ES_0.1",
            TickSize: 0.25m,
            TickValue: 12.50m,
            PointValue: 50m,
            Currency: "USD",
            OpeningRangeMinutes: 30,
            MinStopPoints: 1m,
            MaxRiskPointsHard: 12m,
            AtrRiskMultiplier: 0.60m,
            MinEstimatedRr: 1.50m,
            DefaultContracts: 1),
        ["MES_0.1"] = new InstrumentProfile(
            Instrument: "MES",
            Version: "MES_0.1",
            TickSize: 0.25m,
            TickValue: 1.25m,
            PointValue: 5m,
            Currency: "USD",
            OpeningRangeMinutes: 30,
            MinStopPoints: 1m,
            MaxRiskPointsHard: 12m,
            AtrRiskMultiplier: 0.60m,
            MinEstimatedRr: 1.50m,
            DefaultContracts: 1),
        ["GC_0.1"] = new InstrumentProfile(
            Instrument: "GC",
            Version: "GC_0.1",
            TickSize: 0.10m,
            TickValue: 10m,
            PointValue: 100m,
            Currency: "USD",
            OpeningRangeMinutes: 30,
            MinStopPoints: 1m,
            MaxRiskPointsHard: 20m,
            AtrRiskMultiplier: 0.60m,
            MinEstimatedRr: 1.50m,
            DefaultContracts: 1),
        ["MGC_0.1"] = new InstrumentProfile(
            Instrument: "MGC",
            Version: "MGC_0.1",
            TickSize: 0.10m,
            TickValue: 1m,
            PointValue: 10m,
            Currency: "USD",
            OpeningRangeMinutes: 30,
            MinStopPoints: 1m,
            MaxRiskPointsHard: 20m,
            AtrRiskMultiplier: 0.60m,
            MinEstimatedRr: 1.50m,
            DefaultContracts: 1)
    };

    private static readonly Dictionary<string, ExecutionProfile> ExecutionProfiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["MNQ_1Contract_Target150_200"] = ExecutionProfile.MnqOneContractTarget150(),
        ["MNQ_1Contract_Target300"] = ExecutionProfile.MnqOneContractTarget300(),
        ["MNQ_2Contract_Target300"] = new ExecutionProfile(
            Name: "MNQ_2Contract_Target300",
            Version: "EXEC_0.1",
            DailyTargetDollars: 300m,
            DailyLossLimitDollars: 200m,
            MaxContracts: 2,
            ContractSizingMode: "Fixed",
            FixedContracts: 2,
            MaxRiskPerTradeDollars: 200m,
            StopAfterDailyTarget: true,
            ContinueResearchAfterDailyTarget: true,
            MaxFullLossTradesPerDay: 2,
            MaxConsecutiveLossesPerDay: 2)
    };

    public static ProfileSelection Select(string instrumentProfileName, string executionProfileName)
    {
        var instrumentFallback = false;
        if (!InstrumentProfiles.TryGetValue(instrumentProfileName, out var instrumentProfile))
        {
            instrumentProfile = InstrumentProfile.MnqDefault();
            instrumentFallback = true;
        }

        var executionFallback = false;
        if (!ExecutionProfiles.TryGetValue(executionProfileName, out var executionProfile))
        {
            executionProfile = ExecutionProfile.MnqOneContractTarget150();
            executionFallback = true;
        }

        return new ProfileSelection(
            instrumentProfile,
            executionProfile,
            instrumentProfileName,
            executionProfileName,
            instrumentFallback,
            executionFallback);
    }

    public static string SupportedInstrumentProfileNames => string.Join(", ", InstrumentProfiles.Keys);

    public static string SupportedExecutionProfileNames => string.Join(", ", ExecutionProfiles.Keys);
}
