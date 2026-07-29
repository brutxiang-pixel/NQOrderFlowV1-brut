import argparse
import importlib.util
from dataclasses import dataclass
from pathlib import Path

import numpy as np
import pandas as pd


def load_module(path: Path, name: str):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


@dataclass(frozen=True)
class Rule:
    side: str
    path: str
    feature: str
    operator: str
    threshold: float

    @property
    def name(self):
        return f"{self.side}_{self.path}_{self.feature}_{self.operator}{self.threshold:g}"

    def blocks(self, row, lane):
        if lane != "Primary" or row.Side != self.side or row.ResearchPath != self.path:
            return False
        if not bool(row.RichAvailable):
            return False
        value = float(getattr(row, self.feature))
        return value < self.threshold if self.operator == "min" else value > self.threshold


FEATURES = {
    "RichAlignedScore": ("min", (40, 60, 80)),
    "RichScoreDelta": ("min", (0, 20, 40)),
    "RichRelativeVolume20": ("min", (0.75, 1.0, 1.25, 1.5)),
    "RichAbsoluteVwapDistanceAtr": ("max", (0.5, 1.0, 1.5, 2.0)),
    "RichAlignedVwapDistanceAtr": ("min", (0.0, 0.5, 1.0)),
    "RichRegimeBars": ("min", (2, 4, 8, 12)),
    "RichAlignedSwingProgressionPassed": ("min", (1,)),
    "RichAlignedVWAPSidePassed": ("min", (1,)),
    "RichAlignedOpeningRangeSidePassed": ("min", (1,)),
    "RichAlignedVwapCrossCountPassed": ("min", (1,)),
    "RichAlignedDirectionalDisplacementPassed": ("min", (1,)),
    "RichAlignedAtrExpansionPassed": ("min", (1,)),
}


def load_tape(path):
    tape = pd.read_csv(path, low_memory=False)
    for column in ("EntryTime", "PredictedExitTime", "TradingDate"):
        tape[column] = pd.to_datetime(tape[column])
    submissions = pd.read_csv(path.with_name("submission_trace.csv"), low_memory=False)
    aborted = submissions[submissions["SubmissionStatus"].eq("PreflightAborted")]
    abort_keys = frozenset(aborted["SignalID"] + "|" + aborted["ResearchPath"] + "|" + aborted["Lane"])
    return tape, abort_keys


def add_rich(tape, rich_module, rich_root):
    rich, _ = rich_module.load_rich_features(rich_root)
    joined = tape.merge(
        rich,
        left_on=["EntryTime", "EntryBar"],
        right_on=["RichTime", "RichBar"],
        how="left",
        validate="many_to_one",
    )
    joined["RichAvailable"] = joined["RichTradingDate"].notna()
    is_long = joined["Side"].eq("Long")
    joined["RichAlignedScore"] = np.where(is_long, joined["RichBullScore"], joined["RichBearScore"])
    joined["RichScoreDelta"] = np.where(
        is_long,
        joined["RichBullScore"] - joined["RichBearScore"],
        joined["RichBearScore"] - joined["RichBullScore"],
    )
    joined["RichAlignedVwapDistanceAtr"] = np.where(
        is_long, joined["RichVwapDistanceAtr"], -joined["RichVwapDistanceAtr"]
    )
    for name in (
        "SwingProgression", "VWAPSide", "OpeningRangeSide", "VwapCrossCount",
        "DirectionalDisplacement", "AtrExpansion",
    ):
        joined[f"RichAligned{name}Passed"] = np.where(
            is_long, joined[f"RichBull{name}Passed"], joined[f"RichBear{name}Passed"]
        )
    joined["RichAbsoluteVwapDistanceAtr"] = joined["RichVwapDistanceAtr"].abs()
    required = tuple(FEATURES)
    if joined.loc[joined["RichAvailable"], list(required)].isna().any().any():
        raise ValueError("Available Rich feature values contain nulls")
    joined.loc[~joined["RichAvailable"], list(required)] = 0.0
    return joined


def metrics(v220, trades, blocked):
    return v220.summarize("H1", v220.Policy(zone_birth_min_quality=45.0), trades, blocked)


def simulate(v220, connector, tape, abort_keys, rule, months=None):
    sample = tape if months is None else tape[tape["Month"].isin(months)]
    return v220.simulate(
        sample,
        connector,
        v220.Policy(zone_birth_min_quality=45.0),
        abort_keys,
        candidate_gate=None if rule is None else rule.blocks,
    )


