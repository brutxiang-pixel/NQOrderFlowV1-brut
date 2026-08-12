using OPFStrategyV1.Core.MarketData;
using OPFStrategyV1.Core.Signals;
using OPFStrategyV1.Research;
using OPFStrategyV1.Zones;

namespace OPFStrategyV1.Strategy;

public sealed partial class OpeningPullbackFailureStrategy
{
    private readonly Dictionary<string, MarketExecutionScenarioTracker> _marketExecutionScenarios = new(StringComparer.Ordinal);

    private string CaptureMarketExecutionScenario(CandidateSignal signal, OpfCandle entryCandle, string researchPath, decimal stop, decimal risk)
    {
        if (!(CandidateScenarioTapeDataOnly || MarketExecutionTapeDataOnly) || _snapshot is null || _researchLogger is null)
            return string.Empty;

        var candidateId = $"{signal.SignalId}|{researchPath}|{entryCandle.Time:O}|{entryCandle.Bar}";
        if (_marketExecutionScenarios.ContainsKey(candidateId))
            return candidateId;

        var quote = ExecutionQuoteSnapshot();
        var activeExecutions = ActiveReplayExecutions();
        var globexLocked = IsGlobexCloseoutLockWindow(entryCandle.Time, out var globexReason);
        var usOpenBlackout = IsUsCashOpenBlackout(entryCandle.Time, out var usOpenReason);
        var weeklyLongGate = signal.Side == TradeSide.Long && IsWeeklyLongLossGateActive(out _);
        var zone = signal.Zone;
        _zoneBehaviorStates.TryGetValue(zone?.ZoneId ?? string.Empty, out var zoneState);
        var zoneContext = CaptureMarketExecutionZoneContext(zone, zoneState);
        var targetR = ActualTargetRFor(signal, researchPath, risk);
        var target = TargetFromRisk(signal.Side, entryCandle.Close, risk, targetR);
        _marketExecutionScenarios.Add(candidateId, new MarketExecutionScenarioTracker(
            candidateId, signal, entryCandle, researchPath, stop, risk, target, targetR, quote.Bid, quote.Ask,
            _marketExecutionTapeSequence, _replayTradesToday, _replayDailyPnlDollars, _liveAccountDailyNetPnlDollars,
            _liveLongWeeklyNetPnlDollars, activeExecutions.Count, activeExecutions.Count == 1, globexLocked, globexReason,
            usOpenBlackout, usOpenReason, _latencyGateActive,
            ReplayMaxTradesPerDay > 0 && _replayTradesToday >= ReplayMaxTradesPerDay,
            _actualDailyLossLimitDollars > 0m && _liveAccountDailyNetPnlDollars <= -_actualDailyLossLimitDollars,
            weeklyLongGate, zoneContext));
        return candidateId;
    }

    private static MarketExecutionZoneContext CaptureMarketExecutionZoneContext(DetectedZone? zone, ZoneBehaviorState? state)
    {
        var all = state?.CumulativeAll;
        var inZone = state?.CumulativeZone;
        return new MarketExecutionZoneContext(
            zone?.ZoneId ?? string.Empty, zone?.ZoneType ?? string.Empty, zone?.Direction ?? string.Empty,
            zone?.Low, zone?.High, zone?.CreatedTime, zone?.CreatedBar, state?.TouchOrdinal ?? zone?.TouchCount,
            state?.LastTouchTime, state?.LastTouchBar, state?.LastTouchMarketSequence,
            all?.Buy, all?.Sell, all is null ? null : all.Buy - all.Sell,
            inZone?.Buy, inZone?.Sell, inZone is null ? null : inZone.Buy - inZone.Sell);
    }

    private void ResolveMarketExecutionScenario(string candidateId, string decision, string reason)
    {
        if (string.IsNullOrEmpty(candidateId) || !_marketExecutionScenarios.TryGetValue(candidateId, out var scenario))
            return;
        scenario.Resolve(decision, reason);
    }

    private void FlushMarketExecutionScenarios()
    {
        if (!(CandidateScenarioTapeDataOnly || MarketExecutionTapeDataOnly) || _snapshot is null || _researchLogger is null)
            return;
        foreach (var scenario in _marketExecutionScenarios.Values.OrderBy(x => x.DecisionTime).ThenBy(x => x.DecisionBar).ThenBy(x => x.CandidateId, StringComparer.Ordinal))
            _researchLogger.AppendMarketExecutionScenario(_snapshot.SnapshotId, scenario.ToLogRow());
        _marketExecutionScenarios.Clear();
    }

