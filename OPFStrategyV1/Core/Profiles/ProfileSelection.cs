namespace OPFStrategyV1.Core.Profiles;

public sealed record ProfileSelection(
    InstrumentProfile InstrumentProfile,
    ExecutionProfile ExecutionProfile,
    string RequestedInstrumentProfileName,
    string RequestedExecutionProfileName,
    bool UsedInstrumentFallback,
    bool UsedExecutionFallback);
