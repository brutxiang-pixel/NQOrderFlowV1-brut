# V4 Actual Calibration and Static Path Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a replay-safe V4 research chain that calibrates Candidate plans against frozen Actual outcomes and derives explicitly counterfactual Rich-M5 static outcomes without changing the frozen strategy or ATAS environment.

**Architecture:** A new offline Python module reads the frozen V3 Actual archive and the 141-day Candidate tape, joins each normal Actual trade by `SignalID + ResearchPath`, and reports plan-versus-fill differences. A separate static-path module scans post-decision M5 bars using only planned geometry and emits terminal labels. A portfolio runner consumes only those labels and keeps Strict, LowerBound, and UpperBound results distinct from Actual evidence.

**Tech Stack:** Python 3, pandas, unittest, frozen CSV archives.

---

## File structure

- Create: `OPFStrategyV1/Scripts/Analyze-OPFV400ActualCalibration.py` — 4C archive loader, strict key join, calibration detail and summary.
- Create: `OPFStrategyV1/Scripts/Build-OPFV400StaticPathLabels.py` — 4D post-decision Rich-M5 terminal label builder and four-day Tick comparison.
- Create: `OPFStrategyV1/Scripts/Analyze-OPFV400StaticPlanPortfolio.py` — 4E conservative portfolio ranking only.
- Create: `OPFStrategyV1/Scripts/tests/test_analyze_opf_v400_actual_calibration.py` — 4C key, duplicate, and isolation tests.
- Create: `OPFStrategyV1/Scripts/tests/test_build_opf_v400_static_path_labels.py` — 4D outcome-window tests.
- Create: `OPFStrategyV1/Scripts/tests/test_analyze_opf_v400_static_plan_portfolio.py` — 4E ambiguity/censoring tests.
- Create: `OPFStrategyV1/Reports/v400_actual_calibration_static_path_contract.md` — generated-run input/output and evidence boundary contract.
- Modify: `项目恢复总览_继续开发指南.md` — append only the verified 4C–4E status and non-deployment boundary.

## Task 1: 4C Actual–Candidate calibration

- [ ] Add a failing test that a `SignalID + ResearchPath` key joins exactly one normal Actual trade to one Candidate.
- [ ] Run the test and confirm it fails because the calibration module is absent.
- [ ] Implement archive discovery for `*_execution_trades.csv`, `*_live_account_pnl.csv`, and Candidate tape loading. Reject duplicate keys, absent Candidate matches, and non-normal/abnormal rows in the 4C population.
- [ ] Emit a detail table with Actual entry/exit, FilledRisk, MFE/MAE, ExitRole, raw/account PnL and all planned geometry; emit coverage and plan-vs-fill distributions grouped by direction/path/target-R.
- [ ] Run the test and the complete test suite. Gate: 927 normal frozen Actual rows each map to exactly one Candidate, with no output field overwriting an Actual field.

## Task 2: 4D static Rich-M5 labels

- [ ] Add failing unit tests for Long and Short SL, TP, TimeStop, same-bar ambiguity, missing continuous bars, and exclusion of the decision bar.
- [ ] Run tests and confirm they fail because the label module is absent.
- [ ] Implement a label builder using only planned Entry/Stop/Target and continuous M5 bars strictly after DecisionBar, maximum 12 bars.
- [ ] Emit exactly one base terminal label per Candidate: `SL`, `TP`, `TimeStop`, `Ambiguous`, or `Censored`. For `Ambiguous`, provide distinct Strict, LowerBound, and UpperBound research values; never produce PnL for `Censored`.
- [ ] Add four-day Tick comparison that reads only ticks strictly after each Candidate decision sequence and reports an M5-versus-Tick confusion matrix without changing full-sample labels.
- [ ] Run tests and integration validation. Gate: no ReplayLocalOnly fields enter calculation, and every Candidate has one base terminal label.

## Task 3: 4E StaticPlan Portfolio

- [ ] Add failing tests that Strict excludes Ambiguous outcomes, LowerBound resolves them as stop, UpperBound resolves them as target, and Censored never reaches PnL.
- [ ] Run tests and confirm expected failure.
- [ ] Implement frozen-constraint portfolio replay for the three separate static research views: 3 contracts, two same-direction slots, 15 normal trades/day, -$250 daily loss, -$500 weekly Long gate, Globex lock, and $3.60 round-trip fee.
- [ ] Emit separate trade, daily, weekly, monthly, replacement-chain, and summary reports. Label every output `StaticPlanPortfolio`, `Counterfactual`, and non-deployment.
- [ ] Restrict first-run policy changes to one candidate-selection switch; do not alter exits, contracts, loss limits, concurrency, or paths.
- [ ] Run the full suite and production-data validation. Gate: Strict/LowerBound/UpperBound are never merged with the frozen Actual result table.

## Task 4: evidence and mainline update

- [ ] Write the 4C–4E contract and reproducible commands with archive paths, source checksums, column contracts, and gates.
- [ ] Execute the calibrated baseline and static labels against the frozen data, preserving output provenance.
- [ ] Append only verified results to the mainline MD; if any gate fails, append the diagnosed stop point and do not begin the next phase.
- [ ] Run the full Python suite and record its exact pass count.

## Verification matrix

| Phase | Required evidence | Stop condition |
|---|---|---|
| 4C | 927/927 exact Actual-to-Candidate join | missing, duplicate, or cross-snapshot key |
| 4D | one terminal label/Candidate; no decision-bar or local-only input | discontinuous/corrupt M5 input |
| 4E | strict three-view separation; Censored is non-economic | any Actual/static table merge |

## Scope guard

This plan must not compile a DLL, deploy a configuration, clear an activity log, change a strategy rule, mutate the V3 tag/archive, or present StaticPlan results as Replay, real trading, Smoke, or deployment evidence.
