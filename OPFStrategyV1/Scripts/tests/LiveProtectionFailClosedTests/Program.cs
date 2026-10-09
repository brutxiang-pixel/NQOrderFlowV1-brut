using OPFStrategyV1.Strategy;
using OPFStrategyV1.Core.Configuration;
using OPFStrategyV1.Core.Profiles;
using OPFStrategyV1.Core.Snapshots;
using OPFStrategyV1.Core.Signals;
using OPFStrategyV1.Core.MarketData;
using ATAS.DataFeedsCore;

var cases = new[]
{
    ("confirmed-fill-without-stop", 1m, 0m, false, false, false, true),
    ("working-stop", 1m, 0m, true, false, false, false),
    ("completed-exit", 1m, 0m, false, true, false, false),
    ("flatten-already-submitted", 1m, 0m, false, false, true, false),
    ("no-confirmed-fill", 0m, 0m, false, false, false, false)
};

foreach (var testCase in cases)
{
    var actual = LiveProtectionFailClosedGuard.RequiresEmergencyFlatten(
        testCase.Item2,
        testCase.Item3,
        testCase.Item4,
        testCase.Item5,
        testCase.Item6);
    if (actual != testCase.Item7)
        throw new InvalidOperationException($"Assertion failed [{testCase.Item1}]: expected={testCase.Item7} actual={actual}");
}

Console.WriteLine($"PASS LiveProtectionFailClosedGuard cases={cases.Length}");

var connectorlessCases = new[]
{
    ("flat-bound-context", false, true, true, 0m, false, true),
    ("connector-present", true, true, true, 0m, false, false),
    ("portfolio-missing", false, false, true, 0m, false, false),
    ("security-missing", false, true, false, 0m, false, false),
    ("non-flat-account", false, true, true, 1m, false, false),
    ("active-execution", false, true, true, 0m, true, false)
};

foreach (var testCase in connectorlessCases)
{
    var actual = LiveConnectorlessContextGate.CanUseContextFallback(
        testCase.Item2,
        testCase.Item3,
        testCase.Item4,
        testCase.Item5,
        testCase.Item6);
    if (actual != testCase.Item7)
        throw new InvalidOperationException($"Assertion failed [{testCase.Item1}]: expected={testCase.Item7} actual={actual}");
}

Console.WriteLine($"PASS LiveConnectorlessContextGate cases={connectorlessCases.Length}");

var restartedState = LiveExecutionLifecycleState.CreateForStart(
    wasStopping: true,
    protectionTimeoutLatched: true,
    connectorlessContextFallbackActive: true,
    latencyGateActive: true,
    orphanPositionFlattenPending: true);

if (restartedState.IsStoppingActualExecution ||
    restartedState.ProtectionTimeoutLatched ||
    restartedState.ConnectorlessContextFallbackActive ||
    restartedState.LatencyGateActive ||
    restartedState.OrphanPositionFlattenPending)
{
    throw new InvalidOperationException("Assertion failed [restart-clears-transient-live-execution-state]: expected all false");
}

Console.WriteLine("PASS LiveExecutionLifecycleState cases=1");

var bracketCases = new[]
{
    ("confirmed-stop-and-target", 1m, 0m, true, true, false, false),
    ("missing-target", 1m, 0m, true, false, false, true),
    ("missing-stop", 1m, 0m, false, true, false, true),
    ("already-exited", 1m, 1m, false, false, true, false)
};

foreach (var testCase in bracketCases)
{
    var actual = LiveBracketConfirmationGate.RequiresEmergencyFlatten(
        testCase.Item2,
        testCase.Item3,
        testCase.Item4,
        testCase.Item5,
        testCase.Item6);
    if (actual != testCase.Item7)
        throw new InvalidOperationException($"Assertion failed [{testCase.Item1}]: expected={testCase.Item7} actual={actual}");
}

Console.WriteLine($"PASS LiveBracketConfirmationGate cases={bracketCases.Length}");

