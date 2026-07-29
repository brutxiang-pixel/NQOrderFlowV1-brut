# v2.25：v3.0 阶段 1B 微观结构 Data Only 采集器

日期：2026-07-27  
版本：`OPF_RESEARCH_2.25 / ACTUAL_EXEC_2.47`

## 目的与边界

本版本只验证 ATAS Historical Replay 是否实际提供可审计的逐笔与 DOM 事件。不改变入场、退出、仓位、日损、周 Long 门、并发风险或订单生命周期。

新增面板开关 `Microstructure Audit Collection Only`，默认 `false`。设为 `true` 时，策略在 `OnStarted` 强制关闭 Actual 订单；研究状态机和日志继续运行。

## 采集内容

按 M5 Bar 与回调来源聚合到 `microstructure_audit_bars.csv`：

- `OnNewTrade`、`OnNewTrades`
- `MarketDepthChanged`、`MarketDepthsChanged`
- `OnBestBidAskChanged`
- 每根新 M5 一次 `DepthSnapshot`

每行记录事件序列范围、首末事件时间、事件数、总量、Bid/Ask/Unknown 量、时间倒序数和价格范围。批量与单条回调必须分开解释，禁止相加后直接当成成交量；`DepthSnapshot` 与深度更新也必须分开解释。

## 安全设计

- 回调只在 Data Only 开关开启时记录；默认 Actual 路径没有额外采集逻辑。
- 数据仅在内存中按 Bar/来源聚合，在 `OnStopped` 一次性由 `ResearchLogger` 写出。
- 未使用 Cumulative Trade、Market-by-order、撤补单或任何订单 API。
- 快照读取失败只记入 `MICROSTRUCTURE_AUDIT_SUMMARY depthSnapshotErrors`，不影响策略运行。

## 1B 运行合同

彻底重启 ATAS 后，策略面板设置：

- `Microstructure Audit Collection Only=true`
- `Rich Bar Data Collection Only=false`
- `Decision Tape Calibration Collection=false`
- `Enable Replay Orders` 的面板历史值不影响本轮；Microstructure 开关会在启动时强制禁止 Actual 订单。

预注册日期：`2026-03-03,2026-01-16,2026-01-27`。每个 Snapshot 按中国时间 `06:00` 至次日 `05:00` 完整运行并正常停止。

每个 Snapshot 必查：

1. ConfigSnapshot 为 `OPF_RESEARCH_2.25 / ACTUAL_EXEC_2.47`。
2. `research.log` 包含 `MICROSTRUCTURE_AUDIT_COLLECTION_ONLY enabled=true actualOrders=false`。
3. HUD 显示 `ActualExec: OFF`，无任何 Actual 订单。
4. 存在 `microstructure_audit_bars.csv`，或明确记录没有回调；停止时存在 `MICROSTRUCTURE_AUDIT_SUMMARY`。

阶段 1B 只判定数据可得性、排序、量级与零订单，不评价收益。
