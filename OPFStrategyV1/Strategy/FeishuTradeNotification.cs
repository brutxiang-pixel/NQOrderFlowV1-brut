using System.Text.Json;

namespace OPFStrategyV1.Strategy;

public static class FeishuTradeNotification
{
    public static string FormatConfirmedEntry(
        string tradeId,
        string productCode,
        string side,
        decimal entry,
        decimal stop,
        decimal target,
        string researchPath,
        DateTimeOffset? eventTime = null)
    {
        var time = (eventTime ?? DateTimeOffset.UtcNow).ToOffset(TimeSpan.FromHours(8));
        return $"【开仓】订单已成交｜OPEN\n产品: {productCode}\n订单标识: {tradeId}\n消息标识: {tradeId}/OPEN\n开仓确认时间（北京时间 UTC+8）: {time:yyyy-MM-dd HH:mm:ss}\n方向: {side}\n成交: {entry:0.00}\nSL: {stop:0.00}\nTP: {target:0.00}\n路径: {researchPath}";
    }

    public static string FormatCompletedExit(
        string tradeId,
        string productCode,
        string side,
        decimal entry,
        decimal exit,
        string exitRole,
        decimal pnlPoints,
        decimal pnlDollars,
        string researchPath,
        DateTimeOffset? eventTime = null,
        decimal commissionDollars = 0m,
        bool breakEvenApplied = false)
    {
        var time = (eventTime ?? DateTimeOffset.UtcNow).ToOffset(TimeSpan.FromHours(8));
        var reason = exitRole == "SL"
            ? breakEvenApplied ? "保本止损（已移动，含手续费目标）｜SL" : "原始止损｜SL"
            : exitRole;
        return $"【平仓】订单已全部平仓｜CLOSE\n产品: {productCode}\n订单标识: {tradeId}\n消息标识: {tradeId}/CLOSE\n平仓确认时间（北京时间 UTC+8）: {time:yyyy-MM-dd HH:mm:ss}\n方向: {side}\n入场: {entry:0.00}\n平仓: {exit:0.00}\n原因: {reason}\n毛盈亏（未扣手续费）: {pnlPoints:+0.00;-0.00;0.00} 点 / ${pnlDollars:+0.00;-0.00;0.00}\n手续费（配置估算）: ${commissionDollars:0.00}\n净盈亏（按配置手续费）: ${pnlDollars - commissionDollars:+0.00;-0.00;0.00}\n路径: {researchPath}";
    }

    public static string CreatePayload(string text) => JsonSerializer.Serialize(new { msg_type = "text", content = new { text } });

    public static int? ReadResponseCode(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.ValueKind != JsonValueKind.Object)
                return null;
            if ((json.RootElement.TryGetProperty("code", out var code) ||
                 json.RootElement.TryGetProperty("StatusCode", out code)) &&
                code.ValueKind == JsonValueKind.Number &&
                code.TryGetInt32(out var value))
                return value;
        }
        catch (JsonException) { }
        return null;
    }
}
