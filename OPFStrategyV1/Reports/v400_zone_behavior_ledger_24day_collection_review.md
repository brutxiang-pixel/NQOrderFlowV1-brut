# V4.0 阶段 2：Zone Behavioral Ledger 24 日分层采集验收

日期：2026-07-29  
合同：`v400_zone_behavior_ledger_contract.md`  
结论：**24 日数据集 Gate 通过；尚未作 Alpha、阈值、交易规则或收益结论。**

## 预注册范围

分层清单 `v400_zone_behavior_ledger_24day_collection_plan.csv` 的 24 个日期均恰有一个有效 Snapshot：其中 2026-01-16 与 2026-07-07 复用四日技术验收 Snapshot，另外 22 日为本轮新采集。2026-03-16 与 2026-05-04 仅为四日技术验收样本，不属于预注册 24 日研究集，永久不混入本集。

全部 24 个研究 Snapshot 都是：

`OPF_RESEARCH_2.32 / ACTUAL_EXEC_2.49 / V400_ZONE_BEHAVIOR_LEDGER_DATA_ONLY_PILOT / ZoneBehaviorLedgerDataOnly / actualOrders=false`

`execution_trades.csv` 与 `live_account_pnl.csv` 均为 0 文件，`ENTRY_SEND=0`。

## 数据集规模

| 项目 | 数量 |
|---|---:|
| 有效日期 / Snapshot | 24 / 24 |
| Zone Birth | 945 |
| 唯一终态 | 945 |
| Touch episode | 1,416 |
| Zone × 已闭合 M5 Bar | 31,992 |
| Birth / Touch PriceLevel | 262,388 |

每个 Birth 均有且仅有一个 `Invalidated`、`Expired` 或 `SnapshotEnd` 终态；终态之后无 Bar 写入。

## 数据完整性 Gate

| 检查 | 结果 |
|---|---:|
| 配置身份不一致 | 0 |
| 真实订单、账户成交、`ENTRY_SEND` | 0 / 0 / 0 |
| Birth 缺失或多终态 | 0 |
| TouchOrdinal 跳号 | 0 |
| 连续 M5 Touch episode 重复 / 事件 Bar 不匹配 | 0 / 0 |
| 终态后的 Zone Bar | 0 |
| PriceLevel 孤儿 EventID | 0 |
| Bar 逐笔边界无效 | 0 |
| PriceLevel 逐笔边界无效 | 0 |

验收期间最初报告的 62 项 `TouchOrdinal` “异常”经逐 Zone 回溯，全部是**零触及 Zone**：其 Touch Bar 和 Touch Event 均为 0；PowerShell 的空序列比较误将 `1..0` 解释为 `1,0`。修正验证器后为 0 异常。该问题只在临时验收脚本，不在 DLL、CSV 或数据集中；不需要重新编译、重跑或清理日志。

## 阶段裁决

Zone 生命周期、真实触及 episode、逐 Bar 主动成交代理、Birth/Touch 价格层与可验证的时间边界均已形成可复用 24 日分层数据集。下一阶段仅允许离线派生并预注册检验：

1. 出生后至首次 Touch 的成交燃料、价格推进与反向穿透代理；
2. 每次 Touch 后的价格响应、后续 Bar 的 MFE/MAE 与失效路径；
3. 跨月份留一的描述性稳定性。

禁止直接把代理写入评分、入场、退出或实盘；只有离线结论跨期成立并通过组合约束评估，才可讨论新的最小 Data Only 或分级 Replay。
