# 2026-10-14 盘中入场、多周期与原生冰山整轮升级

本轮一次性实现后统一验证，目标版本 `OPF_INTRADAY_1.0`，经典版程序集 `OPFStrategyV1.dll`。构建和离线测试通过不代表模拟盘成交验收通过。

## 下单路径

既有八条继续保留，新增第九条独立 `NativeIcebergReaction`。四条原 OPF 路径与四条 ES 代理路径经过同一实际下单、日损、报价和原生保护链。

| 路径 | 本轮执行触发 | 核心变化 |
|---|---|---|
| ObservationConfirm | 已确认观察区边界突破后盘中确认 | 基础质量至少配置值56，取消低分补量豁免；突破至少1 tick、保持2秒、最近10秒主动成交方向占比至少10%；超出触发位半个结构风险不追单 |
| BreakawayFvg_Qualified | FVG出生收盘确认后，后续3根内回踩再突破 | 不用未收盘FVG；原Qualified出生样本继续研究，实际等待回踩反应 |
| FailureReverse_ObservationInvalidated_WideStop1_5R | 观察区失效收盘确认后，回测失败盘中触发 | 保留结构宽止损，3根有效窗口，方向仍仅Short |
| FailureReverse_RetestFailed | 先触达，再独立事件突破反转区边界 | 不把同一个事件的触达与失败混为一步，方向仍仅Short |
| ESResponseAlignedBreakout | ES五分钟响应同向＋突破此前3根已闭合M5结构 | 不再只过上一根高低点，也不再排队等下一根开盘 |
| ESWallRejectionReversal | 既有ES墙触达收回证据＋本品种盘中反应 | 保留墙覆盖与触达证据，使用当前反应证据重查期权门禁 |
| ESExpectedMoveExpansion | ES锚定EM达到75%且不超过125%＋本品种结构突破 | 限制已耗尽后的追价 |
| ESGammaConcentrationResponse | ES集中度和价格响应＋本品种盘中反应 | 集中度仍为代理假设，不能冒充官方X-Ray或做市商真实仓位 |
| NativeIcebergReaction | 原生刷新确认＋2 tick价格离开＋保持2秒 | 双向、优先处理、独立槽；删除/过期事件不用；历史加载排除 |

所有盘中反应仍要求有效逐笔行情，250ms评估节流。同一候选只尝试一次；未成交不在每tick重发。收盘基线只归档。

## 多周期与位置上下文

使用经典SDK `CandlesDataProvider.SubscribeCandles` 订阅15M、30M、1H、4H、6H、D。只使用截至决策时刻完整的蜡烛，日线使用下一交易日开端判断上一日完整。请求220日历史；SMA10/20/60/120缺足量完整交易日时为N/A，不伪造。

延续类在15M/30M/1H中至少两个有效周期斜率反向时拒绝；不要求全部周期同向。4H/6H/日线高低点、完整日线均线、对向有效显著区作为前方空间参考。前方空间小于费用＋2 tick滑点，或延续0.25R/反转0.1R近障碍阈值时拒绝。**没有恢复“最近结构RR≥1”全局门禁**。目标R和实际空间分别归档。

毛刺按场景：同向Exhaustion阻止延续；反向Initiative阻止逆势反转。缺毛刺证据不伪造强信号；吸收代理不能触发原生冰山路径。显著区既有来源与状态机继续保留，作为位置预期，不强制价格触区就交易。

## 退出、槽位和账户

