import argparse
import importlib.util
from pathlib import Path

import numpy as np
import pandas as pd


NUMERIC_FEATURES = [
    "SetupQualityScore",
    "RegimeScore",
    "InitialRiskPoints",
    "RichRelativeVolume20",
    "RichAlignedVwapDistanceAtr",
    "RichAbsoluteVwapDistanceAtr",
    "RichAlignedScore",
    "RichScoreDelta",
    "RichRegimeBars",
]
CATEGORICAL_FEATURES = [
    "Side",
    "ResearchPath",
    "Session",
    "ZoneType",
    "ZoneFreshness",
    "RichRegime",
]


def load_module(path: Path):
    spec = importlib.util.spec_from_file_location("opf_v207_portfolio", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def read_csvs(root: Path, pattern: str) -> pd.DataFrame:
    return pd.concat(
        (pd.read_csv(path, low_memory=False) for path in sorted(root.glob(pattern))),
        ignore_index=True,
    )


def load_rich(root: Path) -> pd.DataFrame:
    selected = pd.read_csv(root / "selected_snapshots.csv", dtype={"Date": str})
    columns = [
        "Time",
        "Bar",
        "Regime",
        "RegimeBars",
        "RelativeVolume20",
        "VwapDistanceAtr",
        "BullScore",
        "BearScore",
    ]
    parts = []
    for row in selected.itertuples(index=False):
        parts.append(
            pd.read_csv(root / f"{row.SnapshotId}_rich_bar_features.csv", usecols=columns)
        )
    rich = pd.concat(parts, ignore_index=True)
    rich.rename(
        columns={column: f"Rich{column}" for column in columns if column not in ("Time", "Bar")},
        inplace=True,
    )
    rich.rename(columns={"Time": "EntryTime", "Bar": "EntryBar"}, inplace=True)
    rich["EntryTime"] = pd.to_datetime(rich["EntryTime"])
    rich["EntryBar"] = rich["EntryBar"].astype(int)
    return rich


def gross_three_contracts(row) -> float:
    gross = float(row.ActualGross2)
    if row.ResearchPath != "ZoneBirthResearch":
        return gross * 1.5
    if row.ExitReason == "Base:Target|Runner:ProtectBE":
        return gross * 2
    if row.ExitReason == "Base:Target|Runner:Target":
        return gross * 18 / 13
    return gross * 1.5


def prepare_rows(module, evidence: Path, rich_root: Path) -> pd.DataFrame:
    _, core, _, _, _ = module.load_candidates(evidence)
    rows = core[core["Period"].eq("H1")].copy()
    signals = read_csvs(evidence, "*_signals.csv")[
        [
            "SnapshotID",
            "SignalID",
            "SetupQualityScore",
            "RegimeScore",
            "ZoneType",
            "ZoneFreshness",
        ]
    ].drop_duplicates(["SnapshotID", "SignalID"])
    rows = rows.merge(signals, on=["SnapshotID", "SignalID"], how="left", validate="one_to_one")
    rows = rows.merge(
        load_rich(rich_root), on=["EntryTime", "EntryBar"], how="left", validate="many_to_one"
    )
    is_long = rows["Side"].eq("Long")
    rows["RichAlignedScore"] = np.where(is_long, rows["RichBullScore"], rows["RichBearScore"])
    rows["RichOpposingScore"] = np.where(is_long, rows["RichBearScore"], rows["RichBullScore"])
    rows["RichScoreDelta"] = rows["RichAlignedScore"] - rows["RichOpposingScore"]
    rows["RichAlignedVwapDistanceAtr"] = np.where(
        is_long, rows["RichVwapDistanceAtr"], -rows["RichVwapDistanceAtr"]
    )
    rows["RichAbsoluteVwapDistanceAtr"] = rows["RichVwapDistanceAtr"].abs()
    hour = rows["EntryTime"].dt.hour
    rows["Session"] = pd.cut(
        hour, [-1, 5, 12, 19, 23], labels=["Asia", "Europe", "US", "Late"]
    ).astype(str)
    rows["Gross3"] = rows.apply(gross_three_contracts, axis=1)
    rows["Net3"] = rows["Gross3"] - 3.6
    iso = rows["TradingDate"].dt.isocalendar()
    rows["Week"] = iso["year"].astype(str) + "-W" + iso["week"].astype(str).str.zfill(2)
    for column in NUMERIC_FEATURES:
        rows[column] = pd.to_numeric(rows[column], errors="coerce")
        rows[column] = rows[column].fillna(rows[column].median())
    for column in CATEGORICAL_FEATURES:
        rows[column] = rows[column].fillna("Missing").astype(str)
    return rows


def sse(values: np.ndarray) -> float:
    if len(values) == 0:
        return 0.0
    return float(((values - values.mean()) ** 2).sum())


def build_tree(rows: pd.DataFrame, depth: int, max_depth: int, min_leaf: int) -> dict:
    target = rows["Gross3"].to_numpy(dtype=float)
    node = {"mean": float(target.mean()), "count": int(len(rows))}
    if depth >= max_depth or len(rows) < min_leaf * 2:
        return node

    parent_sse = sse(target)
    best = None
    for feature in NUMERIC_FEATURES:
        values = rows[feature].to_numpy(dtype=float)
        thresholds = np.unique(np.quantile(values, np.linspace(0.1, 0.9, 9)))
        for threshold in thresholds:
            mask = values <= threshold
            if mask.sum() < min_leaf or (~mask).sum() < min_leaf:
                continue
            gain = parent_sse - sse(target[mask]) - sse(target[~mask])
            if best is None or gain > best[0]:
                best = (gain, "numeric", feature, float(threshold), mask)
    for feature in CATEGORICAL_FEATURES:
        values = rows[feature].astype(str).to_numpy()
        for category in np.unique(values):
            mask = values == category
            if mask.sum() < min_leaf or (~mask).sum() < min_leaf:
                continue
            gain = parent_sse - sse(target[mask]) - sse(target[~mask])
            if best is None or gain > best[0]:
                best = (gain, "categorical", feature, str(category), mask)
    if best is None or best[0] <= 0:
        return node

    _, kind, feature, value, mask = best
    node.update({"kind": kind, "feature": feature, "value": value})
    node["left"] = build_tree(rows.loc[mask], depth + 1, max_depth, min_leaf)
    node["right"] = build_tree(rows.loc[~mask], depth + 1, max_depth, min_leaf)
    return node


def predict_one(node: dict, row) -> float:
    if "kind" not in node:
        return node["mean"]
    if node["kind"] == "numeric":
        go_left = float(row[node["feature"]]) <= float(node["value"])
    else:
        go_left = str(row[node["feature"]]) == str(node["value"])
    return predict_one(node["left"] if go_left else node["right"], row)


def max_losing_streak(weekly: pd.Series) -> int:
    current = 0
    maximum = 0
    for value in weekly:
        current = current + 1 if value < 0 else 0
        maximum = max(maximum, current)
    return maximum


def metrics(rows: pd.DataFrame, all_weeks: list[str]) -> dict:
    if rows.empty:
        return {"Trades": 0, "Gross": 0.0, "AccountNet": 0.0}
    weekly = rows.groupby("Week")["Net3"].sum().reindex(all_weeks, fill_value=0)
    daily = rows.groupby("TradingDate")["Net3"].sum().sort_index()
    cumulative = daily.cumsum()
    positive = rows.loc[rows["Net3"] > 0, "Net3"].sum()
    negative = -rows.loc[rows["Net3"] < 0, "Net3"].sum()
    gross = float(rows["Gross3"].sum())
    return {
        "Trades": int(len(rows)),
        "Gross": round(gross, 2),
        "AccountNet": round(float(rows["Net3"].sum()), 2),
        "PF": round(float(positive / negative), 4) if negative else float("inf"),
        "MaxDD": round(float((cumulative.cummax() - cumulative).max()), 2),
        "PositiveWeekPct": round(float((weekly > 0).mean() * 100), 2),
        "WeeklyMean": round(float(weekly.mean()), 2),
        "WeeklyMedian": round(float(weekly.median()), 2),
        "BestWeek": round(float(weekly.max()), 2),
        "WorstWeek": round(float(weekly.min()), 2),
        "MaxLosingWeeks": max_losing_streak(weekly),
        "BestWeekGrossSharePct": round(float(weekly.max() / gross * 100), 2) if gross > 0 else 0,
        "JanFebGross": round(float(rows[rows["Month"].isin(["2026-01", "2026-02"])]["Gross3"].sum()), 2),
        "MarAprGross": round(float(rows[rows["Month"].isin(["2026-03", "2026-04"])]["Gross3"].sum()), 2),
        "MayJunGross": round(float(rows[rows["Month"].isin(["2026-05", "2026-06"])]["Gross3"].sum()), 2),
    }


def describe_tree(node: dict, prefix: str = "") -> list[str]:
    if "kind" not in node:
        return [f"{prefix}=> expected Gross ${node['mean']:.2f}, n={node['count']}"]
    operator = "<=" if node["kind"] == "numeric" else "=="
    left = describe_tree(node["left"], f"{prefix}{node['feature']} {operator} {node['value']} AND ")
    opposite = ">" if node["kind"] == "numeric" else "!="
    right = describe_tree(node["right"], f"{prefix}{node['feature']} {opposite} {node['value']} AND ")
    return left + right


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--rich", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    args = parser.parse_args()

    module = load_module(Path(__file__).with_name("Analyze-OPFV207ProfitRiskPortfolio.py"))
    rows = prepare_rows(module, args.evidence, args.rich)
    weeks = sorted(rows["Week"].unique())
    results = []
    prediction_sets = {}
    for max_depth in (2, 3, 4):
        for min_leaf in (20, 40, 60):
            predictions = pd.Series(index=rows.index, dtype=float)
            for week in weeks:
                train = rows[rows["Week"].ne(week)]
                test = rows[rows["Week"].eq(week)]
                tree = build_tree(train, 0, max_depth, min_leaf)
                predictions.loc[test.index] = [predict_one(tree, row) for _, row in test.iterrows()]
            key = (max_depth, min_leaf)
            prediction_sets[key] = predictions
            for threshold in (0, 5, 10, 15, 20):
                selected = rows[predictions.ge(threshold)]
                result = {
                    "MaxDepth": max_depth,
                    "MinLeaf": min_leaf,
                    "Threshold": threshold,
                    **metrics(selected, weeks),
                }
                result["TargetMet"] = result["Gross"] >= 20000
                results.append(result)

    summary = pd.DataFrame(results).sort_values(
        ["TargetMet", "Gross", "MaxDD", "PositiveWeekPct"],
        ascending=[False, False, True, False],
    )
    args.output_dir.mkdir(parents=True, exist_ok=True)
    summary.to_csv(args.output_dir / "v208_h1_strict_entry_tree_summary.csv", index=False)

    best = summary.iloc[0]
    key = (int(best.MaxDepth), int(best.MinLeaf))
    output = rows.copy()
    output["OofExpectedGross"] = prediction_sets[key]
    output["Selected"] = output["OofExpectedGross"].ge(float(best.Threshold))
    output.to_csv(args.output_dir / "v208_h1_strict_entry_tree_oof.csv", index=False)
    final_tree = build_tree(rows, 0, int(best.MaxDepth), int(best.MinLeaf))
    rules = describe_tree(final_tree)
    (args.output_dir / "v208_h1_strict_entry_tree_rules.txt").write_text(
        "\n".join(rules) + "\n", encoding="utf-8"
    )

    print("Baseline 3-contract direct stream")
    print(pd.Series(metrics(rows, weeks)).to_string())
    print("\nTop weekly-LOO gates")
    print(summary.head(20).to_string(index=False))
    print("\nFinal full-H1 tree rules for the top structure")
    print("\n".join(rules))


if __name__ == "__main__":
    main()
