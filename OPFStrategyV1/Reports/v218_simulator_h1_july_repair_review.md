# v2.18 离线模拟器 H1＋7月一致性修复复核

日期：2026-07-26

## 裁决

离线模拟器对v2.18当前政策基线的一致性修复通过。2026 H1 122日与2026年7月MTD 17日均达到发送身份、第二槽、发送后中止、正常成交、退出角色、Gross和AccountNet精确一致。

本结论只证明“当前政策基线复刻”通过。当前政策已发生的成交与发送后中止使用受限Actual锚；未成交替代候选仍使用压缩行情预测，因此反事实方案仍需报告预测部分占比，并最终由有限ATAS Replay裁决。

## 正式结果

| 指标 | 2026 H1 122日 | 2026年7月MTD 17日 |
|---|---:|---:|
| Candidate rows | 1,356 | 100 |
| Actual outcome anchors | 964 | 73 |
| Actual abort anchors | 72 | 4 |
| Predicted / Actual ENTRY_SEND | 1,036 / 1,036 | 77 / 77 |
| Predicted / Actual Secondary | 176 / 176 | 9 / 9 |
| 精确发送身份 | 1,036 | 77 |
| 漏预测 / 多预测 | 0 / 0 | 0 / 0 |
| Predicted / Actual Preflight abort | 72 / 72 | 4 / 4 |
| 配对 / Actual正常交易 | 953 / 953 | 73 / 73 |
| 退出角色一致率 | 100% | 100% |
| 正常交易Gross | +$24,287.48 / +$24,287.48 | -$609.50 / -$609.50 |
| 账户Gross | +$24,239.98 | -$609.50 |
| Predicted / Actual AccountNet | +$20,769.58 / +$20,769.58 | -$872.30 / -$872.30 |
| Identity Gate | PASS | PASS |
| Economic Gate | PASS | PASS |

H1的964个Actual outcome anchors由953笔正常交易和11笔Abnormal/Quarantine隔离成交组成。隔离成交不计入15笔正常交易上限，但按真实退出时间占用槽位，并计入账户损益和日损链。

## 已修复根因

1. 严格风险带原先在`ENTRY_SEND`前阻止，现复刻Actual先发送、后Preflight中止的顺序。
2. 第二槽与`$300`组合风险门使用计划风险；成交后活动风险使用刷新报价后的ExactRisk。
3. G2资格改用C#权威名单，不再用浮点评分近似。
4. 未见月份静态评分使用只基于H1历史的全局fallback，7月不再触发`KeyError: ('2026-07', '__GLOBAL__')`，且不读取7月结果。
5. CLI冻结配置显式改为15笔、日损`-$250`、组合风险`$300`，不再误用旧默认18笔、日损`-$450`。
6. 补齐`BreakawayShortWideV128`风险标签，Short Breakaway宽风险上限正确复刻为30点。
7. 当前政策正常成交及Abnormal/Quarantine隔离成交使用Actual退出时间、退出角色和Gross锚定；隔离单不计正常单量。
8. 刷新报价无法由压缩Tape重建的当前政策发送后中止，使用身份严格匹配的Actual abort锚定。
9. 新增`--candidate-tape`缓存入口，H1复核由反复读取3.26GB证据的数分钟降到约4秒；Actual台账只读取交易、PnL和事件文件。
10. 新增逐候选`blocked_trace.csv`，可以直接定位DailyLoss、Active、G2、组合风险和日单量分叉。

## 使用边界

- 可用于：当前退出政策不变时的选单、日单量、日损、G2、并发风险和路径组合初筛。
- Actual锚只覆盖当前政策已经实际发送/成交的身份；新增或替代候选仍是预测结果，不得宣称100%精确。
- 若研究改变TP/SL、TimeStop或保护逻辑，不得沿用当前政策Actual outcome锚作为候选退出结果，必须重新计算对应退出或采集新证据。
- 离线通过仍只允许进入Smoke；最终晋级继续由有限ATAS Replay确认。全量Replay只用于最终候选。

## 验证

- `Test-OPFV217DualSlotCalibration.py`：10项通过。
- `py_compile`：通过。
- `git diff --check`：通过。
- 未修改或编译DLL；未清理、未归档当前7月日志。

证据目录：

- `OPFStrategyV1/Reports/v218_simulator_repair_h1_anchored/`
- `OPFStrategyV1/Reports/v218_simulator_repair_july_mtd_anchored/`

