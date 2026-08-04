# 路径治理与 ManualAlert 设计

## 目标

在不删除既有策略代码的前提下，关闭已确认低质量路径，恢复四条 HTF 单边路径，并新增绝不自动下单的人工提示模式。

## 路径治理

通过 `ActualExecutionPaths` 白名单关闭下列路径；代码保留，配置回退即可恢复：

- `ZoneBirthResearch`
- `FailureReverse_ObservationInvalidated`
- `FailureReverse_ObservationInvalidated_WideStop1_5R`
- `FailureReverse_RetestFailed`
- `FailureReverse_RetestFailed_WideStop1_5R`
- `BreakawayFvg`
- `BreakawayFvg_Qualified`
- `AlmostConfirmed`
- `ObservationStrict_Other`
- `ObservationStrict_Other_WideStop1_5R`
- `ObservationStrict_BullFresh_WideStop1_5R`
- `ShadowCandidate`
- `ObservationConfirm`

保留 `ObservationConfirm_WideStop1_5R`、`UnknownRegimeZoneTouch`、`TrendPullbackConfirmed` 与 `BreakawayRetest`。四条 HTF 路径不参与本轮淘汰，因为其未进入 2025-04 至 2025-07 的实际执行白名单。

## HTF 单边路径恢复

从隔离工作树 `NQOrderFlowV1-trend-pullback-research` 仅迁移四条已实现路径及其必要依赖：

- `HtfTrendContinuationLong`
- `HtfTrendContinuationShort`
- `HtfSweepReclaimLong`
- `HtfSweepReclaimShort`

迁移不得覆盖当前显著区生命周期、排序、历史投影或统一 ZoneDecision 实现。四条路径加入执行白名单，并继续接受全部既有风险、时段、Globex、延迟、显著区和账户安全门禁。

## ManualAlert

新增显式布尔配置 `ManualAlertEnabled`，默认 `false`。

- `false`：保持当前 ActualExecution 行为，不产生人工提示。
- `true`：强制禁止策略订单提交、撤单、保护单和强平；完整执行候选评估；仅对通过全部静态和账户前置门禁的候选发送一次提示。

提示包含路径、方向、计划入场价、止损、止盈、数量、初始风险、质量分和显著区依据。提示同时写入 HUD、ATAS 通知和独立 `MANUAL_ALERT` 事件。账户非空仓时不发送新的入场提示，以维持单持仓纪律。

人工成交不是策略管理订单：ManualAlert 不伪造成交、持仓、手续费、日单数、日损或周损状态。相关限制在 HUD 中仅作为人工风控参考；只有策略管理的订单才可由代码强制执行这些限制。

## 安全与验收

1. `ManualAlertEnabled=true` 时，所有订单 API 调用计数为零，且每个合格候选只产生一次 `MANUAL_ALERT`。
2. `ManualAlertEnabled=false` 时，提示事件为零，ActualExecution 行为不变。
3. 四条 HTF 路径在配置快照白名单中出现，且实际候选可通过现有门禁到达执行/提示分支。
4. 被关闭路径在最终执行白名单中不存在；保留路径和四条 HTF 路径均存在。
5. 编译无新增警告，并对配置契约与提示分支运行自动化测试。
