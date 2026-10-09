#!/usr/bin/env python3
"""Validate that every B pre-entry confirmation has one A legacy ENTRY_SEND."""

from __future__ import annotations

import argparse
import csv
import json
import re
from collections import defaultdict
from pathlib import Path


OLD_PATH = "FailureReverse_ObservationInvalidated_WideStop1_5R"
NEW_PATH = "FailureReverse_PreEntryConfirmedWideStopShort"
ACTUAL_VERSION = "ACTUAL_EXEC_2.51"
PROFILE = "V500_FR_PREENTRY_WIDESHORT_CALIBRATION"
CONFIRM = re.compile(
    r"FR_PREENTRY_CONFIRM signal=(?P<b_signal>\S+) LegacySignalId=(?P<legacy_signal_id>\S+) LegacyPath=(?P<legacy_path>\S+)"
)


def execution_entries(root: Path, path_name: str) -> dict[str, list[str]]:
    entries: dict[str, list[str]] = defaultdict(list)
    for file in root.rglob("*_execution_events.csv"):
        with file.open(newline="", encoding="utf-8-sig") as stream:
            for row in csv.DictReader(stream):
                if row.get("Event") == "ENTRY_SEND" and row.get("ResearchPath") == path_name:
                    entries[row.get("SignalID", "")].append(row.get("TradeID", ""))
    return entries


def confirmations(root: Path) -> list[dict[str, str]]:
    found: list[dict[str, str]] = []
    for file in root.rglob("*_research.log"):
        for line in file.read_text(encoding="utf-8").splitlines():
            match = CONFIRM.search(line)
            if match:
                found.append(match.groupdict())
    return found


def candidate_config_failures(root: Path) -> list[str]:
    snapshots = list(root.rglob("*_ConfigSnapshot.json"))
    if not snapshots:
        return ["CONFIG_SNAPSHOT_REQUIRED"]

    failures: set[str] = set()
    for file in snapshots:
        snapshot = json.loads(file.read_text(encoding="utf-8-sig"))
        settings = snapshot.get("ActualExecutionSettings", {})
        paths = settings.get("ActualExecutionPaths", "").split("|")
        if snapshot.get("ActualExecutionConfigStatus") != "loaded":
            failures.add("CONFIG_STATUS_NOT_LOADED")
        if settings.get("Version") != ACTUAL_VERSION:
            failures.add("CONFIG_VERSION_MISMATCH")
        if settings.get("RunProfileId") != PROFILE:
            failures.add("CONFIG_PROFILE_MISMATCH")
        if OLD_PATH in paths:
            failures.add("OLD_IMMEDIATE_PATH_ENABLED")
        if NEW_PATH not in paths:
            failures.add("NEW_STRICT_PATH_DISABLED")
    return sorted(failures)


def analyze(a_dir: Path, b_dir: Path, out_dir: Path) -> dict[str, object]:
    a_entries = execution_entries(a_dir, OLD_PATH)
    b_entries = execution_entries(b_dir, NEW_PATH)
    b_old_entries = execution_entries(b_dir, OLD_PATH)
    config_failures = candidate_config_failures(b_dir)
    rows: list[dict[str, str]] = []
    for confirmation in confirmations(b_dir):
        a_trade_ids = a_entries.get(confirmation["legacy_signal_id"], [])
        b_trade_ids = b_entries.get(confirmation["b_signal"], [])
        status = "MAPPED_ENTRY_SEND"
        if confirmation["legacy_path"] != OLD_PATH or len(a_trade_ids) != 1:
            status = "A_ENTRY_SEND_REQUIRED"
        elif len(b_trade_ids) > 1:
            status = "B_ENTRY_SEND_NOT_UNIQUE"
        elif not b_trade_ids:
            status = "MAPPED_CONFIRMATION_NO_ENTRY_SEND"
        rows.append({
            "legacy_signal_id": confirmation["legacy_signal_id"],
            "legacy_path": confirmation["legacy_path"],
            "a_trade_id": "|".join(a_trade_ids),
            "b_trade_id": "|".join(b_trade_ids),
            "mapping_status": status,
        })

    out_dir.mkdir(parents=True, exist_ok=True)
    mapping = out_dir / "legacy_replacement_mapping.csv"
    with mapping.open("w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, fieldnames=["legacy_signal_id", "legacy_path", "a_trade_id", "b_trade_id", "mapping_status"])
        writer.writeheader()
        writer.writerows(rows)
    failed_mapping = any(row["mapping_status"] == "A_ENTRY_SEND_REQUIRED" for row in rows)
    summary = {
        "status": "FAIL" if failed_mapping or config_failures or b_old_entries else "PASS",
        "a_entry_send_count": sum(map(len, a_entries.values())),
        "b_confirmation_count": len(rows),
        "b_entry_send_count": sum(map(len, b_entries.values())),
        "b_old_immediate_entry_send_count": sum(map(len, b_old_entries.values())),
        "config_failures": config_failures,
        "mapping_csv": str(mapping),
    }
    (out_dir / "strict_replacement_summary.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding="utf-8")
    return summary


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--a-dir", type=Path, required=True)
    parser.add_argument("--b-dir", type=Path, required=True)
    parser.add_argument("--out-dir", type=Path, required=True)
    args = parser.parse_args()
    summary = analyze(args.a_dir, args.b_dir, args.out_dir)
    print(json.dumps(summary, ensure_ascii=False, indent=2))
    return 0 if summary["status"] == "PASS" else 1


if __name__ == "__main__":
    raise SystemExit(main())
