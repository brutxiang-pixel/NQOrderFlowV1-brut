# v3.0 阶段 3A：G2 微观确认首轮预注册合同

日期：2026-07-28  
输入：`v230_outcome_label_validation/outcome_labels.csv` 中的 `NormalOutcome + IsSecondary=true`。

## 边界

当前同冻结标签仅覆盖 2026 年 1–7 月，且正常 G2 仅 37 条（H1 34 条、7 月 3 条）。因此本轮是**局部淘汰实验**，不是 V3.0 阶段 3 四组合的最终裁决：不评估 Short-only 或三态 Regime，不产生 DLL/Smoke 候选，也不声称 Q4 稳健性。

训练集为 H1 的 2026-01 至 2026-06；7 月只作短样本压力检查。任何 `EntryAborted`、`ExcludedAbnormal`、未连接和预热未完成行均永久排除。

## 固定候选

只测试以下四个单特征条件；不搜索分位数、不组合特征、不按路径或日期调参。

| ID | G2 保留条件 | 决策时可得性 |
|---|---|---|
| `Aggressor60Aligned` | `Side × (BuyVolume60 - SellVolume60) >= 0` | 候选冻结时的 60 秒原始 Buy/Sell 量 |
| `ZoneTouchDeltaAligned` | `Side × ZoneTouchDelta >= 0` | 最近已观测 ZoneTouch |
| `PocMigrationAligned` | `Side × (PocMigration1 + PocMigration2) >= 0` | 前三根已收盘 M5 POC |
| `ZoneOccupancyMedian` | `ZoneOccupiedLevelRatio >=` 当轮训练月中位数 | 候选前已观测的 Zone 价格层覆盖率 |

`Side` 为 Long=+1、Short=-1。中位数仅在每个 H1 留出月之外的训练月计算，留出月只评分。

## 固定输出与淘汰纪律

每个候选报告 H1 按月留一汇总、H1 每月、7 月冻结阈值压力检查：基线/保留/剔除笔数、Net、笔均、保留率和最差月。

本轮只作淘汰：若候选在 H1 留一汇总不能使“剔除部分”的净值为负，或在 7 月使保留结果低于基线，则淘汰。即使未淘汰，也因没有 Q4、G2 样本不足 60 条和未完成容量替代链审计而不得晋级。
