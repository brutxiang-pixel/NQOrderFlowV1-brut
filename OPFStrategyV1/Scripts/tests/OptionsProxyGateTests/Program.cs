using OPFStrategyV1.Strategy;
var now = new DateTime(2026, 10, 12, 14, 0, 0, DateTimeKind.Utc);
foreach (var stop in new[] { 14m, 15.75m, 24.25m, 24.75m, 50m })
    if (StructureRiskPolicy.Reject(stop, 20m, 1m) is not null || StructureRiskPolicy.Reject(stop, 2m, 1m) is not null)
        throw new Exception("Approved structural risk must pass for NQ and MNQ.");
if (StructureRiskPolicy.Reject(50.25m, 20m, 1m) is null || StructureRiskPolicy.Reject(0m, 2m, 1m) is null ||
    StructureRiskPolicy.Applies("StartupTestOrder") || StructureRiskPolicy.Applies("ZoneBirthResearch"))
    throw new Exception("Structural risk cap and path scope must remain bounded.");
if (!StructureRiskPolicy.Reject(51m, 20m, 1m)!.Contains("dollars=1020") ||
    !StructureRiskPolicy.Reject(51m, 2m, 1m)!.Contains("dollars=102"))
    throw new Exception("Instrument risk dollars must use the correct point value.");
Console.WriteLine("PASS structural risk accepts four logged candidates, 50pt boundary, rejects excess and reports correct dollars");
void AssertClock(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); }
AssertClock(!ExecutionClock.IsHistorical(DateTime.SpecifyKind(now.AddMinutes(-5), DateTimeKind.Unspecified), now), "UTC candle without Kind stays live on Beijing host");
AssertClock(!ExecutionClock.IsHistorical(now, now), "UTC live candle");
AssertClock(ExecutionClock.IsHistorical(now.AddDays(-1), now), "old Replay candle stays historical");
AssertClock(ExecutionClock.MarketSilence(now, null, true, now.AddSeconds(-1)) == TimeSpan.FromSeconds(1), "missing SDK metrics use observed chart reception");
AssertClock(ExecutionClock.MarketSilence(now, now.AddSeconds(-20), true, now) == TimeSpan.FromSeconds(20), "stale SDK reception cannot be hidden by chart fallback");
AssertClock(ExecutionClock.MarketSilence(now, null, false, now) is null, "disconnected feed cannot use chart fallback");
AssertClock(ExecutionClock.MarketSilence(now, null, true, null) is null, "no reception evidence stays unavailable");
var profile = OPFStrategyV1.Core.Profiles.ProfileCatalog.Select("MNQ_0.1", "MNQ_2Contract_Evidence");
var ids = Enumerable.Range(0, 100).AsParallel().Select(_ => OPFStrategyV1.Core.Snapshots.ConfigSnapshot.Create(
    profile, "test", "test", OPFStrategyV1.Core.Configuration.ActualExecutionSettings.Default()).SnapshotId).ToArray();