    private sealed class MarketExecutionScenarioTracker
    {
        private readonly CandidateSignal _signal;
        private readonly OpfCandle _candle;
        private readonly string _researchPath;
        private readonly decimal _stop;
        private readonly decimal _risk;
        private readonly decimal _target;
        private readonly decimal _targetR;
        private readonly decimal _bid;
        private readonly decimal _ask;
        private readonly long _marketSequence;
        private readonly int _dailyTradeCount;
        private readonly decimal _dailyGross;
        private readonly decimal _dailyAccountNet;
        private readonly decimal _weeklyLongNet;
        private readonly int _activeTradeCount;
        private readonly bool _secondarySlotOccupied;
        private readonly bool _globexLocked;
        private readonly string _globexReason;
        private readonly bool _usOpenBlackout;
        private readonly string _usOpenReason;
        private readonly bool _latencyGateActive;
        private readonly bool _dailyTradeLimitReached;
        private readonly bool _dailyLossReached;
        private readonly bool _weeklyLongGateActive;
        private readonly MarketExecutionZoneContext _zoneContext;

        public MarketExecutionScenarioTracker(string candidateId, CandidateSignal signal, OpfCandle candle, string researchPath, decimal stop, decimal risk, decimal target, decimal targetR, decimal bid, decimal ask, long marketSequence, int dailyTradeCount, decimal dailyGross, decimal dailyAccountNet, decimal weeklyLongNet, int activeTradeCount, bool secondarySlotOccupied, bool globexLocked, string globexReason, bool usOpenBlackout, string usOpenReason, bool latencyGateActive, bool dailyTradeLimitReached, bool dailyLossReached, bool weeklyLongGateActive, MarketExecutionZoneContext zoneContext)
        {
            CandidateId = candidateId;
            _signal = signal;
            _candle = candle;
            _researchPath = researchPath;
            _stop = stop;
            _risk = risk;
            _target = target;
            _targetR = targetR;
            _bid = bid;
            _ask = ask;
            _marketSequence = marketSequence;
            _dailyTradeCount = dailyTradeCount;
            _dailyGross = dailyGross;
            _dailyAccountNet = dailyAccountNet;
            _weeklyLongNet = weeklyLongNet;
            _activeTradeCount = activeTradeCount;
            _secondarySlotOccupied = secondarySlotOccupied;
            _globexLocked = globexLocked;
            _globexReason = globexReason;
            _usOpenBlackout = usOpenBlackout;
            _usOpenReason = usOpenReason;
            _latencyGateActive = latencyGateActive;
            _dailyTradeLimitReached = dailyTradeLimitReached;
            _dailyLossReached = dailyLossReached;
            _weeklyLongGateActive = weeklyLongGateActive;
            _zoneContext = zoneContext;
        }

        public string CandidateId { get; }
        public DateTime DecisionTime => _candle.Time;
        public int DecisionBar => _candle.Bar;
        public string Decision { get; private set; } = "Unresolved";
        public string Reason { get; private set; } = "NotResolved";

        public void Resolve(string decision, string reason)
        {
            Decision = decision;
            Reason = reason;
        }

        public ResearchLogger.MarketExecutionScenario ToLogRow()
        {
            return new ResearchLogger.MarketExecutionScenario(
                CandidateId, _signal.SignalId, _candle.Time, _candle.Bar, _signal.Side.ToString(), _signal.SetupType.ToString(), _researchPath,
                _candle.Close, _stop, _target, _risk, _targetR, _bid, _ask, _marketSequence, _dailyTradeCount, _dailyGross,
                _dailyAccountNet, _weeklyLongNet, _activeTradeCount, _secondarySlotOccupied, _globexLocked, _globexReason,
                _usOpenBlackout, _usOpenReason, _latencyGateActive, _dailyTradeLimitReached, _dailyLossReached, _weeklyLongGateActive,
                _zoneContext.ZoneId, _zoneContext.ZoneType, _zoneContext.ZoneDirection, _zoneContext.ZoneLow, _zoneContext.ZoneHigh,
                _zoneContext.ZoneCreatedTime, _zoneContext.ZoneCreatedBar, _zoneContext.ZoneTouchOrdinal, _zoneContext.ZoneLastTouchTime,
                _zoneContext.ZoneLastTouchBar, _zoneContext.ZoneLastTouchMarketSequence, _zoneContext.ZoneCumulativeBuy, _zoneContext.ZoneCumulativeSell,
                _zoneContext.ZoneCumulativeDelta, _zoneContext.ZoneCumulativeInZoneBuy, _zoneContext.ZoneCumulativeInZoneSell, _zoneContext.ZoneCumulativeInZoneDelta,
                Decision, Reason);
        }
    }

    private readonly record struct MarketExecutionZoneContext(
        string ZoneId,
        string ZoneType,
        string ZoneDirection,
        decimal? ZoneLow,
        decimal? ZoneHigh,
        DateTime? ZoneCreatedTime,
        int? ZoneCreatedBar,
        int? ZoneTouchOrdinal,
        DateTime? ZoneLastTouchTime,
        int? ZoneLastTouchBar,
        long? ZoneLastTouchMarketSequence,
        decimal? ZoneCumulativeBuy,
        decimal? ZoneCumulativeSell,
        decimal? ZoneCumulativeDelta,
        decimal? ZoneCumulativeInZoneBuy,
        decimal? ZoneCumulativeInZoneSell,
        decimal? ZoneCumulativeInZoneDelta);
}
