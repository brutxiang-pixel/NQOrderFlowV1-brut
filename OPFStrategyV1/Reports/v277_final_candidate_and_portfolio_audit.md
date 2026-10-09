# v2.77 最终候选质量与组合边际贡献审计

日期：2026-08-06  
状态：完成离线审计；没有修改策略、DLL、ATAS 配置或活动日志。

## 审计合同与边界

本审计严格执行已冻结顺序：先完成全路径普查，再做候选质量与组合边际贡献审计；在此之前不开发新路径、不增加新入场门禁，也不将离线结果当作实盘资格。

证据分为两层，不能混用：

1. **80 日候选质量层**：89 个 `research_outcomes` 快照经 `SignalID + ResearchPath + EntryTime + Side` 去重后，有 22,756 条路径级提案、32 个路径标签、54 个路径×方向组合、10,645 个物理机会。全部为 `ResearchOHLC`、`WouldTradeLive=false`、`ActualVerified=false`，仅用于检查候选形态与重叠，不能当作 Actual PnL。
2. **141 日 StaticPlan 组合层**：34,771 个候选均有 StaticPlan 出场标签；在双槽、15 笔/日、-$250 日损、-$500 周 Long 门和手续费约束下重建组合。它是反事实离线组合证据，不是 Replay/Smoke/实盘证据。

StaticPlan 覆盖 2026-01 至 2026-07 的 24 个研究标签。80 日普查中以下 8 个标签未被该带覆盖，故本轮不能对它们宣称组合边际结论：`BreakawayRetest12Research`、`BreakawayRetest18Research`、`HtfSweepReclaimLong`、`HtfSweepReclaimShort`、`HtfTrendContinuationLong`、`HtfTrendContinuationShort`、`SignificantZoneFirstTouchLong`、`SignificantZoneFirstTouchShort`。

## 已验证的组合锚点

复刻既有组合脚本的结果完全一致：

| 视图 | 成交数 | 净值 | PF | 最差周 |
|---|---:|---:|---:|---:|
| Strict | 1,148 | +$1,597.77 | 1.0203 | -$2,857.98 |
| LowerBound | 1,166 | +$1,943.49 | 1.0253 | -$2,857.98 |

因此后续的“移除某家族/方向后重新排队”的差值，确实衡量其在这一固定组合里的边际贡献，而不是把各路径收益简单相加。

## 预注册的稳定性筛选尺子

为避免看到结果后改口径，家族×方向必须同时满足以下五项，才仅可进入下一步的**半自动观察设计**：

1. Strict 和 LowerBound 的总边际贡献均为正；
2. 七个月中至少四个月在两个视图的贡献都不为负；
3. 任一视图中，最大正向月份不超过该视图所有正向月贡献的 50%；
4. Strict 组合中该方向实际被接受的候选不少于 20 笔；
5. 纳入该方向不使任一视图的最差周恶化超过 $250。

这是一道“值得开始人工观察”的离线筛选门，不是 Smoke、Actual 或自动下单的放行门。

## 家族级结果

| 家族 | Strict 边际 | LowerBound 边际 | 联合非负月数 | 结论 |
|---|---:|---:|---:|---|
| FailureReverse Invalidated | +$1,691.79 | +$4,420.95 | 4/7 | 严格视图盈利过度集中，未过门 |
| UnknownRegimeZoneTouch | +$907.71 | +$678.81 | 4/7 | 严格视图盈利过度集中，未过门 |
| StructureConfirmShadow | +$70.74 | +$70.74 | 6/7 | 仅 5 笔被组合接受，样本不足 |
| ObservationConfirm | -$6,280.86 | -$7,179.87 | 2/7 | 两视图均为负，淘汰出候选池 |
| FailureReverse RetestFailed | -$5,475.90 | -$5,259.69 | 2/7 | 两视图均为负，淘汰出候选池 |
| ZoneBirthResearch | -$4,349.25 | -$2,257.50 | 3/7 | 两视图均为负，淘汰出候选池 |
| BreakawayFvg | -$538.41 | -$342.51 | 5/7 | 两视图为负且被接受样本仅 15 笔 |
| ShadowCandidate | -$1,544.49 | -$955.50 | 5/7 | 两视图为负且样本不足 |

