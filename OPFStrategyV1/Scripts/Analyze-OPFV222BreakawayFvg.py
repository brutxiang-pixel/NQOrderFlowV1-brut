import argparse
import importlib.util
from dataclasses import dataclass
from pathlib import Path

import pandas as pd


def load_module(path: Path, name: str):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


@dataclass(frozen=True)
class Rule:
    min_quality: float | None
    min_risk: float | None
    min_regime: float | None

    @property
    def name(self) -> str:
        quality = "Off" if self.min_quality is None else f"Q{self.min_quality:g}"
        risk = "Off" if self.min_risk is None else f"R{self.min_risk:g}"
        regime = "Off" if self.min_regime is None else f"G{self.min_regime:g}"
        return f"Primary_BreakawayShort_{quality}_{risk}_{regime}"

    def blocks(self, row, lane: str) -> bool:
        if lane != "Primary" or row.Side != "Short" or row.ResearchPath != "BreakawayFvg":
            return False
        return (
            (self.min_quality is not None and float(row.SetupQualityScore) < self.min_quality)
            or (self.min_risk is not None and float(row.ExactRisk) < self.min_risk)
            or (self.min_regime is not None and float(row.RegimeScore) < self.min_regime)
        )


def load_tape(path: Path):
    tape = pd.read_csv(path, low_memory=False)
    for column in ("EntryTime", "PredictedExitTime", "TradingDate"):
        tape[column] = pd.to_datetime(tape[column])
    submissions = pd.read_csv(path.with_name("submission_trace.csv"), low_memory=False)
    aborted = submissions[submissions["SubmissionStatus"].eq("PreflightAborted")]
    abort_keys = frozenset(aborted["SignalID"] + "|" + aborted["ResearchPath"] + "|" + aborted["Lane"])
    return tape, abort_keys


def metrics(trades: pd.DataFrame) -> dict:
    daily = trades.assign(Date=trades["TradingDate"].dt.strftime("%Y-%m-%d")).groupby("Date")["Net"].sum()
    weekly = trades.assign(Week=trades["TradingDate"].dt.to_period("W-SUN").astype(str)).groupby("Week")["Net"].sum()
    monthly = trades.assign(Month=trades["TradingDate"].dt.strftime("%Y-%m")).groupby("Month")["Net"].sum()
    wins = float(trades.loc[trades["Gross"] > 0, "Gross"].sum())
    losses = -float(trades.loc[trades["Gross"] < 0, "Gross"].sum())
    drawdown = daily.cumsum().cummax() - daily.cumsum()
    return {
        "Trades": len(trades),
        "Gross": round(float(trades["Gross"].sum()), 2),
        "Net": round(float(trades["Net"].sum()), 2),
        "PF": round(wins / losses, 4),
        "PositiveWeeks": int(weekly.gt(0).sum()),
        "WorstWeek": round(float(weekly.min()), 2),
        "MaxDD": round(float(drawdown.max()), 2),
        "Monthly": monthly,
    }


def simulate(v220, connector, tape, abort_keys, rule: Rule):
    return v220.simulate(
        tape,
        connector,
        v220.Policy(zone_birth_min_quality=45.0),
        abort_keys,
        candidate_gate=rule.blocks,
    )[0]


