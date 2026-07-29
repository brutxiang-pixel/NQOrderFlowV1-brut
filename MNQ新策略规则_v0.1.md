# Opening Pullback Failure 新策略规则 v0.1

> 策略目标：建立一套可编码、可回测、可解释、可扩展到多品种和多执行目标的 Pullback / Failure 框架。v0.1 默认研究场景是 MNQ 单手、日内目标 150-200 美金，但这只是 `InstrumentProfile + ExecutionProfile` 的默认组合，不是 StrategyEngine 的硬编码目标。以后目标改为 300 美金、增加手数、或切换到 NQ / ES / MES / GC / MGC，应优先通过 profile 调整，而不是重写策略核心。

## 0. 当前默认研究场景

v0.1 的第一轮回测默认使用：

- 品种：MNQ。
- 手数：1 手。
- 目标：日内盈利 150-200 美金。
- 日亏损保护：先以 100 美金作为默认研究值。
- 目的：先验证 Entry 是否有 edge，而不是优化所有执行变量。

长期设计目标：

- 支持不同品种：MNQ / NQ / ES / MES / GC / MGC。
- 支持不同日目标：150 / 200 / 300 美金或其他值。
- 支持固定手数和未来风险手数。
- 支持用同一批 signal 回放不同 ExecutionProfile。
- StrategyEngine 不因目标金额、品种名称、手数变化而改变 setup 判断。

## 1. 核心设计原则

旧策略的问题不是单纯风控不足，而是进场机会质量不稳定。v1.29/v1.30 的限制能减少明显差的交易，但继续增加限制会让交易越来越少，进入“没有单就没有盈利”的误区。

新策略采用分层结构，并且代码层也必须分离 Strategy / Execution / Research：

1. Market Regime：先判断今天是否值得交易。
2. Setup：只寻找 Trend Pullback 或 Failure Reverse。
3. Trigger：必须有明确二次确认或失败回测确认。
4. OrderFlow：从硬过滤器降级为评分项，但明显逆向仍降低质量。
5. Risk：风险必须和波动率匹配。
6. Exit：先完整统计 MFE/MAE，再优化出场。

## 1.1 模块边界

### StrategyEngine

只负责研究意义上的交易机会，不处理实盘纪律。

输入：

- K线。
- VWAP / Opening Range / ATR。
- M5/M15 结构。
- Zone。
- OrderFlow。

输出：

- `CandidateSignal`。
- `SignalStage`。
- `SkipReasons`。
- `SetupQualityScore`。

职责：

- Regime。
- Setup。
- Trigger。
- Risk。
- EstimatedReward / EstimatedRR。

约束：

- StrategyEngine 不读取 DailyPnL、日目标、当前持仓数量。
- StrategyEngine 不直接下单。
- StrategyEngine 不因为 DailyGuard 改变信号判断。
- StrategyEngine 输出的每个信号必须可被 ResearchEngine 独立回放。

### ExecutionEngine

只负责实际是否执行。

输入：

- `CandidateSignal`。

输出：

- `ActualTrade`。
- `WouldTradeLive`。
- `ExecutionSkipReasons`。

职责：

- DailyGuard。
- MaxPosition。
- SessionTime。
- Live/Replay 下单。
- 实盘纪律。

约束：

- ExecutionEngine 只能决定 `WouldTradeLive` 和实际下单。
- ExecutionEngine 不能修改 `SetupQualityScore`、`MarketRegime`、`EntryTrigger`。
- 因日目标、时间、持仓限制跳过时，只写 `ExecutionSkipReasons`，不写入策略层 `SkipReasons`。

### ResearchEngine

只负责记录和评估。

输入：

- 所有 `CandidateSignal`。
- 所有 `ActualTrade`。
- 后续 K线路径。

输出：

- MFE/MAE。
- HitR。
- NoTradeReason。
- CSV。
- 分析字段。

职责：

- 研究时即使 Execution 不交易，也继续跟踪信号后续表现。
- 让策略评估不被 DailyGuard 等执行规则污染。
- ResearchEngine 可以绕过 ExecutionEngine 直接统计全部 `CandidateSignal`。
- ResearchEngine 不改变 StrategyEngine 的信号结果，只追加后续路径统计。

## 1.2 SignalID

每一个潜在信号必须生成唯一 `SignalID`。

格式建议：

```text
SignalID = yyyyMMdd-HHmm-SIDE-SEQ
Example  = 20260704-0935-BULL-001
```

无论最终结果是：

