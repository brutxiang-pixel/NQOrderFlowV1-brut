# V4 Market Execution Tape 四日技术 Gate 复核

日期：2026-07-29

运行档案：`V400_MARKET_EXECUTION_TAPE_4DAY_DATA_ONLY / MarketExecutionTapeDataOnly / OPF_RESEARCH_2.33 / ACTUAL_EXEC_2.50`。

## 裁决：通过

四个预注册 Replay Session（`2026-01-16, 2026-02-11, 2026-04-13, 2026-07-07`）均使用同一 Profile，`actualOrders=false`，没有订单文件、账户成交文件或 `ENTRY_SEND`。

| Snapshot | Tick | M5 | Scenario | Tick 序列错误 | Candidate 重复 | Risk 覆盖缺失 | Tick/Rich 量偏差 |
|---|---:|---:|---:|---:|---:|---:|---:|
| OPF-20260729-090912 | 1,350,497 | 276 | 233 | 0 | 0 | 0 | 0.008293% |
| OPF-20260729-091122 | 1,862,186 | 276 | 326 | 0 | 0 | 0 | 0.006099% |
| OPF-20260729-091340 | 1,478,707 | 276 | 342 | 0 | 0 | 0 | 0.070896% |
| OPF-20260729-091546 | 2,912,483 | 276 | 285 | 0 | 0 | 0 | 0.002048% |

合计 7,603,873 Tick、1,186 个唯一 Candidate Scenario。全部 Scenario 为 `DataOnlyObserved / ActualOrdersDisabled`；计划 Entry/SL/TP/Risk、Bid/Ask、Tick Sequence、ZoneID 均有效。719 个候选已有真实 Last-Touch 上下文；其余显式为空。所有已知 Zone Last-Touch Sequence 均不晚于候选的 MarketSequence，未发现未来数据泄漏。

每 Snapshot 有一个开始前的 Rich warmup M5 没有 Tick；无 Session 中段缺失。该边界差异已单独计数，且四日 Tick/Rich 可对账量偏差均低于既有 `0.20%` 容忍带。

## 可复现离线输入

- `v400_market_execution_tape_4day_gate_summary.csv`：Gate 摘要。
- `v400_market_execution_tape_4day_m5.csv`：从原始 Tick 带重建的统一 M5 MarketTape，含 OHLC、Buy/Sell/Unknown Volume 与 Sequence 边界。
- 原始 `*_market_execution_scenarios.csv`：DecisionTape 输入，按 `SignalID + DecisionTime + DecisionBar + ResearchPath` 与所有 `risk_evaluations.csv` 严格一对一连接。
- 验收器：`OPFStrategyV1/Scripts/Validate-OPFV400MarketExecutionTape.py`；它同时输出 Gate 摘要与规范化 M5 MarketTape。

因此四日深层带已可作为后续未执行候选的价格路径/跨越顺序/报价锚校准样本。它不替代广层 H1+7月 Candidate Scenario Tape，也不作收益或 alpha 结论。
