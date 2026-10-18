# NQ/MNQ ConnectorMissing 误报修复

日志证据：08:45 UTC 启动的 NQ、MNQ 实例均先记录 ConnectorMissing，再被 Started 核对显示为 Disconnected。ES 快照读取成功，两项状态独立。

已检查本机 ATAS SDK：ChartStrategy.Connector 为独立可写属性，Security/Portfolio 来自 TradingManager；不能假定图表绑定账户时 Connector 必然已赋值。ITradingCore.GetConnector(Portfolio) 提供账户路由解析。

修复：通过图表服务获取交易核心；经典版未注册该服务时，在 TradingManager/DataProvider 的有限深度对象图中查找同一类型核心。仅按当前 Portfolio 解析连接，不使用任意在线行情连接。启动与运行期检查连接，变更时移除旧事件、绑定新事件并执行原持仓/挂单核对。空连接保留 ConnectorMissing，实际离线保留 Disconnected。

新增 OFT.Platform.Core 引用，Private=false，使用 ATAS 自带 SDK 程序集。Release 构建零警告零错误；原39项期权回归与飞书/测试单离线测试通过。账户连接修复尚待 ATAS 运行验证，离线测试未模拟平台对象图。部署 DLL SHA256：86DA4C969116B8AF2F71ED5D233DB3E992CA90B56A2C9CB93057FD5B19ED40F9。

截图期权 coverage=13%，现有入场门禁要求至少50%；连接恢复不等于候选满足期权门禁，本轮未调整阈值。最新日志中重启后的 ES 实例仍有 loadingSeries，消费者快照时间曾停在08:45:14 UTC，需在加载新版后分别验证实时源更新与账户连接恢复。