- 成交。
- 因风险跳过。
- 因日目标跳过。
- 仅研究记录。
- 后续失败转化为反向 setup。

都必须保留同一个 `SignalID` 或明确记录 `ParentSignalID`。

Failure 反向信号建议：

```text
SignalID = 20260704-1010-BEAR-002
ParentSignalID = 20260704-0935-BULL-001
FailureSourceSignalID = 20260704-0935-BULL-001
```

## 1.3 SignalStage

每个信号按阶段推进：

- `Candidate`：Regime + Zone 满足，出现潜在 setup。
- `Confirmed`：二次确认或 failure retest 确认。
- `Triggered`：价格达到计划入场条件。
- `Executed`：ExecutionEngine 实际下单成交。
- `ResearchOnly`：策略有效但因执行纪律不交易，只跟踪后续表现。
- `Invalidated`：setup 失效。
- `Skipped`：被规则跳过。

日志必须记录阶段变化：

```text
SIGNAL_STAGE id=20260704-0935-BULL-001 from=Candidate to=Confirmed reason=ReclaimPassed score=84
```

## 1.4 InstrumentProfile：多品种支持

策略核心不能写死 MNQ。v0.1 研究目标先以 MNQ 单手为主，但架构必须支持以后扩展到 NQ / ES / MES / GC / MGC。

原则：

- StrategyEngine 只输出 points、R、score、signal。
- StrategyEngine 不直接使用合约美元价值判断 setup 好坏。
- InstrumentProfile 负责把 points 换算成 dollars。
- RiskEngine 可以使用 InstrumentProfile 的 tick、point、ATR 限制，但不能把某个品种名称写进策略判断。

InstrumentProfile 字段：

- `Instrument`：MNQ / NQ / ES / MES / GC / MGC。
- `TickSize`。
- `TickValue`。
- `PointValue`。
- `Currency`。
- `DefaultSession`。
- `OpeningRangeMinutes`。
- `RegularTradingHoursStart`。
- `RegularTradingHoursEnd`。
- `MinStopPoints`。
- `MaxRiskPointsHard`。
- `AtrRiskMultiplier`。
- `MinEstimatedRR`。
- `DefaultContracts`。

建议默认值以后放在配置文件，不写死在代码分支里。

示例：

```text
MNQ: PointValue = 2
NQ : PointValue = 20
MES: PointValue = 5
ES : PointValue = 50
```

实现要求：

- 所有美元字段必须通过 `PointValue * Contracts` 换算。
- CSV 必须记录当时使用的 `InstrumentProfileName`。
- 不同品种可以有不同风险上限和 session，但共用同一套 setup / trigger / research 结构。
- 如果某品种的 session 或波动节奏明显不同，先新增 profile，不修改 StrategyEngine。

## 1.5 Scoring Framework：统一评分框架

所有评分必须使用统一结构，避免 TrendScore、SetupQualityScore、ZoneScore、OFScore 各自实现。

统一评分组件：

```text
ScoreComponent
- Name
- RawValue
- Passed
- Weight
- Contribution
- Reason
```

统一评分结果：

```text
ScoreBreakdown
- ScoreName
- TotalScore
- Threshold
- Passed
- Components[]
```

示例：

```text
ScoreName = TrendScore
Component = SwingProgression
RawValue = HigherHighHigherLow
Passed = true
Weight = 30
Contribution = 30
```

适用范围：

- TrendScore。
- SetupQualityScore。
- ZoneQualityScore。
- OFScoreContribution。
- 未来 FailureScore / LiquidityScore / VolatilityScore。

实现要求：

- 每个评分项必须输出 `ScoreBreakdown`。
- CSV 至少记录总分和关键子项贡献。
- 详细日志必须能输出全部 `ScoreComponent`。
- 权重来自配置或常量表，不写散在条件判断里。
- 新增评分只能新增 component，不复制一套新的评分计算框架。

## 1.6 Version 与 ConfigSnapshot

每次回测必须能追溯到当时使用的策略、配置、检测器版本。

必须记录版本字段：

- `StrategyVersion`：例如 OPF_0.1。
- `InstrumentProfileVersion`。
- `ExecutionProfileVersion`。
- `ScoringConfigVersion`。
- `ZoneDetectorVersion`。
- `OrderFlowVersion`。
- `ResearchSchemaVersion`。

配置快照：

- 每次回测开始时生成 `ConfigSnapshot.json`。
- 每个 snapshot 生成唯一 `SnapshotID`。
- CSV、日志、交易记录必须记录 `SnapshotID`。
- 历史交易只能用自己的 snapshot 解释，不能用当前最新配置反推。

