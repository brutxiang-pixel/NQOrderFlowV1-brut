# 显著区被动限价中点与扩展止损设计

## 目标

保留双向 `SignificantZonePassiveLimit` 的 A/B、Active、未测试区域资格，将限价从内沿改到区域中点，并提高外沿止损缓冲，避免内沿首次触及后的微小穿透立即止损。

## 固定规则

- Long 限价：`(InnerBoundary + OuterBoundary) / 2`。
- Short 限价：`(InnerBoundary + OuterBoundary) / 2`。
- Long 止损：`OuterBoundary - 1.0` 点。
- Short 止损：`OuterBoundary + 1.0` 点。
- 删除该路径专属的 4 点最大结构风险拒绝；保留已有全局 25 点硬上限、日损 200 美元、日限 20、单手、A/B、Active、`TestCount == 0`、时段和账户保护门禁。
- TP 继续为 `max(1R, 1 点)`；不改其它七条执行路径。

## 生命周期

未成交挂单不占活动槽或日单；成交才计入日单并建立 SL/TP 保护；区域失效、被测试、Globex 收盘、策略停止和另一被动单成交时撤单。此改动不改变上述生命周期。

## 验收

静态测试必须确认中点、1 点缓冲、无路径专属 4 点上限；构建零错误；部署 DLL 和现行八路径自动配置。Smoke 日 `2026-05-05` 中，已成交被动单应留有 `PASSIVE_LIMIT_FILLED`、`PASSIVE_LIMIT_PROTECTION_QUOTE_ACCEPTED`、`SL_SENT` 和 TP 保护事件，撤单必须有 `CANCEL_SENT`。
