using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace OPFStrategyV1.Strategy;

internal sealed record ArchivedHistoricalExecution(
    string TradeId,
    DateTime EntryTime,
    DateTime ExitTime,
    string Side,
    string ResearchPath,
    decimal Quantity,
    decimal EntryPrice,
    decimal ExitPrice,
    decimal StopPrice,
    decimal TargetPrice,
    decimal GrossDollars,
    string ExitRole);

internal sealed record HistoricalExecutionArchiveResult(
    IReadOnlyList<ArchivedHistoricalExecution> Executions,
    int EligiblePnlRows,
    int SkippedRows);

internal static class HistoricalExecutionArchive
{
    private static readonly Regex InstrumentRootPattern = new(
        "^(MNQ|MES|NQ|ES)(?=$|[^A-Z]|[FGHJKMNQUVXZ][0-9])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string InstrumentRoot(string? productCode)
    {
        if (string.IsNullOrWhiteSpace(productCode))
            return string.Empty;
        return InstrumentRootPattern.Match(productCode.Trim()).Value.ToUpperInvariant();
    }

    public static HistoricalExecutionArchiveResult Load(string directory, string accountScope, string productCode)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory) ||
            string.IsNullOrWhiteSpace(accountScope) || accountScope == "unresolved")
            return new(Array.Empty<ArchivedHistoricalExecution>(), 0, 0);

        var root = InstrumentRoot(productCode);
        if (root.Length == 0)
            return new(Array.Empty<ArchivedHistoricalExecution>(), 0, 0);

        var eligiblePnl = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var skippedRows = 0;
        foreach (var path in Directory.EnumerateFiles(directory, "*_live_account_pnl.csv"))
        {
            foreach (var row in ReadCsvRows(path, () => skippedRows++))
            {
                if (!TryGet(row, "TradeID", out var tradeId) || string.IsNullOrWhiteSpace(tradeId) ||
                    !TryGet(row, "AccountScope", out var account) || !string.Equals(account, accountScope, StringComparison.Ordinal) ||
                    !TryGet(row, "HistoricalReplay", out var replayText) ||
                    !bool.TryParse(replayText, out var replay) || replay ||
                    !TryGet(row, "ProductCode", out var archivedProduct) || InstrumentRoot(archivedProduct) != root ||
                    !TryGetDecimal(row, "GrossPnLDollars", out var grossDollars))
                    continue;

                eligiblePnl[tradeId] = grossDollars;
            }
        }

        var executions = new Dictionary<string, ArchivedHistoricalExecution>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(directory, "*_execution_trades.csv"))
        {
            foreach (var row in ReadCsvRows(path, () => skippedRows++))
            {
                if (!TryGet(row, "TradeID", out var tradeId) || !eligiblePnl.TryGetValue(tradeId, out var grossDollars) ||
                    !TryGet(row, "EntryTime", out var entryText) || !TryGetDateTime(entryText, out var entryTime) ||
                    !TryGet(row, "ExitTime", out var exitText) || !TryGetDateTime(exitText, out var exitTime) ||
                    !TryGet(row, "Side", out var side) || (side != "Long" && side != "Short") ||
                    !TryGetDecimal(row, "Quantity", out var quantity) || quantity <= 0m ||
                    !TryGetDecimal(row, "EntryPrice", out var entryPrice) || entryPrice <= 0m ||
                    !TryGetDecimal(row, "ExitPrice", out var exitPrice) || exitPrice <= 0m)
                {
                    skippedRows++;
                    continue;
                }

                TryGetDecimal(row, "StopPrice", out var stopPrice);
                TryGetDecimal(row, "TargetPrice", out var targetPrice);
                TryGet(row, "ResearchPath", out var researchPath);
                TryGet(row, "ExitRole", out var exitRole);

                var execution = new ArchivedHistoricalExecution(
                    tradeId, entryTime, exitTime, side, researchPath, quantity,
                    entryPrice, exitPrice, stopPrice, targetPrice, grossDollars, exitRole);
                if (!executions.TryGetValue(tradeId, out var previous) || execution.ExitTime > previous.ExitTime)
                    executions[tradeId] = execution;
            }
        }

        return new(executions.Values.OrderByDescending(x => x.ExitTime).ToArray(), eligiblePnl.Count, skippedRows);
    }

    internal static bool TryParseCsvLine(string line, out string[] fields)
    {
        var result = new List<string>();
        var value = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var character = line[i];
            if (character == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    value.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (character == ',' && !inQuotes)
            {
                result.Add(value.ToString());
                value.Clear();
            }
            else
            {
                value.Append(character);
            }
        }

        fields = Array.Empty<string>();
        if (inQuotes)
            return false;
        result.Add(value.ToString());
        fields = result.ToArray();
        return true;
    }

    private static IEnumerable<Dictionary<string, string>> ReadCsvRows(string path, Action onSkippedRow)
    {
        using var reader = new StreamReader(path);
        var headerLine = reader.ReadLine();
        if (headerLine is null || !TryParseCsvLine(headerLine, out var headerFields))
        {
            onSkippedRow();
            yield break;
        }

        var headers = headerFields.Select((name, index) => (Name: name.Trim().TrimStart('\uFEFF'), Index: index))
            .Where(x => x.Name.Length > 0)
            .ToDictionary(x => x.Name, x => x.Index, StringComparer.OrdinalIgnoreCase);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            if (!TryParseCsvLine(line, out var fields) || fields.Length < headerFields.Length)
            {
                onSkippedRow();
                continue;
            }

            var values = new Dictionary<string, string>(headers.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var header in headers)
                values[header.Key] = fields[header.Value].Trim();
            yield return values;
        }
    }

    private static bool TryGet(Dictionary<string, string> row, string field, out string value)
    {
        if (row.TryGetValue(field, out var found))
        {
            value = found;
            return true;
        }
        value = string.Empty;
        return false;
    }

    private static bool TryGetDecimal(Dictionary<string, string> row, string field, out decimal value)
    {
        value = 0m;
        return TryGet(row, field, out var text) && decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryGetDateTime(string text, out DateTime value) =>
        DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value);
}
