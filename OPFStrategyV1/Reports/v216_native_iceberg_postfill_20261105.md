# OPF Intraday 2.16 冰山反转与成交后保护修复

## 变更

1. 保护观察在首笔实际入场成交时以成交价和成交时间启动。逐笔成交按事件时间更新；入场所在K线整段高低点不再参与0.75R保本和利润锁观察。只纳入开始时间严格晚于成交时间的完整后续K线。该措施避免前置极值误触发，但不承诺止损改价后不会因跳价或成交滑点产生亏损。
2. 冰山出现时冻结事件前30秒成交结构。若最近5秒价格向信号反方向移动至少max(4 ticks, 0.1 ATR)，该次信号按逆势处理：价格先突破局部结构1 tick，之后回踩到结构锚点，随后保持在顺势一侧1 tick至少1秒才准入。顺势事件仍走原2秒停留及主动成交量比>=0.15规则。
3. 同方向原生冰山实际亏损完成后，同交易日同方向新信号需在亏损形成的30秒结构边界之外完成同一突破-回踩-重新站回确认。旧事件不能借用该确认；等待持仓切换/保护清理之后会重新校验事件有效期和结构状态。

所有价格阈值均为本轮明确的研究量化假设，不代表Rose原文或ATAS的原生冰山定义。研究门禁最长受现有30秒事件寿命限制，可能减少信号或错失快速V形反转。当前没有本轮Replay收益结果；目标盈利与胜率未证明。

## 回归证据

验证结果：IntradayEntryTests 136项通过，LiveProtectionFailClosedTests 27项通过，OptionsProxyGateTests 81项通过。首次 ValidationReplayTests 失败 `ESGammaConcentrationResponse frozen input round trip` 后复核发现，通用合成快照的 Gamma 集中位为5000、Put Wall代理为4990，按生产门禁这组未映射数据本应拒绝；修正为该路径专用有效快照（Put Wall代理5000）后，有效输入可通过 round trip。生产门禁未修改。Release构建通过且已部署。新DLL SHA-256为 `06A9E1B26C4E55C8C428D0FDD6C92A9DC6B102CCF411880A7DCC4E6C0C221876`，修改前DLL SHA-256为 `d8114eef0bf65c12b811304418f2b3c8935d876e5e9ea686af272191b214d973`。原版与新版DLL、项目文件和完整测试/构建输出保存在 `OPFStrategyV1/Reports/v216_native_iceberg_postfill_evidence/`；完整源码与工作区快照见本轮外部备份。

2026-11-05 测试复核补充：ValidationReplayTests 31项通过，新增将“GammaStrike=5000、PutWallProxy=4990”的未映射快照序列化往返后仍必须返回 `OptionsGate:GammaUnsignedLongRequiresPutWall`；OptionsProxyGateTests 81项通过。确认之前的失败原因是无效通用测试夹具被当成应通过样本，非生产代码回归。本次只变更验证测试与文档，没有重建或重新部署 DLL。

IntradayEntryTests 覆盖长短入场K线污染、成交后价格更新、过期乱序行情、后续K线、多空突破回踩确认、顺势兼容，以及用户案例：入场28399.75、初始风险16点，0.75R触发价28411.75；成交后MFE仅9.25点的观察值不得触发该保护。

## 发布

版本：OPF_INTRADAY_2.16 / OPF_RESEARCH_2.34。部署DLL SHA-256为 06A9E1B26C4E55C8C428D0FDD6C92A9DC6B102CCF411880A7DCC4E6C0C221876；修改前DLL SHA-256为 D8114EEF0BF65C12B811304418F2B3C8935D876E5E9EA686AF272191B214D973；自动部署备份和本地Git恢复点见主线MD。基线审查：同目录 baseline-review.before.json 与 baseline-review.before-release.json。
