# v2.24 二十日分层 Replay 证据重组复核

日期：2026-07-27  
合同：`v223_20day_stratified_replay_contract.md`

## 证据组成

- 18 个未受 Historical Split Virtual TP 缺陷影响的 v2.23 Snapshot，复用失败归档 `opf_v2.23_20day_stratified_replay_lifecycle_failed_20new_snapshots_20260727`。
- `2026-01-08`、`2026-01-12` 用 v2.24 定点回归替换，证据归档 `opf_v2.24_historical_split_virtual_tp_lifecycle_regression_passed_2snapshots_20260727`。
- 此为固定合同下的证据重组，不是重新运行 20 日，也不是纯 v2.24 的 20 日 Replay。v2.24 唯一行为变化是 Historical Split Virtual TP 成交后的同腿 SL 撤销/OCO 对等；入场候选、Rich 门禁、仓位、日损、周门和退出政策未变。

## 生命周期与连接

- 20 Snapshot：18 个 `OPF_RESEARCH_2.23` + 2 个 `OPF_RESEARCH_2.24`。
- 169 个 `ENTRY_SEND`：159 个正常 Actual Trade，10 个 `ENTRY_SUBMISSION_ABORTED_V178` 报价预检中止；后者未注册订单、不占仓位或交易额度。
- 159/159 Actual Trade 与 Execute 决策按 `TradeID + SignalID + ResearchPath` 精确对应。
- 159 次 `PROTECTION_CLEANUP_DONE`；`EXIT_FILL_EXCEEDS_REMAINING_V220`、`EXEC_ORPHAN_POSITION_BLOCKED`、`ExitOverfill`、`UnmanagedAccountState`、取消失败、订单状态失败、异常/隔离/安全平仓均为 0。
- v2.24 两日记录 4 次 `HISTORICAL_SPLIT_TARGET_LEG_STOP_CANCEL_SENT_V224`。原故障交易 `20260108-1800-Short-1332` 与 `20260112-0925-Short-1229` 均已覆盖；Base TP 后无 Base SL 成交。
- Rich 门禁记录：`SKIP_PRIMARY_OC_REGIME_BARS_V223=5`，`SKIP_PRIMARY_BREAKAWAY_RICH_V223=2`。

## 经济结果

| 口径 | 笔数 | Gross | 手续费 | Net | PF |
|---|---:|---:|---:|---:|---:|
| 冻结离线候选 | 141 | +$5,005.01 | $507.60 | +$4,497.41 | 1.8999 |
| 重组 Actual | 159 | +$5,839.50 | $572.40 | +$5,267.10 | 1.8985 |
| Actual - 离线 | +18 | +$834.49 | +$64.80 | +$769.69 | -0.0014 |

替换前的两个失效 Snapshot 表面结果为 21 笔/Gross `+$552.01`/Net `+$476.41`；替换后为 24 笔/Gross `+$373.50`/Net `+$287.10`。因此修复使重组 20 日较原表面汇总少 3 笔、Gross 少 `$178.51`、Net 少 `$189.31`，但消除了不可接受的超额退出与孤儿仓位。

## 裁决

1. Historical Split Virtual TP 生命周期修复通过；20 日不存在已知订单安全缺陷。
2. Actual 与冻结离线候选同为正，PF 几乎相同，经济方向和量级成立。
3. Actual 159 笔高于离线 141 笔 12.8%。本合同没有预注册精确笔数阈值，但这已足以拒绝将 `$5,267.10` 视为模拟器可精确承诺的金额。
4. 结论为：**功能/生命周期通过，候选经济方向通过，精确模拟金额未晋级；不触发 H1 全量 Replay。**

下一步应只在离线侧分析这 18 笔身份差及其占用链，确认候选可重复性后再决定下一轮有限 Smoke/Replay；不得以本轮正收益直接扩大样本或放行实盘。
