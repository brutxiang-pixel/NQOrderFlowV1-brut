# ES/SPX 期权代理模型（经典版）

## 第一阶段：ES 供数，NQ/MNQ 自动交易（cross-market-v6）

2026-10-12：ES 图表实例固定为 SourceOnly，仅订阅 ES/SPX 期权链并生成上下文，本实例不自动开仓。NQ/MNQ 图表实例为 Consumer，不订阅自身期权链，每秒读取同机共享快照，下单候选再即时读取。ES/MES 自动交易留待后续阶段；本轮没有更改全局仓位、日损、日笔数或八条路径配置。

实时共享文件：%APPDATA%/ATAS/StrategyConfigs/OPF_es_options_proxy_latest.json。供数端最多每秒原子替换一次，历史文件 OPF_es_options_proxy.jsonl 最多每五秒归档一次；供数与研究日志开关独立。初始化显示 loadingSeries/loadingChain，失败或 25 秒超时后隔 30 秒重试；停启隔离旧异步任务与旧订阅，期权链到期后重新初始化。HUD 显示 SourceOnly/Consumer、ModelReady、覆盖率、FRESH/STALE/FUTURE、数据年龄及北京时间更新时刻。最新文件只供当前模拟/实时行情，历史 Replay 仍使用 JSONL 的 as-of 记录。

### 模拟账户测试步骤

1. 重启经典 ATAS 加载 Strategies/OPFStrategyV1.dll。先添加 ES 当前合约图表和策略，绑定模拟账户并启动。看到 ES OptProxy [shared-v4]: SourceOnly ModelReady，coverage 至少 50%、FRESH，确认北京时间 update 持续变化。ES 不会自动开仓。
2. 添加 NQ 或 MNQ 当前合约图表及同一策略，绑定模拟账户。策略设置 Manual Alert Mode=false（自动模式）、Enable ES Options Entry Gate=true。保留当前全局配置；HUD 的实际下单开关还必须为开启。Consumer 的时间、regime、CW/PW 与 ES 快照应一致（允许约 1 秒刷新差）。墙位是 ES 价格，不直接与 NQ 价格比较。
3. 等待原有八条路径出现候选。研究日志 OPTIONS_ENTRY_GATE 中 Allow 表示期权门禁通过，仍须通过原有交易时段、风险、槽位及路径门禁；拒绝时应记录具体 OptionsGate 原因。实际成交后检查平台上真实 SL/TP，以及平仓后的订单生命周期。没有候选不等于本轮接入失败，不主动发送测试订单。
4. 停止 ES 供数端，等待最后快照超过 5 分钟。Consumer HUD 应变为 STALE，新候选应 OptionsGate:Stale。重新启动 ES 并取得新数据后，Consumer 自动恢复 FRESH，无需重启 NQ/MNQ。
5. 历史 Replay 只能选已有 ES 期权 JSONL 归档覆盖的日期与时段；候选只使用 UTC 时点及之前、且距候选不超过 5 分钟的数据。没有归档的历史日期会被 DataUnavailable 阻止。回放期货 K 线不能补出历史期权链。

在原八条路径之外，当前配置新增四条独立实际下单路径：

- `ESResponseAlignedBreakout`：ES 60 秒响应达到 `max(0.5, EM×5%)`，方向与 NQ/MNQ 确认K线一致。
- `ESWallRejectionReversal`：ES 接近对应 Put/Call Wall（不超过 `max(1, EM×15%)`），并出现离开墙位的同向响应。
- `ESExpectedMoveExpansion`：ES 相对启动锚点偏离至少 `0.75×ExpectedMove`，NQ/MNQ 同向确认。
- `ESGammaConcentrationResponse`：最高 Gamma×OI 集中执行价占比至少 18%，ES 接近该价位且出现同向响应。该路径不假设 Dealer Gamma 正负，只交易实际价格响应。

四条新路径使用独立候选、独立 SignalId、独立 `OPTIONS_ENTRY_GATE` 和实际执行路径名，不是原八条路径的统计别名。它们仍受原有交易时段、日损、每日笔数、仓位槽位、风险、RR、SL/TP 和生命周期保护约束。

