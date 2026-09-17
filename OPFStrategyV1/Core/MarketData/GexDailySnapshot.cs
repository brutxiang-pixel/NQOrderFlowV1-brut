using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OPFStrategyV1.Core.MarketData;

public sealed record GexLevel(string LevelType, decimal Price, string Label, decimal OpenInterest, string Side);

public sealed record GexDailySnapshot(
    string Status,
    string Detail,
    DateOnly? DataDate,
    DateTimeOffset? UpdatedAt,
    DateTimeOffset? DataAvailableTime,
    string Sha256,
    string RawPayload,
    IReadOnlyList<GexLevel> Levels)
{
    public static GexDailySnapshot Unavailable(string detail) =>
        new("Unavailable", detail, null, null, null, string.Empty, string.Empty, Array.Empty<GexLevel>());
}

public static class GexDailySnapshotLoader
{
    /// <summary>
    /// If the scheduler's own sync-state file records a last successful download older than this
    /// threshold, the snapshot is displayed as Stale even when the vendor's <c>dataDate</c> field is
    /// still within the existing 3-day tolerance. This only changes the HUD/chart status text; it
    /// never affects any trading path, gate, or order.
    /// </summary>
    public static readonly TimeSpan SyncFreshnessThreshold = TimeSpan.FromMinutes(90);

    public static GexDailySnapshot Load(string path, decimal tickSize, DateTimeOffset nowUtc) =>
        Load(path, tickSize, nowUtc, syncStatePath: null);

    public static GexDailySnapshot Load(string path, decimal tickSize, DateTimeOffset nowUtc, string? syncStatePath)
    {
        if (!File.Exists(path))
            return GexDailySnapshot.Unavailable("snapshotFileMissing");

        try
        {
            var raw = File.ReadAllText(path, Encoding.UTF8);
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            if (!root.TryGetProperty("symbols", out var symbols) ||
                !symbols.TryGetProperty("NQ", out var nq) ||
                !nq.TryGetProperty("levels", out var levelsElement) ||
                levelsElement.ValueKind != JsonValueKind.Array)
            {
                return GexDailySnapshot.Unavailable("symbols.NQ.levelsMissing");
            }

            var levels = new List<GexLevel>();
            foreach (var item in levelsElement.EnumerateArray())
            {
                if (!TryReadDecimal(item, "price", out var price) || price < 10000m || price > 50000m || !IsTickAligned(price, tickSize))
                    continue;
                var levelType = ReadString(item, "levelType");
                var label = ReadString(item, "label");
                if (string.IsNullOrWhiteSpace(levelType) || string.IsNullOrWhiteSpace(label))
                    continue;
                TryReadDecimal(item, "oi", out var openInterest);
                levels.Add(new GexLevel(levelType, price, label, openInterest, ReadString(item, "side")));
            }

            if (levels.Count == 0)
                return GexDailySnapshot.Unavailable("noValidNqLevels");

            var dataDate = TryReadDate(root, "dataDate");
            var updatedAt = TryReadDateTimeOffset(root, "updatedAt");
            var availableAt = TryReadDateTimeOffset(nq, "dataAvailableTime");
            var dataDateStale = dataDate is null || nowUtc.UtcDateTime.Date.Subtract(dataDate.Value.ToDateTime(TimeOnly.MinValue)).TotalDays > 3;

            var lastSyncCompletedUtc = TryReadSyncCompletedAtUtc(syncStatePath);
            var syncStale = lastSyncCompletedUtc is not null && nowUtc.UtcDateTime - lastSyncCompletedUtc.Value > SyncFreshnessThreshold;

            var stale = dataDateStale || syncStale;
            var detail = dataDateStale ? "dataDateOlderThan3Days" : syncStale ? "syncOlderThan90Minutes" : "validated";
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
            return new GexDailySnapshot(stale ? "Stale" : "Ready", detail, dataDate, updatedAt, availableAt, hash, raw, levels);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return GexDailySnapshot.Unavailable($"loadFailed:{ex.GetType().Name}");
        }
    }

    /// <summary>
    /// Reads the scheduler's sync-state file (written by Start-OPFGexSnapshotScheduler.ps1 after every
    /// successful vendor download) and returns its <c>completedAt</c> timestamp in UTC. Returns null
    /// when the path is not supplied, the file is missing, or the field cannot be parsed; callers must
    /// treat null as "freshness unknown", not as "stale", so that this optional overlay never produces
    /// a false Stale status when the scheduler has not been deployed yet.
    /// </summary>
    private static DateTime? TryReadSyncCompletedAtUtc(string? syncStatePath)
    {
        if (string.IsNullOrWhiteSpace(syncStatePath) || !File.Exists(syncStatePath))
            return null;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(syncStatePath, Encoding.UTF8));
            var completedAt = ReadString(document.RootElement, "completedAt");
            return DateTimeOffset.TryParse(completedAt, out var value) ? value.UtcDateTime : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

    private static bool TryReadDecimal(JsonElement element, string name, out decimal value)
    {
        value = 0m;
        return element.TryGetProperty(name, out var item) &&
            (item.ValueKind == JsonValueKind.Number ? item.TryGetDecimal(out value) : decimal.TryParse(item.GetString(), out value));
    }

    private static DateOnly? TryReadDate(JsonElement element, string name) =>
        DateOnly.TryParse(ReadString(element, name), out var value) ? value : null;

    private static DateTimeOffset? TryReadDateTimeOffset(JsonElement element, string name) =>
        DateTimeOffset.TryParse(ReadString(element, name), out var value) ? value : null;

    private static bool IsTickAligned(decimal price, decimal tickSize) =>
        tickSize > 0m && decimal.Round(price / tickSize, 8) == decimal.Round(price / tickSize, 0);
}
