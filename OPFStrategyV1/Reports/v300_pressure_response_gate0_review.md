# V3.0 Gate 0：主动成交压力—价格响应

日期：2026-07-28  
性质：零 Replay、零 DLL、零收益结论的独立信号采集可行性审计。

## 固定数据合同与代理定义

- 仅使用已通过 v2.29 数据合同的 `OnNewTrade` 原始 Buy/Sell 成交量，并按 Bar 连接同一归档的 Rich OHLC。
- DOM、撤补单、冰山和任何静态盘口字段均不使用。
- 宽松密度代理固定为：`|BuyVolume-SellVolume| / TotalVolume >= 10%`，且收盘落在与主动压力**相反**的外三分之一。Buy pressure + down close、Sell pressure + up close 才进入计数。
- 10% 是刻意宽松的可行性下限，不是待优化的 alpha 阈值。更严格的吸收/衰竭定义只会减少样本。

这一定义只能识别“压力—价格响应”。现有逐 M5 汇总不能区分主动买入先衰竭后回落与全程吸收；但若连宽松代理都不足，则没有理由先为细分语义付出采集成本。

## 预注册采集资格

| 项目 | 门槛 | 原因 |
|---|---:|---|
| H1 响应事件 | >=90 | 才能进行低自由度、按月留一的单特征研究 |
| 7月响应事件 | >=20 | 保留压力期基本反证能力 |
| 每个已审计 H1 日 | >=8 | 避免总数集中于少数行情日 |
| 已审计 7 月日 | >=5 | 同上 |

## 八日零成本审计

八个 Snapshot 的 `OnNewTrade` 均为 276 根 M5，和 Rich OHLC 的 Bar 连接 276/276，解析有效率 100%。结果如下：

| 日期 | 压力反向收盘事件 |
|---|---:|
| 2026-01-15 | 8 |
| 2026-01-26 | 9 |
| 2026-02-23 | 3 |
| 2026-03-02 | 1 |
| 2026-04-09 | 5 |
| 2026-05-11 | 1 |
| 2026-06-16 | 2 |
| 2026-07-06 | 1 |

H1 共 29 条，7 月 1 条；四项门槛全部失败。相比之下，同向压力—同向收盘有 474 条，但那是普通延续结构，已经处在 V3.0 禁止重复扫描的既有突破/延续类别，不能以新名称重开。

## 裁决

**主动成交压力—价格响应（吸收/衰竭）家族 Gate 0 no-go。**

不新增 DLL、不清日志、不冻结采集清单、不做 Data Only 或 Smoke。以当前观察密度估计，即使放大日期，也只会把稀疏样本线性放大，仍无法获得按月留一所需的稳定样本；放宽 10% 下限则会把“吸收/衰竭”改造成普通 Delta/方向研究，违反研究定义。

## 可复核输出

- 脚本：`OPFStrategyV1/Scripts/Audit-OPFV300PressureResponseGate0.py`
- 汇总：`OPFStrategyV1/Reports/v300_pressure_response_gate0/summary.json`
- 日汇总：`OPFStrategyV1/Reports/v300_pressure_response_gate0/day_summary.csv`
- 事件明细：`OPFStrategyV1/Reports/v300_pressure_response_gate0/response_events.csv`
