# V4 Actual 校准优先与静态路径研究设计

## 目标

先建立冻结 V3 Actual Outcome 与 V4 Candidate Scenario 的精确连接，量化计划价格、真实成交和动态退出之间的差异；随后生成仅供筛选研究使用的 Rich M5 静态路径标签。静态结果不得被称为冻结策略基线、策略收益或 Smoke 晋级依据。

## 已验证前提

- 冻结 Actual 归档有 927 笔 `IsAbnormalExecution=False` 输入，其中 1 笔为完全相同 Replay 副本；canonical Actual 为 926 笔。
- 141 日 Candidate Scenario Tape 包含 34,771 条唯一候选。
- 按 `SignalID + ResearchPath`，926 笔 canonical Actual 均可连接 Candidate；Candidate TargetR 与 Actual PlannedTargetR 均一致。
- Candidate 计划风险与 Actual 计划风险一致 881/926，而 Actual 计划风险与 FilledRisk 一致 132/926；计划入场价只与 139 笔 Actual 成交价相同。因此计划几何不能直接代替 Actual 生命周期。

## 不变边界

- 不修改 DLL、ATAS 配置、策略规则、活动日志、冻结基线 Tag 或历史 Actual 结果。
- `EntryBid`、`EntryAsk`、`MarketSequence`、`ZoneLastTouchMarketSequence` 与 Zone 累计成交量保持 `ReplayLocalOnly`，不得用于跨 Replay 收益、筛选或入场价格。
- Actual 基线与静态反事实永久分表；任何图表、汇总和结论均须显示数据来源。
- 当前 24 日 Zone 生命周期研究已 No-go；不得借本阶段重新扫描其阈值或将课程“燃料/证伪/吸收”等概念事后伪造为全量字段。

## 阶段 4C：Actual—Candidate 连接与校准

### 输入

1. 冻结 Actual 归档：`opf_v300_full_actual_replay_202601_to_20260728_20260729` 的 `execution_trades.csv`、账户 PnL 与执行事件。
2. `v400_portfolio_candidate_tape_141day.csv`。

### 键与输出

- 主键：`SignalID + ResearchPath`；连接后输出 CandidateID、Actual TradeID、Actual Entry/Exit、FilledRisk、ActualMFE/MAE、ExitRole、Raw/Normalized PnL、账户影响及所有计划字段。
- 校准报告必须输出：连接覆盖、计划 Entry 与 Actual Entry 的差值分布、计划风险与 FilledRisk 的差值分布、路径×方向×目标 R 分布、动态退出角色分布和账户 PnL 连接缺失。
- Data Only Tape 中的日损、槽位、账户状态均不可当作 Actual 状态；Actual 基线状态只来自冻结 Actual 归档。

### Gate

- 所有 926 笔 canonical Actual 都必须恰好连接一条 Candidate；完全相同 Replay 副本仅作审计排除，任何其它重复、缺失或跨 Snapshot 键冲突为 0。
- Actual Outcome 字段不得被静态路径标签覆盖或反向写入。
- 若上述 Gate 不通过，停止在连接诊断，不进入 Rich M5 标签或 PortfolioEngine。

## 阶段 4D：Rich M5 静态路径标签

### 固定入场与窗口

- 使用 Candidate 的 `PlannedEntry/PlannedStop/PlannedTarget`；入场定义为 Decision Bar 收盘后。
- 只扫描 `Bar > DecisionBar` 的连续后续 M5，最多 12 根；Decision Bar 本身不参与触价判定。
- 不足 12 根后续 M5 标为 `Censored`，不编造退出价或 PnL。

### 终态与三口径

- 单柱只触 Stop：`SL`；只触 Target：`TP`；12 根均未触及：`TimeStop`，以第 12 根 Close 结算。
- 同一 M5 同时触及 Stop 与 Target：基础标签 `Ambiguous`。
- 每条 `Ambiguous` 同时产生三个研究口径：
  - `Strict`: 不计入静态收益，单列覆盖缺口；
  - `LowerBound`: 按 Stop 结算；
  - `UpperBound`: 按 Target 结算，仅作敏感性上界，绝不用于晋级。
- `Censored` 不进入任一收益口径；必须按月、方向和路径报告数量。

### 四日 Tick 校准

- 四日深层带按 `Sequence > Candidate.MarketSequence` 扫描 Tick Last Price，以相同计划几何和 12 根 M5 上限结算。
- 输出 Tick 与 Rich M5 的终态混淆矩阵、Rich M5 Ambiguous 的实际先触构成和不可校准覆盖。
- Tick 结果只验证 M5 误差带；不得将 Tick 的先触比例外推至全量候选，不得改写全量 `Ambiguous`。

## 阶段 4E：StaticPlan Portfolio 研究

- 仅在 4C 与 4D Gate 通过后运行。
- 输入是静态路径标签，不是 Actual Outcome；输出名称固定为 `StaticPlanPortfolio`。
- 对 Strict、LowerBound、UpperBound 分别执行冻结的单/双槽、15 笔、日损、周 Long 门、Globex 与手续费约束，并报告候选替代链差异。
- 所有结果与冻结 Actual 927 笔基线分表。StaticPlan 只能回答候选选择规则的相对排序，不可作为策略收益、实盘资格或 Smoke 通过证据。
- 首轮只允许一个候选选择开关；禁止同时修改退出、手数、日损、并发或路径规则。

## 验收与测试

1. 4C 单元测试：精确键匹配、重复键拒绝、Actual 状态与 Data Only 状态隔离。
2. 4D 单元测试：Long/Short 的 SL、TP、TimeStop、Ambiguous、Censored，Decision Bar 排除和连续窗口缺失。
3. 4D 单元测试：Tick 校准仅扫描 Candidate Decision Sequence 后的 Tick。
4. 4E 单元测试：三口径的 Ambiguous 处理不同，且 Censored 永不产生 PnL。
5. 集成 Gate：926 canonical Actual 全连接；34,771 CandidateID 唯一；所有静态标签恰有一个基础终态；动态 ReplayLocalOnly 字段不出现在收益计算输入中。

## 后续决策

4E 完成后才评审是否继续 V4 alpha。当前全量带稳定支持的主要是路径、方向、计划几何和 Zone 距离；课程中的燃料、证伪、吸收、B/P Delta 与两次速度减弱仍不具备全量无泄漏数据。若要研究它们，必须另行授权一个高成本、独立的全量生命周期/逐笔采集项目。

## 实施校正记录（2026-07-30）

- 冻结归档的 927 条正常 Actual 输入中，发现 1 条 2026-07-06 的完全相同 Replay 副本；4C 权威基数修正为 926，且 `SignalID + ResearchPath` 为 926/926 严格连接。
- 该校正不改变冻结 Actual 基线；重复副本保留在原归档中，连接器仅将其作为审计排除项处理。

## 4F 实施结果（2026-07-30）

- 926/926 canonical Actual 均连接静态标签。确定性静态 TP/SL 为 749 笔：静态 SL 的 Actual 净收益为负 465/465，静态 TP 的 Actual 净收益为正 279/284。
- 该结果只证明已成交样本的终态方向诊断，不证明静态 PnL 或组合替代链。完整门与字段定义见 `OPFStrategyV1/Reports/v400_static_actual_calibration_contract.md`。
