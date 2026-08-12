using ATAS.Indicators;

namespace OPFStrategyV1.Strategy;

public sealed partial class OpeningPullbackFailureStrategy
{
    private long _marketExecutionTapeSequence;

    private void InitializeMarketExecutionTape() => _marketExecutionTapeSequence = 0;

    private void RecordMarketExecutionTapeTrade(MarketDataArg trade)
    {
        if (!(CandidateScenarioTapeDataOnly || MarketExecutionTapeDataOnly) || _snapshot is null || _researchLogger is null || trade.Price <= 0m || trade.Volume <= 0m)
            return;
        var sequence = ++_marketExecutionTapeSequence;
        if (!MarketExecutionTapeDataOnly)
            return;
        _researchLogger.AppendMarketExecutionTick(_snapshot.SnapshotId, sequence, FloorToM5(trade.Time), trade.Time, trade.Price, trade.Volume, trade.Direction.ToString());
    }
}
