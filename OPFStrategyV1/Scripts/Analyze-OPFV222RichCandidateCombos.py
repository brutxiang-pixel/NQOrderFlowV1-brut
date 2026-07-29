import argparse
import importlib.util
from pathlib import Path

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
    blocked = submissions[submissions["SubmissionStatus"].eq("PreflightAborted")]
    abort_keys = frozenset(blocked["SignalID"] + "|" + blocked["ResearchPath"] + "|" + blocked["Lane"])
    return tape, abort_keys


def metrics(trades):
    dates = pd.to_datetime(trades["TradingDate"])
    monthly = trades.assign(Month=dates.dt.strftime("%Y-%m")).groupby("Month")["Net"].sum()
    gross = trades["Gross"]
    wins = float(gross[gross > 0].sum())
    losses = -float(gross[gross < 0].sum())
    weekly = trades.assign(Week=dates.dt.to_period("W-SUN").astype(str)).groupby("Week")["Net"].sum()
    daily = trades.assign(Date=dates.dt.strftime("%Y-%m-%d")).groupby("Date")["Net"].sum()
    drawdown = daily.cumsum().cummax() - daily.cumsum()
    return {
        "Trades": len(trades), "Gross": round(float(gross.sum()), 2), "Net": round(float(trades["Net"].sum()), 2),
        "PF": round(wins / losses, 4), "PositiveWeeks": int(weekly.gt(0).sum()),
        "WorstWeek": round(float(weekly.min()), 2), "MaxDD": round(float(drawdown.max()), 2), "Monthly": monthly,
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--h1-tape", type=Path, required=True)
    parser.add_argument("--july-tape", type=Path, required=True)
    parser.add_argument("--rich", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    args = parser.parse_args()
    scripts = Path(__file__).parent
    v220 = load_module(scripts / "Analyze-OPFV220H1LatchedProfitResearch.py", "v220")
    connector = load_module(scripts / "Analyze-OPFV217DualSlotCalibration.py", "connector")
    rich_analysis = load_module(scripts / "Analyze-OPFV222RichEntryLomo.py", "rich_analysis")
    h1, h1_abort = load_tape(args.h1_tape)
    july, july_abort = load_tape(args.july_tape)
    h1 = rich_analysis.add_rich(h1, load_module(scripts / "Connect-OPFRichFeatures.py", "rich"), args.rich)

    def gate(long_regime_min, breakaway_mode, short_regime):
        def blocks(row, lane):
            if lane != "Primary":
                return False
            if long_regime_min is not None and row.Side == "Long" and row.ResearchPath == "ObservationConfirm":
                return bool(row.RichAvailable) and float(row.RichRegimeBars) < long_regime_min
            if breakaway_mode is not None and row.Side == "Short" and row.ResearchPath == "BreakawayFvg":
                if breakaway_mode == "QualityRisk":
                    return float(row.SetupQualityScore) < 88.0 or float(row.ExactRisk) < 12.0
                if breakaway_mode == "VwapSide":
                    return bool(row.RichAvailable) and float(row.RichAlignedVWAPSidePassed) < 1.0
                if breakaway_mode == "Both":
                    return (
                        float(row.SetupQualityScore) < 88.0
                        or float(row.ExactRisk) < 12.0
                        or (bool(row.RichAvailable) and float(row.RichAlignedVWAPSidePassed) < 1.0)
                    )
                raise ValueError(f"Unexpected Breakaway mode: {breakaway_mode}")
            if short_regime and row.Side == "Short" and row.ResearchPath == "ObservationConfirm":
                return bool(row.RichAvailable) and float(row.RichRegimeBars) < 2.0
            return False
        return blocks

    variants = [
        (threshold, breakaway_mode, False)
        for threshold in (None, 3.0, 5.0, 8.0)
        for breakaway_mode in (None, "QualityRisk", "VwapSide", "Both")
    ]
    base_trades, _ = v220.simulate(h1, connector, v220.Policy(zone_birth_min_quality=45.0), h1_abort)
    base = metrics(base_trades)
    rows = []
    details = []
    for long_regime, breakaway, short_regime in variants:
        regime_name = "Off" if long_regime is None else f"{long_regime:g}"
        breakaway_name = "Off" if breakaway is None else breakaway
        name = f"LongOCRegimeMin={regime_name};Breakaway={breakaway_name};ShortOCRegime2={short_regime}"
        trades, blocked = v220.simulate(
            h1, connector, v220.Policy(zone_birth_min_quality=45.0), h1_abort,
            candidate_gate=gate(long_regime, breakaway, short_regime),
        )
        result = metrics(trades)
        delta = result["Monthly"].sub(base["Monthly"], fill_value=0)
        rows.append({
            "Variant": name, "H1Trades": result["Trades"], "H1Gross": result["Gross"], "H1Net": result["Net"],
            "H1NetDelta": round(result["Net"] - base["Net"], 2), "H1PF": result["PF"],
            "PositiveWeeks": result["PositiveWeeks"], "WorstWeek": result["WorstWeek"], "MaxDD": result["MaxDD"],
            "PositiveDeltaMonths": int(delta.gt(0).sum()), "NegativeDeltaMonths": int(delta.lt(0).sum()),
            **{f"Delta_{month}": round(float(delta.get(month, 0.0)), 2) for month in ("2026-01", "2026-02", "2026-03", "2026-04", "2026-05", "2026-06")},
            **{f"Blocked_{key}": value for key, value in blocked.items()},
        })
        details.append(trades.assign(Variant=name))
    # 7月没有当前管线的Rich Bar，故只验证不依赖Rich的Breakaway门禁；其余Rich门禁不伪造OOS结果。
    july_base, _ = v220.simulate(july, connector, v220.Policy(zone_birth_min_quality=45.0), july_abort)
    july_breakaway, _ = v220.simulate(july, connector, v220.Policy(zone_birth_min_quality=45.0), july_abort,
                                        candidate_gate=gate(None, "QualityRisk", False))
    result = pd.DataFrame(rows).sort_values("H1NetDelta", ascending=False)
    all_trades = pd.concat(details, ignore_index=True)
    monthly = all_trades.assign(Month=pd.to_datetime(all_trades["TradingDate"]).dt.strftime("%Y-%m")).groupby(
        ["Variant", "Month"]
    )["Net"].sum().unstack(fill_value=0.0)
    totals = all_trades.groupby("Variant")["Net"].sum()
    baseline_name = "LongOCRegimeMin=Off;Breakaway=Off;ShortOCRegime2=False"
    lomo_rows = []
    for month in monthly.columns:
        train = totals - monthly[month]
        chosen = train.idxmax()
        lomo_rows.append({
            "TestMonth": month,
            "ChosenVariant": chosen,
            "TrainNet": round(float(train[chosen]), 2),
            "ChosenTestNet": round(float(monthly.loc[chosen, month]), 2),
            "BaselineTestNet": round(float(monthly.loc[baseline_name, month]), 2),
            "TestNetDelta": round(float(monthly.loc[chosen, month] - monthly.loc[baseline_name, month]), 2),
        })
    lomo = pd.DataFrame(lomo_rows)
    args.output_dir.mkdir(parents=True, exist_ok=True)
    result.to_csv(args.output_dir / "combo_grid.csv", index=False)
    all_trades.to_csv(args.output_dir / "combo_trades.csv", index=False)
    lomo.to_csv(args.output_dir / "combo_lomo_selection.csv", index=False)
    pd.DataFrame([{
        "LomoNet": round(float(lomo["ChosenTestNet"].sum()), 2),
        "BaselineNet": round(float(lomo["BaselineTestNet"].sum()), 2),
        "LomoNetDelta": round(float(lomo["TestNetDelta"].sum()), 2),
        "DistinctSelectedVariants": int(lomo["ChosenVariant"].nunique()),
    }]).to_csv(args.output_dir / "combo_lomo_summary.csv", index=False)
    pd.DataFrame([{
        "JulyBaselineNet": metrics(july_base)["Net"], "JulyBreakawayOnlyNet": metrics(july_breakaway)["Net"],
        "JulyBreakawayOnlyDelta": round(metrics(july_breakaway)["Net"] - metrics(july_base)["Net"], 2),
        "RichJulyStatus": "Unavailable: no current-pipeline July Rich Bar archive; no Rich OOS claim",
    }]).to_csv(args.output_dir / "july_scope.csv", index=False)
    print(result.to_string(index=False))


if __name__ == "__main__":
    main()
