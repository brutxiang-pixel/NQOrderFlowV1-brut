using System.Text.Json;
using OPFStrategyV1.Strategy;
using OPFStrategyV1.Core.MarketData;

void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); }
var account = new FakePortfolio { AccountID = "DEMO2AC94", Balance = 100m };
var scope = LiveAccountScope.FromPortfolio(account);
account.Balance = 250m;
Check(scope == "DEMO2AC94" && LiveAccountScope.FromPortfolio(account) == scope, "SDK AccountID stays stable when balance changes");
Check(LiveAccountScope.FromPortfolio(new DynamicDescription()) == "unresolved", "dynamic description cannot become account identity");
var legacy = new LegacyPnlIdentity();
var fields = new string[23]; Array.Fill(fields, "");
fields[6] = "trade-1"; fields[7] = "2026-10-14T06:03:00Z";
fields[20] = "DEMO2AC94_Bal_100_PnL_2"; fields[21] = "MNQZ6"; fields[22] = "False";
Check(legacy.Matches(fields, "DEMO2AC94", "MNQZ6", false) == true, "dynamic scope restores stable account");
Check(legacy.Matches(fields, "DEMO2AC9", "MNQZ6", false) == false, "account prefixes cannot match");
Check(legacy.Matches(fields, "DEMO2AC94", "NQZ6", false) == false, "product isolation preserved");
Check(legacy.Matches(fields, "DEMO2AC94", "MNQZ6", true) == false, "live ledger excluded from Replay");
var old = fields.Take(20).ToArray();
var stamp = new DateTime(2026,10,14,6,3,2,DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
legacy.Observe(stamp + ",000 INFO ATAS Sim : Order registered 1/2 Buy Market MNQZ6@CME_Ind on DEMO2AC94 at 0/0 vol 1/1 state Active OPF|trade-1|ENTRY|ObservationConfirm");
Check(legacy.Matches(old, "DEMO2AC94", "MNQZ6", false) == true, "confirmed platform order restores legacy identity");
Check(legacy.Matches(old, "OTHER", "MNQZ6", false) == false, "other account excluded");
Check(legacy.Matches(old, "DEMO2AC94", "MNQZ6", true) is null, "platform evidence cannot invent Replay identity");
old[7] = "2026-10-13T06:03:00Z";
Check(legacy.Matches(old, "DEMO2AC94", "MNQZ6", false) is null, "unrelated wall time rejected");
old[7] = fields[7];
legacy.Observe(stamp + ",000 INFO ATAS Sim : Order registered 3/4 Buy Market MNQZ6@CME_Ind on OTHER at 0/0 vol 1/1 state Active OPF|trade-1|ENTRY|ObservationConfirm");
Check(legacy.Matches(old, "DEMO2AC94", "MNQZ6", false) is null, "ambiguous identity stays unresolved");
var now = new DateTime(2026, 10, 14, 5, 0, 0, DateTimeKind.Utc);
var context = new EsOptionsProxySnapshot("ModelReady", "synthetic-function-test", "ES", now, now.Date,
    5000m, 5010m, 4990m, null, 20m, 0m, "Unsigned", 1m, "Test", "not profit evidence")
{
    UnderlyingPrice = 5002m, UnderlyingPrice5mAgo = 5000m, UnderlyingPrice60sAgo = 5001m,
    Price5mReferenceAtUtc = now.AddMinutes(-5), PriceReferenceAtUtc = now.AddMinutes(-1),
    AtmQuotesReady = true, LocalQuoteCoverage = 1m, CallQuoteCoverage = 1m, PutQuoteCoverage = 1m,
    ExpectedMoveAnchor = 4985m, ExpectedMoveAtAnchor = 20m, ResponseLow = 4989m,
    GammaConcentrationStrike = 5000m, GammaConcentrationShare = .25m
};
var evidence = new OpfCandle(1, now, 100m, 110m, 99m, 109m, 100m, 100m);
var paths = new[] { "ObservationConfirm", "BreakawayFvg_Qualified",
    "FailureReverse_ObservationInvalidated_WideStop1_5R", "FailureReverse_RetestFailed",
    "ESResponseAlignedBreakout", "ESWallRejectionReversal", "ESExpectedMoveExpansion", "ESGammaConcentrationResponse" };
var syntheticPackets = new List<OptionsGatePacket>();
foreach (var path in paths)
{
    var input = path == "ESWallRejectionReversal" ? context with { PutWallProxy = 5000m } : context;
    var packet = new OptionsGatePacket("synthetic", path, "MNQ", path, now, true, evidence, 10m, input, null);
    syntheticPackets.Add(packet);
    var decoded = JsonSerializer.Deserialize<OptionsGatePacket>(JsonSerializer.Serialize(packet))!;
    Check(decoded.Replay() is null, path + " frozen input round trip");
    Check((packet with { Context = input with { CapturedAtUtc = now.AddSeconds(1) } }).Replay() == "OptionsGate:FutureData", path + " rejects future input");
}
var reaction = new ReactionPacket("synthetic", "s", "MNQ", "ObservationConfirm", true, 101m, 100m, 98m,
    .25m, now, now.AddSeconds(10), now.AddSeconds(-3), 20m, 100m, 2m, null);
Check(reaction.Replay() is null, "production reaction function accepts held flow");
Check((reaction with { Time = now.AddSeconds(11) }).Replay() == "Expired", "expired reaction rejected");
Check((reaction with { SignedVolume = 0m }).Replay() == "WaitingFlow", "flow rejection replay");

if (args.Length == 0) return;
if (args[0] == "--legacy-audit")
{
    var archive = new LegacyPnlIdentity();
    foreach (var file in Directory.EnumerateFiles(args[1], "app_*.log").Where(x => File.GetLastWriteTimeUtc(x) >= DateTime.UtcNow.AddDays(-3)))
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line) archive.Observe(line);
    }
    foreach (var file in Directory.EnumerateFiles(args[2], "*_live_account_pnl.csv"))
        foreach (var line in File.ReadLines(file).Skip(1))
        {
            var row = line.Split(',');
            if (row.Length < 18 || !DateTime.TryParse(row[8].Trim('"'), out var exit) || exit < DateTime.Parse(args[5])) continue;
            var product = args[4];
            if (!row[3].Trim('"').StartsWith(product.StartsWith("MNQ") ? "MNQ_" : "NQ_")) continue;
            Console.WriteLine($"LEGACY-AUDIT {row[6]} {product} net={row[16]} matched={archive.Matches(row, args[3], product, false)}");
        }
    return;
}
if (args[0] == "--write-synthetic")
{
    Directory.CreateDirectory(args[1]);
    File.WriteAllLines(Path.Combine(args[1], "synthetic_validation_options.jsonl"), syntheticPackets.Select(x => JsonSerializer.Serialize(x)));
    File.WriteAllText(Path.Combine(args[1], "synthetic_validation_reaction.jsonl"), JsonSerializer.Serialize(reaction) + Environment.NewLine);
    Console.WriteLine("SYNTHETIC fixtures only; never include in profit or real coverage reports");
    return;
}
var results = new List<object>();
var failures = 0;
foreach (var file in Directory.EnumerateFiles(args[0], "*_validation_*.jsonl").Order())
{
    if (!file.EndsWith("_options.jsonl") && !file.EndsWith("_reaction.jsonl")) continue;
    var lineNumber = 0;
    foreach (var line in File.ReadLines(file))
    {
        lineNumber++;
        string? expected, actual; string path, signal, product, snapshot;
        if (file.EndsWith("_options.jsonl"))
        {
            var packet = JsonSerializer.Deserialize<OptionsGatePacket>(line)!;
            expected = packet.ExpectedReason; actual = packet.Replay(); path = packet.Path; signal = packet.SignalId;
            product = packet.Product; snapshot = packet.SnapshotId;
        }
        else
        {
            var packet = JsonSerializer.Deserialize<ReactionPacket>(line)!;
            expected = packet.ExpectedReason; actual = packet.Replay(); path = packet.Path; signal = packet.SignalId;
            product = packet.Product; snapshot = packet.SnapshotId;
        }
        var matches = expected == actual;
        if (!matches) failures++;
        results.Add(new { Key = Path.GetFileName(file) + ":" + lineNumber, Snapshot = snapshot, Signal = signal,
            Product = product, Path = path, Expected = expected ?? "Allow", Actual = actual ?? "Allow", Matches = matches });
    }
}
if (args.Length > 1) File.WriteAllText(args[1], JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Frozen gate packets={results.Count} differences={failures}; data provenance is defined by the input manifest; candidate generation and broker callbacks require separate validation");
if (results.Count == 0) Console.WriteLine("NO_REAL_FIXTURES: install capture build and collect candidates before claiming real replay coverage");
if (failures > 0) Environment.ExitCode = 2;

public sealed class FakePortfolio
{
    public string AccountID { get; set; } = "";
    public decimal Balance { get; set; }
    public override string ToString() => $"{AccountID} Bal={Balance}";
}
public sealed class DynamicDescription { public override string ToString() => "Account Bal=100"; }
