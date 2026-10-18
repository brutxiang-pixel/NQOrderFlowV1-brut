namespace OPFStrategyV1.Strategy;

public static class AccountPnlScope
{
    public static bool Matches(string[] fields, string account, string instrument, bool replay) =>
        account != "unresolved" && fields.Length >= 23 &&
        string.Equals(fields[20].Trim('"'), account, StringComparison.Ordinal) &&
        string.Equals(fields[21].Trim('"'), instrument, StringComparison.OrdinalIgnoreCase) &&
        bool.TryParse(fields[22].Trim('"'), out var historical) && historical == replay;
}
