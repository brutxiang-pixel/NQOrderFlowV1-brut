using OPFStrategyV1.Core.Scoring;

namespace OPFStrategyV1.Regime;

public sealed record RegimeResult(
    MarketRegime Regime,
    ScoreBreakdown BullTrendScore,
    ScoreBreakdown BearTrendScore);
