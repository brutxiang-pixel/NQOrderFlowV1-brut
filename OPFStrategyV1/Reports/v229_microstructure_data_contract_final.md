# v3.0 阶段 1B 最终数据合同

## 裁决

| 数据层 | 状态 | 允许范围 |
|---|---|---|
| 逐笔价格/数量 | 通过 | 阶段 2 的价格、数量、密度、POC 类特征 |
| 原始主动方向 | 通过 | 阶段 2 的主动 Delta、主动不平衡、价格-Delta 背离 |
| DOM 静态盘口 | 诊断限定 | 仅可在下一阶段重建并验证，不得直接产生 alpha 特征 |
| DOM 动态撤补单 | 不通过 | 禁止撤单、补单、冰山、吸收推断 |

## 八日证据

- 所有 8 个 Snapshot：`OPF_RESEARCH_2.29 / ACTUAL_EXEC_2.47`，Data Only 标记存在，Actual orders=0，6 Source，DepthSnapshot error=0。
- `OnNewTrade`：八日均 276 Bar；源内 ArrivalSequence 连续、事件时间无逆序；Tick/M5 Volume 偏差 `-0.0389%~-0.1630%`；原始 `Direction` 为 Buy/Sell，成交量覆盖均为 100%。
- 规范深度源为 `MarketDepthChanged`；`MarketDepthsChanged` 为重复回调，永不相加。

## DOM 质量量化

- `MarketDepthChanged` 共 90,791,681 事件；此前到达 BBO 的引用覆盖 90,791,639（99.99995%）。
- 在 ±1000 点审计带内 90,764,388；带外 27,251（0.030015%，低于预注册 0.1%）。极值可用“此前到达 BBO + 价格带”作为固定排除规则。
- 但仅 93.7420% 与最新 BBO 侧别一致，逐日最低为 90.5704%。这不是可接受的顶档状态验证：Depth 与 BBO 回调的到达顺序不同，不能把两者直接拼成同一时刻的盘口。
- `DepthSnapshot` 带外 1,422,155 / 7,234,330（19.65842%），不得作为历史盘口快照来源。
- `OriginPrice` 恒为 0；深度 `Price` 是唯一候选价格字段，`DataType=Bid/Ask` 只足以识别侧别，未提供新增/撤单/成交动作语义。

## 后续约束

阶段 2 可开始逐笔/Footprint Data Only 采集，但 DOM 只能作为独立的重建诊断：以 ArrivalSequence 更新价格层，且仅在候选决策时验证其最终 top-of-book 是否能与该时刻的 BestBid/Ask 一致。只有该诊断在预注册样本通过，才讨论静态深度不平衡；动态 DOM 研究永久停止。

## 归档

`opf_v229_1b_final_tick_footprint_pass_dom_static_diagnostic_only_8snapshots_20260727`

- 144 文件，29,802,069 bytes，HashMismatch=0。
- Manifest SHA256：`8A21D78EDBED3773CF8C96326703D3095BD6B2E552FA50D6BD9C66DEE641FCCE`。
