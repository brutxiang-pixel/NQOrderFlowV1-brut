namespace OPFStrategyV1.Strategy;

internal static class ExecutionIdentity
{
    public static string Oco(string snapshot, string product, string trade)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes($"{snapshot}|{product}|{trade}");
        return "OPF-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))[..24];
    }
}
