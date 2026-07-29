# v3.0 阶段 5：Sweep-Reclaim 新信号采集合同

## 决策边界

阶段 3A/3B 已否决所有已预注册的旧 OPF 路径内变量。阶段 5 不修改、不过滤、也不重排既有 OPF 候选；Sweep-Reclaim 是独立的新信号家族，先以 Data Only 形式采集。任何收益结论均不能替代当前 `ACTUAL_EXEC_2.47` 基线的 Replay 结论。

## 冻结的信号定义

- 时间框架：已收盘 M5 Bar；只在收盘后生成候选，绝不使用同柱未来价格。
- 回看区间：此前恰好 12 根已收盘 M5 Bar（不含决策 Bar）。
- Long：决策 Bar 的 Low 严格低于此前区间 Low，且 Close 大于或等于该区间 Low。
- Short：决策 Bar 的 High 严格高于此前区间 High，且 Close 小于或等于该区间 High。
- 最小扫单深度自然为一个价格 tick；采集不按深度、成交量、时段、Regime 或旧 OPF 路径过滤。

## 冻结的研究标签

- 计划入场：下一根 M5 Bar 的 Open；因而决定与入场严格隔离。
- 止损：Long 为扫单 Low 下方一 tick；Short 为扫单 High 上方一 tick。
- 结果标签审计范围：实际风险 2--20 点；范围外候选保留原始 CSV，但标为 `RiskOutsideAuditRange`，不进入首轮绩效统计。
- 目标：固定 1.5R；最长持有 12 根完整 M5 Bar，未触发则以第 12 根 Close 标为 `TimeStop`。
- 同一根 Bar 同时触及止损和目标，一律标为 `AmbiguousSameBar`；不猜测先后顺序，也不计入确定性收益。
- 只记录 30/60 秒逐笔 Buy/Sell/Unknown 成交量、顺序边界与预热状态；不消费 DOM，不使用既有 OPF Zone、G2 或 Regime 特征。

## 数据与晋级门

首轮复用已冻结的 24 日分层日期清单：`v230_footprint_24day_collection_plan.csv`。该清单覆盖 H1 高/中/低收益日以及 7 月压力日；它是采集集，不是收益调参集。

采集通过条件：24/24 Snapshot 均存在一份候选 CSV 和结果 CSV；候选 SignalID 唯一；每个候选恰有一个结果；Actual 订单、账户损益与保护订单文件均为零；所有确定性结果有合法 Entry/Stop/Target，且 `AmbiguousSameBar`、范围外和未完成样本单独报告。

只有采集通过后，才允许离线研究：先做 Long/Short 独立的按月留一评估，并先比较“无成交量条件”的固定规则。首轮仅允许测试两个预注册方向：扫单深度分位与 60 秒同向/反向成交量不平衡；不得与旧 OPF 信号叠加。任一方向必须在 H1 每月留一中保持正向、并在 7 月压力样本不发生结构性失效，才允许进入最小 Smoke；否则归档并更换新信号家族。
