using ATAS.Indicators;
using OFT.Rendering.Context;
using OFT.Rendering.Tools;
using OPFStrategyV1.Core.MarketData;
using OPFStrategyV1.Core.Signals;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace OPFStrategyV1.Strategy;

public sealed partial class OpeningPullbackFailureStrategy
{
    private const string GexSnapshotFileName = "OPFStrategyV1_gex_snapshot.json";
    private const string GexSyncStateFileName = "OPFStrategyV1_gex_sync_state.json";
    private const string GexRefreshRequestFileName = "OPFStrategyV1_gex_refresh_request.json";

    /// <summary>
    /// Gate0 shadow-only: "near wall" distance in NQ/MNQ futures points.
    /// Used only for SHADOW_NO_SPACE tags; never gates orders. Calibrate in Gate1.
    /// </summary>
    private const decimal GexNearWallPoints = 40m;

    private GexDailySnapshot _gexSnapshot = GexDailySnapshot.Unavailable("notLoaded");
    private decimal? _gexPreviousCheckClose;
    private DateTime? _gexLastForcedRefreshRequestUtc;

    private static string GexConfigDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ATAS", "StrategyConfigs");

    /// <summary>
    /// Re-reads the local GEX snapshot file from disk. Safe to call repeatedly (e.g. once per closed
    /// M5 bar): it never touches the network itself. This is what lets the HUD/chart pick up a snapshot
    /// that the out-of-process scheduler refreshed in the background, without requiring an ATAS
    /// restart. The freshness overlay reads the scheduler's own sync-state file so a stale local file
    /// (scheduler down, network failure, etc.) is surfaced as Stale even if the vendor payload itself
    /// still looks internally consistent.
    /// </summary>
    private void LoadGexSnapshot()
    {
        var directory = GexConfigDirectory();
        var path = Path.Combine(directory, GexSnapshotFileName);
        var syncStatePath = Path.Combine(directory, GexSyncStateFileName);
        var tickSize = _snapshot?.InstrumentProfile.TickSize ?? 0.25m;
        _gexSnapshot = GexDailySnapshotLoader.Load(path, tickSize, DateTimeOffset.UtcNow, syncStatePath);
        _researchLogger?.AppendGexSnapshot(_snapshot?.SnapshotId ?? string.Empty, DateTime.UtcNow, _gexSnapshot);
    }

    /// <summary>
    /// Called once per closed bar. Re-reads the local snapshot (periodic in-strategy reload) and, when
    /// the completed bar crossed a tracked GEX reference level or spiked in range relative to ATR14,
    /// writes a forced-refresh request file for the out-of-process scheduler to consume on its next
    /// poll. This is purely advisory: it never blocks any path, gate, or order, and the scheduler
    /// remains the only component that ever calls the vendor.
    /// </summary>
    private void UpdateGexIntradayRefresh(OpfCandle current)
    {
        LoadGexSnapshot();

        var previousClose = _gexPreviousCheckClose;
        _gexPreviousCheckClose = current.Close;
        if (previousClose is null)
            return;

        var reason = GexReferenceLevels(current.Close)
            .Select(level => GexRefreshTriggerGate.DetectKeyLevelCross(previousClose.Value, current.Close, level.Price, GexTag(level)))
            .FirstOrDefault(x => x is not null)
            ?? GexRefreshTriggerGate.DetectVolatilitySpike(current.High, current.Low, CalculateAtr14(current));

        if (reason is null)
            return;

        var nowUtc = DateTime.UtcNow;
        if (GexRefreshTriggerGate.IsInCooldown(_gexLastForcedRefreshRequestUtc, nowUtc))
            return;

        _gexLastForcedRefreshRequestUtc = nowUtc;
        RequestForcedGexRefresh(reason, nowUtc);
    }

    private void RequestForcedGexRefresh(string reason, DateTime requestedAtUtc)
    {
        try
        {
            var directory = GexConfigDirectory();
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, GexRefreshRequestFileName);
            var payload = JsonSerializer.Serialize(new { requestedAtUtc = requestedAtUtc.ToString("O"), reason });
            File.WriteAllText(path, payload);
            _researchLogger?.AppendInfo(_snapshot?.SnapshotId ?? string.Empty, _lastResearchCandle?.Bar ?? 0, requestedAtUtc, $"GEX_FORCED_REFRESH_REQUESTED reason={reason}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _researchLogger?.AppendInfo(_snapshot?.SnapshotId ?? string.Empty, _lastResearchCandle?.Bar ?? 0, requestedAtUtc, $"GEX_FORCED_REFRESH_REQUEST_FAILED reason={reason} error={ex.GetType().Name}");
        }
    }

    private void DrawGexLevels(RenderContext context)
    {
        if (ChartInfo is null || _gexSnapshot.Levels.Count == 0)
            return;

        var left = ChartArea.X;
        var right = ChartArea.X + ChartArea.Width;
        foreach (var level in _gexSnapshot.Levels)
        {
            var y = (int)ChartInfo.GetYByPrice(level.Price, false);
            var color = GexColor(level.LevelType, level.Label);
            DrawGexLine(context, left, right, y, level, color);
            context.DrawString($"{GexTag(level)} {level.Price:0.##} {level.Label}", new RenderFont("Consolas", 9), color, left + 4, y - 12);
        }
    }

