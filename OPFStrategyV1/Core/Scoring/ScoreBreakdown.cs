namespace OPFStrategyV1.Core.Scoring;

public sealed record ScoreBreakdown(
    string ScoreName,
    decimal TotalScore,
    decimal Threshold,
    bool Passed,
    IReadOnlyList<ScoreComponent> Components)
{
    public static ScoreBreakdown Empty(string scoreName, decimal threshold)
    {
        return new ScoreBreakdown(scoreName, 0m, threshold, false, Array.Empty<ScoreComponent>());
    }
}
