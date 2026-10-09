# V4 Candidate Scenario Tape Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 新增可全量运行的零订单候选场景采集模式，为离线 PortfolioEngine 提供决策时的完整候选与冻结 Zone 上下文，同时避免逐笔全市场文件的成本。

**Architecture:** 保留现有 `MarketExecutionTapeDataOnly` 作为四日深层 Tick 校准模式。新增 `CandidateScenarioTapeDataOnly`；它复用相同的候选捕获与 Zone 状态内存层，但不调用 Tick Tape、Zone 行为 Ledger 的 CSV 输出。启动时由唯一 JSON RunMode 强制关闭 Actual orders。

**Tech Stack:** C# / ATAS strategy / Python `unittest` 合同测试。

---

### Task 1: 为新运行模式写失败合同测试

**Files:**
- Modify: `OPFStrategyV1/Scripts/tests/test_v400_market_execution_tape_contract.py`

- [ ] **Step 1: 写入失败测试**

```python
def test_candidate_scenario_mode_collects_context_without_tick_or_zone_csv(self):
    self.assertIn('CandidateScenarioTapeDataOnly', self.strategy)
    self.assertIn('CandidateScenarioTapeDataOnly || MarketExecutionTapeDataOnly', self.scenario)
    self.assertIn('ZoneContextCollectionEnabled', self.zone_behavior)
    self.assertIn('ZoneBehaviorOutputEnabled', self.zone_behavior)
    self.assertIn('if (!MarketExecutionTapeDataOnly', self.tape)
```

- [ ] **Step 2: 运行测试确认失败**

Run: `python -m unittest OPFStrategyV1/Scripts/tests/test_v400_market_execution_tape_contract.py -v`

Expected: FAIL，因为新模式及两层 Zone 开关尚不存在。

- [ ] **Step 3: 最小实现**

为策略添加新模式、强制零订单、候选写盘条件及 Zone 内存/输出拆分；不改动 Actual 路径。

- [ ] **Step 4: 再运行测试**

Run: 同 Step 2。

Expected: PASS。

### Task 2: 新增唯一采集 Profile 与 Gate 验证器

**Files:**
- Create: `OPFStrategyV1/Configs/OPFStrategyV1_v400_candidate_scenario_tape_data_only.json`
- Create: `OPFStrategyV1/Scripts/Validate-OPFV400CandidateScenarioTape.py`
- Modify: `OPFStrategyV1/Scripts/tests/test_v400_market_execution_tape_contract.py`

- [ ] **Step 1: 写入失败测试**

```python
def test_candidate_scenario_profile_and_validator_require_zero_orders_without_tick_files(self):
    self.assertIn('V400_CANDIDATE_SCENARIO_TAPE_DATA_ONLY', self.profile)
    self.assertIn('CandidateScenarioTapeDataOnly', self.profile)
    self.assertIn('market_execution_scenarios.csv', self.validator)
    self.assertIn('market_execution_ticks.csv', self.validator)
```

- [ ] **Step 2: 运行测试确认失败**

Run: `python -m unittest OPFStrategyV1/Scripts/tests/test_v400_market_execution_tape_contract.py -v`

Expected: FAIL，因为 Profile 与验证器尚不存在。

- [ ] **Step 3: 最小实现**

Profile 固定 `EnableActualOrders=false` 与新 RunMode。验证器检查 Config 身份、场景/风险一一对应、CandidateID 唯一、冻结 Zone 边界、零订单、零 `ENTRY_SEND`，并要求不存在 Tick 与 Zone Ledger 全量 CSV。

- [ ] **Step 4: 运行全部测试与构建**

Run: `python -m unittest discover -s OPFStrategyV1/Scripts/tests -p test_*.py -v` 以及 `dotnet build OPFStrategyV1/OPFStrategyV1.csproj -c Debug --no-restore`。

Expected: 所有测试通过，构建 0 警告、0 错误。

### Task 3: 部署前保护与四日 Gate 准备

**Files:**
- Modify: `项目恢复总览_继续开发指南.md`
- Create: `OPFStrategyV1/Reports/v400_candidate_scenario_tape_contract.md`

- [ ] **Step 1: 记录不可变边界与 Gate**

记录新模式仅用于采集、不得修改 Actual 规则；四日 Gate 复用 `2026-01-16, 2026-02-11, 2026-04-13, 2026-07-07`，并逐项列明零订单、无 Tick/Zone 全量文件、场景完整性与四日深层样本字段一致性。

- [ ] **Step 2: 归档当前四日深层证据、编译部署、清活动日志**

仅在 Task 1-2 验证通过后执行。保留原始深层证据，部署唯一 JSON Profile，清除活动日志并要求完全重启 ATAS。
