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


def segment(trades: pd.DataFrame, months: tuple[str, str]) -> float:
    normal = trades[trades["CountsNormal"]]
    return round(float(normal.loc[normal["Month"].isin(months), "Gross3"].sum()), 2)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--rich", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    args = parser.parse_args()

    engine = load_module(
        "opf_decision_tape_engine_combo",
        Path(__file__).with_name("Analyze-OPFDecisionTapePortfolio.py"),
    )
    rich_module = load_module(
        "opf_v209_execution_calibrated_combo",
        Path(__file__).with_name("Analyze-OPFV209H1ExecutionCalibratedPortfolio.py"),
    )
    raw = engine.load_decision_tape(args.evidence, "H1")
    bars = rich_module.bars_by_trading_date(args.rich)
    tape = engine.prepare_policy_tape(raw, rich_module, bars)
    prepared = engine.prepare_simulation_days(tape, rich_module, bars)
    dates = sorted(tape["TradingDate"].unique())
    candidates = [
        ("Short", "FailureReverse_ObservationInvalidated"),
        ("Short", "FailureReverse_RetestFailed_WideStop1_5R"),
        ("Short", "ShadowCandidate"),
        ("Long", "ObservationStrict_BullFresh_WideStop1_5R"),
        ("Short", "UnknownRegimeZoneTouch"),
    ]
    rows = []
    for size in range(len(candidates) + 1):
        for removed in itertools.combinations(candidates, size):
            for use_risk_band in (False, True):
                drop_long = {"FailureReverse_RetestFailed"}
                drop_short = set()
                for side, path in removed:
                    (drop_long if side == "Long" else drop_short).add(path)
                config = {
                    "DailyCap": 10,
                    "DailyLoss": 600.0,
                    "DeferredEnabled": False,
                    "DeferredBars": 18,
                    "DeferredTargetR": 3.0,
                    "DropLongPaths": drop_long,
                    "DropShortPaths": drop_short,
                    "ExcludedRiskBands": [(13.25, 16.0)] if use_risk_band else [],
                    "MinSetupQuality": 0.0,
                    "MinEstimatedRR": 0.0,
                    "BaselineMode": False,
                    "ReleaseOnEqual": False,
                    "PreparedDays": prepared,
                }
                trades, diagnostics = engine.simulate_policy(tape, rich_module, bars, config)
                result = engine.policy_metrics(trades, dates)
                rows.append(
                    {
                        "Removed": ";".join(f"{side}:{path}" for side, path in removed) or "None",
                        "RemovedCount": len(removed),
                        "RiskBand": use_risk_band,
                        **result,
                        "JanFebGross": segment(trades, ("2026-01", "2026-02")),
                        "MarAprGross": segment(trades, ("2026-03", "2026-04")),
                        "MayJunGross": segment(trades, ("2026-05", "2026-06")),
                        **diagnostics,
                    }
                )
    summary = pd.DataFrame(rows).sort_values(["NormalGross", "PF"], ascending=False)
    args.output_dir.mkdir(parents=True, exist_ok=True)
    summary.to_csv(args.output_dir / "path_combination_summary.csv", index=False)
    print(summary.head(30).to_string(index=False))


if __name__ == "__main__":
    main()
