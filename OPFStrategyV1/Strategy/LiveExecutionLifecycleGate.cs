namespace OPFStrategyV1.Strategy;

public readonly record struct LiveExecutionLifecycleState(
    bool IsStoppingActualExecution,
    bool ProtectionTimeoutLatched,
    bool ConnectorlessContextFallbackActive,
    bool LatencyGateActive,
    bool OrphanPositionFlattenPending)
{
    public static LiveExecutionLifecycleState CreateForStart(
        bool wasStopping,
        bool protectionTimeoutLatched,
        bool connectorlessContextFallbackActive,
        bool latencyGateActive,
        bool orphanPositionFlattenPending) =>
        new(false, false, false, false, false);
}
