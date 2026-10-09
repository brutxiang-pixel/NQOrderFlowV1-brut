using System.Collections;
using System.ComponentModel;
using System.Reflection;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using ATAS.DataFeedsCore;
using ATAS.DataFeedsCore.Pricing;
using ATAS.Indicators;
using OFT.DxFeed;
using OFT.Platform.Core.Managers.Connections;
using OFT.Platform.Core.Providers;
using OFT.Platform.Core.Providers.Options;
using OFT.Platform.Core.ViewModels.OptionsBoard;
using DxFeed.Graal.Net.Ipf;


namespace OPFIndicatorX;

[DisplayName("OPF X Research / GEX")]
[Category("OPF")]
public sealed class OpfResearchIndicator : Indicator
{
    private const int MaxZonePairs = 4;
    private const int MaxOptionSubscriptions = 120;
    private static readonly TimeSpan ZoneLookback = TimeSpan.FromDays(14);
    private readonly ValueDataSeries _probeSeries = CreateSeries("OPF_Close", System.Drawing.Color.DodgerBlue);
    private readonly ValueDataSeries _callWallSeries = CreateSeries("GEX_CallWall_Resistance", System.Drawing.Color.IndianRed);
    private readonly ValueDataSeries _putWallSeries = CreateSeries("GEX_PutWall_Support", System.Drawing.Color.LimeGreen);
    private readonly ValueDataSeries _zeroGammaSeries = CreateSeries("GEX_ZeroGamma_Regime", System.Drawing.Color.MediumPurple);
    private readonly ValueDataSeries _volatilityTriggerSeries = CreateSeries("GEX_VolatilityTrigger", System.Drawing.Color.DarkOrange);
    private readonly RangeDataSeries[] _zoneSeries = CreateZoneSeries();
    private readonly Dictionary<string, decimal> _gexLevels = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(decimal Low, decimal High, DateTime Created)> _zones = new();
    private readonly Dictionary<string, IOptionQuoteSubscription> _optionSubscriptions = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Security> _liveOptions = new();
    private readonly object _gexSync = new();
    private DateTime _lastZoneLoadUtc = DateTime.MinValue;
    private DateTime _lastGexSnapshotLoadUtc = DateTime.MinValue;
    private DateTime _gexSnapshotWriteUtc = DateTime.MinValue;
    private string _gexSnapshotPath = string.Empty;
    private decimal _lastPrice;
    private decimal _zoneReferencePrice;
    private IMboAnalyticsDataProvider? _mboProvider;
    private OptionsSubscriptionService? _optionsSubscriptionService;
    private IDataFeedConnector? _optionsConnector;
    private IPlatformTradingCore? _tradingCore;
    private DateTime _lastIcebergAlertUtc;
    private bool _icebergStatusAlerted;
    private bool _liveGexInitialized;
    private bool _gexInitRunning;
    private DateTime _lastGexInitAttemptUtc;
    private DateTime _lastGexStatusUtc = DateTime.MinValue;
    private string _lastGexStatusText = string.Empty;
    private bool _summaryHooked;

    public OpfResearchIndicator() : base(true)
    {
        Name = "OPF X Research / GEX";
        CustomName = Name;
        DataSeries.Add(_probeSeries);
        DataSeries.Add(_callWallSeries);
        DataSeries.Add(_putWallSeries);
        DataSeries.Add(_zeroGammaSeries);
        DataSeries.Add(_volatilityTriggerSeries);
        foreach (var series in _zoneSeries)
            DataSeries.Add(series);
        LoadHistoricalZones();
        LoadLocalGexSnapshot();
    }

    protected override void OnInitialize()
    {
        base.OnInitialize();
        try
        {
            LoadLocalGexSnapshot();
            if (DataProvider is not null && DataProvider.TryGetMboAnalyticsDataProvider(out var mboProvider))
                _mboProvider = mboProvider;
            else
                _mboProvider = null;
            if (_mboProvider is null)
            {
                PublishIcebergStatus("ICEBERG STATUS: UNAVAILABLE (MBO SERVICE NOT REGISTERED)");
                return;
            }
            _mboProvider.MboIcebergsChanged += OnMboIcebergsChanged;
            _ = _mboProvider.SubscribeMboAnalyticsData();
            PublishIcebergStatus("ICEBERG STATUS: SUBSCRIBED");
        }
        catch (Exception)
        {
            _mboProvider = null;
            PublishIcebergStatus("ICEBERG STATUS: UNAVAILABLE (MBO SUBSCRIBE FAILED)");
        }
    }

    private void TryStartLiveGexInitialization()
    {
        if (_liveGexInitialized || _gexInitRunning || DateTime.UtcNow - _lastGexInitAttemptUtc < TimeSpan.FromSeconds(10))
            return;
        _lastGexInitAttemptUtc = DateTime.UtcNow;
        _ = InitializeLiveGexAsync();
    }

    private void PublishGexStatus(string text)
    {
        if (DataProvider is null)
            return;
        if (text == _lastGexStatusText && DateTime.UtcNow - _lastGexStatusUtc < TimeSpan.FromSeconds(20))
            return;
        _lastGexStatusText = text;
        _lastGexStatusUtc = DateTime.UtcNow;
        DataProvider.AddAlert("OPF_X_GEX_STATUS", text, "OPF X Research / GEX", System.Drawing.Color.White, System.Drawing.Color.DarkSlateBlue, null);
    }

    private void PublishIcebergStatus(string text)
    {
        if (_icebergStatusAlerted || DataProvider is null)
            return;
        _icebergStatusAlerted = true;
        DataProvider.AddAlert("OPF_X_ICEBERG_STATUS", text, "OPF X Research / GEX", System.Drawing.Color.White, System.Drawing.Color.DarkSlateBlue, null);
    }

