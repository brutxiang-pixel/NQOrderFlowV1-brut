# V5.0 Actualized Counterfactual Tape：边际 ZoneBirth Short 审计

日期：2026-07-31  
候选：仅禁止 `ZoneBirthResearch Short` 且 `SetupQualityScore = 45`。

## 验收结果

Actualized Tape 使用账户的实际 Normal PnL 作为唯一已实现收益来源，基线全开关精确复刻：2,047笔、Normal Net `+$19,904.30`。没有使用旧影子PnL、静态计划PnL或未来字段替代实际订单结果。

候选删除51笔实际订单：

| 情景 | 笔数 | Normal Net | 相对基线 |
|---|---:|---:|---:|
| 冻结Actual基线 | 2,047 | +$19,904.30 | — |
| 冻结同日门、无替代订单 | 1,996 | +$21,111.91 | +$1,207.61 |

该51笔在四个训练段均为负：2025春夏19笔/`-$669.92`、Q4 9笔/`-$17.39`、2026 H1 20笔/`-$237.50`、7月3笔/`-$282.80`。`SetupQualityScore=45` 是现有离散决策等级，不是对连续阈值的事后搜索。

## 组合约束审计

- 被删除订单没有造成任何 `ActiveTrade` 阻塞候选，故不存在持仓占用释放后的直接替代链。
- 但其后的日单数或日损门候选共有124个唯一记录：2025春夏77、Q4 13、2026 H1 29、7月5。
- 这些候选是否会真的被释放、相互之间是否仍会阻塞、以及真实成交后的收益，均不能从现有静态/影子文件可靠推出。

## 裁决

候选通过“方向与实际基线复刻”门，但**未通过策略晋级门**：`+$1,207.61` 仅是冻结门、零替代的直接差值，不是可部署或可预测收益；不得据此改DLL、安排Smoke或宣称策略提升。

旧影子表与当前Actual退出链的跨年校准已失败，因此继续扩展离线利润模拟会制造虚假精度。下一步唯一有效的证据路径是：经用户明确授权后，实施单变量、配对的 Actual Replay 校准；每个日期先跑冻结基线再跑该候选，逐笔比较订单、日损/日单数门及账户PnL。只有配对样本能证明124个潜在替代候选没有吞掉直接改善，才可讨论有限Smoke。

输出文件：

- `OPFStrategyV1/Reports/v500_actualized_counterfactual_tape/v500_actualized_baseline_trades.csv`
- `OPFStrategyV1/Reports/v500_actualized_counterfactual_tape/v500_zonebirth_short_frozen_gate_scenario.csv`
- `OPFStrategyV1/Reports/v500_actualized_counterfactual_tape/v500_zonebirth_short_exposure_tape.csv`
