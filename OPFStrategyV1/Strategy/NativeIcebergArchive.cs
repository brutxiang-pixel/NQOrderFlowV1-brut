using System.Text.Json;

namespace OPFStrategyV1.Strategy;

public sealed record NativeIcebergEvent(string Instrument, long Id, DateTime Time, decimal Price, bool IsLong,
    decimal Hidden, decimal Filled, bool Deleted, DateTime CapturedAtUtc);

public static class NativeIcebergArchive
{
    private static readonly object Sync = new();
    public static void Append(string directory, NativeIcebergEvent item)
    {
        lock (Sync)
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"native_icebergs_{item.Time:yyyyMMdd}.jsonl");
            File.AppendAllText(path, JsonSerializer.Serialize(item) + Environment.NewLine);
        }
    }

    public static NativeIcebergEvent[] Read(string directory, DateTime day)
    {
        var path = Path.Combine(directory, $"native_icebergs_{day:yyyyMMdd}.jsonl");
        if (!File.Exists(path)) return Array.Empty<NativeIcebergEvent>();
        lock (Sync) return File.ReadLines(path).Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => JsonSerializer.Deserialize<NativeIcebergEvent>(x)!).Where(x => x is not null).OrderBy(x => x.CapturedAtUtc).ToArray();
    }

    public static bool AvailableAsOf(NativeIcebergEvent item, DateTime now) =>
        item.Time <= now && item.CapturedAtUtc <= now && now - item.Time <= TimeSpan.FromSeconds(30);
}