def result_row(rule, h1, july, base_h1, base_july):
    current = metrics(h1)
    baseline = metrics(base_h1)
    july_current = metrics(july)
    july_baseline = metrics(base_july)
    delta_monthly = current["Monthly"].sub(baseline["Monthly"], fill_value=0)
    base_ids = set(zip(base_h1.SignalID, base_h1.ResearchPath, base_h1.Lane))
    current_ids = set(zip(h1.SignalID, h1.ResearchPath, h1.Lane))
    row = {
        "Rule": rule.name,
        "MinQuality": rule.min_quality,
        "MinRisk": rule.min_risk,
        "MinRegime": rule.min_regime,
        "H1Trades": current["Trades"],
        "H1Gross": current["Gross"],
        "H1Net": current["Net"],
        "H1NetDelta": round(current["Net"] - baseline["Net"], 2),
        "H1PF": current["PF"],
        "H1PositiveWeeks": current["PositiveWeeks"],
        "H1WorstWeek": current["WorstWeek"],
        "H1MaxDD": current["MaxDD"],
        "PositiveDeltaMonths": int(delta_monthly.gt(0).sum()),
        "NegativeDeltaMonths": int(delta_monthly.lt(0).sum()),
        "ChangedIdentities": len(base_ids.symmetric_difference(current_ids)),
        "JulyTrades": july_current["Trades"],
        "JulyNet": july_current["Net"],
        "JulyNetDelta": round(july_current["Net"] - july_baseline["Net"], 2),
    }
    for month in ("2026-01", "2026-02", "2026-03", "2026-04", "2026-05", "2026-06"):
        row[f"Delta_{month}"] = round(float(delta_monthly.get(month, 0.0)), 2)
    return row


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--h1-tape", type=Path, required=True)
    parser.add_argument("--july-tape", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    args = parser.parse_args()

    scripts = Path(__file__).parent
    v220 = load_module(scripts / "Analyze-OPFV220H1LatchedProfitResearch.py", "v220")
    connector = load_module(scripts / "Analyze-OPFV217DualSlotCalibration.py", "connector")
    h1_tape, h1_abort = load_tape(args.h1_tape)
    july_tape, july_abort = load_tape(args.july_tape)
    baseline_rule = Rule(None, None, None)
    baseline_h1 = simulate(v220, connector, h1_tape, h1_abort, baseline_rule)
    baseline_july = simulate(v220, connector, july_tape, july_abort, baseline_rule)
    baseline = result_row(baseline_rule, baseline_h1, baseline_july, baseline_h1, baseline_july)
    if baseline["H1Trades"] != 878 or baseline["H1Gross"] != 26435.12:
        raise RuntimeError(f"v2.21 baseline mismatch: {baseline}")

    rules = [
        Rule(quality, risk, regime)
        for quality in (None, 88.0, 90.0, 92.0, 95.0)
        for risk in (None, 8.0, 10.0, 12.0)
        for regime in (None, 70.0, 80.0, 90.0)
        if any(value is not None for value in (quality, risk, regime))
    ]
    rows = []
    for rule in rules:
        h1 = simulate(v220, connector, h1_tape, h1_abort, rule)
        july = simulate(v220, connector, july_tape, july_abort, rule)
        rows.append(result_row(rule, h1, july, baseline_h1, baseline_july))

    result = pd.DataFrame(rows).sort_values(["H1Net", "H1PF", "H1MaxDD"], ascending=[False, False, True])
    screened = result[
        result["H1NetDelta"].ge(500.0)
        & result["H1PF"].ge(baseline["H1PF"])
        & result["H1WorstWeek"].ge(baseline["H1WorstWeek"] - 100.0)
        & result["H1MaxDD"].le(baseline["H1MaxDD"])
        & result["PositiveDeltaMonths"].ge(4)
        & result["NegativeDeltaMonths"].le(2)
        & result["JulyNetDelta"].ge(-250.0)
    ]

    args.output_dir.mkdir(parents=True, exist_ok=True)
    pd.DataFrame([baseline]).to_csv(args.output_dir / "baseline.csv", index=False)
    result.to_csv(args.output_dir / "grid.csv", index=False)
    screened.to_csv(args.output_dir / "screened.csv", index=False)
    print("BASELINE")
    print(pd.DataFrame([baseline]).to_string(index=False))
    print("TOP")
    print(result.head(20).to_string(index=False))
    print("SCREENED")
    print(screened.to_string(index=False) if not screened.empty else "none")


if __name__ == "__main__":
    main()
