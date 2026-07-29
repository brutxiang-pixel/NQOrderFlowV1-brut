import argparse
import importlib.util
import json
from pathlib import Path

import pandas as pd


def load_module(path, name):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--logs", type=Path, required=True)
    parser.add_argument("--july-tape", type=Path, required=True)
    parser.add_argument("--dates", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    args = parser.parse_args()

    dates = pd.read_csv(args.dates, dtype={"TradingDate": str})["TradingDate"].tolist()
    tape = pd.read_csv(args.july_tape, low_memory=False)
    tape["EntryTime"] = pd.to_datetime(tape["EntryTime"])
    tape["EntryBar"] = tape["EntryBar"].astype(int)
    tape = tape[tape["TradingDate"].astype(str).isin(dates)].copy()
    rich_module = load_module(Path(__file__).with_name("Connect-OPFRichFeatures.py"), "rich")

    snapshots = []
    for rich_path in sorted(args.logs.glob("*_rich_bar_features.csv")):
        snapshot_id = rich_path.name.removesuffix("_rich_bar_features.csv")
        config_path = args.logs / f"{snapshot_id}_ConfigSnapshot.json"
        log_path = args.logs / f"{snapshot_id}_research.log"
        if not config_path.is_file() or not log_path.is_file():
            continue
        config = json.loads(config_path.read_text(encoding="utf-8"))
        log = log_path.read_text(encoding="utf-8", errors="replace")
        if "RICH_BAR_DATA_COLLECTION_ONLY enabled=true actualOrders=false" not in log:
            continue
        part = pd.read_csv(rich_path, low_memory=False)
        part["Time"] = pd.to_datetime(part["Time"])
        part["Bar"] = part["Bar"].astype(int)
        snapshots.append((snapshot_id, config.get("ResearchSchemaVersion"), part))
    if not snapshots:
        raise ValueError("No Data Only Rich snapshots found")

    rich = pd.concat(
        [part.assign(RichSnapshotID=snapshot_id, Schema=schema) for snapshot_id, schema, part in snapshots],
        ignore_index=True,
    )
    if rich.duplicated(["Time", "Bar"]).any():
        raise ValueError("Rich Time+Bar keys duplicate across selected Data Only snapshots")
    joined = tape.merge(rich, left_on=["EntryTime", "EntryBar"], right_on=["Time", "Bar"], how="left")
    rows = []
    for day in dates:
        expected = joined[joined["TradingDate"].astype(str).eq(day)]
        matched = expected["RichSnapshotID"].notna()
        used_ids = expected.loc[matched, "RichSnapshotID"].dropna().unique().tolist()
        execution_files = sum(
            int((args.logs / f"{snapshot_id}_{suffix}.csv").is_file())
            for snapshot_id in used_ids
            for suffix in ("execution_decisions", "execution_trades", "live_account_pnl")
        )
        initialization = expected.loc[matched, ["AverageVolume20", "RelativeVolume20", "Atr14"]].fillna(0).eq(0).any(axis=1).sum()
        rows.append({
            "TradingDate": day,
            "TapeCandidates": len(expected),
            "RichMatched": int(matched.sum()),
            "RichCoveragePct": round(100 * float(matched.mean()) if len(expected) else 0.0, 2),
            "InitializationZeroMatches": int(initialization),
            "DataOnlySnapshots": "|".join(used_ids),
            "UnexpectedExecutionFiles": execution_files,
        })
    report = pd.DataFrame(rows)
    failures = report[(report["RichMatched"] != report["TapeCandidates"]) | (report["InitializationZeroMatches"] != 0) | (report["UnexpectedExecutionFiles"] != 0)]
    args.output_dir.mkdir(parents=True, exist_ok=True)
    report.to_csv(args.output_dir / "validation.csv", index=False)
    pd.DataFrame([{
        "ExpectedDates": len(dates), "DataOnlySnapshots": len(snapshots), "RichRows": len(rich),
        "Failures": len(failures), "Status": "PASS" if failures.empty else "FAIL",
    }]).to_csv(args.output_dir / "summary.csv", index=False)
    print(report.to_string(index=False))
    if not failures.empty:
        raise SystemExit("Data Only July Rich validation failed")


if __name__ == "__main__":
    main()