var feishuMessage = FeishuTradeNotification.FormatConfirmedEntry(
    "20260817-0250-Long-886",
    "Long",
    30224.75m,
    30218.50m,
    30234.25m,
    "ObservationConfirm");
foreach (var expected in new[] { "20260817-0250-Long-886", "Long", "30224.75", "30218.50", "30234.25", "ObservationConfirm" })
{
    if (!feishuMessage.Contains(expected, StringComparison.Ordinal))
        throw new InvalidOperationException($"Assertion failed [feishu-confirmed-entry-format]: missing={expected}");
}

Console.WriteLine("PASS FeishuTradeNotification formatting");

var feishuExitMessage = FeishuTradeNotification.FormatCompletedExit(
    "20260817-0650-Long-934",
    "Long",
    30316.50m,
    30326.50m,
    "TP",
    10m,
    20m,
    "ObservationConfirm");
foreach (var expected in new[] { "【平仓】", "20260817-0650-Long-934", "30316.50", "30326.50", "TP", "+10.00", "+20.00", "ObservationConfirm" })
{
    if (!feishuExitMessage.Contains(expected, StringComparison.Ordinal))
        throw new InvalidOperationException($"Assertion failed [feishu-completed-exit-format]: missing={expected}");
}

Console.WriteLine("PASS FeishuTradeNotification exit formatting");

var snapshot = ConfigSnapshot.Create(
    ProfileCatalog.Select("MNQ_0.1", "MNQ_1Contract_Target150_200"),
    "local.json",
    "loaded",
    ActualExecutionSettings.Default() with
    {
        FeishuNotificationEnabled = true,
        FeishuWebhookUrl = "https://open.feishu.cn/open-apis/bot/v2/hook/test-secret"
    });
if (snapshot.ActualExecutionSettings.FeishuWebhookUrl != "REDACTED")
    throw new InvalidOperationException("Assertion failed [feishu-webhook-redacted-in-snapshot]");

Console.WriteLine("PASS Feishu webhook snapshot redaction");

var nativeParent = new Order();
var nativeStop = new Order();
var nativeTarget = new Order();
NativeAttachedBracket.Configure(nativeParent, nativeStop, nativeTarget, "OPF-native-test");
if (!ReferenceEquals(nativeStop.Parent, nativeParent) ||
    !ReferenceEquals(nativeTarget.Parent, nativeParent) ||
    nativeStop.IsAttached != true ||
    nativeTarget.IsAttached != true ||
    nativeStop.OCOGroup != "OPF-native-test" ||
    nativeTarget.OCOGroup != "OPF-native-test")
{
    throw new InvalidOperationException("Assertion failed [native-attached-bracket-links-both-legs-to-entry]");
}

Console.WriteLine("PASS NativeAttachedBracket linkage");

var alignedStop = NativeAttachedBracket.AlignPriceToTick(30309.50m, 0.25m);
var alignedTarget = NativeAttachedBracket.AlignPriceToTick(30326.375m, 0.25m);
if (alignedStop != 30309.50m || alignedTarget != 30326.50m ||
    !NativeAttachedBracket.IsTickAligned(alignedStop, 0.25m) ||
    !NativeAttachedBracket.IsTickAligned(alignedTarget, 0.25m))
{
    throw new InvalidOperationException($"Assertion failed [native-attached-bracket-aligns-all-prices]: stop={alignedStop} target={alignedTarget}");
}

Console.WriteLine("PASS NativeAttachedBracket tick alignment");

var probeCases = new[]
{
    ("enabled-first-live-exam-run", true, false, true, false, true, true, true, true),
    ("already-claimed", true, true, true, false, true, true, true, false),
    ("manual-alert-mode", true, false, false, true, true, true, true, false),
    ("historical-replay", true, false, true, false, false, true, true, false),
    ("wrong-instrument", true, false, true, false, true, true, false, false)
};

