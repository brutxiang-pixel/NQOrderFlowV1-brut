#!/usr/bin/env python3
"""Read-only reconstruction of V5 Actual daily gate state."""

from __future__ import annotations

import argparse
import importlib.util
from pathlib import Path

import pandas as pd


BASELINE_SCRIPT = Path(__file__).with_name("Analyze-OPFV500CrossYearBaseline.py")
BASELINE_SPEC = importlib.util.spec_from_file_location("v500_baseline", BASELINE_SCRIPT)
BASELINE = importlib.util.module_from_spec(BASELINE_SPEC)
BASELINE_SPEC.loader.exec_module(BASELINE)

EVENT_PRIORITY = {"Execute": 0, "Rollback": 1, "AccountPnl": 2}
ROLLBACK_EVENTS = {
    "ENTRY_SUBMISSION_ABORTED_V178",
    "ENTRY_FILL_QUARANTINED_V174",
    "PROTECTIVE_FILL_QUARANTINED_V177",
    "PROTECTION_SETUP_QUARANTINED_V178",
    "ABNORMAL_SAFETY_FLATTEN_ISOLATED_V182",
}


def is_counter_rollback(event_name: str) -> bool:
    return event_name in ROLLBACK_EVENTS


def replay_gate_states(events: pd.DataFrame) -> pd.DataFrame:
    """Return each Execute event with the recorded gate state immediately before it."""
    ordered = events.copy()
    ordered["Time"] = pd.to_datetime(ordered["Time"])
    ordered["_Priority"] = ordered["Kind"].map(EVENT_PRIORITY).fillna(99)
    group_columns = []
    order_columns = []
    if "SnapshotID" in ordered:
        group_columns.append("SnapshotID")
        order_columns.append("SnapshotID")
    if "Sequence" in ordered:
        order_columns.append("Sequence")
    else:
        order_columns.extend(["Time", "_Priority"])
    ordered = ordered.sort_values(order_columns, kind="stable")
    rows = []
    grouped = ordered.groupby(group_columns, sort=False, dropna=False) if group_columns else [(None, ordered)]
    for _, group in grouped:
        count = 0
        daily_net = 0.0
        for event in group.itertuples(index=False):
            if event.Kind in {"Execute", "Decision"}:
                rows.append({
                    "SnapshotID": getattr(event, "SnapshotID", ""),
                    "TradeID": event.TradeID,
                    "Sequence": getattr(event, "Sequence", None),
                    "Kind": event.Kind,
                    "Reason": getattr(event, "Reason", ""),
                    "RecordedDailyTradeCount": getattr(event, "RecordedDailyTradeCount", None),
                    "PreTradeCount": count,
                    "PreDailyNet": daily_net,
                })
                if event.Kind == "Execute":
                    count += 1
            elif event.Kind == "Rollback":
                count = max(0, count - 1)
            elif event.Kind == "AccountPnl":
                daily_net += float(getattr(event, "Net", 0.0))
    return pd.DataFrame(rows)


def simulate_frozen_gates(events: pd.DataFrame, max_trades: int, loss_limit: float) -> pd.DataFrame:
    """Replay recorded entry attempts under tighter gates without inventing candidates."""
    ordered = events.copy()
    ordered["Time"] = pd.to_datetime(ordered["Time"])
    ordered["_Priority"] = ordered["Kind"].map(EVENT_PRIORITY).fillna(99)
    group_columns = ["SnapshotID"] if "SnapshotID" in ordered else []
    order_columns = group_columns + (["Sequence"] if "Sequence" in ordered else ["Time", "_Priority"])
    ordered = ordered.sort_values(order_columns, kind="stable")
    grouped = ordered.groupby(group_columns, sort=False, dropna=False) if group_columns else [(None, ordered)]
    accepted_rows = []
    for _, group in grouped:
        count = 0
        daily_net = 0.0
        active_attempts: dict[str, list[object]] = {}
        accepted_attempts: set[object] = set()
        blocked_trade_ids: set[str] = set()
        for event in group.itertuples(index=False):
            trade_id = str(event.TradeID)
            sequence = getattr(event, "Sequence", (getattr(event, "Time"), trade_id))
            if event.Kind == "Execute":
                if trade_id in blocked_trade_ids:
                    continue
                if max_trades > 0 and count >= max_trades:
                    blocked_trade_ids.add(trade_id)
                    continue
                if loss_limit > 0 and daily_net <= -loss_limit:
                    blocked_trade_ids.add(trade_id)
                    continue
                accepted_attempts.add(sequence)
                active_attempts.setdefault(trade_id, []).append(sequence)
                accepted_rows.append({
                    "SnapshotID": getattr(event, "SnapshotID", ""),
                    "TradeID": event.TradeID,
                    "Sequence": sequence,
                    "PreTradeCount": count,
                    "PreDailyNet": daily_net,
                })
                count += 1
            elif event.Kind == "Rollback":
                attempts = active_attempts.get(trade_id, [])
                if attempts:
                    attempts.pop()
                    count = max(0, count - 1)
            elif event.Kind == "AccountPnl":
                entry_sequence = getattr(event, "EntrySequence", None)
                if entry_sequence is None:
                    should_apply = bool(active_attempts.get(trade_id)) or any(
                        item for item in accepted_attempts if item == sequence
                    )
                else:
                    should_apply = entry_sequence in accepted_attempts
                if should_apply:
                    daily_net += float(getattr(event, "Net", 0.0))
    return pd.DataFrame(accepted_rows)


