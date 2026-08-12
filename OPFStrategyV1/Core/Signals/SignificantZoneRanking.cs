namespace OPFStrategyV1.Core.Signals;

public enum SignificantZoneGrade
{
    C,
    B,
    A
}

public enum SignificantZoneDecision
{
    Neutral,
    PositiveSignal,
    StrongNegativeFilter
}

public sealed record SignificantZoneStrengthInput(
    decimal GapPoints,
    decimal BodyPoints,
    decimal RangePoints,
    decimal Atr14,
    decimal FormingVolume,
    decimal AverageVolume20,
    decimal RecentBuyVolume,
    decimal RecentSellVolume,
    decimal ZoneBuyVolume,
    decimal ZoneSellVolume,
    int ZoneTradeCount,
    int ZonePriceLevelCount,
    decimal LargestPriceLevelVolume);

public sealed record SignificantZoneStrength(
    SignificantZoneGrade Grade,
    decimal Score,
    decimal DisplacementScore,
    decimal ParticipationScore,
    decimal ConcentrationScore,
    decimal StructureScore,
    string Evidence);

public sealed record SignificantZoneReference(string ZoneId, SignificantZoneGrade Grade, decimal Score, decimal DistancePoints, SignificantZoneState State)
{
    public static readonly SignificantZoneReference None = new(string.Empty, SignificantZoneGrade.C, 0m, 0m, SignificantZoneState.Expired);
}

public sealed record SignificantZoneContext(
    SignificantZoneReference SameSide,
    SignificantZoneReference OpposingSide,
    SignificantZoneReference UpperExtreme,
    SignificantZoneReference LowerExtreme,
    bool InsideZone,
    bool AtOuterBoundary,
    bool AtInnerBoundary,
    string Decision,
    string Reason)
{
    public static readonly SignificantZoneContext Empty = new(SignificantZoneReference.None, SignificantZoneReference.None, SignificantZoneReference.None, SignificantZoneReference.None, false, false, false, "ObserveOnly", "NoEligibleSignificantZone");
}

public sealed record SignificantZoneSelection(
    IReadOnlyList<SignificantZone> VisibleZones,
    SignificantZone? UpperExtreme,
    SignificantZone? LowerExtreme);

public sealed record SignificantZoneDecisionResult(SignificantZoneDecision Decision, string Reason)
{
    public bool IsHardBlock => Decision == SignificantZoneDecision.StrongNegativeFilter;
}

public sealed class SignificantZoneRanker
{
    public const decimal GradeA = 70m;
    public const decimal GradeB = 45m;

    public SignificantZoneStrength Score(SignificantZoneStrengthInput input)
    {
        var atr = Math.Max(input.Atr14, 1m);
        var gapAtr = input.GapPoints / atr;
        var bodyRatio = input.RangePoints <= 0m ? 0m : input.BodyPoints / input.RangePoints;
        var displacement = Math.Min(30m, gapAtr * 80m + bodyRatio * 12m);

        var average = Math.Max(input.AverageVolume20, 1m);
        var relativeVolume = input.FormingVolume / average;
        var recentTotal = input.RecentBuyVolume + input.RecentSellVolume;
        var imbalance = recentTotal <= 0m ? 0m : Math.Abs(input.RecentBuyVolume - input.RecentSellVolume) / recentTotal;
        var participation = Math.Min(30m, relativeVolume * 12m + imbalance * 18m);

        var zoneVolume = input.ZoneBuyVolume + input.ZoneSellVolume;
        var zoneShare = input.FormingVolume <= 0m ? 0m : zoneVolume / input.FormingVolume;
        var priceConcentration = zoneVolume <= 0m ? 0m : input.LargestPriceLevelVolume / zoneVolume;
        var coverage = Math.Min(input.ZonePriceLevelCount, 6) / 6m;
        var concentration = Math.Min(20m, zoneShare * 10m + priceConcentration * 6m + coverage * 4m);

        var structure = Math.Min(20m, 8m + (input.ZoneTradeCount >= 12 ? 6m : input.ZoneTradeCount >= 6 ? 3m : 0m) + (gapAtr >= .15m ? 6m : gapAtr >= .08m ? 3m : 0m));
        var score = Math.Round(displacement + participation + concentration + structure, 2);
        var grade = score >= GradeA ? SignificantZoneGrade.A : score >= GradeB ? SignificantZoneGrade.B : SignificantZoneGrade.C;
        return new SignificantZoneStrength(grade, score, Math.Round(displacement, 2), Math.Round(participation, 2), Math.Round(concentration, 2), Math.Round(structure, 2), $"gapAtr={gapAtr:0.###}|relVol={relativeVolume:0.###}|imbalance={imbalance:0.###}|zoneShare={zoneShare:0.###}|levels={input.ZonePriceLevelCount}");
    }

