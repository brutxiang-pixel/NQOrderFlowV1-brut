# v2.02 Rich特征组严格LOMO评估

日期：2026-07-24

状态：趋势六组件、相对量/VWAP距离、Regime状态＋持续时间、RegimeBars单变量均未同时超过观察与保守基线；不进入Smoke。

## 固定口径

- Q4：v1.91精确影子62 Snapshot。
- H1：v1.94权威基线122 Snapshot。
- 严格逐月留一，K=40，类别权重0.75。
- 单ActiveTrade、每日15笔、日损`-$300`、每笔手续费`$2.40`。
- 基线观察：1399笔，Net`+$17,815.04`。
- 基线保守：1418笔，Net`+$16,663.88`。
- 只有观察和保守同时超过各自基线才允许进入Smoke。

## 特征处理

- 趋势组使用方向对齐后的Bull/Bear总分、六组件贡献、对向贡献、低基数Swing/计数状态和可解析数值。
- DirectionalDisplacement与AtrExpansion高基数原始字符串不直接分类，只解析ATR及扩张比率。
- 相对量/VWAP组只使用成交量对数、20-Bar相对量、方向对齐VWAP距离和绝对VWAP距离，不使用绝对VWAP价格。
- RegimeBars使用`log(1 + bars)`；另保留一轮“Regime分类＋RegimeBars”作为拆分反证。
- `2025-10-21 22:35 / ZoneBirth Short`为唯一Rich边界候选。它显式标记为`RichUnavailable`，数值只用每个训练折的中位数填充，不使用测试月信息。

## 每日15笔最佳结果

| FeatureSet | Mode | 参数 | Trades | H1 Net | Q4 Net | Combined Net | 相对基线 |
|---|---|---:|---:|---:|---:|---:|---:|
| Baseline | Observed | shrink=0 / gate=0.075 | 1399 | +$13,306.50 | +$4,508.54 | +$17,815.04 | $0.00 |
| Baseline | Conservative | shrink=15 / gate=0.075 | 1418 | +$12,601.34 | +$4,062.54 | +$16,663.88 | $0.00 |
| Trend | Observed | shrink=0 / gate=0.125 | 1294 | +$10,402.52 | +$5,377.34 | +$15,779.86 | -$2,035.18 |
| Trend | Conservative | shrink=0 / gate=0.125 | 1281 | +$10,804.28 | +$5,005.34 | +$15,809.62 | -$854.26 |
| Volume/VWAP | Observed | shrink=15 / gate=0.075 | 1472 | +$12,185.16 | +$3,927.96 | +$16,113.12 | -$1,701.92 |
| Volume/VWAP | Conservative | shrink=5 / gate=0.150 | 1124 | +$11,107.46 | +$2,953.24 | +$14,060.70 | -$2,603.18 |
| Regime＋Bars | Observed | shrink=0 / gate=0.075 | 1401 | +$13,785.58 | +$3,611.14 | +$17,396.72 | -$418.32 |
| Regime＋Bars | Conservative | shrink=15 / gate=0.050 | 1555 | +$11,826.32 | +$4,369.88 | +$16,196.20 | -$467.68 |
| RegimeBars | Observed | shrink=0 / gate=0.075 | 1402 | +$13,862.30 | +$3,619.70 | +$17,482.00 | -$333.04 |
| RegimeBars | Conservative | shrink=5 / gate=0.000 | 1609 | +$11,704.68 | +$4,187.60 | +$15,892.28 | -$771.60 |

## 结论

- 趋势六组件明显改善Q4，但同时大幅破坏H1，说明整组等权进入距离后改变邻居结构过强。
- 相对量/VWAP距离在观察和保守口径均无整体增益，当前不保留。
- RegimeBars是最接近基线的新增信息，但15笔观察仍低`$333.04`，保守低`$771.60`。
- RegimeBars无限单量观察达到`+$17,911.40`，高于观察基线`$96.36`；但正常15笔口径失败，且无限单量保守仅`+$16,136.54`，仍低于保守基线，因此不能晋级。
- 三组特征都不是“无信息”，但直接按每列标准化后等权加入现有KNN欧氏距离会稀释已验证的旧结构。禁止把失败组全部合并继续碰参数。

## 下一步建议

保持旧KNN距离为主模型，只研究Rich特征块的小权重增量。优先顺序：

1. RegimeBars块权重`0.10/0.25/0.50`。
2. 趋势六组件块权重`0.10/0.25`，重点验证能否保留Q4改善而不损伤H1。
3. 相对量/VWAP组本阶段停止，不进入权重搜索。

权重0必须继续精确复刻旧基线。仍采用严格LOMO和双门槛，未同时通过前不进入Smoke。

## 证据SHA256

```text
v202_rich_feature_engine_baseline_regression_lomo.csv B91E2D9E376B5FA1779B0A942247B9E37A106176BC1F723824A2C8B449585E7C
v202_rich_trend_lomo.csv 47B6E4BB0BCFFE721BA34A97B9F6B7EFC65CEE7E4F23BE15C77651DFBF48F111
v202_rich_volume_vwap_lomo.csv 7CB8C2286B579B40B151A6AA6294A32F79BB137AA42C31F64B33C56B8D059AF0
v202_rich_regime_state_bars_lomo.csv 1AD868BE4693A91DE34FA3F48810CB34EC2E729D84EE18BD3269105036BEDF36
v202_rich_regime_bars_lomo.csv 447219F6960C69E9659C8CA8513A359545E20B911C2E1452404707E007614D71
```
