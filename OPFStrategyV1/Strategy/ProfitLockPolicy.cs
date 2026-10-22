namespace OPFStrategyV1.Strategy;

internal readonly record struct ProfitLockPlan(decimal TriggerR, decimal LockR);

internal static class ProfitLockPolicy
{
    public static bool TryGet(string path, out ProfitLockPlan plan)
    {
        plan = path switch
        {
            "ObservationConfirm" => new(.75m, .25m),
            "BreakawayFvg_Qualified" => new(.75m, .5m),
            "FailureReverse_ObservationInvalidated_WideStop1_5R" => new(2m, .5m),
            "FailureReverse_RetestFailed" => new(1.25m, .5m),
            "ESResponseAlignedBreakout" => new(1.25m, .5m),
            "ESWallRejectionReversal" => new(1.25m, .5m),
            "ESExpectedMoveExpansion" => new(1.25m, .5m),
            "ESGammaConcentrationResponse" => new(1.25m, .5m),
            _ => default
        };
        return plan.TriggerR > 0m;
    }
}
