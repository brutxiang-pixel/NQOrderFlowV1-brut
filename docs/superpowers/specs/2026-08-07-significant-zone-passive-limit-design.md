# 显著区被动限价路径设计

## 范围

移除 `SignificantZoneFirstTouchLong` 与 `SignificantZoneFirstTouchShort`，以 `SignificantZonePassiveLimitLong` 和 `SignificantZonePassiveLimitShort` 替代；策略总路径数保持八条。

## 挂单资格

- 仅 A/B 级、`Active`、`TestCount=0`、方向一致的显著区。
- Long 使用 Bull 支撑区，限价为内沿；Short 使用 Bear 压力区，限价为内沿。
- 风险为内沿到“外沿外 0.5 点”的距离，超过 4 点拒绝。
- 目标为 `max(1R, 1.0 点)`。
- 每个 ZoneId 仅一次挂单机会。

## 生命周期

`PendingLimit` 不属于仓位、不占日单数、不创建保护单。只有 `OnNewMyTrade` 确认真实成交时才创建 `ReplayExecutionState`、递增日交易计数、提交 SL/TP OCO。区域失效、发生有效测试但未成交、日切换、时段锁定、日损触发或策略停止时撤单。

## 风控

全局数量 1 手、日损 $200、周损关闭、每日最多 20 笔。新路径与其余六条共享单活跃仓位约束。
