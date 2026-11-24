# ATAS X 3.0 预览版迁移

## 3.0.0-preview.11

2026-11-24 00:13:38（北京时间）已部署策略 DLL，文件版本为 3.0.0.11。安装文件与构建产物的 SHA-256 一致：`7FE73589EE725061DA7BFDDF74AA314ECE444EF11655474B9B85D9A580951A10`。旧版备份位于 `OPFIndicatorX/bin/deployment-backups/before-x3-preview11-20261124-001338`。待 ATAS X 启动后验证版本加载及模拟盘平仓通知。

图表切换周期导致策略停止后，继续通过账户连接跟踪已开仓订单的退出成交。仅在同账户、同合约的归属成交数量与开仓手数匹配且账户该合约净持仓归零时，按实际成交均价和时间发出平仓通知。观察在通知入队或 24 小时到期后解除，依赖 ATAS X 进程持续运行。

## 3.0.0-preview.10

同账户的 NQ 和 MNQ 按实际合约分别执行 ES 事件，突破锚点和止损冷却也按合约隔离。冰山剥头皮以盘口补回的行情时间匹配逐笔反应，并从当前买卖价及其时间戳校验报价。漏斗累计记录点差、报价时间和逐笔反应等拦截次数。

2026-11-23 21:54（北京时间）已部署文件版本 3.0.0.10。安装文件与构建产物的 SHA-256 一致，旧版备份位于 OPFIndicatorX/bin/deployment-backups/before-x3-preview10-20261123-215434。平台加载及运行验收待重启后进行。

## 3.0.0-preview.9 — 实时补单推断

- 六个品种的本地剥头皮可由原生 MBO 确认或实时补单推断形成候选。推断要求同一价位同一被动侧在两秒内观察到两轮主动成交、可见量下降及补回。盘口量变化只作为推断证据，单独的撤单或成交无法形成候选。
- 两类候选共用五秒逐笔反应窗口以及现有 IOC、报价、风险与退出检查。同价位同方向在一次尝试后五秒内合并处理。日志分别记录原生片段、推断片段及最终入场来源。
- 离线验证使用合成盘口与逐笔序列。真实 ATAS X 回调时序和成交表现待运行验收。

2026-11-23 17:54（北京时间）已部署策略 DLL，文件版本为 3.0.0.9。安装文件与构建产物的 SHA-256 均为 E2009F19CCDD636CB0E58574E6F11BB61B1A232CDD9501C9B9AE80061E8D402A。旧 DLL 备份在 OPFIndicatorX/bin/deployment-backups/before-x3-preview9-20261123-175419。

## 3.0.0-preview.8 — 六品种本地图表冰山剥头皮

`IcebergAbsorptionScalp` 在 MNQ、NQ、ES、MES、GC、MGC 的各自图表上执行，只接受图表本合约新确认的原生 MBO 冰山补量和本地逐笔成交吸收/收回确认。每个品种每次信号请求 1 手；保留 8 tick 最大初始止损、12 tick 目标、实时双边报价和手续费门禁。止损与目标按品种 tick size 对齐；相同 tick 数对应不同美元风险，需按合约点值评估。设置面板开关名称改为通用品种名称，底层旧属性键保留以兼容已保存图表。

Release 构建零警告、零错误。`IntradayEntryTests`、`OptionsProxyGateTests`、`ValidationReplayTests` 分别通过 259、98、31 项检查。待 ATAS X 关闭后部署。运行验收可按品种查看 `ICEBERG_SCALP_FUNNEL` 的原生确认、分段、信号、IOC 提交和成交计数。GC、MGC 的手续费从执行配置读取，未配置专用品种值时使用通用回退值。

## 3.0.0-preview.7 — MNQ 冰山补量分段

同一个 MNQ 原生冰山订单可以在新的补量后开启新一段确认。后续分段要求隐藏量和累计成交量同时增加，并以新增逐笔成交验证再次受压和收回。每段最多尝试入场一次。旧冰山路径在该事件的 5 秒剥头皮确认窗口内等待。一手 IOC、最多 8 tick 结构止损与 12 tick 目标继续适用。

