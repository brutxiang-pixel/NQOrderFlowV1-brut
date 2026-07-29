# v2.23 Rich Entry 六 Snapshot 校准复核

日期：2026-07-27  
结论：**v2.23 新增入场层与 Actual 一致；可作为有限分层 Replay 的前置，但不构成 H1 收益结论。**

## 校准范围

- 证据：`opf_v2.23_rich_entry_actual_smoke_functional_passed_6snapshots_20260727` 的 6 个 Snapshot。
- 独立特征源：冻结的 v2.02 H1 Rich Bar 归档；7 月缺口由冻结的 v2.22 July Data Only Rich Bar 归档补齐。
- 不使用 `execution_decisions` 的门禁结果反推 Rich 特征；门禁输入从冻结 Rich Bar 的 `RegimeBars`、Bear `VWAPSide` 组件、以及同一候选的质量/风险字段交叉核对。

## 入场门禁复核

| 项目 | 结果 |
|---|---:|
| v2.23 门禁拒绝 | 13 |
| 冻结 Rich Bar 可独立连接 | 13 / 13 |
| Long OC `RegimeBars < 8` | 11 / 11 与记录原因一致 |
| Short Breakaway Rich 拒绝 | 2 / 2 与质量、风险、Bear `VWAPSide` 一致 |

两笔 Short Breakaway 分别为：

- `20260320-0955...BRK`：质量 85 < 88、风险 8.25 < 12、Bear `VWAPSide=true`。
- `20260320-1010...BRK`：质量 88、风险 17.75、Bear `VWAPSide=false`。

因此没有发生 Rich 特征错连、未来数据连接或目标路径外误伤。

## Actual 身份、报价与占用

| 项目 | 结果 |
|---|---:|
| 唯一 `Execute` TradeID | 62 |
| 完整实际交易 | 58 |
| `ENTRY_SEND -> PreflightAborted` | 4 |
| 唯一 Execute 的其余未解释项 | 0 |
| 实际交易与 Decision Tape 原始 TradeID 连接 | 58 / 58 |
| 同侧 Bid/Ask 预检报价与 Tape 完全一致 | 58 / 58 |
| 最大同时持仓 / 同方向最大持仓 | 2 / 2 |
| 缺失终态 / `IsAbnormalExecution=true` | 0 / 0 |

四笔未形成交易的 Execute 均有 `ENTRY_SEND` 后 `ENTRY_SUBMISSION_ABORTED_V178`：三笔是报价重定价后的风险带/最大风险拒绝，一笔为 Secondary 的报价漂移拒绝；均不占仓、不进入成交统计。`20260127-0430-Long-1170` 在决策表重复写入一次相同 TradeID，按唯一 TradeID 计数后不影响上述闭环。

先前把预检日志的 `reference` 与 Tape Bid/Ask 直接比较会产生表面偏差；`reference` 是行情参考价，不是策略执行边界。以 Long Ask / Short Bid 比较后为 58/58 精确匹配。

## 边界

本轮严格校准了 v2.23 **新增入场门禁、候选身份、报价边界和双槽占用**。退出策略和 Historical Virtual TP 未改动，沿用已通过的 v2.20 回归链路；本报告不把 Actual 结果回灌成离线收益，也不据此宣称新的 H1 模拟器收益已被逐美元验证。

## 下一步门槛

可预注册 20 日分层 Actual Replay：按 2026-01 至 06 每月固定 3 日，并保留 2 个 7 月日期作为压力样本；先由离线候选输出预期身份、门禁和组合结果，Replay 后只比较预注册字段。若身份、报价、占用或退出出现结构性偏差，停止，不进入 H1 全量。
