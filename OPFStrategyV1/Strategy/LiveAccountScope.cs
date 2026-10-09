using System.Text;

namespace OPFStrategyV1.Strategy;

public static class LiveAccountScope
{
    public static string FromPortfolio(object? portfolio)
    {
        if (portfolio is null)
            return "unresolved";

        try
        {
            var properties = portfolio.GetType().GetProperties();
            var identity = properties
                .Where(property => property.CanRead &&
                    property.Name is "Id" or "ID" or "Code" or "Name" or "Number" or "AccountId" or "PortfolioId")
                .Select(property => Convert.ToString(property.GetValue(portfolio), System.Globalization.CultureInfo.InvariantCulture))
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            return FromIdentity(identity ?? portfolio.ToString());
        }
        catch
        {
            return "unresolved";
        }
    }

    public static string FromIdentity(string? identity)
    {
        if (string.IsNullOrWhiteSpace(identity))
            return "unresolved";

        var builder = new StringBuilder();
        var previousWasSeparator = false;
        foreach (var character in identity.Trim())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                previousWasSeparator = false;
            }
            else if (!previousWasSeparator)
            {
                builder.Append('_');
                previousWasSeparator = true;
            }
        }

        return builder.ToString().Trim('_') is { Length: > 0 } scope ? scope : "unresolved";
    }
}
