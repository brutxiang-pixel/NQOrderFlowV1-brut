import argparse
import importlib.util
from pathlib import Path

import pandas as pd


def load_module(path: Path, name: str):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


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


def metrics(trades: pd.DataFrame) -> dict:
    dates = pd.to_datetime(trades["TradingDate"])
    daily = trades.assign(Date=dates.dt.strftime("%Y-%m-%d")).groupby("Date")["Net"].sum()
    weekly = trades.assign(Week=dates.dt.to_period("W-SUN").astype(str)).groupby("Week")["Net"].sum()
    monthly = trades.assign(Month=dates.dt.strftime("%Y-%m")).groupby("Month")["Net"].sum()
    gross = trades["Gross"]
    wins = float(gross[gross > 0].sum())
    losses = -float(gross[gross < 0].sum())
    drawdown = daily.cumsum().cummax() - daily.cumsum()
    secondary = trades[trades["Lane"].eq("Secondary")]
    return {
        "Trades": len(trades),
        "Gross": round(float(gross.sum()), 2),
        "Net": round(float(trades["Net"].sum()), 2),
        "PF": round(wins / losses, 4),
        "PositiveWeeks": int(weekly.gt(0).sum()),
        "WorstWeek": round(float(weekly.min()), 2),
        "MaxDD": round(float(drawdown.max()), 2),
        "PositiveMonths": int(monthly.gt(0).sum()),
        "SecondaryTrades": len(secondary),
        "SecondaryNet": round(float(secondary["Net"].sum()), 2),
        "Monthly": monthly,
    }


def policy_name(values: dict) -> str:
    if not values:
        return "V221"
    return "+".join(f"{key}={value:g}" for key, value in values.items())


def run(v220, connector, tape, abort_keys, values):
    policy = v220.Policy(zone_birth_min_quality=45.0, **values)
    return v220.simulate(tape, connector, policy, abort_keys)[0]


def result_row(name, values, h1, july, baseline_h1, baseline_july):
    h = metrics(h1)
    j = metrics(july)
    bh = metrics(baseline_h1)
    bj = metrics(baseline_july)
    month_delta = h["Monthly"].sub(bh["Monthly"], fill_value=0)
    baseline_ids = set(zip(baseline_h1.SignalID, baseline_h1.ResearchPath, baseline_h1.Lane))
    ids = set(zip(h1.SignalID, h1.ResearchPath, h1.Lane))
    return {
        "Policy": name,
        "Variables": len(values),
        "H1Trades": h["Trades"],
        "H1Gross": h["Gross"],
        "H1Net": h["Net"],
        "H1NetDelta": round(h["Net"] - bh["Net"], 2),
        "H1PF": h["PF"],
        "H1PositiveWeeks": h["PositiveWeeks"],
        "H1WorstWeek": h["WorstWeek"],
        "H1MaxDD": h["MaxDD"],
        "PositiveDeltaMonths": int(month_delta.gt(0).sum()),
        "NegativeDeltaMonths": int(month_delta.lt(0).sum()),
        "SecondaryTrades": h["SecondaryTrades"],
        "SecondaryNet": h["SecondaryNet"],
        "ChangedIdentities": len(baseline_ids.symmetric_difference(ids)),
        "JulyTrades": j["Trades"],
        "JulyNet": j["Net"],
        "JulyNetDelta": round(j["Net"] - bj["Net"], 2),
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
    baseline_h1 = run(v220, connector, h1_tape, h1_abort, {})
    baseline_july = run(v220, connector, july_tape, july_abort, {})
    baseline = result_row("V221", {}, baseline_h1, baseline_july, baseline_h1, baseline_july)
    if baseline["H1Trades"] != 878 or baseline["H1Gross"] != 26435.12:
        raise RuntimeError(f"v2.21 baseline mismatch: {baseline}")

    candidates = []
    for value in (100.0, 150.0, 200.0, 250.0, 300.0, 400.0, 500.0, 650.0):
        candidates.append({"secondary_week_loss": value})
        candidates.append({"secondary_week_drawdown": value})
    for value in (50.0, 75.0, 100.0, 150.0, 200.0):
        candidates.append({"secondary_day_loss": value})
    for value in (1, 2, 3, 4):
        candidates.append({"secondary_day_cap": value})
    for value in (3, 5, 8, 10, 12, 15, 20):
        candidates.append({"secondary_week_cap": value})

    rows = []
    values_by_name = {}
    for values in candidates:
        name = policy_name(values)
        h1 = run(v220, connector, h1_tape, h1_abort, values)
        july = run(v220, connector, july_tape, july_abort, values)
        rows.append(result_row(name, values, h1, july, baseline_h1, baseline_july))
        values_by_name[name] = values
    singles = pd.DataFrame(rows).sort_values(["H1Net", "H1PF"], ascending=False)

    top = singles[
        singles["H1NetDelta"].gt(0)
        & singles["H1PF"].ge(baseline["H1PF"])
        & singles["H1WorstWeek"].ge(baseline["H1WorstWeek"] - 100)
        & singles["H1MaxDD"].le(baseline["H1MaxDD"])
        & singles["PositiveDeltaMonths"].ge(4)
        & singles["JulyNetDelta"].ge(-250)
    ].head(8)
    pairs = []
    names = top["Policy"].tolist()
    for i, left in enumerate(names):
        for right in names[i + 1 :]:
            values = {**values_by_name[left], **values_by_name[right]}
            if len(values) != 2:
                continue
            name = policy_name(values)
            h1 = run(v220, connector, h1_tape, h1_abort, values)
            july = run(v220, connector, july_tape, july_abort, values)
            pairs.append(result_row(name, values, h1, july, baseline_h1, baseline_july))
    pairs = pd.DataFrame(pairs)
    if not pairs.empty:
        pairs = pairs.drop_duplicates("Policy").sort_values(["H1Net", "H1PF"], ascending=False)

    args.output_dir.mkdir(parents=True, exist_ok=True)
    pd.DataFrame([baseline]).to_csv(args.output_dir / "baseline.csv", index=False)
    singles.to_csv(args.output_dir / "single_policies.csv", index=False)
    pairs.to_csv(args.output_dir / "pair_policies.csv", index=False)
    print("BASELINE")
    print(pd.DataFrame([baseline]).to_string(index=False))
    print("TOP SINGLES")
    print(singles.head(20).to_string(index=False))
    print("TOP PAIRS")
    print(pairs.head(20).to_string(index=False) if not pairs.empty else "none")


if __name__ == "__main__":
    main()