def _read_archive_files(archive_root: Path, archive: str, suffix: str) -> pd.DataFrame:
    files = sorted((archive_root / archive).rglob(f"*_{suffix}.csv"))
    if not files:
        raise FileNotFoundError(f"No {suffix} files in {archive_root / archive}")
    return pd.concat([pd.read_csv(path) for path in files], ignore_index=True)


def load_gate_events(archive_root: Path) -> tuple[pd.DataFrame, pd.DataFrame]:
    """Load event-ordered counter and PnL rows from the five frozen archives."""
    baseline = BASELINE.load_baseline(archive_root)
    snapshots = set(baseline["SnapshotID"])
    accounts = []
    decisions = []
    events = []
    for archive in BASELINE.DEFAULT_ARCHIVES:
        account = _read_archive_files(archive_root, archive, "live_account_pnl")
        decision = _read_archive_files(archive_root, archive, "execution_decisions")
        execution_event = _read_archive_files(archive_root, archive, "execution_events")
        account["ArchiveName"] = archive
        decision["ArchiveName"] = archive
        execution_event["ArchiveName"] = archive
        execution_event["Sequence"] = execution_event.groupby("SnapshotID", sort=False).cumcount()
        accounts.append(account)
        decisions.append(decision)
        events.append(execution_event)
    accounts = pd.concat(accounts, ignore_index=True)
    decisions = pd.concat(decisions, ignore_index=True)
    events = pd.concat(events, ignore_index=True)
    accounts = accounts.loc[accounts["SnapshotID"].isin(snapshots)].copy()
    decisions = decisions.loc[decisions["SnapshotID"].isin(snapshots)].copy()
    events = events.loc[events["SnapshotID"].isin(snapshots)].copy()

    entry_order = events.loc[events["Event"].eq("ENTRY_SEND"), ["SnapshotID", "TradeID", "Time", "Sequence"]].copy()
    entry_order["Attempt"] = entry_order.groupby(["SnapshotID", "TradeID"], sort=False).cumcount()
    recorded_entries = decisions.loc[
        decisions["Decision"].eq("Execute") & decisions["TradeID"].notna() & decisions["TradeID"].ne(""),
        ["SnapshotID", "TradeID", "DailyTradeCount"],
    ].drop_duplicates(["SnapshotID", "TradeID"], keep="last")
    entries = entry_order.merge(recorded_entries, on=["SnapshotID", "TradeID"], how="left", validate="many_to_one")
    entries.loc[entries["Attempt"].gt(0), "DailyTradeCount"] = pd.NA
    entries["Kind"] = "Execute"
    entries["Reason"] = ""
    entries = entries.rename(columns={"DailyTradeCount": "RecordedDailyTradeCount"})

    rollbacks = events.loc[events["Event"].map(is_counter_rollback), ["SnapshotID", "TradeID", "Time", "Sequence"]].copy()
    rollbacks["Kind"] = "Rollback"
    rollbacks["Reason"] = ""
    rollbacks["RecordedDailyTradeCount"] = pd.NA

    pnl_order = events.loc[events["Event"].eq("LIVE_ACCOUNT_PNL"), ["SnapshotID", "TradeID", "Time", "Sequence"]]
    pnl = accounts.loc[:, ["SnapshotID", "TradeID", "Classification", "NetPnLDollars"]].merge(
        pnl_order, on=["SnapshotID", "TradeID"], how="inner", validate="one_to_one"
    )
    pnl["_PnlRow"] = range(len(pnl))
    pnl = pnl.merge(
        entry_order.loc[:, ["SnapshotID", "TradeID", "Sequence"]].rename(columns={"Sequence": "EntrySequence"}),
        on=["SnapshotID", "TradeID"], how="left", validate="one_to_many"
    )
    pnl = pnl.loc[pnl["EntrySequence"].lt(pnl["Sequence"])].sort_values(["_PnlRow", "EntrySequence"])
    pnl = pnl.drop_duplicates("_PnlRow", keep="last").drop(columns="_PnlRow")
    pnl["Kind"] = "AccountPnl"
    pnl["Reason"] = ""
    pnl["RecordedDailyTradeCount"] = pd.NA
    pnl = pnl.rename(columns={"NetPnLDollars": "Net"})

    skip_events = events.loc[
        events["Event"].isin(["SKIP_LIVE_DAILY_LOSS", "SKIP_DAILY_LIMIT"]),
        ["SnapshotID", "TradeID", "Time", "Sequence", "Message"],
    ].copy()
    skip_events["Kind"] = "Decision"
    skip_events["Reason"] = skip_events.pop("Message")
    skip_events["RecordedDailyTradeCount"] = pd.NA
    timeline = pd.concat([entries, rollbacks, pnl, skip_events], ignore_index=True, sort=False)
    return timeline, accounts


