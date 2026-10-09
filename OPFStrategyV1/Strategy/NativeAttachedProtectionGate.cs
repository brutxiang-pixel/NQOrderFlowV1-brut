namespace OPFStrategyV1.Strategy;

public static class NativeAttachedProtectionGate
{
    public static bool ShouldFinalizeAfterEntryFill(bool nativeAttachedBracketPrepared) => nativeAttachedBracketPrepared;

    public static bool ShouldDeferLossCheck(bool nativeAttachedBracketPrepared, bool confirmationWindowOpen) =>
        nativeAttachedBracketPrepared && confirmationWindowOpen;

}
