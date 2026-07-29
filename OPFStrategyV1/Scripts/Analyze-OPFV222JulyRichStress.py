import argparse
import importlib.util
import json
from pathlib import Path

import numpy as np
import pandas as pd


def load_module(path, name):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def load_tape(path):
    tape = pd.read_csv(path, low_memory=False)
    for column in ("EntryTime", "PredictedExitTime", "TradingDate"):
        tape[column] = pd.to_datetime(tape[column])
    submissions = pd.read_csv(path.with_name("submission_trace.csv"), low_memory=False)
    aborted = submissions[submissions["SubmissionStatus"].eq("PreflightAborted")]
    return tape, frozenset(aborted["SignalID"] + "|" + aborted["ResearchPath"] + "|" + aborted["Lane"])


def load_data_only_rich(logs, rich_module):
    parts = []
    for path in sorted(logs.glob("*_rich_bar_features.csv")):
        snapshot = path.name.removesuffix("_rich_bar_features.csv")
        log_path = logs / f"{snapshot}_research.log"
        if not log_path.is_file() or "RICH_BAR_DATA_COLLECTION_ONLY enabled=true actualOrders=false" not in log_path.read_text(encoding="utf-8", errors="replace"):
            continue
        part = pd.read_csv(path, low_memory=False)
        if "SnapshotID" in part:
            part.rename(columns={"SnapshotID": "RichSourceSnapshotID"}, inplace=True)
        part["RichTime"] = pd.to_datetime(part.pop("Time"))
        part["RichBar"] = part.pop("Bar").astype(int)
        part.rename(columns={name: f"Rich{name}" for name in rich_module.RICH_VALUE_COLUMNS}, inplace=True)
        bull = pd.DataFrame((rich_module._expand_components(value, "Bull") for value in part.pop("BullComponents")), index=part.index)
        bear = pd.DataFrame((rich_module._expand_components(value, "Bear") for value in part.pop("BearComponents")), index=part.index)
        parts.append(pd.concat([part, bull, bear], axis=1))
    rich = pd.concat(parts, ignore_index=True)
    if rich.duplicated(["RichTime", "RichBar"]).any():
        raise ValueError("Data Only Rich keys duplicate")
    return rich


def enrich(tape, rich):
    output = tape.merge(rich, left_on=["EntryTime", "EntryBar"], right_on=["RichTime", "RichBar"], how="left", validate="many_to_one")
    output["RichAvailable"] = output["RichRegimeBars"].notna()
    is_long = output["Side"].eq("Long")
    output["RichAlignedVWAPSidePassed"] = np.where(is_long, output["RichBullVWAPSidePassed"], output["RichBearVWAPSidePassed"])
    return output


def metrics(trades):
    gross = trades["Gross"]
    wins = float(gross[gross > 0].sum())
    losses = -float(gross[gross < 0].sum())
    return {"Trades": len(trades), "Gross": round(float(gross.sum()), 2), "Net": round(float(trades["Net"].sum()), 2), "PF": round(wins / losses, 4)}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--logs", type=Path, required=True)
    parser.add_argument("--july-tape", type=Path, required=True)
    parser.add_argument("--dates", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    args = parser.parse_args()
    scripts = Path(__file__).parent
    v220 = load_module(scripts / "Analyze-OPFV220H1LatchedProfitResearch.py", "v220")
    connector = load_module(scripts / "Analyze-OPFV217DualSlotCalibration.py", "connector")
    rich_module = load_module(scripts / "Connect-OPFRichFeatures.py", "rich")
    dates = pd.read_csv(args.dates, dtype={"TradingDate": str})["TradingDate"].tolist()
    tape, aborts = load_tape(args.july_tape)
    tape = tape[tape["TradingDate"].dt.strftime("%Y-%m-%d").isin(dates)].copy()
    tape = enrich(tape, load_data_only_rich(args.logs, rich_module))
    if not tape["RichAvailable"].all():
        raise ValueError("July stress tape has missing Rich features")

    def candidate_gate(row, lane):
        if lane != "Primary":
            return False
        if row.Side == "Long" and row.ResearchPath == "ObservationConfirm":
            return float(row.RichRegimeBars) < 8.0
        if row.Side == "Short" and row.ResearchPath == "BreakawayFvg":
            return (
                float(row.SetupQualityScore) < 88.0
                or float(row.ExactRisk) < 12.0
                or float(row.RichAlignedVWAPSidePassed) < 1.0
            )
        return False

    baseline, _ = v220.simulate(tape, connector, v220.Policy(zone_birth_min_quality=45.0), aborts)
    candidate, blocked = v220.simulate(tape, connector, v220.Policy(zone_birth_min_quality=45.0), aborts, candidate_gate=candidate_gate)
    base = metrics(baseline)
    current = metrics(candidate)
    args.output_dir.mkdir(parents=True, exist_ok=True)
    pd.DataFrame([{
        "SampleDays": len(dates), **{f"Baseline{k}": v for k, v in base.items()},
        **{f"Candidate{k}": v for k, v in current.items()},
        "NetDelta": round(current["Net"] - base["Net"], 2), "GrossDelta": round(current["Gross"] - base["Gross"], 2),
        **{f"Blocked_{key}": value for key, value in blocked.items()},
    }]).to_csv(args.output_dir / "summary.csv", index=False)
    baseline.assign(Variant="Baseline").to_csv(args.output_dir / "baseline_trades.csv", index=False)
    candidate.assign(Variant="Candidate").to_csv(args.output_dir / "candidate_trades.csv", index=False)
    print(pd.read_csv(args.output_dir / "summary.csv").to_string(index=False))


if __name__ == "__main__":
    main()
