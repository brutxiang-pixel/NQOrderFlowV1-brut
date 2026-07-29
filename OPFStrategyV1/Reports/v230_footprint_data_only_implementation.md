# v2.30 阶段 2 最小 Footprint Data Only 实现

日期：2026-07-27  
范围：v3.0 阶段 2 的只读采集，不引入 alpha、订单或风控改动。

## 冻结边界

- Research Schema 升级为 `OPF_RESEARCH_2.30`；Actual 执行配置保持 `ACTUAL_EXEC_2.47`。
- 新增默认关闭的面板项 `Footprint Data Collection Only`。开启后从启动开始强制关闭 Actual orders。
- 唯一逐笔输入为阶段 1B 已验证的 `OnNewTrade`；不读取 `OnNewTrades`，不读取任何 DOM 回调或快照，不对 Unknown 方向作推断。
- 所有逐笔和候选特征只在内存中累计；停止策略时由 `ResearchLogger` 批量写盘。采集逻辑位于候选已经生成之后，不参与入场、退出、风险、订单或 G2 状态机。

## 输出

每个 Snapshot 输出 `*_footprint_candidate_features.csv`。每行固定：

- SignalID、决策时间/Bar、Side、ResearchPath、冻结 Zone 边界；
- `TickSequenceBoundary` 与当时最后可见 Tick 时间，作为严格时间可得性边界；
- `Tick60WindowComplete` 与 `FootprintHistory15mComplete`；启动后未满 60 秒或 15 分钟的行保留审计但禁止进入特征研究；
- 最近 30/60 秒原始 Buy/Sell/Unknown 成交量；
- 最近一次已观察 ZoneTouch 至决策的 Delta、价格变化、Delta 斜率与价格-Delta 一致/背离标记；
- 决策前 3 根已收盘 M5 的 Footprint POC 及两段 POC 迁移；
- Zone 内/外成交量、价格层覆盖率和最低已观测价格层成交量。

`Lane` 固定为 `ResearchCandidate`。Data Only 中不存在真实主槽保护或第二槽状态，因此不得把任何行标为“已进入 G2”。后续 G2 研究只能把该表按 SignalID、EntryTime、EntryBar 和 ResearchPath 与已归档的 Actual 基线执行证据连接；连接失败必须显式报告，不得补猜。

## 验收合同

首次仅做两个预注册日的 Data Only 定点回归：

1. ConfigSnapshot 为 `OPF_RESEARCH_2.30 / ACTUAL_EXEC_2.47`，research.log 含 `FOOTPRINT_DATA_COLLECTION_ONLY enabled=true actualOrders=false`；HUD 显示 `ActualExec: OFF dataOnly=True`。
2. 每个有效 Snapshot 存在且仅存在一个 Footprint CSV；至少一个候选行的 `TickSequenceBoundary>0`，且所有 `ReferenceTickTime` 不晚于该行可见的逐笔序列边界。任何预热未完成行不得参与连接或筛选。
3. 对每行：`BuyVolume + SellVolume + UnknownVolume` 与对应窗口逐笔量一致；POC 仅引用决策前已收盘 M5；ZoneTouch 缺失时必须显式为 `NoObservedTouch`，不得用零伪装为观测值。
4. 全部 Actual 订单、execution_trades、live_account_pnl 均为 0；不出现未清理保护、孤儿仓位、异常隔离或订单生命周期危险事件。
5. 同日控制组与采集组的候选流只允许落在 Historical Replay 已知抖动范围；若超出，先诊断采集开销，禁止进入阶段 3。

本实现只建立可审计数据集，尚未产生任何收益结论或 G2 门禁候选。