- OC双向统一1.5R目标、0.75R触发实际BE、6根时间上限。
- ES四条保留1.5R目标和1R BE。
- 冰山0.75R目标、0.5R BE；预期盈利必须大于手续费＋2 tick滑点；45秒仍未到0.25R进展时主动退出。普通最小止损不会扩大冰山止损。
- 每笔1手、最多两槽。普通第二槽要求主仓实际BE已应用、独立结构；冰山同向可占独立槽但主仓必须有有效保护。组合初始风险上限仍300美元。
- 反向冰山先平旧仓，保护单在平仓前继续有效；确认旧仓结束、账户平仓、保护清理后重新核对候选。候选保留30秒，失效/删除/追价时丢弃。
- 人工模式只提示，不平旧仓、不发新仓。外部人工成交在自动模式中锁住后续自动入场；账户平仓后清理旧保护，不自动重入。
- 日损200、日20笔、无周损，现有本地配置已核对。
- 日损CSV补AccountScope、ProductCode、HistoricalReplay；新记录按账户、合约、模式隔离和去重。旧同日无账户记录优先用连接器当前账户的唯一成交ID重建；无法重建时保留日志并明确LegacyPnlScopeUnresolved，不能删日志或直接清零越过日损。
- 尾盘与时间退出保持SL/TP直到平仓成交，拒单时保留保护。

## 数据、归档与限制

原生接口 `IMboAnalyticsDataProvider.MboIcebergsChanged`；确认 `NativeRefreshConfirmed`、Hidden、Side、价格、事件时间；实时NQ与MNQ同到期合约可共享进程内原生事件（ES与MES亦同组），不跨不同合约月份。

原生事件写入 `%APPDATA%/ATAS/StrategyLogs/OPFStrategyV1/NativeIcebergArchive/native_icebergs_yyyyMMdd.jsonl`。Replay同时按Time与CapturedAtUtc过滤，未来事件不许使用。未采集日期显示REPLAY_ARCHIVE_MISSING。期权Replay仍需要当时ES代理归档。CQG没有原生MBO能力时不以代理补出原生冰山。

日志关键字：MTF_CONTEXT、ENTRY_CONTEXT、REALTIME_PREPARED、REALTIME_FUNNEL、REALTIME_TRIGGER、REALTIME_INVALIDATED、REALTIME_CLOSED_PAIR、NATIVE_ICEBERG_CAPABILITY、NATIVE_ICEBERG_REACTION、ICEBERG_ARBITRATION。成对归档包含早入时刻、收盘时刻、价差、时延、收盘前是否到止损。正常研究MFE/MAE继续记录；盘中候选不使用入场前当前K线高低点充当入场后收益。

## 统一验证与模拟盘步骤

离线回归：IntradayEntryTests（29项时间/反应/仲裁/归档/账户断言）、OptionsProxyGateTests、LiveProtectionFailClosedTests、FeishuNotificationTests。Release构建要求0警告0错误，部署文件SHA256必须与构建一致。

1. 重启经典ATAS，挂 `OPFStrategyV1.dll`，ES窗口提供代理，NQ/MNQ使用模拟账户；确认版本OPF_INTRADAY_1.0、ES ModelReady、MTF各周期、日线SMA或明确N/A、Iceberg能力状态。
2. 先不开测试单，核对历史加载期间没有自动成交。Replay只选有期权/冰山归档的日期；无归档只检验普通结构和缺失诊断。
3. 观察REALTIME_TRIGGER是否发生于当前M5内部，订单数量1手，并真实挂出SL/TP；OPEN/CLOSE飞书同订单标识与产品一致。
4. 模拟账户观察冰山同向独立槽、反向平旧仓后再开、新旧保护不残留，以及45秒无进展退出。无原生确认事件不能假称此项已验收。
5. 模拟手动平仓后应ManualInterventionDetected，后续不重入；停止并确认平仓/保护清理后重新启动才解除。

本轮没有代用户启动策略、发送测试订单/飞书消息，也没有清理旧日志。平台运行中的旧程序集仍需重启ATAS换新。

最终Release部署已完成，构建与部署SHA256一致：`236643874E4C10DFB31C64FF770F1CBF2DB5CDAD8758EC2AA37D6C3219F38363`。部署位置：`C:/Users/Administrator/AppData/Roaming/ATAS/Strategies/OPFStrategyV1.dll`。
