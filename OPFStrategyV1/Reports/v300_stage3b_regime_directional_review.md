# v3.0 阶段 3B：Short-only 与三态 Regime 方向审计

日期：2026-07-28  
范围：同冻结 24 日 Actual 正常结果的直接删减归因。Q4 已按项目决策移出本阶段口径，仅报告 H1 与 7 月。

## Regime 可得性

每个 `NormalOutcome` 均可按其 Actual Snapshot 的 `regime_changes.csv`，取不晚于决策时间的最后一条状态，严格重建为：`BullTrend / BearTrend / ChopRange`（原 `Unknown` 映射为 `ChopRange`）。

192 / 192 条正常结果均有决策时可得 Regime：BullTrend 10 条、BearTrend 12 条、ChopRange 170 条。故本轮失败不是状态缺失或使用未来数据。

## Short-only：直接删减下界

| 期间 | 保留 Short | Net | 删除 Long | Net | 结论 |
|---|---:|---:|---:|---:|---|
| H1 | 70 | +$1,988.50 | 103 | +$3,811.70 | 不支持 |
| 7月 | 17 | -$161.20 | 2 | -$237.20 | 不支持 |

该结果不含删除 Long 后可能释放的槽位替代，因此不是精确策略预测；但被删 Long 在 H1 明显为正、Short-only 在 7 月仍为负，未满足值得启动容量反事实模拟的必要条件。

## 三态 Regime：直接归因

| 期间 | 状态/方向 | 笔数 | Net |
|---|---|---:|---:|
| H1 | BullTrend Long | 10 | -$104.00 |
| H1 | BearTrend Long | 1 | +$363.90 |
| H1 | BearTrend Short | 5 | +$319.00 |
| H1 | ChopRange Long | 92 | +$3,551.80 |
| H1 | ChopRange Short | 65 | +$1,669.50 |
| 7月 | BearTrend Short | 6 | -$272.10 |
| 7月 | ChopRange Long | 2 | -$237.20 |
| 7月 | ChopRange Short | 11 | +$110.90 |

预注册三态动作要求关闭 BearTrend Long 与 ChopRange Long。前者样本仅 1 条，不能支持规则；后者会直接删除 H1 最大的正贡献 `+$3,551.80`。虽可改善 7 月的 Long 损失，但不足以抵消 H1 证据。因此不再为 Chop/Range 擅自定义“高确信 Short”，也不进行阈值搜索。

## 裁决

Short-only 与三态 Regime 均未通过方向筛选；它们不进入离线容量模拟、DLL、Smoke 或 Replay。结合阶段 3A G2 微观确认四项全淘汰，当前 V3.0 已完成的预注册探索线没有产生任何可进入确认梯子的候选。

这是“先做减法”得到的有效结论，不应靠增加规则、特征组合或旧版本收益去推翻。下一步应进入阶段 5 的 go/no-go 评审；如要继续，必须由用户明确授权转向新的信号家族，而不是在当前 OPF 路径、G2、Regime 或时段门内继续调参。

可复现脚本：`Scripts/Audit-OPFV230RegimeDirectional.py`，输出：`Reports/v300_stage3b_regime_directional_audit/`。
