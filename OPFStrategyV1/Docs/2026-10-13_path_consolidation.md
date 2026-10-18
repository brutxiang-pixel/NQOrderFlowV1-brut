# 2026-10-13 MNQ/NQ 执行与八路径收敛

## 日志结论

- MNQ 实例 `OPF-20261012-165052` 确有 3 笔开仓，前两笔已止损，第三笔在日志截止时仍未出现平仓回调；原问题是飞书成交通知缺少调用，不是 MNQ 没有成交。
- NQ 实例 `OPF-20261012-165126` 没有成交不是候选链停止，而是启动时先后记录 `ConnectorMissing`、`Disconnected`，LIVE readiness 因连接器不可用而封锁所有新 Actual entry。期权代理数据本身持续 `ModelReady`，不能把 NQ 零成交归因于 ES 期权门禁。
- NQ 仍有 25 条 ActualCandidate 研究/执行决策记录，但 Execute 为 0；其中 6 条是方向禁用，其余主要是风险、RR、质量或入场柱触止损拦截。

## 新的八条实际下单门禁

保留旧路径四条：

1. `ObservationConfirm`
2. `BreakawayFvg_Qualified`
3. `FailureReverse_ObservationInvalidated_WideStop1_5R`
4. `FailureReverse_RetestFailed`

加入独立 ES 研究家族四条：

5. `ESResponseAlignedBreakout`
6. `ESWallRejectionReversal`
7. `ESExpectedMoveExpansion`
8. `ESGammaConcentrationResponse`

暂时只记录、不下单的旧路径：

- `ObservationConfirm_WideStop1_5R`
- `BreakawayFvg`
- `SignificantZoneFirstTouchLong`
- `SignificantZoneFirstTouchShort`

这些路径仍会进入研究候选、漏斗、跳过原因和结果统计；由于不在 ActualExecutionPaths，尝试进入执行阶段时只写 `PathDisabled`，不会提交订单。

已同步更新默认配置、现有本地配置及 DLL。配置归一化会从旧配置中移除四条下线路径，并补齐八条新门禁，避免旧 JSON 把 12 条路径重新带回。
