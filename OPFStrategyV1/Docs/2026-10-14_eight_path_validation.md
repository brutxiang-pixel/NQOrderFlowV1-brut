# 八路径固定验证包（OPF_INTRADAY_1.1）

目的：把修改后的功能检查缩短到本地数十秒，减少每次改版等待自然行情。功能通过不代表收益改善。主八路径保持当前逻辑；原生冰山为独立实验，不并入八路径成绩。运行中的 ATAS 不切换、不清日志、不发送测试单或飞书消息。

## 一键入口

在项目根目录运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File OPFStrategyV1/Scripts/Invoke-EightPathValidation.ps1
```

输出在 `OPFStrategyV1/Evidence/Validation/时间戳/`：待换版 DLL、五组测试输出、正式构建日志、SHA256、输入清单、门禁重放、八路径漏斗、有效净收益、排除交易、产品/方向/时段/账户/模式分层与真实期权归档可用时段。目录被 Git 忽略，不上传账户流水。脚本不复制带 webhook 的配置。

真实固定输入来自新版本的 `_validation_options.jsonl` 和 `_validation_reaction.jsonl`。脚本先复制为不可变输入，随后调用与生产相同的 `OptionsProxyEntryGate.Evaluate` / `IntradayEntryPolicy.Reaction`；逐条核对当时拒绝原因或 Allow。无真实文件时明确输出 NO_REAL_FIXTURES。构造样本只验证八路径的函数接线、序列化、未来数据拒绝、到期和订单流条件，不纳入收益。

前后比较必须使用同一个固定包：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File OPFStrategyV1/Scripts/Invoke-EightPathValidation.ps1 -FixtureDirectory '上一包绝对路径/fixtures' -BaselineDirectory '上一包绝对路径'
```

输入文件数或 SHA 不同会拒绝比较。行为变化生成 `gate-differences.csv`；与原观察不同的结果返回失败，须明确评审变化。

## 采集范围与边界

统一决策输入记录：实际候选/方向/路径、入场K线与证据K线、当前报价、止损与风险、已完成多周期、日均线、显著区/毛刺/大单/吸收代理上下文、最近30秒成交带、账户范围、人工闭锁、日损/单量及活动订单。实际期权门禁在调用点冻结当时完整 ES 代理快照与决策 UTC；盘中反应在状态变化或通过时冻结价格、持续时间、订单流和追价参数。研究写入失败只归档错误，不阻止订单决策。

这些数据足以固定重放已抽取的期权和反应门禁，**尚不是完整候选生成器、全量逐笔行情或券商回调重放器**。未来优化候选生成和订单生命周期必须继续使用对应功能测试与 ATAS 验收。缺失期权、MBO、完成日线时不能补造。只记录最终候选，不能拿候选总量代表所有潜在图形机会。

净收益仅关联同 SnapshotID + TradeID 的正常闭仓流水，且成交数量与账本数量必须一致。测试单、冰山、异常交易、缺净收益账本或数量不一致单独排除。旧流水没有账户/Replay字段时只能作历史参考；不得把不同策略版本合并宣称盈利。Execute 决策不是成交。浮盈回吐为 max(0, 实际MFE_R - 实际PnL_R)。

## 固定 ATAS 验收

先停止并移除旧实例、关闭 ATAS，再用本包 DLL 换版；重启后以 HUD 和 ConfigSnapshot 的 OPF_INTRADAY_1.1 确认载入。先模拟账户，不开启测试开关时启动应不自动创建测试单。

1. 使用 `options-replay-windows.csv` 中真实覆盖段，并核对图表有同段逐笔数据；先预热 M5 和 ES 响应，不使用未来快照。表只证明期权归档覆盖，不能证明券商历史行情可用。
2. 默认关闭的测试单开关手动开启、重新启动后只执行一次；查看 ENTRY/SL/TP 三笔真实订单、Parent/OCO关联，开关自动关闭。测试单不计八路径收益。
3. Long 到 TP、Short 到 SL 各验证一次，退出后另一保护单撤销，无孤立残单；BE 修改真实 SL，尾盘/time-stop 平仓前保留保护。
4. 单独验证人工提前平仓和移动保护单；出现人工干预后应闭锁自动新开仓，重启前不能马上反向自动补单。
5. 飞书通知验收包含产品、相同交易标识、明确开仓/平仓、北京时间。Replay 是否推送以现有配置为准，不因离线脚本主动推送。
6. 两个产品/实例分别验证账户标识稳定，余额变化不改变 AccountScope；旧动态 scope 日损从唯一实际成交重建，无法重建时显示 LegacyPnlScopeUnresolved，不能忽略旧损益。

实时模拟账户只补验 Replay 无法代表的连接器回调、真实附属单/撤单确认及通知；不再用于等待全部路径的函数检查。

## 同步修正

- LiveAccountScope 优先读取 SDK 实际 `AccountID`；不再从动态余额/PnL描述生成身份。
- 旧 `_Bal_` scope 纳入日损恢复审查；唯一 TradeID 防止旧/新重建记录重复累计。
- HUD 显示真实策略版本；ES模块注明主8路径+独立冰山。
- 冰山显示 started、事件数量、最近事件时间，订阅成功与原生冰山确认仍区分。

本轮不更改路径阈值、风控金额、SL/TP规则或下单模式，也不自动部署到运行实例。
