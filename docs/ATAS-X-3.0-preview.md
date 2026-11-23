# ATAS X 3.0.0-preview.1 迁移

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

X 完全退出后备份现有插件 DLL，分别复制两份产物到 `%APPDATA%/ATAS X/Strategies` 和 `%APPDATA%/ATAS X/Indicators`。本机已有旧策略及 `ACTUAL_EXEC_2.52` 配置，自动订单开关为开启。安装前归档该配置并准备兼容的观察模式配置，首次启动应在 HUD 中看到 `OPF_X_3.0.0-preview.1` 和 `OBSERVE`。经典版配置中的账户及通知设置须按 X 环境重新核对。

开启交易验证前核对图表合约、账户、实时 ES 代理与原生冰山订阅。在模拟账户验证入场成交、CQG OCO 两张保护单注册、任一成交后兄弟单撤销、账户净仓、飞书 OPEN/CLOSE 及重启恢复。现有编译和回归测试未覆盖 X 的实际交易通道。
