# 近期实盘模拟订单与 ES 数据可用性复盘

日期：2026-11-03（北京时间）

## 结论

当前实际下单路径为 6 条。订单库中 2026-10-20 之后、策略版本为 `OPF_INTRADAY_2.10` 的记录只有 4 笔，全部来自 MNQ：`ESExpectedMoveExpansion` 1 笔盈利、`ESGammaConcentrationResponse` 1 笔盈利、`FailureReverse_RetestFailed` 1 笔盈利、`ESResponseAlignedBreakout` 1 笔亏损。因此不能用这 4 笔评价六条路径的长期胜率。

更完整的近期订单库（包含 2.6–2.10 的历史运行）显示：ES 代理路径成交主要集中在 `ESResponseAlignedBreakout`，而 NQ 的结果明显弱于 MNQ；但这段样本跨越多个版本，适合作为问题定位和假设生成，不适合作为单独的参数优化证据。

## 路径与品种摘要

| 范围 | 路径/品种 | 笔数 | 盈利/亏损 | 毛盈亏 | 观察 |
|---|---|---:|---:|---:|---|
| 10-20 起 | ESResponseAlignedBreakout / MNQ | 35 | 17 / 18 | -$205 | 24 笔 MFE<1R；需优先复核入场时机和 ES 延迟 |
| 10-20 起 | ESResponseAlignedBreakout / NQ | 16 | 7 / 8，另1笔持平 | -$1,070 | 10 笔 MFE<1R；需检查 ES 快照年龄和本地确认 |
| 10-20 起 | ESExpectedMoveExpansion / MNQ | 11 | 5 / 6 | +$58 | 样本小，方向有边际但仍有宽风险亏损 |
| 10-20 起 | ESExpectedMoveExpansion / NQ | 7 | 3 / 4 | -$585 | NQ 风险点数和合约点值放大亏损，优先检查风险缩放与迟到扩张 |
| 10-20 起 | ESGammaConcentrationResponse / MNQ | 2 | 2 / 0 | +$113 | 样本不足，只能说明候选方向值得继续采集 |
| 10-20 起 | ESWallRejectionReversal / MNQ+NQ | 3 | 3 / 0 | +$963 | 样本极小，暂不放宽门禁 |
| 10-20 起 | FailureReverse_RetestFailed / MNQ | 1 | 1 / 0 | +$40.50 | 样本不足 |

## ES 数据问题的证据

统计口径：订单库 `order_ledger.csv`，入场 UTC 时间自 2026-10-20 起，版本 2.6–2.10；按 TradeID 核对无重复。账本最后一笔入场为 2026-10-28 00:11 UTC（北京时间 08:11），不能将账本无新单直接视为其后所有日志均无成交。四笔 2.10 订单合计毛收益 $155.50，按当前每笔1手、往返手续费 $1.18 估算净收益 $150.78；这只是极小样本。

2.10 三笔盈利均为 1.5R TP；突破亏损单 MFE 仅 0.5067R，未启动保护，最终约 -0.97R。至少这笔亏损不是“已有大浮盈后未锁住”，更值得检查触发有效性和入场时点。混合版本突破路径的26笔亏损中，24笔标记为原始风险亏损、2笔为保本保护后小亏；因此不能只靠动态退出解决。

近期决策日志出现大量 `OptionsGate:EsSourceOnly`，这表示当前图表是 ES 源图或该候选不允许在 ES 源图直接执行，并不等于四条 ES 代理路径都满足了可执行数据条件。另有 `NQOwnConfirmationUnavailableV29`、`MTF:ContinuationAgainstTwoFastFrames` 和 `Context:ImmediateObstacle`，说明部分候选在期权门禁之后仍被本地质量/多周期门禁拦截。

10 月 30 日运行快照还记录 `CONNECTOR_DISCONNECTED`；11 月 2—3 日快照主要是候选与阻断记录，没有对应的新 `ENTRY_SEND`/`EXIT_FILLED` 订单链。由此可以确认：近期“没有单”不能直接归因于六条路径放行过严，连接和 ES 数据可用性是独立的一级阻断原因。

## 可落地的突破点（本轮不改代码）

1. **先修数据可观测性**：在四条 ES 路径每次候选上记录 `ModelReady`、快照年龄、ATM/期权覆盖、最近 ES 价格时间、Rithmic 连接状态和阻断原因。把 `Unavailable`、`Stale`、`EsSourceOnly`、本地 NQ/MNQ 质量阻断分成独立计数。
2. **将 ES 延迟与路径胜率解耦**：只有在快照年龄和覆盖满足门禁的成交才进入路径性能样本；数据不合格的候选进入“数据阻断样本”，不用于淘汰路径。
3. **优先研究 ESResponseAlignedBreakout**：它成交量最大且总体毛亏损，重点对比成交前 ES 快照年龄、ES 事件到 NQ/MNQ 入场的秒数、NQ/MNQ 自身实体/收盘确认和 MFE<1R 的比例。第一候选是缩短事件到入场延迟、禁止迟到追价，而不是简单放宽质量分。
4. **谨慎处理 ESExpectedMoveExpansion**：NQ 样本亏损金额大，先检查点值/风险缩放和 `ExpectedMoveExpansionLate`，避免用 MNQ 的点数门槛直接套到 NQ。
5. **暂不改动小样本路径**：`ESWallRejectionReversal`、`ESGammaConcentrationResponse`、`FailureReverse_RetestFailed` 暂无足够同版本样本，继续采集。

本报告只读，不改变策略逻辑、配置或实际下单行为。
