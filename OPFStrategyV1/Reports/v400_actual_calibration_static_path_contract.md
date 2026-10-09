# V4 Actual 校准与静态路径研究合同

日期：2026-07-30。此合同只覆盖 V4 阶段 4C、4D、4E 的离线研究；不改变冻结策略、DLL、ATAS 配置或活动日志。

## 证据边界

- 冻结 Actual 权威来源：`opf-v3.0-full-replay-baseline-20260729` 的归档 `execution_trades.csv` 与 `live_account_pnl.csv`。
- Candidate 来源：`v400_portfolio_candidate_tape_141day.csv`。
- Rich M5 只使用已有 137 日 `rich_bar_features.csv` 加四日深层 `v400_market_execution_tape_4day_m5.csv`。
- `EntryBid`、`EntryAsk`、`MarketSequence`、Zone 的局部累计量不进入全量 M5 收益计算。深层 Tick 仅用于四日误差校准。

## 4C：Actual—Candidate 校准

- 键：`SignalID + ResearchPath`。
- 冻结归档输入有 927 条正常 Actual；其中 2026-07-06 的完全相同 Replay 副本出现两次，按除 `SnapshotID` 外的完整 Actual/账户字段完全相同而去重 1 条。因此权威基数为 926，不是 927。
- Gate：926/926 Candidate 连接、账户 PnL 缺失 0、Candidate 缺失 0。任何非完全相同的重复键均为失败。
- 输出中 `Actual*` 字段永远来自冻结归档，`Planned*` 字段永远来自 Candidate，不相互覆盖。

## 4D：Rich M5 静态终态

- 入场几何：Candidate 的 `PlannedEntry / PlannedStop / PlannedTarget`。
- 窗口：仅 Decision Bar 后连续 12 根 M5；Decision Bar 本身不参与。
- 基础终态唯一且只能为 `SL`、`TP`、`TimeStop`、`Ambiguous`、`Censored`。
- `Ambiguous`：Strict 不计 PnL；LowerBound 以 SL；UpperBound 以 TP。`Censored` 在三种口径均不计 PnL。
- 四日 Tick 校准以深层 Candidate 的本地 `MarketSequence` 后的逐笔 Last Price 扫描，结果仅生成混淆矩阵，不覆盖全量 M5 标签，不把局部 Sequence 外推到其它 Replay。

## 4E：StaticPlanPortfolio

- 三种口径独立运行：Strict、LowerBound、UpperBound；不得合并或与 Actual 基线同表。
- 固定研究约束：3 手、同向最多 2 槽、15 笔/日、-$250 日损、-$500 周 Long 门、每笔 $3.60 手续费。
- 首轮只运行冻结的全 Candidate 流，不启用任何入场筛选开关；禁止同时优化退出、手数、日损、并发或路径。
- 所有输出都标记 `StaticPlanPortfolio / Counterfactual`，不得作为 Replay 收益、Smoke 通过、实盘资格或策略 PnL。

## 可复现命令

```powershell
$py = 'C:\Users\Administrator\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe'
$actual = "$env:APPDATA\ATAS\StrategyLogs\OPFStrategyV1_Archive\opf_v300_full_actual_replay_202601_to_20260728_20260729\OPFStrategyV1"
& $py OPFStrategyV1\Scripts\Analyze-OPFV400ActualCalibration.py --actual-root $actual --candidate-csv OPFStrategyV1\Reports\v400_portfolio_candidate_tape_141day.csv --output-dir OPFStrategyV1\Reports\v400_actual_calibration
& $py OPFStrategyV1\Scripts\Build-OPFV400StaticPathLabels.py --candidate-csv OPFStrategyV1\Reports\v400_portfolio_candidate_tape_141day.csv --rich-root "$env:APPDATA\ATAS\StrategyLogs\OPFStrategyV1" --deep-m5-csv OPFStrategyV1\Reports\v400_market_execution_tape_4day_m5.csv --deep-root OPFStrategyV1\Evidence\opf_v400_market_execution_tape_4day_gate_passed_4snapshots_20260729 --output-dir OPFStrategyV1\Reports\v400_static_path
& $py OPFStrategyV1\Scripts\Analyze-OPFV400StaticPlanPortfolio.py --candidate-csv OPFStrategyV1\Reports\v400_portfolio_candidate_tape_141day.csv --label-csv OPFStrategyV1\Reports\v400_static_path\static_path_labels.csv --output-dir OPFStrategyV1\Reports\v400_static_plan_portfolio
```

## 当前裁决

4C-R 已将 Candidate 计划风险、Actual 计划风险与 FilledRisk 分离；4F 已验证确定性静态 TP/SL 对已成交 Actual 的方向诊断能力。详见 `v400_static_actual_calibration_contract.md`。下一步只允许评估一个预注册单变量 Candidate 筛选器，并同时满足该合同的五项门；不得直接进入 Smoke。
