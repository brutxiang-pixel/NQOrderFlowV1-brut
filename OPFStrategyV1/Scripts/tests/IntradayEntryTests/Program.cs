using OPFStrategyV1.Strategy;
using OPFStrategyV1.Core.MarketData;

var now = new DateTime(2026, 10, 14, 10, 0, 0, DateTimeKind.Utc);
void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); Console.WriteLine("PASS " + name); }
foreach (var path in new[] { "ESResponseAlignedBreakout", "ESWallRejectionReversal", "ESExpectedMoveExpansion", "ESGammaConcentrationResponse", "CrossMarketIcebergOrderFlowReaction" })
{
    Check(ProfitLockPolicy.TryGet(path, 0, out var stage1) && stage1.TriggerR == 1m && stage1.LockR == .25m, path + " stage1");
    Check(ProfitLockPolicy.TryGet(path, 1, out var stage2) && stage2.TriggerR == 1.25m && stage2.LockR == .5m, path + " stage2");
    Check(!ProfitLockPolicy.TryGet(path, 2, out _), path + " no repeated final lock");
}
var claimKey = "switch-test-" + Guid.NewGuid();
Check(!PhysicalEventExecutionDedup.IsClaimed(claimKey, now), "switch preflight permits unclaimed event without consuming it");
Check(PhysicalEventExecutionDedup.TryClaim(claimKey, now), "actual entry can claim after preflight");
Check(PhysicalEventExecutionDedup.IsClaimed(claimKey, now.AddSeconds(1)), "switch rejects claimed opportunity before exit");
Check(!PhysicalEventExecutionDedup.IsClaimed(claimKey, now.AddHours(7)), "expired event claim does not block switch");
PhysicalEventExecutionDedup.Release(claimKey);
var historyFixture = Path.Combine(Path.GetTempPath(), "opf-history-test-" + Guid.NewGuid());
Directory.CreateDirectory(historyFixture);
try
{
    File.WriteAllText(Path.Combine(historyFixture, "fixture_live_account_pnl.csv"),
        "TradeID,AccountScope,HistoricalReplay,ProductCode,GrossPnLDollars\nmnq,A,False,MNQZ6,12.5\nnq,A,False,NQZ6,-20\nother,B,False,MNQZ6,50\nreplay,A,True,MNQZ6,60\n");
    File.WriteAllText(Path.Combine(historyFixture, "fixture_execution_trades.csv"),
        "TradeID,EntryTime,ExitTime,Side,Quantity,EntryPrice,ExitPrice,StopPrice,TargetPrice,ResearchPath,ExitRole\n" +
        string.Join("\n", new[] { "mnq", "nq", "other", "replay" }.Select(id => id + ",2026-11-04T03:00:00Z,2026-11-04T03:05:00Z,Long,2,100,103.125,95,110,Iceberg,TP")));
    var firstLoad = HistoricalExecutionArchive.Load(historyFixture, "A", "MNQZ6");
    var remount = HistoricalExecutionArchive.Load(historyFixture, "A", "MNQZ6");
    Check(firstLoad.Executions.Count == 1 && remount.Executions.SequenceEqual(firstLoad.Executions), "remount restores scoped history without duplicate or cross-account trades");
    Check(remount.Executions[0].Quantity == 2m && remount.Executions[0].GrossDollars == 12.5m, "archive preserves actual quantity and authoritative gross amount");
    Check(HistoricalExecutionArchive.Load(historyFixture, "unresolved", "MNQZ6").Executions.Count == 0, "unresolved account never imports unrelated history");
}
finally { Directory.Delete(historyFixture, true); }
Check(!IntradayEntryPolicy.IcebergNoProgressExit(true, TimeSpan.FromSeconds(119), 0m, 20m, 98m, 99m, -20m, 100m), "iceberg cannot time exit before 120 seconds");
Check(!IntradayEntryPolicy.IcebergNoProgressExit(true, TimeSpan.FromSeconds(120), 0m, 20m, 100m, 99m, -20m, 100m), "iceberg normal pullback above structure remains open");
Check(!IntradayEntryPolicy.IcebergNoProgressExit(true, TimeSpan.FromSeconds(120), 0m, 20m, 98m, 99m, 20m, 100m), "iceberg no exit without opposite tape");
Check(!IntradayEntryPolicy.IcebergNoProgressExit(true, TimeSpan.FromSeconds(120), 5m, 20m, 98m, 99m, -20m, 100m), "iceberg meaningful prior progress disables no progress exit");
Check(IntradayEntryPolicy.IcebergNoProgressExit(true, TimeSpan.FromSeconds(120), 0m, 20m, 98m, 99m, -15m, 100m), "iceberg failed long structure with opposing flow exits");
Check(IntradayEntryPolicy.IcebergNoProgressExit(false, TimeSpan.FromSeconds(120), 0m, 20m, 102m, 101m, 15m, 100m), "iceberg failed short symmetric");
Check(!IntradayEntryPolicy.IcebergNoProgressExit(false, TimeSpan.FromSeconds(120), 0m, 20m, 102m, 101m, 0m, 0m), "iceberg missing tape cannot force exit");
var alignment = new CrossMarketAlignment();
Check(alignment.Window(300).Reference is null, "no synthetic cross-market warmup");
for (var second = 0; second <= 300; second++) alignment.Observe(now.AddSeconds(second), 5000m + second, 20000m - second);
Check(alignment.Window(300).EsMove == 300m && alignment.Window(300).NqMove == -300m && alignment.Window(300).SameDirection == false, "ES NQ five-minute comparison uses same observation window");
Check(alignment.Window(60).EsRangeFraction == 1m && alignment.Window(60).NqRangeFraction == -1m, "cross-market displacement normalized by own range");
foreach (var reason in new[] { "OptionsGate:Stale", "OptionsGate:OptionQuotesStale", "ActiveTradeLimit:one,two", "LiveLatencyRecovering:stableMs=1", "SecondaryPrimaryNotActuallyProtectedAtBE" })
    Check(CandidateAttemptPolicy.CanRetry("Skip", reason), "temporary blocker remains eligible: " + reason);
