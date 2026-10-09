# V4 4C-R / 4F 校准闭环合同

日期：2026-07-30。该闭环只修复离线字段口径并验证静态标签的诊断边界；不修改 DLL、ATAS 配置、活动日志、冻结策略或历史 Actual 证据。

## 4C-R：三类几何永久分离

| 字段 | 含义 | 当前精确匹配 |
|---|---|---:|
| `CandidatePlannedRiskPoints` | Data Only Candidate 的计划风险 | 与 Actual 计划风险 881/926 |
| `ActualPlannedRiskPoints` | 冻结 Actual 执行记录的计划风险 | 与 FilledRisk 132/926 |
| `FilledRiskPoints` | 真实成交重定价后的风险 | 不可替代计划风险 |
| `CandidatePlannedTargetR` | Candidate 计划 TargetR | 与 Actual PlannedTargetR 926/926 |
| `CandidatePlannedEntry` | Candidate 计划入场 | 与 Actual Entry 相同 139/926 |

2026-07-06 的完全相同 Replay 副本仍保留在归档；连接器仅排除该 1 条副本，权威实际样本固定为 926。

## 4F：Static—Actual 诊断

- 926/926 canonical Actual 均连接唯一静态标签。
- 只把静态 `SL` / `TP` 作为方向诊断样本：749 笔。
  - 静态 `SL`：465/465 Actual 账户净收益为负。
  - 静态 `TP`：279/284 Actual 账户净收益为正。
- `TimeStop` 128 笔、`Ambiguous` 20 笔、`Censored` 29 笔均不参与静态方向正确率分母；它们仅报告覆盖和 Actual ExitFamily。
- 四日 Tick 只用于 M5 同柱歧义审计，不得外推到全量标签。

## 裁决与后续门

4F 证明确定性静态 TP/SL 可作为**已成交样本的诊断标签**，不证明静态 PnL、替代链、日损触发或组合收益可复刻 Actual。故只允许下一阶段评估**一个**预注册单变量 Candidate 筛选器，且必须同时满足：

1. 只使用非 `ReplayLocalOnly`、决策时已知且未被 V3/V4 No-go 否决的字段；
2. StaticPlan Strict 与 LowerBound 均优于全 Candidate 静态基线；
3. 被筛除的已成交 Actual 子集直接贡献不为正；
4. 单独报告 `TimeStop/Ambiguous/Censored` 覆盖与替代链；
5. 仍不得进入 Smoke；只有离线筛选结论完成独立审查后才另行决定。

任何课程“燃料、证伪、吸收、B/P Delta、两次减速”研究仍需要独立的全生命周期/逐笔采集立项；不得从本轮数据中事后构造。
