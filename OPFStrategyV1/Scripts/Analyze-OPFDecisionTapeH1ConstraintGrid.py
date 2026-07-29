import argparse
import importlib.util
import itertools
from pathlib import Path

import pandas as pd


def load_module(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def segment_gross(trades: pd.DataFrame, months: tuple[str, str]) -> float:
    return round(float(trades.loc[trades["Month"].isin(months), "Gross3"].sum()), 2)


def run_grid(tape: pd.DataFrame, engine, rich_module, bars: dict) -> tuple[pd.DataFrame, pd.DataFrame]:
    rows = []
    top_trades = []
    all_dates = sorted(tape["TradingDate"].unique())
    caps = [8, 10, 15, 999]
    losses = [300.0, 600.0, 99999.0]
    deferred_options = [False, True]
    risk_bands = [[(13.25, 16.0)]]
    drop_long_options = [{"FailureReverse_RetestFailed"}]
    deferred_outcome_cache = {}
    deferred_rr_cache = {}
    prepared_days = engine.prepare_simulation_days(tape, rich_module, bars)

    for cap, loss, deferred, bands, drop_long in itertools.product(
        caps, losses, deferred_options, risk_bands, drop_long_options
    ):
        config = {
            "DailyCap": cap,
            "DailyLoss": loss,
            "DeferredEnabled": deferred,
            "DeferredBars": 18,
            "DeferredTargetR": 3.0,
            "DropLongPaths": drop_long,
            "DropShortPaths": set(),
            "ExcludedRiskBands": bands,
            "MinSetupQuality": 0.0,
            "MinEstimatedRR": 0.0,
            "BaselineMode": False,
            "ReleaseOnEqual": False,
            "DeferredOutcomeCache": deferred_outcome_cache,
            "DeferredRRCache": deferred_rr_cache,
            "PreparedDays": prepared_days,
        }
        trades, diagnostics = engine.simulate_policy(tape, rich_module, bars, config)
        metrics = engine.policy_metrics(trades, all_dates)
        metrics.update(
            {
                "DailyCap": "Unlimited" if cap == 999 else cap,
                "DailyLoss": "Unlimited" if loss == 99999 else loss,
                "DeferredEnabled": deferred,
                "RiskBand": "13.25-16" if bands else "None",
                "DropLongFRRetest": bool(drop_long),
                "JanFebGross": segment_gross(trades, ("2026-01", "2026-02")),
                "MarAprGross": segment_gross(trades, ("2026-03", "2026-04")),
                "MayJunGross": segment_gross(trades, ("2026-05", "2026-06")),
                "CounterfactualGrossSharePct": round(
                    float(metrics["CounterfactualGross"] / metrics["Gross"] * 100), 2
                )
                if metrics["Gross"]
                else 0.0,
                **diagnostics,
            }
        )
        rows.append(metrics)
        if metrics["Gross"] >= 18000 and metrics["PF"] >= 1.25:
            tagged = trades.copy()
            tagged["Config"] = (
                f"cap={metrics['DailyCap']}|loss={metrics['DailyLoss']}|deferred={deferred}"
                f"|risk={metrics['RiskBand']}|dropLongFR={bool(drop_long)}"
            )
            top_trades.append(tagged)
    return pd.DataFrame(rows), pd.concat(top_trades, ignore_index=True) if top_trades else pd.DataFrame()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--rich", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    args = parser.parse_args()

    engine = load_module(
        "opf_decision_tape_engine",
        Path(__file__).with_name("Analyze-OPFDecisionTapePortfolio.py"),
    )
    rich_module = load_module(
        "opf_v209_execution_calibrated_grid",
        Path(__file__).with_name("Analyze-OPFV209H1ExecutionCalibratedPortfolio.py"),
    )
    tape = engine.load_decision_tape(args.evidence, "H1")
    bars = rich_module.bars_by_trading_date(args.rich)
    policy_tape = engine.prepare_policy_tape(tape, rich_module, bars)
    summary, top_trades = run_grid(policy_tape, engine, rich_module, bars)
    summary.sort_values(["Gross", "PF"], ascending=False, inplace=True)
    args.output_dir.mkdir(parents=True, exist_ok=True)
    summary.to_csv(args.output_dir / "constraint_grid_summary.csv", index=False)
    if not top_trades.empty:
        top_trades.to_csv(args.output_dir / "constraint_grid_top_trades.csv", index=False)
    print(summary.head(25).to_string(index=False))


if __name__ == "__main__":
    main()
