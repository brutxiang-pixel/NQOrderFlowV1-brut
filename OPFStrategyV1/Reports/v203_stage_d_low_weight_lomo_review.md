# v2.03 阶段D：清洁特征低权重LOMO结论

日期：2026-07-24

状态：第一轮完成；RegimeBars与Trend均未同时超过清洁观察/保守基线，阶段D第二轮不启动，不进入Smoke。

## 清洁特征基线

审计移除了7个描述旧基线行为的分类字段：`SkipPattern`、`HasZoneBirthResearch`、`HasObservationConfirmResearch`、`HasObservationStrictResearch`、`HasFailureReverseResearch`、`HasFailureRetestTrigger`、`HasBreakawayQualified`。

清洁权重0、每日15笔的最佳结果：

| Mode | Trades | H1 Net | Q4 Net | Combined Net |
|---|---:|---:|---:|---:|
| Observed | 1421 | +$13,519.04 | +$3,819.12 | +$17,338.16 |
| Conservative | 1330 | +$12,037.86 | +$3,216.52 | +$15,254.38 |

RegimeBars与Trend输出中的权重0均与该清洁基线120行逐格完全一致，差异单元格为0。

## RegimeBars第一轮

| Weight | Observed Net | 相对清洁基线 | Conservative Net | 相对清洁基线 | 结论 |
|---:|---:|---:|---:|---:|---|
| 0.10 | +$17,282.58 | -$55.58 | +$15,527.78 | +$273.40 | 失败 |
| 0.25 | +$17,051.82 | -$286.34 | +$15,550.68 | +$296.30 | 失败 |
| 0.50 | +$16,863.98 | -$474.18 | +$15,388.24 | +$133.86 | 失败 |

RegimeBars稳定改善保守口径，但每个非零权重都降低观察口径；没有同一权重通过双门。

## Trend第一轮

| Weight | Observed Net | 相对清洁基线 | Conservative Net | 相对清洁基线 | H1/Q4解释 | 结论 |
|---:|---:|---:|---:|---:|---|---|
| 0.10 | +$17,157.12 | -$181.04 | +$15,200.40 | -$53.98 | 观察Q4提高+$320.58，但H1下降-$501.62 | 失败 |
| 0.25 | +$15,028.54 | -$2,309.62 | +$15,030.70 | -$223.68 | 两期组合整体退化 | 失败 |

Trend 0.10仍复现“Q4改善、H1受损”的旧结构；降低权重不足以消除跨期冲突。

## 判决

- 阶段D第一轮没有合格项，按冻结合同不运行第二轮组合。
- Relative Volume/VWAP继续停止，不恢复无边界Rich扫描。
- 本轮没有修改Actual策略、DLL、订单规则或活动日志。
- 后续主线裁决仍由v2.03的8日Actual替代链校准负责；KNN清洁基线保留为后续新信息研究锚。

## 数据完整性

- 184日：Q4 62日、H1 122日。
- Rich 49,723行，重复键0。
- ExecutionDecision连接46,382/46,382；ExitPolicy连接313,300/313,300；可用Shadow连接2,283/2,283。
- Component JSON错误0；仅1个初始化边界候选，沿用训练折中位数/Unavailable规则。

## SHA256

```text
v203_stage_d_clean_feature_weight0_lomo.csv A4EE0A951B4E67B6E4F792B288E12B6A0F0614AA285A73611995BCE06C1F2200
v203_stage_d_clean_feature_weight0_validation.csv FA48ABDD27C7A48EFABC47865352BE878A1D346FAD432FBB5AE29E8516339DA2
v203_stage_d_regime_bars_low_weight_lomo.csv 194961CE618AAA3E37450470E8B18E6FA273820AD789248F642687DC004FB32C
v203_stage_d_trend_low_weight_lomo.csv DE1CC03A6BD8A9EA0D8BFF29457C33FABB12AC7701BF0829577DD34A76501D63
```
