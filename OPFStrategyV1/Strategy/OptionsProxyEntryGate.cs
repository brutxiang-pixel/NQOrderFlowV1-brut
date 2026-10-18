namespace OPFStrategyV1.Strategy;

public static class OptionsProxyEntryGate
{
    internal static string? Evaluate(EsOptionsProxySnapshot context, DateTime decision,
        string path, bool isLong, decimal open, decimal high, decimal low, decimal close, decimal risk)
    {
        var status = context.Status;
        var captured = context.CapturedAtUtc;
        var crossMarket = path is "ESResponseAlignedBreakout" or "ESWallRejectionReversal" or "ESExpectedMoveExpansion" or "ESGammaConcentrationResponse";
        var structureOnly = !crossMarket;
        if (status != "ModelReady" && !(structureOnly && context.EvidenceSchema >= 2 && status == "PriceReady")) return "OptionsGate:DataUnavailable";
        if (captured > decision) return "OptionsGate:FutureData";
        if (decision - captured > TimeSpan.FromSeconds(90)) return "OptionsGate:Stale";
        if (context.EvidenceSchema >= 2 && (context.UnderlyingObservedAtUtc is not { } observed || observed > decision || decision - observed > TimeSpan.FromSeconds(90)))
            return "OptionsGate:UnderlyingStale";
        if (!structureOnly || context.EvidenceSchema < 2)
        {
            if (context.EvidenceSchema >= 2 && (context.OptionQuotesObservedAtUtc is not { } quotes || quotes > decision || decision - quotes > TimeSpan.FromSeconds(90)))
                return "OptionsGate:OptionQuotesStale";
            if (context.AtmQuotesReady != true || context.LocalQuoteCoverage is null) return "OptionsGate:QuoteQualityUnavailable";
            if (context.LocalQuoteCoverage < 0.5m) return "OptionsGate:LocalQuotesIncomplete";
        }
        var concentrationPath = path is "ESWallRejectionReversal" or "ESGammaConcentrationResponse";
        if (concentrationPath && (context.CallQuoteCoverage is null || context.PutQuoteCoverage is null ||
            context.CallQuoteCoverage < 0.5m || context.PutQuoteCoverage < 0.5m)) return "OptionsGate:WallQuotesIncomplete";
        if (risk <= 0m || high <= low) return "OptionsGate:InvalidCandidate";
        var alignedBody = isLong ? close - open : open - close;
        var closeFraction = isLong ? (close - low) / (high - low) : (high - close) / (high - low);
        var breakout = path.StartsWith("Breakaway", StringComparison.Ordinal);
        var current = context.UnderlyingPrice;
        var esMove = current.HasValue && context.UnderlyingPrice5mAgo.HasValue
            ? current.Value - context.UnderlyingPrice5mAgo.Value
            : 0m;
        var fastMove = current.HasValue && context.UnderlyingPrice60sAgo.HasValue
            ? current.Value - context.UnderlyingPrice60sAgo.Value : 0m;
        var responseThreshold = Math.Max(0.5m, (context.ExpectedMoveProxy ?? 0m) * 0.05m);
        if (structureOnly && context.EvidenceSchema >= 2 &&
            (current is null || context.UnderlyingPrice5mAgo is null || context.Price5mReferenceAtUtc is not { } structuralReference ||
             captured - structuralReference < TimeSpan.FromSeconds(295) || captured - structuralReference > TimeSpan.FromSeconds(315)))
            return "OptionsGate:ESResponseWarmingUp";
        if (crossMarket)
        {
            if (!current.HasValue || context.Price5mReferenceAtUtc is not { } reference || context.UnderlyingPrice5mAgo is null ||
                context.PriceReferenceAtUtc is not { } fastReference || context.UnderlyingPrice60sAgo is null)
                return "OptionsGate:ESResponseWarmingUp";
            var referenceAge = context.CapturedAtUtc - reference;
            var fastAge = context.CapturedAtUtc - fastReference;
            if (referenceAge < TimeSpan.FromSeconds(295) || referenceAge > TimeSpan.FromSeconds(315) ||
                fastAge < TimeSpan.FromSeconds(55) || fastAge > TimeSpan.FromSeconds(70))
                return "OptionsGate:ESResponseReferenceInvalid";
            if (isLong ? fastMove <= -responseThreshold : fastMove >= responseThreshold)
                return "OptionsGate:ESFastResponseOpposesEntry";
            if (alignedBody <= 0m || closeFraction < 0.65m)
                return "OptionsGate:NqConfirmationWeak";
        }
        if (path == "ESResponseAlignedBreakout")
        {
            if (Math.Abs(esMove) < responseThreshold) return "OptionsGate:ESResponseTooSmall";
            if ((isLong && esMove < 0m) || (!isLong && esMove > 0m)) return "OptionsGate:ESResponseDisagreesWithBreakout";
        }
        if (path == "ESWallRejectionReversal")
        {
            var wall = context.EvidenceSchema >= 2 ? context.EventLevel : isLong ? context.PutWallProxy : context.CallWallProxy;
            if (context.EvidenceSchema >= 2 && !ValidLevelEvent(context, decision, "Reject")) return "OptionsGate:WallEventUnavailable";
            if (wall is null || !current.HasValue) return "OptionsGate:ESWallUnavailable";
            var distance = Math.Abs(current.Value - wall.Value);
            var reach = Math.Max(1m, (context.ExpectedMoveProxy ?? 0m) * 0.15m);
            if (distance > reach) return "OptionsGate:ESWallNotReached";
            if (context.EvidenceSchema < 2 && (isLong ? context.ResponseLow is null || context.ResponseLow > wall + 0.5m || current <= wall + 0.25m
                       : context.ResponseHigh is null || context.ResponseHigh < wall - 0.5m || current >= wall - 0.25m)
                || context.EvidenceSchema >= 2 && (isLong ? current <= wall + .25m : current >= wall - .25m))
                return "OptionsGate:ESWallTouchNotReclaimed";
            if (Math.Abs(esMove) < responseThreshold || (isLong && esMove <= 0m) || (!isLong && esMove >= 0m)) return "OptionsGate:ESWallNoRejection";
        }
        if (path == "ESExpectedMoveExpansion")
        {
            var anchor = context.ExpectedMoveAnchor;
            var expected = context.ExpectedMoveAtAnchor;
            if (anchor is null || expected is not > 0m || !current.HasValue) return "OptionsGate:ExpectedMoveUnavailable";
            if (context.EvidenceSchema >= 2 && (context.AnchorEstablishedAtUtc is not { } established || established > decision ||
                context.AnchorTradingDay != OptionsProxyExpiry.ExchangeDate(decision) || string.IsNullOrEmpty(context.AnchorSeries)))
                return "OptionsGate:ExpectedMoveAnchorInvalid";
            var displacement = current.Value - anchor.Value;
            if (Math.Abs(displacement) < expected.Value * 0.75m) return "OptionsGate:ExpectedMoveNotExpanded";
            if (Math.Abs(displacement) > expected.Value * 1.25m) return "OptionsGate:ExpectedMoveOverextended";
            if ((isLong && displacement <= 0m) || (!isLong && displacement >= 0m)) return "OptionsGate:ExpectedMoveDirectionDisagrees";
            if (Math.Abs(esMove) < responseThreshold || (isLong ? esMove <= 0m : esMove >= 0m)) return "OptionsGate:ExpectedMoveNoFollowThrough";
        }
        if (path == "ESGammaConcentrationResponse")
        {
            if (context.EvidenceSchema >= 2 && !ValidLevelEvent(context, decision, null)) return "OptionsGate:GammaEventUnavailable";
            if (context.EvidenceSchema >= 2 && (isLong ? current <= context.EventLevel + .25m : current >= context.EventLevel - .25m))
                return "OptionsGate:GammaResponseLost";
            if (context.GammaConcentrationStrike is null || !current.HasValue || context.GammaConcentrationShare < 0.18m) return "OptionsGate:GammaConcentrationInsufficient";
            var distance = Math.Abs(current.Value - (context.EventLevel ?? context.GammaConcentrationStrike.Value));
            if (distance > Math.Max(1m, (context.ExpectedMoveProxy ?? 0m) * 0.2m)) return "OptionsGate:GammaConcentrationNotNear";
            if (Math.Abs(esMove) < responseThreshold || (isLong && esMove <= 0m) || (!isLong && esMove >= 0m)) return "OptionsGate:GammaConcentrationNoResponse";
        }
        if (breakout && Math.Abs(esMove) >= responseThreshold)
        {
            var responseAligned = (isLong && esMove > 0m) || (!isLong && esMove < 0m);
            if (!responseAligned)
                return "OptionsGate:ESResponseDisagreesWithBreakout";
        }
        return null;
    }

    private static bool ValidLevelEvent(EsOptionsProxySnapshot context, DateTime decision, string? kind) =>
        context.EventLevel is > 0m && context.EventTouchedAtUtc is { } touched && touched < decision &&
        decision - touched <= TimeSpan.FromMinutes(5) &&
        (kind is null ? context.EventKind is "CrossHold" or "Reject" : context.EventKind == kind);
}