foreach (var testCase in probeCases)
{
    var actual = NativeBracketProbeGate.CanDispatch(
        testCase.Item2,
        testCase.Item3,
        testCase.Item4,
        testCase.Item5,
        testCase.Item6,
        testCase.Item7,
        testCase.Item8);
    if (actual != testCase.Item9)
        throw new InvalidOperationException($"Assertion failed [native-bracket-probe-{testCase.Item1}]: expected={testCase.Item9} actual={actual}");
}

Console.WriteLine($"PASS NativeBracketProbeGate cases={probeCases.Length}");

if (!NativeBracketProbeGate.ShouldPersistDisabledAfterClaim(true) ||
    NativeBracketProbeGate.ShouldPersistDisabledAfterClaim(false))
{
    throw new InvalidOperationException("Assertion failed [native-bracket-probe-disables-persisted-arm-after-claim]");
}

Console.WriteLine("PASS NativeBracketProbeGate persistent one-shot");

var abnormalHudText = ExecutionHudTextFormatter.Format(
    TradeSide.Short,
    "ABNORMAL_SL",
    -2.91m,
    -48m,
    "ObservationConfirm");
if (abnormalHudText != "S ABNORMAL_SL -2.91R $-48.00 ObservationConfirm")
    throw new InvalidOperationException($"Assertion failed [abnormal-protective-fill-is-visible-in-hud]: actual={abnormalHudText}");

Console.WriteLine("PASS ExecutionHudTextFormatter abnormal exit formatting");

var globexCloseoutCases = new[]
{
    ("weekday-before-rms-lock", DayOfWeek.Monday, new TimeSpan(16, 39, 59), false),
    ("weekday-rms-lock-start", DayOfWeek.Monday, new TimeSpan(16, 40, 0), true),
    ("weekday-reopen", DayOfWeek.Monday, new TimeSpan(18, 0, 0), false),
    ("friday-rms-lock-start", DayOfWeek.Friday, new TimeSpan(16, 40, 0), true)
};

foreach (var testCase in globexCloseoutCases)
{
    var actual = LiveGlobexCloseoutGate.IsLocked(testCase.Item2, testCase.Item3);
    if (actual != testCase.Item4)
        throw new InvalidOperationException($"Assertion failed [globex-closeout-{testCase.Item1}]: expected={testCase.Item4} actual={actual}");
}

Console.WriteLine($"PASS LiveGlobexCloseoutGate cases={globexCloseoutCases.Length}");

var globexFlattenCases = new[]
{
    ("weekday-before-flatten", DayOfWeek.Monday, new TimeSpan(16, 34, 59), false),
    ("weekday-flatten-start", DayOfWeek.Monday, new TimeSpan(16, 35, 0), true),
    ("weekday-lock-still-flatten-window", DayOfWeek.Monday, new TimeSpan(16, 40, 0), true),
    ("weekday-reopen", DayOfWeek.Monday, new TimeSpan(18, 0, 0), false),
    ("friday-flatten-start", DayOfWeek.Friday, new TimeSpan(16, 35, 0), true)
};

foreach (var testCase in globexFlattenCases)
{
    var actual = LiveGlobexCloseoutGate.ShouldAttemptFlatten(testCase.Item2, testCase.Item3);
    if (actual != testCase.Item4)
        throw new InvalidOperationException($"Assertion failed [globex-flatten-{testCase.Item1}]: expected={testCase.Item4} actual={actual}");
}

Console.WriteLine($"PASS LiveGlobexCloseoutGate flatten cases={globexFlattenCases.Length}");

var accountScope = LiveAccountScope.FromIdentity("  Evaluation Account #1 / MNQ  ");
if (accountScope != "Evaluation_Account_1_MNQ")
    throw new InvalidOperationException($"Assertion failed [live-account-scope-normalization]: actual={accountScope}");

Console.WriteLine("PASS LiveAccountScope normalization");

