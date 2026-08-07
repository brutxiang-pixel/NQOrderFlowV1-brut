# 八路径执行与人工提示 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 仅允许八条冻结路径运行，并以一个安全、互斥的配置在自动下单和人工提示之间切换。

**Architecture:** 在执行配置中加入 `ExecutionMode`，由加载阶段归一化为唯一安全行为。主策略使用一个路径/方向策略表作为执行资格和人工提示的共同来源；从研究工作树仅精确引入显著区首次触达引擎和其最小接线。

**Tech Stack:** C#、.NET 10、ATAS Strategy API、JSON 配置、PowerShell/Python 回归脚本。

---

### Task 1: 固化配置模式和风险语义

**Files:**
- Modify: `OPFStrategyV1/Core/Configuration/ActualExecutionSettings.cs`
- Modify: `OPFStrategyV1/Configs/OPFStrategyV1_actual_execution.default.json`
- Test: `OPFStrategyV1/Scripts/tests/test_execution_mode_config.py`

- [ ] **Step 1: 写失败测试**

测试默认 JSON 包含 `ExecutionMode: ManualAlert`、1 手、$200 日损、0 周损、12 笔；并静态断言 `ActualWeeklyLongLossLimitDollars` 的归一化不将 `0` 替换为默认值。

- [ ] **Step 2: 运行并确认失败**

Run: `python -m pytest OPFStrategyV1/Scripts/tests/test_execution_mode_config.py -q`

- [ ] **Step 3: 实现最小配置改动**

给 record 加入 `ExecutionMode`；仅接受 `Auto`/`ManualAlert`；令 `ManualAlert` 强制禁用实际订单，`Auto` 强制关闭人工提示；0 周损保持关闭。

- [ ] **Step 4: 运行测试确认通过**

Run: `python -m pytest OPFStrategyV1/Scripts/tests/test_execution_mode_config.py -q`

### Task 2: 建立八路径唯一资格表

**Files:**
- Modify: `OPFStrategyV1/Strategy/OpeningPullbackFailureStrategy.cs`
- Test: `OPFStrategyV1/Scripts/tests/test_eight_path_execution_policy.py`

- [ ] **Step 1: 写失败测试**

测试静态策略表仅包含八个标签及冻结方向，`UnknownRegimeZoneTouch`、`ZoneBirthResearch` 和 `HtfTrendContinuation` 不在表中；人工提示不再硬编码 Unknown Long。

- [ ] **Step 2: 运行并确认失败**

Run: `python -m pytest OPFStrategyV1/Scripts/tests/test_eight_path_execution_policy.py -q`

- [ ] **Step 3: 实现最小策略表**

以一个私有路径/方向策略表替换旧执行白名单和 Unknown Long 专用提示入口。执行与提示均先检查同一表；相同候选键仅保留优先级较高的路径。

- [ ] **Step 4: 运行测试确认通过**

Run: `python -m pytest OPFStrategyV1/Scripts/tests/test_eight_path_execution_policy.py -q`

### Task 3: 精确接回显著区首次触达

**Files:**
- Create: `OPFStrategyV1/Core/Signals/SignificantZoneFirstTouchEngine.cs`
- Modify: `OPFStrategyV1/Strategy/OpeningPullbackFailureStrategy.cs`
- Modify: `OPFStrategyV1/Strategy/OpeningPullbackFailureStrategy.SignificantZones.cs`
- Modify: `OPFStrategyV1/OPFStrategyV1.csproj`
- Test: `OPFStrategyV1/Scripts/tests/test_significant_zone_first_touch_wiring.py`

- [ ] **Step 1: 写失败测试**

测试项目包含 FirstTouch 引擎、主策略有 Long/Short 路径常量和队列激活点、显著区模块调用创建逻辑，且不引入 HTF 路径。

- [ ] **Step 2: 运行并确认失败**

Run: `python -m pytest OPFStrategyV1/Scripts/tests/test_significant_zone_first_touch_wiring.py -q`

- [ ] **Step 3: 仅移植最小实现**

从 `NQOrderFlowV1-trend-pullback-research` 移植 FirstTouch 引擎与其候选排队/下一根激活接线。保留 A、Active、未测试、首次触达约束，禁止迁入 HTF/Sweep 研究模式。

- [ ] **Step 4: 运行测试确认通过**

Run: `python -m pytest OPFStrategyV1/Scripts/tests/test_significant_zone_first_touch_wiring.py -q`

### Task 4: 构建、部署和回归验收

**Files:**
- Modify: `OPFStrategyV1/Configs/OPFStrategyV1_actual_execution.default.json`
- Test: `OPFStrategyV1/Scripts/tests/test_execution_mode_config.py`
- Test: `OPFStrategyV1/Scripts/tests/test_eight_path_execution_policy.py`
- Test: `OPFStrategyV1/Scripts/tests/test_significant_zone_first_touch_wiring.py`

- [ ] **Step 1: 执行脚本回归**

Run: `python -m pytest OPFStrategyV1/Scripts/tests/test_execution_mode_config.py OPFStrategyV1/Scripts/tests/test_eight_path_execution_policy.py OPFStrategyV1/Scripts/tests/test_significant_zone_first_touch_wiring.py -q`

- [ ] **Step 2: 构建 DLL**

Run: `& "C:\Program Files\dotnet\dotnet.exe" build "OPFStrategyV1\OPFStrategyV1.csproj" -c Debug`

- [ ] **Step 3: 部署**

复制已构建 DLL 和默认 `ManualAlert` 配置到 ATAS 配置位置；不删除任何日志。

- [ ] **Step 4: 核对部署物**

确认配置快照为 1 手、$200、0 周损、12 笔，以及 DLL 时间戳晚于构建开始时间。
