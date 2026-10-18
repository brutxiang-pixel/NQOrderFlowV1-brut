using System.Globalization;
using System.Text.RegularExpressions;

namespace OPFStrategyV1.Strategy;

internal sealed class LegacyPnlIdentity
{
    private readonly Dictionary<string, HashSet<(string Account, string Product, DateTime Utc)>> _entries = new();

    public void Observe(string line)
    {
        if (!line.Contains("Order registered", StringComparison.Ordinal) || !line.Contains("OPF|", StringComparison.Ordinal)) return;
        var match = Regex.Match(line, @"^(?<time>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}),\d+.*Order registered .* (?:Buy|Sell) Market (?<product>\S+) on (?<account>\S+) .* OPF\|(?<trade>[^|]+)\|ENTRY\|");
        if (!match.Success || !DateTime.TryParseExact(match.Groups["time"].Value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local)) return;
        var utc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), TimeZoneInfo.Local);
        var id = match.Groups["trade"].Value;
        if (!_entries.TryGetValue(id, out var entries)) _entries[id] = entries = new();
        entries.Add((LiveAccountScope.FromIdentity(match.Groups["account"].Value), match.Groups["product"].Value.Split('@')[0], utc));
    }

    public bool? Matches(string[] fields, string account, string product, bool replay)
    {
        if (fields.Length < 18 || account == "unresolved") return null;
        if (fields.Length >= 23 && fields[20].Trim('"') is { } oldScope && oldScope.Contains("_Bal_", StringComparison.Ordinal) &&
            bool.TryParse(fields[22].Trim('"'), out var oldReplay))
        {
            var stable = oldScope[..oldScope.IndexOf("_Bal_", StringComparison.Ordinal)];
            return stable == account && string.Equals(fields[21].Trim('"'), product, StringComparison.OrdinalIgnoreCase) && oldReplay == replay;
        }
        if (replay || !DateTime.TryParse(fields[7].Trim('"'), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var entry)) return null;
        entry = entry.Kind == DateTimeKind.Local ? entry.ToUniversalTime() : DateTime.SpecifyKind(entry, DateTimeKind.Utc);
        if (!_entries.TryGetValue(fields[6].Trim('"'), out var evidence)) return null;
        var identities = evidence.Where(x => Math.Abs((x.Utc - entry).TotalMinutes) <= 10)
            .Select(x => (x.Account, x.Product)).Distinct().ToArray();
        if (identities.Length != 1) return null;
        return identities[0].Account == account && string.Equals(identities[0].Product, product, StringComparison.OrdinalIgnoreCase);
    }
}
