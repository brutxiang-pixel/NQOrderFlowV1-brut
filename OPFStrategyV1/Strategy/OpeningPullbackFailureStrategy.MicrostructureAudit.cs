using ATAS.Indicators;
using OPFStrategyV1.Research;

namespace OPFStrategyV1.Strategy;

public sealed partial class OpeningPullbackFailureStrategy
{
    private readonly Dictionary<(int Bar, string Source), MicrostructureAuditAccumulator> _microstructureAudit = new();
    private readonly List<ResearchLogger.MicrostructureAuditSample> _microstructureAuditSamples = [];
    private readonly HashSet<(int Bar, string Source, string DataType, string Kind)> _microstructureAuditSampleKeys = new();
    private readonly Dictionary<string, long> _microstructureSourceSequences = new(StringComparer.Ordinal);
    private decimal? _microstructureBestBid;
    private decimal? _microstructureBestAsk;
    private long _microstructureSequence;
    private int _microstructureDepthSnapshotErrorCount;

    private void InitializeMicrostructureAudit()
    {
        _microstructureAudit.Clear();
        _microstructureAuditSamples.Clear();
        _microstructureAuditSampleKeys.Clear();
        _microstructureSourceSequences.Clear();
        _microstructureBestBid = null;
        _microstructureBestAsk = null;
        _microstructureSequence = 0;
        _microstructureDepthSnapshotErrorCount = 0;
    }

    protected override void OnNewTrade(MarketDataArg trade)
    {
        base.OnNewTrade(trade);
        RecordMicrostructureEvent("OnNewTrade", trade);
        RecordFootprintTrade(trade);
        RecordZoneBehaviorTrade(trade);
        RecordMarketExecutionTapeTrade(trade);
    }

    protected override void OnNewTrades(IEnumerable<MarketDataArg> trades)
    {
        var batch = trades as ICollection<MarketDataArg> ?? trades.ToArray();
        base.OnNewTrades(batch);
        foreach (var trade in batch)
            RecordMicrostructureEvent("OnNewTrades", trade);
    }

    protected override void MarketDepthChanged(MarketDataArg depth)
    {
        base.MarketDepthChanged(depth);
        RecordMicrostructureEvent("MarketDepthChanged", depth);
    }

    protected override void MarketDepthsChanged(IEnumerable<MarketDataArg> depths)
    {
        var batch = depths as ICollection<MarketDataArg> ?? depths.ToArray();
        base.MarketDepthsChanged(batch);
        foreach (var depth in batch)
            RecordMicrostructureEvent("MarketDepthsChanged", depth);
    }

    protected override void OnBestBidAskChanged(MarketDataArg depth)
    {
        base.OnBestBidAskChanged(depth);
        UpdateMicrostructureBestQuote(depth);
        RecordMicrostructureEvent("OnBestBidAskChanged", depth);
    }

    private void CaptureMicrostructureDepthSnapshot(int bar)
    {
        if (!MicrostructureAuditCollectionOnly)
            return;

        try
        {
            foreach (var depth in MarketDepthInfo?.GetMarketDepthSnapshot() ?? Enumerable.Empty<MarketDataArg>())
                RecordMicrostructureEvent("DepthSnapshot", depth, bar);
        }
        catch
        {
            _microstructureDepthSnapshotErrorCount++;
        }
    }

    private void RecordMicrostructureEvent(string source, MarketDataArg data, int? barOverride = null)
    {
        if (!MicrostructureAuditCollectionOnly || _snapshot is null || _researchLogger is null)
            return;

        var key = (barOverride ?? Math.Max(_lastSeenBar, 0), source);
        if (!_microstructureAudit.TryGetValue(key, out var audit))
        {
            audit = new MicrostructureAuditAccumulator(key.Item1, source);
            _microstructureAudit[key] = audit;
        }

        var sequence = ++_microstructureSequence;
        var sourceSequence = _microstructureSourceSequences.TryGetValue(source, out var currentSourceSequence)
            ? currentSourceSequence + 1
            : 1;
        _microstructureSourceSequences[source] = sourceSequence;
        var quoteAudit = EvaluateMicrostructureQuote(source, data);
        audit.Add(sequence, sourceSequence, data, quoteAudit);

        var dataType = data.DataType.ToString();
        var sampleKind = quoteAudit.IsOutOfBand ? "OutOfBand" : "First";
        if (_microstructureAuditSampleKeys.Add((key.Item1, source, dataType, sampleKind)))
        {
            _microstructureAuditSamples.Add(new ResearchLogger.MicrostructureAuditSample(
                key.Item1,
                source,
                sequence,
                sourceSequence,
                data.Time,
                data.Price,
                data.OriginPrice,
                data.Volume,
                data.IsBid,
                data.IsAsk,
                data.Direction.ToString(),
                dataType,
                sampleKind));
        }
    }

