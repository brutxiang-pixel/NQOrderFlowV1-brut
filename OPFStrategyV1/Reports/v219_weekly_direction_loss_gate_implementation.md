# v2.19 周度方向损失门实施与Smoke合同

日期：2026-07-26

## 实施结论

v2.19仅实现离线冻结的`WeeklyAccountOrLongAllLong500`规则，没有修改入场路径、退出政策、双槽规则、日单量或日损参数。

## 冻结规则

- 统计周：Snapshot交易日所属周一至周日。
- 本周已实现AccountNet或已实现Long方向Net任一达到`-$500`及以下后，门禁锁存至本周结束。
- 门禁触发后禁止所有新Long；已有Long不强平，继续原OCO管理。
- Short与Breakaway Short继续正常交易。
- 新周自动复位。
- Abnormal/Quarantine不计15笔正常交易上限，但真实AccountNet与手续费计入周损。
- 其余配置保持：3手、15笔、日损`-$250`、同向G2、并发初始风险`$300`。

## 实现要点

- Research版本：`OPF_RESEARCH_2.19`。
- Actual配置版本：`ACTUAL_EXEC_2.44`，新增`ActualWeeklyLongLossLimitDollars=500`。
- 入场跳过事件：`SKIP_WEEKLY_LONG_LOSS_V219`。
- 首次触发事件：`WEEKLY_LONG_LOSS_GATE_TRIGGERED_V219`。
- HUD显示周起始日、AccountNet、LongNet、门状态和阈值。
- 门禁采用周内锁存状态；后续Short盈利不能重新打开Long。
- 每日Replay重启策略时，从活动目录全部`*_live_account_pnl.csv`按TradeID去重、按退出时间重建本周损益轨迹及历史触发点。
- Replay从电脑当前日期切到历史交易日时，会恢复历史目标周，而不是按本地当前周清零。
- 恢复成功记录`LIVE_TRADING_WEEK_NET_RESTORED`；周切换记录`LIVE_TRADING_WEEK_ROLLOVER`。

## 离线依据

冻结候选H1为940笔、Gross`+$26,210.73`、Net`+$22,826.73`、PF`1.6569`、23/27盈利周；基线为964笔、Gross`+$24,239.98`、Net`+$20,769.58`、PF`1.5851`。7月两轮分别由`-$872.30`改善至`-$38.98`、由`-$689.10`改善至`+$184.67`。完整研究见`v218_h1_weekly_direction_loss_gate_review.md`。

## 构建与部署

- `dotnet build OPFStrategyV1/OPFStrategyV1.csproj -c Release`：0警告、0错误。
- 本地与ATAS部署DLL SHA256一致：`EBE40BBE7062F87FB684E268E902F286077AC999B6F1820DB7FD178C0FD90467`。
- 编译部署后活动日志文件数：0，符合“重新编译DLL必须清日志”规则。
- AppData运行配置仍为`ACTUAL_EXEC_2.43`；重启ATAS加载v2.19时，版本升级逻辑会自动生成`ACTUAL_EXEC_2.44`及周损字段，不手工改运行配置。

## Smoke合同

按以下顺序连续运行，中间不得清理日志：

`2026-04-06,2026-04-07,2026-04-08,2026-07-06,2026-07-07`

验收项：

1. HUD与ConfigSnapshot为`OPF_RESEARCH_2.19 / ACTUAL_EXEC_2.44 / 3手 / 15笔 / 日损250 / 周Long门500`。
2. 4月7日、4月8日重启后出现`LIVE_TRADING_WEEK_NET_RESTORED`，累计值延续前序Snapshot。
3. 门触发后出现`WEEKLY_LONG_LOSS_GATE_TRIGGERED_V219`及后续`SKIP_WEEKLY_LONG_LOSS_V219`。
4. 门触发后Short仍可下单，已有Long不被门禁强平。
5. 7月6日进入新周后周状态复位，再按7月6日至7日重新累计。
6. Long门一旦触发，即使账户周Net被Short盈利拉回阈值上方，本周仍保持`BLOCKED`。
7. OCO、保护单、双槽、异常隔离、Globex终态与正常交易计数零回归。

本轮只裁决功能一致性和定点经济方向；不直接启动H1全量Replay。
