# 八路径执行与人工提示设计

## 目标

将运行范围收敛至四个家族中的八条路径；同一套候选、质量和风险门禁同时支持自动下单与仅人工提示两种互斥模式。

## 冻结路径与方向

| 家族 | 路径 | 方向 |
|---|---|---|
| OC | `ObservationConfirm` | Long、Short |
| OC | `ObservationConfirm_WideStop1_5R` | Long、Short |
| Breakaway | `BreakawayFvg` | Short |
| Breakaway | `BreakawayFvg_Qualified` | Long、Short |
| FailureReverse | `FailureReverse_ObservationInvalidated_WideStop1_5R` | Short |
| FailureReverse | `FailureReverse_RetestFailed` | Short |
| Significant zone | `SignificantZoneFirstTouchLong` | Long |
| Significant zone | `SignificantZoneFirstTouchShort` | Short |

其他所有路径均为 `PathDisabled`，不产生订单或人工提示。

## 执行模式

配置新增 `ExecutionMode`，仅允许：

- `Auto`：八路径经过既有质量、时段、账户仓位和风险门禁后，可提交订单及既有保护单。
- `ManualAlert`：使用完全相同的候选和门禁；仅写入 `MANUAL_ALERT`、HUD 和 ATAS 通知。不得发送订单、OCO 或管理人工仓位。

旧布尔字段仅用于旧配置兼容读取。新配置中模式字段为唯一权威；无效或冲突组合以安全的 `ManualAlert` 解释并记录加载状态。

## 唯一机会与优先级

同一信号/同一入场 bar 只允许一个路径进入执行或提示。优先顺序：

1. `SignificantZoneFirstTouchLong` / `SignificantZoneFirstTouchShort`
2. `FailureReverse_ObservationInvalidated_WideStop1_5R`
3. `FailureReverse_RetestFailed`
4. `BreakawayFvg_Qualified`
5. `BreakawayFvg`
6. `ObservationConfirm_WideStop1_5R`
7. `ObservationConfirm`

显著区首次触达保持其既有语义：仅 A 级、Active、未测试区的第一次有效触达；第二次触达不会生成候选。

## 全局风控

- 数量：1 手。
- 日损：$200。
- 周损：关闭。配置值 `0` 必须保持为关闭，不能被默认 $500 覆盖。
- 每日最多 12 笔。

在 `ManualAlert` 下，这些参数限制提醒资格并展示建议风险；用户手工订单不由策略接管或强制计数。

## 验收

1. `ManualAlert` 对八路径均可产生 `MANUAL_ALERT`，不出现 `ENTRY_SEND`、OCO 或保护单。
2. `Auto` 仅八路径可进入订单提交；所有其他路径记录 `PathDisabled`。
3. 周损 `0` 在配置快照中保持 `0`。
4. 显著区首次触达可生成 Long/Short 候选，且第二次触达被拒绝。
5. 显著区历史和实时绘制不回归。
6. 编译为 0 warning、0 error；部署 DLL 与默认配置但不清理活动日志。
