"""Stage-5 Sweep-Reclaim portfolio screen. Data Only labels only; never an Actual PnL replica."""

import argparse
import csv
import datetime as dt
import glob
import json
import os
from collections import Counter, defaultdict
from pathlib import Path

POINT_VALUE = 2.0
CONTRACTS = 3
ROUND_TRIP_COMMISSION = 1.2 * CONTRACTS
DAILY_LOSS_LIMIT = -250.0
DAILY_TRADE_CAP = 15
CONCURRENT_RISK_CAP = 300.0


def percentile(values, fraction):
    ordered = sorted(values)
    return ordered[int((len(ordered) - 1) * fraction)]


def session_month(session):
    counts = Counter(row["month"] for row in session)
    return max(counts, key=counts.get)


def read_rows(directory):
    candidates = {}
    outcomes = []
    for path in glob.glob(str(directory / "*_sweep_reclaim_candidates.csv")):
        with open(path, encoding="utf-8-sig", newline="") as handle:
            candidates.update({row["SignalID"]: row for row in csv.DictReader(handle)})
    for path in glob.glob(str(directory / "*_sweep_reclaim_outcomes.csv")):
        with open(path, encoding="utf-8-sig", newline="") as handle:
            outcomes.extend(csv.DictReader(handle))

    sessions = defaultdict(list)
    for outcome in outcomes:
        candidate = candidates[outcome["SignalID"]]
        if outcome["DeterministicOutcome"].lower() != "true":
            continue
        if candidate["EstimatedRiskInAuditRange"].lower() != "true":
            continue
        entry = float(outcome["Entry"])
        exit_price = float(outcome["ExitPrice"])
        side = candidate["Side"]
        points = exit_price - entry if side == "Long" else entry - exit_price
        entry_time = dt.datetime.fromisoformat(outcome["EntryTime"])
        sessions[outcome["SnapshotID"]].append({
            "snapshot": outcome["SnapshotID"],
            "month": entry_time.strftime("%Y-%m"),
            "entry_time": entry_time,
            "entry_bar": int(outcome["EntryBar"]),
            "exit_bar": int(outcome["ExitBar"]),
            "side": side,
            "risk_points": float(outcome["RiskPoints"]),
            "depth": float(candidate["SweepDepthPoints"]),
            "counter_long": float(candidate["BuyVolume60"]) <= float(candidate["SellVolume60"]),
            "gross": points * POINT_VALUE * CONTRACTS,
            "net": points * POINT_VALUE * CONTRACTS - ROUND_TRIP_COMMISSION,
        })
    return sorted(sessions.values(), key=lambda items: min(item["entry_time"] for item in items))


def simulate(sessions, allow, slots=1):
    trades, daily_rows = [], []
    for session in sessions:
        active, day_net, day_trades = [], 0.0, 0
        blocked = defaultdict(int)

        def settle_before(bar):
            nonlocal active, day_net
            finished = [item for item in active if item["exit_bar"] < bar]
            active = [item for item in active if item["exit_bar"] >= bar]
            day_net += sum(item["net"] for item in finished)

        for item in sorted(session, key=lambda row: (row["entry_bar"], row["entry_time"], row["side"])):
            settle_before(item["entry_bar"])
            if not allow(item):
                blocked["filter"] += 1
            elif day_net <= DAILY_LOSS_LIMIT:
                blocked["daily_loss"] += 1
            elif day_trades >= DAILY_TRADE_CAP:
                blocked["daily_cap"] += 1
            elif len(active) >= slots:
                blocked["active_trade"] += 1
            elif (item["risk_points"] + sum(row["risk_points"] for row in active)) * POINT_VALUE * CONTRACTS > CONCURRENT_RISK_CAP:
                blocked["concurrent_risk"] += 1
            else:
                active.append(item)
                trades.append(item)
                day_trades += 1
        settle_before(10**9)
        daily_rows.append({
            "month": session_month(session),
            "date": min(item["entry_time"] for item in session).date().isoformat(),
            "trades": day_trades,
            "net": day_net,
            "blocked": dict(blocked),
        })
    return trades, daily_rows


