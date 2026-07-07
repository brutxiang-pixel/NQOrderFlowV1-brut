namespace OPFStrategyV1.Core.MarketData;

public sealed record OpfCandle(
    int Bar,
    DateTime Time,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume,
    decimal Vwap);