ConfigSnapshot 至少包含：

- StrategyVersion。
- InstrumentProfile。
- ExecutionProfile。
- Scoring weights。
- Trend thresholds。
- Risk parameters。
- ZoneDetector settings。
- OrderFlow settings。
- ExitPolicy。
- Research schema。

目的：

- 三个月后仍能知道某一笔交易来自哪一版规则。
- 阈值从 70 改成 75 后，历史 CSV 仍可重现。
- 多品种、多目标、多手数回测不会混成一份无法解释的数据。

## 1.7 ZoneDetector：独立 Zone 检测模块

Zone 不应深埋在 StrategyEngine 内部。ZoneDetector 独立负责识别和输出可交易区域，StrategyEngine 只消费标准化 zone。

ZoneDetector 输入：

- K线。
- VWAP / VPOC / Opening Range。
- FVG / OB / Demand / Supply 识别所需数据。
- 未来可扩展 Volume Shelf / Delta Cluster / VWAP Band / Composite VAH。

ZoneDetector 输出：

```text
DetectedZone
- ZoneID
- ZoneType
- Direction
- Low
- High
- CreatedTime
- Freshness
- TouchCount
- Mitigated
- Source
- DetectorVersion
```

约束：

- StrategyEngine 不直接生成 zone。
- Setup 只能消费 `DetectedZone`。
- 替换 ZoneDetector 时，Setup / Trigger 尽量不改。
- CSV 必须记录 `ZoneID`、`ZoneType`、`ZoneDetectorVersion`。

## 1.8 Replay Capability：信号回放能力

ResearchEngine 后续需要支持按 SignalID 回放信号出现那一刻的上下文。

目标接口：

```text
ReplaySignal(SignalID)
```

Replay 至少还原：

- SignalID。
- SnapshotID。
- StrategyVersion。
- InstrumentProfile / ExecutionProfile。
- SignalStage。
- MarketRegime。
- ScoreBreakdown。
- DetectedZone。
- OrderFlow 状态。
- Risk / EstimatedRR。
- SkipReasons / ExecutionSkipReasons。
- 后续 MFE / MAE / Outcome。

实现要求：

- v0.1 可以先通过日志和 CSV 支持人工回放。
- 后续再做自动 ReplaySignal 工具。
- 任何信号如果没有 SnapshotID，就视为不可完整复盘。

## 2. v0.1 范围

v0.1 只实现最小可回测版本：

- Regime 只分：Trend / Unknown。
- Unknown 不交易。
- 不强行识别 Range 和 Reversal，只在 failure setup 中观察反转。
- 实现 Trend Pullback 二次确认。
- 实现 Long Failure -> Short，Short Failure -> Long 先只记录或严格限制。
- 新增完整交易过程统计和 No Trade Reason。

暂不做：

- 机器学习。
- 多合约分批。
- 太多参数优化。
- 复杂 Range 策略。

## 3. Market Regime：客观可编码

不要使用人工判断式描述。v0.1 使用 TrendScore。

### 3.1 TrendScore 组成

TrendScore 使用权重，不写死所有条件都是 1 分。v0.1 可以先用默认权重，但架构必须允许调权重。

默认权重，总分 100：

- SwingProgression：30。
- VWAPSide：20。
- OpeningRangeSide：20。
- VwapCrossCount：10。
- DirectionalDisplacement：10。
- AtrExpansion：10。

实现要求：

- 权重必须来自配置或独立常量表，不写进各条件判断内部。
- 每个条件只输出 true/false 和原始指标值。
- TrendScore 只由评分器汇总，方便以后调整权重而不改判定逻辑。
- 日志必须记录每个子项实际贡献分，而不是只记录总分。

### 3.2 TrendScore 条件

1. `VWAPSide`
   - Bull：最近 `N=5` 根 M5 收盘价至少 `4` 根在 VWAP 上方。
   - Bear：最近 `N=5` 根 M5 收盘价至少 `4` 根在 VWAP 下方。

2. `VwapCrossCount`
   - 最近 `M=12` 根 M5，收盘价穿越 VWAP 次数 `<= 2`。

3. `SwingProgression`
   - Bull：最近两个有效 Swing High 抬高，且最近两个 Swing Low 抬高。
   - Bear：最近两个有效 Swing High 降低，且最近两个 Swing Low 降低。

4. `OpeningRangeSide`
   - Bull：价格在开盘区间上沿上方运行，最近 5 根至少 3 根收盘在上沿上方。
   - Bear：价格在开盘区间下沿下方运行，最近 5 根至少 3 根收盘在下沿下方。

