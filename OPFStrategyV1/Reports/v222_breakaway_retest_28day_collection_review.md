# v2.22 BreakawayRetest 28日影子采集验收

## 样本与完整性

- 28 个 Snapshot，全部为 `OPF_RESEARCH_2.22`，覆盖冻结的 24 个 H1 日期和 4 个随机 7 月日期。
- 新增路径在 `execution_decisions.csv`、`execution_trades.csv`、`execution_events.csv` 中均为 0 行，证明 `BreakawayRetest12Research` 与 `BreakawayRetest18Research` 没有进入 Actual 执行链。
- 原 6-Bar 路径的日志、信号身份和执行资格保持原状。

## 漏斗结果

| 窗口 | 起始 Breakaway | 成功 Retest | 区块失效 | 质量拒绝 | 到期 |
|---|---:|---:|---:|---:|---:|
| 6 Bar | 73 | 1 | 16 | 34 | 22 |
| 12 Bar | 73 | 1 | 20 | 36 | 16 |
| 18 Bar | 73 | 1 | 22 | 38 | 12 |

唯一成功候选为 `20260528-1020-BEAR-BRT-001204-Z001204`，Short、质量分 90；三个窗口都是同一时刻的同一候选，不是延长窗口带来的新增机会。研究用 `Fixed1_5R` 生命周期结果为 `+1.5R / +$25.50`（单研究合约，未扣手续费）。原 6-Bar 的 Actual 候选因 `EntryBarTargetTouched` 跳过，没有形成实际订单。

6-Bar 的 22 个到期候选在延长窗口后的去向：12 个在 18-Bar 仍到期、6 个转为区块失效、4 个转为确认质量拒绝；新增成功为 0。也就是说，额外等待没有释放可交易的 Short Retest，而是暴露了更晚的失效或低质量确认。

## 合同判定

未通过，且不进入组合模拟器：

- 12/18-Bar 新增成功候选为 0；
- Short Retest 总数仍仅 1，远低于预注册的至少 20 笔；
- 唯一结果不足以评价跨期稳定性或组合替代链。

结论：`BreakawayRetest` 的主要瓶颈不是等待窗口。v2.22 保留为“Research-only 采集功能通过、策略候选否决”；不改 Actual 规则，不安排 Smoke 或大规模 Replay。

## 建议的下一研究线

不再扩展 Retest 窗口。先离线分析现有 `BreakawayFvg Short` 候选的入场前可知特征、质量分、风险、时段和后续价格路径，定位其静态正期望是否能经组合约束保留；仅在离线结果显著超过 v2.21 基线后，再决定是否需要新的 Research-only 采集。
