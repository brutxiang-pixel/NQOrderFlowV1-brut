# V4.0 阶段 2：Zone Behavioral Ledger 四日技术验收

日期：2026-07-29  
合同：`v400_zone_behavior_ledger_contract.md`  
结论：**技术链路通过；未作 Alpha 或收益结论。**

## 运行身份与零订单

四个有效 Snapshot 均为：

`OPF_RESEARCH_2.32 / ACTUAL_EXEC_2.49 / V400_ZONE_BEHAVIOR_LEDGER_DATA_ONLY_PILOT / ZoneBehaviorLedgerDataOnly`

每个 `research.log` 均明确记录 `actualOrders=false` 与 `dom=false`。四日的 `execution_trades.csv` 与 `live_account_pnl.csv` 均不存在；仅有各两条零数量的 `GLOBEX_TRADING_DAY_ROLLOVER` 安全事件，不是订单或账户成交。

## Snapshot 与完整性

| 交易日（北京时间） | Snapshot | Birth | Touch | 终态 | Zone×M5 Bar | PriceLevel | 生命周期/触及/外键/边界异常 |
|---|---|---:|---:|---:|---:|---:|---:|
| 2026-01-16 | OPF-20260729-013150 | 33 | 61 | 33 | 1,524 | 7,661 | 0 / 0 / 0 / 0 |
| 2026-03-16 | OPF-20260729-013356 | 43 | 67 | 43 | 1,537 | 11,699 | 0 / 0 / 0 / 0 |
| 2026-05-04 | OPF-20260729-013552 | 33 | 57 | 33 | 913 | 9,475 | 0 / 0 / 0 / 0 |
| 2026-07-07 | OPF-20260729-013749 | 40 | 49 | 40 | 1,721 | 11,637 | 0 / 0 / 0 / 0 |
| 合计 | 4 | 149 | 234 | 149 | 5,695 | 40,472 | 0 / 0 / 0 / 0 |

终态构成：`Invalidated=125`、`SnapshotEnd=24`、`Expired=0`。每个 Birth 恰有一个终态；终态后没有任何 Zone Bar。

## 触及与逐笔边界检查

1. 每个 Zone 的 `TouchOrdinal` 从 1 连续递增，无跳号。
2. 逐 Zone 从 `Touching=True` 的 M5 Bar 重建连续 episode，episode 数与 Touch 事件数逐一相等；每个事件 Bar 与 episode 首 Bar 一致，零重复。
3. 每个 PriceLevel 的 EventID 都对应 Birth 或 Touch；不存在终态价格层或孤儿外键。
4. 所有 Bar 均有逐笔数据；`TickDataAvailable=True` 的 5,695 行均有正的 Bar 内 `TickSequenceBoundary`。40,472 条价格层也均有正边界。没有把后续 Bar 的成交写回当前 Bar。

## 排除项

`OPF-20260729-011840` 仅为启动后未回放的空 Snapshot（ConfigSnapshot、research.log、signals header 三个文件），没有任何 Zone Ledger 文件或 Bar；它不属于四日样本，归档时明确排除。

## 阶段裁决与下一步

四日 Gate 的全部技术条款满足，可以复用 2026-01-16 与 2026-07-07 两个已采集 Snapshot，并只补跑预注册 24 日分层清单中的其余 22 日。该扩展仍仅为 Data Only；完成前不进入特征解释、筛选、Smoke 或 Actual Replay。
