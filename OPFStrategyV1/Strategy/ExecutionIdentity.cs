namespace OPFStrategyV1.Strategy;

internal static class ExecutionIdentity
{
    public static string OrderKey(string trade) => "@" + Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(trade)))[..24];

    public static string SplitComment(string trade, bool runner, bool stop) =>
        $"OPF|{OrderKey(trade)}|{(runner ? stop ? "RS" : "RT" : stop ? "BS" : "BT")}";

    public static string NormalizeRole(string role) => role switch
    {
        "RS" => "RUNNER_SL", "RT" => "RUNNER_TP", "BS" => "BASE_SL", "BT" => "BASE_TP", _ => role
    };

    public static string Oco(string snapshot, string product, string trade)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes($"{snapshot}|{product}|{trade}");
        return "OPF-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))[..24];
    }
}