def lomo_from_full(v220, baseline_trades, baseline_net, rule_trades, rule_net, blocked):
    # 策略持仓不会跨Snapshot；只有周门在月边界可能产生很小的非可加影响。
    # 因此以全样本的逐月结果扣除测试月选择门禁，避免为每条规则重复18次完整重放。
    months = sorted(pd.to_datetime(baseline_trades["TradingDate"]).dt.strftime("%Y-%m").unique())
    base_by_month = {
        month: baseline_trades[pd.to_datetime(baseline_trades["TradingDate"]).dt.strftime("%Y-%m").eq(month)]
        for month in months
    }
    rule_by_month = {
        month: rule_trades[pd.to_datetime(rule_trades["TradingDate"]).dt.strftime("%Y-%m").eq(month)]
        for month in months
    }
    selected = []
    for month in months:
        base_test = float(base_by_month[month]["Net"].sum())
        rule_test = float(rule_by_month[month]["Net"].sum())
        train_delta = (rule_net - rule_test) - (baseline_net - base_test)
        selected.append(rule_by_month[month] if train_delta > 0 else base_by_month[month])
    trades = pd.concat(selected, ignore_index=True)
    return metrics(v220, trades, blocked), sum(
        int(not item.equals(base_by_month[month])) for month, item in zip(months, selected)
    )


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--h1-tape", type=Path, required=True)
    parser.add_argument("--rich", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--lomo-top", type=int, default=30)
    parser.add_argument("--lomo-offset", type=int, default=0)
    parser.add_argument("--min-base-trades", type=int, default=20)
    args = parser.parse_args()

    scripts = Path(__file__).parent
    v220 = load_module(scripts / "Analyze-OPFV220H1LatchedProfitResearch.py", "v220")
    connector = load_module(scripts / "Analyze-OPFV217DualSlotCalibration.py", "connector")
    rich_module = load_module(scripts / "Connect-OPFRichFeatures.py", "rich")
    tape, abort_keys = load_tape(args.h1_tape)
    tape = add_rich(tape, rich_module, args.rich)

    baseline_trades, baseline_blocked = simulate(v220, connector, tape, abort_keys, None)
    baseline = metrics(v220, baseline_trades, baseline_blocked)
    primary = baseline_trades[baseline_trades["Lane"].eq("Primary")]
    groups = primary.groupby(["Side", "ResearchPath"]).size().reset_index(name="BaseTrades")
    groups = groups[groups["BaseTrades"].ge(args.min_base_trades)]
    rules = [
        Rule(group.Side, group.ResearchPath, feature, operator, threshold)
        for group in groups.itertuples(index=False)
        for feature, (operator, thresholds) in FEATURES.items()
        for threshold in thresholds
    ]

    rows = []
    for rule in rules:
        full_trades, full_blocked = simulate(v220, connector, tape, abort_keys, rule)
        full = metrics(v220, full_trades, full_blocked)
        monthly = full_trades.assign(Month=full_trades["TradingDate"].dt.strftime("%Y-%m")).groupby("Month")["Net"].sum()
        base_monthly = baseline_trades.assign(Month=baseline_trades["TradingDate"].dt.strftime("%Y-%m")).groupby("Month")["Net"].sum()
        delta = monthly.sub(base_monthly, fill_value=0)
        rows.append({
            "Rule": rule.name,
            "Side": rule.side,
            "ResearchPath": rule.path,
            "Feature": rule.feature,
            "Operator": rule.operator,
            "Threshold": rule.threshold,
            "H1Net": full["Net"],
            "H1NetDelta": round(full["Net"] - baseline["Net"], 2),
            "H1Gross": full["Gross"],
            "H1PF": full["PF"],
            "H1Trades": full["Trades"],
            "PositiveWeeks": full["PositiveWeeks"],
            "WorstWeek": full["WorstWeek"],
            "MaxDD": full["MaxDD"],
            "PositiveDeltaMonths": int(delta.gt(0).sum()),
            "NegativeDeltaMonths": int(delta.lt(0).sum()),
            "LomoNet": np.nan,
            "LomoNetDelta": np.nan,
            "LomoPF": np.nan,
            "LomoSelectedMonths": np.nan,
            **{f"Delta_{month}": round(float(delta.get(month, 0.0)), 2) for month in sorted(tape["Month"].unique())},
        })
    result = pd.DataFrame(rows)
    finalists = result.sort_values(
        ["H1NetDelta", "PositiveDeltaMonths", "H1PF"], ascending=False
    ).iloc[args.lomo_offset : args.lomo_offset + args.lomo_top]
    by_name = {rule.name: rule for rule in rules}
    for index, row in finalists.iterrows():
        full_trades, full_blocked = simulate(
            v220, connector, tape, abort_keys, by_name[row.Rule]
        )
        full = metrics(v220, full_trades, full_blocked)
        lomo_metrics, lomo_selected = lomo_from_full(
            v220, baseline_trades, baseline["Net"], full_trades, full["Net"], full_blocked
        )
        result.loc[index, "LomoNet"] = lomo_metrics["Net"]
        result.loc[index, "LomoNetDelta"] = round(lomo_metrics["Net"] - baseline["Net"], 2)
        result.loc[index, "LomoPF"] = lomo_metrics["PF"]
        result.loc[index, "LomoSelectedMonths"] = lomo_selected
    result = result.sort_values(
        ["LomoNetDelta", "H1NetDelta", "PositiveDeltaMonths"], ascending=False, na_position="last"
    )
    args.output_dir.mkdir(parents=True, exist_ok=True)
    pd.DataFrame([baseline]).to_csv(args.output_dir / "baseline.csv", index=False)
    pd.DataFrame([{
        "TapeRows": len(tape),
        "RichAvailable": int(tape["RichAvailable"].sum()),
        "RichUnavailable": int((~tape["RichAvailable"]).sum()),
    }]).to_csv(args.output_dir / "rich_coverage.csv", index=False)
    groups.to_csv(args.output_dir / "eligible_primary_groups.csv", index=False)
    result.to_csv(args.output_dir / "single_rule_lomo.csv", index=False)
    print(result.head(30).to_string(index=False))


if __name__ == "__main__":
    main()
