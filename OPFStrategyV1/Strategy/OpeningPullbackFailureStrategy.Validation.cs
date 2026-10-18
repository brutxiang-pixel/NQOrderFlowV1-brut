using OPFStrategyV1.Core.MarketData;
using OPFStrategyV1.Core.Signals;

namespace OPFStrategyV1.Strategy;

public sealed partial class OpeningPullbackFailureStrategy
{
    private void CaptureDecisionInput(CandidateSignal signal, OpfCandle candle, string path, decimal stop,
        decimal risk, string decision, string reason, string tradeId)
    {
        if (_snapshot is null || _researchLogger is null) return;
        try
        {
            var evidence = _intradayEvidence.GetValueOrDefault(signal.SignalId) ?? candle;
            var micro = BuildCandidateMicroContext(signal, evidence, path, risk);
            var quote = ExecutionQuoteSnapshot();
            var decisionUtc = IsHistoricalReplayTime(candle.Time) ? DateTime.SpecifyKind(candle.Time, DateTimeKind.Utc) : DateTime.UtcNow;
            object[] tape;
            lock (_intradaySync)
                tape = _entryTape.Where(x => x.Time <= decisionUtc).Select(x => (object)new { x.Time, x.Price, x.Volume, x.Signed }).ToArray();
            _researchLogger.AppendValidationPacket(_snapshot.SnapshotId, "decision", new
            {
                Schema = 1, Strategy = Core.Versions.StrategyVersions.StrategyVersion,
                DecisionUtc = decisionUtc,
                signal.SignalId, Product = Security?.Code, Path = path, Decision = decision, Reason = reason, TradeId = tradeId,
                Candle = candle, Evidence = evidence, Stop = stop, Risk = risk, Side = signal.Side.ToString(),
                Quote = new { quote.Bid, quote.Ask }, Account = LiveAccountScope.FromPortfolio(Portfolio),
                Replay = IsHistoricalReplayTime(candle.Time), ManualLatch = _manualInterventionLatch,
                PnlUnresolved = _dailyPnlScopeUnresolved, DailyPnl = _replayDailyPnlDollars, TradesToday = _replayTradesToday,
                ActiveTrades = ActiveReplayExecutions().Select(x => x.TradeId).ToArray(),
                Frames = _completedFrames.Values.ToArray(), DailyAverages = _dailyAverages.ToArray(),
                MicroContext = micro, Tape = tape,
                IsTest = path == "StartupTestOrder", IsIndependentIceberg = path == "NativeIcebergReaction"
            });
        }
        catch (Exception ex)
        {
            LogLiveReadinessInfo($"VALIDATION_CAPTURE_FAILED {ex.GetType().Name}");
        }
    }
}
