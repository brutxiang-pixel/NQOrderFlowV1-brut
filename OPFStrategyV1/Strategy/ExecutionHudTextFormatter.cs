using OPFStrategyV1.Core.Signals;

namespace OPFStrategyV1.Strategy;

public static class ExecutionHudTextFormatter
{
    public static string Format(TradeSide side, string exitRole, decimal pointsR, decimal dollars, string researchPath)
    {
        var sideText = side == TradeSide.Long ? "L" : "S";
        return $"{sideText} {exitRole} {pointsR:0.00}R ${dollars:0.00} {researchPath}";
    }
}
