#!/usr/bin/env python3
"""Diagnostic-only comparison between static terminals and frozen Actual outcomes."""

from __future__ import annotations

import argparse
from pathlib import Path

import pandas as pd


DETAIL_COLUMNS = (
    "CandidateID", "Side", "ResearchPath", "ExitRole", "ActualNetPnLDollars", "EntryDeltaPoints",
    "CandidatePlanActualPlanRiskDeltaPoints", "ActualPlanFilledRiskDeltaPoints",
)
LABEL_COLUMNS = ("CandidateID", "TerminalLabel")


def exit_family(role: str) -> str:
    role = str(role)
    if role.startswith("SPLIT_"):
        return "Split"
    if role == "SL":
        return "SL"
    if role == "TP":
        return "TP"
    if role == "TIME_STOP":
        return "TimeStop"
    if role == "SESSION_FLATTEN":
        return "SessionFlatten"
    return "Other"


def _net_sign(value: float) -> str:
    if value > 0:
        return "Positive"
    if value < 0:
        return "Negative"
    return "Flat"


def calibrate(actual_detail: pd.DataFrame, labels: pd.DataFrame) -> tuple[pd.DataFrame, dict[str, int]]:
    missing_detail = sorted(set(DETAIL_COLUMNS) - set(actual_detail.columns))
    missing_label = sorted(set(LABEL_COLUMNS) - set(labels.columns))
    if missing_detail:
        raise ValueError(f"Missing Actual calibration columns: {missing_detail}")
    if missing_label:
        raise ValueError(f"Missing static label columns: {missing_label}")
    if actual_detail["CandidateID"].duplicated().any():
        raise ValueError("Duplicate canonical Actual CandidateID")
    if labels["CandidateID"].duplicated().any():
        raise ValueError("Duplicate static label CandidateID")
    output = actual_detail.loc[:, DETAIL_COLUMNS].merge(
        labels.loc[:, LABEL_COLUMNS], on="CandidateID", how="left", validate="one_to_one", indicator="StaticLabelJoin"
    )
    if output["StaticLabelJoin"].ne("both").any():
        raise ValueError("Canonical Actual missing static label")
    output["ActualNetPnLDollars"] = pd.to_numeric(output["ActualNetPnLDollars"], errors="raise")
    output["ActualExitFamily"] = output["ExitRole"].map(exit_family)
    output["ActualNetSign"] = output["ActualNetPnLDollars"].map(_net_sign)
    output["StaticSignEligible"] = output["TerminalLabel"].isin(("SL", "TP"))
    output["DiagnosticOnly"] = True
    output = output.drop(columns=["StaticLabelJoin"])
    summary = {
        "CanonicalActualRows": len(output),
        "ExactStaticLabelJoins": len(output),
        "StaticSignEligibleRows": int(output["StaticSignEligible"].sum()),
        "StaticAmbiguousRows": int(output["TerminalLabel"].eq("Ambiguous").sum()),
        "StaticCensoredRows": int(output["TerminalLabel"].eq("Censored").sum()),
    }
    return output, summary


def write_reports(detail: pd.DataFrame, summary: dict[str, int], output_dir: Path) -> None:
    output_dir.mkdir(parents=True, exist_ok=True)
    detail.to_csv(output_dir / "static_actual_diagnostic_detail.csv", index=False, encoding="utf-8")
    pd.DataFrame([summary]).to_csv(output_dir / "static_actual_diagnostic_summary.csv", index=False, encoding="utf-8")
    pd.crosstab(detail["TerminalLabel"], detail["ActualExitFamily"]).reset_index().to_csv(
        output_dir / "static_terminal_actual_exit_family.csv", index=False, encoding="utf-8"
    )
    comparable = detail.loc[detail["StaticSignEligible"]]
    pd.crosstab(comparable["TerminalLabel"], comparable["ActualNetSign"]).reset_index().to_csv(
        output_dir / "static_terminal_actual_net_sign.csv", index=False, encoding="utf-8"
    )
    geometry = detail.groupby(["Side", "ResearchPath"], dropna=False).agg(
        ActualRows=("CandidateID", "size"),
        MedianEntryDeltaPoints=("EntryDeltaPoints", "median"),
        MedianCandidatePlanActualPlanRiskDeltaPoints=("CandidatePlanActualPlanRiskDeltaPoints", "median"),
        MedianActualPlanFilledRiskDeltaPoints=("ActualPlanFilledRiskDeltaPoints", "median"),
    ).reset_index()
    geometry.to_csv(output_dir / "static_actual_geometry_drift.csv", index=False, encoding="utf-8")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--calibration-detail", type=Path, required=True)
    parser.add_argument("--label-csv", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    args = parser.parse_args()
    detail, summary = calibrate(pd.read_csv(args.calibration_detail), pd.read_csv(args.label_csv))
    write_reports(detail, summary, args.output_dir)
    print(pd.Series(summary).to_string())


if __name__ == "__main__":
    main()
