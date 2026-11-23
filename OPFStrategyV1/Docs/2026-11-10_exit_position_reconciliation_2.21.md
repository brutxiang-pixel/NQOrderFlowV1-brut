# 2.21 退出后账户净仓对账

状态：2026-11-10 12:11:51北京时间已部署2.21，待启动验收；执行配置ACTUAL_EXEC_2.54与退出参数OPF_ENTRY_EXIT_2.19不变。

部署前确认ATAS经典版及X进程均退出，旧DLL备份至`OPFStrategyV1/bin/deployment-backups/before-2.21-20261110-121150/OPFStrategyV1.dll`。仅替换`C:/Users/Administrator/AppData/Roaming/ATAS/Strategies/OPFStrategyV1.dll`并校验SHA256与下方一致；未重新构建或修改活动配置。

## 案例与诊断

- NQ：`20261109-1639-Short-487-CrossMarketIcebergOrderFlowReaction-ICE-ICE-NQZ6-6885025544018-20261109163904-NQZ6-55367e9d`
- MNQ：`20261109-1640-Short-764-CrossMarketIcebergOrderFlowReaction-ICE-ICE-NQZ6-6885025667599-20261109163955-MNQZ6-d932ad64`

订单账本和成交事件均显示空单各1手，保护性买入止损成交并记录CLOSE。随后旧会话出现`Unmanaged position detected (1)`；之后重连事件记录`flat=true`，较新的策略会话启动又各报告1手未管理仓位、无OPF工作单。账户状态记录存在矛盾，无法由本地日志确认残仓方向、开仓来源或在两个状态事件间是否由人工/其它实例改变。此前回复将其简单定性为“止损已正常平仓、后来另有外部多仓”证据不足，予以更正。

根因级缺陷：实际执行模式收到的MyTrade退出成交量达到逻辑仓位后，代码直接调用完成归档并发送CLOSE，没有先核对账户连接器的实际净仓。账户仍有仓位时，后续孤立仓位保护将策略置为Blocked；外部账户与策略账本可发生分歧。

## 修复

实际执行的完整退出回报现在进入待对账状态。策略每秒检查一次：账户连接器须已连接，且当前净仓必须等于当前图表品种所有活动策略执行的预期净仓。匹配后才标记ExitCompleted、写成交归档并发送CLOSE；不匹配或连接器不可用时保留活动记录与图表线，不发送CLOSE，置`ExitAccountPositionMismatch`并报告实际/预期净仓，每5秒限频记录后续不匹配状态。

不会自动下单平掉不匹配仓位，也不把手数方向根据猜测修正。历史Replay流程及入场、止损、止盈参数不变。账户状态恢复一致后待处理退出会继续完成，不要求新的成交回报。

## 验证与部署

新增账户净仓匹配回归：短仓残留、多仓反向残留不允许完成；账户净仓为零允许完成；其它受管仓位仍在时以组合预期净仓比较。Release构建0警告0错误；LiveProtectionFailClosedTests、IntradayEntryTests、OptionsProxyGateTests、ValidationReplayTests、FeishuNotificationTests均通过。

活动账户上现有未管理仓位须由用户在平台/账户确认并按需处理；新DLL不会猜测或自动平仓，也不会自动重建旧执行状态。启动后HUD版本须为2.21，再用模拟成交验收：若券商净仓未回到策略管理预期，不能出现CLOSE且需Blocked；一致后只归档、推送一次CLOSE并清除活动线。

Release DLL SHA256：`FB3661346E4FF446034A88B66FEA5CF964538DF7365998DB949CEB905BB0F390`。
