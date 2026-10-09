# V4.0 修正合同：策略模拟器数据基础设施

日期：2026-07-29。目的不是直接增加策略规则，而是让离线组合模拟器在不重复 ATAS Replay 的前提下，准确重建冻结策略、评估所有候选替代链，并为 V4 特征提供可审计的候选级输入。

## 不可变边界

- 恢复锚保持 `opf-v3.0-full-replay-baseline-20260729`；V4 不得反向修改其策略规则或重写其历史结果。
- DOM、热图、Spoofing、撤补单语义永久不进入本合同。
- 所有采集 Profile 强制 `actualOrders=false`；只读、内存缓冲、停止时批量写盘。
- 每次 DLL 编译前必须先归档当前有效证据；编译、部署后清理活动日志并完整重启 ATAS。

## 两层数据面

### A. 深层 Market Execution Tape（四个技术校准日）

每个全市场成交逐笔记录：`SnapshotID, Sequence, TickTime, M5Bar, Price, Volume, Direction`。不把方向解释为被动流动性。

每个策略候选决策记录：稳定 `CandidateID/SignalID`、决策时间与 Bar、路径/方向、Entry Bid/Ask、计划 Entry/SL/TP、初始风险、当时组合状态及阻塞原因。候选后 36 根 M5 的 OHLC 只由全局 Market Tape 索引，不得按候选复制逐笔数据。

目标是为已执行交易校准报价锚、价格跨越顺序和保护生命周期；它不能把未执行候选的结果称为 Actual。

### B. 广层 Candidate Scenario Tape（2026 H1 + 7 月全部有效日）

对每个候选、每个被阻塞候选和每个 Execute 写一行：稳定身份、决策时可见输入、路径、报价、计划风险、`Active/G2/DailyLimit/DailyLoss/WeeklyLong/Globex/Latency` 状态、所有阻塞原因、以及关联的 Zone Context。广层不存逐 Tick，只引用 Rich M5/深层 Tape 的路径键。

Zone Context 在候选决策时冻结：ZoneID、最后 Touch 时间/序号、年龄、触时累计 Buy/Sell/Delta、Zone 内成交占比、触前顺逆推进与价格层摘要。未发生可见 Touch 时显式为空；不得事后寻找“最近 Zone”。

## 先决 Gate 0：标签与键可观测性

先只读现有 24 日 Zone Ledger、Rich Bar 和 Candidate 日志，不改 DLL。

1. 每个研究 Snapshot 的全市场 M5 时间序列必须唯一、连续；Zone Touch 必须可用 `SnapshotID + Time + Bar` 一对一连接。
2. 主标签固定为 Touch 收盘后 6 根全市场 M5 的方向性响应；3/12 根只作敏感性报告，不参与选择。
3. Zone 终态是独立标签，绝不截断全市场未来 OHLC；仅 Snapshot 最后 6 根 Bar 可自然截尾。
4. 报告每个窗口覆盖率、终态率、Touch→Candidate 时间差和候选覆盖率。任何连接缺失、重复或未来字段均为 0 才能进入采集。

## 深层四日技术 Gate

日期在合同实施前一次性冻结，覆盖 Long/Short、普通/ZoneBirth、主槽/G2、至少一个日损或日上限阻塞日。

必须同时满足：

- 原始逐笔量与 M5 Volume 对账处于已验证误差范围；Sequence 单调且无重复键。
- 深层 Candidate Scenario 与同日 Risk 按 `SignalID + DecisionTime + DecisionBar + ResearchPath` 精确一一对应；CandidateID 唯一，计划价格、报价、风险、市场序列和冻结 Zone 边界有效。
- `actualOrders=false`、订单/账户成交/`ENTRY_SEND` 全为 0，且 Tick、Scenario、ConfigSnapshot 均存在。
- 广层四日重复采集后，按同一稳定键比较深层/广层候选身份、计划 Entry/SL/TP/Risk、Bid/Ask 与冻结 Zone Context；除 Profile/Snapshot 元数据外必须完全一致。广层不得输出 Tick 或 Zone Ledger 全量 CSV。

失败只允许修数据链路或模拟器；不得扩大日期、不得研究特征、不得 Smoke。

## 广层采集与模拟器验收

四日通过后才采集 2026 H1 + 7 月全部有效日。离线引擎必须拆为：

`MarketTape`（路径与报价） -> `DecisionTape`（所有候选及状态） -> `PortfolioEngine`（槽位、15笔、日损、周 Long 门、Globex、手续费与替代链）。

已成交交易如需回归，仍以冻结 Actual Outcome 为权威锚；广层 Data Only 中所有候选均使用 MarketTape/Rich M5 推演，必须显式标为反事实。研究臂首轮只允许一个候选选择开关，禁止同时改退出、手数、日损或并发。

## V4 特征研究晋级

只有模拟器回归通过后才研究 Zone 行为。四项首轮特征固定为方向 Delta、Zone 成交占比、触前顺向推进、触前逆向穿透。评价顺序为：候选级数据覆盖 -> H1 LOMO -> 7月压力 -> PortfolioEngine 组合结果。任何通过仅允许进入 Data Only 连接验证，不能直接进入 Smoke。