    public SignificantZoneSelection Select(IEnumerable<SignificantZone> zones, decimal price, decimal atr14)
    {
        var eligible = zones.Where(IsEligible).ToArray();
        var upper = eligible.OrderByDescending(x => Math.Max(x.InnerBoundary, x.OuterBoundary)).ThenByDescending(ScoreOf).ThenBy(x => x.CreatedTime).FirstOrDefault();
        var lower = eligible.OrderBy(x => Math.Min(x.InnerBoundary, x.OuterBoundary)).ThenByDescending(ScoreOf).ThenBy(x => x.CreatedTime).FirstOrDefault();
        var nearbyDistance = Math.Max(2m * Math.Max(atr14, 1m), 40m);
        var nearby = eligible.Where(x => Distance(x, price) <= nearbyDistance)
            .GroupBy(x => x.Side)
            .Select(group => group.OrderByDescending(ScoreOf).ThenBy(x => Distance(x, price)).ThenByDescending(x => x.CreatedTime).First())
            .Where(x => GradeOf(x) == SignificantZoneGrade.A)
            .ToList();
        AddExtreme(nearby, upper);
        AddExtreme(nearby, lower);
        return new SignificantZoneSelection(nearby, upper, lower);
    }

    public SignificantZoneContext BuildContext(IEnumerable<SignificantZone> zones, TradeSide side, decimal price, decimal atr14, decimal riskPoints = 0m)
    {
        var selection = Select(zones, price, atr14);
        var eligible = zones.Where(IsEligible).ToArray();
        var same = Best(eligible.Where(x => x.Side == side), price);
        var opposing = Best(eligible.Where(x => x.Side != side), price);
        var inside = eligible.FirstOrDefault(x => price >= Math.Min(x.InnerBoundary, x.OuterBoundary) && price <= Math.Max(x.InnerBoundary, x.OuterBoundary));
        var tickRange = .5m;
        var outer = inside is null ? false : Math.Abs(price - inside.OuterBoundary) <= tickRange;
        var inner = inside is null ? false : Math.Abs(price - inside.InnerBoundary) <= tickRange;
        var decision = BuildDecision(eligible, side, price, atr14, riskPoints, selection.UpperExtreme, selection.LowerExtreme);
        return new SignificantZoneContext(ToReference(same, price), ToReference(opposing, price), ToReference(selection.UpperExtreme, price), ToReference(selection.LowerExtreme, price), inside is not null, outer, inner, decision.Decision.ToString(), decision.Reason);
    }

