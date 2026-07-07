using OPFStrategyV1.Core.Scoring;
using OPFStrategyV1.Zones;

namespace OPFStrategyV1.Core.Signals;

public enum SignalStage
{
    Candidate,
    Confirmed,
    Triggered,
    Executed,
    ResearchOnly,
    Invalidated,
    Skipped
}

public enum SetupType
{
    TrendPullback,
    FailureReverse
}

public enum TradeSide
{
    Long,
    Short
}

public sealed record CandidateSignal(
    string SignalId,
    string SnapshotId,
    DateTime Time,
    int Bar,
    TradeSide Side,
    SetupType SetupType,
    SignalStage Stage,
    DetectedZone? Zone,
    ScoreBreakdown RegimeScore,
    ScoreBreakdown SetupQualityScore,
    IReadOnlyList<string> SkipReasons);
