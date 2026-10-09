# V4 Candidate Scenario Tape 四日 Gate 与离线连接复核

日期：2026-07-29

## 裁决

广层 Candidate Scenario Tape 的数据链路与稳定组合输入通过；瞬时 Replay 局部字段不晋级为跨 Replay 的特征或组合决策输入。

## 广层自检

四个 Snapshot 均为 `V400_CANDIDATE_SCENARIO_TAPE_DATA_ONLY / CandidateScenarioTapeDataOnly / OPF_RESEARCH_2.33 / ACTUAL_EXEC_2.50`，`EnableActualOrders=false`。

| Snapshot | Candidate Scenario |
|---|---:|
| OPF-20260729-095722 | 233 |
| OPF-20260729-095929 | 326 |
| OPF-20260729-100156 | 342 |
| OPF-20260729-100354 | 285 |

总计 1,186 个场景。四日均满足：CandidateID 唯一、Scenario 与 Risk 按 `SignalID + Time + Bar + ResearchPath` 一一对应、计划价格/风险/报价/市场序列/ZoneID 有效、订单文件/账户成交/`ENTRY_SEND` 为零，且没有 `market_execution_ticks.csv`、Zone Event/Bar/PriceLevel 全量输出。

## 深层/广层交叉结果

按稳定键连接后：深层 1,186、广层 1,186、身份匹配 1,186；以下字段 100% 一致：

- CandidateID、SignalID、DecisionTime、DecisionBar、路径、方向、SetupType；
- Planned Entry/SL/TP/Risk/TargetR；
- 日/周状态、Active/G2、Globex、开盘禁单、延迟、日限额与日损状态；
- ZoneID、类型、方向、边界、出生时间/Bar、TouchOrdinal 与 LastTouch 时间/Bar。

跨 Replay 不可视为精确回归锚的字段：

| 字段 | 差异候选数 | 结论 |
|---|---:|---|
| MarketSequence | 1,186 | 每次 Replay 的逐笔回调本地序号；只允许运行内排序/审计。 |
| ZoneLastTouchMarketSequence | 719 | 依赖本地序号；只允许运行内审计。 |
| EntryBid / EntryAsk | 327 / 335 | OnCalculate 瞬时 BBO，跨运行存在回调时序抖动；不作为第一版模型入场价。 |
| Zone Cumulative Buy/Sell/Delta | 98 / 70 / 130 | 少量同 Bar 逐笔边界差异；不作为跨 Replay 微观特征。 |
| Zone In-Zone Buy/Sell/Delta | 9 / 4 / 5 | 同上，数值差异最大仅 1。 |

这不是候选流或交易几何漂移：稳定字段完全一致。它界定了 ATAS Historical Replay 的正确使用边界——瞬时回调观测可留作单次运行审计，不能被伪装成可重复的微观特征。

## 离线组合连接

`Prepare-OPFV400PortfolioTape.py` 已将广层带输出为：

- `v400_portfolio_candidate_tape_4day.csv`
- `v400_portfolio_candidate_tape_4day_summary.csv`

其结果为 `DeepCandidates=1186, BroadCandidates=1186, StableMatches=1186`。每行保留 `SessionID`、稳定组合状态和计划交易几何；显式标记 `OutcomeSource=CounterfactualPending`、`PricePathSource=RichM5Required`、`DynamicAuditState=ReplayLocalOnly`。因此它已经是 PortfolioEngine 的规范输入，但四日数据只用于技术连接，不能作为收益研究或策略晋级样本。

## 后续边界

可扩大为 2026 H1+7 月广层采集；离线第一版只允许使用稳定字段及 Rich M5 路径，动态审计字段不进入候选筛选、入场价、收益或风险结论。若未来要重新研究即时 BBO/Zone Delta，必须先以停止时确定性压缩重建候选时点上下文，再重新跑独立技术 Gate。
