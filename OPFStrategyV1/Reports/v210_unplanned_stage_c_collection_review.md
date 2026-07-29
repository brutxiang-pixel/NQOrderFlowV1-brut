# v2.10提前验证日采集复核（2026-07-25）

## 结论

本轮实际运行日期为`2026-03-10,2026-05-12`，不是阶段A预注册的`2026-01-06,2026-01-28`。因此本轮不能裁决阶段A，也不得用于连接器开发或参数调整。

修正校验器的采集边界后，v2.10采集链健康检查通过：444个应采候选与444条校准记录一一对应，Decision、Reason、Execute覆盖和Execute TradeID均为100%，重复、缺失、额外及无Decision记录均为0；16笔Actual交易对应16次ENTRY_SEND和16次PROTECTION_CLEANUP_DONE，危险生命周期事件为0。

## 校验器边界修正

- `PathDisabled`等路径门禁位于`StartDecisionTapeCalibrationTracking`之后，按v2.10设计必须进入校准表，不能作为采集前排除项。
- `AggressiveExpansionWait1`的Expired、FollowThroughFailed及无效ConfirmBar Stop在正式执行边界前已被拒绝，不会进入`TrySubmitReplayExecution`，应作为采集前排除项。
- 该修正只涉及`Validate-OPFV210DecisionTapeCalibration.py`，未修改策略DLL，无需重新编译或因编译清日志。

## 本轮证据

| 日期 | 校准候选 | Actual交易 | Gross | 异常交易 |
|---|---:|---:|---:|---:|
| 2026-03-10 | 223 | 6 | -$98.00 | 0 |
| 2026-05-12 | 221 | 10 | +$304.00 | 0 |

本轮仅证明采集模式在这两日完整且没有订单生命周期缺陷，不证明模拟器已经准确，也不允许进入盈利研究。

证据已归档至`opf_v2.10_unplanned_stage_c_collection_gate_passed_not_oos_2snapshots_20260725`：47个Manifest前证据文件、4,963,154字节，HashMismatch=0，Manifest SHA256=`10B0DF2A6382C8A515B2F8239BAD59F01628BCACE1F32EBF78F7DAFC90AF817A`。归档后活动日志已清空。

## 下一步

仍按冻结合同运行阶段A：`2026-01-06,2026-01-28`。阶段A必须覆盖1月6日旧Decision Tape漏失候选与同柱Stop，以及1月28日ActiveTrade清理抖动。阶段A通过后，连接器只能使用这两日开发。

由于原阶段C日期已提前运行并被查看，后续连接器冻结验证时不得把它们作为严格盲测证据；正式进入阶段C前另行预注册替代验证日。
