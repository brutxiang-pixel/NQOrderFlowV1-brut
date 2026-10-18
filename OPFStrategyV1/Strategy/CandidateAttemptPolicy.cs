namespace OPFStrategyV1.Strategy;

internal static class CandidateAttemptPolicy
{
    // Retry only temporary execution conditions. Manual intervention and submitted orders
    // remain terminal for the event; a broker rejection never causes an automatic resend.
    internal static bool CanRetry(string decision, string reason) => decision == "Skip" &&
        (reason.StartsWith("OptionsGate:", StringComparison.Ordinal) ||
         reason.StartsWith("LiveReadinessBlocked:", StringComparison.Ordinal) ||
         reason.StartsWith("LiveLatency", StringComparison.Ordinal) ||
         reason.StartsWith("ActiveTradeLimit:", StringComparison.Ordinal) ||
         reason.StartsWith("SecondaryPrimaryNotActuallyProtectedAtBE", StringComparison.Ordinal) ||
         reason.StartsWith("SecondaryDirectionMismatch:", StringComparison.Ordinal) ||
         reason.StartsWith("SecondaryRiskCap:", StringComparison.Ordinal) ||
         reason.StartsWith("MTF:", StringComparison.Ordinal) ||
         reason.StartsWith("Micro:", StringComparison.Ordinal) ||
         reason.StartsWith("Context:", StringComparison.Ordinal));
}
