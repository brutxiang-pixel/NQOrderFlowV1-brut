# V4 PlannedRisk 单变量 Candidate 筛选器设计

## 目的与结论边界

本研究只评估一个下单前可知的 Candidate 筛选器是否提高 **StaticPlanPortfolio / Counterfactual** 的整体净收益。它不是策略规则实现，不修改 DLL、ATAS 配置、活动日志、冻结 Actual 归档或 V3 基线；结果不得称为 Actual Replay、Smoke、实盘收益或实盘资格。

本设计承接 V4 4C-R / 4F。4F 仅证明确定性 Rich M5 的 TP/SL 标签可诊断已成交 Actual 的方向，未证明静态组合 PnL、替代链、日损或并发占用可复刻 Actual。因此，本轮即使通过，也只允许独立审查是否值得另行设计最小 Data Only/Actual 验证，不自动安排 Smoke。

## 复核后的预注册变量

唯一变量为 `CandidatePlannedRiskPoints`。

- 输入只能来自 `v400_portfolio_candidate_tape_141day.csv` 的 Candidate 计划几何。
- 决策规则：`CandidatePlannedRiskPoints <= 48.00` 时保留；大于 48.00 时筛除。
- 48.00 是 2026 H1（30,732 个 Candidate）**不含收益标签**的 P90；7 月不参与阈值确定。此前 49.50 是含 7 月 141 日流的 P90，因会让压力集参与定阈值，正式研究不得使用。
- 不设第二阈值、分路径阈值、方向阈值、交互项或后续阈值扫描。无论结果如何，35/40/50/65 等变体均不运行。

## 明确排除的字段和方向

不得读取、派生或作为筛选条件使用：

- `EntryBid`、`EntryAsk`、`MarketSequence`、`ZoneLastTouchMarketSequence`、Zone 局部累计 Buy/Sell/Delta，及任何 `ReplayLocalOnly` 字段；
- 已 No-go 的 Zone 行为阈值、既有路径/Regime 微调、普通 OHLC/VWAP/ATR/RelativeVolume 阈值；
- 静态标签、Actual PnL、实际成交价、FilledRisk、退出角色或任何事后字段。

计划风险的筛选必须在静态路径标签与组合重放之前执行；不得用任何结果字段反推或改变 Candidate 是否保留。

## 数据切分与比较方式

- 主评估集：2026 H1。H1 的 48.00 点阈值仅由无标签几何分布决定。
- 压力集：2026 年 7 月；只报告同一固定规则的结果，不参与阈值、变量或规则选择。
- 2025 Q4 不进入本轮研究。
- 对照：同一日期、同一 StaticPlanPortfolio 约束、全 Candidate 静态基线。
- 固定组合约束：3 手、同向最多 2 槽、15 笔/日、-$250 日损、-$500 周 Long 门、并发风险 $300、每笔 $3.60 手续费、Globex 锁定；不调整退出、仓位、日损、周门、并发或路径规则。
- 口径：Strict 和 LowerBound 分别重放；UpperBound 只保留为敏感性上界，不能用作晋级依据。

## 输出与硬门

报告必须按 H1、7 月、月、周、方向、路径输出 Candidate 数、接受/筛除数、StaticPlan Gross/Net、PF、日均、最差周、TimeStop/Ambiguous/Censored 覆盖及替代链。

已成交 Actual 诊断只使用 canonical 926 行：被筛除的 Actual 子集的累计 AccountNet 必须不为正，并报告样本数、月度与 ExitFamily；它不是收益验证，也不允许用 Actual PnL 改写静态组合结果。

以下任一项失败即为 No-go：

1. Strict 或 LowerBound 的 H1 Net 未高于同口径全 Candidate 静态基线；
2. 被筛除的 canonical Actual 子集累计 AccountNet 为正；
3. 7 月同口径 Net 低于对应静态基线；
4. 使用了任何禁用字段、产生静态/Actual PnL 混合、或遗漏 TimeStop/Ambiguous/Censored/替代链披露。

通过也不进入 Smoke。唯一允许的后续动作是独立审查：该静态候选是否有足够价值，值得设计最小、零订单的 Data Only 运行验证；该审查仍需新的设计与用户确认。

## 验证与防误用

- 离线脚本必须有回归测试：默认开关复刻静态基线；48.00 边界保留、48.00 以上筛除；禁用列触发失败；Static 与 Actual 输出分表。
- 每次运行前先复核 `docs/superpowers/specs/2026-07-30-v400-actual-calibration-static-path-design.md` 与 `项目恢复总览_继续开发指南.md` 的 V4 当前状态。
- 本设计不授权编译 DLL、清日志、修改配置、Replay、Smoke、归档或 Tag。
