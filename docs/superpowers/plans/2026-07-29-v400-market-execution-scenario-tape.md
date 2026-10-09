# V4 Market Execution Scenario Tape Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在四日 Data Only 采集中，记录每个已形成执行候选的决策时状态，并与逐笔 Market Execution Tape 同步落盘，且绝不产生真实订单。

**Architecture:** 逐笔带继续由现有 `OnNewTrade` 记录。新增独立 partial 在 `TrySubmitReplayExecution` 入口冻结候选、报价、计划风险、组合状态与 Zone Context；Data Only 直接以 `ActualOrdersDisabled` 终态保存，不执行任何订单或风险门副作用。停止时由 `ResearchLogger` 批量写出候选 CSV。

**Tech Stack:** C#/.NET 10、ATAS Strategy API、Python `unittest` 静态契约测试。

---

### Task 1: 锁定零订单与入口捕获契约

**Files:**
- Create: `OPFStrategyV1/Scripts/tests/test_v400_market_execution_tape_contract.py`
- Modify: `OPFStrategyV1/Strategy/OpeningPullbackFailureStrategy.cs`

- [ ] **Step 1: Write failing test**

```python
def test_data_only_blocks_orders_and_captures_before_early_return():
    source = STRATEGY.read_text(encoding="utf-8")
    assert "!MarketExecutionTapeDataOnly" in source
    start = source.index("private void TrySubmitReplayExecution(")
    capture = source.index("CaptureMarketExecutionScenario(", start)
    early = source.index("if (!ActualOrdersEnabled || _snapshot is null)", start)
    assert capture < early
```

- [ ] **Step 2: Verify RED**

Run: `python -m unittest OPFStrategyV1/Scripts/tests/test_v400_market_execution_tape_contract.py -v`

Expected: FAIL because Scenario Tape capture does not exist and MarketExecutionTape mode is not a defense-in-depth order block.

- [ ] **Step 3: Implement minimal guarded entry capture**

Add `!MarketExecutionTapeDataOnly` to `ActualOrdersEnabled`. At beginning of `TrySubmitReplayExecution`, call `CaptureMarketExecutionScenario(...)`; after the no-orders early return, resolve it as `DataOnlyObserved/ActualOrdersDisabled`.

- [ ] **Step 4: Verify GREEN**

Run same `unittest` command. Expected: PASS.

### Task 2: 实现不可变 Candidate Scenario Tape

**Files:**
- Create: `OPFStrategyV1/Strategy/OpeningPullbackFailureStrategy.MarketExecutionScenario.cs`
- Modify: `OPFStrategyV1/Research/ResearchLogger.cs`
- Test: `OPFStrategyV1/Scripts/tests/test_v400_market_execution_tape_contract.py`

- [ ] **Step 1: Write failing test**

```python
def test_scenario_tape_has_stable_identity_zone_context_and_stop_flush():
    scenario = SCENARIO.read_text(encoding="utf-8")
    logger = LOGGER.read_text(encoding="utf-8")
    for token in ("CandidateId", "ZoneId", "ResolveMarketExecutionScenario", "FlushMarketExecutionScenarios"):
        assert token in scenario
    assert "AppendMarketExecutionScenario" in logger
    assert "market_execution_scenarios.csv" in logger
```

- [ ] **Step 2: Verify RED**

Run same `unittest` command. Expected: FAIL because no Scenario Tape implementation exists.

- [ ] **Step 3: Implement minimal immutable collector**

Use `SignalID|ResearchPath|EntryTime|EntryBar` as candidate identity. Store decision-time values only: planned entry/stop/risk/target, bid/ask, daily and weekly state, slot state, gate flags, Zone fields already on `CandidateSignal.Zone`, and `DataOnlyObserved/ActualOrdersDisabled` terminal state. Stop flush emits one row per identity.

- [ ] **Step 4: Verify GREEN**

Run same `unittest` command. Expected: PASS.

### Task 3: 固定唯一四日 Profile 并验证构建

**Files:**
- Create: `OPFStrategyV1/Configs/OPFStrategyV1_v400_market_execution_tape_4day_data_only.json`
- Modify: `OPFStrategyV1/Core/Configuration/ActualExecutionSettings.cs`
- Modify: `OPFStrategyV1/Core/Versions/StrategyVersions.cs`

- [ ] **Step 1: Add profile**

Set `EnableActualOrders=false`, `RunProfileId=V400_MARKET_EXECUTION_TAPE_4DAY_DATA_ONLY`, `RunMode=MarketExecutionTapeDataOnly`, compact logs, and preserve current frozen 3-contract risk configuration.

- [ ] **Step 2: Compile and test**

Run: `& "C:\Program Files\dotnet\dotnet.exe" build .\OPFStrategyV1\OPFStrategyV1.csproj -c Debug`

Expected: 0 warning / 0 error.

- [ ] **Step 3: Deployment discipline**

Before any later compile, archive active evidence. After this compile, clear activity logs, deploy only this JSON to `%APPDATA%\ATAS\StrategyConfigs\OPFStrategyV1_actual_execution.json`, and require complete ATAS restart before the four dates.