    private void OnMboIcebergsChanged(IReadOnlyList<MboIcebergRecord> records)
    {
        var now = DateTime.UtcNow;
        if (now - _lastIcebergAlertUtc < TimeSpan.FromSeconds(30))
            return;
        var iceberg = records.FirstOrDefault(x => x.NativeRefreshConfirmed && x.Hidden > 0m);
        if (iceberg is null || DataProvider is null)
            return;
        _lastIcebergAlertUtc = now;
        DataProvider.AddAlert("OPF_X_ICEBERG", $"ICEBERG CONFIRMED\nSIDE: {iceberg.Side}\nPRICE: {iceberg.Price:0.##}\nFILLED: {iceberg.Filled:0.##}\nHIDDEN: {iceberg.Hidden:0.##}", "OPF X Research / GEX", System.Drawing.Color.White, System.Drawing.Color.DarkSlateBlue, null);
    }

    protected override void OnCalculate(int bar, decimal value)
    {
        var candles = DataProvider?.CandlesDataSeries;
        if (candles is null || candles.Count == 0 || bar < 0 || bar >= candles[0].Count)
            return;

        var candle = candles[0].GetCandle(bar);
        _lastPrice = candle.Close;
        _zoneReferencePrice = candles[0].GetCandle(candles[0].Count - 1).Close;
        _probeSeries[bar] = candle.Close;
        if (DateTime.UtcNow - _lastGexSnapshotLoadUtc >= TimeSpan.FromSeconds(30))
            LoadLocalGexSnapshot();
        if (DateTime.UtcNow - _lastZoneLoadUtc >= TimeSpan.FromSeconds(30))
            LoadHistoricalZones();

        SetLevel(_callWallSeries, bar, "GEX-CW");
        SetLevel(_putWallSeries, bar, "GEX-PW");
        SetLevel(_zeroGammaSeries, bar, "GEX-ZG");
        SetLevel(_volatilityTriggerSeries, bar, "GEX-VT");
        for (var i = 0; i < _zoneSeries.Length; i++)
            _zoneSeries[i][bar] = new RangeValue();

        var selected = SelectZones();
        for (var i = 0; i < selected.Count && i < _zoneSeries.Length; i++)
        {
            var zone = selected[i];
            ApplyZoneAppearance(_zoneSeries[i], zone.Low, zone.High, _zoneReferencePrice);
            if (candle.Time >= zone.Created)
                _zoneSeries[i][bar] = new RangeValue { Lower = zone.Low, Upper = zone.High };
        }
    }

    private async Task InitializeLiveGexAsync()
    {
        _gexInitRunning = true;
        try
        {
            _tradingCore = FindTradingCore();
            _optionsSubscriptionService = TryGetService<OptionsSubscriptionService>();
            if (_optionsSubscriptionService is null && _tradingCore is not null)
                _optionsSubscriptionService = new OptionsSubscriptionService(_tradingCore);

            if (TryLoadFromOptionsBoard())
            {
                CompleteGexInit("GEX STATUS: OPTIONS BOARD LIVE");
                return;
            }

            var provider = DataProvider?.OptionsDataProvider ?? TryGetService<IOptionsDataProvider>();
            if (provider is not null && provider.IsAvailable)
            {
                var series = await provider.GetOptionSeriesAsync(CancellationToken.None);
                var selected = SelectNearestSeries(series);
                if (!selected.Equals(default(OptionSeries)))
                {
                    var options = await provider.GetOptionsAsync(selected, CancellationToken.None);
                    SubscribeOptions(options, connector: null, subscribe: option => provider.SubscribeToOption(option));
                    if (_liveOptions.Count > 0)
                    {
                        CompleteGexInit($"GEX STATUS: PROVIDER SUBSCRIBED {_liveOptions.Count}");
                        return;
                    }
                }
            }

            var connectors = ResolveConnectors();
            var optionsFeed = connectors.OfType<IOptionsDataFeed>().FirstOrDefault();
            var dxFeed = connectors.OfType<DxFeedConnector>().FirstOrDefault()
                ?? connectors.FirstOrDefault(x => x.GetType().Name.Contains("DxFeed", StringComparison.OrdinalIgnoreCase));
            var underlying = ResolveUnderlyingSecurity();
            var root = ResolveOptionRoot(underlying);

            if (optionsFeed is not null && underlying is not null)
            {
                _optionsConnector = optionsFeed as IDataFeedConnector;
                var feedSeries = await optionsFeed.GetOptionSeriesAsync(underlying, CancellationToken.None);
                var selected = SelectNearestSeries(feedSeries);
                if (!selected.Equals(default(OptionSeries)))
                {
                    var options = await optionsFeed.GetOptionsAsync(selected, CancellationToken.None);
                    SubscribeOptions(options, _optionsConnector, option => _optionsSubscriptionService!.Subscribe(_optionsConnector!, option, this));
                    if (_liveOptions.Count > 0)
                    {
                        CompleteGexInit($"GEX STATUS: FEED SUBSCRIBED {_liveOptions.Count} ROOT={root}");
                        return;
                    }
                }
            }

            if (dxFeed is not null)
            {
                _optionsConnector = dxFeed;
                var dx = dxFeed as DxFeedConnector ?? throw new InvalidOperationException("dxfeed");
                var searched = await LoadDxFeedProfilesAsync(dx, root);
                SubscribeOptions(searched, dxFeed, option => _optionsSubscriptionService?.Subscribe(dxFeed, option, this));
                if (_liveOptions.Count > 0)
                {
                    var delay = dxFeed.MarketDataDelayPeriod.ToString();
                    CompleteGexInit($"GEX STATUS: DXFEED SUBSCRIBED {_liveOptions.Count} ROOT={root} DELAY={delay} SAMPLE={FormatOptionSample(_liveOptions[0])}");
                    return;
                }
                PublishGexStatus($"GEX STATUS: DXFEED FOUND BUT NO OPTIONS ROOT={root}");
                return;
            }

            PublishGexStatus("GEX STATUS: NO OPTION PROVIDER AND NO DXFEED");
        }
        catch (Exception ex)
        {
            PublishGexStatus($"GEX STATUS: INIT FAILED {ex.GetType().Name}");
        }
        finally
        {
            _gexInitRunning = false;
        }
    }

