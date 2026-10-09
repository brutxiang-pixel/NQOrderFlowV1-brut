# V4 广层 Candidate Scenario Tape 合同

日期：2026-07-29

## 目的与边界

本模式是广层 H1+7 月采集器，只为离线 PortfolioEngine 提供候选、决策报价、风险和冻结 Zone Context。它不产生任何策略结论，也不改动 Actual 入场、退出、风控、仓位、并发或订单生命周期。

它与四日 `MarketExecutionTapeDataOnly` 的职责严格分离：四日深层模式保留全市场 Tick，作为价格路径和事件顺序的校准带；广层模式不写全市场 Tick，不写 Zone 行为事件、逐 Bar 和价格层 CSV。

## 唯一运行档案

`V400_CANDIDATE_SCENARIO_TAPE_DATA_ONLY / CandidateScenarioTapeDataOnly / OPF_RESEARCH_2.33 / ACTUAL_EXEC_2.50`

配置文件：`OPFStrategyV1/Configs/OPFStrategyV1_v400_candidate_scenario_tape_data_only.json`。

启动时两层强制零订单：档案中的 `EnableActualOrders=false`，以及 `CandidateScenarioTapeDataOnly` 在 `ActualOrdersEnabled` 中永久否决。用户不得手动设置策略参数；每次运行仅部署该 JSON。

## 数据合同

1. 每个抵达执行入口的候选均保留到 `market_execution_scenarios.csv`，并在订单早退前冻结候选 ID、计划 Entry/SL/TP/Risk、Bid/Ask、市场序列、日/周风险状态、槽位占用及关联 Zone Context。
2. `OnNewTrade` 仍在内存累计 Zone 的 Buy/Sell/Delta、Touch 和序列边界；候选仅复制当时值，绝不保留随后的 Zone State 引用。
3. 只允许必要的标准研究日志、ConfigSnapshot、`risk_evaluations.csv` 和 `market_execution_scenarios.csv`。禁止生成 `market_execution_ticks.csv`、`zone_behavior_events.csv`、`zone_behavior_bars.csv`、`zone_behavior_price_levels.csv`。
4. 所有候选终态固定为 `DataOnlyObserved / ActualOrdersDisabled`，不得被解释为策略门禁。

## 四日技术 Gate

先在 `2026-01-16,2026-02-11,2026-04-13,2026-07-07` 各运行一个完整 Snapshot。验收器 `Validate-OPFV400CandidateScenarioTape.py` 必须同时确认：

- Profile、Research/Actual 版本和零订单设置一致；
- CandidateID 唯一，Scenario 与 Risk 按 `SignalID + Time + Bar + ResearchPath` 一一对应；
- 计划价格、风险、报价、市场序列、ZoneID 与冻结 Zone 边界均有效；
- 不存在真实订单、账户成交、`ENTRY_SEND`、全市场 Tick 或 Zone 全量输出文件；
- 广层与深层样本只比较字段定义和候选身份，不把四日技术样本作为收益证据。

通过后才允许扩展到 H1+7 月广层采集；失败仅修采集/验证链路，不改 Actual 策略。
