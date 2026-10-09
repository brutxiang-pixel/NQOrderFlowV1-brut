# V5.0 FailureReverse 前置确认 WideShort：8 日配对 Actual Replay 合同

状态：预注册；本合同只定义功能与归因验证，不代表收益晋级。

## 固定基线与替换

A 为冻结 `OPF_RESEARCH_2.31 / ACTUAL_EXEC_2.48` 完整组合。B 使用当前加载器兼容的 `ACTUAL_EXEC_2.51` 配置架构，保留 A 的所有路径与经济参数，仅将即时 `FailureReverse_ObservationInvalidated_WideStop1_5R` 替换为 `FailureReverse_PreEntryConfirmedWideStopShort`。B 绝不允许旧即时 WideStop FR 下单。

固定参数：3 手、15 笔/日、-$250 日损、-$500 周 Long 门、$1.20/手往返手续费、`ActualRequireFailureRetest=false`。

## 严格替代规则

仅 BullFVG 源区失效后的 Short WideStop FR，且源时刻 `penetrationR >= 0.25`，可进入 armed。源时刻必须逐项通过旧即时路径的同步准入（路径、质量、RR/风险、同柱、日损、日单量、持仓槽位和账户仓位）；否则只写 `FR_PREENTRY_LEGACY_REJECTED`，后续不得下单。

armed 只检查下一根完整 M5；该根收盘低于源 ZoneLow 才确认。确认价、止损、风险、4R 目标与 OCO 均按确认时重算；异步报价与订单准入仍由原执行链独立审计。

## 固定日期与裁决

每个日期先跑 A 再跑 B，窗口均为 06:00 至次日 05:00：

`2025-04-29, 2025-06-24, 2025-10-09, 2025-12-04, 2026-01-23, 2026-04-20, 2026-06-22, 2026-07-08`

每个 B 的 `FR_PREENTRY_CONFIRM` 必须由 `Validate-OPFV500FailureReverseStrictReplacement.py` 唯一映射到 A 同一 `SignalID` 的旧 `ENTRY_SEND`。任一 `A_ENTRY_SEND_REQUIRED`、孤立保护、重复提交、无保护成交或未解释的非 FR 路径漂移，均判本轮失败。收益只如实记录，不设通过线。

## B 配置前置条件

B 的每个 `ConfigSnapshot` 必须为 `ActualExecutionConfigStatus=loaded`、`ACTUAL_EXEC_2.51` 和 `V500_FR_PREENTRY_WIDESHORT_CALIBRATION`；路径必须包含 `FailureReverse_PreEntryConfirmedWideStopShort` 且不得包含旧即时 WideStop FR。验证器会将版本自动迁移、错误 Profile、旧路径仍启用或缺少 ConfigSnapshot 直接判失败，禁止以零确认的空 PASS 结案。
