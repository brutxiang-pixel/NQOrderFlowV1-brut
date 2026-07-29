# v3.0 阶段 1B-2：微观结构字段语义审计合同

## 目的与边界

本轮不做策略收益研究、不推断盘口语义，也不改变任何订单或风控路径。目标是让 Historical Replay 的逐笔成交与 DOM 深度分别取得可审计的数据合同。

`OPF_RESEARCH_2.28 / ACTUAL_EXEC_2.47` 仅新增 Data Only 内存聚合和有界样本：每个 `Bar × Source × DataType` 最多一行原始样本，停止时批量写盘。Actual orders 始终强制关闭。

## 预注册日期

开发审计：`2026-01-16,2026-01-27,2026-03-03`

冻结验证：`2026-02-24,2026-04-10,2026-05-12,2026-06-17,2026-07-07`

每个完整 Snapshot 覆盖亚、欧、美时段；日期覆盖冬夏、不同波动及独立 Replay 数据批次。运行中不得根据前三日结果改字段、过滤规则或验证日期。

## 必需输出

- `microstructure_audit_bars.csv`：全局/源内到达序号、回调时间、数量、原始方向和数据类型汇总、`Price/OriginPrice` 的独立范围及差异/非正计数。
- `microstructure_audit_samples.csv`：有界字段样本，用于解释每种 `DataType` 的价格和方向语义；不保存无界逐笔流。
- 无 Actual 订单、`MICROSTRUCTURE_AUDIT_SUMMARY sources=6 depthSnapshotErrors=0`。

## 分层晋级规则

### A. 逐笔价格/数量

1. 八日均有规范 `OnNewTrade` 流；批量回调只证明重复性，禁止相加。
2. `OnNewTrade` 总量与 M5 Volume 的逐日绝对偏差不超过 1%。
3. 源内 ArrivalSequence 连续，价格 tick 合法；事件时间逆序单独报告，不以它重排到达序列。

### B. 主动方向

1. 原始 `Direction` 标签必须可解释，并在每个验证日覆盖至少 95% 的成交量；或
2. 后续单独实现“成交前已到达 Bid/Ask”的 quote-rule 审计，推断覆盖至少 90%，Unknown 必须显式保留。

未满足时，逐笔价格/数量特征仍可继续；主动 Delta、主动不平衡和 Footprint 方向特征不得进入阶段 2。

### C. DOM 静态状态

1. 先按 `DataType` 证明 `Price/OriginPrice` 中哪一个是深度价；不得猜测。
2. 规范 `MarketDepthChanged` 源经预注册过滤后，所有保留记录必须 tick 合法，且未分类异常不超过 0.1%。
3. 以到达序列维护的盘口状态，在至少 99% 的候选决策点同时具备有效 Bid/Ask，并能与独立 BestBid/Ask 交叉核验。

通过后只允许静态前 N 档不平衡等状态特征；不允许撤补单、冰山或吸收推断。

### D. DOM 动态事件

只有在 `DataType` 能明确区分新增、撤单和成交，且增量状态跨事件连续时，才另行立项。否则永久不把 DOM 变化解释为撤补单行为。

## 裁决原则

- ArrivalSequence 是 Replay 下“策略当时可见”的唯一排序锚；禁止按事后时间戳重排。
- 通过 A/B/C 的任一层只授权对应层的数据研究，不自动授权交易规则变更。
- 完成八日审计后先产出数据合同，再决定阶段 2 的最小采集集。