5. `AtrExpansion`
   - 当前 M5 ATR(14) `>=` 最近 50 根 ATR(14) 均值的 `1.1` 倍。

6. `DirectionalDisplacement`
   - 最近 10 根内出现过同方向 displacement K，实体或收盘推进幅度 `>= 1.2 * ATR(14)`。

### 3.3 Regime 判定

- Bull Trend：Bull TrendScore `>= 70`，且 Bear TrendScore `< 70`。
- Bear Trend：Bear TrendScore `>= 70`，且 Bull TrendScore `< 70`。
- Unknown：其他全部情况。

v0.1 不交易 Unknown。Unknown 比错误分类更安全。

### 3.4 Regime 日志

每次 Regime 改变时记录：

```text
REGIME_CHANGE date=... bar=... regime=BullTrend bullScore=80 bearScore=20 swing=30 vwapSide=20 openingRange=20 vwapCross=0 atrExp=10 disp=0
```

## 4. Setup A：Trend Pullback 二次确认

Trend Pullback 只在 Trend Regime 下允许。

## 4.0 Pullback 次数定义

必须明确“第几次 pullback”，否则程序和人工回测会数错。

Bull Trend：

- 当价格从趋势推进高点回撤到有效 zone/VWAP/OR 区域时，创建一个 `PullbackEpisode`。
- 该 episode 在以下任一条件满足时结束：
  - 价格重新突破回撤前的 swing high；
  - setup invalidated；
  - 超过 `MaxPullbackBars`；
  - Regime 变为 Unknown 或 BearTrend。
- 在 episode 结束前，所有小回撤都属于同一次 pullback。

Bear Trend 反向同理，以重新跌破回撤前 swing low 作为完成条件。

PullbackCount：

- 每个 Trend Regime 内按完成的 episode 计数。
- v0.1 只允许第 1 或第 2 次 pullback 交易。
- 第 3 次以后只记录 `NoTradeReason=ThirdPullback`。

日志字段：

- `PullbackEpisodeID`。
- `PullbackCountInRegime`。
- `PullbackStartBar`。
- `PullbackEndReason`：BreakPriorSwing / Invalidated / MaxBars / RegimeChanged。
- episode 未结束前，不创建新的 pullback 计数。

### 4.1 Long Pullback

前置条件：

- Regime = BullTrend。
- 价格回撤到 Bullish FVG / demand zone / VWAP 附近。
- 该 zone 不是刚生成即追入。
- 这是当前趋势中的第 1 或第 2 次有效 pullback，第三次以后只记录不交易。

二次确认 Trigger 采用明确规则，不允许人工解释。

Long Trigger 满足以下 3 项：

1. `SweepOrProbe`
   - 价格探入 zone，或最低价跌破 zone 上沿至少 `2 ticks`。

2. `Reclaim`
   - 确认 K 收盘重新站回 zone 上沿；
   - 或确认 K 收盘突破前一根 K 高点；
   - 二者满足其一。

3. `MicroBos`
   - 确认 K 收盘突破最近 `3-5` 根的局部高点；
   - 如果没有局部高点，则该项为 false。

执行规则：

- `Reclaim` 必须为 true。
- `SweepOrProbe` 必须为 true。
- `MicroBos` 为加分项，不是硬要求。
- 确认后 1-2 根内不能重新收回 zone 深处，否则取消 setup。

入场：

- 激进：确认 K 收盘入场。
- 保守：确认 K 后小回撤入场。
- v0.1 默认使用保守入场，若 2 根内不回撤则放弃，记录 `SkipNoRetraceAfterConfirm`。

止损：

- 放在 sweep low 下方 `bufferTicks`。
- 不再机械放在原始 zone 下方。

### 4.2 Short Pullback

Short 反向同理。

Short Trigger：

1. `SweepOrProbe`
   - 价格探入 Bearish zone，或最高价突破 zone 下沿至少 `2 ticks`。

2. `Reclaim`
   - 确认 K 收盘重新跌回 zone 下沿；
   - 或确认 K 收盘跌破前一根 K 低点。

3. `MicroBos`
   - 确认 K 收盘跌破最近 `3-5` 根局部低点。

Short 是历史回测中表现更好的方向，v0.1 保留优先级，但仍必须遵守 Regime 和 Trigger。

## 5. Setup B：Failure Reverse

Failure 是很有价值的方向，但必须严格，避免 Range Day 两边挨打。