    public SignificantZoneDecisionResult BuildDecision(
        IEnumerable<SignificantZone> zones,
        TradeSide side,
        decimal price,
        decimal atr14,
        decimal riskPoints,
        SignificantZone? upperExtreme,
        SignificantZone? lowerExtreme)
    {
        var limit = Math.Max(riskPoints, Math.Max(atr14 / 2m, 5m));
        var active = zones.ToArray();
        var adverse = active.FirstOrDefault(zone => IsZoneHardNegative(zone, side, price, limit));
        if (adverse is not null)
            return new SignificantZoneDecisionResult(SignificantZoneDecision.StrongNegativeFilter, $"OpposingAZoneWithinRisk:zone={adverse.ZoneId}|distance={Distance(adverse, price):0.##}|limit={limit:0.##}");

        var support = active.FirstOrDefault(zone => IsSameSideSupport(zone, side, price, limit));
        if (support is not null)
            return new SignificantZoneDecisionResult(SignificantZoneDecision.PositiveSignal, $"SameSideASupport:zone={support.ZoneId}|distance={Distance(support, price):0.##}|limit={limit:0.##}");

        var extreme = side == TradeSide.Long ? lowerExtreme : upperExtreme;
        if (extreme is not null && extreme.Side == side && IsGradeBOrBetter(extreme) && Distance(extreme, price) <= limit)
            return new SignificantZoneDecisionResult(SignificantZoneDecision.PositiveSignal, $"AlignedSessionExtreme:zone={extreme.ZoneId}|grade={GradeOf(extreme)}|distance={Distance(extreme, price):0.##}|limit={limit:0.##}");

        return new SignificantZoneDecisionResult(SignificantZoneDecision.Neutral, "NoActionableSignificantZone");
    }

    public static bool IsZoneHardNegative(SignificantZone zone, TradeSide side, decimal price, decimal limit)
    {
        if (zone.Side == side || GradeOf(zone) != SignificantZoneGrade.A)
            return false;
        var low = Math.Min(zone.InnerBoundary, zone.OuterBoundary);
        var high = Math.Max(zone.InnerBoundary, zone.OuterBoundary);
        if (price >= low && price <= high)
            return true;
        return side == TradeSide.Long
            ? low > price && low - price <= limit
            : high < price && price - high <= limit;
    }

    public static bool IsEligible(SignificantZone zone) => zone.State is SignificantZoneState.Active or SignificantZoneState.TestedOnce;

    public static decimal Distance(SignificantZone zone, decimal price)
    {
        var low = Math.Min(zone.InnerBoundary, zone.OuterBoundary);
        var high = Math.Max(zone.InnerBoundary, zone.OuterBoundary);
        return price < low ? low - price : price > high ? price - high : 0m;
    }

    private static bool IsSameSideSupport(SignificantZone zone, TradeSide side, decimal price, decimal limit)
    {
        if (zone.Side != side || GradeOf(zone) != SignificantZoneGrade.A)
            return false;
        var low = Math.Min(zone.InnerBoundary, zone.OuterBoundary);
        var high = Math.Max(zone.InnerBoundary, zone.OuterBoundary);
        if (price >= low && price <= high)
            return true;
        return side == TradeSide.Long
            ? high <= price && price - high <= limit
            : low >= price && low - price <= limit;
    }

    private static SignificantZone? Best(IEnumerable<SignificantZone> zones, decimal price) => zones.OrderByDescending(ScoreOf).ThenBy(x => Distance(x, price)).ThenByDescending(x => x.CreatedTime).FirstOrDefault();
    private static SignificantZoneReference ToReference(SignificantZone? zone, decimal price) => zone is null ? SignificantZoneReference.None : new(zone.ZoneId, GradeOf(zone), ScoreOf(zone), Distance(zone, price), zone.State);
    private static SignificantZoneGrade GradeOf(SignificantZone zone) => zone.Strength?.Grade ?? SignificantZoneGrade.C;
    private static bool IsGradeBOrBetter(SignificantZone zone) => GradeOf(zone) is SignificantZoneGrade.A or SignificantZoneGrade.B;
    private static decimal ScoreOf(SignificantZone zone) => zone.Strength?.Score ?? 0m;
    private static void AddExtreme(ICollection<SignificantZone> zones, SignificantZone? extreme)
    {
        if (extreme is not null && GradeOf(extreme) is SignificantZoneGrade.A or SignificantZoneGrade.B && !zones.Any(x => x.ZoneId == extreme.ZoneId))
            zones.Add(extreme);
    }
}


