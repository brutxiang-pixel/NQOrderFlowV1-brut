# v2.03 阶段D：KNN入场时特征审计

日期：2026-07-24

状态：审计完成；25个数值特征全部保留，18个分类特征中11个保留、7个移除。该结论只约束离线KNN研究线，不修改Actual策略。

## 审计标准

- 特征值必须在候选执行决策时已经存在，并且可由当时的行情、结构、候选或确认信息稳定重建。
- 凡描述旧基线已经执行、阻塞或跳过了什么的字段均移除，即使它在历史CSV中与入场时间同行。
- 退出结果、后续Bar、旧TradeID、旧ActiveTrade状态、日限额/日损阻塞结果不得进入距离特征。
- `ResearchPath`与`Side`仍是模型分组键，不属于本次25+18距离特征审计。

## 数值特征（25/25保留）

| 特征 | 结论 | 入场时来源/理由 |
|---|---|---|
| RegimeScore | 保留 | execution decision当根评分 |
| SetupQualityScore | 保留 | execution decision当根评分 |
| InitialRiskPoints | 保留 | 候选计划Entry/Stop计算值，不使用事后FilledRisk |
| EstimatedRR | 保留 | 候选计划风险收益比 |
| ATR14 | 保留 | risk evaluation在同一EntryBar的ATR |
| ZoneWidth | 保留 | 候选所属Zone的当时宽度 |
| ZoneTouchCount | 保留 | edge attribution在决策时的触碰计数 |
| PullbackCountInRegime | 保留 | candidate evaluation在决策时的计数 |
| CandleRange | 保留 | EntryBar已完成K线High-Low |
| BodyRatio | 保留 | EntryBar已完成K线实体/范围 |
| CloseLocation | 保留 | EntryBar收盘位置 |
| ZoneAge | 保留 | EntryBar-CreatedBar |
| BullScore | 保留 | EntryBar向后匹配的最近regime change状态 |
| BearScore | 保留 | EntryBar向后匹配的最近regime change状态 |
| TrendStrength | 保留 | max(BullScore, BearScore) |
| Alignment | 保留 | 按Side对齐的BullScore-BearScore |
| ConfirmRange | 保留 | 仅在确认型候选决策时已完成的确认Bar |
| ConfirmBodyRatio | 保留 | 同上；无确认信息时为缺失/0 |
| ConfirmCloseLocation | 保留 | 同上 |
| ConfirmAlignment | 保留 | 确认Bar相对交易方向的实体方向 |
| ConfirmPrevBreak | 保留 | 确认Bar收盘相对前高/前低 |
| ConfirmZoneReclaim | 保留 | 确认Bar收盘相对Zone边界 |
| HourSin | 保留 | EntryTime确定性派生 |
| HourCos | 保留 | EntryTime确定性派生 |
| DayOfWeek | 保留 | EntryTime确定性派生 |

## 分类特征（11保留，7移除）

| 特征 | 结论 | 入场时来源/风险 |
|---|---|---|
| Season | 保留 | EntryTime确定性派生 |
| Session | 保留 | EntryTime确定性派生 |
| ZoneType | 保留 | 决策时Zone类型 |
| ZoneFreshness | 保留 | 决策时Zone状态 |
| RegimeBucket | 保留 | 决策时RegimeScore分桶 |
| SetupQualityBucket | 保留 | 决策时质量分桶 |
| RiskBucket | 保留 | 计划风险分桶 |
| EstimatedRRBucket | 保留 | 计划RR分桶 |
| TimeBucket | 保留 | EntryTime分桶 |
| SkipPattern | 移除 | 来自signals.SkipReasons；可包含ReplayExecuted、TradeID、ActiveTrade及旧门禁结果，描述旧基线行为 |
| HasZoneBirthResearch | 移除 | 从SkipPattern派生，属于旧候选链已经产生过什么的状态 |
| HasObservationConfirmResearch | 移除 | 同上 |
| HasObservationStrictResearch | 移除 | 同上 |
| HasFailureReverseResearch | 移除 | 同上 |
| HasFailureRetestTrigger | 移除 | 同上 |
| HasBreakawayQualified | 移除 | 同上 |
| ConfirmAvailable | 保留 | 确认型候选决策时确认记录是否存在；不读取后续Bar |
| ConfirmReason | 保留 | 同EntryBar确认结果；与ResearchPath可能冗余，但不是事后泄漏 |

## 代码证据

- `Analyze-OPFDynamicPolicyPriority.py`先把signals的`SkipReasons`并入候选，再生成`SkipPattern`及6个`Has*`字段；这些字段并非纯市场状态。
- 同脚本的确认特征只连接`Stage=ObservationConfirm`、`Result=Confirmed`且EntryBar相同的记录，因此确认特征在对应候选决策时已知。
- Bull/Bear分数使用`merge_asof(... direction="backward")`，不会读取EntryBar之后的regime change。

## 阶段D第一轮约束

1. 先从KNN距离中移除上述7个分类字段。
2. 权重0必须重新精确复刻干净KNN基线；旧SHA只能用于旧特征集，清洁特征集需生成新的冻结SHA与收益锚。
3. 权重0通过后只运行：RegimeBars `0.10/0.25/0.50`，Trend `0.10/0.25`。
4. Relative Volume/VWAP继续停止；第一轮没有同时超过观察与保守清洁基线的项，不启动第二轮组合。
