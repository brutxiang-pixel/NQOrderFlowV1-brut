# v3.0 阶段 1B：v2.26 三日微观结构复测审计

## 结论

v2.26 的 CSV 持久化、版本一致性和零订单门全部通过；Historical Replay 的逐笔价格与数量可用于后续研究采集。严格 DOM 在当前三日的字段级初筛未通过，但不能据此永久暂停：必须先审计 `OriginPrice`、`DataType` 与回调到达序列的语义。逐笔主动方向尚未通过，因为 `IsBid/IsAsk` 在三日逐笔流中均为空；后续采集必须同时审计原始 `Direction`，不做隐式推断。

## 运行完整性

| Replay 日期 | Snapshot | Schema / Actual | Micro CSV | Actual 订单 | Source / Snapshot 错误 |
|---|---|---|---|---|---|
| 2026-01-16 | OPF-20260727-105803 | 2.26 / 2.47 | 1,655 行 | 0 | 6 / 0 |
| 2026-01-27 | OPF-20260727-110005 | 2.26 / 2.47 | 1,655 行 | 0 | 6 / 0 |
| 2026-03-03 | OPF-20260727-110157 | 2.26 / 2.47 | 1,655 行 | 0 | 6 / 0 |

## 逐笔成交

- `OnNewTrade` 与 `OnNewTrades` 在 276 根 Bar 上事件数、数量、Bid/Ask/Unknown 聚合均完全一致。它们是同一流的双回调，后续只能选择一个规范源，绝不可相加。
- `OnNewTrade` 事件时间在每个 Bar 内完全非递减；逐笔数量与 Rich M5 `Volume` 的差异分别为 `-0.0565%`、`-0.1630%`、`-0.0515%`，价格范围均处于对应 MNQ 当日可交易区间。
- 但三日 `OnNewTrade` 的 `UnknownVolume / TotalVolume = 100%`，不能将当前 `IsBid/IsAsk` 当成主动买卖方向。

## DOM

- `MarketDepthChanged` 与 `MarketDepthsChanged` 同样是双回调，逐 Bar 聚合完全相同。
- Depth 事件时间逆序率为 `29.98%–31.82%`，DepthSnapshot 为 `40.06%–44.79%`；快照也只覆盖 275/276 根 Bar。
- Depth 流出现不可能的价格：最小 `0.25`、最大达 `6,186,788.25`；快照出现 `1` 或 `999999`，受影响 Bar 分别为 142、82、16。
- 因而当前字段使用方式不能为 DOM 队列、撤补单、盘口失衡等严格时间序列特征提供可信输入。它不影响已有 BestBid/BestAsk 执行安全门；v3.0 在 `OriginPrice/DataType` 语义审计完成前不作 DOM 历史收益结论。

## 证据与下一步

- 证据包：`opf_v226_microstructure_tick_available_dom_unreliable_direction_pending_3snapshots_20260727`，51 文件、7,324,660 bytes、HashMismatch=0、Manifest SHA256=`0AFCE3BC78C5ECB62169EA840FC944F0674F1EDB5EF357F1346A42AB032DABA1`。
- 原 v2.27 只新增 `DirectionSummary`，不足以完成 DOM 语义裁决，后续由 1B-2 版本合并记录 `Direction`、`DataType`、`Price/OriginPrice` 的汇总与有界样本；无策略、订单或风控改动。
- 只有扩展日期集下的原始方向、字段语义和回调序列均经审计后，才为逐笔 Footprint 或 DOM 特征定级。