foreach (var outcome in new[] { ("Execute", "EntrySend"), ("Skip", "ManualInterventionDetected"), ("ManualAlert", "ManualAlertOnly"), ("Skip", "PathDirectionDisabled"), ("Skip", "OrphanPosition:pos=1") })
    Check(!CandidateAttemptPolicy.CanRetry(outcome.Item1, outcome.Item2), "terminal outcome cannot auto resend: " + outcome.Item2);
var level = new LevelReactionEvent(100m, 99m, now);
Check(LevelReactionEvent.TryCreate(100m, 98m, 99m, 100.5m, now) is null, "model level movement cannot manufacture a touch");
Check(LevelReactionEvent.TryCreate(100m, 100m, 99m, 100.5m, now) is not null, "price crossing stable level creates event");
Check(LevelReactionEvent.TryCreate(100m, 100m, 100m, 100m, now) is null, "stationary price at newly moved model level cannot manufacture touch next tick");
level.Observe(100.5m, now);
Check(level.Response(true, 100.5m, .25m) is null, "single ES observation cannot confirm level response");
level.Observe(100.5m, now.AddSeconds(1)); level.Observe(100.5m, now.AddSeconds(2));
Check(level.Response(true, 100.5m, .25m) == "CrossHold", "cross hold requires observed persistence");
level.Observe(99.5m, now.AddSeconds(3)); level.Observe(99.5m, now.AddSeconds(4)); level.Observe(99.5m, now.AddSeconds(5));
Check(level.Response(false, 99.5m, .25m) == "Reject", "frozen approach distinguishes rejection from crossing");
Check(level.Response(true, 100.25m, .25m) is null && !level.Active(now.AddMinutes(6)), "level response boundary and expiry");
var shadow = new PathOpportunity("s", "OC", true, now, 1, 100m, 90m, 10m, 1.5m, .75m);
Check(!shadow.Observe(now, 1, 111m), "policy observation cannot retrospectively stop on activation tick");
shadow.Observe(now.AddSeconds(1), 1, 100m);
Check(shadow.ProtectedPoints == 0m && shadow.FixedPoints is null, "fixed target continues after protected policy stops");
Check(shadow.Observe(now.AddSeconds(2), 1, 115m) && shadow.FixedPoints == 15m, "shadow follows prices beyond alternative exit");
var peer = new PathOpportunity("p", "ES", true, now.AddSeconds(10), 1, 100.25m, 90m, 10.25m, 1.5m, .75m);
Check(shadow.Overlaps(peer, .25m) && !shadow.Overlaps(new PathOpportunity("x", "ES", false, now, 1, 100m, 110m, 10m, 1.5m, .75m), .25m), "overlap requires direction, event time and structure");
Check(!ExecutionClock.TimeStopDue(1, 0) && !ExecutionClock.TimeStopDue(100, 0), "zero time stop is disabled even after bars elapsed");
Check(!ExecutionClock.TimeStopDue(5, 6) && ExecutionClock.TimeStopDue(6, 6), "positive time stop fires at its existing boundary");
Check(ProfitLockPolicy.TryGet("ObservationConfirm", out var oc) && oc.TriggerR == .75m && oc.LockR == .25m, "observation confirm profit lock policy");
Check(ProfitLockPolicy.TryGet("FailureReverse_RetestFailed", out var fr) && fr.TriggerR == 1.25m && fr.LockR == .5m, "failure reverse profit lock policy");
Check(ProfitLockPolicy.TryGet("ESResponseAlignedBreakout", 0, out var firstLock) && firstLock.TriggerR == 1m && firstLock.LockR == .25m, "first staged lock preserves room after break even");
Check(ProfitLockPolicy.TryGet("ESResponseAlignedBreakout", 1, out var secondLock) && secondLock.TriggerR == 1.25m && secondLock.LockR == .5m, "second staged lock confirms at 1.25R");
Check(!ProfitLockPolicy.TryGet("ESResponseAlignedBreakout", 2, out _), "staged profit lock stops after final stage");
Check(!ProfitLockPolicy.TryGet("FailureReverse_ObservationInvalidated_WideStop1_5R", 0, out _), "wide stop path remains outside staged lock change");
Check(!ProfitLockPolicy.TryGet("Unknown", out _), "unknown path has no profit lock policy");
Check(IntradayEntryPolicy.IcebergStructuralStop(true, 100m, 99m, 98m, 101m, 10m, .25m) == 97m, "iceberg long stop outside local low with buffer");
Check(IntradayEntryPolicy.IcebergStructuralStop(false, 100m, 101m, 99m, 102m, 10m, .25m) == 103m, "iceberg short stop outside local high with buffer");
Check(IntradayEntryPolicy.IcebergStructuralStop(true, 100m, 99m, 99m, 100m, 30m, .25m) == 94m, "iceberg volatility floor widens initial risk");
Check(IntradayEntryPolicy.IcebergStructuralStop(false, 100m, 101m, 100m, 101m, 30m, .25m) == 106m, "iceberg volatility floor symmetric");
Check(ProfitLockPolicy.TryGet("CrossMarketIcebergOrderFlowReaction", out var iceLock) && iceLock.TriggerR > IntradayEntryPolicy.IcebergBreakEvenR && iceLock.TriggerR < IntradayEntryPolicy.IcebergTargetR && iceLock.LockR == .25m, "iceberg break even lock and target separated");
foreach (var path in new[] { "ObservationConfirm", "BreakawayFvg_Qualified", "FailureReverse_ObservationInvalidated_WideStop1_5R", "FailureReverse_RetestFailed", "ESResponseAlignedBreakout", "ESWallRejectionReversal", "ESExpectedMoveExpansion", "ESGammaConcentrationResponse" })
    Check(ProfitLockPolicy.TryGet(path, out var plan) && plan.TriggerR > 0m && plan.LockR > 0m && plan.TriggerR > plan.LockR, $"all eight paths have monotonic profit lock policy: {path}");
