// Strategy/NQOrderFlowStrategy.QualityAndStops.cs
using ATAS.DataFeedsCore;
using ATAS.Indicators;
using ATAS.Strategies;
using ATAS.Strategies.Chart;
using NQOrderFlowV1.Engines;
using NQOrderFlowV1.Models;
using NQOrderFlowV1.Services;
using NQOrderFlowV1.Zones;
using OFT.Rendering.Context;
using OFT.Rendering.Tools;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace NQOrderFlowV1.Strategy
{
    public partial class NQOrderFlowStrategy
    {
        // =========================
        // Zone Quality Score (0-10)
        // =========================
        private bool TryEvaluateZoneQualityScore(int bar, TradingZone z, out int score, out string detail)
        {
            score = 0;
            detail = "-";

            var stateScore =
                !z.IsTouched ? 2 :
                (z.IsTouched && !z.IsMitigated ? 1 : 0);

            var width = Math.Abs(z.High - z.Low);
            var idealMin = Math.Max(0m, QualityIdealWidthMinPoints);
            var idealMax = Math.Max(idealMin, QualityIdealWidthMaxPoints);

            var widthScore =
                (width >= idealMin && width <= idealMax) ? 3 :
                (width >= idealMin * 0.5m && width <= idealMax * 2m) ? 2 :
                0;

            var strongBars = Math.Max(1, QualityFreshStrongBars);
            var okBars = Math.Max(strongBars, QualityFreshOkBars);

            var ageBars = z.CreatedBar >= 0 ? Math.Max(0, bar - z.CreatedBar) : int.MaxValue;

            var freshScore =
                ageBars <= strongBars ? 2 :
                ageBars <= okBars ? 1 :
                0;

            var vpScore = 0;

            if (TryGetNearestVpRefDistanceToZone(z, out string vpRefName, out decimal vpRefPrice, out decimal vpDist))
            {
                var strongD = Math.Max(0m, QualityVpStrongDistPoints);
                var okD = Math.Max(strongD, QualityVpOkDistPoints);
                var weakD = Math.Max(okD, QualityVpWeakDistPoints);

                vpScore =
                    vpDist <= strongD ? 3 :
                    vpDist <= okD ? 2 :
                    vpDist <= weakD ? 1 :
                    0;
            }
            else
            {
                vpScore = 1;
            }

            score = stateScore + widthScore + freshScore + vpScore;

            if (score < 0) score = 0;
            if (score > 10) score = 10;

            detail =
                $"Q={score}/10 | state={stateScore}/2({(z.IsMitigated ? "Mitigated" : (z.IsTouched ? "Touched" : "Untouched"))})" +
                $" | width={width:0.00}pt->{widthScore}/3(ideal {idealMin:0.##}-{idealMax:0.##})" +
                $" | age={ageBars}b->{freshScore}/2(<= {strongBars}/{okBars})" +
                $" | vp={vpRefName}@{(vpRefPrice == 0m ? "-" : vpRefPrice.ToString("0.00"))} dist={vpDist:0.00}pt->{vpScore}/3";

            return true;
        }

        private bool TryGetNearestVpRefDistanceToZone(TradingZone z, out string refName, out decimal refPrice, out decimal dist)
        {
            refName = "-";
            refPrice = 0m;
            dist = 0m;

            var vp = _lastClosedVpCandle;
            if (vp is null)
                return false;

            var refs = new List<(string Name, decimal Price)>(4);
            if (vp.POC != 0m) refs.Add(("POC", vp.POC));
            if (vp.VAH != 0m) refs.Add(("VAH", vp.VAH));
            if (vp.VAL != 0m) refs.Add(("VAL", vp.VAL));
            if (vp.VWAP != 0m) refs.Add(("VWAP", vp.VWAP));

            if (refs.Count == 0)
                return false;

            decimal distToZone(decimal p) => GetDistanceToRangePoints(p, z.Low, z.High);

            var best = refs[0];
            var bestDist = distToZone(best.Price);

            for (var i = 1; i < refs.Count; i++)
            {
                var d = distToZone(refs[i].Price);
                if (d < bestDist)
                {
                    best = refs[i];
                    bestDist = d;
                }
            }

            refName = best.Name;
            refPrice = best.Price;
            dist = bestDist;
            return true;
        }

        /// <summary>
        /// Stage B Live context readiness check.
        /// - Connector 允许为 null（你已在 Binance/Replay 验证 OpenOrderAsync 仍可能可用）
        /// - Replay 环境下，Strategy.State 可能显示为 Stopped，但 OnCalculate/下单仍可工作；
        ///   因此：若 Portfolio 显示为 Replay，则允许 State!=Started。
        /// </summary>
        private bool IsLiveContextReady(out string why)
        {
            if (Portfolio is null)
            {
                why = "Portfolio=null";
                return false;
            }

            if (Security is null)
            {
                why = "Security=null";
                return false;
            }

            var portText = Portfolio.ToString() ?? string.Empty;
            var isReplayPortfolio = portText.Contains("Replay", StringComparison.OrdinalIgnoreCase);

            if (State != StrategyStates.Started)
            {
                if (isReplayPortfolio)
                {
                    why = $"OK(State={State} allowed for Replay)";
                    return true;
                }

                why = $"State={State} (need Started)";
                return false;
            }

            if (Connector is null)
            {
                why = "OK(Connector=null allowed)";
                return true;
            }

            why = "OK";
            return true;
        }

        private bool IsZoneTradeableByQuality(int bar, TradingZone z, out int qScore, out string qDetail)
        {
            qScore = -1;
            qDetail = "-";

            if (DisallowMitigatedZones && z.IsMitigated)
            {
                qScore = 0;
                qDetail = $"区块质量：Mitigated且禁止交易 | {z.ToShortText()}";
                return false;
            }

            if (!EnableZoneQualityScoreFilter)
                return true;

            _ = TryEvaluateZoneQualityScore(bar, z, out qScore, out qDetail);

            var min = Math.Max(0, Math.Min(10, MinZoneQualityScore));
            return qScore >= min;
        }

        // =========================
        // Stop selection：Swing -> Rolling(adaptive) -> Zone
        // =========================
        private enum StopSelectFailReason
        {
            Invalid,
            TooSmall,
            TooLarge,
            MixedOutOfRange
        }

        private readonly record struct StopEval(
            decimal Stop,
            string Source,
            decimal RiskPoints,
            int RiskTicks,
            bool InRange,
            bool IsValid);

        private bool TrySelectStop(
            int bar,
            bool isLong,
            decimal entry,
            TradingZone zone,
            bool usedShadow,
            out decimal stop,
            out string stopSource,
            out int riskTicks,
            out decimal riskPoints,
            out StopSelectFailReason failReason,
            out string failDetail)
        {
            EnsureInstrumentRulesInitialized(bar, "TrySelectStop");

            stop = 0m;
            stopSource = "-";
            riskTicks = 0;
            riskPoints = 0m;
            failReason = StopSelectFailReason.Invalid;
            failDetail = "-";

            var tick = GetTickSize(bar, "TrySelectStop.tick");
            if (tick <= 0m)
                tick = 0.01m;

            var minTicks = Math.Max(0, MinRiskTicks);
            var maxTicks = Math.Max(minTicks, MaxRiskTicks);

            bool IsValidSide(decimal s)
                => isLong ? (s <= entry - tick) : (s >= entry + tick);

            StopEval EvalCandidate(decimal rawStop, string rawSource)
            {
                var alignedStop = AlignStopToTick(rawStop, isLong, bar);

                if (!IsValidSide(alignedStop))
                {
                    return new StopEval(alignedStop, rawSource + " (InvalidSide)", 0m, 0, false, false);
                }

                var rp = Math.Abs(entry - alignedStop);
                if (rp < tick)
                {
                    return new StopEval(alignedStop, rawSource + " (TooClose)", rp, 0, false, false);
                }

                var rt = PointsToTicksRound(rp, bar);
                var ok = rt >= minTicks && rt <= maxTicks;

                return new StopEval(alignedStop, rawSource, rp, rt, ok, true);
            }

            static int DistToRange(int rt, int min, int max)
            {
                if (rt <= 0) return int.MaxValue / 2;
                if (rt < min) return min - rt;
                if (rt > max) return rt - max;
                return 0;
            }

            var bufferPoints = GetSlBufferPoints(bar);

            var zoneStopRaw = GetFallbackStopFromZone(isLong, zone, bufferPoints);
            var zoneSrc = usedShadow ? "ZoneStop(Shadow)" : "ZoneStop";
            zoneSrc += isLong ? $"@{zone.Low:0.########}" : $"@{zone.High:0.########}";
            var zoneEval = EvalCandidate(zoneStopRaw, zoneSrc);

            StopEval? swingEval = null;
            if (_lastM5StructureSnapshot is not null)
            {
                if (isLong && _lastM5StructureSnapshot.LastSwingLow is not null)
                {
                    var sl = _lastM5StructureSnapshot.LastSwingLow.Price;
                    var raw = sl - bufferPoints;
                    var src = (usedShadow ? "M5SL(Shadow)" : "M5SL") + $"@{sl:0.########}";
                    var ev = EvalCandidate(raw, src);
                    if (ev.IsValid) swingEval = ev;
                }
                else if (!isLong && _lastM5StructureSnapshot.LastSwingHigh is not null)
                {
                    var sh = _lastM5StructureSnapshot.LastSwingHigh.Price;
                    var raw = sh + bufferPoints;
                    var src = (usedShadow ? "M5SH(Shadow)" : "M5SH") + $"@{sh:0.########}";
                    var ev = EvalCandidate(raw, src);
                    if (ev.IsValid) swingEval = ev;
                }
            }

            StopEval? rollingSelected = null;
            StopEval? rollingBest = null;
            string rollingTriedText = "-";

            if (EnableRollingStop)
            {
                var maxLb = Math.Max(2, RollingStopLookbackBars);
                var minLb = Math.Max(2, RollingStopMinLookbackBars);
                if (minLb > maxLb) minLb = maxLb;

                rollingTriedText = $"{maxLb}->{minLb}";

                var rollingEvals = new List<StopEval>(capacity: Math.Max(1, maxLb - minLb + 1));

                for (var lb = maxLb; lb >= minLb; lb--)
                {
                    if (isLong)
                    {
                        if (!TryGetRollingLow(bar, lb, out var rl, out var rlBar))
                            continue;

                        var raw = rl - bufferPoints;
                        var src = $"RollingStop RL{lb}@{rl:0.########}(bar={rlBar})";
                        var ev = EvalCandidate(raw, src);

                        if (ev.IsValid) rollingEvals.Add(ev);
                        if (ev.InRange) { rollingSelected = ev; break; }
                    }
                    else
                    {
                        if (!TryGetRollingHigh(bar, lb, out var rh, out var rhBar))
                            continue;

                        var raw = rh + bufferPoints;
                        var src = $"RollingStop RH{lb}@{rh:0.########}(bar={rhBar})";
                        var ev = EvalCandidate(raw, src);

                        if (ev.IsValid) rollingEvals.Add(ev);
                        if (ev.InRange) { rollingSelected = ev; break; }
                    }
                }

                if (rollingSelected is not null)
                    rollingBest = rollingSelected;
                else if (rollingEvals.Count > 0)
                {
                    rollingBest = rollingEvals
                        .OrderBy(e => DistToRange(e.RiskTicks, minTicks, maxTicks))
                        .ThenBy(e => e.RiskTicks)
                        .First();
                }
            }

            if (swingEval is not null && swingEval.Value.InRange)
            {
                stop = swingEval.Value.Stop;
                stopSource = swingEval.Value.Source;
                riskTicks = swingEval.Value.RiskTicks;
                riskPoints = swingEval.Value.RiskPoints;
                return true;
            }

            if (rollingSelected is not null && rollingSelected.Value.InRange)
            {
                stop = rollingSelected.Value.Stop;
                stopSource = rollingSelected.Value.Source;
                riskTicks = rollingSelected.Value.RiskTicks;
                riskPoints = rollingSelected.Value.RiskPoints;
                return true;
            }

            if (zoneEval.InRange)
            {
                stop = zoneEval.Stop;
                stopSource = zoneEval.Source;
                riskTicks = zoneEval.RiskTicks;
                riskPoints = zoneEval.RiskPoints;
                return true;
            }
            var evals = new List<StopEval>(capacity: 3);
            if (swingEval is not null) evals.Add(swingEval.Value);
            if (rollingBest is not null) evals.Add(rollingBest.Value);
            if (zoneEval.IsValid) evals.Add(zoneEval);
            if (evals.Count == 0)
            {
                failReason = StopSelectFailReason.Invalid;
                failDetail = $"Stop选择失败 | min={minTicks}t max={maxTicks}t | entry={entry:0.########} | 无有效候选(stop在反面或过近)";
                return false;
            }

            var allTooSmall = evals.All(e => e.RiskTicks > 0 && e.RiskTicks < minTicks);
            var allTooLarge = evals.All(e => e.RiskTicks > 0 && e.RiskTicks > maxTicks);
            if (allTooSmall)
            {
                // 兜底：取风险最大的候选（最接近 MinRiskTicks）
                var best = evals.Where(e => e.RiskTicks > 0).OrderByDescending(e => e.RiskTicks).First();
                stop = AlignStopToTick(best.Stop, isLong, bar);
                stopSource = best.Source + "(Fallback-TooSmall)";
                riskTicks = best.RiskTicks;
                riskPoints = best.RiskPoints;
                AppendLog($"TRY_SELECT_STOP bar={bar} src={stopSource} stop={stop:0.########} risk={riskTicks}t ALL_CANDIDATES_TOO_SMALL_USE_FALLBACK");
                return true;
            }

            if (allTooLarge)
            {
                failReason = StopSelectFailReason.TooLarge;
                string fmtL(StopEval e) => $"{e.Source}: stop={e.Stop:0.########} risk={(e.RiskTicks == 0 ? "-" : e.RiskTicks.ToString())}t({e.RiskPoints:0.########}pt)";
                failDetail = $"Stop选择失败 | min={minTicks}t max={maxTicks}t | entry={entry:0.########} | rollingTried={rollingTriedText} | {string.Join(" | ", evals.Select(fmtL))}";
                return false;
            }
            failReason = StopSelectFailReason.MixedOutOfRange;
            string fmt2(StopEval e) => $"{e.Source}: stop={e.Stop:0.########} risk={(e.RiskTicks == 0 ? "-" : e.RiskTicks.ToString())}t({e.RiskPoints:0.########}pt)";
            failDetail = $"Stop选择失败 | min={minTicks}t max={maxTicks}t | entry={entry:0.########} | rollingTried={rollingTriedText} | {string.Join(" | ", evals.Select(fmt2))}";
            return false;
        }

        private static decimal GetFallbackStopFromZone(bool isLong, TradingZone zone, decimal bufferPoints)
            => isLong ? zone.Low - bufferPoints : zone.High + bufferPoints;
}
}