    private bool TryLoadFromOptionsBoard()
    {
        var board = TryGetService<IOptionsBoardViewModel>();
        if (board?.Strikes is null)
            return false;
        var options = new List<Security>();
        foreach (var strike in board.Strikes)
        {
            if (strike.Call?.Security is not null)
                options.Add(strike.Call.Security);
            if (strike.Put?.Security is not null)
                options.Add(strike.Put.Security);
        }
        if (options.Count == 0)
            return false;
        _optionsConnector = ResolveConnectors().FirstOrDefault();
        SubscribeOptions(options, _optionsConnector, option => _optionsSubscriptionService is not null && _optionsConnector is not null
            ? _optionsSubscriptionService.Subscribe(_optionsConnector, option, this)
            : null);
        return _liveOptions.Count > 0;
    }

    private IPlatformTradingCore? FindTradingCore()
    {
        return TryGetService<IPlatformTradingCore>() ?? FindTyped<IPlatformTradingCore>(DataProvider, 4);
    }

    private static T? FindTyped<T>(object? root, int depth) where T : class
    {
        if (root is null)
            return null;
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        return WalkFind<T>(root, depth, seen);
    }

    private static T? WalkFind<T>(object current, int depth, HashSet<object> seen) where T : class
    {
        if (current is T hit)
            return hit;
        if (depth < 0 || !seen.Add(current))
            return null;
        foreach (var field in current.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            object? value;
            try { value = field.GetValue(current); } catch { continue; }
            if (value is null || value is string || value.GetType().IsPrimitive)
                continue;
            if (value is T typed)
                return typed;
            if (value is IEnumerable enumerable and not IEnumerable<char>)
            {
                foreach (var item in enumerable)
                {
                    if (item is T itemHit)
                        return itemHit;
                }
                continue;
            }
            if (depth == 0)
                continue;
            var nested = WalkFind<T>(value, depth - 1, seen);
            if (nested is not null)
                return nested;
        }
        return null;
    }

    private List<IDataFeedConnector> ResolveConnectors()
    {
        var list = new List<IDataFeedConnector>();
        _tradingCore ??= FindTradingCore();
        var security = DataProvider?.TradingManager?.Security;
        var portfolio = DataProvider?.TradingManager?.Portfolio;
        if (_tradingCore is not null)
        {
            if (security is not null)
                TryAddConnector(list, _tradingCore.GetConnector(security));
            if (portfolio is not null)
                TryAddConnector(list, _tradingCore.GetConnector(portfolio));
            CollectConnectorsFrom(_tradingCore, list);
        }
        return list.Where(x => x.IsConnected).ToList();
    }

    private static void CollectConnectorsFrom(object source, List<IDataFeedConnector> list)
    {
        foreach (var field in source.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            object? value;
            try { value = field.GetValue(source); } catch { continue; }
            if (value is IConnectorsManager manager && manager.Connectors is not null)
            {
                foreach (var connector in manager.Connectors)
                    TryAddConnector(list, connector);
            }
            if (value is IDataFeedConnector connectorValue)
                TryAddConnector(list, connectorValue);
            if (value is IEnumerable enumerable and not string)
            {
                foreach (var item in enumerable)
                    TryAddConnectorFromItem(list, item);
            }
        }
    }

    private static void TryAddConnectorFromItem(List<IDataFeedConnector> list, object? item)
    {
        if (item is IDataFeedConnector connector)
        {
            TryAddConnector(list, connector);
            return;
        }
        if (item is DictionaryEntry entry)
        {
            TryAddConnectorFromItem(list, entry.Value);
            return;
        }
        if (item is null)
            return;
        var valueProperty = item.GetType().GetProperty("Value");
        if (valueProperty?.GetValue(item) is IDataFeedConnector nested)
            TryAddConnector(list, nested);
    }

    private static void TryAddConnector(List<IDataFeedConnector> list, IDataFeedConnector? connector)
    {
        if (connector is null)
            return;
        if (list.Any(x => ReferenceEquals(x, connector) || x.Id == connector.Id))
            return;
        list.Add(connector);
    }

    private Security? ResolveUnderlyingSecurity()
    {
        var security = DataProvider?.TradingManager?.Security;
        return security?.UnderlyingSecurity ?? security;
    }

