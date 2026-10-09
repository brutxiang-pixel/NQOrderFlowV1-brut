# Live Protection Fail-Closed Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ensure every confirmed live entry is protected by a working stop or is fail-closed flattened.

**Architecture:** A small pure guard defines whether a confirmed entry requires emergency protection handling. The strategy starts an out-of-lock watchdog when an entry fill arrives; it latches new entries and submits an exact-quantity market flatten if no stop becomes working.

**Tech Stack:** C# / .NET 10 / ATAS Strategies.

---

### Task 1: Protect the entry-to-stop gap

**Files:**
- Create: `OPFStrategyV1/Strategy/LiveProtectionFailClosedGuard.cs`
- Create: `OPFStrategyV1/Scripts/tests/LiveProtectionFailClosedTests/LiveProtectionFailClosedTests.csproj`
- Create: `OPFStrategyV1/Scripts/tests/LiveProtectionFailClosedTests/Program.cs`
- Modify: `OPFStrategyV1/Strategy/OpeningPullbackFailureStrategy.cs`

- [ ] Write a failing test proving a confirmed, open fill with no working stop requires protection recovery, while a working stop, completed exit, or existing emergency flatten does not.
- [ ] Run the test and confirm it fails because `LiveProtectionFailClosedGuard` is missing.
- [ ] Add the pure guard and run the test again.
- [ ] Start the watchdog immediately after a confirmed entry fill; remove the `BracketSubmitted` requirement from the protection-gap detection.
- [ ] On timeout, latch Actual entries, log and notify, then submit exact confirmed remaining quantity through an out-of-lock emergency path.

### Task 2: Correct live-time classification and verify

**Files:**
- Modify: `OPFStrategyV1/Strategy/OpeningPullbackFailureStrategy.cs`

- [ ] Make `IsHistoricalReplayTime` compare UTC instants.
- [ ] Run the new guard test and `dotnet build OPFStrategyV1/OPFStrategyV1.csproj -c Debug`.
- [ ] Inspect the build output and deployed DLL timestamp/hash before requesting a one-trade controlled exam-account validation.
