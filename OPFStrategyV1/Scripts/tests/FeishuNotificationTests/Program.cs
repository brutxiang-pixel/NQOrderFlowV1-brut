using OPFStrategyV1.Strategy;
using System.Text.Json;

async Task VerifyRetry(FeishuSendResult[] responses, int expectedCalls, bool expectedSuccess)
{
    var calls = 0; var pauses = new List<TimeSpan>();
    var result = await FeishuNotificationSender.RetryRateLimitAsync(
        () => Task.FromResult(responses[Math.Min(calls++, responses.Length - 1)]),
        pause => { pauses.Add(pause); return Task.CompletedTask; }, (_, _) => { });
    if (calls != expectedCalls || result.Success != expectedSuccess || pauses.Count != expectedCalls - 1)
        throw new Exception("Incorrect bounded Feishu rate limit retry.");
}
await VerifyRetry(new[] { new FeishuSendResult(200,11232), new FeishuSendResult(200,0) }, 2, true);
await VerifyRetry(new[] { new FeishuSendResult(429,null), new FeishuSendResult(200,0) }, 2, true);
await VerifyRetry(new[] { new FeishuSendResult(200,11232) }, 4, false);
await VerifyRetry(new[] { new FeishuSendResult(200,19024) }, 1, false);
await VerifyRetry(new[] { new FeishuSendResult(200,null) }, 1, false);
await VerifyRetry(new[] { new FeishuSendResult(200,0) }, 1, true);
Console.WriteLine("PASS Feishu 11232/429 retry, bounded exhaustion, permanent rejection, unknown response and success stop (offline)");

var fills = new ExecutionFillDeduplicator();
if (!fills.TryAccept("fill-1", "ENTRY") || fills.TryAccept("fill-1", "ENTRY") ||
    !fills.TryAccept("fill-2", "ENTRY") || !fills.TryAccept("fill-1", "SL") ||
    !fills.TryAccept(null, "ENTRY") || !fills.TryAccept(null, "ENTRY"))
    throw new Exception("Duplicate fill IDs must not merge distinct partial fills.");
Console.WriteLine("PASS fill ID deduplication, partial fills and missing ID fallback");

var utc = new DateTimeOffset(2026, 10, 13, 1, 20, 30, TimeSpan.Zero);
if (BreakEvenPolicy.Stop(30763.75m, false, 1.2m, 2m, .25m) != 30763m ||
    BreakEvenPolicy.Stop(30763.75m, true, 1.2m, 2m, .25m) != 30764.5m ||
    BreakEvenPolicy.Stop(100m, false, 1.2m, 20m, .25m) != 99.75m ||
    BreakEvenPolicy.Stop(100m, true, 0m, 2m, .25m) != 100m)
    throw new Exception("Fee-covered break-even tick rounding failed.");
var beExit = FeishuTradeNotification.FormatCompletedExit("be", "MNQ", "Short", 30763.75m, 30763m, "SL", .75m, 1.5m, "ESWallRejectionReversal", utc, 1.2m, true);
if (!beExit.Contains("保本止损") || !beExit.Contains("$+0.30") || !beExit.Contains("$1.20"))
    throw new Exception("Break-even exit must show fees and net result.");
Console.WriteLine("PASS fee-covered long/short stops, MNQ/NQ tick rounding and break-even net notification");
var entry = FeishuTradeNotification.FormatConfirmedEntry("trade-1", "MNQ", "Long", 100m, 95m, 107.5m, "ObservationConfirm", utc);
var exit = FeishuTradeNotification.FormatCompletedExit("trade-1", "MNQ", "Long", 100m, 95m, "SL", -5m, -10m, "ObservationConfirm", utc.AddMinutes(5));
if (!entry.Contains("trade-1/OPEN") || !exit.Contains("trade-1/CLOSE") ||
    !entry.Contains("MNQ") || !exit.Contains("MNQ") ||
    !entry.Contains("2026-10-13 09:20:30") || !exit.Contains("2026-10-13 09:25:30"))
    throw new Exception("Trade markers or Beijing timestamps are incorrect.");
if (!entry.Contains("【开仓】") || !exit.Contains("【平仓】") ||
    !entry.Contains("trade-1") || !exit.Contains("trade-1") || !exit.Contains("-10.00"))
    throw new Exception("Trade notifications must identify the same trade and its result.");
using var payload = JsonDocument.Parse(FeishuTradeNotification.CreatePayload(entry));
if (payload.RootElement.GetProperty("content").GetProperty("text").GetString() != entry)
    throw new Exception("Payload text was altered.");
foreach (var test in new (string Body, int? Code)[]
{
    ("{\"code\":0}", 0), ("{\"StatusCode\":0}", 0),
    ("{\"code\":19024}", 19024), ("{}", null),
    ("not-json", null), ("[]", null), ("{\"code\":\"bad\"}", null)
})
    if (FeishuTradeNotification.ReadResponseCode(test.Body) != test.Code)
        throw new Exception($"Incorrect response classification: {test.Body}");
Console.WriteLine("PASS trade formatting, payload, and 7 response classifications (offline)");
var latch = new StartupTestOrderLatch();
if (latch.TryConsume()) throw new Exception("Test orders must default to off.");
latch.Arm(true);
if (!latch.TryConsume() || latch.TryConsume()) throw new Exception("Only one attempt is allowed.");
latch.Arm(false);
if (latch.TryConsume()) throw new Exception("Restart without enabling must not rearm.");
latch.Arm(true);
if (!latch.TryConsume()) throw new Exception("Explicit enabling must allow another attempt.");
latch.Arm(true);
latch.Arm(false);
if (latch.TryConsume()) throw new Exception("Disabled restart must cancel a pending request.");
Console.WriteLine("PASS one-shot startup test order lifecycle");
var start = DateTime.UtcNow;
latch.Arm(true);
if (!StartupTestOrderLatch.CanWait("LiveLatencyUnavailable", start, start.AddSeconds(30)) ||
    !latch.IsPending || !latch.TryConsume() || latch.TryConsume())
    throw new Exception("Startup waiting must preserve exactly one attempt.");
if (StartupTestOrderLatch.CanWait("LiveLatencyUnavailable", start.AddSeconds(30), start.AddSeconds(30)) ||
    StartupTestOrderLatch.CanWait("AccountNotFlatOrWorkingOrdersPresent", start, start.AddSeconds(30)) ||
    StartupTestOrderLatch.CanWait("AutoModeRequired", start, start.AddSeconds(30)))
    throw new Exception("Waiting must be bounded and must not retry account or mode failures.");
Console.WriteLine("PASS bounded startup readiness wait without duplicate attempts");
if (StartupTestOrderLatch.UseVirtualTarget("StartupTestOrder", true) ||
    StartupTestOrderLatch.UseVirtualTarget("StartupTestOrder", false) ||
    StartupTestOrderLatch.UseVirtualTarget("ObservationConfirm", false) ||
    !StartupTestOrderLatch.UseVirtualTarget("ObservationConfirm", true))
    throw new Exception("Test orders must always submit real targets; normal Replay policy must remain unchanged.");
Console.WriteLine("PASS real test target routing in live and Replay modes");
