namespace OPFStrategyV1.Strategy;

public static class LiveProtectionFailClosedGuard
{
    public static async Task<bool> RegisterPairAsync(Func<Task<bool>> stop, Func<Task<bool>> target)
    {
        var stopRegistration = stop();
        var targetRegistration = target();
        var results = await Task.WhenAll(stopRegistration, targetRegistration);
        return results.All(result => result);
    }

    public static bool RegistrationAcknowledged(string state) => state is "Active" or "Done";

    public static async Task AwaitRegistrationAsync(Task registration, TimeSpan timeout)
    {
        try { await registration.WaitAsync(timeout); }
        catch (TimeoutException)
        {
            _ = registration.ContinueWith(task => { _ = task.Exception; },
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
            throw;
        }
    }

    public static bool AccountPositionMatchesExpected(decimal actual, decimal expected) =>
        Math.Abs(actual - expected) <= 0.0000001m;

    public static bool RequiresEmergencyFlattenResidual(
        bool flattenOrderFullyFilled,
        decimal accountPositionQuantity,
        decimal executionRemainingQuantity)
    {
        return !flattenOrderFullyFilled &&
            Math.Abs(accountPositionQuantity) > 0.0000001m &&
            executionRemainingQuantity > 0.0000001m;
    }

    public static bool RequiresEmergencyFlatten(
        decimal entryFilledQty,
        decimal exitFilledQty,
        bool hasWorkingStop,
        bool exitCompleted,
        bool emergencyFlattenSubmitted)
    {
        return entryFilledQty > exitFilledQty &&
            !hasWorkingStop &&
            !exitCompleted &&
            !emergencyFlattenSubmitted;
    }
}
