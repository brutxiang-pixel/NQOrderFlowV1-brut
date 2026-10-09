# V4 Rich M5 保守反事实结果设计（已被校准优先规格替代）

> 已于 2026-07-30 被 `2026-07-30-v400-actual-calibration-static-path-design.md` 替代。不得按本文件直接实现 PortfolioEngine。

## 目标

基于冻结的 141 日 Candidate Scenario Tape 与同 Snapshot 的 Rich M5 OHLC，在不再运行 ATAS Replay 的情况下，生成可审计的候选级保守结果标签；该结果只用于离线筛选和组合仿真，不宣称复刻 Actual 订单收益。

## 不变边界

- 不修改 DLL、ATAS 配置、策略规则、活动日志或 `opf-v3.0-full-replay-baseline-20260729`。
- 输入只能读取四日深层 Market Execution Tape、四日广层 Gate、137 日广层归档和已生成的 141 日 Portfolio Candidate Tape。
- `EntryBid`、`EntryAsk`、`MarketSequence`、`ZoneLastTouchMarketSequence` 与 Zone 累计成交量保持 `ReplayLocalOnly`；不得作为跨 Replay 选择特征、入场价或收益结论。
- 输出的经济含义固定为 `CounterfactualStaticPlan`，不得与 Actual Outcome、可部署策略绩效或 Smoke 晋级混用。

## 输入与键

1. `v400_portfolio_candidate_tape_141day.csv`：每行一个唯一 CandidateID，读取 `SnapshotID`、`DecisionTime`、`DecisionBar`、`Side`、`ResearchPath`、`PlannedEntry`、`PlannedStop`、`PlannedTarget` 和 `PlannedRiskPoints`。
2. 四日 Gate 和 137 日归档中的 `*_rich_bar_features.csv`：按 `SnapshotID + Time + Bar` 唯一索引，读取 OHLC。
3. 四日深层归档中的 `*_market_execution_ticks.csv`：只用于校准 Rich M5 的双触判定；按 `SnapshotID + Sequence` 索引。

输入 Gate：CandidateID 唯一；每一条 Candidate 的决策 M5 Bar 唯一存在；每条路径在决策后最多 12 根 M5 的索引连续。任一 Gate 失败应停止输出该 Snapshot，不以邻近 Bar 或其他 Snapshot 补值。

## Rich M5 标签器

### 固定入场模型

- 入场价固定为 `PlannedEntry`，入场时刻定义为 Decision Bar 收盘后。
- 仅扫描 `Bar > DecisionBar` 的后续 M5；Decision Bar 本身的高低价不参与止盈/止损判定，避免使用决策发生前的价格。
- 固定最长持仓为 12 根后续 M5 Bar；第 12 根仍未先触及 SL 或 TP 时，以该 Bar `Close` 产生 `TimeStop`。

### 固定结果模型

Long：`Low <= PlannedStop` 为 Stop，`High >= PlannedTarget` 为 Target。Short 方向相反。

- 单根只触 Stop：`SL`；只触 Target：`TP`。
- 同一根同时触 Stop 与 Target：`Ambiguous`，`ExitPrice` 和 PnL 留空，不进入收益汇总或 PortfolioEngine；它不是 Stop 或 Target 的任意一方。
- 不足 12 根后续 M5：`Censored`，同样不进入收益汇总或 PortfolioEngine。
- `TimeStop` 使用第 12 根收盘价。MFE/MAE 可报告，但不得替代上述终态。

每条标签输出 CandidateID、SnapshotID、入口几何、扫描首末 Bar、Outcome、Outcome Bar/Time、ExitPrice、持仓 Bar 数、MFE/MAE、`OutcomeSource=RichM5Counterfactual`、`PricePathSource=RichM5OHLC` 和 `Eligibility=Eligible|Ambiguous|Censored`。

## 四日深层 Tick 校准

对四日深层 Candidate，使用 `Sequence > MarketSequence` 的逐 Tick Last Price 路径，执行相同 Entry/SL/TP/12 根 M5 上限规则。

- Tick 路径仅用于报告 Rich M5 `Ambiguous` 标签中实际先触边界的构成，以及 Rich M5 与 Tick 的终态混淆矩阵。
- Tick 结果不外推给全量候选，不覆盖或重写 Rich M5 的 `Ambiguous`。
- 若 Tick 索引、Candidate Sequence 或相应 M5 窗口不可唯一连接，校准行标为 `CalibrationCensored`。

## 保守 Portfolio 输入

PortfolioEngine 第一版只接收 `Eligibility=Eligible` 的静态计划标签，以固定风险几何计算静态 PnL，再应用冻结的单/双槽、15 笔、日损、周 Long 门、Globex 和手续费约束。

它必须单独标记 `StaticPlanPortfolio`，并与 Actual 基线收益分表；只允许评估单一候选选择开关，禁止同时改变退出、手数、日损或并发。`Ambiguous` 与 `Censored` 数量、方向、路径和月份必须随结果一起报告。

## 验收与测试

1. 单元测试：Long/Short 的 SL、TP、TimeStop、Ambiguous、Censored；Decision Bar 排除；连续性与重复键失败。
2. 单元测试：同一 Candidate 的四日 Tick 校准只扫描 Decision Sequence 之后的 Tick。
3. 集成验收：141 个 Snapshot、34,771 CandidateID 唯一；每行恰一种终态；无 Eligible 行使用动态 ReplayLocalOnly 字段。
4. 校准报告：四日 Tick/M5 的可校准覆盖、终态混淆矩阵和 Rich M5 Ambiguous 占比。
5. 只有上述数据 Gate 通过，才允许运行静态 PortfolioEngine；Portfolio 结果为研究筛选，不进入 Smoke。

## 非目标

- 不预测真实成交、滑点、盘口队列、OCO 事件顺序或动态保护止损。
- 不用四日 Tick 训练或估计全量候选的收益。
- 不以静态标签取代 Actual 回归、实盘 Gate 或策略绩效声明。
