# 实盘保护单 Fail-Closed 设计

## 目标

任何已确认的 OPF 入场成交，必须在短时限内拥有工作中的止损单；否则停止后续开仓、明确告警，并以该笔已确认成交量提交市价反向平仓。

## 已证实故障

`20260814-0555-Long-923` 已收到 `MYTRADE ENTRY`，但调用 `OpenOrderAsync(SL)` 后没有返回。旧代码仅在 `BracketSubmitted=true` 后认定持仓“未保护”；而该标记在 SL/TP 都提交后才设置，导致看门狗没有启动。所有常规救援任务又等待同一执行锁，无法跨过卡住的 SL 调用。

此外，`IsHistoricalReplayTime` 用本地时间直接比较未标注时区的 K 线时间，使北京时间环境中的实盘 K 线可被误判为历史，错误触发 `HISTORICAL_REPLAY_CONNECTOR_BYPASS`。

## 设计

1. 历史判定统一将未标注时间视为 UTC，并与 `DateTime.UtcNow` 比较。
2. `MYTRADE ENTRY` 累加有效成交量后，立即启动独立的保护确认看门狗；它不等待 `BracketSubmitted`。
3. 看门狗在有限等待后要求该笔剩余成交量拥有工作中的 SL。若没有：
   - 永久锁住本次策略运行的后续 Actual 开仓；
   - HUD/日志记录 `UNPROTECTED` 与 `PROTECTION_TIMEOUT`；
   - 不经过 `_executionLock`，以已确认的 `MyTrade` 剩余数量发送反向市价单。
4. 该紧急路径只以已确认的 entry fill 为数量依据；不因连接器持仓回报滞后而跳过平仓。若平仓单注册失败，继续锁死并发出人工干预告警。

## 不在范围内

- 不修改八条路径、入场门禁、止损价格、目标价格、仓位或日损规则。
- 不自动重新启用交易；发生保护超时后必须停止并人工核对账户后重启策略。

## 验收

正常：`MYTRADE ENTRY -> SL_SENT -> EXEC_ORDER_ATTACH role=SL state=Active`。

故障：`MYTRADE ENTRY -> PROTECTION_TIMEOUT -> EMERGENCY_FLATTEN_SEND`，且后续候选被 `ProtectionTimeoutLatched` 阻止。
