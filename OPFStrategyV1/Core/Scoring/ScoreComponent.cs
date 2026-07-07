namespace OPFStrategyV1.Core.Scoring;

public sealed record ScoreComponent(
    string Name,
    string RawValue,
    bool Passed,
    decimal Weight,
    decimal Contribution,
    string Reason);
