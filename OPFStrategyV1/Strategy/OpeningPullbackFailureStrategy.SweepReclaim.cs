using OPFStrategyV1.Core.MarketData;
using OPFStrategyV1.Research;

namespace OPFStrategyV1.Strategy;

public sealed partial class OpeningPullbackFailureStrategy
{
    private const int SweepReclaimLookbackBars = 12;
    private const int SweepReclaimTimeStopBars = 12;
    private const decimal SweepReclaimTickSize = 0.25m;
    private const decimal SweepReclaimMinRiskPoints = 2m;
    private const decimal SweepReclaimMaxRiskPoints = 20m;
    private const decimal SweepReclaimTargetR = 1.5m;
    private readonly List<SweepReclaimCandidate> _sweepReclaimCandidates = [];

    private void InitializeSweepReclaimCollection() => _sweepReclaimCandidates.Clear();

    private void UpdateSweepReclaimCollection(OpfCandle candle)
    {
        if (!SweepReclaimDataCollectionOnly || _snapshot is null || _researchLogger is null)
            return;

        ResolveSweepReclaimCandidates(candle);
        DetectSweepReclaim(candle);
    }

    private void DetectSweepReclaim(OpfCandle candle)
    {
        var prior = _recentCandles.TakeLast(SweepReclaimLookbackBars).ToArray();
        if (prior.Length != SweepReclaimLookbackBars)
            return;

        var rangeLow = prior.Min(x => x.Low);
        var rangeHigh = prior.Max(x => x.High);
        if (candle.Low < rangeLow && candle.Close >= rangeLow)
            CaptureSweepReclaimCandidate(candle, "Long", rangeLow, rangeHigh, candle.Low);
        if (candle.High > rangeHigh && candle.Close <= rangeHigh)
            CaptureSweepReclaimCandidate(candle, "Short", rangeLow, rangeHigh, candle.High);
    }

    private void CaptureSweepReclaimCandidate(OpfCandle candle, string side, decimal rangeLow, decimal rangeHigh, decimal sweepExtreme)
    {
        var stop = side == "Long" ? sweepExtreme - SweepReclaimTickSize : sweepExtreme + SweepReclaimTickSize;
        var estimatedEntry = candle.Close;
        var estimatedRisk = decimal.Abs(estimatedEntry - stop);
        var referenceTime = _footprintLastTradeTime;
        var tick60Complete = referenceTime.HasValue && _footprintFirstTradeTime.HasValue && referenceTime.Value - _footprintFirstTradeTime.Value >= TimeSpan.FromSeconds(60);
        var visible = _footprintTrades.Where(x => x.Sequence <= _footprintSequence).ToArray();
        var aggregate30 = AggregateFootprint(visible, referenceTime, TimeSpan.FromSeconds(30));
        var aggregate60 = AggregateFootprint(visible, referenceTime, TimeSpan.FromSeconds(60));
        var signalId = $"SR-{candle.Time:yyyyMMdd-HHmm}-{side}-{candle.Bar:000000}";
        var candidate = new SweepReclaimCandidate(
            signalId, candle, side, rangeLow, rangeHigh, sweepExtreme, stop, candle.Bar + 1,
            _footprintSequence, referenceTime, tick60Complete, aggregate30, aggregate60);
        _sweepReclaimCandidates.Add(candidate);
        _researchLogger!.AppendSweepReclaimCandidate(_snapshot!.SnapshotId, candidate.ToFeature(estimatedEntry, estimatedRisk));
    }

    private void ResolveSweepReclaimCandidates(OpfCandle candle)
    {
        foreach (var candidate in _sweepReclaimCandidates.Where(x => !x.Completed).ToArray())
        {
            if (!candidate.Entered)
            {
                if (candle.Bar != candidate.PlannedEntryBar)
                    continue;

                candidate.Enter(candle);
                candidate.Observe(candle);
                if (candidate.Completed)
                    _researchLogger!.AppendSweepReclaimOutcome(_snapshot!.SnapshotId, candidate.ToOutcome());
                continue;
            }

            candidate.Observe(candle);
            if (candidate.Completed)
                _researchLogger!.AppendSweepReclaimOutcome(_snapshot!.SnapshotId, candidate.ToOutcome());
        }
    }

    private void FinalizeSweepReclaimCollection()
    {
        if (!SweepReclaimDataCollectionOnly || _snapshot is null || _researchLogger is null)
            return;

        foreach (var candidate in _sweepReclaimCandidates.Where(x => !x.Completed))
        {
            candidate.MarkIncomplete();
            _researchLogger.AppendSweepReclaimOutcome(_snapshot.SnapshotId, candidate.ToOutcome());
        }
        _sweepReclaimCandidates.Clear();
    }

    private sealed class SweepReclaimCandidate
    {
        private decimal _mfe;
        private decimal _mae;
        private int _barsObserved;