Check(ExecutionClock.ExitTimestamp(now, now.AddSeconds(20), now.Date, now.AddSeconds(21), false) == now.AddSeconds(20), "live archive uses actual fill timestamp instead of bar opening");
Check(ExecutionClock.ExitTimestamp(now, now.AddMinutes(-3), now.Date, now.AddSeconds(21), false) == now.AddSeconds(21), "invalid stale live fill uses callback reception");
Check(ExecutionClock.ExitTimestamp(now, now.AddDays(1), now.AddSeconds(30), now.AddDays(1), true) == now.AddSeconds(30), "Replay never imports wall clock fill timestamp");
Check(ExecutionClock.ExitTimestamp(now, null, now.AddSeconds(30), now.AddDays(1), true) == now.AddSeconds(30), "Replay reconciliation uses replay market clock");
var bars = Enumerable.Range(0, 121).Select(i => new OpfCandle(i, now.Date.AddDays(i - 121), 100, 110, 90, i + 100, 1000, 100)).ToArray();
var complete = IntradayEntryPolicy.ClosedAsOf(bars.Select(c => (c, c.Time.AddDays(1))), now.Date.AddDays(-1));
Check(complete.Length == 120, "daily as-of excludes future/incomplete session");
Check(IntradayEntryPolicy.Sma(complete, 120) == 159.5m, "120 complete daily sessions SMA");
Check(IntradayEntryPolicy.Sma(complete.Take(119).ToArray(), 120) is null, "119 sessions cannot pretend SMA120 exists");
Check(IntradayEntryPolicy.Sma(complete, 10) == 214.5m, "SMA10 uses trailing completed sessions");
string? React(bool isLong, decimal price, decimal stop, DateTime time, DateTime? held, decimal delta = 30) =>
    IntradayEntryPolicy.Reaction(isLong, price, 100, stop, .25m, time, now.AddSeconds(30), held, delta, 100, 2m);
