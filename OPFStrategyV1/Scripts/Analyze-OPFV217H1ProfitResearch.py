import argparse
import importlib.util
from pathlib import Path

import pandas as pd


OLD_EXACT_GROSS_10M = 7636.05
OLD_H1_POINT_GROSS = 24619.79
OLD_H1_CONSERVATIVE_GROSS = 19087.35


def load_module(path: Path):
    spec = importlib.util.spec_from_file_location("opf_v217_profit_connector", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def profit_factor(gross: pd.Series) -> float:
    wins = float(gross[gross > 0].sum())
    losses = -float(gross[gross < 0].sum())
    return wins / losses if losses > 0 else float("inf")


def summarize(trades: pd.DataFrame, arm: str, diagnostics: dict, deployable: bool) -> dict:
    gross = float(trades["Gross"].sum()) if not trades.empty else 0.0
    net = float(trades["Net"].sum()) if not trades.empty else 0.0
    dates = pd.to_datetime(trades["TradingDate"])
    blocks = {
        "JanFebGross": float(trades[dates.dt.month.le(2)]["Gross"].sum()),
        "MarAprGross": float(trades[dates.dt.month.between(3, 4)]["Gross"].sum()),
        "MayJunGross": float(trades[dates.dt.month.ge(5)]["Gross"].sum()),
    }
    daily = trades.assign(Date=dates.dt.strftime("%Y-%m-%d")).groupby("Date")["Gross"].sum()
    return {
        "Arm": arm,
        "Deployable": deployable,
        "Trades": len(trades),
        "SecondaryTrades": int(trades["Lane"].eq("Secondary").sum()),
        "Gross": round(gross, 2),
        "Net": round(net, 2),
        "PF": round(profit_factor(trades["Gross"]), 4),
        "PositiveSampleDayPct": round(float(daily.gt(0).mean() * 100.0), 2),
        "WorstSampleDay": round(float(daily.min()), 2),
        "BestSampleDay": round(float(daily.max()), 2),
        **{key: round(value, 2) for key, value in blocks.items()},
        "ProjectedH1PointGross": round(OLD_H1_POINT_GROSS * gross / OLD_EXACT_GROSS_10M, 2),
        "ProjectedH1ConservativeGross": round(
            OLD_H1_CONSERVATIVE_GROSS * gross / OLD_EXACT_GROSS_10M, 2
        ),
        **diagnostics,
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--candidate-tape", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument(
        "--grid",
        choices=("coarse", "fine", "relation", "deployable"),
        default="coarse",
    )
    args = parser.parse_args()

    connector = load_module(
        Path(__file__).with_name("Analyze-OPFV217DualSlotCalibration.py")
    )
    tape = pd.read_csv(
        args.candidate_tape,
        parse_dates=["EntryTime", "PredictedExitTime", "TradingDate"],
        low_memory=False,
    )

    rows = []
    traces = []
    if args.grid == "deployable":
        daily_caps = (14, 15, 16, 17, 18)
        daily_losses = (250.0, 300.0, 350.0, 400.0, 450.0)
        risk_caps = (270.0, 300.0, 330.0)
        score_floors = (1.0, 2.0, 3.0, 4.0)
        opposite_score_floors = (None,)
        direction_modes = (True,)
    elif args.grid == "relation":
        daily_caps = (15,)
        daily_losses = (300.0,)
        risk_caps = (300.0,)
        score_floors = (1.0, 2.0, 3.0, 4.0)
        opposite_score_floors = (0.0, 1.0, 2.0, 3.0, 4.0)
        direction_modes = (False,)
    elif args.grid == "fine":
        daily_caps = (14, 15, 16)
        daily_losses = (250.0, 300.0, 350.0, 400.0)
        risk_caps = (270.0, 300.0, 330.0)
        score_floors = (1.0, 2.0, 3.0)
        opposite_score_floors = (None,)
        direction_modes = (False,)
    else:
        daily_caps = (12, 15, 18, 21, 0)
        daily_losses = (300.0, 450.0, 600.0, 750.0)
        risk_caps = (240.0, 300.0, 360.0, 450.0)
        score_floors = (0.0, 2.0, 4.0, 6.0)
        opposite_score_floors = (None,)
        direction_modes = (True, False)

    for daily_cap in daily_caps:
        for daily_loss in daily_losses:
            for combined_risk_cap in risk_caps:
                for score_floor in score_floors:
                    for opposite_score_floor in opposite_score_floors:
                        for same_direction in direction_modes:
                            relation_suffix = (
                                f"SameG{score_floor:g}_OppG{opposite_score_floor:g}"
                                if opposite_score_floor is not None
                                else f"G{score_floor:g}_{'Same' if same_direction else 'Any'}"
                            )
                            arm = (
                                f"Cap{daily_cap or 'Inf'}_Loss{daily_loss:g}_Risk{combined_risk_cap:g}_"
                                f"{relation_suffix}"
                            )
                            _, accepted, diagnostics = connector.simulate(
                                tape,
                                score_floor,
                                same_direction,
                                daily_cap=daily_cap,
                                daily_loss=daily_loss,
                                combined_risk_cap=combined_risk_cap,
                                opposite_score_floor=opposite_score_floor,
                            )
                            deployable = same_direction and opposite_score_floor is None
                            rows.append(
                                summarize(accepted, arm, diagnostics, deployable)
                            )
                            if arm == "Cap18_Loss450_Risk300_G2_Same":
                                trace = accepted.copy()
                                trace["Arm"] = arm
                                traces.append(trace)

    result = pd.DataFrame(rows)
    result["AllBiMonthsPositive"] = result[
        ["JanFebGross", "MarAprGross", "MayJunGross"]
    ].gt(0).all(axis=1)
    result["ResearchGatePassed"] = (
        result["Deployable"]
        & result["ProjectedH1ConservativeGross"].ge(20000.0)
        & result["PF"].ge(1.35)
        & result["AllBiMonthsPositive"]
    )
    result = result.sort_values(
        ["ResearchGatePassed", "ProjectedH1ConservativeGross", "PF", "Trades"],
        ascending=[False, False, False, True],
    )

    args.output_dir.mkdir(parents=True, exist_ok=True)
    result.to_csv(args.output_dir / "constraint_grid.csv", index=False)
    if traces:
        pd.concat(traces, ignore_index=True).to_csv(
            args.output_dir / "current_baseline_trades.csv", index=False
        )
    print(result.head(25).to_string(index=False))


if __name__ == "__main__":
    main()
