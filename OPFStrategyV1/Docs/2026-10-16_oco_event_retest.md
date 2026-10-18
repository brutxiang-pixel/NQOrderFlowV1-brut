# OPF_INTRADAY_1.8

跨产品OCO修复：新TradeID加入产品代码和快照后缀；OCO由完整SnapshotID、产品、TradeID的SHA256前24位生成，BASE/RUNNER再分组。订单回报附着、成交处理、ExecutionForOrder先匹配当前产品代码及稳定账户。不同产品/实例不再共享组名。旧挂单不会自动改名，升级前需平仓并清理旧挂单。

ESResponseAlignedBreakout：共享账户、同合约月份NQ/MNQ、方向及Replay状态的事件；保留最多15分钟，结构止损失效或超时才重新建立，取消按5分钟重置。每实例每事件只尝试一次。超初始风险0.5倍追价、风险扩大1.25倍、或初始风险>25点时等待回踩；进入原突破水平附近（初始风险0.25倍以内，至少4tick容差），当前风险<=25点，继续原2秒/主动流确认。保留事件原结构SL，不机械缩SL。大风险阈值与有效期是待模拟验证参数，不宣称优化收益。当前实现是本地跨产品候选事件，不是官方ES事件ID。

动态退出：真实1R手续费保本与TP不改。新增每订单前向观察价格的两套影子：0.75R手续费保本；1R手续费保本+1.25R锁0.5R。只到真实订单退出为止；未完成标UNRESOLVED，不外推。下一个观察价越过影子止损时按该观察价记录点数，不按触发价伪造有利成交，不包含盘口/排队/改单延迟，因此不是可实盘收益。日志DYNAMIC_EXIT_COMPARISON。

NQ测试期日/周损关闭保持，MNQ配置不变。

验证：五组测试及统计专项通过，2193冻结门禁重放0差异（不等于新候选端到端回测）；新增跨产品/实例OCO唯一性、跨K事件、过期失效、双向宽风险回踩、影子退出时序测试通过。最终构建0warning/0error。SDK误引用测试bin旧DataFeedsCore已通过主项目None排除修复，未改ATAS安装文件。

证据Evidence/Validation/20261016-124001；末次小改后IntradayEntryTests和正式build再次通过。部署DLL SHA256 F4B41B5AF9B19627E8DBF7D2620A69999FDFEDB5BFFA046FA44FE30058A47A30。

验收：重启经典ATAS，HUD1.8；NQ/MNQ订单标识带各自产品、不同OCO；一个产品TP不应取消另一个产品保护；日志ES_BREAKOUT_RETEST_WAIT可解释宽风险等待，REALTIME_TRIGGER标明原触发/SL；下一轮看实际成交与影子退出对照。未清理日志、未下测试单、未发送飞书消息。