自动回归覆盖四条新路径门禁、到期/DST、共享刷新、无数据、历史未来排除、损坏尾行以及停止供数后的过期阻止，共 27 项。编译测试不能替代 ATAS 模拟账户的供数→候选→真实保护单闭环验证。

2026-10-12 修复（expiry-v3）：运行日志证实 E2AV6 当日期权 Expiration 为无时区的日期零点，旧实现以电脑当地时区转换后误判为过期，导致 189 个有报价的合约全部被拒绝。仅对识别出的 CME ES 欧式周期期权 E1A-E5D / EW1-EW5 日期型字段，按到期日芝加哥 15:00 转 UTC（常规提前收市日 12:00，自动处理 DST）；有明确时间的字段保留原时刻，未知/季度合约不套用 PM 到期规则。系列日期筛选使用芝加哥日历。规则来源：https://www.cmegroup.com/rulebook/CME/IV/350/358A%20.pdf 。HUD 改用短行，完整诊断保留日志。16 项门禁与到期回归检查通过；部署不代表 ATAS 运行时已确认 ModelReady，重启加载后须核对 expiry-v3 日志。

经典 ChartStrategy 使用 IOptionsDataProvider，默认启用下单门禁（Enable ES Options Entry Gate）。ES、MES、NQ、MNQ合约代码自动选择对应InstrumentProfile，点值分别为50、5、20、2美元/点。只接受ES/SPX相关标的，不能将NQ链作为ES链。

### response-v5 门禁修正

PDF 与 ATAS X-Ray 资料都强调：Gamma 的正负方向必须知道做市商实际持仓；仅凭 OI、Call/Put 或 bid/ask 推断会把未知当成事实。因此当前代理不再输出 `PositiveGammaProxy`/`NegativeGammaProxy`，改为 `DealerGammaUnknown`，只保留 Gamma×OI 的集中度、CW/PW、ATM 预期波幅和 ES 价格响应。

在 ES 60 秒响应数据可用且幅度达到 `max(0.5, EM×5%)` 时，BreakawayFvg 与 BreakawayFvg_Qualified 必须与 ES 方向一致，否则记录 `OptionsGate:ESResponseDisagreesWithBreakout`。ObservationConfirm、ObservationConfirm_WideStop1_5R、FailureReverse 两条路径及 SignificantZone 两条路径仍由各自结构确认和原有风控决定，ES 响应用于记录和上下文，不伪造 Gamma 方向硬门禁。这样没有把不可观察的 Charm、Vanna、Flow、Big Trades 变成假数据，也避免把 ES 绝对墙位直接比较到 NQ/MNQ。

当前输出状态：

- `Unavailable`：没有可用 provider、期权系列、ES/SPX 合约或足够的 OI。
- ModelReady：有效期权中间价反推IV，再使用ATAS BlackScholesPricer计算Gamma。利率/分红为零，Call正Put负为持仓假设，不代表真实做市商库存。

代理字段：

- CW/PW：按Gamma×OI聚合的执行价最大暴露。
- ZG：留空，尚未计算空间Gamma Flip。
- EM：同执行价ATM Call+Put中间价。

门禁：数据缺失、来自未来、超过5分钟、有效报价覆盖不足50%时阻止。归一化Gamma代理>=0.08时，突破家族须有同向实体>=0.25R且收盘位于方向侧25%区间；代理<=-0.08时，显著区/FailureReverse家族须有同向实体>=0.1R且收盘位于方向侧35%区间。阈值是本项目研究假设，不是PDF验证过的参数。自动与人工提示共用门禁，仍接受其它风控。

NQ/MNQ只使用环境分类，不把ES/SPX墙位与NQ绝对价格比较。实时上下文归档到%APPDATA%/ATAS/StrategyConfigs/OPF_es_options_proxy.jsonl。同机NQ/MNQ可读取ES实例归档的有效上下文。历史Replay只取候选UTC时点及之前的记录；无历史期权记录会阻止，不得使用今天链回测过去。

OPTIONS_ENTRY_GATE日志记录每次候选决策。当前尚未实现跨连接自动发现SPX provider、Charm/Vanna、真实Flow以及ES/NQ实时同步。部署和编译通过不证明运行时已取得ES/SPX链。
