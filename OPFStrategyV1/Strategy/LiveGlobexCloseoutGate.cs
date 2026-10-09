namespace OPFStrategyV1.Strategy;

public static class LiveGlobexCloseoutGate
{
    public static readonly TimeSpan FlattenStartEastern = new(16, 35, 0);
    public static readonly TimeSpan CloseoutStartEastern = new(16, 40, 0);
    public static readonly TimeSpan ReopenEastern = new(18, 0, 0);

    public static bool IsLocked(DayOfWeek day, TimeSpan easternTime)
    {
        return day == DayOfWeek.Saturday ||
            (day == DayOfWeek.Sunday && easternTime < ReopenEastern) ||
            (day == DayOfWeek.Friday && easternTime >= CloseoutStartEastern) ||
            (day is >= DayOfWeek.Monday and <= DayOfWeek.Thursday &&
                easternTime >= CloseoutStartEastern && easternTime < ReopenEastern);
    }

    public static bool ShouldAttemptFlatten(DayOfWeek day, TimeSpan easternTime)
    {
        return (day == DayOfWeek.Friday && easternTime >= FlattenStartEastern) ||
            (day is >= DayOfWeek.Monday and <= DayOfWeek.Thursday &&
                easternTime >= FlattenStartEastern && easternTime < ReopenEastern);
    }
}
