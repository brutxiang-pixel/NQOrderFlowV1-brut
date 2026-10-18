using System.Text.Json;

namespace OPFStrategyV1.Strategy;

internal sealed record EsOptionsProxySnapshot(string Status, string Source, string Underlying, DateTime CapturedAtUtc, DateTime? Expiration, decimal? AtmStrike, decimal? CallWallProxy, decimal? PutWallProxy, decimal? ZeroGammaProxy, decimal? ExpectedMoveProxy, decimal NetGammaProxy, string Regime, decimal Coverage, string Confidence, string Detail)
{
    public int EvidenceSchema { get; init; }
    public DateTime? UnderlyingObservedAtUtc { get; init; }
    public DateTime? OptionQuotesObservedAtUtc { get; init; }
    public DateTime? AnchorEstablishedAtUtc { get; init; }
    public string? AnchorSource { get; init; }
    public string? AnchorSeries { get; init; }
    public decimal? EventLevel { get; init; }
    public DateTime? EventTouchedAtUtc { get; init; }
    public string? EventKind { get; init; }
    public decimal? UnderlyingPrice { get; init; }
    public decimal? UnderlyingPrice60sAgo { get; init; }
    public DateTime? PriceReferenceAtUtc { get; init; }
    public decimal? UnderlyingPrice5mAgo { get; init; }
    public DateTime? Price5mReferenceAtUtc { get; init; }
    public bool? AtmQuotesReady { get; init; }
    public decimal? LocalQuoteCoverage { get; init; }
    public decimal? CallQuoteCoverage { get; init; }
    public decimal? PutQuoteCoverage { get; init; }
    public DateTime? AnchorTradingDay { get; init; }
    public bool HasAnchorFor(DateTime tradingDay, DateTime utcNow) =>
        AnchorTradingDay == tradingDay && CapturedAtUtc <= utcNow &&
        ExpectedMoveAnchor is > 0m && ExpectedMoveAtAnchor is > 0m &&
        (!AnchorEstablishedAtUtc.HasValue || AnchorEstablishedAtUtc <= utcNow);
    public decimal? ResponseHigh { get; init; }
    public decimal? ResponseLow { get; init; }
    public decimal? ExpectedMoveAnchor { get; init; }
    public decimal? ExpectedMoveAtAnchor { get; init; }
    public decimal? GammaConcentrationStrike { get; init; }
    public decimal GammaConcentrationShare { get; init; }
    public static EsOptionsProxySnapshot Unavailable(string detail) => new("Unavailable", "IOptionsDataProvider", "-", DateTime.UtcNow, null, null, null, null, null, null, 0m, "Unknown", 0m, "None", detail);
}

internal static class OptionsProxySnapshotStore
{
    private static readonly object Sync = new();

    public static void Publish(string latestPath, string archivePath, EsOptionsProxySnapshot snapshot, bool archive)
    {
        lock (Sync)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(latestPath)!);
            var json = JsonSerializer.Serialize(snapshot);
            var temporary = latestPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, json);
                File.Move(temporary, latestPath, overwrite: true);
                if (archive) File.AppendAllText(archivePath, json + Environment.NewLine);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }

    public static EsOptionsProxySnapshot ReadLatest(string latestPath)
    {
        lock (Sync)
        {
            if (!File.Exists(latestPath)) return EsOptionsProxySnapshot.Unavailable("waitingForEsSource");
            using var stream = new FileStream(latestPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return JsonSerializer.Deserialize<EsOptionsProxySnapshot>(stream) ?? EsOptionsProxySnapshot.Unavailable("sharedSnapshotInvalid");
        }
    }

    public static EsOptionsProxySnapshot ReadAsOf(string archivePath, DateTime decisionUtc)
    {
        var context = EsOptionsProxySnapshot.Unavailable("asOfSnapshotMissing");
        if (!File.Exists(archivePath)) return context;
        using var stream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            EsOptionsProxySnapshot? row;
            // An append interrupted by shutdown must not discard earlier valid history.
            try { row = JsonSerializer.Deserialize<EsOptionsProxySnapshot>(line); }
            catch (JsonException) { continue; }
            if (row is { Status: "ModelReady" or "PriceReady" } && row.CapturedAtUtc <= decisionUtc &&
                (context.Status is not ("ModelReady" or "PriceReady") || row.CapturedAtUtc > context.CapturedAtUtc)) context = row;
        }
        return context;
    }
}
