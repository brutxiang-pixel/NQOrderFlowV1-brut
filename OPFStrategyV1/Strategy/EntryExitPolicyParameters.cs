namespace OPFStrategyV1.Strategy;

// Frozen values from OPF_INTRADAY_2.17; extraction does not retune thresholds.
public static class EntryExitPolicyParameters
{
    public const string Version = "OPF_ENTRY_EXIT_2.18";
    public const decimal IcebergTargetR = 1.5m;
    public const decimal IcebergBreakEvenR = .75m;
    public const decimal ZoneBirthVolumeV122MinSetupQualityScore = 45m;
    public const decimal ZoneBirthVolumeV122MaxRiskPoints = 25m;
    public const decimal ZoneBirthVolumeV122MinEstimatedRr = 0.3m;
    public const decimal StrictVolumeV122MinSetupQualityScore = 45m;
    public const decimal StrictVolumeV122MaxRiskPoints = 25m;
    public const decimal StrictVolumeV122MinEstimatedRr = 0.5m;
    public const decimal BreakawayVolumeV128MinSetupQualityScore = 76m;
    public const decimal BreakawayVolumeV128LongMaxRiskPoints = 25m;
    public const decimal BreakawayVolumeV128ShortMaxRiskPoints = 30m;
    public const decimal BreakawayVolumeV128MinEstimatedRr = 1m;
    public const decimal BreakawayLongSelectiveV134MinSetupQualityScore = 95m;
    public const decimal BreakawayLongSelectiveV134MaxRiskPoints = 18m;
    public const decimal BreakawayLongSelectiveV134MinEstimatedRr = 1.5m;
    public const decimal ObservationLongVolumeV123MinSetupQualityScore = 70m;
    public const decimal ObservationLongVolumeV123MaxRiskPoints = 18m;
    public const decimal ObservationLongVolumeV123MinEstimatedRr = 0.8m;
    public const decimal UnknownMicroRiskVolumeV126MinSetupQualityScore = 45m;
    public const decimal UnknownMicroRiskVolumeV126MaxRiskPoints = 8.5m;
    public const decimal UnknownMicroRiskVolumeV126MinEstimatedRr = 1.5m;
    public const decimal ObservationShortVolumeV124MinSetupQualityScore = 60m;
    public const decimal ObservationShortVolumeV124MaxRiskPoints = 18m;
    public const decimal ObservationShortVolumeV124MinEstimatedRr = 1m;
    public const decimal ObservationConfirmLongV137MaxRiskPoints = 12m;
    public const decimal ObservationConfirmWideStopVolumeV131LongMinSetupQualityScore = 70m;
    public const decimal ObservationConfirmWideStopVolumeV131ShortMinSetupQualityScore = 60m;
    public const decimal ObservationConfirmWideStopVolumeV131MaxRiskPoints = 22m;
    public const decimal ObservationConfirmWideStopVolumeV131MinEstimatedRr = 0.8m;
    public const decimal ObservationConfirmWideStopLongExpansionV157MaxRiskPoints = 25m;
    public const int ObservationConfirmWideStopLongExpansionV163MaxTradesPerDay = 1;
    public const decimal ObservationConfirmWideStopLowRiskV132MinSetupQualityScore = 56m;
    public const decimal ObservationConfirmWideStopLowRiskV132MaxRiskPoints = 18m;
    public const decimal ObservationConfirmWideStopLowRiskV132MinEstimatedRr = 0.5m;
    public const decimal ObservationConfirmRisk22V132MinSetupQualityScore = 70m;
    public const decimal ObservationConfirmRisk22V132MaxRiskPoints = 22m;
    public const decimal ObservationConfirmRisk22V132MinEstimatedRr = 0.8m;
    public const decimal SelectiveProfitTargetV145R = 2.0m;
    public const decimal ObservationConfirmLongTarget2RV161MinRiskPoints = 8m;
    public const decimal ObservationConfirmLongTarget2RV161MaxRiskPoints = 11m;
    public const decimal ObservationConfirmLongTarget2RV161R = 2m;
    public const decimal BreakawayShortTargetRV209 = 4m;
    public const decimal BreakawayShortBreakEvenTriggerRV209 = 1.5m;
    public const int LongPolicyTimeStopBarsV209 = 36;
    public const int ShortPolicyTimeStopBarsV209 = 36;
    public const int FastPolicyTimeStopBarsV209 = 12;

    public static bool TryGetExit(
        bool isLong,
        string researchPath,
        out decimal targetR,
        out decimal breakEvenTriggerR,
        out int timeStopBars,
        decimal risk = 0m)
    {
        targetR = 0m;
        breakEvenTriggerR = 0m;
        timeStopBars = 0;

        if (researchPath is "NativeIcebergReaction" or "CrossMarketIcebergOrderFlowReaction")
            (targetR, breakEvenTriggerR, timeStopBars) = (IcebergTargetR, IcebergBreakEvenR, 0);
        else if (researchPath == "ObservationConfirm")
            (targetR, breakEvenTriggerR, timeStopBars) = (1.5m, .75m, 6);
        else if (researchPath == "BreakawayFvg_Qualified")
            (targetR, breakEvenTriggerR, timeStopBars) = (1.5m, .75m, FastPolicyTimeStopBarsV209);
        else if (researchPath == "FailureReverse_RetestFailed")
            (targetR, breakEvenTriggerR, timeStopBars) = (1.5m, 1m, ShortPolicyTimeStopBarsV209);
        else if (researchPath == "FailureReverse_ObservationInvalidated_WideStop1_5R")
            (targetR, breakEvenTriggerR, timeStopBars) = (4m, 1m, ShortPolicyTimeStopBarsV209);
        else if (!isLong && researchPath == "ZoneBirthResearch" && risk > 0m && risk <= 20m)
            (targetR, timeStopBars) = (1.5m, 12);
        else if (researchPath == "ESResponseAlignedBreakout")
            (targetR, breakEvenTriggerR) = (1.5m, 0.75m);
        else if (researchPath is "ESWallRejectionReversal" or "ESExpectedMoveExpansion" or "ESGammaConcentrationResponse")
            (targetR, breakEvenTriggerR) = (1.5m, 1m);
        else if (!isLong && string.Equals(researchPath, "BreakawayFvg", StringComparison.OrdinalIgnoreCase))
            (targetR, breakEvenTriggerR, timeStopBars) = (BreakawayShortTargetRV209, BreakawayShortBreakEvenTriggerRV209, ShortPolicyTimeStopBarsV209);
        else if (isLong && string.Equals(researchPath, "ObservationConfirm_WideStop1_5R", StringComparison.OrdinalIgnoreCase))
            (targetR, timeStopBars) = (3m, FastPolicyTimeStopBarsV209);
        else if (isLong && string.Equals(researchPath, "ObservationStrict_Other", StringComparison.OrdinalIgnoreCase))
            (targetR, timeStopBars) = (3m, FastPolicyTimeStopBarsV209);
        else if (isLong && string.Equals(researchPath, "AlmostConfirmed", StringComparison.OrdinalIgnoreCase))
            (targetR, timeStopBars) = (4m, LongPolicyTimeStopBarsV209);
        else if (!isLong && string.Equals(researchPath, "FailureReverse_PreEntryConfirmedWideStopShort", StringComparison.OrdinalIgnoreCase))
            (targetR, timeStopBars) = (4m, ShortPolicyTimeStopBarsV209);

        return targetR > 0m;
    }
}
