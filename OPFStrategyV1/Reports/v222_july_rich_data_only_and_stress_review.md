# v2.22 July Rich Data Only 与冻结候选压力测试

## Data Only 验收

- 预注册7日：2026-07-01、07-07、07-13、07-15、07-16、07-20、07-24。
- 7个 Data Only Snapshot、2,443根Rich Bar；每个日期的 July Candidate Tape 都按 `EntryTime + EntryBar` 100%连接。
- 命中候选的 AverageVolume20、RelativeVolume20、ATR14 初始化零值均为0。
- 关联 Snapshot 的 execution_decisions、execution_trades、live_account_pnl 均为0；采集没有提交订单。

## 冻结候选的7日压力测试

候选保持H1离线定义：Primary Long ObservationConfirm 仅允许 RegimeBars>=8；Primary BreakawayFvg Short 需要质量>=88、风险>=12、同向VWAPSide通过。

| 指标 | 基线 | 候选 | 变化 |
|---|---:|---:|---:|
| 交易数 | 33 | 32 | -1 |
| Gross | +$249.00 | +$303.00 | +$54.00 |
| Net | +$130.20 | +$187.80 | +$57.60 |
| PF | 1.1175 | 1.1467 | +0.0292 |

唯一差异发生在2026-07-07：候选跳过1笔 Primary Long ObservationConfirm，日Net由`-$292.30`改善至`-$234.70`。其余六日交易身份和收益保持不变；没有新增替代交易或新的日损/并发风险问题。

## 判定

Data Only功能和连接验收通过；7日压力样本为正且没有结构性恶化，满足进入有限 Actual Smoke 的前置条件。

该结果仍只有7日、只有一次实际Rich门禁命中，不能当作最终OOS收益证明。下一步若获确认，应实现候选为单一DLL版本，清理日志后执行4至6日Actual Smoke，再由真实Replay裁决。
