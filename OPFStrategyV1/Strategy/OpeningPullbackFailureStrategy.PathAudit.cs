using OPFStrategyV1.Core.MarketData;
using OPFStrategyV1.Core.Signals;

namespace OPFStrategyV1.Strategy;

public sealed partial class OpeningPullbackFailureStrategy
{
    private readonly List<PathOpportunity> _pathOpportunities = new();
    private readonly HashSet<string> _pathOpportunityKeys = new();
    private readonly List<object> _pathOpportunityPackets = new();

    private void StartPathOpportunity(CandidateSignal signal, OpfCandle candle, string path, decimal stop, decimal risk)
        => StartPathOpportunity(signal.SignalId, signal.Side, candle, path, stop, risk);

    private void StartPathOpportunity(string signalId, TradeSide side, OpfCandle candle, string path, decimal stop, decimal risk)
    {
        if (_snapshot is null || risk <= 0m || !_pathOpportunityKeys.Add(signalId + "|" + path)) return;
        var candidate = new PathOpportunity(signalId, path, side == TradeSide.Long, candle.Time, candle.Bar,
            candle.Close, stop, risk, path == "FailureReverse_ObservationInvalidated_WideStop1_5R" ? 4m : 1.5m,
            _actualCommissionPerContractRoundTrip / _snapshot.InstrumentProfile.PointValue);
        var peers = _pathOpportunities.Where(x => x.Path != path && candidate.Overlaps(x, _snapshot.InstrumentProfile.TickSize)).ToArray();
        foreach (var peer in peers)
            LogLiveReadinessInfo($"PATH_OPPORTUNITY_OVERLAP product={Security?.Code} candidate={signalId} path={path} peer={peer.Signal} peerPath={peer.Path} peerDecision={peer.Decision} entry={candidate.Entry} stop={stop} policy=AuditOnly");
        _pathOpportunities.Add(candidate);
        WritePathOpportunity(candidate, "Start");
    }

    private void RecordPathOpportunityDecision(string signal, string decision, string reason, string tradeId)
    {
        var opportunity = _pathOpportunities.LastOrDefault(x => x.Signal == signal);
        if (opportunity is null) return;
        opportunity.Decision = decision; opportunity.Reason = reason; opportunity.TradeId = tradeId;
    }

    private void FlushPathOpportunities()
    {
        foreach (var item in _pathOpportunities) WritePathOpportunity(item, "StoppedIncomplete");
        _pathOpportunities.Clear();
        foreach (var packet in _pathOpportunityPackets)
        {
            try { _researchLogger?.AppendValidationPacket(_snapshot?.SnapshotId ?? "-", "opportunity", packet); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            { LogLiveReadinessInfo($"PATH_OPPORTUNITY_FLUSH_FAILED {ex.GetType().Name}"); }
        }
        _pathOpportunityPackets.Clear();
    }

    private void ObservePathOpportunities(decimal price)
    {
        if (price <= 0m || _lastSeenBar != CurrentBar - 1 || GetCandle(_lastSeenBar) is not { } raw) return;
        var now = IntradayDecisionTime(raw);
        foreach (var item in _pathOpportunities.ToArray())
            if (item.Observe(now, _lastSeenBar, price))
            {
                WritePathOpportunity(item, "Complete");
                _pathOpportunities.Remove(item);
            }
    }

    private void WritePathOpportunity(PathOpportunity item, string stage)
    {
        try
        {
            // Freeze primitive values; never retain the mutable tracker as the packet.
            _pathOpportunityPackets.Add(new
            { Schema = 1, Stage = stage, Product = Security?.Code, Account = LiveAccountScope.FromPortfolio(Portfolio),
                Opportunity = new { item.Signal, item.Path, item.IsLong, item.Time, item.Bar, item.Entry, item.Stop, item.Risk,
                    item.FeePoints, item.MfeR, item.MaeR, item.FixedPoints, item.ProtectedPoints, item.EarlyPoints, item.LockedPoints, item.Decision, item.Reason, item.TradeId },
                Evidence = "ForwardObservedPrices", NotBrokerFills = true,
                Policies = "fixed target; .75R fee BE; 1R fee BE; .75R fee BE plus 1.25R lock .5R; OC 6 bars, others 36 bars/3h" });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        { LogLiveReadinessInfo($"PATH_OPPORTUNITY_CAPTURE_FAILED {ex.GetType().Name}"); }
    }
}
