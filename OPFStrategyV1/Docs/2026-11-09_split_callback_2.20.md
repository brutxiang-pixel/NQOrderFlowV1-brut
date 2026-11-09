# 2.20 分批退出成交识别修复

状态：已构建测试，尚未部署。当前运行版本仍为2.19。参数仍为OPF_ENTRY_EXIT_2.19，配置仍为ACTUAL_EXEC_2.54；不调整交易规则。

## 故障及证据

订单：`20261109-1336-Short-727-CrossMarketIcebergOrderFlowReaction-ICE-ICE-NQZ6-6884993374025-20261109133617-MNQZ6-61053c86`。

仅定向查看会话`OPF-20261109-090017-a9712346809c4c6aa6444d4661053c86`。原始保护单注释把完整长TradeID放在角色之前，128字符截断使RUNNER_SL成为RUNNER_。13:44:04 UTC，两笔各1手在27301退出，runner出现TRADE_ORDER_MISMATCH / expectedOrderMissing，base正常记账。后续账户flat但内部余1手，CLOSE及图表退出收尾没有触发。

按两笔实际退出成交，入场27319.25、退出27301、MNQ两手，毛盈亏+73美元；按当前配置费用2.36美元，净额+70.64美元。该结论来自成交回报，不使用计划止损价代替成交价。旧日志与订单库未回写，历史飞书未补发。

## 修复

- 仅分批保护单改为固定短注释：OPF、96位TradeID哈希、BS/BT/RS/RT，完整保留执行身份和具体腿角色。
- 解析时恢复标准角色，通过内存执行登记表找回原TradeID；旧完整注释继续兼容。
- 订单/成交回报共用解析，优先核对已登记非零ExtId；限定当前产品及账户。旧RUNNER_只在已确认执行身份且确为分批订单时按订单类型恢复。
- 不按账户flat猜测成交价格、不生成虚构平仓记录。

## 验证

Release构建0警告0错误，显式RunRepositorySnapshot=false、DeployToATAS=false。

IntradayEntryTests、OptionsProxyGateTests、ValidationReplayTests、FeishuNotificationTests、LiveProtectionFailClosedTests全部退出码0。新增实际长ID的128字符截断复现、四种短注释解析回原执行、旧格式兼容；已有分腿生命周期验证首腿退出不完成runner、runner退出单独累计和全平分配。

DLL SHA256：`6F1829FAEE960C4A3A09671B78F6A46EA00C6D9FBC3207CA42984C82FEF45253`。

平台端真实回报及飞书发送仍需重启后的模拟成交验证。下一笔两手订单应累计两手退出，仅发送一次CLOSE并移除活动持仓线；保留的历史订单标记由历史显示开关控制。旧会话缺失的CLOSE不会因新DLL自动补发。