    private void UpdateMicrostructureBestQuote(MarketDataArg data)
    {
        if (!MicrostructureAuditCollectionOnly || data.Price <= 0m)
            return;

        var dataType = data.DataType.ToString();
        if (data.IsBid || string.Equals(dataType, "Bid", StringComparison.Ordinal))
            _microstructureBestBid = data.Price;
        else if (data.IsAsk || string.Equals(dataType, "Ask", StringComparison.Ordinal))
            _microstructureBestAsk = data.Price;
    }

    private MicrostructureQuoteAudit EvaluateMicrostructureQuote(string source, MarketDataArg data)
    {
        if (source is not ("MarketDepthChanged" or "MarketDepthsChanged" or "DepthSnapshot"))
            return MicrostructureQuoteAudit.NotApplicable;

        var dataType = data.DataType.ToString();
        var isBid = data.IsBid || string.Equals(dataType, "Bid", StringComparison.Ordinal);
        var isAsk = data.IsAsk || string.Equals(dataType, "Ask", StringComparison.Ordinal);
        var reference = isBid ? _microstructureBestBid : isAsk ? _microstructureBestAsk : null;
        if (!reference.HasValue)
            return new MicrostructureQuoteAudit(true, false, false, false, false);

        var sideConsistent = isBid ? data.Price <= reference.Value : isAsk && data.Price >= reference.Value;
        var inBand = data.Price > 0m && Math.Abs(data.Price - reference.Value) <= 1000m;
        return new MicrostructureQuoteAudit(true, true, inBand, sideConsistent, !inBand);
    }

    private void FlushMicrostructureAudit()
    {
        if (!MicrostructureAuditCollectionOnly || _snapshot is null || _researchLogger is null)
            return;

        foreach (var audit in _microstructureAudit.Values.OrderBy(x => x.Bar).ThenBy(x => x.Source, StringComparer.Ordinal))
            _researchLogger.AppendMicrostructureAudit(_snapshot.SnapshotId, audit.ToRow());
        foreach (var sample in _microstructureAuditSamples.OrderBy(x => x.Bar).ThenBy(x => x.Source, StringComparer.Ordinal).ThenBy(x => x.ArrivalSequence))
            _researchLogger.AppendMicrostructureAuditSample(_snapshot.SnapshotId, sample);

        _researchLogger.AppendInfo(
            _snapshot.SnapshotId,
            _lastSeenBar,
            DateTime.UtcNow,
            $"MICROSTRUCTURE_AUDIT_SUMMARY sources={_microstructureAudit.Values.Select(x => x.Source).Distinct().Count()} depthSnapshotErrors={_microstructureDepthSnapshotErrorCount}");

        _microstructureAudit.Clear();
        _microstructureAuditSamples.Clear();
        _microstructureAuditSampleKeys.Clear();
    }

    private sealed class MicrostructureAuditAccumulator(int bar, string source)
    {
        private DateTime? _lastTime;
        private readonly Dictionary<string, (int Count, decimal Volume)> _directionSummary = new(StringComparer.Ordinal);
        private readonly Dictionary<string, (int Count, decimal Volume)> _dataTypeSummary = new(StringComparer.Ordinal);

        public int Bar { get; } = bar;
        public string Source { get; } = source;
        public long FirstSequence { get; private set; }
        public long LastSequence { get; private set; }
        public long FirstSourceSequence { get; private set; }
        public long LastSourceSequence { get; private set; }
        public DateTime? FirstTime { get; private set; }
        public DateTime? LastTime { get; private set; }
        public int EventCount { get; private set; }
        public decimal TotalVolume { get; private set; }
        public decimal BidVolume { get; private set; }
        public decimal AskVolume { get; private set; }
        public decimal UnknownVolume { get; private set; }
        public int NonMonotonicTimeCount { get; private set; }
        public decimal MinPrice { get; private set; }
        public decimal MaxPrice { get; private set; }
        public decimal MinOriginPrice { get; private set; }
        public decimal MaxOriginPrice { get; private set; }
        public int PriceOriginDifferenceCount { get; private set; }
        public int NonPositivePriceCount { get; private set; }
        public int NonPositiveOriginPriceCount { get; private set; }
        public int QuoteReferenceCount { get; private set; }
        public int QuoteMissingReferenceCount { get; private set; }
        public int QuoteInBandCount { get; private set; }
        public int QuoteOutOfBandCount { get; private set; }
        public int QuoteSideConsistentCount { get; private set; }

