#!/usr/bin/env python3
"""Read-only attribution for the V5 15-to-13 daily-cap Actual calibration."""

from __future__ import annotations

import argparse
import importlib.util
import re
from pathlib import Path

import pandas as pd


BASELINE_PATH = Path(__file__).with_name("Analyze-OPFV500CrossYearBaseline.py")
BASELINE_SPEC = importlib.util.spec_from_file_location("v500_baseline", BASELINE_PATH)
BASELINE = importlib.util.module_from_spec(BASELINE_SPEC)
BASELINE_SPEC.loader.exec_module(BASELINE)

KEY_COLUMNS = ["SignalID", "ResearchPath", "Side"]
EXPECTED = {
    "2025-05-14": (-47.10, 67.60),
    "2025-06-25": (458.89, 210.59),
    "2025-07-21": (-283.00, -149.80),
    "2025-10-21": (-143.70, -53.50),
    "2025-12-15": (436.50, 245.70),
    "2025-12-22": (201.90, 375.60),
    "2026-01-28": (597.10, 772.30),
    "2026-04-29": (384.90, 623.60),
}


def classify_trade_pairs(candidate: pd.DataFrame, baseline: pd.DataFrame) -> pd.DataFrame:
    """Outer-join Normal rows and make Replay drift explicit without inventing PnL."""
    for name, rows in (("candidate", candidate), ("baseline", baseline)):
        duplicates = rows.duplicated(KEY_COLUMNS, keep=False)
        if duplicates.any():
            values = rows.loc[duplicates, KEY_COLUMNS].to_dict("records")
            raise ValueError(f"Ambiguous {name} composite identity: {values}")

    candidate_rows = candidate.loc[:, KEY_COLUMNS + ["ExitRole", "NetPnLDollars"]].rename(
        columns={"ExitRole": "CandidateExitRole", "NetPnLDollars": "CandidateNetPnLDollars"}
    )
    baseline_rows = baseline.loc[:, KEY_COLUMNS + ["ExitRole", "NetPnLDollars"]].rename(
        columns={"ExitRole": "BaselineExitRole", "NetPnLDollars": "BaselineNetPnLDollars"}
    )
    pairs = candidate_rows.merge(
        baseline_rows,
        on=KEY_COLUMNS,
        how="outer",
        indicator=True,
        validate="one_to_one",
    )
    pairs["StableMatched"] = pairs["_merge"].eq("both") & pairs["CandidateExitRole"].eq(pairs["BaselineExitRole"])
    pairs["ExitRoleChanged"] = pairs["_merge"].eq("both") & ~pairs["StableMatched"]
    pairs["CandidateOnly"] = pairs["_merge"].eq("left_only")
    pairs["BaselineOnly"] = pairs["_merge"].eq("right_only")
    return pairs.drop(columns="_merge")


def summarize_candidate_ledger(ledger: pd.DataFrame) -> dict[str, float | int]:
    """Keep isolated account effects visible but out of Normal strategy economics."""
    normal = ledger.loc[ledger["Classification"].eq("Normal")]
    abnormal = ledger.loc[ledger["Classification"].ne("Normal")]
    return {
        "NormalTrades": int(len(normal)),
        "NormalNetDollars": round(float(normal["NetPnLDollars"].sum()), 2),
        "IsolatedAbnormalTrades": int(len(abnormal)),
        "AbnormalAccountNetDollars": round(float(abnormal["NetPnLDollars"].sum()), 2),
    }


def summarize_pair_economics(pairs: pd.DataFrame) -> dict[str, float]:
    """Compare candidate to the actual matched frozen Snapshot ledger, not a prior estimate."""
    reference_net = round(float(pairs["BaselineNetPnLDollars"].sum()), 2)
    candidate_net = round(float(pairs["CandidateNetPnLDollars"].sum()), 2)
    return {
        "ReferenceFrozenNormalNetDollars": reference_net,
        "CandidateNormalNetDollars": candidate_net,
        "ActualChangeVsReferenceDollars": round(candidate_net - reference_net, 2),
    }


def build_normal_execution_rows(ledger: pd.DataFrame, trades: pd.DataFrame) -> pd.DataFrame:
    """Attach execution identity to Normal account rows without retaining ledger-side Side."""
    normal_ledger = ledger.loc[ledger["Classification"].eq("Normal")].drop(columns="Side", errors="ignore")
    candidate = normal_ledger.merge(
        trades.loc[:, ["TradeID"] + KEY_COLUMNS + ["ExitRole"]], on="TradeID", how="inner", validate="one_to_one"
    )
    if len(candidate) != len(normal_ledger):
        raise ValueError("Normal ledger/trade join mismatch")
    return candidate


def _single_file(directory: Path, snapshot_id: str, suffix: str) -> Path:
    path = directory / f"{snapshot_id}_{suffix}.csv"
    if not path.exists():
        raise FileNotFoundError(path)
    return path


def _trading_date(events: pd.DataFrame) -> str:
    dates = []
    for message in events.loc[events["Event"].eq("GLOBEX_TRADING_DAY_ROLLOVER"), "Message"].astype(str):
        match = re.search(r"current=(\d{4}-\d{2}-\d{2})", message)
        if match:
            dates.append(match.group(1))
    if not dates:
        raise ValueError("Missing GLOBEX_TRADING_DAY_ROLLOVER current date")
    return max(dates)