AssertClock(ids.Distinct().Count() == ids.Length, "simultaneous strategy instances have unique log IDs");
var warmEngine = new OPFStrategyV1.Regime.TrendScoreEngine();
OPFStrategyV1.Regime.RegimeResult? warmResult = null;
for (var i = 0; i < 80; i++)
{
    var price = 100m + i;
    warmResult = warmEngine.Update(new OPFStrategyV1.Core.MarketData.OpfCandle(i, now.Date.AddMinutes(i * 5), price, price + 2m, price - 1m, price + 1m, 100m, price - 2m));
}
AssertClock(warmResult!.BullTrendScore.Components.All(x => x.RawValue is not ("VwapUnavailable" or "AtrUnavailable" or "InsufficientBars")), "history warmup fills VWAP and ATR components");
EsOptionsProxySnapshot Context(int age = 0, decimal coverage = 1m, decimal? es = 5000m, decimal? prior = 4998m, decimal? em = 20m, string status = "ModelReady") => new(status, "test", "ES", now.AddSeconds(-age), now.Date, 5000m, 5010m, 4990m, null, em, 0m, "DealerGammaUnknown", coverage, "UnsignedGammaOi", "test") { UnderlyingPrice = es, UnderlyingPrice60sAgo = prior, PriceReferenceAtUtc = now.AddSeconds(-age - 60), UnderlyingPrice5mAgo = prior, Price5mReferenceAtUtc = now.AddSeconds(-age - 300), AtmQuotesReady = true, LocalQuoteCoverage = 1m, CallQuoteCoverage = 1m, PutQuoteCoverage = 1m, AnchorTradingDay = now.Date, ExpectedMoveAnchor = 5000m, ExpectedMoveAtAnchor = em, GammaConcentrationStrike = 5000m, GammaConcentrationShare = .25m };
string? Gate(EsOptionsProxySnapshot context, string path = "BreakawayFvg", bool isLong = true, decimal close = 109m) => OptionsProxyEntryGate.Evaluate(context, now, path, isLong, 100m, 110m, 99m, close, 10m);
void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); }
Check(Gate(Context()) is null, "ES response aligned breakout allowed");
Check(Gate(Context(es: 4998m, prior: 5000m)) == "OptionsGate:ESResponseDisagreesWithBreakout", "ES response disagrees with long breakout");
Check(Gate(Context(), isLong: false) == "OptionsGate:ESResponseDisagreesWithBreakout", "ES response disagrees with short breakout");
Check(Gate(Context(es: 5000m, prior: 5000m), path: "SignificantZoneFirstTouchLong", close: 100.5m) is null, "non-breakout path uses ES response as context only");
Check(Gate(Context(status: "Unavailable")) == "OptionsGate:DataUnavailable", "missing data blocked");
Check(Gate(Context(age: -1)) == "OptionsGate:FutureData", "future data blocked");
Check(Gate(Context(age: 301)) == "OptionsGate:Stale", "stale data blocked");
Check(Gate(Context() with { LocalQuoteCoverage = .49m }) == "OptionsGate:LocalQuotesIncomplete", "incomplete local data blocked");
Check(Gate(Context(coverage: .13m)) is null, "far strikes no longer block complete ATM and local quotes");
Check(Gate(Context() with { AtmQuotesReady = false }) == "OptionsGate:QuoteQualityUnavailable", "missing ATM pair remains blocked");
Check(Gate(Context() with { CallQuoteCoverage = .49m }, path: "ESWallRejectionReversal") == "OptionsGate:WallQuotesIncomplete", "wall path needs call and put coverage");
Check(Gate(Context() with { CallQuoteCoverage = .13m }, path: "ObservationConfirm") is null, "OC uses local quality without global wall coverage");
Check(Gate(Context(), path: "ObservationConfirm") is null, "observation confirmation preserved");
Check(Gate(Context(), path: "ESResponseAlignedBreakout") is null, "ES response path allowed");
Check(Gate(Context(es: 4998m, prior: 5000m) with { UnderlyingPrice60sAgo = 4998m }, path: "ESWallRejectionReversal", isLong: true) == "OptionsGate:ESWallNotReached", "wall rejection requires wall proximity");
Check(Gate(Context(es: 5020m, prior: 5018m), path: "ESExpectedMoveExpansion") is null, "expected move expansion allowed");
Check(Gate(Context(es: 5070m, prior: 5068m), path: "ESExpectedMoveExpansion") == "OptionsGate:ExpectedMoveOverextended", "final gate agrees with generator 125 percent upper bound");
var currentEvidence = Context() with { EvidenceSchema = 2, UnderlyingObservedAtUtc = now, OptionQuotesObservedAtUtc = now, AnchorEstablishedAtUtc = now.AddHours(-1), AnchorSource = "FirstValidSessionObservation", AnchorSeries = "test", AnchorTradingDay = OptionsProxyExpiry.ExchangeDate(now) };
Check(Gate(currentEvidence with { Status = "PriceReady", AtmQuotesReady = false, LocalQuoteCoverage = 0m }, path: "ObservationConfirm") is null, "structural path uses live ES response without unrelated option quote requirement");
Check(Gate(currentEvidence with { Status = "PriceReady" }, path: "ESResponseAlignedBreakout") == "OptionsGate:DataUnavailable", "ES family retains option model requirement");
Check(Gate(currentEvidence with { UnderlyingObservedAtUtc = now.AddSeconds(-91) }) == "OptionsGate:UnderlyingStale", "new snapshot cannot mask stale underlying observation");
Check(Gate(currentEvidence with { OptionQuotesObservedAtUtc = now.AddSeconds(-91) }, path: "ESResponseAlignedBreakout") == "OptionsGate:OptionQuotesStale", "new snapshot cannot mask stale option evidence");
Check(Gate(currentEvidence with { UnderlyingPrice = 4992m, UnderlyingPrice5mAgo = 4989m, UnderlyingPrice60sAgo = 4991m }, path: "ESWallRejectionReversal") == "OptionsGate:WallEventUnavailable", "wall proximity does not manufacture touch event");
Check(Gate(currentEvidence with { UnderlyingPrice = 4992m, UnderlyingPrice5mAgo = 4989m, UnderlyingPrice60sAgo = 4991m, EventLevel = 4990m, EventTouchedAtUtc = now.AddSeconds(-3), EventKind = "Reject" }, path: "ESWallRejectionReversal") is null, "frozen wall rejection admitted");
Check(Gate(currentEvidence with { UnderlyingPrice = 5002m, UnderlyingPrice5mAgo = 5000m }, path: "ESGammaConcentrationResponse") == "OptionsGate:GammaEventUnavailable", "gamma concentration alone cannot choose direction");
Check(Gate(currentEvidence with { UnderlyingPrice = 5002m, UnderlyingPrice5mAgo = 5000m, EventLevel = 5000m, EventTouchedAtUtc = now.AddSeconds(-3), EventKind = "CrossHold" }, path: "ESGammaConcentrationResponse") is null, "gamma crossing response admitted without dealer sign assumption");
Check(Gate(currentEvidence with { UnderlyingPrice = 4998m, UnderlyingPrice5mAgo = 5000m, UnderlyingPrice60sAgo = 4999m, EventLevel = 5000m, EventTouchedAtUtc = now.AddSeconds(-3), EventKind = "Reject" }, path: "ESGammaConcentrationResponse", isLong: false, close: 99.5m) is null, "short gamma rejection uses observed response symmetrically");
Check(Gate(currentEvidence with { UnderlyingPrice = 5002m, UnderlyingPrice5mAgo = 5000m, EventLevel = 5000m, EventTouchedAtUtc = now.AddMinutes(-6), EventKind = "CrossHold" }, path: "ESGammaConcentrationResponse") == "OptionsGate:GammaEventUnavailable", "expired level event cannot re-enter");
Check(Gate(currentEvidence with { Status = "PriceReady", UnderlyingPrice5mAgo = null }, path: "ObservationConfirm") == "OptionsGate:ESResponseWarmingUp", "price-only structural path still needs ES history");
Check(Gate(currentEvidence with { UnderlyingPrice = 5020m, UnderlyingPrice5mAgo = 5018m, AnchorEstablishedAtUtc = now.AddSeconds(1) }, path: "ESExpectedMoveExpansion") == "OptionsGate:ExpectedMoveAnchorInvalid", "future expected move anchor rejected");
Check(Gate(Context(es: 5002m, prior: 5000m), path: "ESGammaConcentrationResponse") is null, "gamma concentration response allowed");
Check(Gate(Context(es: 4992m, prior: 4989m) with { ResponseLow = 4989m }, path: "ESWallRejectionReversal") is null, "touched put wall reclaimed allows long");
Check(Gate(Context(es: 4992m, prior: 4989m), path: "ESWallRejectionReversal") == "OptionsGate:ESWallTouchNotReclaimed", "wall proximity without touch is rejected");
Check(Gate(Context() with { PriceReferenceAtUtc = null }, path: "ESResponseAlignedBreakout") == "OptionsGate:ESResponseWarmingUp", "new path cannot use missing response reference");
Check(Gate(Context() with { PriceReferenceAtUtc = now.AddSeconds(-120) }, path: "ESGammaConcentrationResponse") == "OptionsGate:ESResponseReferenceInvalid", "old reference rejected");
Check(Gate(Context() with { UnderlyingPrice5mAgo = null }, path: "ESResponseAlignedBreakout") == "OptionsGate:ESResponseWarmingUp", "M5 history required");
Check(Gate(Context() with { UnderlyingPrice60sAgo = 5000m }, path: "ESResponseAlignedBreakout") is null, "M5 aligned with flat last minute allowed");
Check(Gate(Context() with { UnderlyingPrice60sAgo = 5002m }, path: "ESResponseAlignedBreakout") == "OptionsGate:ESFastResponseOpposesEntry", "strong M1 reversal blocks M5 long");
Check(Gate(Context() with { Price5mReferenceAtUtc = now.AddSeconds(-400) }, path: "ESResponseAlignedBreakout") == "OptionsGate:ESResponseReferenceInvalid", "old M5 reference rejected");
Check(Context().HasAnchorFor(now.Date, now), "same-day anchor restored");
Check(!Context().HasAnchorFor(now.Date.AddDays(1), now.AddDays(1)), "prior-day anchor not reused");
Check(!(Context() with { CapturedAtUtc = now.AddSeconds(1) }).HasAnchorFor(now.Date, now), "future anchor not reused");
Check(Gate(Context(es: 5020m, prior: 5022m) with { UnderlyingPrice60sAgo = 5020m }, path: "ESExpectedMoveExpansion") == "OptionsGate:ExpectedMoveNoFollowThrough", "expansion against current response rejected");
Check(Gate(Context() with { GammaConcentrationShare = .17m }, path: "ESGammaConcentrationResponse") == "OptionsGate:GammaConcentrationInsufficient", "diffuse exposure does not trigger cluster path");
var expiry = OptionsProxyExpiry.ToUtc(new DateTime(2026, 10, 12), "E2AV6 C7880");
Check(expiry == new DateTime(2026, 10, 12, 20, 0, 0, DateTimeKind.Utc), "reported 0DTE expires at Chicago close");
Check(expiry > now, "reported live 0DTE remains eligible");
Check(expiry <= now.AddHours(6), "expired 0DTE is rejected at cutoff");
Check(OptionsProxyExpiry.ToUtc(new DateTime(2026, 12, 7), "E1AZ6 P7800").Hour == 21, "winter DST handled");
Check(OptionsProxyExpiry.ToUtc(new DateTime(2026, 11, 27), "EW4X6 P7800").Hour == 18, "Thanksgiving early close");
Check(OptionsProxyExpiry.ToUtc(new DateTime(2026, 10, 12), "ESZ6 C7880").Hour == 0, "unknown quarterly expiry not extended");
var explicitUtc = new DateTime(2026, 10, 12, 18, 15, 0, DateTimeKind.Utc);
Check(OptionsProxyExpiry.ToUtc(explicitUtc, "E2AV6 C7880") == explicitUtc, "explicit expiry preserved");
Check(OptionsProxyExpiry.ExchangeDate(new DateTime(2026, 10, 13, 1, 0, 0, DateTimeKind.Utc)) == new DateTime(2026, 10, 12), "series date uses exchange calendar");
var directory = Path.Combine(Path.GetTempPath(), "opf-options-test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
try
{
    var latest = Path.Combine(directory, "latest.json");
    var archive = Path.Combine(directory, "history.jsonl");
    Check(OptionsProxySnapshotStore.ReadLatest(latest).Status == "Unavailable", "missing ES source is explicit");
    var priceOnly = currentEvidence with { Status = "PriceReady", CapturedAtUtc = now.AddSeconds(-1) };
    var priceArchive = Path.Combine(directory, "price-history.jsonl");
    OptionsProxySnapshotStore.Publish(latest, priceArchive, priceOnly, true);
    Check(OptionsProxySnapshotStore.ReadAsOf(priceArchive, now).Status == "PriceReady", "Replay preserves price-only context rather than falling back to older option model");
    var first = new EsOptionsProxySnapshot("ModelReady", "ES", "ES", now.AddSeconds(-10), now.Date, 7800m, 7850m, 7750m, null, 20m, .2m, "PositiveGammaProxy", .6m, "ModelAssumption", "test") { AnchorTradingDay = now.Date, ExpectedMoveAnchor = 7800m, ExpectedMoveAtAnchor = 20m };
    OptionsProxySnapshotStore.Publish(latest, archive, first, true);
    var next = first with { CapturedAtUtc = now.AddSeconds(10), NetGammaProxy = -.2m };
    OptionsProxySnapshotStore.Publish(latest, archive, next, true);
    Check(OptionsProxySnapshotStore.ReadLatest(latest) == next, "consumer observes updated ES snapshot");
    Check(OptionsProxySnapshotStore.ReadLatest(latest).HasAnchorFor(now.Date, now.AddSeconds(10)), "anchor survives file round trip and restart");
    Check(OptionsProxySnapshotStore.ReadAsOf(archive, now) == first, "Replay excludes future ES context");
    Check(OptionsProxySnapshotStore.ReadAsOf(archive, now.AddMinutes(-1)).Status == "Unavailable", "Replay without prior history remains blocked");
    File.AppendAllText(archive, "{interrupted");
    Check(OptionsProxySnapshotStore.ReadAsOf(archive, now) == first, "interrupted archive tail preserves valid evidence");
    var newest = next with { CapturedAtUtc = now.AddSeconds(20) };
    OptionsProxySnapshotStore.Publish(latest, archive, newest, false);
    Check(OptionsProxySnapshotStore.ReadLatest(latest) == newest, "live publication independent of archive cadence");
    Parallel.Invoke(
        () => { for (var i = 0; i < 100; i++) OptionsProxySnapshotStore.Publish(latest, archive, newest, false); },
        () => { for (var i = 0; i < 100; i++) if (OptionsProxySnapshotStore.ReadLatest(latest).Status != "ModelReady") throw new Exception("concurrent snapshot read"); });
    Check(true, "100 concurrent snapshot publications and reads");
    Check(OptionsProxyEntryGate.Evaluate(first, now.AddMinutes(6), "ObservationConfirm", true, 100m, 110m, 99m, 109m, 10m) == "OptionsGate:Stale", "stopped ES source cannot keep consumer trading indefinitely");
}
finally
{
    foreach (var file in Directory.EnumerateFiles(directory)) File.Delete(file);
    Directory.Delete(directory);
}
