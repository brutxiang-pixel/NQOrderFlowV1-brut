using ATAS.Indicators;
using ATAS.DataFeedsCore;
using ATAS.DataFeedsCore.Pricing;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.ComponentModel;
using OPFStrategyV1.Core.MarketData;
using OPFStrategyV1.Core.Signals;
using OPFStrategyV1.Core.Versions;
using OFT.Platform.Core.Providers.Interfaces;
using Utils.Common.Collections.Synchronized;

namespace OPFStrategyV1.Strategy;

public sealed partial class OpeningPullbackFailureStrategy
{
    private readonly List<Security> _esOptionsProxyOptions = [];
    private readonly Dictionary<string, IOptionQuoteSubscription> _esOptionsProxySubscriptions = new(StringComparer.OrdinalIgnoreCase);
    private EsOptionsProxySnapshot _esOptionsProxy = EsOptionsProxySnapshot.Unavailable("notInitialized");
    private bool _esOptionsProxyInitStarted;
    private DateTime _esOptionsProxyLastLogUtc = DateTime.MinValue;
    private readonly object _esOptionsProxySync = new();
    private CancellationTokenSource? _esOptionsProxyCancellation;
    private string _esOptionsChartRoot = "";
    private DateTime _esOptionsProxyLastPublishUtc = DateTime.MinValue;
    private DateTime _esOptionsProxyLastArchiveUtc = DateTime.MinValue;
    private DateTime _esOptionsProxyLastHealthLogUtc = DateTime.MinValue;
    private string _esOptionsProxyRetryReason = "-";
    private readonly Queue<(DateTime Time, decimal Price)> _esOptionsPriceHistory = new();
    private decimal? _esExpectedMoveAnchor;
    private decimal? _esExpectedMoveAtAnchor;
    private DateTime? _esAnchorTradingDay;
    private DateTime? _esAnchorEstablished;
    private string? _esAnchorSource;
    private string? _esAnchorSeries;
    private readonly Dictionary<string, DateTime> _esQuoteReceived = new(StringComparer.OrdinalIgnoreCase);
    private decimal? _esObservedPrice;
    private DateTime? _esPriceReceived;
    private const string EsOptionChainSource = "RITHMIC_ES_OPTION_CHAIN_RESPONSE";
    private IDataFeedConnector? _rithmicOptionsConnector;

    [Category("OPF Options")]
    [DisplayName("Enable ES Options Entry Gate")]
    public bool EnableEsOptionsEntryGate { get; set; } = true;

    private static string OptionsProxyArchivePath => Path.Combine(GexConfigDirectory(), "OPF_es_options_proxy.jsonl");
    private static string OptionsProxyLatestPath => Path.Combine(GexConfigDirectory(), "OPF_es_options_proxy_latest.json");

    private EsOptionsProxySnapshot ReadEsOptionsProxyArchiveAsOf(DateTime decisionUtc)
    {
        return OptionsProxySnapshotStore.ReadAsOf(OptionsProxyArchivePath, decisionUtc, EsOptionChainSource);
    }
    private bool IsEsOptionsSource => _esOptionsChartRoot == "ES";
    private bool IsEsOptionsConsumer => _esOptionsChartRoot is "NQ" or "MNQ" or "MES";
    private bool IsEsOptionsTradingChart => IsEsOptionsSource || IsEsOptionsConsumer;

    private bool IsHistoricalOptionsContext(DateTime time)
    {
        DateTime lastTrade;
        lock (_intradaySync) lastTrade = _lastEntryTradeTime;
        return ExecutionClock.IsHistorical(time, DateTime.UtcNow, lastTrade == default ? null : lastTrade);
    }

