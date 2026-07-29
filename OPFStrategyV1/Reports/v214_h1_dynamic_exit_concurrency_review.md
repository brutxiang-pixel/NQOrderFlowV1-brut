# v2.14 H1盈利研究：动态退出与并发组合粗筛通过

日期：2026-07-25

状态：A层粗筛安全门通过；停止本轮模拟。尚未完成v2.14精确校正、Actual双OCO实现、Smoke或Replay，禁止把本结果表述为可部署收益。

## 冻结目标

- 优化集：2026 H1，122个有效交易日。
- 每笔3手；周为最小稳定性统计单元。
- 粗筛门：保守Gross至少`+$24,000`、PF至少1.35、盈利周至少75%、1-2月/3-4月/5-6月均为正。
- 只有越过粗筛门，才允许冻结24日v2.14精确校正集；全量Replay仍只用于最终候选。

## 研究闭环

1. 单ActiveTrade基线在`18笔/-$450`下精确复刻为保守Gross`+$19,221.73`。
2. 单独并发、全局门禁、同柱排序、静态Long扩展均未达到`+$24,000`。
3. 对当前候选使用路径×风险带的严格逐月留一退出选择：每个测试月只读取其他五个月；训练样本至少60笔，使用1.645标准误惩罚，保守笔均改善不足`$4`则保持当前退出。单槽保守Gross提高到`+$21,712.59`，PF 1.4421，盈利周81.48%。
4. 在该动态退出基线上增加事件驱动第二槽；第二槽只接收原本被ActiveTrade阻塞的候选，使用路径×风险带逐月留一分数，alpha=50，保守单合约净期望必须大于等于0。
5. 入场只增加持仓和日单数，PnL到ExitTime才实现；同一时点先结算退出再处理入场；日损只读取已实现PnL。5/10分钟Active释放均完整重放，并取更差结果。

## 通过候选

冻结A层候选：`DynamicExit_PathRisk_N60_Z1.645_M4 + Concurrent_PathRisk_A50_G0 + RiskCap300`

| 指标 | 结果 |
|---|---:|
| 5分钟保守Gross | `+$24,509.46` |
| 10分钟保守Gross | `+$24,619.79` |
| 鲁棒保守Gross | `+$24,509.46` |
| PF | 1.4171 |
| 盈利周 | 81.48% |
| MaxDD | `$1,497.53` |
| 最差周 | `-$616.87` |
| 正常交易 | 1,079笔 |
| 1-2月 | `+$5,444.81` |
| 3-4月 | `+$7,847.03` |
| 5-6月 | `+$11,217.62` |
| 第二槽保守Gross | `+$2,860.35` |
| 第二槽保守净期望 | `+$10.99/笔` |
| 第二槽交易 | 199笔 |
| 同向重叠率 | 76.88% |
| 峰值并发初始风险 | `$297.75` |
| 日损阻断日 | 9日 |

共有25个组合越过全部门槛，但全部属于`3手主槽+3手第二槽`容量臂；`2+1`和`3+1`均未通过。绝对Gross最高项为`+$24,613.22`，但风险上限为`$450`、PF和回撤均略逊。按预注册的风险优先原则，选择`RiskCap=$300`的`+$24,509.46`项，而不是挑最大值。

## 风险与边界

- “每笔3手”已满足，但并发时最多6手；这不是当前单OCO策略可直接部署的配置。
- 虽然峰值初始风险被限制在`$297.75`，同向重叠率仍为76.88%，第二槽主要增加同方向暴露，而非真正分散。
- 结果使用旧122日候选流与v2.02 Rich做A层粗筛；未成交候选已扣执行风险储备，Rich退出已扣M5校准储备，但仍不能替代v2.14精确报价与转折带校正。
- 退出选择和第二槽选择都使用逐月留一，降低了同月泄漏，但H1整体仍是研究集；不得作为前向OOS或实盘通过证据。

## 下一Gate

1. 立即停止扩大模拟网格，冻结上述候选、脚本参数和报告SHA。
2. 预注册24个分层日期，采集v2.14精确Decision Tape、Entry Quote和MarketTurn；不修改Actual交易规则。
3. 精确校正必须同时验证动态退出、第二槽候选身份、风险重算、同时事件顺序和保守Gross下修幅度。若校正后保守Gross低于`+$20,000`或PF低于1.35，候选退回离线研究，不进入双OCO实现。
4. 只有24日校正通过，才设计双OCO生命周期并安排功能Smoke；全量H1 Replay仍放在最后。

## 证据

- `OPFStrategyV1/Reports/v214_h1_dynamic_exit_lomo/dynamic_exit_robust.csv`
- `OPFStrategyV1/Reports/v214_h1_dynamic_exit_concurrency/concurrency_raw.csv`
- `OPFStrategyV1/Reports/v214_h1_dynamic_exit_concurrency/concurrency_robust.csv`
- `OPFStrategyV1/Reports/v214_h1_dynamic_exit_concurrency/concurrency_trades.csv`
- `OPFStrategyV1/Scripts/Analyze-OPFV214H1DynamicExitLomo.py`
- `OPFStrategyV1/Scripts/Analyze-OPFV214H1Concurrency.py`

关键SHA256：

- `concurrency_robust.csv`: `617FF90D88BFCDD5E908AD3620CD10322FCEC8AC84A614B2F14A69E28CF94061`
- `dynamic_exit_robust.csv`: `550C04710F1735AEA6AD05533700097B945F629C9FC0FDCDC3F97ED23D2EBA1E`

本轮没有修改或编译策略DLL，没有清理ATAS日志，没有安排Smoke或Replay。