def _first_daily_cap(decisions: pd.DataFrame) -> tuple[str, str]:
    rows = decisions.loc[decisions["Reason"].astype(str).str.contains("DailyTradeLimit:max=13", na=False)]
    if rows.empty:
        return "", ""
    first = rows.iloc[0]
    return str(first["DailyTradeCount"]), str(first["Time"])


def _lifecycle_defects(events: pd.DataFrame) -> int:
    return int(events["Event"].astype(str).str.contains(r"PROTECTION.*FAIL|ORPHAN|UNPROTECTED|ERROR|EXCEPTION", case=False, regex=True).sum())


def _reference_snapshot(candidate: pd.DataFrame, baseline: pd.DataFrame) -> str:
    matches = candidate.loc[:, KEY_COLUMNS].merge(
        baseline.loc[:, ["SnapshotID"] + KEY_COLUMNS], on=KEY_COLUMNS, how="inner", validate="one_to_one"
    )
    if matches.empty:
        raise ValueError("No frozen baseline identity matches for candidate Snapshot")
    return str(matches["SnapshotID"].value_counts().idxmax())


def analyze_snapshot(active_log_dir: Path, snapshot_id: str, baseline: pd.DataFrame) -> tuple[dict, pd.DataFrame]:
    ledger = pd.read_csv(_single_file(active_log_dir, snapshot_id, "live_account_pnl"))
    trades = pd.read_csv(_single_file(active_log_dir, snapshot_id, "execution_trades"))
    decisions = pd.read_csv(_single_file(active_log_dir, snapshot_id, "execution_decisions"))
    events = pd.read_csv(_single_file(active_log_dir, snapshot_id, "execution_events"))
    config = pd.read_json(active_log_dir / f"{snapshot_id}_ConfigSnapshot.json", typ="series")

    candidate = build_normal_execution_rows(ledger, trades)
    reference_id = _reference_snapshot(candidate, baseline)
    reference = baseline.loc[baseline["SnapshotID"].eq(reference_id)].copy()
    pairs = classify_trade_pairs(candidate, reference)
    pairs.insert(0, "CandidateSnapshotID", snapshot_id)
    pairs.insert(1, "FrozenSnapshotID", reference_id)

    trading_date = _trading_date(events)
    if trading_date not in EXPECTED:
        raise ValueError(f"Unexpected calibration trading date: {trading_date}")
    frozen_net, predicted_net = EXPECTED[trading_date]
    first_cap_count, first_cap_time = _first_daily_cap(decisions)
    settings = config["ActualExecutionSettings"]
    ledger_summary = summarize_candidate_ledger(ledger)
    pair_economics = summarize_pair_economics(pairs)
    summary = {
        "CandidateSnapshotID": snapshot_id,
        "TradingDate": trading_date,
        "FrozenSnapshotID": reference_id,
        "ConfigVersion": settings["Version"],
        "RunProfileId": settings["RunProfileId"],
        "ActualOrderQuantity": settings["ActualOrderQuantity"],
        "ActualMaxTradesPerDay": settings["ActualMaxTradesPerDay"],
        "ActualDailyLossLimitDollars": settings["ActualDailyLossLimitDollars"],
        "ActualWeeklyLongLossLimitDollars": settings["ActualWeeklyLongLossLimitDollars"],
        "EnableActualOrders": settings["EnableActualOrders"],
        "FirstDailyCapCount": first_cap_count,
        "FirstDailyCapTime": first_cap_time,
        "LifecycleDefects": _lifecycle_defects(events),
        "ContractFrozen15NormalNetDollars": frozen_net,
        "ContractPredicted13NormalNetDollars": predicted_net,
        **ledger_summary,
        **pair_economics,
        "ContractPredictionErrorDollars": round(ledger_summary["NormalNetDollars"] - predicted_net, 2),
        "StableMatched": int(pairs["StableMatched"].sum()),
        "ExitRoleChanged": int(pairs["ExitRoleChanged"].sum()),
        "CandidateOnly": int(pairs["CandidateOnly"].sum()),
        "BaselineOnly": int(pairs["BaselineOnly"].sum()),
    }
    return summary, pairs


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--archive-root", type=Path, required=True)
    parser.add_argument("--active-log-dir", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    args = parser.parse_args()

    baseline = BASELINE.load_baseline(args.archive_root)
    snapshot_ids = sorted(path.name.replace("_ConfigSnapshot.json", "") for path in args.active_log_dir.glob("*_ConfigSnapshot.json"))
    if len(snapshot_ids) != 8:
        raise ValueError(f"Expected 8 active calibration snapshots, found {len(snapshot_ids)}")
    summaries, attributions = zip(*(analyze_snapshot(args.active_log_dir, snapshot_id, baseline) for snapshot_id in snapshot_ids))
    summary = pd.DataFrame(summaries).sort_values("TradingDate")
    attribution = pd.concat(attributions, ignore_index=True)
    args.output_dir.mkdir(parents=True, exist_ok=True)
    summary.to_csv(args.output_dir / "v500_daily_cap13_actual_calibration_summary.csv", index=False, encoding="utf-8")
    attribution.to_csv(args.output_dir / "v500_daily_cap13_actual_calibration_attribution.csv", index=False, encoding="utf-8")
    print(summary.to_string(index=False))


if __name__ == "__main__":
    main()