    private static string ResolveOptionRoot(Security? security)
    {
        var candidates = new[]
        {
            security?.UnderlyingSecurity?.Code,
            security?.UnderlyingSecurity?.SecurityId,
            security?.Code,
            security?.SecurityId,
            security?.UnderlyingSecurity?.Instrument,
            security?.Instrument
        };
        foreach (var raw in candidates)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            var token = raw.Split('@')[0];
            token = new string(token.TakeWhile(char.IsLetter).ToArray());
            if (token.Length < 2)
                continue;
            if (token.Equals("MNQ", StringComparison.OrdinalIgnoreCase) || token.Equals("NQ", StringComparison.OrdinalIgnoreCase))
                return "NQ";
            if (token.Equals("MES", StringComparison.OrdinalIgnoreCase) || token.Equals("ES", StringComparison.OrdinalIgnoreCase))
                return "ES";
        }
        return "NQ";
    }

    private async Task<List<Security>> LoadDxFeedProfilesAsync(DxFeedConnector? connector, string root)
    {
        if (connector is null)
            return new List<Security>();
        var manager = connector.InstrumentProfilesManager;
        if (manager is null)
            return new List<Security>();
        IReadOnlyList<InstrumentProfile> profiles = Array.Empty<InstrumentProfile>();
        try
        {
            profiles = await manager.LoadAsync(connector.DataPath);
        }
        catch
        {
        }
        if (profiles.Count == 0)
        {
            try
            {
                profiles = await manager.LoadAsync(manager.Address);
            }
            catch
            {
            }
        }
        DumpIpfStats(profiles, root);
        var options = new List<Security>();
        foreach (var profile in profiles)
        {
            if (!IsNqOptionProfile(profile, root))
                continue;
            var option = new Security
            {
                SecurityId = profile.Symbol,
                Code = profile.Symbol,
                Instrument = string.IsNullOrWhiteSpace(profile.Underlying) ? root : profile.Underlying,
                Exchange = string.IsNullOrWhiteSpace(profile.OPOL) ? "CME" : profile.OPOL,
                Type = SecType.Option,
                StrikePrice = profile.Strike > 0 ? (decimal)profile.Strike : null,
                OptionType = ParseProfileOptionType(profile),
                Expiration = ParseProfileExpiration(profile.Expiration),
                ConnectorId = profile.Symbol
            };
            NormalizeOptionContract(option);
            if (option.Expiration.Year >= 2000 && option.Expiration.Date < DateTime.UtcNow.Date)
                continue;
            if (option.StrikePrice is > 0 && option.OptionType is not null)
                options.Add(option);
        }
        var selected = SelectTradableOptions(options, root);
        if (selected.Count > 0)
            return selected;
        selected = SelectTradableOptions(connector.Securities ?? Array.Empty<Security>(), root);
        if (selected.Count > 0)
            return selected;
        return BuildSyntheticOptions(connector, root, profiles);
    }

    private List<Security> BuildSyntheticOptions(DxFeedConnector connector, string root, IReadOnlyList<InstrumentProfile> profiles)
    {
        var template = PickNqFutureProfile(profiles, root);
        if (template is null && connector.Securities is not null)
        {
            foreach (var security in connector.Securities)
            {
                if (security.Parent is DxFeedSecurityParent parent && parent.First is InstrumentProfile profile && IsNqFutureProfile(profile, root))
                {
                    template = profile;
                    break;
                }
            }
        }
        if (template is null)
        {
            PublishGexStatus("GEX STATUS: IPF HAS NO OPTION AND NO NQ FUTURE PROFILE");
            return new List<Security>();
        }
        var spot = _lastPrice > 0 ? _lastPrice : 25000m;
        var options = new List<Security>();
        foreach (var expiry in NextQuarterlies(3))
        {
            var month = expiry.MonthCode;
            var yy = expiry.Expiry.ToString("yy", CultureInfo.InvariantCulture);
            var atm = Math.Round(spot / 25m) * 25m;
            for (var i = -20; i <= 20; i++)
            {
                var strike = atm + i * 25m;
                if (strike <= 0)
                    continue;
                options.Add(MakeDxFeedOption(root, month, yy, 'C', strike, expiry.Expiry, template));
                options.Add(MakeDxFeedOption(root, month, yy, 'P', strike, expiry.Expiry, template));
            }
        }
        DumpDxFeedSamples(options, root + "-SYN");
        return SelectTradableOptions(options, root);
    }

    private static Security MakeDxFeedOption(string root, char month, string yy, char cp, decimal strike, DateTime expiry, InstrumentProfile template)
    {
        var symbol = "/" + root + month + yy + cp + strike.ToString("0", CultureInfo.InvariantCulture) + ":XCME";
        var profile = new InstrumentProfile(template)
        {
            Symbol = symbol,
            Type = "OPTION",
            Underlying = root,
            Product = root,
            Strike = (double)strike,
            OptionType = cp == 'C' ? "CALL" : "PUT",
            CFI = cp == 'C' ? "OCXXXX" : "OPXXXX",
            Expiration = int.Parse(expiry.ToString("yyyyMMdd", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture)
        };
        return new Security
        {
            SecurityId = symbol,
            Code = symbol,
            ConnectorId = symbol,
            Instrument = root,
            Exchange = "CME",
            Type = SecType.Option,
            StrikePrice = strike,
            OptionType = cp == 'C' ? OptionTypes.Call : OptionTypes.Put,
            Expiration = expiry,
            Parent = new DxFeedSecurityParent(profile, template)
        };
    }

    private static InstrumentProfile? PickNqFutureProfile(IReadOnlyList<InstrumentProfile> profiles, string root)
    {
        var futures = profiles.Where(x => IsNqFutureProfile(x, root)).ToList();
        return futures.FirstOrDefault(x => (x.Symbol ?? string.Empty).IndexOf("Z26", StringComparison.OrdinalIgnoreCase) >= 0)
            ?? futures.FirstOrDefault(x => (x.Symbol ?? string.Empty).IndexOf(root + "Z", StringComparison.OrdinalIgnoreCase) >= 0)
            ?? futures.OrderByDescending(x => x.Expiration).FirstOrDefault();
    }

    private static bool IsNqFutureProfile(InstrumentProfile profile, string root)
    {
        var type = profile.Type ?? string.Empty;
        if (type.IndexOf("OPTION", StringComparison.OrdinalIgnoreCase) >= 0)
            return false;
        if (type.IndexOf("FUTURE", StringComparison.OrdinalIgnoreCase) < 0 && type.IndexOf("PRODUCT", StringComparison.OrdinalIgnoreCase) < 0)
            return false;
        var blob = $"{profile.Symbol} {profile.Underlying} {profile.Product}";
        if (blob.IndexOf("MNQ", StringComparison.OrdinalIgnoreCase) >= 0 && !root.Equals("MNQ", StringComparison.OrdinalIgnoreCase))
            return false;
        return blob.IndexOf(root, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static List<(char MonthCode, DateTime Expiry)> NextQuarterlies(int count)
    {
        var map = new (int Month, char Code)[] { (3, 'H'), (6, 'M'), (9, 'U'), (12, 'Z') };
        var list = new List<(char, DateTime)>();
        var year = DateTime.UtcNow.Year;
        var startMonth = DateTime.UtcNow.Month;
        for (var y = year; list.Count < count && y <= year + 2; y++)
        {
            foreach (var item in map)
            {
                if (y == year && item.Month < startMonth)
                    continue;
                var expiry = new DateTime(y, item.Month, 1).AddMonths(1).AddDays(-1);
                if (expiry.Date < DateTime.UtcNow.Date)
                    continue;
                list.Add((item.Code, expiry));
                if (list.Count >= count)
                    break;
            }
        }
        return list;
    }

    private static void DumpIpfStats(IReadOnlyList<InstrumentProfile> profiles, string root)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ATAS X", "StrategyState", "OPFStrategyV1");
            Directory.CreateDirectory(dir);
            var types = profiles.GroupBy(x => x.Type ?? "null").Select(g => g.Key + "=" + g.Count());
            var nq = profiles.Where(x =>
            {
                var blob = $"{x.Symbol} {x.Underlying} {x.Product}";
                return blob.IndexOf(root, StringComparison.OrdinalIgnoreCase) >= 0;
            }).Take(40).Select(x =>
                "type=" + x.Type + " sym=" + x.Symbol + " und=" + x.Underlying + " prod=" + x.Product + " strike=" + x.Strike + " opt=" + x.OptionType + " exp=" + x.Expiration + " exch=" + x.Exchanges);
            var lines = new List<string> { "count=" + profiles.Count, "types=" + string.Join(",", types) };
            lines.AddRange(nq);
            File.WriteAllLines(Path.Combine(dir, "gex_ipf_stats.txt"), lines);
        }
        catch
        {
        }
    }
    private List<Security> SelectTradableOptions(IEnumerable<Security> options, string? root = null)
    {
        root ??= "NQ";
        var usable = options.Where(x => IsUsableOption(x, root)).ToList();
        if (usable.Count == 0)
            return usable;
        var today = DateTime.UtcNow.Date;
        var dated = usable.Where(x => x.Expiration.Year >= 2000 && x.Expiration.Date >= today).ToList();
        var pool = dated.Count > 0 ? dated : usable;
        var expirations = pool
            .Select(x => x.Expiration.Date)
            .Where(x => x.Year >= 2000)
            .Distinct()
            .OrderBy(x => x)
            .Take(2)
            .ToHashSet();
        var spot = _lastPrice > 0 ? _lastPrice : pool.Select(x => x.StrikePrice.GetValueOrDefault()).FirstOrDefault(x => x > 0);
        if (expirations.Count > 0)
            pool = pool.Where(x => expirations.Contains(x.Expiration.Date)).ToList();
        return pool
            .OrderBy(x => Math.Abs((x.StrikePrice ?? 0m) - spot))
            .Take(MaxOptionSubscriptions)
            .ToList();
    }

    private void SubscribeOptions(IEnumerable<Security> options, IDataFeedConnector? connector, Func<Security, IOptionQuoteSubscription?> subscribe)
    {
        ClearOptionSubscriptions();
        HookSummary(connector);
        foreach (var option in SelectTradableOptions(options))
        {
            _liveOptions.Add(option);
            try
            {
                connector?.SubscribeToMarketData(option, SubscriptionType.Best | SubscriptionType.Summary);
            }
            catch
            {
            }
            try
            {
                var sub = subscribe(option);
                if (sub is null)
                    continue;
                sub.Changed += OnOptionQuoteChanged;
                _optionSubscriptions[OptionKey(option)] = sub;
            }
            catch
            {
            }
        }
    }

    private void HookSummary(IDataFeedConnector? connector)
    {
        if (connector is null || _summaryHooked)
            return;
        connector.SecuritySummaryChanged += OnSecuritySummaryChanged;
        _optionsConnector = connector;
        _summaryHooked = true;
    }

    private void OnSecuritySummaryChanged(IDataFeedConnector connector, SecuritySummary summary)
    {
        if (summary.Security is null)
            return;
        var option = FindLiveOption(summary.Security);
        if (option is null)
            return;
        if (summary.OpenInterest is > 0)
            option.OpenInterest = summary.OpenInterest;
        if (summary.MarkPrice is > 0)
            option.MarkPrice = summary.MarkPrice;
        if (summary.LastTradePrice is > 0)
            option.LastTradePrice = summary.LastTradePrice;
        if (summary.BestBidPrice is > 0)
            option.BestBidPrice = summary.BestBidPrice.Value;
        if (summary.BestAskPrice is > 0)
            option.BestAskPrice = summary.BestAskPrice.Value;
        RecalculateLiveGex();
    }

    private Security? FindLiveOption(Security security)
    {
        var key = OptionKey(security);
        return _liveOptions.FirstOrDefault(x => OptionKey(x).Equals(key, StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(security.Code) && string.Equals(x.Code, security.Code, StringComparison.OrdinalIgnoreCase)));
    }

    private static string OptionKey(Security option)
        => string.IsNullOrWhiteSpace(option.SecurityId) ? (option.Code ?? string.Empty) : option.SecurityId;

    private void CompleteGexInit(string status)
    {
        _liveGexInitialized = _liveOptions.Count > 0;
        PublishGexStatus(status);
        RecalculateLiveGex();
    }

    private static OptionSeries SelectNearestSeries(IEnumerable<OptionSeries> series)
    {
        return series.Where(x => x.Expiration > DateTime.UtcNow).OrderBy(x => x.Expiration).FirstOrDefault();
    }

    private static bool IsUsableOption(Security option, string? root = null)
    {
        NormalizeOptionContract(option);
        if (option.StrikePrice is not > 0 || option.OptionType is null)
            return false;
        option.Type = SecType.Option;
        if (string.IsNullOrWhiteSpace(root))
            return true;
        var blob = $"{option.Code} {option.Instrument} {option.SecurityId} {option.UnderlyingSecurity?.Code}";
        return blob.IndexOf(root, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void NormalizeOptionContract(Security option)
    {
        var symbol = $"{option.Code} {option.SecurityId} {option.Instrument}";
        var ymd = Regex.Match(symbol, @"[./]?(?<root>[A-Z]{1,6})\s*(?<ymd>\d{6})(?<cp>[CP])(?<strike>\d{3,8})", RegexOptions.IgnoreCase);
        var month = Regex.Match(symbol, @"[./]?(?<root>[A-Z]{1,6})(?<month>[FGHJKMNQUVXZ])(?<yy>\d{1,2})(?<cp>[CP])(?<strike>\d{3,8})", RegexOptions.IgnoreCase);
        var match = ymd.Success ? ymd : month;
        if (!match.Success)
            return;
        option.Type = SecType.Option;
        if (option.OptionType is null)
            option.OptionType = char.ToUpperInvariant(match.Groups["cp"].Value[0]) == 'C' ? OptionTypes.Call : OptionTypes.Put;
        if (option.StrikePrice is null or <= 0 && decimal.TryParse(match.Groups["strike"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var strike))
            option.StrikePrice = NormalizeStrike(strike);
        if (option.Expiration.Year < 2000)
        {
            if (ymd.Success && DateTime.TryParseExact(ymd.Groups["ymd"].Value, "yyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var expiry))
                option.Expiration = expiry;
            else if (month.Success)
                option.Expiration = ExpirationFromMonthCode(month.Groups["month"].Value[0], month.Groups["yy"].Value);
        }
    }

    private static decimal NormalizeStrike(decimal strike)
    {
        while (strike > 400000m)
            strike /= 10m;
        return strike;
    }

    private static DateTime ExpirationFromMonthCode(char month, string yearToken)
    {
        var months = "FGHJKMNQUVXZ";
        var monthNumber = months.IndexOf(char.ToUpperInvariant(month)) + 1;
        if (monthNumber <= 0)
            return DateTime.MinValue;
        if (!int.TryParse(yearToken, out var year))
            return DateTime.MinValue;
        year = yearToken.Length == 1 ? 2020 + year : (year < 100 ? 2000 + year : year);
        if (year < DateTime.UtcNow.Year - 1)
            year += 10;
        return new DateTime(year, monthNumber, 1);
    }

    private static void DumpDxFeedSamples(IReadOnlyList<Security> options, string root)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ATAS X", "StrategyState", "OPFStrategyV1");
            Directory.CreateDirectory(dir);
            var lines = options.Take(20).Select(x =>
                $"root={root} type={x.Type} code={x.Code} id={x.SecurityId} inst={x.Instrument} strike={x.StrikePrice} opt={x.OptionType} exp={x.Expiration:yyyy-MM-dd}");
            File.WriteAllLines(Path.Combine(dir, "gex_dxfeed_samples.txt"), lines);
        }
        catch
        {
        }
    }

    private static string FormatOptionSample(Security? option)
    {
        if (option is null)
            return "NONE";
        var raw = option.Code ?? option.SecurityId ?? option.Instrument ?? "EMPTY";
        return raw.Length <= 48 ? raw : raw[..48];
    }
    private static bool IsNqOptionProfile(InstrumentProfile profile, string root)
    {
        var type = profile.Type ?? string.Empty;
        if (type.IndexOf("OPTION", StringComparison.OrdinalIgnoreCase) < 0 && type.IndexOf("FUTURES_OPTION", StringComparison.OrdinalIgnoreCase) < 0)
            return false;
        var underlying = profile.Underlying ?? profile.Product ?? string.Empty;
        var symbol = profile.Symbol ?? string.Empty;
        return underlying.IndexOf(root, StringComparison.OrdinalIgnoreCase) >= 0
            || symbol.IndexOf(root, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static OptionTypes? ParseProfileOptionType(InstrumentProfile profile)
    {
        var cfi = profile.CFI ?? string.Empty;
        if (cfi.StartsWith("OC", StringComparison.OrdinalIgnoreCase))
            return OptionTypes.Call;
        if (cfi.StartsWith("OP", StringComparison.OrdinalIgnoreCase))
            return OptionTypes.Put;
        var optionType = profile.OptionType ?? string.Empty;
        if (optionType.StartsWith("C", StringComparison.OrdinalIgnoreCase))
            return OptionTypes.Call;
        if (optionType.StartsWith("P", StringComparison.OrdinalIgnoreCase))
            return OptionTypes.Put;
        return null;
    }

    private static DateTime ParseProfileExpiration(int expiration)
    {
        if (DateTime.TryParseExact(expiration.ToString(CultureInfo.InvariantCulture), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            return parsed;
        return DateTime.MinValue;
    }

    private void OnOptionQuoteChanged(IOptionQuoteSubscription _) => RecalculateLiveGex();

    private void RecalculateLiveGex()
    {
        var spot = (double)_lastPrice;
        if (spot <= 0)
            return;
        var rows = new List<(double Strike, OptionTypes Type, double Gex)>();
        foreach (var option in _liveOptions)
        {
            _optionSubscriptions.TryGetValue(OptionKey(option), out var liveSub);
            var quote = liveSub?.Summary;
            var strike = (double?)option.StrikePrice;
            var oi = (double?)(quote?.OpenInterest ?? option.OpenInterest);
            if (strike is null || oi is null || oi <= 0 || option.OptionType is null)
                continue;
            var bid = quote?.BestBidPrice.GetValueOrDefault() ?? option.BestBidPrice;
            var ask = quote?.BestAskPrice.GetValueOrDefault() ?? option.BestAskPrice;
            var px = quote?.MarkPrice ?? quote?.LastTradePrice ?? option.MarkPrice ?? option.LastTradePrice ?? (bid > 0 && ask > 0 ? (bid + ask) / 2m : 0m);
            if (px <= 0)
                continue;
            var t = Math.Max((option.Expiration - DateTime.UtcNow).TotalDays / 365d, 1d / 365d / 24d);
            var iv = BlackScholesPricer.ImpliedVolatility(option.OptionType.Value, (double)px, spot, strike.Value, t, 0d, 0d);
            if (iv is null || iv <= 0)
                continue;
            var gamma = BlackScholesPricer.CalculateGreeks(option.OptionType.Value, spot, strike.Value, t, iv.Value, 0d, 0d).Gamma;
            var sign = option.OptionType == OptionTypes.Call ? 1d : -1d;
            rows.Add((strike.Value, option.OptionType.Value, sign * gamma * oi.Value * 100d * spot * spot * 0.01d));
        }
        if (rows.Count == 0)
        {
            if (_liveOptions.Count > 0)
                PublishGexStatus($"GEX STATUS: SUBSCRIBED {_liveOptions.Count} QUOTES={_optionSubscriptions.Count} OI=0 SAMPLE={FormatOptionSample(_liveOptions[0])}");
            return;
        }
        var groups = rows.GroupBy(x => x.Strike).Select(g => (Strike: g.Key, Total: g.Sum(x => x.Gex), Abs: g.Sum(x => Math.Abs(x.Gex)))).ToList();
        var callWall = rows.Where(x => x.Type == OptionTypes.Call).OrderByDescending(x => Math.Abs(x.Gex)).FirstOrDefault().Strike;
        var putWall = rows.Where(x => x.Type == OptionTypes.Put).OrderByDescending(x => Math.Abs(x.Gex)).FirstOrDefault().Strike;
        var zeroGamma = groups.OrderBy(x => Math.Abs(groups.Where(y => y.Strike <= x.Strike).Sum(y => y.Total))).First().Strike;
        var volTrigger = groups.OrderByDescending(x => x.Abs).First().Strike;
        lock (_gexSync)
        {
            if (callWall > 0) _gexLevels["GEX-CW"] = (decimal)callWall;
            if (putWall > 0) _gexLevels["GEX-PW"] = (decimal)putWall;
            if (zeroGamma > 0) _gexLevels["GEX-ZG"] = (decimal)zeroGamma;
            if (volTrigger > 0) _gexLevels["GEX-VT"] = (decimal)volTrigger;
        }
    }


    private void LoadLocalGexSnapshot()
    {
        _lastGexSnapshotLoadUtc = DateTime.UtcNow;
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var candidates = new[]
            {
                Path.Combine(appData, "ATAS", "StrategyConfigs", "OPFStrategyV1_gex_snapshot.json"),
                Path.Combine(appData, "ATAS X", "StrategyConfigs", "OPFStrategyV1_gex_snapshot.json")
            };
            var path = candidates
                .Where(File.Exists)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (string.IsNullOrWhiteSpace(path))
            {
                PublishGexStatus("GEX STATUS: SNAPSHOT MISSING");
                return;
            }

            var writeUtc = File.GetLastWriteTimeUtc(path);
            var spot = _lastPrice > 0m ? _lastPrice : _zoneReferencePrice;
            if (path == _gexSnapshotPath && writeUtc == _gexSnapshotWriteUtc && _gexLevels.Count > 0)
                return;

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            if (!root.TryGetProperty("symbols", out var symbols) ||
                !symbols.TryGetProperty("NQ", out var nq) ||
                !nq.TryGetProperty("levels", out var levels) ||
                levels.ValueKind != JsonValueKind.Array)
            {
                PublishGexStatus("GEX STATUS: SNAPSHOT NQ LEVELS MISSING");
                return;
            }

            var tagged = new List<(string Tag, decimal Price, string Label, bool Preferred)>();
            foreach (var item in levels.EnumerateArray())
            {
                if (!TryDecimal(item, "price", out var price) || price < 10000m || price > 50000m)
                    continue;
                var levelType = Text(item, "levelType");
                var label = Text(item, "label");
                var tag = GexSnapshotTag(levelType, label);
                if (string.IsNullOrEmpty(tag))
                    continue;
                tagged.Add((tag, price, label, label.IndexOf("QQQ", StringComparison.OrdinalIgnoreCase) < 0));
            }

            var selected = tagged
                .GroupBy(x => x.Tag, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderBy(x => x.Preferred ? 0 : 1).ThenBy(x => Math.Abs(x.Price - spot)).First().Price,
                    StringComparer.OrdinalIgnoreCase);

            if (selected.Count == 0)
            {
                PublishGexStatus("GEX STATUS: SNAPSHOT HAS NO CW/PW/ZG/VT");
                return;
            }

            lock (_gexSync)
            {
                _gexLevels.Clear();
                foreach (var pair in selected)
                    _gexLevels[pair.Key] = pair.Value;
            }

            _gexSnapshotPath = path;
            _gexSnapshotWriteUtc = writeUtc;
            _liveGexInitialized = true;
            var dataDate = Text(root, "dataDate");
            var updated = Text(root, "updatedAt");
            selected.TryGetValue("GEX-CW", out var cw);
            selected.TryGetValue("GEX-PW", out var pw);
            selected.TryGetValue("GEX-ZG", out var zg);
            selected.TryGetValue("GEX-VT", out var vt);
            var src = path.Contains("ATAS X", StringComparison.OrdinalIgnoreCase) ? "ATASX" : "ATAS";
            PublishGexStatus($"GEX STATUS: LOCAL SNAPSHOT src={src} date={dataDate} updated={updated} CW={cw:0.##} PW={pw:0.##} ZG={zg:0.##} VT={vt:0.##}");
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            PublishGexStatus($"GEX STATUS: SNAPSHOT LOAD FAILED {ex.GetType().Name}");
        }
    }

    private static string GexSnapshotTag(string levelType, string label)
    {
        if (levelType.Equals("call_wall", StringComparison.OrdinalIgnoreCase))
            return "GEX-CW";
        if (levelType.Equals("put_wall", StringComparison.OrdinalIgnoreCase))
            return "GEX-PW";
        if (label.Contains("Zero Gamma", StringComparison.OrdinalIgnoreCase))
            return "GEX-ZG";
        if (label.Contains("VolTrig", StringComparison.OrdinalIgnoreCase))
            return "GEX-VT";
        return string.Empty;
    }
    private void LoadHistoricalZones()
    {
        _zones.Clear();
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var directories = new[]
        {
            Path.Combine(appData, "ATAS X", "StrategyState", "OPFStrategyV1", "SignificantZones"),
            Path.Combine(appData, "ATAS", "StrategyState", "OPFStrategyV1", "SignificantZones"),
            Path.Combine(appData, "ATAS X", "StrategyConfigs"),
            Path.Combine(appData, "ATAS", "StrategyConfigs")
        };
        var files = directories
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.GetFiles(directory, "significant_zone_history*.jsonl", SearchOption.AllDirectories))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var latest = new Dictionary<string, (decimal Low, decimal High, DateTime Created, DateTime Updated, string Grade)>();
        foreach (var file in files)
        {
            try
            {
                foreach (var line in File.ReadLines(file))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    if (!IsGradeA(root)) continue;
                    var id = Text(root, "ZoneId");
                    if (!TryDecimal(root, "InnerBoundary", out var inner) || !TryDecimal(root, "OuterBoundary", out var outer) || !DateTime.TryParse(Text(root, "CreatedTime"), out var created)) continue;
                    DateTime.TryParse(Text(root, "UpdatedAt"), out var updated);
                    latest[id] = (Math.Min(inner, outer), Math.Max(inner, outer), created, updated, "A");
                }
            }
            catch (IOException) { }
            catch (JsonException) { }
        }
        _lastZoneLoadUtc = DateTime.UtcNow;
        var cutoff = DateTime.UtcNow - ZoneLookback;
        _zones.AddRange(latest.Values.Where(x => x.Created >= cutoff).Select(x => (x.Low, x.High, x.Created)));
    }

    private List<(decimal Low, decimal High, DateTime Created)> SelectZones()
    {
        return _zones
            .OrderBy(x => x.Low <= _zoneReferencePrice && x.High >= _zoneReferencePrice ? 0 : Math.Abs((x.Low + x.High) / 2m - _zoneReferencePrice))
            .Take(MaxZonePairs)
            .ToList();
    }

    private void SetLevel(ValueDataSeries series, int bar, string tag)
    {
        lock (_gexSync)
        {
            if (_gexLevels.TryGetValue(tag, out var price) && price > 0m)
                series[bar] = price;
        }
    }

    protected override void OnDispose()
    {
        if (_mboProvider is not null)
        {
            _mboProvider.MboIcebergsChanged -= OnMboIcebergsChanged;
            _mboProvider.UnsubscribeMboAnalyticsData();
            _mboProvider = null;
        }
        if (_summaryHooked && _optionsConnector is not null)
        {
            _optionsConnector.SecuritySummaryChanged -= OnSecuritySummaryChanged;
            _summaryHooked = false;
        }
        ClearOptionSubscriptions();
        _optionsSubscriptionService?.ReleaseOwned(this);
        base.OnDispose();
    }

    private void ClearOptionSubscriptions()
    {
        foreach (var sub in _optionSubscriptions.Values)
            sub.Changed -= OnOptionQuoteChanged;
        _optionSubscriptions.Clear();
        _liveOptions.Clear();
    }

    private T? TryGetService<T>()
    {
        try
        {
            return DataProvider is null ? default : DataProvider.GetService<T>();
        }
        catch
        {
            return default;
        }
    }

    private static ValueDataSeries CreateSeries(string name, System.Drawing.Color color) => new(name) { Color = color, Width = 2 };

    private static void ApplyZoneAppearance(RangeDataSeries series, decimal low, decimal high, decimal price)
    {
        System.Drawing.Color fill;
        System.Drawing.Color border;
        if (high < price)
        {
            fill = System.Drawing.Color.FromArgb(55, 46, 160, 80);
            border = System.Drawing.Color.FromArgb(200, 40, 150, 70);
        }
        else if (low > price)
        {
            fill = System.Drawing.Color.FromArgb(55, 190, 70, 70);
            border = System.Drawing.Color.FromArgb(200, 180, 55, 55);
        }
        else
        {
            fill = System.Drawing.Color.FromArgb(50, 210, 170, 40);
            border = System.Drawing.Color.FromArgb(200, 200, 150, 30);
        }

        series.RangeColor = fill;
        series.RenderColor = border;
    }

    private static RangeDataSeries[] CreateZoneSeries() => Enumerable.Range(1, MaxZonePairs)
        .Select(i => new RangeDataSeries($"AZone_{i}")
        {
            RangeColor = System.Drawing.Color.FromArgb(50, 70, 130, 180),
            RenderColor = System.Drawing.Color.FromArgb(180, 70, 130, 180),
            ScaleIt = false,
            DrawAbovePrice = false
        })
        .ToArray();

    private static string Text(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

    private static bool IsGradeA(JsonElement element)
    {
        if (!element.TryGetProperty("Grade", out var value))
            return false;
        return value.ValueKind == JsonValueKind.Number
            ? value.TryGetInt32(out var grade) && grade >= 2
            : string.Equals(value.GetString(), "A", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryDecimal(JsonElement element, string name, out decimal value)
    {
        value = 0m;
        return element.TryGetProperty(name, out var item) && (item.ValueKind == JsonValueKind.Number ? item.TryGetDecimal(out value) : decimal.TryParse(item.GetString(), out value));
    }
}