def metrics(name, trades, daily_rows, scope):
    gross = sum(row["gross"] for row in trades)
    net = sum(row["net"] for row in trades)
    profit = sum(row["net"] for row in trades if row["net"] > 0)
    loss = -sum(row["net"] for row in trades if row["net"] < 0)
    return {
        "policy": name,
        "scope": scope,
        "trades": len(trades),
        "gross": round(gross, 2),
        "net": round(net, 2),
        "pf": round(profit / loss, 4) if loss else 0.0,
        "win_rate": round(sum(row["net"] > 0 for row in trades) / len(trades), 4) if trades else 0.0,
        "worst_day": round(min((row["net"] for row in daily_rows), default=0.0), 2),
    }


def run_policy(name, sessions, allow, output, scope):
    for slots in (1, 2):
        trades, daily = simulate(sessions, allow, slots)
        row = metrics(name, trades, daily, scope)
        row["slots"] = slots
        output.append(row)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--log-dir", type=Path, default=Path(os.environ["APPDATA"]) / "ATAS" / "StrategyLogs" / "OPFStrategyV1")
    parser.add_argument("--output-dir", type=Path, default=Path("OPFStrategyV1/Reports/v300_stage5_sweep_reclaim_portfolio"))
    args = parser.parse_args()
    sessions = read_rows(args.log_dir)
    h1 = [session for session in sessions if session_month(session) < "2026-07"]
    july = [session for session in sessions if session_month(session) == "2026-07"]
    output = []

    long_all = lambda row: row["side"] == "Long"
    long_counter = lambda row: row["side"] == "Long" and row["counter_long"]
    run_policy("LongAll", h1, long_all, output, "H1")
    run_policy("LongAll", july, long_all, output, "JulyStress")
    run_policy("LongCounter60", h1, long_counter, output, "H1")
    run_policy("LongCounter60", july, long_counter, output, "JulyStress")

    # Depth cutoffs are trained only on H1; July is never used to pick them.
    h1_depths = [row["depth"] for session in h1 for row in session if row["side"] == "Long"]
    for quantile in (0.25, 0.50, 0.75):
        cutoff = percentile(h1_depths, quantile)
        policy_name = f"LongCounter60_DepthLE_Q{int(quantile * 100)}"
        policy = lambda row, cutoff=cutoff: row["side"] == "Long" and row["counter_long"] and row["depth"] <= cutoff
        run_policy(policy_name, h1, policy, output, "H1")
        run_policy(policy_name, july, policy, output, "JulyStress")

    # Leave-one-month report uses fixed volume direction and train-only depth cutoffs.
    lomo = []
    months = sorted({session_month(session) for session in h1})
    for held_out in months:
        train = [session for session in h1 if session_month(session) != held_out]
        test = [session for session in h1 if session_month(session) == held_out]
        train_depths = [row["depth"] for session in train for row in session if row["side"] == "Long"]
        for quantile in (0.25, 0.50, 0.75):
            cutoff = percentile(train_depths, quantile)
            policy = lambda row, cutoff=cutoff: row["side"] == "Long" and row["counter_long"] and row["depth"] <= cutoff
            trades, daily = simulate(test, policy, 1)
            row = metrics(f"LongCounter60_DepthLE_Q{int(quantile * 100)}", trades, daily, held_out)
            row["train_depth_cutoff"] = cutoff
            lomo.append(row)

    args.output_dir.mkdir(parents=True, exist_ok=True)
    with open(args.output_dir / "portfolio_summary.csv", "w", newline="", encoding="utf-8") as handle:
        writer = csv.DictWriter(handle, fieldnames=list(output[0]))
        writer.writeheader()
        writer.writerows(output)
    with open(args.output_dir / "lomo_summary.csv", "w", newline="", encoding="utf-8") as handle:
        writer = csv.DictWriter(handle, fieldnames=list(lomo[0]))
        writer.writeheader()
        writer.writerows(lomo)
    with open(args.output_dir / "metadata.json", "w", encoding="utf-8") as handle:
        json.dump({"sessions": len(sessions), "h1_sessions": len(h1), "july_sessions": len(july), "contracts": CONTRACTS, "commission": ROUND_TRIP_COMMISSION}, handle, indent=2)


if __name__ == "__main__":
    main()
