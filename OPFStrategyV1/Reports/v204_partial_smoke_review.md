# v2.04部分Smoke复核（2026-07-24）

## 结论

- 活动目录实际包含6个Snapshot：`2025-10-21,2025-12-02,2025-12-11,2026-02-11,2026-04-13,2026-05-04`；预注册的`2025-12-12,2026-03-16`尚未运行。
- 6个Snapshot的版本一致为`OPF_RESEARCH_2.04 / ACTUAL_EXEC_2.34`。共47个Execute、41笔正常ExecutionTrade；ExecutedMissingTrade、TradesMissingExecuteDecision、TradesMissingActualVerifiedOutcome和重复ActualVerified均为0。
- `2026-05-04`整个Snapshot判为无效，不得用于收益、组合方向或Smoke晋级结论。ATAS Historical Replay在多个不同时段、方向和订单角色上重复返回固定旧价`27979.00`。
- 普通TP/SL旧价已由现有归一化逻辑纠正；Quarantine平仓也只扣手续费。但`2026-05-04 01:55`的Abnormal Safety Flatten仍按原始旧价计入AccountNet，虚增Net`+$227.60`，暴露Historical Replay异常安全平仓计价缺口。
- 当日6笔正常交易Gross为`+$33.00`、正常扣费Net为`+$18.60`；加上异常平仓虚假Net`+$227.60`和Quarantine手续费`-$2.40`后，AccountNet被污染为`+$243.80`。
- 本轮只能归档为部分Smoke诊断证据，v2.04不得判定通过。下一步只修复Historical Replay异常安全平仓参考价归一化；实盘继续使用真实成交损益。

## `2026-05-04`固定旧价证据

- `23:15` Short Entry：计划`27824.25`，原始回调`27979.00`。
- `00:50` Long TP：预期`27909.25`，原始回调`27979.00`。
- `01:55` Long异常安全平仓：入场`27921.50`，平仓回调`27979.00`，原始Gross`+$230.00`。
- `03:25` Long SL：预期`27917.75`，原始回调`27979.00`。
- `06:55` Long Quarantine平仓：原始回调`27979.00`，已正确按手续费`-$2.40`隔离。
- `08:30`、`19:35`和`20:45`的Short Entry原始回调继续为`27979.00`；`20:55`还重复推送已完成订单回调，策略抑制了重复平仓和重复账户统计。

## 修复合同

- 异常安全平仓提交时记录当根M5收盘参考价。
- Historical Replay中，若Abnormal Safety Flatten回调价相对参考价漂移超过现有执行容差，则以参考价计算账户Gross，并记录原始回调价、参考价、漂移、原始和归一化Gross。
- Real-time/live永远保留真实成交价；Quarantine、普通TP/SL、Globex Session Flatten及正常策略统计不改。
- 修复后补跑：`2025-12-12,2026-03-16,2026-05-04`。

## 归档

- 名称：`opf_v2.04_partial_smoke_6snapshots_20260504_invalid_20260724`
- 内容：6个Snapshot、118个活动日志文件及本报告，共119个清单文件、15,451,107字节。
- 源—归档逐文件SHA256不匹配：0。
- `manifest_sha256.csv` SHA256：`D91B670DBBD9B1CF785439E8502791149DD02A2F949CA7D77CAA579412868E44`。
