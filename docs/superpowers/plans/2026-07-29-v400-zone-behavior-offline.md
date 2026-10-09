# V4 Zone Behavior Offline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Produce reproducible, leakage-safe first-Touch labels and pre-registered monthly LOMO descriptive outputs from the accepted 24-day Zone Behavior ledger.

**Architecture:** A standard-library Python analyzer reads only the three ledger CSV types and the frozen date plan. It constructs one row per first Touch, derives all features at the Touch boundary and labels only from subsequent Zone Bars, then writes transparent CSV/JSON outputs. A small `unittest` fixture verifies time boundary and directional-label rules.

**Tech Stack:** Python 3 standard library, CSV, JSON, unittest.

---

### Task 1: Define and lock the offline inputs and outputs

**Files:**
- Create: `OPFStrategyV1/Reports/v400_stage3_zone_behavior_offline_contract.md`
- Create: `docs/superpowers/plans/2026-07-29-v400-zone-behavior-offline.md`

- [x] **Step 1: Freeze the data boundary and labels**

The contract fixes 24 planned dates, first Touch only, a 12-subsequent-Zone-Bar window, censored handling, four pre-Touch features, H1 LOMO and July stress rules.

- [x] **Step 2: Confirm no execution surface is in scope**

No DLL, configuration, ATAS or active-log write is permitted.

### Task 2: Build first-Touch label extraction with tests

**Files:**
- Create: `OPFStrategyV1/Scripts/Analyze-OPFV400ZoneBehavior.py`
- Create: `OPFStrategyV1/Scripts/tests/test_analyze_opf_v400_zone_behavior.py`

- [ ] **Step 1: Write the failing directional-label test**

Create a Bull and Bear fixture. Each asserts that the Touch Bar is excluded, the next twelve bars define directional favorable/adverse excursion, and a terminal inside the window is detected.

- [ ] **Step 2: Run the test to verify it fails because the analyzer does not exist**

Run: `python -m unittest OPFStrategyV1.Scripts.tests.test_analyze_opf_v400_zone_behavior`

Expected: import failure for `Analyze-OPFV400ZoneBehavior`.

- [ ] **Step 3: Implement the minimal extraction and validation functions**

Implement CSV ingestion, date-plan filtering, first-Touch matching, observed/censored labels, and strict missing-key checks.

- [ ] **Step 4: Run the unit test and verify it passes**

Run: `python -m unittest OPFStrategyV1.Scripts.tests.test_analyze_opf_v400_zone_behavior`

Expected: all tests pass.

### Task 3: Implement deterministic LOMO descriptive outputs

**Files:**
- Modify: `OPFStrategyV1/Scripts/Analyze-OPFV400ZoneBehavior.py`

- [ ] **Step 1: Write the failing LOMO-bin test**

Use a six-month synthetic fixture and assert held-out-month boundaries use only the five remaining months, while July uses the full H1 boundary.

- [ ] **Step 2: Run the test and verify it fails**

Run: `python -m unittest OPFStrategyV1.Scripts.tests.test_analyze_opf_v400_zone_behavior`

Expected: missing LOMO summary function.

- [ ] **Step 3: Implement tercile summaries and eligibility logic**

Produce `first_touch_labels.csv`, `feature_lomo.csv`, `monthly_coverage.csv`, and `summary.json` exactly as contracted.

- [ ] **Step 4: Run all unit tests and compile checks**

Run: `python -m unittest OPFStrategyV1.Scripts.tests.test_analyze_opf_v400_zone_behavior && python -m py_compile OPFStrategyV1/Scripts/Analyze-OPFV400ZoneBehavior.py`

Expected: exit 0.

### Task 4: Run full 24-day analysis and audit the result

**Files:**
- Create: `OPFStrategyV1/Reports/v400_stage3_zone_behavior_offline/first_touch_labels.csv`
- Create: `OPFStrategyV1/Reports/v400_stage3_zone_behavior_offline/feature_lomo.csv`
- Create: `OPFStrategyV1/Reports/v400_stage3_zone_behavior_offline/monthly_coverage.csv`
- Create: `OPFStrategyV1/Reports/v400_stage3_zone_behavior_offline/summary.json`
- Create: `OPFStrategyV1/Reports/v400_stage3_zone_behavior_offline_review.md`

- [ ] **Step 1: Run the analyzer against the active accepted ledger**

Run with the plan, active log directory and output directory explicitly supplied.

- [ ] **Step 2: Independently reconcile coverage and inspect each feature's LOMO direction**

Verify only planned dates are present, no Censored row receives a fixed-window outcome, all features meet coverage rules, and no eligibility claim bypasses the 5/6, 1.0-point, July-direction and 30-per-bin requirements.

- [ ] **Step 3: Write the research review**

State data counts, exclusions, all pre-registered results, the exact status of each feature, and the next permitted action. No strategy or deployment claim is allowed.