## 5.0 Failure 来源

每个 Failure 必须记录来源：

- `FailureSourceSignalID`。
- `FailureSource`：BullFVG / BearFVG / Demand / Supply / VWAP / VPOC / OpeningRangeHigh / OpeningRangeLow / OB。
- `FailureSourceZoneType`。
- `FailureSourceZoneFreshness`。

原因：不同来源的 Failure 质量可能完全不同，后续必须能统计。

### 5.1 Long Failure -> Short

必须满足全部条件：

1. 曾出现有效 Long Pullback 候选。
2. Bullish zone 失守：收盘跌破 zone 下沿。
3. 出现 Lower High：
   - 跌破后反抽；
   - 反抽高点低于前一有效 swing high；
   - 至少 1 根 K 收盘确认该 lower high。
4. Retest 失败：
   - 反抽到 zone 下沿/中线/VWAP 附近；
   - 收盘无法重新站回 zone 内。
5. OF 不明显逆向：
   - OFScore 和方向作为评分项；
   - 若 OF 明显 Bull，不允许 Short。

入场：

- 不在第一次跌破时追空。
- 只在 retest failed 后入场。

止损：

- 放在 lower high 上方。

### 5.2 Short Failure -> Long

反向同理，但 Long 历史表现弱，v0.1 只记录或极严格交易：

- 必须 Regime 从 BearTrend 退化为 Unknown 后再形成 BullTrend。
- 必须重新站回 VWAP 或开盘区间关键位。
- 必须出现 Higher Low 并确认。
- Risk 必须更小。

## 6. OrderFlow：评分项，不是一票否决

旧策略的问题是 OF softening 会把逆向 OF 放行。新策略取消 softening 放行。

但 OF 也不作为一票否决，因为结构极好时 OF 可能慢一拍。

v0.1 使用 SetupQualityScore，满分 100：

- Regime：40 分。
- Structure/Trigger：30 分。
- Zone：20 分。
- OrderFlow：10 分。

允许交易条件：

- 总分 `>= 80`。
- 如果 OF 明显逆向，最多只能给 0 分，并且总分必须仍 `>= 85` 才允许。
- Failure setup 中，如果 OF 明显反向于 failure 方向，则不交易。

OF 记录字段：

- OFScore。
- BullWeight。
- BearWeight。
- OFDirection：Bull / Bear / Mixed。
- OFAligned。
- OFReclaimed。
- OFScoreContribution。

## 6.1 ZoneQualityScore

Zone 不能只写成宽泛的 `Zone`。必须拆成可统计字段。

字段：

- `ZoneType`：
  - BullFVG。
  - BearFVG。
  - Demand。
  - Supply。
  - VWAP。
  - VPOC。
  - OpeningRangeHigh。
  - OpeningRangeLow。
  - OB。
- `ZoneFreshness`：
  - Fresh。
  - SecondTouch。
  - ThirdTouch。
  - Mitigated。
- `ZoneWidthPoints`。
- `DistanceToVWAP`。
- `DistanceToVPOC`。
- `DistanceToOpeningRange`。
- `ZoneScoreContribution`。

实现要求：

- `ZoneType` 和 `ZoneFreshness` 必须记录原始枚举值，不能只折算成分数。
- 多个 zone 重叠时，必须记录主交易 zone，并可选记录 `ConfluenceZoneTypes`。
- Zone 分数只影响 `SetupQualityScore`，不能覆盖 Trigger 失败。

后续分析目标：

- 哪类 Zone 胜率最高。
- 哪类 Failure 最强。
- 第几次触碰后质量明显下降。

## 7. Risk：使用波动率适配

固定 20 点风险太僵硬。v0.1 使用双限制：

- 绝对上限：`MaxRiskPointsHard = 25`。
- 波动率上限：`Risk <= 0.6 * ATR(M5,14)`。
- 低波动保护：如果 ATR 很小，允许最小上限为 `12 points`。

最终最大风险：

```text
MaxAllowedRisk = min(MaxRiskPointsHard, max(12, 0.6 * ATR14))
```

如果实际 stop 风险超过 MaxAllowedRisk：

- 不交易。
- 记录 NoTradeReason = `RiskTooWideVolAdjusted`。

## 7.1 Estimated Reward / RR

只检查风险不够，还要检查前方空间。

每个信号计算：

- `NearestResistanceDistance`：Long 前方最近阻力距离。
- `NearestSupportDistance`：Short 前方最近支撑距离。
- `EstimatedRewardPoints`。
- `EstimatedRR = EstimatedRewardPoints / InitialRiskPts`。

