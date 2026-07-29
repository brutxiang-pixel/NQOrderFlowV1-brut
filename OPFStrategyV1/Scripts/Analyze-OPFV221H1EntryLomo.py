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
    lane: str
    side: str
    path: str
    metric: str
    threshold: float

    @property
    def name(self) -> str:
        return f"{self.lane}_{self.side}_{self.path}_{self.metric}_{self.threshold:g}"

    def blocks(self, row, lane: str) -> bool:
        if lane != self.lane or row.Side != self.side or row.ResearchPath != self.path:
            return False
        if self.metric == "MinQuality":
            return float(row.SetupQualityScore) < self.threshold
        if self.metric == "MaxRisk":
            return float(row.ExactRisk) > self.threshold
        if self.metric == "MinRisk":
            return float(row.ExactRisk) < self.threshold
        raise ValueError(self.metric)


def load_tape(path: Path):
    tape = pd.read_csv(path, low_memory=False)
    for column in ("EntryTime", "PredictedExitTime", "TradingDate"):
        tape[column] = pd.to_datetime(tape[column])
    submissions = pd.read_csv(path.with_name("submission_trace.csv"), low_memory=False)
    aborted = submissions[submissions["SubmissionStatus"].eq("PreflightAborted")]
    abort_keys = frozenset(
        aborted["SignalID"] + "|" + aborted["ResearchPath"] + "|" + aborted["Lane"]
    )
    return tape, abort_keys


def metrics(trades: pd.DataFrame) -> dict:
    dates = pd.to_datetime(trades["TradingDate"])
    daily = trades.assign(Date=dates.dt.strftime("%Y-%m-%d")).groupby("Date")["Net"].sum()
    weekly = trades.assign(Week=dates.dt.to_period("W-SUN").astype(str)).groupby("Week")["Net"].sum()
    monthly = trades.assign(Month=dates.dt.strftime("%Y-%m")).groupby("Month")["Net"].sum()
    gross = trades["Gross"]
    wins = float(gross[gross > 0].sum())
    losses = -float(gross[gross < 0].sum())
    drawdown = daily.cumsum().cummax() - daily.cumsum()
    return {
        "Trades": len(trades),
        "Gross": round(float(gross.sum()), 2),
        "Net": round(float(trades["Net"].sum()), 2),
        "PF": round(wins / losses, 4),
        "PositiveWeeks": int(weekly.gt(0).sum()),
        "WorstWeek": round(float(weekly.min()), 2),
        "MaxDD": round(float(drawdown.max()), 2),
        "PositiveMonths": int(monthly.gt(0).sum()),
        "Monthly": monthly,
    }


def make_rules(baseline: pd.DataFrame) -> list[Rule]:
    groups = baseline.groupby(["Lane", "Side", "ResearchPath"], as_index=False).agg(
        Trades=("Net", "size"), Net=("Net", "sum")
    )
    groups = groups[
        groups["Lane"].eq("Primary")
        & ~groups["ResearchPath"].str.startswith("Breakaway")
        & (groups["Trades"].ge(20) | groups["Net"].lt(0))
    ]
    rules = []
    for lane, side, path, _, _ in groups.itertuples(index=False):
        for threshold in (40, 45, 50, 55, 60, 65, 70, 75, 80):
            rules.append(Rule(lane, side, path, "MinQuality", threshold))
        for threshold in (8, 10, 12, 15, 18, 20, 22, 25):
            rules.append(Rule(lane, side, path, "MaxRisk", threshold))
        for threshold in (6, 8, 10, 12):
            rules.append(Rule(lane, side, path, "MinRisk", threshold))
    return rules


def simulate(v220, connector, tape, abort_keys, rules):
    gate = None if not rules else lambda row, lane: any(rule.blocks(row, lane) for rule in rules)
    return v220.simulate(
        tape,
        connector,
        v220.Policy(zone_birth_min_quality=45.0),
        abort_keys,
        candidate_gate=gate,
    )[0]


