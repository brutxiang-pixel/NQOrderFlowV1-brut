import argparse
import importlib.util
from pathlib import Path

import pandas as pd


def load_module():
    path = Path(__file__).with_name("Analyze-OPFV207ProfitRiskPortfolio.py")
    spec = importlib.util.spec_from_file_location("opf_v207_portfolio", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def run_arm(module, core, secondary, day_order, arm_name, core_qty, sec_qty, risk_cap, sec_loss, total_loss):
    variants = [("ALL", secondary)]
    paths = sorted(set(zip(secondary["Side"], secondary["ResearchPath"])))
    for side, path in paths:
        kept = secondary[~(secondary["Side"].eq(side) & secondary["ResearchPath"].eq(path))]
        variants.append((f"DROP|{side}|{path}", kept))

    core_by_day = {key: value for key, value in core.groupby("SnapshotID", sort=False)}
    rows = []
    traces = {}
    for variant, candidates in variants:
        secondary_by_day = {
            key: value for key, value in candidates.groupby("SnapshotID", sort=False)
        }
        for mode in ("Observed", "Conservative"):
            trades, diag = module.simulate(
                core_by_day,
                secondary_by_day,
                mode,
                core_qty,
                sec_qty,
                risk_cap,
                3,
                sec_loss,
                total_loss,
                -999,
                False,
                True,
                False,
                "ActiveTradeOnly",
            )
            rows.append(
                {
                    "Arm": arm_name,
                    "Variant": variant,
                    "Mode": mode,
                    **module.metrics(trades, day_order),
                    **diag,
                }
            )
            traces[(variant, mode)] = trades

    results = pd.DataFrame(rows)
    conservative = results[results["Mode"].eq("Conservative")].copy()
    base = conservative[conservative["Variant"].eq("ALL")].iloc[0]
    conservative["NetDeltaVsAll"] = conservative["Net"] - base["Net"]
    conservative["Q4DeltaVsAll"] = conservative["Q4Net"] - base["Q4Net"]
    conservative["H1DeltaVsAll"] = conservative["H1Net"] - base["H1Net"]
    conservative["MaxDDDeltaVsAll"] = conservative["MaxDD"] - base["MaxDD"]
    conservative["StrictHarmful"] = (
        conservative["Variant"].ne("ALL")
        & conservative["NetDeltaVsAll"].gt(0)
        & conservative["Q4DeltaVsAll"].ge(0)
        & conservative["H1DeltaVsAll"].ge(0)
        & conservative["MaxDDDeltaVsAll"].le(0)
    )
    results = results.merge(
        conservative[
            [
                "Variant",
                "NetDeltaVsAll",
                "Q4DeltaVsAll",
                "H1DeltaVsAll",
                "MaxDDDeltaVsAll",
                "StrictHarmful",
            ]
        ],
        on="Variant",
        how="left",
    )

    harmful = conservative[conservative["StrictHarmful"]]["Variant"].tolist()
    if harmful:
        combined = secondary.copy()
        for variant in harmful:
            _, side, path = variant.split("|", 2)
            combined = combined[
                ~(combined["Side"].eq(side) & combined["ResearchPath"].eq(path))
            ]
        secondary_by_day = {
            key: value for key, value in combined.groupby("SnapshotID", sort=False)
        }
        for mode in ("Observed", "Conservative"):
            trades, diag = module.simulate(
                core_by_day,
                secondary_by_day,
                mode,
                core_qty,
                sec_qty,
                risk_cap,
                3,
                sec_loss,
                total_loss,
                -999,
                False,
                True,
                False,
                "ActiveTradeOnly",
            )
            row = {
                "Arm": arm_name,
                "Variant": "DROP_STRICT_HARMFUL",
                "Mode": mode,
                **module.metrics(trades, day_order),
                **diag,
            }
            if mode == "Conservative":
                row.update(
                    {
                        "NetDeltaVsAll": row["Net"] - base["Net"],
                        "Q4DeltaVsAll": row["Q4Net"] - base["Q4Net"],
                        "H1DeltaVsAll": row["H1Net"] - base["H1Net"],
                        "MaxDDDeltaVsAll": row["MaxDD"] - base["MaxDD"],
                        "StrictHarmful": False,
                    }
                )
            results = pd.concat([results, pd.DataFrame([row])], ignore_index=True)
    return results, harmful


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()

    module = load_module()
    _, core, secondary, _, _ = module.load_candidates(args.evidence)
    scored = module.add_lomo_scores(secondary, [20])
    day_order = (
        core.groupby("SnapshotID")["EntryTime"].min().sort_values().reset_index()[["SnapshotID"]]
    )
    arms = [
        ("Moderate_2plus1_R300", 2, 1, 300, 150, 300),
        ("Upper_2plus2_R400", 2, 2, 400, 200, 400),
    ]
    parts = []
    harmful_by_arm = {}
    for arm in arms:
        results, harmful = run_arm(module, core, scored, day_order, *arm)
        parts.append(results)
        harmful_by_arm[arm[0]] = harmful
    output = pd.concat(parts, ignore_index=True)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    output.to_csv(args.output, index=False)

    conservative = output[output["Mode"].eq("Conservative")].sort_values(
        ["Arm", "Net"], ascending=[True, False]
    )
    print(conservative.groupby("Arm").head(15)[
        [
            "Arm",
            "Variant",
            "Net",
            "PF",
            "MaxDD",
            "Q4Net",
            "H1Net",
            "SecondaryAccepted",
            "SecondaryNet",
            "NetDeltaVsAll",
            "StrictHarmful",
        ]
    ].to_string(index=False))
    print("\nStrict harmful paths:")
    for arm, harmful in harmful_by_arm.items():
        print(arm, harmful)


if __name__ == "__main__":
    main()