var scopedSnapshotA = ConfigSnapshot.Create(ProfileCatalog.Select("MNQ_0.1", "MNQ_1Contract_Target150_200"), "config", "loaded", ActualExecutionSettings.Default(), "account_A");
var scopedSnapshotB = ConfigSnapshot.Create(ProfileCatalog.Select("MNQ_0.1", "MNQ_1Contract_Target150_200"), "config", "loaded", ActualExecutionSettings.Default(), "account_B");
if (scopedSnapshotA.SnapshotId == scopedSnapshotB.SnapshotId ||
    !scopedSnapshotA.SnapshotId.EndsWith("-account_A", StringComparison.Ordinal) ||
    !scopedSnapshotB.SnapshotId.EndsWith("-account_B", StringComparison.Ordinal))
{
    throw new InvalidOperationException($"Assertion failed [account-scoped-snapshots]: first={scopedSnapshotA.SnapshotId} second={scopedSnapshotB.SnapshotId}");
}

Console.WriteLine("PASS account-scoped snapshots");

if (!NativeAttachedProtectionGate.ShouldFinalizeAfterEntryFill(true) ||
    NativeAttachedProtectionGate.ShouldFinalizeAfterEntryFill(false) ||
    !NativeAttachedProtectionGate.ShouldDeferLossCheck(true, true) ||
    NativeAttachedProtectionGate.ShouldDeferLossCheck(true, false) ||
    NativeAttachedProtectionGate.ShouldDeferLossCheck(false, true))
{
    throw new InvalidOperationException("Assertion failed [native-attached-protection-race-guards]");
}

Console.WriteLine("PASS native attached protection race guards");

var projectDirectory = new DirectoryInfo(AppContext.BaseDirectory);
while (projectDirectory is not null && !File.Exists(Path.Combine(projectDirectory.FullName, "OPFStrategyV1.csproj")))
    projectDirectory = projectDirectory.Parent;
if (projectDirectory is null)
    throw new InvalidOperationException("Assertion failed [significant-zone-history-startup-order]: project directory was not found");

var strategySource = File.ReadAllText(Path.Combine(projectDirectory.FullName, "Strategy", "OpeningPullbackFailureStrategy.cs"));
if (strategySource.Contains("GlobexCloseout:SL", StringComparison.Ordinal) ||
    strategySource.Contains("GlobexCloseout:TP", StringComparison.Ordinal) ||
    !strategySource.Contains("GLOBEX_CLOSEOUT_FLATTEN_REJECTED_PROTECTION_RETAINED", StringComparison.Ordinal))
{
    throw new InvalidOperationException("Assertion failed [globex-closeout-retains-protection-on-flatten-rejection]");
}

Console.WriteLine("PASS Globex closeout retains protection on flatten rejection");

var snapshotCreation = strategySource.IndexOf("_snapshot = ConfigSnapshot.Create(", StringComparison.Ordinal);
var historicalRestore = strategySource.IndexOf("RestoreHistoricalSignificantZones();", StringComparison.Ordinal);
if (snapshotCreation < 0 || historicalRestore < 0 || historicalRestore < snapshotCreation)
    throw new InvalidOperationException("Assertion failed [significant-zone-history-restores-after-snapshot]: history restore must run after snapshot creation");

Console.WriteLine("PASS significant zone history startup order");

var historicalZones = new List<SignificantZoneHistoricalReference>
{
    new("MNQ", "SZ_RANK_1", "below", TradeSide.Long, 29502m, 29494m, DateTime.UtcNow, 1, SignificantZoneState.Active, 0, SignificantZoneGrade.A, 80m, "test", DateTime.UtcNow),
    new("MNQ", "SZ_RANK_1", "crosses", TradeSide.Short, 29549m, 29560m, DateTime.UtcNow, 2, SignificantZoneState.Active, 0, SignificantZoneGrade.A, 90m, "test", DateTime.UtcNow),
    new("MNQ", "SZ_RANK_1", "above", TradeSide.Short, 29580m, 29592m, DateTime.UtcNow, 3, SignificantZoneState.Active, 0, SignificantZoneGrade.A, 70m, "test", DateTime.UtcNow)
};
var belowZoneIds = HistoricalSignificantZoneSideSelector.Below(historicalZones, 29551m).Select(x => x.ZoneId).ToArray();
var aboveZoneIds = HistoricalSignificantZoneSideSelector.Above(historicalZones, 29551m).Select(x => x.ZoneId).ToArray();
if (!belowZoneIds.SequenceEqual(new[] { "below" }) || !aboveZoneIds.SequenceEqual(new[] { "above" }))
    throw new InvalidOperationException($"Assertion failed [historical-zone-selector-keeps-one-above-and-below]: below={string.Join(',', belowZoneIds)} above={string.Join(',', aboveZoneIds)}");