`MNQ_SCALP_FUNNEL` 按 Globex 交易日每分钟记录当前运行实例的确认冰山 ID 数、补量段数、合格信号、IOC 提交调用和实际开仓成交笔数，并附当前等待原因。跨重启统计应按日志实例合并，开仓成交量级以完整交易日数据验证。

2026-11-23 已部署文件版本 `3.0.0.7`。Release 构建零警告、零错误，`IntradayEntryTests` 通过 247 项断言。部署 SHA-256 为 `06CA7CE57C654B3929B1F0321A4924F038DFAC43F3D01B5B574C85060DB1790E`，旧版备份位于 `OPFIndicatorX/bin/deployment-backups/before-x3-preview7-20261123-153054`。

## 3.0.0-preview.6 — 修复 ES 期权初始化

改用图表 SDK 的 `OptionsDataProvider` 获取 ES 期权系列、合约链和报价，由 ATAS X 执行自身的期权权限检查。策略核对 ES 的可用期权连接及合约链归属，只有确认 Rithmic 来源才接受。来源无法验证及订阅失败会写入 `ES_OPTIONS_PROXY_HEALTH`。实时验收以 `subscriptions > 0`、收到报价并进入 `ModelReady` 为准。

2026-11-23 已部署 `3.0.0.6`。Release 构建零警告、零错误，`OptionsProxyGateTests` 通过。部署 SHA-256 为 `800C0C337D73829F18BC3470F873FBD4802E3ED4B069F609B00FA481EE6CF295`，旧版备份位于 `OPFIndicatorX/bin/deployment-backups/before-x3-preview6-20261123-145113`。实时订阅待 ATAS X 重启后验收。

## 3.0.0-preview.5 — 策略版本与 ES 期权初始化诊断

策略设置 `OPF Profile → Strategy Version` 显示只读的完整构建版本。文件版本为 `3.0.0.5`。ES 期权初始化失败时，`ES_OPTIONS_PROXY_HEALTH` 的 `retry` 字段记录失败阶段和异常类型。期权数据仍限定使用 Rithmic。

2026-11-23 已部署至 ATAS X 策略目录。Release 构建通过，零警告、零错误，`OptionsProxyGateTests` 通过。部署 SHA-256 为 `A8AD4032982F05C6F2428800ECD9BA0AE9C3D3569396CCA57A3D7762BFA4F342`。旧版备份位于 `OPFIndicatorX/bin/deployment-backups/before-x3-preview5-20261123-121427`。图表面板和 Rithmic 实时订阅待重启验收。

## 3.0.0-preview.4 — MNQ 冰山吸收剥头皮

2026-11-23 已部署到 ATAS X 策略目录。部署文件与构建产物的 SHA-256 一致，均为 `AFAF8088FB11AAD69D23D6E5FF2ED4091354E3E91F68762FFE1AC1E76F041C33`。旧 DLL 备份在 `OPFIndicatorX/bin/deployment-backups/before-x3-preview4-20261123-115045`。

在 MNQ 图表的 `OPF Iceberg` 分类启用 `Enable MNQ Iceberg Absorption Scalp` 后，策略使用同一 MNQ 合约的原生冰山补量与逐笔成交。先观察对手盘攻击，再要求冰山事件到达后本地成交收回并接管。每个事件只评估一次有效入场，入场限一手，使用 IOC 限价。止损取吸收结构外一跳，计划风险最多 8 tick，目标距实际成交 12 tick。持仓满 20 秒且最大有利波动不足 3 tick 时，尝试通过现有退出流程平仓。该设置默认关闭，实时订单链路待连接测试。

## 3.0.0-preview.3 — Rithmic options and MBO sweep evidence

ES 期权到期系列、合约链和报价订阅均通过已连接的 `OFT.Rithmic`，由 ATAS `OptionsSubscriptionService` 处理。期权链只使用 Rithmic。断线或停止 ES 数据策略时，发布失效状态，实时消费者和回放都停止沿用旧期权模型。若仍有 ES 标的逐笔，策略可继续生成纯价格快照，墙位、预期波动和 Gamma 字段留空。