Check(React(true, 100.5m, 98, now, now.AddSeconds(-3)) is null, "long real-time break hold and tape confirmation");
Check(React(false, 99.5m, 102, now, now.AddSeconds(-3), -30) is null, "short symmetric confirmation");
Check(React(true, 100.5m, 98, now, now.AddSeconds(-1)) == "WaitingHold", "single tick is not sustained reaction");
Check(React(true, 103, 98, now, now.AddSeconds(-3)) == "ChaseTooFar", "late chase rejected");
Check(React(true, 97.5m, 98, now, now.AddSeconds(-3)) == "Invalidated", "stop invalidation takes precedence");
Check(React(true, 100.5m, 98, now.AddSeconds(31), now) == "Expired", "expiry invalidates candidate");
Check(React(true, 100.5m, 98, now, now.AddSeconds(-3), -20) == "WaitingFlow", "opposite tape cannot confirm long");
Check(IntradayEntryPolicy.IcebergArbitration(0, false, true, true, true, true, 30) == "Enter", "iceberg flat clean admission");
Check(IntradayEntryPolicy.IcebergArbitration(1, false, true, true, false, true, 300) == "Enter", "independent same-side slot at risk boundary");
Check(IntradayEntryPolicy.IcebergArbitration(1, false, true, true, false, true, 301) == "RiskCap", "combined risk above cap blocked");
Check(IntradayEntryPolicy.IcebergArbitration(1, false, false, true, false, true, 30) == "FlattenThenWait", "opposite iceberg waits for confirmed flatten");
Check(IntradayEntryPolicy.IcebergArbitration(0, false, true, true, true, false, 30) == "WaitCleanup", "flat with old protection cannot re-enter");
Check(IntradayEntryPolicy.IcebergArbitration(1, false, true, false, false, true, 30) == "WaitProtection", "naked primary cannot add slot");
Check(IntradayEntryPolicy.IcebergArbitration(1, true, true, true, false, true, 30) == "Occupied", "only one iceberg slot");
Check(IntradayEntryPolicy.IcebergArbitration(2, true, true, true, false, true, 30) == "Occupied", "two-contract maximum");
var item = new NativeIcebergEvent("NQZ6", 12, now, 100, true, 40, 60, false, now.AddSeconds(1));
var early = new DynamicExitComparison();
early.Observe(true, 108m, 100m, 10m, 90m, 115m, 100.75m, true);
Check(early.ExitPoints is null, "shadow stop cannot fill retroactively");
early.Observe(true, 100.5m, 100m, 10m, 90m, 115m, 100.75m, true);
Check(early.ExitPoints == .5m, "shadow stop uses next observed price including gaps");
var locked = new DynamicExitComparison();
locked.Observe(false, 87m, 100m, 10m, 110m, 85m, 99.25m, false);
locked.Observe(false, 96m, 100m, 10m, 110m, 85m, 99.25m, false);
Check(locked.ExitPoints == 4m, "short shadow profit lock follows observation order");
Check(ExecutionIdentity.Oco("instance", "NQZ6", "same") != ExecutionIdentity.Oco("instance", "MNQZ6", "same"), "cross-product OCO isolation");
Check(ExecutionIdentity.Oco("one", "NQZ6", "same") != ExecutionIdentity.Oco("two", "NQZ6", "same"), "cross-instance OCO isolation");
var breakoutEvent = new EsBreakoutEvent(now, false, 90m, 120m, 100m);
Check(breakoutEvent.Active(now.AddMinutes(6), 95m), "event persists across five minute boundary");
Check(!breakoutEvent.Active(now.AddMinutes(16), 95m) && !breakoutEvent.Active(now, 121m), "event expires or invalidates");
Check(breakoutEvent.NeedsRetest(90m) && !breakoutEvent.RetestReady(90m, .25m), "wide stop must wait for retest");
Check(breakoutEvent.RetestReady(99m, .25m), "short retest reduces structural risk without tightening stop");
var stoppedBreakout = breakoutEvent with { StoppedAt = now.AddMinutes(2) };
Check(stoppedBreakout.NeedsFreshReference(now.AddMinutes(2)) && !stoppedBreakout.NeedsFreshReference(now.AddMinutes(7)), "stopped breakout waits for a strictly newer ES five-minute reference");
Check(BreakEvenPolicy.ProfitLockStop(100m, true, 1.2m, 2m, .25m) == 101m, "long profit lock covers fees plus one tick");
Check(BreakEvenPolicy.ProfitLockStop(100m, false, 1.2m, 2m, .25m) == 99m, "short profit lock covers fees plus one tick");
var longEvent = new EsBreakoutEvent(now, true, 110m, 80m, 100m);
Check(longEvent.RetestReady(101m, .25m) && !longEvent.RetestReady(110m, .25m), "long retest symmetric");
Check(IntradayEntryPolicy.BreakoutChase(false, 29880.5m, 29908m, 29891m, 17.25m) == "CrossMarketChaseTooFar", "paired NQ late short rejected");
Check(IntradayEntryPolicy.BreakoutChase(true, 110.5m, 83m, 100m, 17.25m) == "CrossMarketChaseTooFar", "long late chase symmetric");
Check(IntradayEntryPolicy.BreakoutChase(false, 100m, 125m, 100m, 17.25m) == "CrossMarketRiskExpanded", "risk expansion without price chase rejected");
Check(IntradayEntryPolicy.BreakoutChase(false, 99m, 117m, 100m, 17.25m) is null, "near-anchor entry remains eligible");
Check(IntradayEntryPolicy.BreakoutChase(false, 100m, 117m, 100m, 17.25m) is null, "retest near original anchor remains eligible");
Check(IntradayEntryPolicy.EvidenceSize(false, false, true, 3m) == (1, 1m), "one independent evidence axis uses base size");
Check(IntradayEntryPolicy.EvidenceSize(true, true, false, 3m) == (2, 2m), "two orthogonal evidence axes increase size");
Check(IntradayEntryPolicy.EvidenceSize(true, true, true, 3m) == (3, 3m), "three orthogonal evidence axes reach three-contract tier");
Check(IntradayEntryPolicy.EvidenceSize(true, true, true, 1m) == (3, 1m), "configured per-product cap bounds evidence tier");
var eventKey = "acct|NQZ6|False|ES_EVENT_KEY:Long|touch|Put";
Check(IntradayEntryPolicy.ProductQuantityLimit("NQZ6") == 1m, "NQ contract suffix cap is one");
Check(IntradayEntryPolicy.ProductQuantityLimit("mnqz6") == 2m, "MNQ cap is two with case insensitive suffix");
Check(IntradayEntryPolicy.EvidenceSize(true, true, true, IntradayEntryPolicy.ProductQuantityLimit("NQZ6")) == (3, 1m), "NQ strongest evidence cannot exceed one");
Check(IntradayEntryPolicy.EvidenceSize(true, true, true, IntradayEntryPolicy.ProductQuantityLimit("MNQZ6")) == (3, 2m), "MNQ strongest evidence cannot exceed two");
Check(IntradayEntryPolicy.AvailableEntryQuantity("NQZ6", 3m, 0m) == 1m, "final NQ submission clamps to one");
Check(IntradayEntryPolicy.AvailableEntryQuantity("MNQZ6", 3m, 0m) == 2m, "final MNQ submission clamps to two");
Check(IntradayEntryPolicy.AvailableEntryQuantity("NQZ6", 1m, 1m) == 0m, "NQ second slot cannot exceed held cap");
Check(IntradayEntryPolicy.AvailableEntryQuantity("MNQZ6", 2m, 1m) == 1m, "MNQ second slot uses remaining single contract");
Check(IntradayEntryPolicy.AvailableEntryQuantity("MNQZ6", 2m, 3m) == 0m, "legacy over-cap position cannot add");
Check(PhysicalEventExecutionDedup.TryClaim(eventKey, now), "first strategy claims physical event");
Check(!PhysicalEventExecutionDedup.TryClaim(eventKey, now.AddSeconds(1)), "same account and event cannot submit twice");
PhysicalEventExecutionDedup.Release(eventKey);
Check(PhysicalEventExecutionDedup.TryClaim(eventKey, now.AddSeconds(2)), "aborted preflight releases event claim for later attempt");
Check(PhysicalEventExecutionDedup.TryClaim("acct|NQZ6|True|ES_EVENT_KEY:Long|touch|Put", now), "Replay and live events are isolated");
Check(PhysicalEventExecutionDedup.TryClaim("other|NQZ6|False|ES_EVENT_KEY:Long|touch|Put", now), "other accounts are isolated");
Check(!NativeIcebergArchive.AvailableAsOf(item, now), "replay excludes event not captured yet");
Check(NativeIcebergArchive.AvailableAsOf(item, now.AddSeconds(1)), "replay exposes native event as of capture");
Check(!NativeIcebergArchive.AvailableAsOf(item, now.AddSeconds(31)), "old native event cannot trigger stale entry");
var directory = Path.Combine(Path.GetTempPath(), "opf-iceberg-test-" + Guid.NewGuid().ToString("N"));
try
{
    NativeIcebergArchive.Append(directory, item);
    NativeIcebergArchive.Append(directory, item with { Deleted = true, CapturedAtUtc = now.AddSeconds(3) });
    var restored = NativeIcebergArchive.Read(directory, now);
    Check(restored.Length == 2 && restored[1].Deleted, "native event update and exhaustion preserved on round trip");
}
finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
var row = Enumerable.Repeat("", 23).ToArray();
row[20] = "account_A"; row[21] = "MNQZ6"; row[22] = "False";
Check(AccountPnlScope.Matches(row, "account_A", "MNQZ6", false), "same account contract and mode restores PnL");
Check(!AccountPnlScope.Matches(row, "account_B", "MNQZ6", false), "other account PnL excluded");
Check(!AccountPnlScope.Matches(row, "account_A", "NQZ6", false), "NQ cannot import MNQ PnL");
Check(!AccountPnlScope.Matches(row, "account_A", "MNQZ6", true), "Replay cannot import live PnL");
Check(!AccountPnlScope.Matches(row.Take(20).ToArray(), "account_A", "MNQZ6", false), "legacy unscoped rows not silently summed");
Check(!AccountPnlScope.Matches(row, "unresolved", "MNQZ6", false), "unknown account not treated as shared scope");