参考阻力/支撑：

- 前 swing high / low。
- VWAP。
- VPOC。
- OpeningRangeHigh / OpeningRangeLow。
- 未失效反向 FVG/OB。

规则：

- `EstimatedRR < 1.5`：跳过。
- 记录 `NoTradeReason=EstimatedRRTooLow`。

实现要求：

- Long 使用最近上方阻力计算 `EstimatedRewardPoints`。
- Short 使用最近下方支撑计算 `EstimatedRewardPoints`。
- 如果找不到可靠阻力/支撑，记录 `EstimatedRewardUnknown`，v0.1 默认跳过。
- `EstimatedRR` 只使用初始止损风险计算，不使用后续移动止损。

目的：避免快撞墙的位置才追入。

## 8. Exit：v0.1 重点统计，不急着优化

先不要急着设计复杂出场。v0.1 主要记录完整路径。

v0.1 第一轮回测不启用 trailing。原因：先回答 Entry 有没有 Edge，避免出场变量污染结果。

第一轮固定退出实验：

- Run A：1.5R fixed target。
- Run B：2.0R fixed target。
- Run C：2.5R fixed target。
- Run D：SessionClose / time stop。

所有 run 都记录同一批 signal 的 MFE/MAE 和 HitR。

暂不使用：

- tight trailing。
- BE 后立即收紧 TP。
- 动态 TP。

这些等 Entry Edge 证明后再研究。

研究时不要因为日目标停止而丢掉后续信号。

v0.1 日志约束：

- `ExitPolicy` 只能是 Fixed1_5R / Fixed2R / Fixed2_5R / SessionClose。
- `ExitReason` 只能是 Target / Stop / SessionClose / TimeStop。
- 不记录 Trail / BE 作为真实退出原因。
- 即使未来加入 trailing，也必须作为独立版本实验，不能混入 v0.1 第一轮结果。

## 8.1 Outcome 分类

只看 PnL 不够，必须记录交易质量和出场效率。

字段：

- `OutcomeClass`：Excellent / Good / Scratch / Poor / Catastrophic。
- `ExitEfficiency`：ActualPnL_R / MFE_R。
- `RunupCapturePct`：实际捕获的 MFE 比例。
- `AdverseBeforeProfit_R`：达到正向 MFE 前经历的最大 MAE。

默认分类建议：

- Excellent：MFE >= 2R，且实际结果 >= 1.5R。
- Good：实际结果 >= 1R。
- Scratch：实际结果在 -0.25R 到 +0.25R 之间。
- Poor：实际结果 < -0.25R 且 > -1R。
- Catastrophic：Full Stop 或滑点后亏损超过 -1R。

ExitEfficiency：

```text
ExitEfficiency = ActualPnL_R / MFE_R
```

示例：

- MFE = 4R。
- ActualPnL = 1R。
- ExitEfficiency = 25%。

分析意义：

- MFE 高但 ExitEfficiency 低：出场需要研究。
- MFE 低且 MAE 高：入场质量差。
- MFE 高但先经历大 MAE：入场可能太早，或止损结构需要调整。

## 9. 日目标：实盘规则和研究规则分离

实盘纪律：

- 达到 `DailyTargetDollars` 可停止。
- 达到 `DailyLossLimitDollars` 停止。
- 连续 2 笔 FullLoss 停止。

v0.1 默认 ExecutionProfile 示例：

```text
ExecutionProfileName = MNQ_1Contract_Target150_200
DailyTargetDollars = 150-200
DailyLossLimitDollars = 100
FixedContracts = 1
```

研究回测：

- 继续记录所有符合条件的信号。
- 增加字段：
  - `WouldTradeLive`：按日目标/日亏损规则是否真实会交易。
  - `SkippedByDailyGuard`。
  - `ResearchOnlySignal`。

这样可以同时看到：

- 遵守日目标后的实际曲线。
- 如果继续交易，策略真实期望值如何。

## 9.1 ExecutionProfile：目标、手数、实盘纪律

日内盈利目标、日亏损、最大手数属于 ExecutionEngine，不属于 StrategyEngine。

原因：

- 日目标从 150 美金改到 300 美金，不应该改变一个 setup 是否有 edge。
- 单手改多手，不应该改变 Regime / Trigger / ZoneQuality 的判断。
- 研究时需要用同一批 signal 回放不同执行规则。

ExecutionProfile 字段：

