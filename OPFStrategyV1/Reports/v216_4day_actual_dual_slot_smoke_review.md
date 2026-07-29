# v2.16 四日 Actual 双槽配对回归复核

日期：2026-07-26

## 裁决

v2.16 未通过完整 Smoke。Globex 终态、保护清理和报价风险前置均通过；主槽保护后第二槽重评仍未生效，根因是等待候选过期边界 off-by-one，而不是 G2、风险合同或双槽方向规则失败。

## 样本与账户结果

- Snapshot：`OPF-20260725-155502`、`OPF-20260725-155714`、`OPF-20260725-155944`、`OPF-20260725-160153`
- 日期：`2026-01-02,2026-02-11,2026-04-24,2026-05-05`
- 版本：`OPF_RESEARCH_2.16 / ACTUAL_EXEC_2.41`
- 活动日志共92个文件，已原样归档到`v216_4day_actual_dual_slot_smoke_evidence/`。
- 正常交易53笔，Gross `+$1,790.00`，Net `+$1,599.20`；另有2笔Quarantine，仅手续费`-$7.20`；账户总Net `+$1,592.00`。
- `ENTRY_SEND=59`：53笔正常完成、2笔Quarantine、4笔在报价风险前置阶段中止。第二槽发送11笔，其中10笔形成正常账户交易。

## 已通过项

- `ACTIVE_ON_STOP=0`
- `GLOBEX_CLOSEOUT_UNCONFIRMED=0`
- `TRADE_ORDER_MISMATCH=0`
- `PROTECTION_LOST_BEFORE_EXIT=0`
- `PROTECTION_CLEANUP_PENDING=55`且`PROTECTION_CLEANUP_DONE=55`
- 报价前置阻断4笔：`ENTRY_QUOTE_RISK_EXCEEDED_V216=3`、`ENTRY_QUOTE_STRICT_RISK_BAND_EXCLUDED_V216=1`
- 成交后风险兜底未再触发：`ENTRY_FILLED_RISK_EXCEEDED=0`、`ENTRY_FILLED_STRICT_RISK_BAND_EXCLUDED_V208=0`、`SECONDARY_ACTUAL_RISK_CAP_EXCEEDED_V215=0`

## 未通过根因

- `SECONDARY_WAITING_FOR_PROTECTION_V216=10`
- `SECONDARY_PROTECTION_READY_RETRY_V216=0`
- `SECONDARY_PROTECTION_RETRY_REJECTED_V216=10`
- 10笔全部以`PendingSecondaryExpiredV216`拒绝；其中9笔在保护回调到达时恰好为`currentBar = entryBar + 1`。
- 异步下单预检本来使用`barLag = lastSeenBar - (createdBar + 1)`，明确允许`entryBar + 1`。等待候选却使用`lastSeenBar > entryBar`，提前一根Bar判定过期，两个边界互相矛盾。
- 剩余1笔在`closedBar=entryBar+1`时仍未获得保护，属于真实超时，不应被强行恢复。

## 修复合同

只把保护回调重评的合法窗口对齐到现有异步下单边界：激活时仅在`lastSeenBar > entryBar + 1`拒绝；关闭Bar清理改为严格大于entry Bar。不得放宽方向、G2、单笔风险、严格风险禁带、`$300`并发风险、18笔或日损`$450`合同。
