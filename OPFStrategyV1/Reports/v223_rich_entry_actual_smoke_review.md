# v2.23 Rich 路径候选 Actual Smoke 验收

归档日期：2026-07-27  
结论：**功能通过；策略收益未晋级。**

## 固定范围

- Snapshot：6 个，均为 `OPF_RESEARCH_2.23`。
- 运行配置：3 手、15 笔日上限、-$250 日损、-$500 周 Long 损失门、同向 G2、并发初始风险上限 $300。
- 预注册日期：`2026-01-27,2026-02-24,2026-03-20,2026-05-08,2026-07-07,2026-07-24`。
- 门禁：Primary Long `ObservationConfirm` 仅允许 `RegimeBars >= 8`；Primary Short `BreakawayFvg` 要求质量分 >=88、实际风险 >=12、同向 `VWAPSide` 通过。

## 验收结果

| 项目 | 结果 |
|---|---:|
| 实际闭环交易 | 58 |
| TP / SL / TimeStop / SESSION_FLATTEN | 16 / 33 / 6 / 3 |
| Gross / 手续费 / Net | +$1,100.50 / $208.80 / +$891.70 |
| 数量错误、未终态、未平仓、保护残留 | 0 / 0 / 0 / 0 |
| `EXEC_ABNORMAL`、隔离、安全平仓、超剩余数量 | 0 / 0 / 0 / 0 |
| v2.23 门禁命中 | 13 |

13 次门禁均只命中预注册的 Primary 目标路径：11 次 Long OC 因 `RegimeBars < 8` 拒绝，2 次 Short Breakaway 分别因质量/风险不足或 `VWAPSide` 未通过拒绝。不存在 Secondary、其他路径或生命周期的误伤。

4 笔 TP 带有 `AbnormalTPFill` 原始 Replay 回报偏差（5.25--15.25 点），但均走既有 `NormalizedReplayExitFill`，以预期价记录正常统计；`IsAbnormalExecution=false`，未触发隔离或安全平仓。这是 Historical Replay 的原始市场退出回报抖动，不判为生命周期缺陷；原始价、预期价和偏差均保留于证据。

`ConnectorMissing` 仅发生于策略启动的连接器尚未就绪阶段；其后 Actual 订单均正常提交、保护、退出和清理，因此不构成阻塞。

## 判定边界

本 Smoke 只裁决 DLL 行为、门禁方向和订单生命周期。它不要求、也不能以 6 日的美元结果裁决离线 H1 候选收益；不得将 +$891.70 外推为 H1 收益或作为候选晋级依据。

## 证据归档

- 路径：`%APPDATA%\\ATAS\\StrategyLogs\\OPFStrategyV1_Archive\\opf_v2.23_rich_entry_actual_smoke_functional_passed_6snapshots_20260727`
- Manifest 前文件：139；总字节：160,986,296；源—归档 SHA256 不一致：0。
- `manifest_sha256.csv` SHA256：`B637FC6D63CB43744B8557DE3DC881C1F27F48A4696F9E8FC9B3078699E9FF2D`。
- 活动日志按确认保留，未清理。

## 下一步

使用本轮 6 个 Snapshot 的 Decision Tape，执行 v2.23 候选与 Actual 的逐笔校准：先核对候选身份、门禁命中、入场报价、持仓占用和退出结果；只在校准结果支持离线候选后，才预注册 20 日分层 Replay。不得直接进行 H1 全量 Replay。
