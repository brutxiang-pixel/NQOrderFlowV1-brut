# Live Bracket and Feishu Notification Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ensure each live entry has both broker-confirmed SL and TP or is fail-closed, and send a Feishu notification after a confirmed entry fill.

**Architecture:** Live bracket submission dispatches SL and TP independently so a pending SL cannot block TP submission. A bounded confirmation guard requires both legs before marking the bracket ready; otherwise it locks entry and submits an emergency flatten. Feishu formatting is pure and testable; HTTP delivery is optional and never participates in order handling.

**Tech Stack:** C#, ATAS strategy API, System.Net.Http, System.Text.Json, existing console regression tests.

---

### Task 1: Bracket confirmation regression guard

**Files:**
- Create: `OPFStrategyV1/Strategy/LiveBracketConfirmationGate.cs`
- Modify: `OPFStrategyV1/Scripts/tests/LiveProtectionFailClosedTests/Program.cs`

- [ ] Write cases proving a filled position with a missing TP requires fail-closed flatten, while a confirmed SL+TP pair does not.
- [ ] Run the test and confirm it fails because `LiveBracketConfirmationGate` is absent.
- [ ] Add the smallest pure gate that decides whether both legs are confirmed and whether the timeout must flatten.
- [ ] Re-run the test and confirm it passes.

### Task 2: Non-blocking live bracket submission

**Files:**
- Modify: `OPFStrategyV1/Strategy/OpeningPullbackFailureStrategy.cs`

- [ ] Dispatch SL and TP without awaiting either long-lived ATAS working-order task.
- [ ] Mark the live bracket ready only after both broker order references are confirmed; on timeout, block new entries and flatten if either is absent.
- [ ] Preserve existing historical Replay virtual-target behavior and existing stop-loss failure handling.

### Task 3: Feishu confirmed-fill notification

**Files:**
- Create: `OPFStrategyV1/Strategy/FeishuTradeNotification.cs`
- Modify: `OPFStrategyV1/Core/Configuration/ActualExecutionSettings.cs`
- Modify: `OPFStrategyV1/Core/Snapshots/ConfigSnapshot.cs`
- Modify: `OPFStrategyV1/Strategy/OpeningPullbackFailureStrategy.cs`
- Modify: `OPFStrategyV1/Scripts/tests/LiveProtectionFailClosedTests/Program.cs`

- [ ] Add a pure formatter test which verifies the confirmed-fill payload contains trade id, side, fill, SL, TP, and path.
- [ ] Add optional local-only Feishu settings; redact the webhook URL from config snapshots.
- [ ] Trigger HTTP notification only after an ENTRY `MyTrade` callback and never block the execution lock or protective orders.
- [ ] Record only enabled/disabled and HTTP result code in research logs, never the webhook URL.

### Task 4: Verification and deployment

**Files:**
- Verify: `OPFStrategyV1/Scripts/tests/LiveProtectionFailClosedTests/LiveProtectionFailClosedTests.csproj`
- Verify: `OPFStrategyV1/OPFStrategyV1.csproj`

- [ ] Run regression tests.
- [ ] Build the DLL with zero errors.
- [ ] Write the supplied webhook only to the local ATAS JSON configuration, then verify snapshots do not contain it.
- [ ] Compare source and deployed DLL SHA-256 hashes.
