# V4.0 阶段 3：Zone Behavior 离线生命周期研究合同

日期：2026-07-29。前提为 24 日 `ZoneBehaviorLedgerDataOnly` 数据集已通过验收；本阶段只读取 CSV，不编译 DLL、不修改 ATAS 配置、不清理活动日志。

## 目的与边界

本轮只回答一个描述性问题：在首次真实 Touch 当时已经可见的 Zone 行为代理，是否与随后固定窗口内的 Zone 外价格响应存在跨月稳定关系。

- 不产生入场、评分、TP/SL、仓位、风险门或实盘结论。
- 不连接策略订单、收益、候选流或组合模拟；静态 Zone 响应不是交易收益。
- 不使用 DOM、热图、Spoofing，亦不把主动成交代理称为真实被动挂单、吸收或被套资金。
- 只分析每个 Zone 的首次 Touch (`TouchOrdinal=1`)；后续 Touch 保留在原始账本中，但不进入本轮，以避免同一 Zone 的相关样本放大结论。

## 固定研究样本与时间边界

仅使用 `v400_zone_behavior_ledger_24day_collection_plan.csv` 的 24 个日期；技术样本 `2026-03-16`、`2026-05-04` 与空启动 Snapshot 永久排除。

对每一首次 Touch：

1. 特征截点为该 Touch M5 已闭合时刻；只读 Birth 至该 Touch（含）的 `zone_behavior_bars.csv` 与该 Touch 前已经存在的事件。
2. 标签窗口为其后最多 12 根连续 Zone Bar，不含 Touch Bar。本轮只对存在完整 12 根后续 Zone Bar 的样本计算固定窗口标签；提前终态的样本标为 `Censored`，只进入样本覆盖报告，不作零值填补。
3. Bull Zone 的有利推进为后续最高 High 超过 `ZoneHigh` 的点数；不利穿透为 `ZoneLow` 减后续最低 Low 的正值。Bear Zone 方向镜像：有利推进为 `ZoneLow` 减后续最低 Low，不利穿透为后续最高 High 超过 `ZoneHigh`。
4. `InvalidatedWithin12Bars` 仅根据 Touch 后 12 根窗口内的 `Invalidated` 终态确定；`Expired`、`SnapshotEnd` 不冒充失效。

## 预注册特征（全部为 Touch 时或此前可知）

每项单独分析，不组合、不搜索阈值、不按 Zone 类型、时段、方向或日期二次切片：

1. `SignedDeltaRatio`：方向对齐后的 `(CumulativeBuyVolume - CumulativeSellVolume) / (Buy + Sell)`。
2. `ZoneVolumeShare`：`(CumulativeZoneBuyVolume + CumulativeZoneSellVolume) / (CumulativeBuyVolume + CumulativeSellVolume)`。
3. `FavorableAdvancePoints`：Birth 至 Touch（含）累积的 `MaxFavorableExcursion`。
4. `AdversePenetrationPoints`：Birth 至 Touch（含）累积的 `MaxAdverseExcursion`。

分母为零、逐笔不可用或缺失 Birth/Touch/Bar 键的记录必须显式排除并计数，不能用 0 补值。

## 分析与稳健性规则

- 2026-01 至 2026-06 为主研究窗，按月份留一（LOMO）。每个被留月份的低/中/高三分箱边界仅由其余 H1 月份计算；7 月只使用完整 H1 的边界作压力样本，不参与任何边界选择。
- 每个特征×分箱报告：样本数、完整窗口率、有利/不利推进中位数、`NetResponsePoints = Favorable - Adverse` 中位数、12 Bar 内失效率；并按月输出。
- 只做低分箱对高分箱的方向性比较。不得因某月结果更好临时改为中分箱或改阈值。
- 本轮不存在“可部署通过”。只有当某一特征的高低分箱差异在至少 5/6 H1 LOMO 留出月同向、H1 合并差异绝对值至少 1.0 点、7 月方向不反转且每个比较分箱不少于 30 个完整标签时，才标记为 `FollowUpEligible`；它仅允许进入“与策略候选流的连接可行性审计”，不允许 Smoke。

## 必交输出与否决条件

离线脚本必须输出：`first_touch_labels.csv`、`feature_lomo.csv`、`monthly_coverage.csv`、`summary.json` 及研究报告。

若任一项发生，立即 No-go，且不改 DLL、不追加采集：研究样本无法覆盖全部 24 日、H1 完整窗口总数少于 180、任何预注册特征完整率低于 95%、或没有 `FollowUpEligible` 特征。No-go 是本阶段有效结论，不得通过重新分箱、挑日期或增加交叉条件规避。