    private void AppendGexHud(StringBuilder hud)
    {
        var date = _gexSnapshot.DataDate?.ToString("yyyy-MM-dd") ?? "-";
        var updated = _gexSnapshot.UpdatedAt?.ToString("yyyy-MM-dd HH:mm zzz") ?? "-";
        var available = _gexSnapshot.DataAvailableTime?.ToString("yyyy-MM-dd HH:mm zzz") ?? "-";
        var price = _lastResearchCandle?.Close ?? 0m;
        var distances = GexReferenceLevels(price)
            .Select(x => $"{GexTag(x)}={x.Price:0.##} d={price - x.Price:+0.##;-0.##;0.##}");
        hud.AppendLine($"GEX: {_gexSnapshot.Status} data={date} updated={updated} available={available} levels={_gexSnapshot.Levels.Count} {_gexSnapshot.Detail}");
        hud.AppendLine($"GEX Dist: {string.Join(" ", distances)}");
    }

    /// <summary>
    /// Gate0 SignificantZone × GEX shadow audit. Writes CSV only; never changes eligibility,
    /// ManualAlert fire, stops/targets, path priority, or order submission.
    /// </summary>
    private void AuditGexCandidate(CandidateSignal signal, OpfCandle candle, string path, string auditPhase = "Candidate")
    {
        if (_snapshot is null || _researchLogger is null)
            return;

        var price = candle.Close;
        var cw = TryGexLevelByTag(price, "GEX-CW");
        var pw = TryGexLevelByTag(price, "GEX-PW");
        var zg = TryGexLevelByTag(price, "GEX-ZG");
        var vt = TryGexLevelByTag(price, "GEX-VT");

        var gexRegime = zg is null
            ? string.Empty
            : price >= zg.Price ? "PosGamma" : "NegGamma";

        var prefilterActive = IsGexPrefilterActiveForShadow(cw, pw, zg);
        var roleFlipState = "Unknown";
        var tags = new List<string>();
        var sizeHint = string.Empty;

        // EM fields are not in the current snapshot schema — leave empty; never invent.
        var emUsedPct = string.Empty;
        var emRemainingUpper = string.Empty;
        var emRemainingLower = string.Empty;

        if (!prefilterActive)
        {
            // F0 fail: record inactivity only; do not invent soft space/regime tags from missing/stale data.
            tags.Add("SHADOW_PREFILTER_INACTIVE");
        }
        else if (IsSignificantZoneFirstTouchPath(path))
        {
            AppendSignificantZoneGexShadowTags(
                signal.Side,
                price,
                gexRegime,
                cw,
                pw,
                emUsedPct,
                emRemainingUpper,
                emRemainingLower,
                roleFlipState,
                tags,
                ref sizeHint);
        }

        _researchLogger.AppendGexCandidateAudit(
            _snapshot.SnapshotId,
            candle.Time,
            candle.Bar,
            signal.SignalId,
            path,
            signal.Side,
            price,
            _gexSnapshot,
            auditPhase,
            gexRegime,
            prefilterActive ? "true" : "false",
            string.Join("|", tags),
            sizeHint,
            roleFlipState,
            DistString(price, cw),
            DistString(price, pw),
            DistString(price, zg),
            DistString(price, vt),
            PriceString(cw),
            PriceString(pw),
            PriceString(zg),
            PriceString(vt),
            emUsedPct,
            emRemainingUpper,
            emRemainingLower,
            GexNearWallPoints.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Minimal F0 for Gate0 soft tags: Status=Ready and CW/PW/ZG present.
    /// Missing enrich fields (proxy/coverage/convention/window) stay unknown and keep soft tags off
    /// only when the Ready+levels check fails — we never invent those enrich values.
    /// </summary>
    private bool IsGexPrefilterActiveForShadow(GexLevel? cw, GexLevel? pw, GexLevel? zg) =>
        string.Equals(_gexSnapshot.Status, "Ready", StringComparison.OrdinalIgnoreCase) &&
        cw is not null &&
        pw is not null &&
        zg is not null;

    private static void AppendSignificantZoneGexShadowTags(
        TradeSide side,
        decimal price,
        string gexRegime,
        GexLevel? cw,
        GexLevel? pw,
        string emUsedPct,
        string emRemainingUpper,
        string emRemainingLower,
        string roleFlipState,
        List<string> tags,
        ref string sizeHint)
    {
        if (side == TradeSide.Long)
        {
            if (IsNearUpperSpaceBound(price, cw?.Price, emRemainingUpper))
                tags.Add("SHADOW_NO_SPACE");

            if (string.Equals(gexRegime, "NegGamma", StringComparison.Ordinal))
            {
                tags.Add("SHADOW_NEG_GAMMA_LONG_HALF");
                sizeHint = "0.5";
            }
        }
        else
        {
            if (IsNearLowerSpaceBound(price, pw?.Price, emRemainingLower))
                tags.Add("SHADOW_NO_SPACE");

            if (TryParseEmUsedPct(emUsedPct, out var used) && used > 70m)
                tags.Add("SHADOW_NO_SPACE");

            if (string.Equals(gexRegime, "PosGamma", StringComparison.Ordinal))
                tags.Add("SHADOW_POS_GAMMA_SHORT_STRICT");
        }

        // Role-flip conflict only when wall state is known from feed. Current snapshot has no wall-state
        // history → RoleFlipState stays Unknown and we never invent SHADOW_ROLE_FLIP_CONFLICT.
        if (string.Equals(roleFlipState, "Conflict", StringComparison.OrdinalIgnoreCase))
            tags.Add("SHADOW_ROLE_FLIP_CONFLICT");
    }

    private static bool IsNearUpperSpaceBound(decimal price, decimal? callWall, string emRemainingUpper)
    {
        if (callWall is decimal cw)
        {
            // Headroom to Call Wall (above). Already at/above CW also means no upside space.
            if (price >= cw || cw - price <= GexNearWallPoints)
                return true;
        }

        if (decimal.TryParse(emRemainingUpper, NumberStyles.Number, CultureInfo.InvariantCulture, out var emUpper))
        {
            if (price >= emUpper || emUpper - price <= GexNearWallPoints)
                return true;
        }

        return false;
    }

    private static bool IsNearLowerSpaceBound(decimal price, decimal? putWall, string emRemainingLower)
    {
        if (putWall is decimal pw)
        {
            if (price <= pw || price - pw <= GexNearWallPoints)
                return true;
        }

        if (decimal.TryParse(emRemainingLower, NumberStyles.Number, CultureInfo.InvariantCulture, out var emLower))
        {
            if (price <= emLower || price - emLower <= GexNearWallPoints)
                return true;
        }

        return false;
    }

    private static bool TryParseEmUsedPct(string value, out decimal used) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out used);

    private GexLevel? TryGexLevelByTag(decimal price, string tag) =>
        GexReferenceLevels(price).FirstOrDefault(x => GexTag(x) == tag);

    private static string DistString(decimal price, GexLevel? level) =>
        level is null ? string.Empty : (price - level.Price).ToString(CultureInfo.InvariantCulture);

    private static string PriceString(GexLevel? level) =>
        level is null ? string.Empty : level.Price.ToString(CultureInfo.InvariantCulture);

    private static string GexTag(GexLevel level) =>
        level.LevelType.Equals("call_wall", StringComparison.OrdinalIgnoreCase) ? "GEX-CW" :
        level.LevelType.Equals("put_wall", StringComparison.OrdinalIgnoreCase) ? "GEX-PW" :
        level.Label.Contains("Zero Gamma", StringComparison.OrdinalIgnoreCase) ? "GEX-ZG" :
        level.Label.Contains("VolTrig", StringComparison.OrdinalIgnoreCase) ? "GEX-VT" : "GEX-Cluster";

    private IReadOnlyList<GexLevel> GexReferenceLevels(decimal price) =>
        _gexSnapshot.Levels
            .Where(x => GexTag(x) is "GEX-CW" or "GEX-PW" or "GEX-ZG" or "GEX-VT")
            .GroupBy(GexTag)
            .Select(x => x.OrderBy(y => Math.Abs(y.Price - price)).First())
            .OrderBy(x => GexTag(x))
            .ToArray();

    private static void DrawGexLine(RenderContext context, int left, int right, int y, GexLevel level, Color color)
    {
        var tag = GexTag(level);
        if (tag is "GEX-CW" or "GEX-PW")
        {
            context.DrawLine(new RenderPen(Color.FromArgb(210, color), 2), left, y, right, y);
            return;
        }

        var alternate = tag == "GEX-VT" ? Color.MediumPurple : color;
        var dash = tag == "GEX-Cluster" ? 4 : 8;
        var gap = tag == "GEX-Cluster" ? 5 : 6;
        var segment = 0;
        for (var x = left; x < right; x += dash + gap, segment++)
        {
            var segmentColor = tag == "GEX-VT" && segment % 2 == 1 ? alternate : color;
            context.DrawLine(new RenderPen(Color.FromArgb(tag == "GEX-Cluster" ? 110 : 190, segmentColor), 1), x, y, Math.Min(x + dash, right), y);
        }
    }

    private static Color GexColor(string levelType, string label)
    {
        if (string.Equals(levelType, "call_wall", StringComparison.OrdinalIgnoreCase)) return Color.IndianRed;
        if (string.Equals(levelType, "put_wall", StringComparison.OrdinalIgnoreCase)) return Color.LimeGreen;
        if (label.Contains("VolTrig", StringComparison.OrdinalIgnoreCase)) return Color.DarkOrange;
        if (string.Equals(levelType, "zero_gex", StringComparison.OrdinalIgnoreCase)) return Color.MediumPurple;
        return Color.SlateGray;
    }

    private static float GexLineWidth(string levelType) =>
        levelType is "call_wall" or "put_wall" ? 2f : 1f;
}
