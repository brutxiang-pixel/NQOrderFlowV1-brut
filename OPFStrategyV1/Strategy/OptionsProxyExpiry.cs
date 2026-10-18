using System.Text.RegularExpressions;

namespace OPFStrategyV1.Strategy;

internal static class OptionsProxyExpiry
{
    private static readonly TimeZoneInfo Chicago = TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time");

    internal static DateTime ToUtc(DateTime expiration, string code)
    {
        // The provider returns a calendar date for CME European weekly options.
        // Do not apply their PM settlement convention to quarterly/unknown contracts.
        if (expiration.TimeOfDay == TimeSpan.Zero &&
            Regex.IsMatch(code, @"^(E[1-5][A-D]|EW[1-5])[FGHJKMNQUVXZ][0-9]", RegexOptions.IgnoreCase))
        {
            var date = expiration.Date;
            var earlyClose = (date.Month == 11 && date.DayOfWeek == DayOfWeek.Friday && date.Day is >= 23 and <= 29)
                || (date.Month == 12 && date.Day == 24 && date.DayOfWeek is >= DayOfWeek.Monday and <= DayOfWeek.Thursday)
                || (date.Month == 7 && date.Day == 3 && date.DayOfWeek is >= DayOfWeek.Monday and <= DayOfWeek.Thursday);
            return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(date.AddHours(earlyClose ? 12 : 15), DateTimeKind.Unspecified), Chicago);
        }
        return expiration.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(expiration, DateTimeKind.Utc)
            : expiration.ToUniversalTime();
    }

    internal static DateTime ExchangeDate(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(utc, Chicago).Date;
}
