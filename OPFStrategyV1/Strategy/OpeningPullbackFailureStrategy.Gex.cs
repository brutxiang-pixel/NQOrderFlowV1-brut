using ATAS.Indicators;
using OFT.Rendering.Context;
using OFT.Rendering.Tools;
using OPFStrategyV1.Core.MarketData;
using OPFStrategyV1.Core.Signals;
using System.Drawing;
using System.Text;
using System.Text.Json;

namespace OPFStrategyV1.Strategy;

public sealed partial class OpeningPullbackFailureStrategy
{
    private const string GexSnapshotFileName = "OPFStrategyV1_gex_snapshot.json";
    private const string GexSyncStateFileName = "OPFStrategyV1_gex_sync_state.json";
    private const string GexRefreshRequestFileName = "OPFStrategyV1_gex_refresh_request.json";
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

    private void AuditGexCandidate(CandidateSignal signal, OpfCandle candle, string path)
    {
        if (_snapshot is null || _researchLogger is null)
            return;
        _researchLogger.AppendGexCandidateAudit(_snapshot.SnapshotId, candle.Time, candle.Bar, signal.SignalId, path, signal.Side, candle.Close, _gexSnapshot);
    }

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
