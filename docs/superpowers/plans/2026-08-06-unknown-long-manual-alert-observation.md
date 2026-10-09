# UnknownRegimeZoneTouch Long Manual Alert Observation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restore a zero-order manual observation alert for the single v2.77-qualified `UnknownRegimeZoneTouch Long` candidate.

**Architecture:** A configuration boolean selects a narrow branch at the existing execution boundary. The branch validates shared safety only, emits presentation/audit output, then returns before the normal order path. It has no dependency on manual position management.

**Tech Stack:** C#, ATAS strategy API, existing OPF execution/HUD/research logger.

---

### Task 1: Add the disabled-by-default observation setting

**Files:**
- Modify: `OPFStrategyV1/Core/Configuration/ActualExecutionSettings.cs`
- Modify: `OPFStrategyV1/Configs/OPFStrategyV1_actual_execution.default.json`

- [ ] Add `ManualAlertEnabled` after `EnableActualOrders`; set it to `false` in both defaults. Bump the settings version so a stale local configuration upgrades deterministically.
- [ ] Add a focused normalization test showing a missing/false value leaves the alert disabled.

### Task 2: Emit the zero-order candidate alert

**Files:**
- Modify: `OPFStrategyV1/Strategy/OpeningPullbackFailureStrategy.cs`
- Test: `OPFStrategyV1/Tests/ManualAlertObservationTests.cs`

- [ ] Write failing tests for matching Long, nonmatching Short/path, and `ManualAlertEnabled=false`.
- [ ] Add a single helper that verifies the fixed path/side and shared safety boundary, builds entry/SL/TP/risk context and emits one `MANUAL_ALERT` event/HUD/notification per candidate key.
- [ ] Call it before the normal `ActualOrdersEnabled` early return. Return immediately after an alert; never create `ReplayExecutionState` or call an order API.
- [ ] Run the focused tests and build.

### Task 3: Package an explicit observation config and verify

**Files:**
- Create: `OPFStrategyV1/Configs/OPFStrategyV1_v277_unknown_long_manual_alert.json`
- Modify: `OPFStrategyV1/Reports/v277_final_candidate_and_portfolio_audit.md`

- [ ] Package `EnableActualOrders=false`, `ManualAlertEnabled=true`, and the existing risk/target/quantity settings for manual observation.
- [ ] Document that the configuration is alert-only and that the trader manually attaches the displayed hard SL/TP.
- [ ] Build the DLL and inspect the configuration snapshot plus execution events from a focused Smoke run before any broader observation use.