var postFill = new PostFillPriceObservation();
var fillAt = now.AddMinutes(1);
postFill.Start(100m, fillAt);
postFill.ObserveRange(80m, 120m, fillAt);
Check(postFill.Low == 100m && postFill.High == 100m, "entry candle extremes are excluded from protection observation");
postFill.ObservePrice(101m, fillAt.AddSeconds(1));
Check(postFill.High == 101m && postFill.Low == 100m, "post-fill tick updates protection observation");
postFill.ObservePrice(99m, fillAt);
Check(postFill.Low == 100m, "out-of-order tick cannot contaminate protection observation");
postFill.ObserveRange(98m, 103m, fillAt.AddMinutes(1));
Check(postFill.Low == 98m && postFill.High == 103m, "fully post-fill bar updates protection observation");

var counterLong = new IcebergStructureConfirmation(true, 100m, true);
Check(IcebergStructureConfirmation.CounterTrend(true, 105m, 99m, 20m, 0.25m), "declining context marks long counter-trend");
Check(counterLong.Observe(100m, 0.25m, now) == "WAIT_STRUCTURE_RETEST", "long waits for structure break");
Check(counterLong.Observe(100.25m, 0.25m, now.AddSeconds(1)) == "WAIT_STRUCTURE_RETEST", "long break arms retest");
Check(counterLong.Observe(100m, 0.25m, now.AddSeconds(2)) == "WAIT_STRUCTURE_RECLAIM", "long retest alone is insufficient");
Check(counterLong.Observe(100.25m, 0.25m, now.AddSeconds(3)) == "WAIT_STRUCTURE_RECLAIM", "long reclaim must persist");
Check(counterLong.Observe(100.25m, 0.25m, now.AddSeconds(4)) == "STRUCTURE_CONFIRMED", "long break retest reclaim confirms");
var counterShort = new IcebergStructureConfirmation(false, 100m, true);
Check(counterShort.Observe(100m, 0.25m, now) == "WAIT_STRUCTURE_RETEST", "short waits for structure break");
Check(counterShort.Observe(99.75m, 0.25m, now.AddSeconds(1)) == "WAIT_STRUCTURE_RETEST", "short break arms retest");
Check(counterShort.Observe(100m, 0.25m, now.AddSeconds(2)) == "WAIT_STRUCTURE_RECLAIM", "short retest alone is insufficient");
Check(counterShort.Observe(99.75m, 0.25m, now.AddSeconds(3)) == "WAIT_STRUCTURE_RECLAIM", "short reclaim must persist");
Check(counterShort.Observe(99.75m, 0.25m, now.AddSeconds(4)) == "STRUCTURE_CONFIRMED", "short break retest reclaim confirms");
var trend = new IcebergStructureConfirmation(true, 100m, false);
Check(IcebergStructureConfirmation.Ready(trend.Observe(99m, 0.25m, now)), "aligned trend response keeps prior entry flow");

Check(postFill.High == 103m && postFill.High < 28411.75m, "observed 28409 MFE cannot reach 0.75R trigger at 28411.75");
var shortFill = new PostFillPriceObservation();
shortFill.Start(100m, fillAt);
shortFill.ObserveRange(80m, 120m, fillAt);
shortFill.ObservePrice(99m, fillAt.AddSeconds(1));
Check(shortFill.Low == 99m && shortFill.High == 100m, "short entry-bar pre-fill low cannot trigger protective movement");
