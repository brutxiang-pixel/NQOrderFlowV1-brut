namespace OPFStrategyV1.Strategy;

public static class LiveConnectorlessContextGate
{
    public static bool CanUseContextFallback(
        bool connectorAvailable,
        bool portfolioAvailable,
        bool securityAvailable,
        decimal currentPosition,
        bool hasActiveExecutions)
    {
        return !connectorAvailable &&
            portfolioAvailable &&
            securityAvailable &&
            currentPosition == 0m &&
            !hasActiveExecutions;
    }
}
