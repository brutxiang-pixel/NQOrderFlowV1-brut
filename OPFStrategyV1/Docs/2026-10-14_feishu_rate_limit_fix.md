# OPF_INTRADAY_1.4：飞书限流漏通知修复

实际订单20261014-0755-Long-947-ESResponseAlignedBreakout，MNQ Long，30813.75开仓、30806.25 SL平仓，毛-$15、配置扣费净-$16.20。退出成交、损益与TP撤销完成；飞书EXIT收到HTTP200但businessCode11232，原实现只记拒绝、不重试。官方自定义机器人文档明确该码是系统限流，整点可能发生：https://open.feishu.cn/document/client-docs/bot-v3/add-custom-bot

开仓/平仓/毛刺共用650ms最小发送间隔，异步执行；11232或HTTP429按2/5/10秒重试，总4次，成功立即停止。其他业务拒绝及未知响应不作为成功、不盲目重试；网络超时不自动重试以避免不确定送达重复。每次记录attempt/status/businessCode/success，终止失败明确DELIVERY_FAILED。重试沿用原消息标识与时间。未补发旧消息或主动发测试消息。限流持续存在时仍可能最终失败，不能承诺外部消息必达。

五组回归与统计专项通过，80条真实门禁重放0差异，正式构建零警告零错误。通知修复不改订单执行、路径或风控。完整重启ATAS后确认OPF_INTRADAY_1.4。
