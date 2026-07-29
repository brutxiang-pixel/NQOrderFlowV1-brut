# v2.17 双槽离线连接器三日校准复核

日期：2026-07-26

## 裁决

本轮离线连接器修复通过三日开发集校准：`ENTRY_SEND`身份、第二槽身份、报价前置中止和正常交易配对均与Actual一致；加入已知执行缺口储备后，保守Net与Actual账户Net完全一致。

该结论只表示开发集校准通过，不等于未参与修模日期的盲验通过。盈利研究继续暂停，下一步只允许使用冻结的新日期验证连接器；盲验通过前不得用本轮连接器给出盈利晋级结论。

## 证据范围

- 日期：`2026-01-02,2026-04-24,2026-05-05`
- Actual证据：`v217_3day_secondary_retry_smoke_evidence/`
- 校准输出：`v217_dual_slot_connector_calibration/`
- 版本：`OPF_RESEARCH_2.17 / ACTUAL_EXEC_2.42`
- 活动日志保持69个文件，未清理。

## 根因与修复

旧离线结果51笔/15笔S2来自v2.14旧Tape。把同一连接器切到v2.17三日Tape后，金额已接近Actual，说明主要偏差不是退出引擎全面失真，而是旧Tape与当前执行链不匹配。

本轮连接器只修三项已证实的组合状态错误：

1. 用活动持仓列表替代固定Primary/Secondary槽字典。Primary退出而Secondary仍存活时，新候选仍按“当前已有一笔持仓”进入Secondary评估，不回填Primary。
2. 显式模拟`ENTRY_SEND -> PreflightAborted`。报价预检中止保留发送轨迹，但不计交易、不占槽位，后续候选可继续评估。
3. 移除离线端固定5分钟释放延迟；持仓在记录的实际退出时间后立即释放。

新增3项回归测试，分别覆盖Preflight中止不占槽、活动列表压缩后的Lane判定，以及Quarantine手续费不得重复扣除。

## 身份与事件Gate

- CandidateRows：49
- Predicted / Actual `ENTRY_SEND`：49 / 49
- 精确`SignalID + ResearchPath + Lane`身份：49 / 49
- Missing / Extra：0 / 0
- Predicted / Actual Secondary：13 / 13
- Predicted / Actual PreflightAborted：2 / 2
- Actual Wait / Ready / Timeout：8 / 7 / 1
- 正常交易配对：45 / 45

两笔前置中止均被正确复刻：

- `20260102-0105...OC`：刷新报价风险26.25点，高于WideStop Long 25点上限；发送后中止，不占槽。
- `20260424-0940...OC`：风险12.75点落入严格禁带；发送后中止，不占槽。

## 经济层Gate

- 退出角色一致：43 / 45，`95.56%`
- 离线正常Gross：`+$2,186.50`
- Actual正常Gross：`+$2,036.50`
- Callback/成交路径储备：`-$150.00`
- Quarantine：2笔；离线预测Gross `-$120.00`被替换为Actual执行Gross `$0`，两笔手续费`-$7.20`只计一次
- 离线保守Net：`+$1,867.30`
- Actual账户Net：`+$1,867.30`
- 差额：`$0.00`

角色不一致的2笔中，只有`20260102-1415...OC`产生重大金额差：离线普通Bar路径判TP `+$85.50`，Actual在同时间戳止损`-$57.00`，差`$142.50`。`20260102-1655...BreakawayFvg`为离线BE、Actual SL标签，但双方Gross均为`$0`。其余成交价细差合计`$7.50`，总储备为`$150.00`。

普通Calibration Bar和MarketTurn没有覆盖订单回调内的全部触发tick，因此上述`$150`不能被描述为离线引擎已经精确预测；它是开发集上量化出的执行缺口储备。盲验必须同时报告原始预测、储备后保守值和Actual，禁止只展示储备后的对齐值。

## 下一步合同

1. 不改Actual DLL、不编译、不清日志。
2. 冻结当前连接器与三项测试；开发集不再用于修改通过门槛。
3. 下一轮只验证未参与本次修模的`2026-02-11`。
4. 盲验至少要求：ENTRY_SEND身份不低于90%，逐日笔数误差不超过1，Secondary数量与关键Lane链一致，退出角色不低于95%，保守Net误差不超过`$150`且不超过15%。
5. 盲验失败则继续修连接器；通过后才恢复盈利研究，不直接进入全量Replay。

## 盲验后补充

`2026-02-11`盲验前发现Short `ZoneBirthResearch`的研究静态退出`F1.5_T12`与Actual `2.5R Base + 4R Runner`分拆OCO并非同一生命周期。连接器已按Actual分拆重放ZoneBirth后重新验证：三日开发集原Gate保持通过，单日盲验达到12/12身份、100%退出角色，原始Net误差`$23.25 / 4.47%`。最终裁决见`v217_dual_slot_connector_blind_20260211_review.md`。
