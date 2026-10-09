namespace OPFStrategyV1.Strategy;

public static class NativeBracketProbeGate
{
    public static bool CanDispatch(
        bool enabled,
        bool alreadyClaimed,
        bool actualOrdersEnabled,
        bool manualAlertMode,
        bool isLive,
        bool hasOrderContext,
        bool isMnqProfile) =>
        enabled && !alreadyClaimed && actualOrdersEnabled && !manualAlertMode &&
        isLive && hasOrderContext && isMnqProfile;

    public static bool ShouldPersistDisabledAfterClaim(bool claimed) => claimed;
}
