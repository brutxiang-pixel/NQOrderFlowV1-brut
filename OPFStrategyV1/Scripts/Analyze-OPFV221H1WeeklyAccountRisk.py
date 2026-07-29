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


def summarize(trades: pd.DataFrame) -> dict:
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


def run(v220, connector, tape, abort_keys, values):
    return v220.simulate(
        tape,
        connector,
        v220.Policy(zone_birth_min_quality=45.0, **values),
        abort_keys,
    )[0]


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
    base_h1 = run(v220, connector, h1_tape, h1_abort, {})
    base_july = run(v220, connector, july_tape, july_abort, {})
    bh = summarize(base_h1)
    bj = summarize(base_july)
    if bh["Trades"] != 878 or bh["Gross"] != 26435.12:
        raise RuntimeError(f"v2.21 baseline mismatch: {bh}")

    policies = []
    for value in (300.0, 400.0, 500.0, 600.0, 750.0, 900.0, 1000.0, 1200.0):
        policies.append((f"WeeklyLoss{value:g}", {"weekly_account_loss": value}))
        policies.append((f"WeeklyDrawdown{value:g}", {"weekly_account_drawdown": value}))
    rows = []
    for name, values in policies:
        h1 = run(v220, connector, h1_tape, h1_abort, values)
        july = run(v220, connector, july_tape, july_abort, values)
        h = summarize(h1)
        j = summarize(july)
        delta = h["Monthly"].sub(bh["Monthly"], fill_value=0)
        rows.append({
            "Policy": name,
            "H1Trades": h["Trades"],
            "H1Gross": h["Gross"],
            "H1Net": h["Net"],
            "H1NetDelta": round(h["Net"] - bh["Net"], 2),
            "H1PF": h["PF"],
            "H1PositiveWeeks": h["PositiveWeeks"],
            "H1WorstWeek": h["WorstWeek"],
            "H1MaxDD": h["MaxDD"],
            "H1PositiveMonths": h["PositiveMonths"],
            "PositiveDeltaMonths": int(delta.gt(0).sum()),
            "NegativeDeltaMonths": int(delta.lt(0).sum()),
            "JulyTrades": j["Trades"],
            "JulyNet": j["Net"],
            "JulyNetDelta": round(j["Net"] - bj["Net"], 2),
        })
    result = pd.DataFrame(rows).sort_values(["H1Net", "H1PF"], ascending=False)
    args.output_dir.mkdir(parents=True, exist_ok=True)
    result.to_csv(args.output_dir / "weekly_account_risk.csv", index=False)
    print("BASELINE", bh, "JULY", bj)
    print(result.to_string(index=False))


if __name__ == "__main__":
    main()