Console.WriteLine("PASS historical significant zone side selection");

var gexPath = Path.Combine(Path.GetTempPath(), $"opf-gex-{Guid.NewGuid():N}.json");
try
{
    File.WriteAllText(gexPath, "{\"updatedAt\":\"2026-09-08T09:00:00+08:00\",\"dataDate\":\"2026-09-08\",\"symbols\":{\"NQ\":{\"dataAvailableTime\":\"2026-09-08T08:30:00-04:00\",\"levels\":[{\"levelType\":\"call_wall\",\"price\":29273.0,\"label\":\"Call Wall 29273\",\"oi\":100,\"side\":\"call\"},{\"levelType\":\"zero_gex\",\"price\":28809.0,\"label\":\"Zero Gamma 28809\",\"oi\":0,\"side\":\"neutral\"}]}}}");
    var gex = GexDailySnapshotLoader.Load(gexPath, 0.25m, new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero));
    if (gex.Status != "Ready" || gex.Levels.Count != 2 || gex.Levels[0].Price != 29273m || string.IsNullOrWhiteSpace(gex.Sha256))
        throw new InvalidOperationException($"Assertion failed [gex-loader-valid-snapshot]: status={gex.Status} levels={gex.Levels.Count}");

    File.WriteAllText(gexPath, "{\"symbols\":{\"NQ\":{\"levels\":[{\"levelType\":\"call_wall\",\"price\":29273.1,\"label\":\"invalid tick\"}]}}}");
    var invalidGex = GexDailySnapshotLoader.Load(gexPath, 0.25m, DateTimeOffset.UtcNow);
    if (invalidGex.Status != "Unavailable" || invalidGex.Levels.Count != 0)
        throw new InvalidOperationException("Assertion failed [gex-loader-rejects-invalid-price]");
}
finally
{
    if (File.Exists(gexPath)) File.Delete(gexPath);
}

Console.WriteLine("PASS GEX snapshot loader validation");

var shadowZone = new SignificantZone(
    "shadow-support",
    TradeSide.Long,
    101m,
    100m,
    DateTime.UtcNow,
    1,
    SignificantZoneState.Active,
    0,
    new SignificantZoneEvidence(true, true, "test", "test", "ImpulseLiquidityEvidence"));
var shadow = SignificantZoneShadow.Create("ImpulseLiquidity", 1);
shadow = SignificantZoneShadowEvaluator.Update(shadowZone, shadow, new OpfCandle(2, DateTime.UtcNow, 102m, 102m, 100.5m, 101.5m, 1m, 0m));
if (shadow.State != SignificantZoneShadowState.Defending || SignificantZoneShadowEvaluator.ExpectedSide(shadowZone, shadow) != TradeSide.Long)
    throw new InvalidOperationException("Assertion failed [shadow-zone-first-touch-defending]");

shadow = SignificantZoneShadowEvaluator.Update(shadowZone, shadow, new OpfCandle(3, DateTime.UtcNow, 99.5m, 100m, 98m, 99m, 1m, 0m));
if (shadow.State != SignificantZoneShadowState.Breached)
    throw new InvalidOperationException("Assertion failed [shadow-zone-first-breach]");

