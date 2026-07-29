import argparse
import importlib.util
from pathlib import Path

import pandas as pd


def load_module(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def segment_gross(trades: pd.DataFrame, months: tuple[str, str]) -> float:
    normal = trades[trades["CountsNormal"]]
    return round(float(normal.loc[normal["Month"].isin(months), "Gross3"].sum()), 2)


def metrics(engine, trades: pd.DataFrame, dates: list) -> dict:
    result = engine.policy_metrics(trades, dates)
    result.update(
        {
            "JanFebGross": segment_gross(trades, ("2026-01", "2026-02")),
            "MarAprGross": segment_gross(trades, ("2026-03", "2026-04")),
            "MayJunGross": segment_gross(trades, ("2026-05", "2026-06")),
        }
    )
    return result


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--rich", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    args = parser.parse_args()

    engine = load_module(
        "opf_decision_tape_engine_ablation",
        Path(__file__).with_name("Analyze-OPFDecisionTapePortfolio.py"),
    )
    rich_module = load_module(
        "opf_v209_execution_calibrated_ablation",
        Path(__file__).with_name("Analyze-OPFV209H1ExecutionCalibratedPortfolio.py"),
    )
    raw = engine.load_decision_tape(args.evidence, "H1")
    bars = rich_module.bars_by_trading_date(args.rich)
    tape = engine.prepare_policy_tape(raw, rich_module, bars)
    prepared_days = engine.prepare_simulation_days(tape, rich_module, bars)
    dates = sorted(tape["TradingDate"].unique())
    outcome_cache = {}
    rr_cache = {}
    base_configs = {
        "QualityCorner": {"DailyCap": 10, "DailyLoss": 600.0, "DeferredEnabled": False},
        "CapacityCorner": {"DailyCap": 15, "DailyLoss": 600.0, "DeferredEnabled": True},
    }
    rows = []
    contributions = []

    for name, base in base_configs.items():
        common = {
            **base,
            "DeferredBars": 18,
            "DeferredTargetR": 3.0,
            "ExcludedRiskBands": [(13.25, 16.0)],
            "MinSetupQuality": 0.0,
            "MinEstimatedRR": 0.0,
            "BaselineMode": False,
            "ReleaseOnEqual": False,
            "PreparedDays": prepared_days,
            "DeferredOutcomeCache": outcome_cache,
            "DeferredRRCache": rr_cache,
        }
        baseline_config = {
            **common,
            "DropLongPaths": {"FailureReverse_RetestFailed"},
            "DropShortPaths": set(),
        }
        baseline_trades, _ = engine.simulate_policy(tape, rich_module, bars, baseline_config)
        baseline_metrics = metrics(engine, baseline_trades, dates)
        rows.append({"Corner": name, "AblationSide": "-", "AblationPath": "Baseline", **baseline_metrics})
        grouped = (
            baseline_trades[baseline_trades["CountsNormal"]]
            .groupby(["Side", "ResearchPath", "Source"])
            .agg(Trades=("SignalID", "count"), Gross=("Gross3", "sum"), Net=("Net3", "sum"))
            .reset_index()
        )
        grouped["Corner"] = name
        contributions.append(grouped)

        paths = sorted(set(zip(tape["Side"], tape["ResearchPath"])))
        for side, path in paths:
            drop_long = {"FailureReverse_RetestFailed"}
            drop_short = set()
            if side == "Long":
                drop_long = drop_long | {path}
            else:
                drop_short.add(path)
            config = {**common, "DropLongPaths": drop_long, "DropShortPaths": drop_short}
            trades, _ = engine.simulate_policy(tape, rich_module, bars, config)
            result = metrics(engine, trades, dates)
            rows.append(
                {
                    "Corner": name,
                    "AblationSide": side,
                    "AblationPath": path,
                    **result,
                    "GrossDelta": round(result["NormalGross"] - baseline_metrics["NormalGross"], 2),
                    "PFDelta": round(result["PF"] - baseline_metrics["PF"], 4),
                }
            )

    summary = pd.DataFrame(rows).sort_values(["NormalGross", "PF"], ascending=False)
    args.output_dir.mkdir(parents=True, exist_ok=True)
    summary.to_csv(args.output_dir / "path_ablation_summary.csv", index=False)
    pd.concat(contributions, ignore_index=True).to_csv(
        args.output_dir / "path_contributions.csv", index=False
    )
    print(summary.head(30).to_string(index=False))


if __name__ == "__main__":
    main()