    private void InitializeEsOptionsProxy()
    {
        if (!IsEsOptionsTradingChart) return;
        _esObservedPrice = null;
        _esPriceReceived = null;
        _esQuoteReceived.Clear();
        _esOptionsProxyCancellation = new CancellationTokenSource();
        var saved = EsOptionsProxySnapshot.Unavailable(IsEsOptionsConsumer ? "waitingForEsSource" : "initializing");
        try
        {
            saved = OptionsProxySnapshotStore.ReadLatest(OptionsProxyLatestPath);
            if (saved.Source != EsOptionChainSource)
                saved = EsOptionsProxySnapshot.Unavailable("waitingForRithmicOptionChain");
            if (saved.HasAnchorFor(OptionsProxyExpiry.ExchangeDate(DateTime.UtcNow), DateTime.UtcNow))
            {
                _esAnchorTradingDay = saved.AnchorTradingDay;
                _esExpectedMoveAnchor = saved.ExpectedMoveAnchor;
                _esExpectedMoveAtAnchor = saved.ExpectedMoveAtAnchor;
                _esAnchorEstablished = saved.AnchorEstablishedAtUtc;
                _esAnchorSource = saved.AnchorSource;
                _esAnchorSeries = saved.AnchorSeries;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        lock (_esOptionsProxySync)
            _esOptionsProxy = saved.Status == "ModelReady"
                && !IsEsOptionsSource
                ? saved : EsOptionsProxySnapshot.Unavailable(IsEsOptionsConsumer ? "waitingForEsSource" : "initializing");
        _ = RunEsOptionsProxyAsync(_esOptionsProxyCancellation.Token);
    }

    private async Task RunEsOptionsProxyAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                if (IsEsOptionsConsumer) RefreshEsOptionsSharedContext(token);
                if (IsEsOptionsSource)
                {
                    var expired = false;
                    lock (_esOptionsProxySync)
                    {
                        token.ThrowIfCancellationRequested();
                        var reload = _esOptionsProxyOptions.All(x => OptionsProxyExpiry.ToUtc(x.Expiration, x.Code ?? "") <= DateTime.UtcNow);
                        reload |= _rithmicOptionsConnector?.IsConnected != true;
                        if (_esOptionsProxyInitStarted && reload)
                        {
                            ClearEsOptionsSubscriptions();
                            _esOptionsProxyInitStarted = false;
                            expired = true;
                        }
                    }
                    if (expired)
                    {
                        lock (_esOptionsProxySync)
                            SetEsOptionsProxy(EsOptionsProxySnapshot.Unavailable("optionFeedReloading"));
                    }
                }
                if (IsEsOptionsSource && !_esOptionsProxyInitStarted)
                {
                    using var attempt = CancellationTokenSource.CreateLinkedTokenSource(token);
                    attempt.CancelAfter(TimeSpan.FromSeconds(60));
                    await InitializeEsOptionsProxyAsync(attempt.Token, token).ConfigureAwait(false);
                    await Task.Delay(TimeSpan.FromSeconds(10), token).ConfigureAwait(false);
                    continue;
                }
                if (IsEsOptionsSource)
                {
                    lock (_esOptionsProxySync)
                        if (_esOptionsProxyInitStarted && _esOptionsProxyOptions.Count > 0)
                            RecalculateEsOptionsProxy(_esOptionsProxyOptions.Min(x => x.Expiration));
                    LogEsOptionsProxyHealth();
                }
                await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
    }

    private void RefreshEsOptionsSharedContext(CancellationToken token = default)
    {
        if (!IsEsOptionsConsumer) return;
        try
        {
            var replayTime = _lastResearchCandle?.Time;
            var context = replayTime.HasValue && IsHistoricalOptionsContext(replayTime.Value)
                ? ReadEsOptionsProxyArchiveAsOf(DateTime.SpecifyKind(replayTime.Value, DateTimeKind.Utc))
                : OptionsProxySnapshotStore.ReadLatest(OptionsProxyLatestPath);
            if (!(replayTime.HasValue && IsHistoricalOptionsContext(replayTime.Value)) &&
                context.Status == "ModelReady" && context.Source != EsOptionChainSource)
                context = EsOptionsProxySnapshot.Unavailable("waitingForRithmicOptionChain");
            lock (_esOptionsProxySync)
            {
                token.ThrowIfCancellationRequested();
                    SetEsOptionsProxy(context);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            lock (_esOptionsProxySync)
            {
                token.ThrowIfCancellationRequested();
                SetEsOptionsProxy(EsOptionsProxySnapshot.Unavailable($"sharedReadFailed:{ex.GetType().Name}"));
            }
        }
    }

    private async Task InitializeEsOptionsProxyAsync(CancellationToken token, CancellationToken lifetime)
    {
        var phase = "chartOptionsProvider";
        void State(EsOptionsProxySnapshot state)
        {
            if (lifetime.IsCancellationRequested) return;
            if (state.Status == "ModelReady")
            {
                lock (_esOptionsProxySync) SetEsOptionsProxy(state);
            }
            else
                SetEsOptionsProxyTransient(state.Detail);
        }
        try
        {
            var provider = DataProvider?.OptionsDataProvider;
            if (provider?.IsAvailable != true)
            {
                State(EsOptionsProxySnapshot.Unavailable("chartOptionsProviderUnavailable"));
                return;
            }

            phase = "verifyRithmicRoute";
            var instrument = Security is { } security && FindAccountTradingCore() is IInstrumentProvider instruments
                ? instruments.TryGetInstrument(security)
                : null;
            var routes = instrument?.SecuritiesByConnector.Sync(securities => securities
                .Where(x => x.Value is not null && x.Key.IsConnected &&
                    x.Key is IOptionsDataFeed { IsOptionsDataUnavailable: false })
                .ToArray());
            if (routes is not { Length: > 0 })
            {
                State(EsOptionsProxySnapshot.Unavailable("waitingForRithmicEsContract"));
                return;
            }
            if (routes.Any(x => x.Key.GetType().Assembly.GetName().Name != "OFT.Rithmic"))
            {
                State(EsOptionsProxySnapshot.Unavailable("chartOptionsRouteNotRithmicOnly"));
                return;
            }

            phase = "getRithmicSeries";
            State(EsOptionsProxySnapshot.Unavailable("loadingRithmicSeries"));
            var series = await provider.GetOptionSeriesAsync(token).WaitAsync(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var selected = series.Where(x => x.Expiration.Date >= OptionsProxyExpiry.ExchangeDate(DateTime.UtcNow)).OrderBy(x => x.Expiration).FirstOrDefault();
            if (selected.Equals(default(OptionSeries))) { State(EsOptionsProxySnapshot.Unavailable("noFutureSeries")); return; }
            State(EsOptionsProxySnapshot.Unavailable("loadingChain"));
            phase = "getRithmicChain";
            var chain = (await provider.GetOptionsAsync(selected, token).WaitAsync(token).ConfigureAwait(false))
                .Where(IsEsOrSpxOption).Where(x => x.StrikePrice is > 0 && x.OptionType is not null).ToArray();
            token.ThrowIfCancellationRequested();
            var route = routes.FirstOrDefault(x => x.Key.IsConnected && chain.Length > 0 &&
                chain.All(option => ReferenceEquals(option.UnderlyingSecurity, x.Value)));
            if (chain.Length > 0 && route.Key is null)
            {
                State(EsOptionsProxySnapshot.Unavailable("rithmicChainOriginUnverified"));
                return;
            }
            phase = "subscribeRithmicQuotes";
            string? subscriptionError = null;
            var reference = OptionUnderlyingPrice(chain);
            var options = reference.HasValue
                ? chain.OrderBy(x => Math.Abs(x.StrikePrice!.Value - reference.Value)).Take(240).ToArray()
                : chain.Take(240).ToArray();
            if (options.Length == 0) { State(EsOptionsProxySnapshot.Unavailable("providerReturnedNoEsSpxOptions")); return; }
            foreach (var option in options)
            {
                token.ThrowIfCancellationRequested();
                lock (_esOptionsProxySync)
                {
                token.ThrowIfCancellationRequested();
                try
                {
                    var subscription = provider.SubscribeToOption(option);
                    if (subscription is null) continue;
                    if (!ReferenceEquals(subscription.Option, option))
                    {
                        subscription.Dispose();
                        subscriptionError ??= "subscriptionOptionMismatch";
                        continue;
                    }
                    _rithmicOptionsConnector = route.Key;
                    _esOptionsProxyOptions.Add(option);
                    subscription.Changed += OnEsOptionsProxyQuoteChanged;
                    _esOptionsProxySubscriptions[ProxyOptionKey(option)] = subscription;
                }
                catch (Exception ex)
                {
                    subscriptionError ??= ex.GetType().Name;
                }
                }
            }
            lock (_esOptionsProxySync)
            {
                token.ThrowIfCancellationRequested();
                _esOptionsProxyInitStarted = _esOptionsProxySubscriptions.Count > 0;
                if (!_esOptionsProxyInitStarted)
                {
                    State(EsOptionsProxySnapshot.Unavailable($"initFailed:{phase}:{subscriptionError ?? "noSubscriptions"}"));
                    return;
                }
                phase = "recalculateRithmicModel";
                RecalculateEsOptionsProxy(selected.Expiration);
            }
        }
        catch (OperationCanceledException)
        {
            SetEsOptionsProxyTransient("initializationTimedOutRetry10s");
        }
        catch (Exception ex)
        {
            SetEsOptionsProxyTransient($"initFailed:{phase}:{ex.GetType().Name}");
        }
        finally
        {
            lock (_esOptionsProxySync)
                if (!_esOptionsProxyInitStarted && !lifetime.IsCancellationRequested) ClearEsOptionsSubscriptions();
        }
    }

    private static bool IsEsOrSpxOption(Security option)
    {
        var underlying = option.UnderlyingSecurity;
        var text = underlying is null ? option.Code : underlying.Code;
        // ES quote and strikes must use the same coordinate system in phase one.
        return System.Text.RegularExpressions.Regex.IsMatch(text ?? string.Empty, @"^ES(?=$|[^A-Z]|[FGHJKMNQUVXZ][0-9])", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }


    private void OnEsOptionsProxyQuoteChanged(IOptionQuoteSubscription subscription)
    {
        lock (_esOptionsProxySync)
        {
        if (_esOptionsProxyCancellation?.IsCancellationRequested != false) return;
        if (!_esOptionsProxySubscriptions.Values.Any(x => ReferenceEquals(x, subscription))) return;
        foreach (var pair in _esOptionsProxySubscriptions.Where(x => ReferenceEquals(x.Value, subscription)))
            _esQuoteReceived[pair.Key] = DateTime.UtcNow;
        // The one-second source loop recalculates once for the batch of quote changes.
        }
    }

    private void RecalculateEsOptionsProxy(DateTime expiration)
    {
        if (_rithmicOptionsConnector?.IsConnected != true)
        {
            SetEsOptionsProxy(EsOptionsProxySnapshot.Unavailable("rithmicOptionsDisconnected"));
            return;
        }
        foreach (var option in _esOptionsProxyOptions)
        {
            var summaryOi = _esOptionsProxySubscriptions.GetValueOrDefault(ProxyOptionKey(option))?.Summary?.OpenInterest;
            if (summaryOi is > 0m) option.OpenInterest = summaryOi;
        }
        var options = _esOptionsProxyOptions.Where(x => x.StrikePrice is > 0 && x.OpenInterest is > 0 && x.OptionType is not null).ToArray();
        if (options.Length < 4) { SetEsOptionsProxy(EsOptionsProxySnapshot.Unavailable($"insufficientOpenInterest chain={_esOptionsProxyOptions.Count} oi={options.Length} subscriptions={_esOptionsProxySubscriptions.Count}")); return; }
        var strikes = options.Select(x => x.StrikePrice!.Value).OrderBy(x => x).ToArray();
        var spot = _esObservedPrice ?? OptionUnderlyingPrice(options) ?? strikes[strikes.Length / 2];
        var atm = strikes.OrderBy(x => Math.Abs(x - spot)).First();
        var calls = options.Where(x => x.OptionType == OptionTypes.Call).ToArray();
        var puts = options.Where(x => x.OptionType == OptionTypes.Put).ToArray();
        var callWall = calls.OrderByDescending(x => x.OpenInterest!.Value).FirstOrDefault()?.StrikePrice;
        var putWall = puts.OrderByDescending(x => x.OpenInterest!.Value).FirstOrDefault()?.StrikePrice;
        var exposures = new List<(Security Option, double Iv, double Gamma, double Exposure)>();
        if (_esObservedPrice is null || _esPriceReceived is null || _esOptionsPriceHistory.Count == 0)
        {
            SetEsOptionsProxy(EsOptionsProxySnapshot.Unavailable("underlyingQuoteMissing"));
            return;
        }
        var rejectedQuote = 0;
        var rejectedExpiry = 0;
        var rejectedIv = 0;
        foreach (var option in options)
        {
            if (!_esQuoteReceived.TryGetValue(ProxyOptionKey(option), out var quoteReceived) || !OptionsProxyEntryGate.CanPublishQuote(quoteReceived, DateTime.UtcNow))
            { rejectedQuote++; continue; }
            var quote = _esOptionsProxySubscriptions.GetValueOrDefault(ProxyOptionKey(option))?.Summary;
            var bid = quote?.BestBidPrice ?? option.BestBidPrice;
            var ask = quote?.BestAskPrice ?? option.BestAskPrice;
            if (bid <= 0m || ask < bid) { rejectedQuote++; continue; }
            var t = (OptionsProxyExpiry.ToUtc(option.Expiration, option.Code ?? string.Empty) - DateTime.UtcNow).TotalDays / 365.25d;
            if (t <= 0d) { rejectedExpiry++; continue; }
            var iv = BlackScholesPricer.ImpliedVolatility(option.OptionType!.Value, (double)((bid + ask) / 2m), (double)spot, (double)option.StrikePrice!.Value, t, 0d, 0d);
            if (iv is null || !double.IsFinite(iv.Value) || iv <= 0d) { rejectedIv++; continue; }
            var gamma = BlackScholesPricer.CalculateGreeks(option.OptionType.Value, (double)spot, (double)option.StrikePrice.Value, t, iv.Value, 0d, 0d).Gamma;
            // OI reveals concentration, not the dealer's long/short position.
            var exposure = gamma * (double)option.OpenInterest!.Value;
            if (double.IsFinite(exposure)) exposures.Add((option, iv.Value, gamma, exposure));
        }
        if (exposures.Count < 4 || !exposures.Any(x => x.Option.OptionType == OptionTypes.Call) || !exposures.Any(x => x.Option.OptionType == OptionTypes.Put))
        {
            var sample = options.First();
            SetEsOptionsProxy(EsOptionsProxySnapshot.Unavailable($"insufficientValidIvQuotes chain={_esOptionsProxyOptions.Count} oi={options.Length} valid={exposures.Count} quoteReject={rejectedQuote} expiryReject={rejectedExpiry} ivReject={rejectedIv} spot={spot} sample={sample.Code} strike={sample.StrikePrice} expiry={sample.Expiration:O}"));
            return;
        }
        const decimal normalizedGamma = 0m;
        const string regime = "DealerGammaUnknown";
        callWall = exposures.Where(x => x.Option.OptionType == OptionTypes.Call).GroupBy(x => x.Option.StrikePrice).OrderByDescending(g => g.Sum(x => Math.Abs(x.Exposure))).First().Key;
        putWall = exposures.Where(x => x.Option.OptionType == OptionTypes.Put).GroupBy(x => x.Option.StrikePrice).OrderByDescending(g => g.Sum(x => Math.Abs(x.Exposure))).First().Key;
        decimal? zeroGamma = null; // A balanced net exposure is not a spatial gamma flip.
        var expectedMove = EstimateAtmStraddle(exposures.Select(x => x.Option), atm);
        var tradingDay = OptionsProxyExpiry.ExchangeDate(DateTime.UtcNow);
        var seriesKey = expiration.ToString("O", CultureInfo.InvariantCulture);
        if (_esAnchorTradingDay != tradingDay || _esAnchorSeries != seriesKey)
        {
            _esAnchorTradingDay = tradingDay;
            _esExpectedMoveAnchor = null;
            _esExpectedMoveAtAnchor = null;
            _esAnchorEstablished = null;
            _esAnchorSeries = seriesKey;
        }
        if (_esExpectedMoveAnchor is null && expectedMove is > 0m)
        {
            _esExpectedMoveAnchor = spot;
            _esExpectedMoveAtAnchor = expectedMove;
            _esAnchorEstablished = DateTime.UtcNow;
            _esAnchorSource = "FirstValidSessionObservation";
        }
        var concentrations = exposures.GroupBy(x => x.Option.StrikePrice!.Value)
            .Select(g => (Strike: g.Key, Exposure: g.Sum(x => x.Exposure))).OrderByDescending(x => x.Exposure).ToArray();
        var coverage = (decimal)exposures.Count / Math.Max(_esOptionsProxyOptions.Count, 1);
        var now = DateTime.UtcNow;
        while (_esOptionsPriceHistory.Count > 1 && now - _esOptionsPriceHistory.Peek().Time > TimeSpan.FromSeconds(315))
            _esOptionsPriceHistory.Dequeue();
        var reference = _esOptionsPriceHistory.LastOrDefault(x => now - x.Time >= TimeSpan.FromSeconds(60));
        var reference5m = _esOptionsPriceHistory.LastOrDefault(x => now - x.Time >= TimeSpan.FromSeconds(300));
        var localOptions = _esOptionsProxyOptions.Where(x => x.StrikePrice is { } strike &&
            Math.Abs(strike - spot) <= Math.Max(5m, expectedMove ?? 5m)).ToArray();
        var validKeys = exposures.Select(x => ProxyOptionKey(x.Option)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        decimal QuoteCoverage(IEnumerable<Security> sample)
        {
            var rows = sample.ToArray();
            return rows.Length == 0 ? 0m : (decimal)rows.Count(x => validKeys.Contains(ProxyOptionKey(x))) / rows.Length;
        }
        var atmReady = exposures.Any(x => x.Option.StrikePrice == atm && x.Option.OptionType == OptionTypes.Call) &&
            exposures.Any(x => x.Option.StrikePrice == atm && x.Option.OptionType == OptionTypes.Put);
        SetEsOptionsProxy(new EsOptionsProxySnapshot("ModelReady", EsOptionChainSource, "ES", now, expiration, atm, callWall, putWall, zeroGamma, expectedMove, normalizedGamma, regime, coverage, "UnsignedGammaOi", "Gamma*OI concentrations; dealer sign unknown; observed ES response")
        {
            EvidenceSchema = 2,
            UnderlyingObservedAtUtc = _esPriceReceived,
            OptionQuotesObservedAtUtc = exposures.Select(x => _esQuoteReceived[ProxyOptionKey(x.Option)]).Min(),
            AnchorEstablishedAtUtc = _esAnchorEstablished,
            AnchorSource = _esAnchorSource,
            AnchorSeries = _esAnchorSeries,
            UnderlyingPrice = spot,
            UnderlyingPrice60sAgo = reference.Time == default ? null : reference.Price,
            PriceReferenceAtUtc = reference.Time == default ? null : reference.Time,
            UnderlyingPrice5mAgo = reference5m.Time == default ? null : reference5m.Price,
            Price5mReferenceAtUtc = reference5m.Time == default ? null : reference5m.Time,
            AtmQuotesReady = atmReady,
            LocalQuoteCoverage = QuoteCoverage(localOptions),
            CallQuoteCoverage = QuoteCoverage(_esOptionsProxyOptions.Where(x => x.OptionType == OptionTypes.Call)),
            PutQuoteCoverage = QuoteCoverage(_esOptionsProxyOptions.Where(x => x.OptionType == OptionTypes.Put)),
            AnchorTradingDay = _esAnchorTradingDay,
            ResponseHigh = _esOptionsPriceHistory.Where(x => now - x.Time <= TimeSpan.FromSeconds(65)).Max(x => x.Price),
            ResponseLow = _esOptionsPriceHistory.Where(x => now - x.Time <= TimeSpan.FromSeconds(65)).Min(x => x.Price),
            ExpectedMoveAnchor = _esExpectedMoveAnchor,
            ExpectedMoveAtAnchor = _esExpectedMoveAtAnchor,
            GammaConcentrationStrike = concentrations[0].Strike,
            GammaConcentrationShare = (decimal)(concentrations[0].Exposure / concentrations.Sum(x => x.Exposure))
        });
    }

    private decimal? EstimateAtmStraddle(IEnumerable<Security> options, decimal atm)
    {
        decimal? call = null;
        decimal? put = null;
        foreach (var option in options.Where(x => x.StrikePrice == atm))
        {
            var subscription = _esOptionsProxySubscriptions.GetValueOrDefault(ProxyOptionKey(option));
            var summary = subscription?.Summary;
            var bid = summary?.BestBidPrice ?? option.BestBidPrice;
            var ask = summary?.BestAskPrice ?? option.BestAskPrice;
            decimal? price = bid > 0m && ask >= bid ? (bid + ask) / 2m : null;
            if (price is not > 0m)
                continue;
            if (option.OptionType == OptionTypes.Call) call = price;
            if (option.OptionType == OptionTypes.Put) put = price;
        }

        return call.HasValue && put.HasValue ? call.Value + put.Value : null;
    }

    private static decimal? OptionUnderlyingPrice(IEnumerable<Security> options)
    {
        foreach (var option in options)
        {
            var underlying = option.UnderlyingSecurity;
            if (underlying is null)
                continue;
            if (underlying.LastTradePrice > 0m)
                return underlying.LastTradePrice;
            if (underlying.BestBidPrice > 0m && underlying.BestAskPrice > 0m)
                return (underlying.BestBidPrice + underlying.BestAskPrice) / 2m;
        }

        return null;
    }

    private void SetEsOptionsProxy(EsOptionsProxySnapshot snapshot)
    {
        var previous = _esOptionsProxy;
        var now = DateTime.UtcNow;
        if (IsEsOptionsSource && snapshot.Status == "Unavailable")
            snapshot = snapshot with { Source = EsOptionChainSource };
        var statusChanged = previous.Status != snapshot.Status || previous.Source != snapshot.Source;
        var publish = statusChanged || now - _esOptionsProxyLastPublishUtc >= TimeSpan.FromSeconds(1);
        _esOptionsProxy = snapshot;
        var changed = previous.Status != snapshot.Status || previous.Regime != snapshot.Regime || previous.CallWallProxy != snapshot.CallWallProxy || previous.PutWallProxy != snapshot.PutWallProxy || previous.ZeroGammaProxy != snapshot.ZeroGammaProxy;
        if (IsEsOptionsSource &&
            publish &&
            !(_lastResearchCandle is { } replayCandle && IsHistoricalReplayTime(replayCandle.Time)))
        {
            try
            {
                var archive = now - _esOptionsProxyLastArchiveUtc >= TimeSpan.FromSeconds(5);
                archive = statusChanged || (archive && snapshot.Status != "Unavailable");
                OptionsProxySnapshotStore.Publish(OptionsProxyLatestPath, OptionsProxyArchivePath, snapshot, archive);
                _esOptionsProxyLastPublishUtc = now;
                if (archive) _esOptionsProxyLastArchiveUtc = now;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _researchLogger?.AppendInfo(_snapshot?.SnapshotId ?? "-", 0, now, $"OPTIONS_PROXY_ARCHIVE_FAILED error={ex.GetType().Name}");
            }
        }
        if (_researchLogger is not null && _snapshot is not null && (changed || now - _esOptionsProxyLastLogUtc >= TimeSpan.FromMinutes(1)))
        {
            _esOptionsProxyLastLogUtc = now;
            _researchLogger.AppendInfo(_snapshot.SnapshotId, _lastResearchCandle?.Bar ?? 0, now, $"ES_OPTIONS_PROXY build=shared-v4 chart={_esOptionsChartRoot} role={(IsEsOptionsSource ? "SourceOnly" : "Consumer")} assembly={GetType().Assembly.GetName().Name} status={snapshot.Status} captured={snapshot.CapturedAtUtc:O} source={snapshot.Source} underlying={snapshot.Underlying} confidence={snapshot.Confidence} detail={snapshot.Detail}");
        }
    }

    private void SetEsOptionsProxyTransient(string detail)
    {
        _esOptionsProxyRetryReason = detail;
        lock (_esOptionsProxySync)
        {
            if (_esOptionsProxy.Status != "ModelReady")
                SetEsOptionsProxy(EsOptionsProxySnapshot.Unavailable(detail));
        }
        LogEsOptionsProxyHealth(force: true);
    }

    private void LogEsOptionsProxyHealth(bool force = false)
    {
        if (_researchLogger is null || _snapshot is null) return;
        var now = DateTime.UtcNow;
        if (!force && now - _esOptionsProxyLastHealthLogUtc < TimeSpan.FromMinutes(1)) return;
        _esOptionsProxyLastHealthLogUtc = now;
        EsOptionsProxySnapshot snapshot;
        lock (_esOptionsProxySync) snapshot = _esOptionsProxy;
        var age = snapshot.Status is "ModelReady" or "PriceReady" ? Math.Max(0, (now - snapshot.CapturedAtUtc).TotalSeconds) : -1;
        _researchLogger.AppendInfo(_snapshot.SnapshotId, _lastResearchCandle?.Bar ?? 0, now,
            $"ES_OPTIONS_PROXY_HEALTH status={snapshot.Status} ageSec={age:0} retry={_esOptionsProxyRetryReason} subscriptions={_esOptionsProxySubscriptions.Count} options={_esOptionsProxyOptions.Count}");
    }

    private void AppendEsOptionsProxyHud(StringBuilder hud)
    {
        if (_esOptionsChartRoot is "GC" or "MGC")
        {
            hud.AppendLine("ES OptProxy: N/A | Local signals");
            return;
        }
        EsOptionsProxySnapshot s;
        lock (_esOptionsProxySync) s = _esOptionsProxy;
        var replay = _lastResearchCandle is { } candle && IsHistoricalOptionsContext(candle.Time);
        var clock = replay ? DateTime.SpecifyKind(_lastResearchCandle!.Time, DateTimeKind.Utc) : DateTime.UtcNow;
        if (replay)
        {
            try { s = ReadEsOptionsProxyArchiveAsOf(clock); }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            { s = EsOptionsProxySnapshot.Unavailable($"replayArchiveUnavailable:{ex.GetType().Name}"); }
        }
        var age = clock - s.CapturedAtUtc;
        var freshness = s.Status is not ("ModelReady" or "PriceReady") ? "UNAVAILABLE" : age.TotalSeconds < 0 ? "FUTURE" : age.TotalSeconds <= 15 ? "LIVE" : age.TotalSeconds <= 90 ? "DELAYED" : "STALE";
        var role = IsEsOptionsSource ? (IsEsOptionsTradingChart ? "Source + Trading" : "SourceOnly") : "Consumer";
        hud.AppendLine($"ES OptProxy [{StrategyVersions.StrategyVersion}]: {role} {s.Status}");
        hud.AppendLine($"ES OptProxy mode={(replay ? "REPLAY-ASOF" : "LIVE")} clock(BJT)={clock.AddHours(8):MM-dd HH:mm:ss}");
        hud.AppendLine($"ES OptProxy {freshness} age={Math.Max(0, age.TotalSeconds):0}s coverage={s.Coverage:P0}");
        hud.AppendLine($"ES quality ATM={s.AtmQuotesReady} local={s.LocalQuoteCoverage:P0} call={s.CallQuoteCoverage:P0} put={s.PutQuoteCoverage:P0}");
        hud.AppendLine($"ES response M5={(s.UnderlyingPrice - s.UnderlyingPrice5mAgo):0.##} M1={(s.UnderlyingPrice - s.UnderlyingPrice60sAgo):0.##} anchorDay={s.AnchorTradingDay:MM-dd}");
        hud.AppendLine($"ES OptProxy update(BJT)={(s.Status is "ModelReady" or "PriceReady" ? s.CapturedAtUtc.ToUniversalTime().AddHours(8).ToString("MM-dd HH:mm:ss") : "-")}");
        hud.AppendLine($"ES OptProxy retry={_esOptionsProxyRetryReason}");
        hud.AppendLine($"ES OptProxy regime={s.Regime} confidence={s.Confidence}");
        if (s.Status != "ModelReady")
        {
            var diagnostic = s.Detail.Split(' ');
            hud.AppendLine($"ES OptProxy reason: {diagnostic[0]}");
            var counts = diagnostic.Where(x => x.StartsWith("chain=") || x.StartsWith("oi=") || x.StartsWith("valid="));
            hud.AppendLine("ES OptProxy " + string.Join(" ", counts));
            var rejects = diagnostic.Where(x => x.StartsWith("quoteReject=") || x.StartsWith("expiryReject=") || x.StartsWith("ivReject="));
            hud.AppendLine("ES OptProxy " + string.Join(" ", rejects));
        }
        hud.AppendLine($"ES OptProxy CW={FormatProxyPrice(s.CallWallProxy)} PW={FormatProxyPrice(s.PutWallProxy)} ZG={FormatProxyPrice(s.ZeroGammaProxy)}");
        hud.AppendLine($"ES OptProxy EM={FormatProxyPrice(s.ExpectedMoveProxy)} anchor={FormatProxyPrice(s.ExpectedMoveAnchor)} exp={s.Expiration?.ToString("MM-dd", CultureInfo.InvariantCulture) ?? "-"}");
        hud.AppendLine($"ES Response price={FormatProxyPrice(s.UnderlyingPrice)} d60={FormatProxyPrice(s.UnderlyingPrice - s.UnderlyingPrice60sAgo)}");
        hud.AppendLine($"ES GammaCluster={FormatProxyPrice(s.GammaConcentrationStrike)} share={s.GammaConcentrationShare:P0} paths=8+Iceberg");
        hud.AppendLine($"ES anchor={s.AnchorEstablishedAtUtc:MM-dd HH:mm} source={s.AnchorSource ?? "legacy"} quoteSeen={s.OptionQuotesObservedAtUtc:HH:mm:ss}");
    }

    private static string FormatProxyPrice(decimal? value) => value?.ToString("0.##", CultureInfo.InvariantCulture) ?? "-";
    private static string ProxyOptionKey(Security option) => string.IsNullOrWhiteSpace(option.Code) ? $"{option.StrikePrice}:{option.OptionType}:{option.Expiration:yyyyMMdd}" : option.Code;

    private void DisposeEsOptionsProxy()
    {
        _esOptionsProxyCancellation?.Cancel();
        lock (_esOptionsProxySync)
        {
        ClearEsOptionsSubscriptions();
        _esOptionsProxyInitStarted = false;
        if (IsEsOptionsSource) SetEsOptionsProxy(EsOptionsProxySnapshot.Unavailable("esSourceStopped"));
        }
    }

    private void ClearEsOptionsSubscriptions()
    {
        foreach (var subscription in _esOptionsProxySubscriptions.Values) subscription.Changed -= OnEsOptionsProxyQuoteChanged;
        foreach (var subscription in _esOptionsProxySubscriptions.Values) subscription.Dispose();
        _rithmicOptionsConnector = null;
        _esOptionsProxySubscriptions.Clear();
        _esQuoteReceived.Clear();
        _esOptionsProxyOptions.Clear();
        _esOptionsPriceHistory.Clear();
    }

    private void ObserveEsUnderlying(decimal price, DateTime marketTime)
    {
        if (!IsEsOptionsSource || price <= 0m || IsHistoricalReplayTime(marketTime)) return;
        lock (_esOptionsProxySync)
        {
            if (_esOptionsProxyCancellation?.IsCancellationRequested != false) return;
            var now = DateTime.UtcNow;
            _esObservedPrice = price;
            _esPriceReceived = now;
            if (_esOptionsPriceHistory.Count > 0 && now - _esOptionsPriceHistory.Last().Time < TimeSpan.FromSeconds(1)) return;
            _esOptionsPriceHistory.Enqueue((now, price));
            while (_esOptionsPriceHistory.Count > 1 && now - _esOptionsPriceHistory.Peek().Time > TimeSpan.FromSeconds(315))
                _esOptionsPriceHistory.Dequeue();
            var m1 = _esOptionsPriceHistory.LastOrDefault(x => now - x.Time >= TimeSpan.FromSeconds(60));
            var m5 = _esOptionsPriceHistory.LastOrDefault(x => now - x.Time >= TimeSpan.FromSeconds(300));
            var ready = _esOptionsProxy.Status == "ModelReady" && _esOptionsProxy.OptionQuotesObservedAtUtc is { } quotes && now - quotes <= TimeSpan.FromSeconds(90);
            ready &= _rithmicOptionsConnector?.IsConnected == true;
            var baseSnapshot = ready ? _esOptionsProxy : EsOptionsProxySnapshot.Unavailable(_esOptionsProxy.Detail) with { Source = EsOptionChainSource };
            SetEsOptionsProxy(baseSnapshot with { Status = ready ? "ModelReady" : "PriceReady", EvidenceSchema = 2,
                CapturedAtUtc = now, Underlying = "ES", UnderlyingPrice = price, UnderlyingObservedAtUtc = now,
                UnderlyingPrice60sAgo = m1.Time == default ? null : m1.Price, PriceReferenceAtUtc = m1.Time == default ? null : m1.Time,
                UnderlyingPrice5mAgo = m5.Time == default ? null : m5.Price, Price5mReferenceAtUtc = m5.Time == default ? null : m5.Time,
                ResponseHigh = _esOptionsPriceHistory.Where(x => now - x.Time <= TimeSpan.FromSeconds(65)).Max(x => x.Price),
                ResponseLow = _esOptionsPriceHistory.Where(x => now - x.Time <= TimeSpan.FromSeconds(65)).Min(x => x.Price) });
        }
    }

    private string? OptionsEntryGateReason(CandidateSignal signal, OpfCandle candle, string path, decimal risk)
    {
        if (IsIcebergExecutionPath(path)) return null;
        if (_esOptionsChartRoot is "GC" or "MGC")
            return path is "BreakawayFvg_Qualified" or "FailureReverse_RetestFailed" ? null : "OptionsGate:NotApplicableToMetals";
        if (!EnableEsOptionsEntryGate) return null;
        if (!IsEsOptionsTradingChart) return "OptionsGate:UnsupportedInstrument";
        RefreshEsOptionsSharedContext();
        EsOptionsProxySnapshot context;
        lock (_esOptionsProxySync) context = _esOptionsProxy;
        var historical = IsHistoricalOptionsContext(candle.Time);
        var decisionUtc = historical ? DateTime.SpecifyKind(candle.Time, DateTimeKind.Utc) : DateTime.UtcNow;
        if (historical)
        {
            context = EsOptionsProxySnapshot.Unavailable("asOfSnapshotMissing");
            try
            {
                context = ReadEsOptionsProxyArchiveAsOf(decisionUtc);
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                return $"OptionsGate:ArchiveUnavailable:{ex.GetType().Name}";
            }
        }
        var crossMarket = path is "ESResponseAlignedBreakout" or "ESWallRejectionReversal" or "ESExpectedMoveExpansion" or "ESGammaConcentrationResponse";
        if (crossMarket) context = WithLevelEvent(context, path, signal.Side, decisionUtc);
        var evidence = _intradayEvidence.GetValueOrDefault(signal.SignalId) ??
            (crossMarket ? _lastResearchCandle : candle.High > candle.Low ? candle : _lastResearchCandle);
        var reason = OptionsProxyEntryGate.Evaluate(context, decisionUtc,
            path, signal.Side == TradeSide.Long, evidence?.Open ?? candle.Open, evidence?.High ?? candle.High,
            evidence?.Low ?? candle.Low, evidence?.Close ?? candle.Close, risk);
        try
        {
            _researchLogger?.AppendValidationPacket(_snapshot?.SnapshotId ?? "-", "options",
                new OptionsGatePacket(_snapshot?.SnapshotId ?? "-", signal.SignalId, Security?.Code ?? "-", path,
                    decisionUtc, signal.Side == TradeSide.Long, evidence ?? candle, risk, context, reason));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { LogLiveReadinessInfo($"VALIDATION_OPTIONS_CAPTURE_FAILED {ex.GetType().Name}"); }
        _researchLogger?.AppendInfo(_snapshot?.SnapshotId ?? "-", candle.Bar, candle.Time,
            $"OPTIONS_ENTRY_GATE signal={signal.SignalId} path={path} side={signal.Side} regime={context.Regime} captured={context.CapturedAtUtc:O} coverage={context.Coverage:0.000} atm={context.AtmQuotesReady} local={context.LocalQuoteCoverage:0.000} call={context.CallQuoteCoverage:0.000} put={context.PutQuoteCoverage:0.000} decision={reason ?? "Allow"}");
        return reason;
    }
}
