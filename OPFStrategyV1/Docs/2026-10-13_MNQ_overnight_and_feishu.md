# 2026-10-13 MNQ 夜间执行与飞书通知排查

证据实例：`OPF-20261012-165052`。日志截止北京时间 2026-10-13 07:02；交易时间由日志 UTC 加 8 小时。保留全部原始日志。

| 北京时间 | 订单 | 路径 | 方向 | 入场 | 平仓 | 结果 | 含配置手续费净额 |
|---|---|---|---|---:|---:|---|---:|
| 04:20–04:45 | 20261012-2020-Short-1096 | ObservationConfirm_WideStop1_5R | Short | 30878.50 | 30885.50 | SL | -$15.20 |
| 06:15–06:45 | 20261012-2215-Long-1107 | ObservationConfirm | Long | 30896.50 | 30886.50 | SL | -$21.20 |
| 06:55 | 20261012-2255-Short-1115 | ObservationConfirm | Short | 30877.75 | — | 日志截止未记录平仓 | — |

两笔已关闭合计毛亏损 $34，配置往返手续费 $2.40，净亏损 $36.40。第一笔 MFE 7 点（1R）、MAE 7.25 点；第二笔 MFE 9 点（0.878R）、MAE 10.25 点。两笔均有止损成交回调和保护单清理完成记录，没有异常成交隔离标记。样本不足以调整胜率门禁。

## 路径与执行模式

配置快照包含全部 12 条路径且启用自动下单。OC 有 13 条执行决策记录，2 次执行；WideStop 有 13 条，1 次执行。其余拦截集中在风险带、质量、风险上限、入场柱已经触及止损、RR 等条件。两个 FR 路径各有 2 个候选，均因方向禁用被拦截。Breakaway 两路、显著区两路及 ES 四路在本实例中未记录实际执行决策，不能据此认定故障，也不能认定全部验证通过。

日志里的 `shared-v4` 是源码中尚未更新的固定日志文字，HUD 使用 `cross-market-v6`，不能仅据此判断 DLL 版本。

本轮日志有 `HISTORICAL_REPLAY_CONNECTOR_BYPASS`，账务来源 `HistoricalReplayNormalized`；三个 TP 都是 `HISTORICAL_VIRTUAL_TP_ARMED_V220`，真实停止订单则有 `SL_SENT` 和成交回调。因此这是按历史 Replay 分支执行的记录，不是实时模拟账户完整原生双保护单验收。ES 快照偶尔出现共享文件 IOException，随后恢复；本轮未改数据源/历史模式判断或该独立问题。

## 飞书根因与修复

配置启用飞书，但真实成交处理链中缺少开仓和平仓发送调用；只剩毛刺通知发送。已恢复：

- 已匹配并确认全量入场成交后通知，不在下单请求时冒充成交。
- 完整平仓后通知；部分平仓及重复回调不重复通知。
- 同一 TradeID 贯穿开仓、平仓，保留方向、路径、成交价、SL/TP、平仓原因及毛盈亏。
- 增加 QUEUED、HTTP 状态、飞书业务返回码、网络异常类型日志，不记录 webhook 秘密。

未发送联网测试消息、未创建测试订单。离线格式、payload、7 类业务回复解析测试通过；ES 代理门禁 34 项测试通过。旧 `LiveProtectionFailClosedTests` 因现有 ConfigSnapshot.Create 五参数调用与当前四参数接口不符而无法编译，未将该套件报告为通过。

Release 构建 0 警告、0 错误。部署到 `C:/Users/Administrator/AppData/Roaming/ATAS/Strategies/OPFStrategyV1.dll`，部署与构建文件 SHA256 相同：`814BE4357EF9ACC7F13ECDA15ACCFFBBB17E57761A75E41678A4EF36445297CD`。运行中的旧实例需要停止并在 ATAS 重启后加载新版，下一次正常成交检查 FEISHU_ENTRY_QUEUED / FEISHU_EXIT_QUEUED / FEISHU_TRADE_RESULT。
