using System.Text.Json;

namespace OPFStrategyV1.Core.Signals;

public sealed record SignificantZoneHistoricalReference(
    string Instrument,
    string ModelVersion,
    string ZoneId,
    TradeSide Side,
    decimal InnerBoundary,
    decimal OuterBoundary,
    DateTime CreatedTime,
    int CreatedBar,
    SignificantZoneState State,
    int TestCount,
    SignificantZoneGrade Grade,
    decimal Score,
    string LastReason,
    DateTime UpdatedAt);

public sealed class SignificantZoneHistoryLedger
{
    private const string FileName = "significant_zone_history.jsonl";
    private readonly string _path;

    public SignificantZoneHistoryLedger(string directory)
    {
        _path = Path.Combine(directory, FileName);
    }

    public void Append(string instrument, string modelVersion, SignificantZone zone, DateTime updatedAt)
    {
        var reference = new SignificantZoneHistoricalReference(
            instrument,
            modelVersion,
            zone.ZoneId,
            zone.Side,
            zone.InnerBoundary,
            zone.OuterBoundary,
            zone.CreatedTime,
            zone.CreatedBar,
            zone.State,
            zone.TestCount,
            zone.Strength?.Grade ?? SignificantZoneGrade.C,
            zone.Strength?.Score ?? 0m,
            zone.LastReason,
            updatedAt);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.AppendAllText(_path, JsonSerializer.Serialize(reference) + Environment.NewLine);
    }

    public IReadOnlyList<SignificantZoneHistoricalReference> Load(string instrument, string modelVersion)
    {
        if (!File.Exists(_path))
            return Array.Empty<SignificantZoneHistoricalReference>();

        var latest = new Dictionary<string, SignificantZoneHistoricalReference>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(_path))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            try
            {
                var reference = JsonSerializer.Deserialize<SignificantZoneHistoricalReference>(line);
                if (reference is null ||
                    !string.Equals(reference.Instrument, instrument, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(reference.ModelVersion, modelVersion, StringComparison.Ordinal))
                {
                    continue;
                }
                latest[reference.ZoneId] = reference;
            }
            catch (JsonException)
            {
            }
        }

        return latest.Values.OrderBy(x => x.CreatedTime).ToArray();
    }
}


