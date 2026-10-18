namespace OPFStrategyV1.Strategy;

public static class StructureRiskPolicy
{
    public const decimal MaxPoints = 50m;
    public static bool Applies(string path) => path is
        "NativeIcebergReaction" or
        "ObservationConfirm" or "BreakawayFvg_Qualified" or
        "FailureReverse_ObservationInvalidated_WideStop1_5R" or "FailureReverse_RetestFailed" or
        "ESResponseAlignedBreakout" or "ESWallRejectionReversal" or
        "ESExpectedMoveExpansion" or "ESGammaConcentrationResponse";
    public static string? Reject(decimal points, decimal pointValue, decimal quantity) =>
        points <= 0m || pointValue <= 0m || quantity <= 0m ? "StructureRiskInvalid" :
        points > MaxPoints ? $"StructureStopTooWide:risk={points:0.##}|max={MaxPoints}|dollars={points * pointValue * quantity:0.##}|maxDollars={MaxPoints * pointValue * quantity:0.##}" : null;
}
