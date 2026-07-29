# v2.20 Historical Replay虚拟目标适配器修复

日期：2026-07-26

## 修复目标

修复v2.19五日Smoke中`20260408-0740-Short-1127`的Dormant TP旧价部分成交：3手入场被TP异常成交1手后，SL又成交3手，最终形成4手退出和`+1`手孤儿仓位。

本版本只修改Historical Replay目标执行和异常保护成交闭环。周度Long门、入场路径、退出目标、3手、15笔、日损`-$250`、同向G2及并发风险`$300`不变。

## 实现

- 版本更新为`OPF_RESEARCH_2.20 / ACTUAL_EXEC_2.45`。
- Historical Replay不再向ATAS提交500点外Dormant Limit TP。
- Historical Replay仍提交真实Stop保护单。
- 每个TP腿以`HISTORICAL_VIRTUAL_TP_ARMED_V220`记录目标、角色和数量。
- 行情路径明确触及目标且同柱未触及同腿SL后，按该腿剩余数量提交真实Market退出：`HISTORICAL_OBSERVED_TP_EXIT_SEND_V220 / ...SENT_V220`。
- 普通TP使用`EntryFilledQty - ExitFilledQty`；ZoneBirth Base/Runner分别使用各腿剩余量，不得复用原始固定数量造成重复退出。
- Live/实时路径仍执行原有`OpenProtectionOrderAsync(... Limit TP ...)`，没有改为虚拟目标。

## 异常保护成交闭环

- 任何退出回调只把`min(fillQty, remainingQty)`计入逻辑退出量。
- 若不可达保护成交只完成部分仓位：立即清除虚拟目标、取消剩余保护，并按剩余持仓提交Emergency Flatten，完成隔离后策略可继续。
- 若ATAS报告的单次成交量已经超过逻辑剩余量：写`EXIT_FILL_EXCEEDS_REMAINING_V220`、取消剩余保护并阻止继续交易，要求核对真实账户；策略不会把逻辑退出量累计到入场量以上。

## 证据归档

v2.19失败Smoke已归档到：

`%APPDATA%\ATAS\StrategyLogs\OPFStrategyV1_Archive\opf_v2.19_weekly-gate-functional-pass_lifecycle-failed_5snapshots_20260726`

- 文件数：115
- 字节数：148,427,567
- 源与归档SHA256不一致数：0

## 构建与部署

- `dotnet build OPFStrategyV1/OPFStrategyV1.csproj -c Release`：0警告、0错误。
- 本地与ATAS部署DLL SHA256：`1F8D0331CAF71506616C3DCC7F2FDE5275C93E8C8FB2C7E7F5D0FF1AA1FF17C0`。
- 编译部署后活动日志：0。
- AppData旧配置由重启ATAS时自动升级到`ACTUAL_EXEC_2.45`，不手工编辑。

## 回归合同

第一轮只运行：

`2026-04-08`

必须满足：

1. 配置为`OPF_RESEARCH_2.20 / ACTUAL_EXEC_2.45`。
2. `HISTORICAL_DORMANT_TP_SENT_V181=0`，且ATAS订单中不存在500点外Dormant Limit。
3. 出现`HISTORICAL_VIRTUAL_TP_ARMED_V220`；触及目标时出现V220 Market目标退出。
4. `20260408-0740-Short-1127`不得再次出现不可达`24604`成交、4手累计退出或`ORPHAN_POSITION_BLOCKED`。
5. 每笔`ExitFilledQty <= EntryFilledQty`，账户最终平仓，保护清理完成。
6. 周门恢复后Long继续被跳过，Short仍可交易。

第一轮通过后才运行第二轮连续周状态回归：

`2026-04-06,2026-04-07,2026-04-08`

中间不得清日志。第二轮继续要求跨日恢复、周门触发、Short保留、Long跳过和生命周期零缺陷；不直接进入H1全量Replay。
