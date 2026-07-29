# v2.20 2026-04-08虚拟目标定点回归

日期：2026-07-26

## 裁决

Historical Replay虚拟目标适配器第一轮定点回归通过。Dormant Limit已从订单生命周期中消失，真实SL和触目标后的Market退出均正常，5笔交易数量完全守恒，无隔离、过量退出、孤儿仓位或保护清理缺陷。

本轮单独运行4月8日，活动日志中没有4月6日至7日的周损，因此周门未触发，原v2.19的07:40 Short故障交易也没有因同样的候选占用链再次出现。故本轮只通过“适配器一般功能回归”，仍需连续跑4月6日至8日直接验证原故障路径。

## 配置与结果

- Snapshot：`OPF-20260726-084808`
- 版本：`OPF_RESEARCH_2.20 / ACTUAL_EXEC_2.45`
- 配置：3手、15笔、日损`-$250`、周Long门`-$500`
- 交易：5笔，全部Normal
- Gross：`-$279.50`
- AccountNet：`-$297.50`
- Quarantine：0

## 虚拟目标证据

- `HISTORICAL_DORMANT_TP_SENT_V181=0`
- `HISTORICAL_VIRTUAL_TP_ARMED_V220=5`
- `HISTORICAL_OBSERVED_TP_EXIT_SEND_V220=1`
- `HISTORICAL_OBSERVED_TP_EXIT_SENT_V220=1`
- 实际TP成交订单类型：Market，3手一次完成
- 目标成交：计划`25141.00`，ATAS Market成交`25144.00`，按既有Historical Replay归一化审计处理

## 生命周期证据

- 5笔均为EntryQty 3、ExitQty 3。
- 4笔SL、1笔TP；不存在重复退出。
- `PROTECTION_CLEANUP_DONE=5`
- `PROTECTIVE_FILL_QUARANTINED_V177=0`
- `EXIT_FILL_EXCEEDS_REMAINING_V220=0`
- `ORPHAN_POSITION_BLOCKED=0`
- `CANCEL_FAIL=0`
- `ORDER_STATE_FAILED=0`

## 下一步

本轮23份证据、26,921,798字节已归档到`opf_v2.20_virtual-target_20260408_regression-passed_1snapshot_20260726`，逐文件SHA256不一致数0；活动日志已清空。下一轮严格按顺序运行：

`2026-04-06,2026-04-07,2026-04-08`

中间不得清日志。该轮必须让4月7日触发周门、4月8日恢复门状态并释放原07:40 Short候选链；重点确认不存在Dormant Limit、不可达旧价成交、累计退出超过入场、孤儿仓位和Readiness全日阻断。
