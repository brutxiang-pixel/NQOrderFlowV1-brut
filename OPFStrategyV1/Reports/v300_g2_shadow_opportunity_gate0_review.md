# V3.0 Gate 0：G2 影子机会带功效审计

日期：2026-07-28  
性质：零 Replay、零 DLL、零收益结论的采集可行性裁决。

## 冻结定义

一条机会必须同时满足：

1. 主槽是同冻结 Actual 证据中的正常交易；
2. 主槽已经记录 `SL_SENT`，且尚未实际退出；这才是本项目的“保护就绪”，不是达到 1R；
3. 候选与主槽同方向，且已经抵达现有的第二槽决策分支（`Execute`、`SecondaryStaticG2Blocked`、`SecondaryRiskCap` 或保护重试）；
4. 候选自身交易不得被当作主槽窗口，避免自重叠计数。

该定义只衡量未来 Data Only 采集是否有足够的可研究机会；不推断第二槽收益，也不把现有 Actual 第二槽结果当作新策略结果。

## 预注册门槛

| 项目 | 门槛 |
|---|---:|
| 2026 H1 机会总数 | >=120 |
| H1 每月机会数 | >=12 |
| 2026-07 压力样本 | >=25 |
| 已有 Footprint 完整标签率 | >=80% |

只有四项同时通过，才允许冻结 17 日采集清单（H1 每月两日 + 7 月全可用日）；3 日技术验收只能是该清单子集，不得临时替换日期、特征或阈值。

## 结果

来源为 `opf_v230_outcome_label_24day_gate_passed_24snapshots_20260728`，保护窗口为 184 条正常交易的 `SL_SENT -> Actual Exit` 区间。

| 指标 | 结果 | 判定 |
|---|---:|---|
| 可用保护窗口 | 184 | 仅作分母审计 |
| 同向第二槽机会 | 59 | 不足 |
| H1 总数 | 52 | 未达 120 |
| 202601 / 02 / 03 / 04 / 05 / 06 | 20 / 6 / 3 / 10 / 8 / 5 | 仅 1 月达月度下限 |
| 7 月 | 7 | 未达 25 |
| 既有 Footprint 完整标签率 | 100% | 通过 |
| 已执行第二槽 / 静态 G2 拦截 / 风险上限拦截 | 36 / 21 / 2 | 仅作阻塞结构说明 |

## 裁决

**Gate 0 不通过；不编译 DLL、不清理日志、不做 17 日或 3 日新采集，也不进入 Smoke。**

失败原因是机会密度，而不是 Tick 原始方向、价格或 Footprint 标签缺失。按照现有密度，H1 和 7 月均需要数倍于已审计样本的 Replay 才可能仅达到“样本量”门槛；这既不能解决月度失衡，也不符合低成本采集纪律。

G2 影子机会带在本阶段归档为 no-go。若未来出现一个已经独立成立的新信号家族，且它天然在保护窗口内产出足量候选，可以重新以新的、预注册的 Gate 0 评估；不得通过降低本门槛、事后挑日期或扩大特征自由度重开本线。

## 可复核输出

- 脚本：`OPFStrategyV1/Scripts/Audit-OPFV300G2ShadowOpportunityGate0.py`
- 汇总：`OPFStrategyV1/Reports/v300_g2_shadow_gate0/summary.json`
- 机会明细：`OPFStrategyV1/Reports/v300_g2_shadow_gate0/opportunities.csv`
- 日覆盖：`OPFStrategyV1/Reports/v300_g2_shadow_gate0/day_coverage.csv`
- 阻塞原因：`OPFStrategyV1/Reports/v300_g2_shadow_gate0/reason_counts.csv`
