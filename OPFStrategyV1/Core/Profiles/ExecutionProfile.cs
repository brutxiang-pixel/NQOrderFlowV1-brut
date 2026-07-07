namespace OPFStrategyV1.Core.Profiles;

public sealed record ExecutionProfile(
    string Name,
    string Version,
    decimal DailyTargetDollars,
    decimal DailyLossLimitDollars,
    int MaxContracts,
    string ContractSizingMode,
    int FixedContracts,
    decimal MaxRiskPerTradeDollars,
    bool StopAfterDailyTarget,
    bool ContinueResearchAfterDailyTarget,
    int MaxFullLossTradesPerDay,
    int MaxConsecutiveLossesPerDay)
{
    public static ExecutionProfile MnqOneContractTarget150()
    {
        return new ExecutionProfile(
            Name: "MNQ_1Contract_Target150_200",
            Version: "EXEC_0.1",
            DailyTargetDollars: 0m,
            DailyLossLimitDollars: 0m,
            MaxContracts: 1,
            ContractSizingMode: "Fixed",
            FixedContracts: 1,
            MaxRiskPerTradeDollars: 100m,
            StopAfterDailyTarget: false,
            ContinueResearchAfterDailyTarget: true,
            MaxFullLossTradesPerDay: 2,
            MaxConsecutiveLossesPerDay: 2);
    }

    public static ExecutionProfile MnqOneContractTarget300()
    {
        return new ExecutionProfile(
            Name: "MNQ_1Contract_Target300",
            Version: "EXEC_0.1",
            DailyTargetDollars: 300m,
            DailyLossLimitDollars: 100m,
            MaxContracts: 1,
            ContractSizingMode: "Fixed",
            FixedContracts: 1,
            MaxRiskPerTradeDollars: 100m,
            StopAfterDailyTarget: true,
            ContinueResearchAfterDailyTarget: true,
            MaxFullLossTradesPerDay: 2,
            MaxConsecutiveLossesPerDay: 2);
    }
}
