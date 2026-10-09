# V4 4C-R and 4F Calibration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Correct the planned-risk vocabulary in V4 calibration and measure, without pretending to reproduce PnL, whether static Rich-M5 terminal labels remain informative for the frozen Actual executions.

**Architecture:** 4C-R extends the existing frozen-Actual connector to preserve three separate geometries: Candidate plan, Actual recorded plan, and filled risk. 4F joins only the 926 canonical Actual rows to their static labels and reports terminal/exit-family and sign diagnostics; it never calculates a strategy PnL or recommends a deployment change.

**Tech Stack:** Python 3, pandas, unittest, frozen CSV reports.

---

## Locked review gate before every task

- Re-read `docs/superpowers/specs/2026-07-30-v400-actual-calibration-static-path-design.md` and the V4 current-state entry in `项目恢复总览_继续开发指南.md`.
- Confirm: no DLL/config/activity-log mutation; no `ReplayLocalOnly` field in a cross-Replay calculation; no StaticPlan result presented as Actual/Smoke evidence.
- If a task needs a new Replay, collection, strategy rule, path scan, or feature threshold, stop rather than expanding scope.

## Task 1: 4C-R planned/fill geometry correction

**Files:**
- Modify: `OPFStrategyV1/Scripts/Analyze-OPFV400ActualCalibration.py`
- Modify: `OPFStrategyV1/Scripts/tests/test_analyze_opf_v400_actual_calibration.py`
- Modify: `docs/superpowers/specs/2026-07-30-v400-actual-calibration-static-path-design.md`

- [x] Write failing tests proving Candidate planned risk, Actual recorded planned risk, and filled risk remain separately named and produce separate equality counts.
- [x] Run the test and observe failure.
- [x] Preserve `InitialRiskPoints`, `PlannedRiskPoints`, `PlannedTargetR`, and `TargetR` from frozen execution rows under `Actual*` names; rename all Candidate geometry under `Candidate*` names.
- [x] Output exact counts for Candidate-plan vs Actual-plan risk, Actual-plan vs filled risk, Candidate TargetR vs Actual PlannedTargetR, and Actual entry vs Candidate entry.
- [x] Re-run against frozen archive. Gate: canonical 926/926 joins, no ambiguous geometry field name, no changed archive data.

## Task 2: 4F Static-to-Actual diagnostic

**Files:**
- Create: `OPFStrategyV1/Scripts/Analyze-OPFV400StaticActualCalibration.py`
- Create: `OPFStrategyV1/Scripts/tests/test_analyze_opf_v400_static_actual_calibration.py`
- Create: `OPFStrategyV1/Reports/v400_static_actual_calibration_contract.md`

- [x] Write failing tests for exact CandidateID joining, static `Ambiguous/Censored` exclusion from sign metrics, and explicit Actual exit-family classification.
- [x] Run test and observe failure.
- [x] Join 4C-R canonical detail to static labels by CandidateID; emit only diagnostic tables: coverage, M5 terminal × Actual exit family, M5 terminal × Actual account-PnL sign, and path/side geometry drift.
- [x] Mark all rows `DiagnosticOnly`; exclude `Ambiguous` and `Censored` from sign-rate denominators while reporting their coverage separately.
- [x] Run against frozen reports. Gate: 926/926 static-label joins, no Actual/static PnL merge, no Dynamic/ReplayLocalOnly input.

## Task 3: controlled conclusion

**Files:**
- Modify: `OPFStrategyV1/Reports/v400_actual_calibration_static_path_contract.md`
- Modify: `项目恢复总览_继续开发指南.md`

- [x] Record both Gates, the corrected 926 basis, and the diagnostic limitation.
- [x] State only one of: “one pre-registered selector may be assessed offline” or “current V4 data surface is alpha no-go.” Do not schedule Smoke in either case.
- [x] Run the full Python suite and record pass count: 38/38 passed on 2026-07-30.

## Success criteria

1. `CandidatePlannedRisk`, `ActualPlannedRisk`, and `FilledRisk` are unambiguously distinct in code and reports.
2. Frozen Actual remains 926 canonical rows; the 2026-07-06 duplicate remains an audit exclusion, not a deletion.
3. 4F produces diagnosis, not PnL/recommendations, and cannot be confused with Actual Replay evidence.
4. Every execution step passes the locked review gate above.
