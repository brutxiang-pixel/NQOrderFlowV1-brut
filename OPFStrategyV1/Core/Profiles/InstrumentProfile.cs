namespace OPFStrategyV1.Core.Profiles;

public sealed record InstrumentProfile(
    string Instrument,
    string Version,
    decimal TickSize,
    decimal TickValue,
    decimal PointValue,
    string Currency,
    int OpeningRangeMinutes,
    decimal MinStopPoints,
    decimal MaxRiskPointsHard,
    decimal AtrRiskMultiplier,
    decimal MinEstimatedRr,
    int DefaultContracts)
{
    public static InstrumentProfile MnqDefault()
    {
        return new InstrumentProfile(
            Instrument: "MNQ",
            Version: "MNQ_0.1",
            TickSize: 0.25m,
            TickValue: 0.50m,
            PointValue: 2m,
            Currency: "USD",
            OpeningRangeMinutes: 30,
            MinStopPoints: 2m,
            MaxRiskPointsHard: 25m,
            AtrRiskMultiplier: 0.60m,
            MinEstimatedRr: 1.50m,
            DefaultContracts: 1);
    }
}