def row_for(name, rules, h1, july, baseline_h1, baseline_july):
    h = metrics(h1)
    j = metrics(july)
    baseline_ids = set(
        zip(baseline_h1.SignalID, baseline_h1.ResearchPath, baseline_h1.Lane)
    )
    ids = set(zip(h1.SignalID, h1.ResearchPath, h1.Lane))
    monthly_delta = h["Monthly"].sub(metrics(baseline_h1)["Monthly"], fill_value=0)
    return {
        "Rule": name,
        "RuleCount": len(rules),
        "H1Trades": h["Trades"],
        "H1Gross": h["Gross"],
        "H1Net": h["Net"],
        "H1NetDelta": round(h["Net"] - metrics(baseline_h1)["Net"], 2),
        "H1PF": h["PF"],
        "H1PositiveWeeks": h["PositiveWeeks"],
        "H1WorstWeek": h["WorstWeek"],
        "H1MaxDD": h["MaxDD"],
        "H1PositiveMonths": h["PositiveMonths"],
        "PositiveDeltaMonths": int(monthly_delta.gt(0).sum()),
        "NegativeDeltaMonths": int(monthly_delta.lt(0).sum()),
        "ChangedIdentities": len(baseline_ids.symmetric_difference(ids)),
        "JulyTrades": j["Trades"],
        "JulyNet": j["Net"],
        "JulyNetDelta": round(j["Net"] - metrics(baseline_july)["Net"], 2),
    }


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

    baseline_h1 = simulate(v220, connector, h1_tape, h1_abort, [])
    baseline_july = simulate(v220, connector, july_tape, july_abort, [])
    baseline = row_for("V221", [], baseline_h1, baseline_july, baseline_h1, baseline_july)
    if baseline["H1Trades"] != 878 or baseline["H1Gross"] != 26435.12:
        raise RuntimeError(f"v2.21 baseline mismatch: {baseline}")

    single_rows = []
    single_trades = {}
    rules = make_rules(baseline_h1)
    for rule in rules:
        h1 = simulate(v220, connector, h1_tape, h1_abort, [rule])
        july = simulate(v220, connector, july_tape, july_abort, [rule])
        row = row_for(rule.name, [rule], h1, july, baseline_h1, baseline_july)
        if row["ChangedIdentities"] >= 10:
            single_rows.append(row)
            single_trades[rule.name] = (rule, h1)

    singles = pd.DataFrame(single_rows).sort_values(
        ["H1Net", "H1PF", "H1MaxDD"], ascending=[False, False, True]
    )
    eligible = singles[
        singles["H1NetDelta"].gt(0)
        & singles["H1PF"].ge(baseline["H1PF"])
        & singles["H1WorstWeek"].ge(baseline["H1WorstWeek"] - 100)
        & singles["H1MaxDD"].le(baseline["H1MaxDD"])
        & singles["PositiveDeltaMonths"].ge(4)
        & singles["JulyNetDelta"].ge(-250)
    ].head(12)

    pair_rows = []
    names = eligible["Rule"].tolist()
    for i, left_name in enumerate(names):
        for right_name in names[i + 1 :]:
            left = single_trades[left_name][0]
            right = single_trades[right_name][0]
            if (left.lane, left.side, left.path) == (right.lane, right.side, right.path):
                continue
            pair = [left, right]
            h1 = simulate(v220, connector, h1_tape, h1_abort, pair)
            july = simulate(v220, connector, july_tape, july_abort, pair)
            pair_rows.append(
                row_for(f"{left.name}+{right.name}", pair, h1, july, baseline_h1, baseline_july)
            )
    pairs = pd.DataFrame(pair_rows)
    if not pairs.empty:
        pairs = pairs.sort_values(["H1Net", "H1PF", "H1MaxDD"], ascending=[False, False, True])

    args.output_dir.mkdir(parents=True, exist_ok=True)
    pd.DataFrame([baseline]).to_csv(args.output_dir / "baseline.csv", index=False)
    singles.to_csv(args.output_dir / "single_rules.csv", index=False)
    pairs.to_csv(args.output_dir / "pair_rules.csv", index=False)
    print("BASELINE")
    print(pd.DataFrame([baseline]).to_string(index=False))
    print("TOP SINGLES")
    print(singles.head(20).to_string(index=False))
    print("TOP PAIRS")
    print(pairs.head(20).to_string(index=False) if not pairs.empty else "none")


if __name__ == "__main__":
    main()