shadow = SignificantZoneShadowEvaluator.Update(shadowZone, shadow, new OpfCandle(4, DateTime.UtcNow, 99m, 99.5m, 97m, 98.5m, 1m, 0m));
if (shadow.State != SignificantZoneShadowState.Accepted || SignificantZoneShadowEvaluator.ExpectedSide(shadowZone, shadow) != TradeSide.Short)
    throw new InvalidOperationException("Assertion failed [shadow-zone-two-close-acceptance]");

shadow = SignificantZoneShadowEvaluator.Update(shadowZone, shadow, new OpfCandle(5, DateTime.UtcNow, 99.5m, 101m, 99m, 100.5m, 1m, 0m));
if (shadow.State != SignificantZoneShadowState.Reclaimed || SignificantZoneShadowEvaluator.ExpectedSide(shadowZone, shadow) != TradeSide.Long)
    throw new InvalidOperationException("Assertion failed [shadow-zone-reclaim]");

Console.WriteLine("PASS significant zone shadow lifecycle");

var scheduleCases = new (string Name, DateTime EasternTime, string? Expected)[]
{
    ("saturday-is-never-a-slot", new DateTime(2026, 9, 19, 9, 30, 0), null),
    ("sunday-is-never-a-slot", new DateTime(2026, 9, 20, 9, 0, 0), null),
    ("pre-market-slot", new DateTime(2026, 9, 17, 9, 0, 0), "2026-09-17|PreMarket"),
    ("just-before-pre-market-is-not-a-slot", new DateTime(2026, 9, 17, 8, 59, 0), null),
    ("intraday-open-slot", new DateTime(2026, 9, 17, 9, 30, 0), "2026-09-17|Intraday|09:30"),
    ("intraday-off-grid-minute-is-not-a-slot", new DateTime(2026, 9, 17, 9, 31, 0), null),
    ("intraday-close-slot-is-inclusive", new DateTime(2026, 9, 17, 16, 0, 0), "2026-09-17|Intraday|16:00"),
    ("after-intraday-close-is-not-a-slot", new DateTime(2026, 9, 17, 16, 30, 0), null)
};

foreach (var testCase in scheduleCases)
{
    var actual = GexRefreshScheduleGate.ResolveSlotKey(testCase.EasternTime);
    if (actual != testCase.Expected)
        throw new InvalidOperationException($"Assertion failed [gex-schedule-{testCase.Name}]: expected={testCase.Expected ?? "null"} actual={actual ?? "null"}");
}

Console.WriteLine($"PASS GexRefreshScheduleGate cases={scheduleCases.Length}");

var keyLevelCrossCases = new (string Name, decimal PreviousClose, decimal CurrentClose, decimal LevelPrice, string? Expected)[]
{
    ("upward-cross-triggers", 100m, 102m, 101m, "KeyLevelCross:GEX-ZG"),
    ("downward-cross-triggers", 102m, 100m, 101m, "KeyLevelCross:GEX-ZG"),
    ("same-side-no-cross", 102m, 103m, 101m, null),
    ("landing-exactly-on-level-does-not-count-as-crossed-side", 100m, 101m, 101m, null)
};

foreach (var testCase in keyLevelCrossCases)
{
    var actual = GexRefreshTriggerGate.DetectKeyLevelCross(testCase.PreviousClose, testCase.CurrentClose, testCase.LevelPrice, "GEX-ZG");
    if (actual != testCase.Expected)
        throw new InvalidOperationException($"Assertion failed [gex-trigger-{testCase.Name}]: expected={testCase.Expected ?? "null"} actual={actual ?? "null"}");
}

Console.WriteLine($"PASS GexRefreshTriggerGate key level cross cases={keyLevelCrossCases.Length}");

if (GexRefreshTriggerGate.DetectVolatilitySpike(105m, 100m, 0m) is not null)
    throw new InvalidOperationException("Assertion failed [gex-trigger-volatility-spike-atr-not-established]");
