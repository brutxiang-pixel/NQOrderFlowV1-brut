# v3.0 阶段 1B-2：八日字段语义审计结果

## 通过项

- 八个 Snapshot 均为 `OPF_RESEARCH_2.28 / ACTUAL_EXEC_2.47`，均有 6 个 Source、DepthSnapshot error=0、Actual 订单=0。
- 规范逐笔源 `OnNewTrade` 八日均有 276 根 Bar，源内 ArrivalSequence 连续、事件时间无逆序。
- 相对 Rich M5 Volume 的逐日差异为 `-0.0389%~-0.1630%`，全部低于 1% 门槛。
- 原始 `Direction` 只出现 `Buy/Sell`，八日成交量覆盖率均为 100%。因此逐笔价格/数量（A）与原始主动方向（B）通过数据可用性合同。

## DOM 已确认事实与未决项

- `MarketDepthChanged`/`MarketDepthsChanged` 是重复双回调，后续只能选择前者为规范源。
- 深度和 BestBid/Ask 的 `DataType` 为 `Bid/Ask`；深度 `Direction` 与侧别稳定对应；`OriginPrice` 在深度、BestBid/Ask 和 Snapshot 中均为 0，不能用作盘口价格。
- 有界样本中的 `Price` 为正且处于当日 MNQ 区间，故 `Price` 是唯一待验证的深度价格候选。
- 但完整聚合仍出现 `0.25`、`1`、数百万等极端 `Price`。当前只知道至少存在异常，无法知道事件占比及其相对当时 BBO 的方向，故静态 DOM（C）未通过，动态撤补单（D）更不允许进入。

## 证据

`opf_v228_1b2_tick_direction_passed_dom_semantic_partial_8snapshots_20260727`

- 144 文件，26,894,706 bytes，HashMismatch=0。
- Manifest SHA256：`C13D2B6FC7CFFE126228A4ECF562B932DE1AA34D387DF013BC15236B15EC5412`。

## 1B-3 最小补充

`OPF_RESEARCH_2.29` 在不改变任何策略逻辑的前提下，以“此前到达”的 `OnBestBidAskChanged` 为参照，按深度事件记录：有无参考、是否同侧、是否落在 ±1000 点审计带、异常数量，并保留每 Bar 首个异常原始样本。它用于量化异常是否可预注册过滤；同八日重跑后才裁决静态 DOM。
