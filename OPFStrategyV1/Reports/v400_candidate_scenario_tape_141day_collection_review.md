# V4 Candidate Scenario Tape 141 日全量采集验收

日期：2026-07-29

## 范围

- 冻结样本：141 个有效 Replay Snapshot。
- 四日技术 Gate 复用：2026-01-16、2026-02-11、2026-04-13、2026-07-07。
- 新采：137 日；活动日志已归档至 `opf_v400_candidate_scenario_tape_137day_collection_passed_137snapshots_20260729`。
- 运行档案：`V400_CANDIDATE_SCENARIO_TAPE_DATA_ONLY / CandidateScenarioTapeDataOnly / OPF_RESEARCH_2.33 / ACTUAL_EXEC_2.50`。

## 137 日验收

| 项目 | 结果 |
|---|---:|
| Snapshot | 137 / 137 通过 |
| 执行入口 Scenario | 33,585 |
| Scenario 缺 Risk 连接 | 0 |
| 无效 Scenario | 0 |
| 真实订单文件 / ENTRY_SEND | 0 / 0 |
| 禁止的 Tick、Zone 全量输出 | 0 |
| Risk 行但未抵达执行入口 | 39 |

39 条未进入 Scenario 的 Risk 行仅为 `BreakawayRetest12Research`（19）与 `BreakawayRetest18Research`（20）。它们不属于执行入口候选，保留作审计而不进入 Portfolio 输入。

## 验证器修正

全量样本暴露两项四日 Gate 未覆盖的验证器错误，采集 CSV 本身不需要重跑：

1. Risk 日志包含研究专用途径，故仅要求每条 Scenario 都有对应 Risk；反向额外 Risk 单列审计。
2. `MarketSequence` 与 `ZoneLastTouchMarketSequence` 分属执行带与 Zone 带的独立本地计数器，不比较数值大小。两者均继续为 `ReplayLocalOnly`，不得进入跨 Replay 筛选或收益结论。

已为两项边界补充测试，并以原始 137 日日志复验通过。

## 冻结 Portfolio 输入

`v400_portfolio_candidate_tape_141day.csv` 由深层四日校准带、广层四日 Gate 和 137 日归档共同构成：

| 项目 | 结果 |
|---|---:|
| Portfolio Candidate | 34,771 |
| 唯一 CandidateID | 34,771 |
| Snapshot | 141 |
| Long / Short | 18,560 / 16,211 |
| 深层校准身份匹配 | 1,186 / 1,186 |
| 跨 Replay 动态差异 | 1,186 / 1,186（仅审计） |

所有行固定标记为 `OutcomeSource=CounterfactualPending`、`PricePathSource=RichM5Required`、`DynamicAuditState=ReplayLocalOnly`。这只是离线组合输入，尚无收益标签，不构成策略结论或 Smoke 晋级依据。
