# V4.0 阶段 2：Zone Behavioral Ledger Data Only 合同

日期：2026-07-29。前提：`opf-v3.0-full-replay-baseline-20260729` 为不可变恢复点；本阶段不修改任何 Actual 交易、退出或风险规则。

## 目的与边界

验证能否以 ZoneID 为键，完整、无未来信息地记录 M5 FVG 从出生到终态的行为账本，为后续离线研究提供输入。它不是 Alpha 验证，不产生入场过滤、评分、TP/SL 或实盘候选。

- 运行模式唯一为 `ZoneBehaviorLedgerDataOnly`；启动时强制 `actualOrders=false`。
- 只使用已通过语义审计的 `OnNewTrade` 原始方向、价格、数量和已闭合 M5 OHLC。
- DOM、热图、Spoofing、盘口撤补、跨市场相关性均不采集、不推断。
- 不把主动成交解释成被动挂单、吸收或被套资金；这些仅可在离线阶段作为保守代理派生。

## 固定事件定义

| 事件 | 时点 | 定义 |
|---|---|---|
| `Birth` | Zone 创建的闭合 M5 | FVG 检测器首次生成的 ZoneID；保存当根价格层快照。 |
| `Touch` | 首根连续相交 M5 | 非出生 Bar 的 candle `[Low,High]` 与 Zone `[Low,High]` 相交，且上一根 M5 不相交。同一连续 episode 只记一次，`TouchOrdinal` 从 1 连续编号。 |
| `Invalidated` | 检测器失效时 | Bull：Close < ZoneLow；Bear：Close > ZoneHigh。 |
| `Expired` | 检测器容量淘汰时 | 历史活跃 Zone 超出检测器上限而被淘汰；不得冒充失效。 |
| `SnapshotEnd` | 策略停止时 | 仍活跃的 Zone 的唯一终态。 |

每个 Birth 必须恰有一个终态；终态之后不得再写 Bar、Touch 或价格层记录。

## 输出文件

均按 Snapshot 分文件、停止时批量落盘，均含标准 Context 六列。

1. `*_zone_behavior_events.csv`：EventID、ZoneID、事件、时间/Bar、TouchOrdinal、Zone 定义、TickSequenceBoundary、TickTime。
2. `*_zone_behavior_bars.csv`：每个活跃 Zone × 已闭合 M5；OHLC、是否触及、TouchOrdinal、当前/累计全市场与 Zone 内 Buy/Sell/Unknown/Delta/Volume、顺逆向 excursion、逐笔边界。
3. `*_zone_behavior_price_levels.csv`：仅 Birth 与 Touch 的 PriceLevel；EventID/ZoneID、价格、Buy/Sell/Unknown/Total Volume、逐笔边界。

`TickSequenceBoundary` 只包含事件/闭合 Bar 当时已观察到的成交；若该 Bar 无逐笔数据，显式记 `TickDataAvailable=false`，不回填未来数据。

## 四日技术验收（预注册）

日期固定为：`2026-01-16, 2026-03-16, 2026-05-04, 2026-07-07`，每个均运行完整北京时间 06:00 至次日 05:00。

全部通过才可进入 24 日分层采集：

1. ConfigSnapshot 为 `OPF_RESEARCH_2.32 / ACTUAL_EXEC_2.49 / V400_ZONE_BEHAVIOR_LEDGER_DATA_ONLY_PILOT`，`actualOrders=false`；账户订单文件为零。
2. 三个输出文件存在；每个 Birth 恰有一个终态；外键 EventID、ZoneID 完整。
3. TouchOrdinal 无跳号；连续相交 M5 不重复计 Touch；事件后无 Bar。
4. 价格层只关联 Birth 或 Touch；Bar 的逐笔边界不超过当时序列；缺失逐笔显式标注。
5. 无 Alpha、DOM 或实际订单副作用。通过仅表示数据链路可用，不表示存在交易优势。
