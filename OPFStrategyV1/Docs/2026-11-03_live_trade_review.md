# 近期实盘模拟订单与 ES 数据可用性复盘

日期：2026-11-03（北京时间）

## 结论

当前实际下单路径为 6 条。订单库中 2026-10-20 之后、策略版本为 `OPF_INTRADAY_2.10` 的记录只有 4 笔，全部来自 MNQ：`ESExpectedMoveExpansion`、`ESGammaConcentrationResponse`、`FailureReverse_RetestFailed` 各1笔盈利，`ESResponseAlignedBreakout` 1笔亏损。因此不能用这4笔评价六条路径的长期胜率。

更完整的近期订单库（混合多个版本）显示：ES代理路径成交主要集中在 `ESResponseAlignedBreakout`，NQ表现弱于MNQ；但样本跨版本，只适合定位问题和生成假设，不能作为参数优化的充分证据。

## 路径与品种摘要

| 范围 | 路径/品种 | 笔数 | 盈利/亏损/持平 | 毛盈亏 | 观察 |
|---|---|---:|---:|---:|---|
| 10-20 UTC 起 | ESResponseAlignedBreakout / MNQ | 33 | 17 / 16 / 0 | -$111 | 22笔MFE<1R；优先复核本地确认和入场时点 |
| 10-20 UTC 起 | ESResponseAlignedBreakout / NQ | 14 | 7 / 6 / 1 | -$140 | 8笔MFE<1R；样本跨版本 |
| 10-20 UTC 起 | ESExpectedMoveExpansion / MNQ | 11 | 5 / 6 / 0 | +$58 | 样本小，仍有宽风险亏损 |
| 10-20 UTC 起 | ESExpectedMoveExpansion / NQ | 7 | 3 / 4 / 0 | -$585 | 亏损集中在较高合约点值；需继续评估风险缩放与扩张质量 |
| 10-20 UTC 起 | ESGammaConcentrationResponse / MNQ | 2 | 2 / 0 / 0 | +$113 | 样本不足 |
| 10-20 UTC 起 | ESWallRejectionReversal / MNQ+NQ | 3 | 3 / 0 / 0 | +$963 | 样本极小，不据此放宽门禁 |

## ES 数据问题的证据

### 逐单延迟与新鲜度审计

已将2026-10-20 00:00 UTC以来70笔ES家族成交关联到execution event的唯一SignalID和同一策略快照下的期权门禁包，70/70精确匹配。审计脚本为 `Scripts/Audit-OPFEsEntryTiming.ps1`，逐单CSV输出至独立目录，不修改ATAS原始日志。

| 门禁评估时的期权报价年龄 | 笔数 | 毛盈亏 | MNQ | NQ |
|---|---:|---:|---:|---:|
| <=60秒 | 33 | +$1,101 | 21笔，+$126 | 12笔，+$975 |
| 61-90秒 | 37 | -$803 | 27笔，+$57 | 10笔，-$860 |

报价年龄分层同时混有不同品种、路径及版本，不能证明报价延迟导致盈亏。2.10的3笔ES路径订单中，期权报价年龄分别为83.0秒（扩张盈利）、17.3秒（Gamma盈利）、44.7秒（突破亏损）；ES underlying观测年龄约0.5-1.2秒。突破亏损单从SignalID内时间戳到ENTRY_SEND约18.4秒，但SignalID时间不是经独立校准的ES交易所事件时间，不能据此设传播延迟阈值。已成交样本的期权报价年龄均未超过当前85秒发布/90秒消费边界；被阻断候选没有订单结果，不能与成交样本绩效混算。

2026-11-03研究日志显示当日存在ES路径拒绝和代理数据阻断。当前导出脚本对拒绝日志做跨实例去重/分组，但没有可靠的当日逐事件过滤摘要，因此本报告不引用拒绝数量，也不把持续重复采样的状态计为独立机会。逐条原因仍可从 `es_candidate_rejection_funnel.csv` 对应快照检查；Gamma*OI等持续状态的采样次数不等于候选数。

统计口径：订单库 `order_ledger.csv`，按入场UTC时间自2026-10-20 00:00筛选，4条ES路径，版本2.6-2.10，共70笔，TradeID无重复。账本最后一笔入场为2026-10-28 00:11 UTC（北京时间08:11），账本无新单不能证明其后所有运行日志都无成交。4笔2.10订单合计毛收益+$155.50；按每笔1手及往返手续费$1.18估算净收益+$150.78，仅作小样本描述。此前表格因按本地时间解析CSV出现过误计，现统一采用UTC重算。

2.10突破亏损单MFE为0.5067R，ProtectionMoved=False，结果接近原始计划风险；该笔没有“已有大浮盈未锁住”的证据。混合版本突破路径摘要曾报告26笔亏损；账本筛选本次显示相应亏损中可确认25笔 `INITIAL_RISK_LOSS`（未移动保护、SL）及2笔 `BE_PROTECTED_SMALL_LOSS`（有保护事件、SL），合计27笔。由于旧摘要的26笔筛选范围未能从当前账本条件复现，不保留“26笔中24+2”的精确归因；应以TradeID和明确版本/日期过滤重新核验后再引用。

近期决策日志还出现 `OptionsGate:EsSourceOnly`、`NQOwnConfirmationUnavailableV29`、`MTF:ContinuationAgainstTwoFastFrames` 和 `Context:ImmediateObstacle`。这说明有些候选在数据源识别或本地质量/多周期门禁阶段被拒绝；并不等价于四条ES路径全部因同一原因阻断。

10月30日快照记录 `CONNECTOR_DISCONNECTED`；11月2-3日快照以候选与阻断记录为主，未见对应的新 `ENTRY_SEND`/`EXIT_FILLED` 订单链。因此近期无单不能直接归因于路径放行过严，连接和ES数据可用性是独立阻断因素。

## 本轮实施与下一步

1. ES门禁数据阻断日志增加品种、代理状态、详情、快照时间、期权/underlying接收时间和覆盖率；OptionsGate拒绝时归档完整 `OptionsGatePacket`。
2. 新增只读审计脚本，按精确SignalID和快照关联成交与门禁包，导出成交级时延、报价年龄和路径/品种分层；原始日志保持只读。
3. 不调整下单门禁。现有报价年龄分层不足以支持因果结论，后续应按同版本、同品种、同路径累积样本。
4. 后续优先记录可验证的ES参考采样时间、门禁时间、ENTRY_SEND和成交回报时间；SignalID时间不能代替交易所事件时间。
5. `ESExpectedMoveExpansion` 的NQ亏损仍需更多同版本样本；现有1.10扩张上限保持不变。

本轮仅增加审计数据，不改变路径门禁、风险或实际下单行为。