- `ExecutionProfileName`。
- `DailyTargetDollars`。
- `DailyLossLimitDollars`。
- `MaxContracts`。
- `ContractSizingMode`：Fixed / RiskBased。
- `FixedContracts`。
- `MaxRiskPerTradeDollars`。
- `StopAfterDailyTarget`。
- `ContinueResearchAfterDailyTarget`。
- `MaxFullLossTradesPerDay`。
- `MaxConsecutiveLossesPerDay`。

v0.1 默认：

- `ContractSizingMode = Fixed`。
- `FixedContracts = 1`。
- `StopAfterDailyTarget = true` 只影响 live/replay 实际执行。
- `ContinueResearchAfterDailyTarget = true`，研究层继续记录后续 signal。

## 9.2 PositionSizing：先固定手数，预留风险手数

v0.1 不启用动态手数，先固定 1 手，减少变量。

但数据结构必须预留多手字段：

- `PlannedContracts`。
- `ActualContracts`。
- `RiskPerContractDollars`。
- `TotalInitialRiskDollars`。
- `PnLPerContractDollars`。
- `TotalPnLDollars`。

未来可加入风险手数：

```text
Contracts = floor(MaxRiskPerTradeDollars / RiskPerContractDollars)
```

约束：

- RiskBasedSizing 必须作为独立版本实验启用。
- 手数变化不能修改 StrategyEngine 的 signal 判断。
- 多手出场如果要分批，也必须作为独立 ExitPolicy 实验，不混入 v0.1 固定退出结果。

## 10. 交易统计字段

每笔交易必须记录：

- SignalID。
- SnapshotID。
- StrategyVersion。
- InstrumentProfileVersion。
- ExecutionProfileVersion。
- ScoringConfigVersion。
- ZoneDetectorVersion。
- OrderFlowVersion。
- ResearchSchemaVersion。
- ParentSignalID。
- SignalStage。
- Instrument。
- InstrumentProfileName。
- ExecutionProfileName。
- PointValue。
- TickSize。
- TickValue。
- PlannedContracts。
- ActualContracts。
- SetupType：TrendPullback / FailureReverse。
- MarketRegime：BullTrend / BearTrend / Unknown。
- RegimeScoreBull。
- RegimeScoreBear。
- RegimeScoreBreakdown。
- SetupQualityScore。
- SetupQualityScoreBreakdown。
- DirectionBias。
- EntryTrigger：SecondConfirm / FailureRetest。
- EntryRefPrice。
- ZoneID。
- ZoneType。
- ZoneFreshness。
- FailureSource。
- FailureSourceSignalID。
- StopBasis：SweepLow / SweepHigh / LowerHigh / HigherLow / Structure。
- InitialRiskPts。
- RiskPerContractDollars。
- InitialRiskDollars。
- TotalInitialRiskDollars。
- EstimatedRewardPoints。
- EstimatedRR。
- ScoreComponentsSummary。
- MFE_R。
- MAE_R。
- MFE_Points。
- MAE_Points。
- MFE_Dollars。
- MAE_Dollars。
- PnLPoints。
- PnLPerContractDollars。
- TotalPnLDollars。
- ActualPnL_R。
- OutcomeClass。
- ExitEfficiency。
- RunupCapturePct。
- AdverseBeforeProfit_R。
- MaxHeat：达到 MFE 前经历的最大 MAE。
- Hit_1R。
- Hit_1_5R。
- Hit_2R。
- Hit_2_5R。
- Hit_3R。
- Hit_50Dollars。
- Hit_75Dollars。
- Hit_100Dollars。
- TimeInTradeMinutes。
- BarsHeld。
- TimeTo1RMinutes。
- TimeToMFEMinutes。
- ExitReason：Target / Stop / SessionClose / TimeStop。
- ExitPolicy。
- WouldTradeLive。
- SkippedByDailyGuard。
- SkipReasons。
- ExecutionSkipReasons。

分析意义：

- MFE 小：进场逻辑差。
- MFE 到过 1R/1.5R 但最终亏：出场逻辑差。
- 先 MAE 接近 -1R 再 MFE 大：进场太早或 stop 太紧。
- TimeToMFE 很短：目标可能可降低。
- TimeToMFE 很长：持仓成本高，可能不适合日目标。

## 11. No Trade Reason 日志

每个潜在 setup 即使不交易，也要记录原因。

格式：

```text
NO_TRADE id=20260704-0935-BULL-001 time=... side=LONG setup=TrendPullback stage=Candidate regime=BullTrend skipReasons=RiskTooWideVolAdjusted|OFMixed score=76 risk=24 maxRisk=18 of=Mixed
```

