using System.Text.Json;

namespace OPFStrategyV1.Strategy;

public static class FeishuTradeNotification
{
    public static string FormatConfirmedEntry(
        string tradeId,
        string side,
        decimal entry,
        decimal stop,
        decimal target,
        string researchPath)
    {
        return $"【开仓】OPF 成交确认\n订单: {tradeId}\n方向: {side}\n成交: {entry:0.00}\nSL: {stop:0.00}\nTP: {target:0.00}\n路径: {researchPath}";
    }

    public static string FormatCompletedExit(
        string tradeId,
        string side,
        decimal entry,
        decimal exit,
        string exitRole,
        decimal pnlPoints,
        decimal pnlDollars,
        string researchPath)
    {
        return $"【平仓】OPF 成交确认\n订单: {tradeId}\n方向: {side}\n入场: {entry:0.00}\n平仓: {exit:0.00}\n原因: {exitRole}\n盈亏: {pnlPoints:+0.00;-0.00;0.00} 点 / ${pnlDollars:+0.00;-0.00;0.00}\n路径: {researchPath}";
    }

    public static string CreatePayload(string text) => JsonSerializer.Serialize(new { msg_type = "text", content = new { text } });
}
