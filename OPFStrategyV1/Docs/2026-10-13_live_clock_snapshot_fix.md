# 2026-10-13 实时行情时间与 ES 共享快照修复

## 问题与修复

- 日志中的 ATAS K 线时间为 UTC（部分 DateTime.Kind 为 Unspecified），旧逻辑与北京时间 DateTime.Now 比较，产生约八小时时差，将实时行情误判为历史 Replay。统一以 UTC 比较，保留原十分钟识别阈值，恢复正常实时 ES 快照发布。该机制仍为时间推断；延迟超过十分钟的实时行情仍可能被识别为历史。
- ES 共享快照读取允许文件替换，并与同进程发布串行；历史归档读取允许追加，避免消费者阻断生产者。历史读取继续禁止未来数据。
- SnapshotId 追加 GUID，避免 ES、NQ、MNQ 同秒启动时日志文件重名。
- 四条 ES 路径增加 DataBlocked、Prefilter 与 EntryRecheck 漏斗记录，以区分数据阻断、形态未满足与入场复核。

## 验证与部署

- OptionsProxyGateTests：39 项检查通过，含 UTC/历史识别、100 个并发实例 ID、100 次并发快照发布与读取，以及原有门禁/历史防未来数据测试。
- FeishuNotificationTests：离线消息模板、响应分类、一次性测试单与真实目标订单路由测试通过；未发送测试消息或订单。
- Release 构建：零警告、零错误。部署至 `%APPDATA%/ATAS/Strategies/OPFStrategyV1.dll`，构建与部署 SHA256 一致：`BBCBFCE9EEF2F848776E9BBE51CD6895DE4673AFD0B7CCA922AF6F268EDD1E03`。
- 本轮不调整八条实际下单路径、入场阈值或风控配置，不清理日志。

## 平台验证

停止图表策略并重启 ATAS，先启动 ES 数据源图表策略，再启动 NQ/MNQ 消费端策略。检查 ES 快照采集时间持续前进，NQ/MNQ 不再因时区误判使用历史上下文或旧快照。尚需平台运行日志验证；构建与离线回归通过不代表已经有真实成交验证。