常见原因：

- RegimeUnknown。
- TrendScoreTooLow。
- ThirdPullback。
- NoSecondConfirm。
- ReclaimFailed。
- MicroBosFailed。
- OFMisaligned。
- ScoreTooLow。
- RiskTooWideVolAdjusted。
- NoRetraceAfterConfirm。
- DailyGuardLiveOnly。
- EstimatedRRTooLow。

后续可以统计：

- 哪些过滤原因最常出现。
- 被跳过的 setup 后来是否成功。
- 某个过滤是否真的有价值。

SkipReasons 允许多个原因，用 `|` 拼接。

## 11.1 Almost Trigger

必须记录“差一点触发”的 setup。

例如：

- Regime 满足。
- Zone 满足。
- 但没有 Reclaim。

记录：

```text
ALMOST_TRIGGER id=... setup=TrendPullback stage=Candidate missing=Reclaim|MicroBos regime=BullTrend zone=BullFVG score=72
```

目的：

- 统计多少机会死在 Reclaim。
- 多少死在 Risk。
- 多少死在 Execution。
- 找到真正限制交易数量和盈利单数量的节点。

## 11.2 回测输出文件

每次回测建议输出一组文件，而不是只输出交易 CSV。

建议文件：

- `ConfigSnapshot.json`：本次回测完整配置快照。
- `signals.csv`：所有 Candidate / Confirmed / Triggered / Skipped / ResearchOnly 信号。
- `trades.csv`：实际成交记录。
- `no_trade.csv`：未交易原因。
- `score_breakdown.csv`：重要评分组件明细。
- `summary.json`：按天、方向、setup、zone、outcome 的汇总。

关联键：

- `SnapshotID`。
- `SignalID`。
- `ParentSignalID`。
- `ZoneID`。

约束：

- 所有文件必须带 `SnapshotID`。
- 任何 CSV 行都必须能通过 `SignalID` 找回对应 signal。
- `trades.csv` 不应成为唯一研究数据源，因为很多有价值的信息来自未成交信号。

## 12. 新项目建议

当前 NQOrderFlowV1 保留为资料库，不建议继续在旧状态机上硬塞新策略。

可复用：

- OrderFlowEngine。
- M5/HTF 结构识别。
- FVG/zone 识别。
- 日志与 CSV 框架。
- ATAS 接入。

建议新项目名：

```text
MNQOpeningPullbackFailureV1
```

说明：

- 项目名可以先体现当前研究目标 MNQ。
- 内部模块不要写死 MNQ，必须通过 InstrumentProfile 支持其他品种。
- 后续如果 NQ / ES / MES / GC / MGC 表现不同，优先新增 profile 和参数组，不复制整套策略代码。

第一版只做：

1. TrendScore。
2. Trend Pullback 二次确认。
3. Long Failure -> Short。
4. SetupQualityScore。
5. MFE/MAE。
6. NoTradeReason。

## 13. 文档拆分规划

当前 v0.1 先集中在一份文档内，方便快速讨论和确认。进入编码前，建议拆成四份规范，避免单一文档继续膨胀。

### Strategy Specification

内容：

- Regime。
- Setup。
- Trigger。
- Risk。
- Exit 实验。
- Failure Reverse。

原则：

- 只描述业务规则。
- 不写 CSV 字段细节。
- 不写具体工程模块实现。

### Architecture Specification

内容：

- StrategyEngine。
- ExecutionEngine。
- ResearchEngine。
- ZoneDetector。
- Scoring Framework。
- Signal 生命周期。
- Replay Capability。

原则：

- 描述模块边界和数据流。
- 保证策略、执行、研究互不污染。

### Data Dictionary

内容：

- SignalID。
- SnapshotID。
- CSV 字段。
- 日志格式。
- 枚举值。
- OutcomeClass。
- ScoreComponent / ScoreBreakdown。

原则：

- 保持字段稳定。
- 字段变更必须提升 `ResearchSchemaVersion`。

### Configuration Guide

内容：

- InstrumentProfile。
- ExecutionProfile。
- Scoring weights。
- Trend thresholds。
- Risk parameters。
- ZoneDetector settings。
- 默认 MNQ v0.1 参数。

原则：

- 新增品种优先改配置。
- 新增目标或手数优先改 ExecutionProfile。
- 不因为配置变化修改 StrategyEngine。

拆分时机：

- 你确认 v0.1 规则可以进入编码前。
- 或文档继续增长到不便维护时。
