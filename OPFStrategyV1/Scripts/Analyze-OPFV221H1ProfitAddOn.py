import argparse
import importlib.util
from dataclasses import dataclass
from pathlib import Path

import pandas as pd


ADDON_COMMISSION = 1.2


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
    min_quality: float

    @property
    def name(self):
        return f"{self.lane}_{self.side}_{self.path}_Q{self.min_quality:g}"

    def matches(self, row, lane):
        return (
            lane == self.lane
            and row.Side == self.side
            and row.ResearchPath == self.path
            and float(row.SetupQualityScore) >= self.min_quality
        )


def load_tape(path: Path):
    tape = pd.read_csv(path, low_memory=False)
    for column in ("EntryTime", "PredictedExitTime", "TradingDate"):
        tape[column] = pd.to_datetime(tape[column])
    submissions = pd.read_csv(path.with_name("submission_trace.csv"), low_memory=False)
    aborted = submissions[submissions["SubmissionStatus"].eq("PreflightAborted")]
    keys = frozenset(
        aborted["SignalID"] + "|" + aborted["ResearchPath"] + "|" + aborted["Lane"]
    )
    return tape, keys


def addon_gross(row):
    entry_bar = float(row.EntryBar)
    first_r_absolute = pd.to_numeric(row.First1RBar, errors="coerce")
    exit_bar = pd.to_numeric(row.PredictedExitRelativeBar, errors="coerce")
    if pd.isna(first_r_absolute) or pd.isna(exit_bar):
        return None
    first_r = float(first_r_absolute) - entry_bar
    if first_r >= exit_bar:
        return None
    risk = float(row.ExactRisk)
    break_even_absolute = pd.to_numeric(row.FirstBreakEvenAfter1RBar, errors="coerce")
    break_even = (
        float(break_even_absolute) - entry_bar
        if not pd.isna(break_even_absolute)
        else None
    )
    if break_even is not None and first_r <= break_even <= exit_bar:
        return -risk * 2.0
    parent_move = float(row.PredictedGross) / 3.0 / 2.0
    return (parent_move - risk) * 2.0


def adjuster(rule: Rule):
    def apply(row, lane, trade):
        if not rule.matches(row, lane):
            return
        gross = addon_gross(row)
        if gross is None:
            return
        trade["Gross"] += gross
        trade["Net"] += gross - ADDON_COMMISSION
        trade["AddonGross"] = gross
    return apply


def run(v220, connector, tape, abort_keys, rule=None):
    return v220.simulate(
        tape,
        connector,
        v220.Policy(zone_birth_min_quality=45.0),
        abort_keys,
        trade_adjuster=None if rule is None else adjuster(rule),
    )[0]


def summarize(trades):
    dates = pd.to_datetime(trades.TradingDate)
    daily = trades.assign(Date=dates.dt.strftime("%Y-%m-%d")).groupby("Date").Net.sum()
    weekly = trades.assign(Week=dates.dt.to_period("W-SUN").astype(str)).groupby("Week").Net.sum()
    monthly = trades.assign(Month=dates.dt.strftime("%Y-%m")).groupby("Month").Net.sum()
    wins = float(trades.loc[trades.Gross > 0, "Gross"].sum())
    losses = -float(trades.loc[trades.Gross < 0, "Gross"].sum())
    drawdown = daily.cumsum().cummax() - daily.cumsum()
    addon = trades[trades.get("AddonGross", pd.Series(index=trades.index, dtype=float)).notna()]
    return {
        "Trades": len(trades),
        "Gross": round(float(trades.Gross.sum()), 2),
        "Net": round(float(trades.Net.sum()), 2),
        "PF": round(wins / losses, 4),
        "PositiveWeeks": int(weekly.gt(0).sum()),
        "WorstWeek": round(float(weekly.min()), 2),
        "MaxDD": round(float(drawdown.max()), 2),
        "PositiveMonths": int(monthly.gt(0).sum()),
        "Monthly": monthly,
        "AddOns": len(addon),
        "AddOnGross": round(float(addon.get("AddonGross", pd.Series(dtype=float)).sum()), 2),
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
    base_h1 = run(v220, connector, h1_tape, h1_abort)
    base_july = run(v220, connector, july_tape, july_abort)
    bh = summarize(base_h1)
    bj = summarize(base_july)
    if bh["Trades"] != 878 or bh["Gross"] != 26435.12:
        raise RuntimeError(f"v2.21 baseline mismatch: {bh}")

    groups = base_h1.groupby(["Lane", "Side", "ResearchPath"]).size().reset_index(name="Trades")
    groups = groups[groups.Trades.ge(15)]
    rules = [
        Rule(row.Lane, row.Side, row.ResearchPath, quality)
        for row in groups.itertuples(index=False)
        for quality in (50.0, 60.0, 70.0, 80.0, 90.0)
    ]
    rows = []
    for rule in rules:
        h1 = run(v220, connector, h1_tape, h1_abort, rule)
        july = run(v220, connector, july_tape, july_abort, rule)
        h = summarize(h1)
        j = summarize(july)
        delta = h["Monthly"].sub(bh["Monthly"], fill_value=0)
        rows.append({
            "Rule": rule.name,
            "H1Trades": h["Trades"],
            "H1Gross": h["Gross"],
            "H1Net": h["Net"],
            "H1NetDelta": round(h["Net"] - bh["Net"], 2),
            "H1PF": h["PF"],
            "H1PositiveWeeks": h["PositiveWeeks"],
            "H1WorstWeek": h["WorstWeek"],
            "H1MaxDD": h["MaxDD"],
            "PositiveDeltaMonths": int(delta.gt(0).sum()),
            "NegativeDeltaMonths": int(delta.lt(0).sum()),
            "AddOns": h["AddOns"],
            "AddOnGross": h["AddOnGross"],
            "JulyNet": j["Net"],
            "JulyNetDelta": round(j["Net"] - bj["Net"], 2),
            "JulyAddOns": j["AddOns"],
        })
    result = pd.DataFrame(rows).sort_values(["H1Net", "H1PF"], ascending=False)
    args.output_dir.mkdir(parents=True, exist_ok=True)
    result.to_csv(args.output_dir / "profit_addon_lomo.csv", index=False)
    print("BASELINE", bh, "JULY", bj)
    print(result.head(30).to_string(index=False))


if __name__ == "__main__":
    main()