if (GexRefreshTriggerGate.DetectVolatilitySpike(102m, 100m, 10m) is not null)
    throw new InvalidOperationException("Assertion failed [gex-trigger-volatility-spike-below-multiple]");
if (GexRefreshTriggerGate.DetectVolatilitySpike(130m, 100m, 10m) is null)
    throw new InvalidOperationException("Assertion failed [gex-trigger-volatility-spike-at-multiple]");

Console.WriteLine("PASS GexRefreshTriggerGate volatility spike detection");

var cooldownNow = DateTime.UtcNow;
if (GexRefreshTriggerGate.IsInCooldown(null, cooldownNow))
    throw new InvalidOperationException("Assertion failed [gex-trigger-cooldown-no-prior-request]");
if (!GexRefreshTriggerGate.IsInCooldown(cooldownNow.AddMinutes(-5), cooldownNow))
    throw new InvalidOperationException("Assertion failed [gex-trigger-cooldown-still-active]");
if (GexRefreshTriggerGate.IsInCooldown(cooldownNow.AddMinutes(-11), cooldownNow))
    throw new InvalidOperationException("Assertion failed [gex-trigger-cooldown-expired]");

Console.WriteLine("PASS GexRefreshTriggerGate cooldown");

var gexStalePath = Path.Combine(Path.GetTempPath(), $"opf-gex-stale-{Guid.NewGuid():N}.json");
var gexSyncStatePath = Path.Combine(Path.GetTempPath(), $"opf-gex-sync-state-{Guid.NewGuid():N}.json");
try
{
    var freshDataDate = DateTime.UtcNow.ToString("yyyy-MM-dd");
    File.WriteAllText(gexStalePath, $"{{\"updatedAt\":\"{DateTime.UtcNow:O}\",\"dataDate\":\"{freshDataDate}\",\"symbols\":{{\"NQ\":{{\"dataAvailableTime\":\"{DateTime.UtcNow:O}\",\"levels\":[{{\"levelType\":\"call_wall\",\"price\":29273.0,\"label\":\"Call Wall 29273\",\"oi\":100,\"side\":\"call\"}}]}}}}}}");

    File.WriteAllText(gexSyncStatePath, $"{{\"completedAt\":\"{DateTime.UtcNow.AddMinutes(-91):O}\"}}");
    var staleBySyncAge = GexDailySnapshotLoader.Load(gexStalePath, 0.25m, DateTimeOffset.UtcNow, gexSyncStatePath);
    if (staleBySyncAge.Status != "Stale" || staleBySyncAge.Detail != "syncOlderThan90Minutes")
        throw new InvalidOperationException($"Assertion failed [gex-loader-stale-after-90-minutes]: status={staleBySyncAge.Status} detail={staleBySyncAge.Detail}");

    File.WriteAllText(gexSyncStatePath, $"{{\"completedAt\":\"{DateTime.UtcNow.AddMinutes(-10):O}\"}}");
    var freshBySyncAge = GexDailySnapshotLoader.Load(gexStalePath, 0.25m, DateTimeOffset.UtcNow, gexSyncStatePath);
    if (freshBySyncAge.Status != "Ready")
        throw new InvalidOperationException($"Assertion failed [gex-loader-ready-within-90-minutes]: status={freshBySyncAge.Status} detail={freshBySyncAge.Detail}");

    var noSyncStateProvided = GexDailySnapshotLoader.Load(gexStalePath, 0.25m, DateTimeOffset.UtcNow, syncStatePath: null);
    if (noSyncStateProvided.Status != "Ready")
        throw new InvalidOperationException($"Assertion failed [gex-loader-missing-sync-state-does-not-force-stale]: status={noSyncStateProvided.Status} detail={noSyncStateProvided.Detail}");
}
finally
{
    if (File.Exists(gexStalePath)) File.Delete(gexStalePath);
    if (File.Exists(gexSyncStatePath)) File.Delete(gexSyncStatePath);
}

Console.WriteLine("PASS GexDailySnapshotLoader 90-minute sync staleness");
