# v2.30 阶段 2 Footprint Data Only 两日定点回归

日期：2026-07-27  
结论：**采集功能、候选连接与零订单 Gate 通过；尚未进入 G2 alpha 研究。**

## 范围

预注册日期：`2026-01-16,2026-07-07`。两日均为 Footprint Data Only，Actual orders 从启动即强制关闭。

## 验收结果

| 日期 | Snapshot | 特征行 | 完整预热 | Risk 连接缺失 | 订单文件 | 结论 |
|---|---:|---:|---:|---:|---:|---|
| 2026-01-16 | OPF-20260727-142502 | 233 | 233 | 0 | 0 | 通过 |
| 2026-07-07 | OPF-20260727-142707 | 285 | 285 | 0 | 0 | 通过 |

共同检查结果：

- ConfigSnapshot 均为 `OPF_RESEARCH_2.30 / ACTUAL_EXEC_2.47`；research.log 均含强制 `actualOrders=false` 标记。
- 518/518 行的 `TickSequenceBoundary>0`、ReferenceTickTime 非空、Lane=`ResearchCandidate`；没有重复的 `SignalID + Time + Bar + Path` 键。
- 518/518 行已完成 60 秒和 15 分钟预热；所有完整预热行的三个已收盘 M5 POC 均有效。
- 30 秒累计成交量未超过对应 60 秒累计成交量；所有 Buy/Sell/Unknown 成交量均非负。
- Footprint 行与同 Snapshot 的 `risk_evaluations.csv` 按 `SignalID + Time + Bar + ResearchPath` 连接为 518/518。说明特征是在既有研究候选已形成后冻结，且可回溯。
- execution_trades、live_account_pnl 和 legacy trades 文件均为 0；execution_events 仅有 Globex rollover 安全记录，不含订单发送、成交、保护或隔离事件。

## 观察，不作 alpha 结论

- 1 月：Aligned 192、Divergent 28、FlatOrUnknown 13。
- 7 月：Aligned 260、Divergent 10、FlatOrUnknown 11、NoObservedTouch 4。

这些只是字段分布与缺失语义审计，不能解释成收益信号，更不能直接形成入场/G2 门禁。

## G2 标签边界

Data Only 的真实主槽保护和第二槽状态必然不存在。因此 `ResearchCandidate` 不是 G2 标签，也不应与任意旧版本结果直接硬拼。本次通过只证明：后续可将同键的 Footprint 行与**同一冻结 Actual 版本**的执行决策/TradeID 证据连接。此前不同策略版本、不同配置或已知生命周期失败的历史档案仅可诊断，不能作为 G2 收益标签。

## 下一步

保留当前活动日志，先归档这两日通过证据；随后扩大为分层的 24 日 Data Only 收集。采集样本需要覆盖 H1 与 7 月，但仍不编译 DLL、不变更策略、不进入 Smoke。24 日完成后先做键连接与缺失率审计，再开始严格的 G2 微观确认离线研究。

可复现校验：`Scripts/Validate-OPFV230FootprintDataOnly.py`，本轮输出：`Reports/v230_footprint_data_only_validation/summary.csv`。
