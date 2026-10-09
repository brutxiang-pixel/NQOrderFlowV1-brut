# ManualAlert 与路径治理实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 禁用确认的低质量旧路径，恢复四条 HTF 单边路径，并提供默认关闭、绝不自动下单的 ManualAlert 模式。

**Architecture:** 路径治理只改变实际执行白名单，不删除信号代码。HTF 路径从隔离工作树精确迁入当前显著区基线。ManualAlert 在最终候选通过原有门禁后替代下单行为，写 HUD、ATAS 通知与事件，但不创建执行状态。

**Tech Stack:** C# / ATAS Strategy API / JSON 配置 / PowerShell 与 Python 契约测试。

---

### Task 1: 建立隔离实现基线并恢复 HTF 路径

**Files:**
- Modify: `OPFStrategyV1/Strategy/OpeningPullbackFailureStrategy.cs`
- Create: `OPFStrategyV1/Core/Signals/HtfTrendContinuationEngine.cs`
- Modify: `OPFStrategyV1/Research/ResearchLogger.cs`
- Test: `OPFStrategyV1/Scripts/Test-HtfTrendContinuationReplayContract.py`
- Test: `OPFStrategyV1/Scripts/Test-HtfSweepReclaimReplayContract.py`

- [ ] **Step 1: 写入失败的 HTF 路径契约测试**

验证四个名称 `HtfTrendContinuationLong`、`HtfTrendContinuationShort`、`HtfSweepReclaimLong`、`HtfSweepReclaimShort` 同时出现在策略常量、路径资格判断和默认实际执行白名单中。

- [ ] **Step 2: 运行测试确认失败**

Run: `python OPFStrategyV1/Scripts/Test-HtfTrendContinuationReplayContract.py`

Expected: FAIL，因为当前主工作树缺少四条路径的实际执行接入。

- [ ] **Step 3: 精确迁移实现**

仅从 `C:\Users\Administrator\source\repos\NQOrderFlowV10629\NQOrderFlowV1-trend-pullback-research` 迁移 HTF 引擎、四条路径的候选、入场、风险归一化仓位、结构退出和研究日志所需代码；保留当前工作树的 SignificantZone 代码及其调用顺序。

- [ ] **Step 4: 运行 HTF 契约测试**

Run: `python OPFStrategyV1/Scripts/Test-HtfTrendContinuationReplayContract.py; python OPFStrategyV1/Scripts/Test-HtfSweepReclaimReplayContract.py`

Expected: PASS。

### Task 2: 固化路径禁用白名单

**Files:**
- Modify: `OPFStrategyV1/Core/Configuration/ActualExecutionSettings.cs`
- Modify: `OPFStrategyV1/Configs/OPFStrategyV1_actual_execution.default.json`
- Create: `OPFStrategyV1/Configs/OPFStrategyV1_manual_alert.json`
- Test: `OPFStrategyV1/Scripts/Test-ActualExecutionSettingsContract.py`

- [ ] **Step 1: 写入失败的路径治理契约测试**

断言 13 条已确认关闭路径不在默认实际白名单；4 条 HTF 路径与保留路径在白名单中。

- [ ] **Step 2: 运行测试确认失败**

Run: `python OPFStrategyV1/Scripts/Test-ActualExecutionSettingsContract.py`

Expected: FAIL，因为当前白名单仍包含关闭路径且缺少四条 HTF 路径。

- [ ] **Step 3: 最小配置修改**

更新默认执行档案与新的 ManualAlert 档案。禁用通过白名单实现；不删除任何路径代码，不改动风险/时段/显著区规则。

- [ ] **Step 4: 运行配置契约测试**

Run: `python OPFStrategyV1/Scripts/Test-ActualExecutionSettingsContract.py`

Expected: PASS。

### Task 3: 实现 ManualAlert 零订单提示分支

**Files:**
- Modify: `OPFStrategyV1/Core/Configuration/ActualExecutionSettings.cs`
- Modify: `OPFStrategyV1/Strategy/OpeningPullbackFailureStrategy.cs`
- Modify: `OPFStrategyV1/Research/ResearchLogger.cs`
- Test: `OPFStrategyV1/Scripts/tests/test_manual_alert_contract.py`

- [ ] **Step 1: 写入失败的 ManualAlert 契约测试**

测试默认 `ManualAlertEnabled=false`；启用时配置强制 `EnableActualOrders=false`；策略源码存在 `MANUAL_ALERT` 事件、HUD 标记、一次性 SignalID 去重和账户非空仓抑制；提示分支不能调用订单注册方法。

- [ ] **Step 2: 运行测试确认失败**

Run: `python OPFStrategyV1/Scripts/tests/test_manual_alert_contract.py`

Expected: FAIL，因为配置和提示分支尚不存在。

- [ ] **Step 3: 最小实现**

在所有既有执行前置门禁通过、准备提交订单的位置，若 `ManualAlertEnabled` 为真则构造提示票据并返回。票据包含路径、方向、Entry、SL、TP、数量、风险、质量与显著区上下文；调用既有 `RaiseShowNotification`，追加 `MANUAL_ALERT`，更新 HUD。不得创建 `ReplayExecutionState`，不得调用任何订单 API。

- [ ] **Step 4: 运行 ManualAlert 契约测试**

Run: `python OPFStrategyV1/Scripts/tests/test_manual_alert_contract.py`

Expected: PASS。

### Task 4: 编译、回归与 Smoke 打包

**Files:**
- Modify: `OPFStrategyV1/Scripts/Clear-OPFLogs.ps1`（仅当需要识别新的 ManualAlert 文件）
- Create: `OPFStrategyV1/Reports/manual_alert_path_governance_smoke_contract.md`

- [ ] **Step 1: 编译 Debug DLL**

Run: `& "C:\Program Files\dotnet\dotnet.exe" build "OPFStrategyV1\OPFStrategyV1.csproj" -c Debug`

Expected: 0 errors，且无新增 warning。

- [ ] **Step 2: 执行全部新增与既有契约测试**

Run: `python OPFStrategyV1/Scripts/Test-ActualExecutionSettingsContract.py; python OPFStrategyV1/Scripts/Test-HtfTrendContinuationReplayContract.py; python OPFStrategyV1/Scripts/Test-HtfSweepReclaimReplayContract.py; python OPFStrategyV1/Scripts/tests/test_manual_alert_contract.py`

Expected: 全部 PASS。

- [ ] **Step 3: 写入 Smoke 合同并打包档案**

Smoke 首先验证 `ManualAlertEnabled=true`：无订单、候选提示和四条路径可见；其次验证实际执行档案：关闭路径零 Execute、保留路径及四条 HTF 路径可进入正常执行链。部署时复制对应配置，用户无需手工改图表配置。

- [ ] **Step 4: 提交实现**

Run: `git add <changed files>; git commit -m "feat: add manual alert path governance"`

Expected: 仅包含本计划产生的代码、测试、配置和 Smoke 合同。