其余家族在当前排序与组合约束下边际为零或接受样本不足；零并不等于信号无效，只表示它们在该固定组合中没有额外、可实现的贡献。

## 方向级裁决

将 Long/Short 拆开后，只有一项通过上述五项离线稳定性门：

| 候选 | Strict 被接受笔数 | Strict 边际 | LowerBound 边际 | 两视图联合非负月数 | 结论 |
|---|---:|---:|---:|---:|---|
| `UnknownRegimeZoneTouch Long` | 25 | +$1,660.05 | +$1,601.01 | 5/7 | 通过离线观察筛选 |
| `FailureReverseInvalidated Long` | 53 | +$2,087.49 | -$300.81 | 3/7 | 保守视图为负，不通过 |
| `FailureReverseInvalidated Short` | 148 | -$5,579.67 | -$1,461.27 | 1/7 | 不通过 |
| `UnknownRegimeZoneTouch Short` | 46 | -$1,949.61 | -$1,692.60 | 2/7 | 不通过 |
| `ObservationConfirm Long/Short` | 108 / 183 | 均显著为负 | 均显著为负 | 低 | 不通过 |
| `FailureReverseRetestFailed Long/Short` | 18 / 45 | 均为负 | 均为负 | 低 | 不通过 |

`UnknownRegimeZoneTouch Long` 的月度边际贡献（Strict / LowerBound）分别为：

| 月份 | Strict | LowerBound |
|---|---:|---:|
| 2026-01 | +$685.68 | +$945.18 |
| 2026-02 | $0.00 | $0.00 |
| 2026-03 | +$160.62 | +$124.02 |
| 2026-04 | -$325.35 | -$531.75 |
| 2026-05 | +$510.84 | +$636.84 |
| 2026-06 | +$695.94 | +$753.00 |
| 2026-07 | -$67.68 | -$326.28 |

它不是“每月均正”，但满足已定义的 5/7 联合非负、双视图正贡献、无单月主导、最差周不恶化及 Strict 接受样本不少于 20 的观察筛选标准。

## 候选质量层的交叉解释

80 日 `ResearchOHLC` 中，`UnknownRegimeZoneTouch` 的 Long/Short 都有较高 1.5R 触达率（Long 70.58%，Short 72.44%），但也都有约 60% 的“1R 前曾触及止损”记录。这种 OHLC 代理歧义恰好说明：它不能单独替代订单时序、真实成交或组合占用审计。

因此本报告不把“Long 通过 StaticPlan 筛选”解释为已证明 Alpha，更不据此恢复或放开所有路径。它只回答本轮合同的问题：在已覆盖的全候选组合里，是否有一条值得交给人工观察的候选来源。答案是：**有且仅有 `UnknownRegimeZoneTouch Long`。**

## 对原最终建议的复核结论

此前把“研究 OHLC 不是 Actual”推成“无法继续组合边际审计”，以及把执行白名单的少数路径误称为全 32 路径结果，都是错误的。这两点会错误地得出“没有任何候选可进入下一步”的结论，现已撤回。

正确的下一步仍完全遵循冻结建议：

1. 停止新路径与新入场门禁开发；
2. 不改自动策略、不扩大自动下单；
3. 仅为 `UnknownRegimeZoneTouch Long` 设计“提示 + 人工附带硬保护单”的**观察**规格；
4. 提示系统不接管人工仓位，不允许策略替人工任意改 SL，不实现按时段自动切换；
5. 观察证据必须与此离线审计分开记录，积累后才评估人工裁决是否有增量价值。

观察配置：`OPFStrategyV1_v277_unknown_long_manual_alert.json` 固定为 `EnableActualOrders=false` 与 `ManualAlertEnabled=true`。提示中的 Entry/SL/TP 是人工下单时必须一并附带的硬保护单参数；策略不会识别、管理或修改该人工订单。

完整可复算数据：`v277_staticplan_audit/family_stability_summary.csv`、`family_side_stability_summary.csv`、两份月度贡献 CSV 和两份 80 日候选质量 CSV。