NQ/MNQ 冰山路径使用 ATAS 原生 `MboIcebergsChanged` 和图表 `OnNewTrade`。入场检查使用最近 10 秒的方向成交；新增 `MboAnnotatedTradesEmitted` 记录冰山附近的多档主动成交，供决策日志复盘。新增扫单证据暂只记录。ATAS X 与经典版 Release 构建以及 `IntradayEntryTests`、`OptionsProxyGateTests` 均通过。待 ATAS X 退出后部署，实时事件和期权报价回调待平台内验收。

## 3.0.0-preview.2

2026-11-23 已更新策略 DLL，SHA-256 为 `943CB0047C5C5916169AAC88F718C1FAA49F80F14A947975965C5425BD508024`。旧 DLL 备份在 `OPFIndicatorX/bin/deployment-backups/before-x3-preview2-20261123-100600`。策略列表名称带 `OPF_X_3.0.0-preview.2`。

新挂载实例默认勾选 `Enable Actual Orders`。已保存的图表勾选状态优先，`Manual Alert Mode` 和数据采集模式仍可关闭执行。启动后 HUD 显示实际执行状态。策略从图表识别 MNQ、NQ、ES、MES、GC、MGC，MNQ 最多两手，其余最多一手。ES 提供期权上下文并参与交易，MES 与 NQ/MNQ 使用 ES 的上下文，GC/MGC 使用两条本地路径。

ATAS X 的历史订单绘图取消根数和笔数限制，范围由当前账户、合约、本地归档和已加载的图表数据决定。本版构建 0 警告、0 错误，238 项定向断言通过。实际图表显示和订单链路待启动验证。

## 3.0.0-preview.1 迁移记录

2026-11-23 验证本机 ATAS X 8.0.16.683 的 MCP 初始化、143 个工具及工作区读取。MCP 下单、改单、撤单和关闭仓位均需要在 ATAS 平台确认。期货、股票及期权的行情读取受 AI Connect 数据权限限制。新开仓的人工确认工作流可用 `orders_place` 附带 `stopLoss` / `takeProfit`，现有仓位使用 `orders_place_oco`。本轮仅验证只读调用。

- [ATAS AI Connect 帮助](https://help.atas.net/en/support/solutions/articles/72000663243-ai-connect-connect-your-ai-agent-to-atas)
- [ATAS 原生图表策略](https://docs.atas.net/en/chart-strategies/overview.html)

## 构建产物

- `OPFStrategyV1/bin/ATASX/Release/net10.0-windows/OPFStrategyV1.dll` 使用 X SDK 构建，同源复用经典版全部策略与执行路径。`OPF_X_3.0.0-preview.1` 配置与日志落在 `%APPDATA%/ATAS X`。
- `OPFIndicatorX/bin/Release/net10.0-windows/OPFIndicatorX.dll` 保留原指标，新增近期原生买卖冰山价格点。价格点表示收到冰山事件。独立的跨市场反应、入场及保护单逻辑由图表策略处理。

```powershell
dotnet build OPFStrategyV1/OPFStrategyV1.csproj -c Release -p:BuildForATASX=true -p:RunRepositorySnapshot=false -p:DeployToATAS=false
dotnet build OPFIndicatorX/OPFIndicatorX.csproj -c Release
```

## 安装和验收

2026-11-23 已在 ATAS X 退出后安装策略与指标 DLL，两份文件的 SHA-256 均与构建产物一致。旧 DLL 和活动配置已备份至 `OPFIndicatorX/bin/deployment-backups/before-x3-install-20261123-094401`。

活动配置为 `ACTUAL_EXEC_2.54`，`EnableActualOrders=false`，`ExecutionMode=ManualAlert`。待启动验收，预期 HUD 显示 `OPF_X_3.0.0-preview.1` 和 `OBSERVE`。账户及通知设置须按 X 环境重新核对。

开启交易验证前核对图表合约、账户、实时 ES 代理与原生冰山订阅。在模拟账户验证入场成交、CQG OCO 两张保护单注册、任一成交后兄弟单撤销、账户净仓、飞书 OPEN/CLOSE 及重启恢复。现有编译和回归测试未覆盖 X 的实际交易通道。