        public SweepReclaimCandidate(string signalId, OpfCandle decision, string side, decimal rangeLow, decimal rangeHigh, decimal sweepExtreme, decimal stop, int plannedEntryBar, long tickSequenceBoundary, DateTime? referenceTickTime, bool tick60WindowComplete, FootprintAggregate aggregate30, FootprintAggregate aggregate60)
        {
            SignalId = signalId;
            Decision = decision;
            Side = side;
            RangeLow = rangeLow;
            RangeHigh = rangeHigh;
            SweepExtreme = sweepExtreme;
            Stop = stop;
            PlannedEntryBar = plannedEntryBar;
            TickSequenceBoundary = tickSequenceBoundary;
            ReferenceTickTime = referenceTickTime;
            Tick60WindowComplete = tick60WindowComplete;
            Aggregate30 = aggregate30;
            Aggregate60 = aggregate60;
        }

        public string SignalId { get; }
        public OpfCandle Decision { get; }
        public string Side { get; }
        public decimal RangeLow { get; }
        public decimal RangeHigh { get; }
        public decimal SweepExtreme { get; }
        public decimal Stop { get; }
        public int PlannedEntryBar { get; }
        public long TickSequenceBoundary { get; }
        public DateTime? ReferenceTickTime { get; }
        public bool Tick60WindowComplete { get; }
        public FootprintAggregate Aggregate30 { get; }
        public FootprintAggregate Aggregate60 { get; }
        public bool Entered { get; private set; }
        public bool Completed { get; private set; }
        public OpfCandle? EntryCandle { get; private set; }
        public decimal Entry { get; private set; }
        public decimal Risk { get; private set; }
        public decimal Target { get; private set; }
        public string ExitReason { get; private set; } = "PendingEntry";
        public decimal ExitPrice { get; private set; }
        public int? ExitBar { get; private set; }
        public DateTime? ExitTime { get; private set; }

        public void Enter(OpfCandle candle)
        {
            Entered = true;
            EntryCandle = candle;
            Entry = candle.Open;
            Risk = decimal.Abs(Entry - Stop);
            Target = Side == "Long" ? Entry + Risk * SweepReclaimTargetR : Entry - Risk * SweepReclaimTargetR;
            _mfe = 0m;
            _mae = 0m;
            ExitReason = Risk >= SweepReclaimMinRiskPoints && Risk <= SweepReclaimMaxRiskPoints ? "Pending" : "RiskOutsideAuditRange";
            if (ExitReason != "Pending")
                Complete(candle, ExitReason, Entry);
        }

        public void Observe(OpfCandle candle)
        {
            if (Completed || !Entered)
                return;

            _barsObserved++;
            var favorable = Side == "Long" ? candle.High - Entry : Entry - candle.Low;
            var adverse = Side == "Long" ? Entry - candle.Low : candle.High - Entry;
            _mfe = Math.Max(_mfe, favorable);
            _mae = Math.Max(_mae, adverse);
            var hitTarget = Side == "Long" ? candle.High >= Target : candle.Low <= Target;
            var hitStop = Side == "Long" ? candle.Low <= Stop : candle.High >= Stop;
            if (hitTarget && hitStop)
            {
                Complete(candle, "AmbiguousSameBar", 0m);
                return;
            }
            if (hitStop)
            {
                Complete(candle, "SL", Stop);
                return;
            }
            if (hitTarget)
            {
                Complete(candle, "TP", Target);
                return;
            }
            if (_barsObserved >= SweepReclaimTimeStopBars)
                Complete(candle, "TimeStop", candle.Close);
        }

        public void MarkIncomplete()
        {
            if (!Completed)
                Complete(EntryCandle ?? Decision, Entered ? "Incomplete" : "NoNextBar", Entered ? Entry : 0m);
        }

        public ResearchLogger.SweepReclaimCandidateFeature ToFeature(decimal estimatedEntry, decimal estimatedRisk) => new(
            SignalId, Decision.Time, Decision.Bar, Side, SweepReclaimLookbackBars, RangeLow, RangeHigh, SweepExtreme,
            Side == "Long" ? RangeLow - SweepExtreme : SweepExtreme - RangeHigh, Decision.Open, Decision.High, Decision.Low, Decision.Close,
            Side == "Long" ? Decision.Close - RangeLow : RangeHigh - Decision.Close, PlannedEntryBar, estimatedEntry, Stop, estimatedRisk,
            estimatedRisk >= SweepReclaimMinRiskPoints && estimatedRisk <= SweepReclaimMaxRiskPoints, TickSequenceBoundary, ReferenceTickTime,
            Tick60WindowComplete, Aggregate30.Count, Aggregate30.BuyVolume, Aggregate30.SellVolume, Aggregate30.UnknownVolume,
            Aggregate60.Count, Aggregate60.BuyVolume, Aggregate60.SellVolume, Aggregate60.UnknownVolume);

        public ResearchLogger.SweepReclaimOutcome ToOutcome() => new(
            SignalId, EntryCandle?.Time, EntryCandle?.Bar, Entry, Stop, Target, Risk, ExitTime, ExitBar, ExitReason, ExitPrice,
            _barsObserved, _mfe, _mae, ExitReason is "TP" or "SL" or "TimeStop");

        private void Complete(OpfCandle candle, string reason, decimal price)
        {
            Completed = true;
            ExitReason = reason;
            ExitPrice = price;
            ExitBar = candle.Bar;
            ExitTime = candle.Time;
        }
    }
}
