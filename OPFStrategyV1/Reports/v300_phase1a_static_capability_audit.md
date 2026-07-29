# v3.0 阶段 1A：微观结构静态能力审计

日期：2026-07-27  
范围：只读检查当前源码、项目引用与 ATAS 已安装程序集；未启动 ATAS，未编译 DLL，未写活动日志。

## 结论

1. 当前 `OpeningPullbackFailureStrategy` 继承 `ChartStrategy`，实际只使用 `BestBid` 与 `BestAsk` 作为执行报价边界。
2. 当前 Decision Tape 是从 `OnCalculate` 市场价格转折压缩得到，并配合候选/订单的 Bid/Ask 锚；它不是每笔成交带，也不含盘口深度事件。
3. 当前工程没有 `NewTrades`、`OnNewTrade`、`MarketDepth`、`MarketByOrder` 或 DOM 快照的消费/持久化实现。因此，现有 2026-01 至 2026-07-24 冻结 Replay 不能被假定为可直接用于 L2 特征研究。
4. ATAS 已安装引用程序集的符号表包含如下能力名称：
   - `ATAS.DataFeedsCore`：`NewTrades`、`MarketDepthsUpdate`、`MarketByOrderChanged`、`IMarketDepth`、`ProcessTick`。
   - `ATAS.Indicators`：`OnNewTrade`、`OnCumulativeTrade`、`MarketDepthsChanged`、`GetMarketDepthSnapshot`、`IMarketDepthInfoProvider`。
   - `ATAS.Strategies`：`OnMarketDepth`、`MarketDepthsUpdate`、`BestBidAskUpdates`。

这只说明平台二进制暴露了相关能力，不说明当前 `ChartStrategy` 可直接订阅全部接口，也不说明 Historical Replay 会回放完整、排序稳定的逐笔或 DOM 事件。

## 当前可用数据与缺口

| 数据层 | 当前状态 | 可用于 v3.0 的结论 |
|---|---|---|
| 决策价格与 M5 转折 | 已有 Decision Tape | 继续用于执行校准；不能代替逐笔成交 |
| 最优 Bid/Ask 报价 | 已有执行预检和候选锚 | 可继续用作入场边界 |
| 逐笔成交价格/数量/方向 | 未采集 | 必须由 1B 验证事件来源、排序、方向与 Replay 覆盖 |
| Footprint / POC | 未采集 | 不得假定历史可得 |
| DOM 前 N 档/变化 | 未采集 | 不得假定 Historical Replay 可得 |
| Market-by-order / 撤补单 | 未采集 | 未验证前不得作吸收、冰山或真假突破推论 |

## 阶段 1B 的最小经验审计

等待冻结 v2.24 Replay 结束、ATAS 空闲且日志已归档后，使用独立 Data Only 运行验证：

1. 逐笔：事件时间非递减、采集 Sequence 单调、价格/数量可聚合回 M5，总量差异及 Unknown 主动方向占比按日输出。
2. 报价：最优 Bid/Ask 的事件顺序与既有 Decision Tape/入场预检对照。
3. DOM：前 N 档快照是否到达、更新事件是否连续、是否能区分重置与增量；不能证明增量时禁用撤补单类特征。
4. Replay 与实时分别记录；二者不可因名称相同而视为等价。
5. 采集逻辑必须只读、决策后执行、内存缓冲，零影响按控制组 Replay 抖动范围和生命周期危险事件为零验收。

## 结论对后续计划的约束

- 阶段 2 不得在 1B 通过前启动。
- 若 Historical Replay 无法提供可信 DOM，v3.0 保留逐笔/Footprint 研究线，永久暂停 DOM 历史收益研究线。
- 阶段 0 冻结 Replay 继续原样完成；本审计不授权 DLL、配置、日志或 ATAS 操作。
