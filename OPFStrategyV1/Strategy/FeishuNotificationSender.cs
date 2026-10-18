using System.Text;

namespace OPFStrategyV1.Strategy;

internal sealed record FeishuSendResult(int HttpStatus, int? BusinessCode)
{
    public bool Success => HttpStatus is >= 200 and < 300 && BusinessCode == 0;
    public bool RateLimited => HttpStatus == 429 || BusinessCode == 11232;
}

internal static class FeishuNotificationSender
{
    private static readonly SemaphoreSlim SendGate = new(1, 1);
    private static DateTime _lastSendUtc;

    public static async Task<FeishuSendResult> SendAsync(HttpClient client, Uri webhook, string text,
        Action<int, FeishuSendResult> observed)
    {
        return await RetryRateLimitAsync(async () =>
        {
            await SendGate.WaitAsync().ConfigureAwait(false);
            try
            {
                var wait = TimeSpan.FromMilliseconds(650) - (DateTime.UtcNow - _lastSendUtc);
                if (wait > TimeSpan.Zero) await Task.Delay(wait).ConfigureAwait(false);
                using var content = new StringContent(FeishuTradeNotification.CreatePayload(text), Encoding.UTF8, "application/json");
                _lastSendUtc = DateTime.UtcNow;
                using var response = await client.PostAsync(webhook, content).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                return new FeishuSendResult((int)response.StatusCode, FeishuTradeNotification.ReadResponseCode(body));
            }
            finally { SendGate.Release(); }
        }, delay => Task.Delay(delay), observed).ConfigureAwait(false);
    }

    internal static async Task<FeishuSendResult> RetryRateLimitAsync(Func<Task<FeishuSendResult>> send,
        Func<TimeSpan, Task> delay, Action<int, FeishuSendResult> observed)
    {
        var pauses = new[] { 2, 5, 10 };
        for (var attempt = 1; ; attempt++)
        {
            var result = await send().ConfigureAwait(false);
            observed(attempt, result);
            if (!result.RateLimited || attempt > pauses.Length) return result;
            await delay(TimeSpan.FromSeconds(pauses[attempt - 1])).ConfigureAwait(false);
        }
    }
}
