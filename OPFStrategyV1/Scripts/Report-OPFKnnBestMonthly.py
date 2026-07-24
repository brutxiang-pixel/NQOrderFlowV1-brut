import argparse
import importlib.util
from pathlib import Path

import pandas as pd


script_path = Path(__file__).with_name("Analyze-OPFDynamicPolicyKnn.py")
spec = importlib.util.spec_from_file_location("opf_knn", script_path)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


def simulate_detail(rows: pd.DataFrame, threshold: float) -> pd.DataFrame:
    accepted = []
    for _, day in rows.groupby("SnapshotID"):
        active_until = -1
        daily_net = 0.0
        daily_trades = 0
        for row in day.sort_values(["EntryTime", "EntryBar"]).itertuples(index=False):
            if row.Score < threshold or row.EntryBar <= active_until or daily_net <= -300:
                continue
            if daily_trades >= 15:
                continue
            accepted.append(row)
            daily_trades += 1
            daily_net += row.OutcomeNet
            active_until = row.PolicyExitBar
    return pd.DataFrame(accepted)


def summarize(trades: pd.DataFrame, mode: str) -> pd.DataFrame:
    trades = trades.copy()
    trades["Month"] = trades["EntryTime"].dt.to_period("M").astype(str)
    trades["Gross"] = trades["OutcomeNet"] + 2.4
    trades["Commission"] = 2.4
    trades["Win"] = trades["Gross"].gt(0).astype(int)
    trades["Loss"] = trades["Gross"].lt(0).astype(int)
    trades["Scratch"] = trades["Gross"].eq(0).astype(int)
    result = trades.groupby("Month").agg(
        Trades=("OutcomeNet", "size"),
        Wins=("Win", "sum"),
        Losses=("Loss", "sum"),
        Scratches=("Scratch", "sum"),
        Gross=("Gross", "sum"),
        Commission=("Commission", "sum"),
        Net=("OutcomeNet", "sum"),
    ).reset_index()
    result.insert(0, "Mode", mode)
    result["AvgNetPerTrade"] = result["Net"] / result["Trades"]
    for column in ["Gross", "Commission", "Net", "AvgNetPerTrade"]:
        result[column] = result[column].round(2)
    return result


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--h1", type=Path, required=True)
    parser.add_argument("--q4", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()

    observed = pd.concat(
        [module.load_rows(args.h1, False), module.load_rows(args.q4, False)],
        ignore_index=True,
    )
    numeric_features, categorical_features, rich_numeric = module.feature_set("none")
    results = []
    for mode, conservative, shrinkage in (
        ("Observed", False, 0.0),
        ("Conservative", True, 15.0),
    ):
        combined = observed.copy()
        if conservative:
            ambiguous = combined["AmbiguousStopAndTargetSameBar"].astype(str).eq("True")
            risk = combined["InitialRiskPoints_Policy"].fillna(combined["InitialRiskPoints"])
            combined.loc[ambiguous, "OutcomeNet"] = -4 * risk[ambiguous] - 2.4
        combined = module.add_features(combined)
        scored_parts = []
        for month in sorted(combined["Month"].unique()):
            train = combined[combined["Month"].ne(month)].copy().reset_index(drop=True)
            test = combined[combined["Month"].eq(month)].copy().reset_index(drop=True)
            predictions = module.predict_fold(
                train,
                test,
                ks=(40,),
                category_weights=(0.75,),
                shrinkages=(shrinkage,),
                numeric_features=numeric_features,
                categorical_features=categorical_features,
                rich_numeric=rich_numeric,
            )
            score = predictions[("OutcomeR", 40, 0.75, shrinkage)]
            scored_parts.append(test.assign(Score=score))
        selected = module.choose_policy(pd.concat(scored_parts, ignore_index=True))
        trades = simulate_detail(selected, threshold=0.075)
        results.append(summarize(trades, mode))

    output = pd.concat(results, ignore_index=True)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    output.to_csv(args.output, index=False)
    print(output.to_string(index=False))
    print("\nTotals")
    print(
        output.groupby("Mode")[["Trades", "Wins", "Losses", "Scratches", "Gross", "Commission", "Net"]]
        .sum()
        .to_string()
    )


if __name__ == "__main__":
    main()