        public void Add(long sequence, long sourceSequence, MarketDataArg data, MicrostructureQuoteAudit quoteAudit)
        {
            var volume = Math.Max(data.Volume, 0m);
            if (EventCount == 0)
            {
                FirstSequence = sequence;
                FirstSourceSequence = sourceSequence;
                FirstTime = data.Time;
                MinPrice = data.Price;
                MaxPrice = data.Price;
                MinOriginPrice = data.OriginPrice;
                MaxOriginPrice = data.OriginPrice;
            }
            else
            {
                MinPrice = Math.Min(MinPrice, data.Price);
                MaxPrice = Math.Max(MaxPrice, data.Price);
                MinOriginPrice = Math.Min(MinOriginPrice, data.OriginPrice);
                MaxOriginPrice = Math.Max(MaxOriginPrice, data.OriginPrice);
            }

            if (_lastTime.HasValue && data.Time < _lastTime.Value)
                NonMonotonicTimeCount++;

            _lastTime = data.Time;
            LastTime = data.Time;
            LastSequence = sequence;
            LastSourceSequence = sourceSequence;
            EventCount++;
            TotalVolume += volume;
            var direction = data.Direction.ToString();
            if (string.IsNullOrWhiteSpace(direction))
                direction = "<empty>";
            if (_directionSummary.TryGetValue(direction, out var directionAggregate))
                _directionSummary[direction] = (directionAggregate.Count + 1, directionAggregate.Volume + volume);
            else
                _directionSummary[direction] = (1, volume);

            var dataType = data.DataType.ToString();
            if (_dataTypeSummary.TryGetValue(dataType, out var dataTypeAggregate))
                _dataTypeSummary[dataType] = (dataTypeAggregate.Count + 1, dataTypeAggregate.Volume + volume);
            else
                _dataTypeSummary[dataType] = (1, volume);

            if (data.Price != data.OriginPrice)
                PriceOriginDifferenceCount++;
            if (data.Price <= 0m)
                NonPositivePriceCount++;
            if (data.OriginPrice <= 0m)
                NonPositiveOriginPriceCount++;

            if (quoteAudit.Applies)
            {
                if (quoteAudit.HasReference)
                    QuoteReferenceCount++;
                else
                    QuoteMissingReferenceCount++;
                if (quoteAudit.IsInBand)
                    QuoteInBandCount++;
                if (quoteAudit.IsOutOfBand)
                    QuoteOutOfBandCount++;
                if (quoteAudit.IsSideConsistent)
                    QuoteSideConsistentCount++;
            }

            if (data.IsBid)
                BidVolume += volume;
            else if (data.IsAsk)
                AskVolume += volume;
            else
                UnknownVolume += volume;
        }

        public ResearchLogger.MicrostructureAuditBar ToRow() => new(
            Bar,
            Source,
            FirstSequence,
            LastSequence,
            FirstSourceSequence,
            LastSourceSequence,
            FirstTime,
            LastTime,
            EventCount,
            TotalVolume,
            BidVolume,
            AskVolume,
            UnknownVolume,
            string.Join("|", _directionSummary.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => $"{x.Key}:{x.Value.Count}/{x.Value.Volume}")),
            string.Join("|", _dataTypeSummary.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => $"{x.Key}:{x.Value.Count}/{x.Value.Volume}")),
            NonMonotonicTimeCount,
            MinPrice,
            MaxPrice,
            MinOriginPrice,
            MaxOriginPrice,
            PriceOriginDifferenceCount,
            NonPositivePriceCount,
            NonPositiveOriginPriceCount,
            QuoteReferenceCount,
            QuoteMissingReferenceCount,
            QuoteInBandCount,
            QuoteOutOfBandCount,
            QuoteSideConsistentCount);
    }

    private readonly record struct MicrostructureQuoteAudit(
        bool Applies,
        bool HasReference,
        bool IsInBand,
        bool IsSideConsistent,
        bool IsOutOfBand)
    {
        public static readonly MicrostructureQuoteAudit NotApplicable = new(false, false, false, false, false);
    }
}
