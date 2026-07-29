# v2.16 双槽一致性与Globex终态修复

日期：2026-07-25

## 修复目标

一次性关闭v2.15四日Smoke确认的三个缺口：

1. Globex平仓持有执行锁等待回调，导致回调必然排队到超时之后。
2. 同柱第二槽候选在主槽保护尚未提交完成时被永久丢弃。
3. 计划风险通过，但最新可成交报价或真实成交重定价后进入严格风险带、超过单笔上限或突破第二槽合计风险上限。

## 实现

### Globex与停止终态

- `GlobexCloseout`动作提交平仓单后立即释放`_executionLock`，5秒确认延迟移到锁外；`HandleReplayTradeAsync`可立即处理`MyTrade`，不再形成自阻塞。
- 策略停止时，如果已有Globex平仓且账户已平，先等待该回调；回调完成则不再写`ACTIVE_ON_STOP`或重复发送平仓。
- Historical Replay在账户已平、平仓订单已Filled但回调仍缺失时，可按冻结的Globex参考价幂等重建`SESSION_FLATTEN`终态；迟到回调只会进入既有重复成交保护，不会重复记账。
- 实盘账户已平但缺少成交回调时不伪造PnL，继续阻断Live readiness并要求人工核对。

### 同柱第二槽保护后重评

- 只有方向、静态G2和计划合计风险均已通过，而唯一阻塞原因为主槽未保护时，才缓存一个候选。
- 同一主槽只保留按既有SourceSequence到达的首个合格候选；后续候选不替换它。
- 主槽工作止损确认后立即取出候选，刷新Bid/Ask并按新入场价重算风险，再完整调用原Actual入口门禁。
- 候选跨Bar、主槽保护失败、主槽已退出、报价失效或结构止损位于错误方向时直接失效；停止策略时清空。
- 该实现与离线连接器现有“同时间戳按SourceSequence先主槽、后首个合格第二槽”的选择语义一致，不删除`PrimaryNotProtected`安全约束。

### 风险门前置

市场单送出前使用Long Ask / Short Bid复刻成交后止损重定价，检查：

- v2.08严格排除带`(13.25,16]`
- 每条路径保存的单笔最大风险及既有1点容差规则
- 第二槽按主槽真实初始风险计算的`$300`合计初始风险上限

不通过时使用既有`ENTRY_SUBMISSION_ABORTED_V178`终态回滚正常容量，不产生真实订单。成交后原检查全部保留，防止报价到成交之间再次漂移。

## 版本与验证

- Research：`OPF_RESEARCH_2.16`
- Actual配置：`ACTUAL_EXEC_2.41`
- `dotnet build OPFStrategyV1/OPFStrategyV1.csproj -c Debug`：0警告、0错误
- 离线回归测试：5项通过；新增同时间戳第二槽顺序及严格风险带前置两项。
- v2.15四日失败证据已复制到`OPFStrategyV1/Reports/v215_4day_actual_dual_slot_smoke_evidence/`，共92个文件。
- 编译后ATAS活动日志已按规则清理为0文件。

## 回归Gate

日期继续固定：`2026-01-02,2026-02-11,2026-04-24,2026-05-05`。

必须满足：

- `ACTIVE_ON_STOP=0`、`GLOBEX_CLOSEOUT_UNCONFIRMED=0`
- 订单错配、保护丢失、ManagedPositionMismatch和风险超限均为0
- 原3个直接因`PrimaryNotProtected`消失的离线S2应进入保护后重评；每个重评必须有刷新报价和重新门禁证据
- 严格风险带/单笔上限/第二槽风险上限违规在下单前阻断，成交后异常安全平仓数量不得增加
- 正常共同交易的退出角色与尺度不得退化

只有四日配对回归通过后，才讨论增加Smoke；不直接安排全量Replay。