def summarize_frozen_gate_simulation(timeline: pd.DataFrame, max_trades: int, loss_limit: float) -> dict:
    accepted = simulate_frozen_gates(timeline, max_trades=max_trades, loss_limit=loss_limit)
    accepted_keys = set(zip(accepted["SnapshotID"], accepted["TradeID"], accepted["Sequence"]))
    pnl = timeline.loc[timeline["Kind"].eq("AccountPnl")].copy()
    pnl = pnl.loc[pnl.apply(lambda row: (row.SnapshotID, row.TradeID, row.EntrySequence) in accepted_keys, axis=1)]
    normal = pnl.loc[pnl["Classification"].eq("Normal")]
    return {
        "MaxTrades": max_trades,
        "LossLimit": loss_limit,
        "NormalTrades": int(len(normal)),
        "NormalNetDollars": round(float(normal["Net"].sum()), 2),
        "AllAccountNetDollars": round(float(pnl["Net"].sum()), 2),
    }


def select_frozen_normal_baseline(
    timeline: pd.DataFrame, baseline: pd.DataFrame, max_trades: int, loss_limit: float
) -> pd.DataFrame:
    """Return recorded Normal Actual trades whose originating entry remains accepted."""
    accepted = simulate_frozen_gates(timeline, max_trades=max_trades, loss_limit=loss_limit)
    accepted_keys = set(zip(accepted["SnapshotID"], accepted["TradeID"], accepted["Sequence"]))
    pnl = timeline.loc[timeline["Kind"].eq("AccountPnl")].copy()
    pnl = pnl.loc[
        pnl.apply(lambda row: (row.SnapshotID, row.TradeID, row.EntrySequence) in accepted_keys, axis=1)
        & pnl["Classification"].eq("Normal")
    ]
    keys = pnl.loc[:, ["SnapshotID", "TradeID"]].drop_duplicates()
    return baseline.merge(keys, on=["SnapshotID", "TradeID"], how="inner", validate="one_to_one")


def validate_frozen_baseline(timeline: pd.DataFrame, accounts: pd.DataFrame) -> dict:
    states = replay_gate_states(timeline)
    normal = accounts.loc[accounts["Classification"].eq("Normal")].copy()
    entries = states.loc[states["Kind"].eq("Execute")].copy()
    normal_ids = set(zip(normal["SnapshotID"], normal["TradeID"]))
    normal_entries = entries.loc[entries.apply(lambda row: (row.SnapshotID, row.TradeID) in normal_ids, axis=1)]
    recorded_entries = entries.loc[entries["RecordedDailyTradeCount"].notna()]
    recorded_count_mismatches = int((recorded_entries["PreTradeCount"] != recorded_entries["RecordedDailyTradeCount"]).sum())
    daily_limit = states.loc[states["Reason"].astype(str).str.startswith("max=15")]
    live_loss = states.loc[states["Reason"].astype(str).str.startswith("LiveDailyLoss:")]
    return {
        "NormalTrades": int(len(normal)),
        "NormalNetDollars": round(float(normal["NetPnLDollars"].sum()), 2),
        "NormalEntryRows": int(len(normal_entries)),
        "RecordedCountMismatches": recorded_count_mismatches,
        "DailyLimitSkipRows": int(len(daily_limit)),
        "DailyLimitStateViolations": int((daily_limit["PreTradeCount"] < 15).sum()),
        "LiveLossSkipRows": int(len(live_loss)),
        "LiveLossStateViolations": int((live_loss["PreDailyNet"] > -250).sum()),
    }


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--archive-root", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    args = parser.parse_args()
    timeline, accounts = load_gate_events(args.archive_root)
    states = replay_gate_states(timeline)
    summary = validate_frozen_baseline(timeline, accounts)
    simulation = summarize_frozen_gate_simulation(timeline, max_trades=15, loss_limit=250.0)
    if summary["NormalTrades"] != 2047 or summary["NormalNetDollars"] != 19904.30 or simulation["NormalTrades"] != 2047 or simulation["NormalNetDollars"] != 19904.30:
        raise ValueError(f"Frozen baseline mismatch: {summary}")
    args.output_dir.mkdir(parents=True, exist_ok=True)
    timeline.to_csv(args.output_dir / "v500_daily_gate_event_timeline.csv", index=False, encoding="utf-8")
    states.to_csv(args.output_dir / "v500_daily_gate_states.csv", index=False, encoding="utf-8")
    pd.DataFrame([{**summary, **simulation}]).to_csv(args.output_dir / "v500_daily_gate_validation.csv", index=False, encoding="utf-8")
    print(pd.DataFrame([{**summary, **simulation}]).to_string(index=False))


if __name__ == "__main__":
    main()
