# v2.77 80日全路径唯一候选审计：数据状态与口径冻结

日期：2026-08-06  
范围：活动目录中 89 个 `*_research_outcomes.csv` Snapshot；包含 80 日普查与同日补跑/半自动功能校准快照。

## 已核实的事实

1. 80 日普查并非只记录 5 条路径。去除同一 `SignalID + ResearchPath + EntryTime + Side` 的重复 Replay 后，共有 **22,756** 条路径级提案、**32** 个 `ResearchPath` 标签、**54** 个路径×方向组合。
2. 这些行全部是研究观察：`WouldTradeLive=false`、`ResearchOnlySignal=true`、`ActualVerified=false`、`OutcomeSource=ResearchOHLC`。因此它们可用于候选质量筛查，**不能**直接作为 Actual PnL、组合收益、实盘或半自动准入证据。
3. 以 `EntryTime + Side + Entry` 合并同一物理入场机会后，22,756 条路径提案仅对应 **10,645** 个机会，平均每机会 **2.14** 条路径声明；5,085 个机会仅属于一条路径，5,560 个机会被至少两条路径重叠声明，最多一项被 16 条路径同时声明。
4. 典型重叠不是独立 Alpha：
   - `ObservationConfirm` 与 `ObservationConfirm_WideStop1_5R`：Long 2,077、Short 1,682 个同一机会；
   - `FailureReverse_ObservationInvalidated` 与其 WideStop 版本：Short 505、Long 473；
   - `ObservationStrict_BullFresh` 与 OC 两个版本：各 666；
   - `ObservationStrict_Other` 与 OC 两个版本：各 649。
   这些主要是同一入场机会的不同止损/退出或派生标签，不能按“多条独立路径”累计胜率或期望。

## 结论

此前“只有 5 条路径、且没有任何路径可晋级”的表述口径不完整，不能作为 32 路径审计的最终裁决。

但这不等于 32 条路径已经证明有效：当前原始样本只提供 `ResearchOHLC` 的 MFE/MAE/命中标签，没有实际委托、日损/日单门、单 ActiveTrade/双槽占用和替代链后的组合结果。它只能回答“候选在未受组合约束时是否值得继续核查”，不能回答“将其加入半自动候选池后是否赚钱”。

## 已通过的独立功能校准

2026-08-06 的三日 `ManualAlert` 定点回放已经验证修复后的旁路行为：

- `2026-04-10`：目标 `FailureReverse_ObservationInvalidated Short` 发出 2 条 `MANUAL_ALERT`；均带 Entry/SL/TP/数量/风险/显著区上下文；零 `ENTRY_SEND`、零订单创建、零成交。
- `2025-04-21` 与 `2026-07-14`：目标路径没有穿过完整质量链，故零提示；这不是功能失败。
- 该验证只证明“提示分支不会下单，且可绕过该路径的 Actual 白名单禁用以输出提示”；不构成路径收益结论。

## 下一步：不补跑 Replay 的唯一候选审计

1. 将 32 个 `ResearchPath` 映射为“入场家族 + 方向 + 退出/止损变体”：例如 OC 与 OC WideStop 是同一入场家族，结构确认的不同 Stop 也是同一入场家族。
2. 固定物理候选键为 `EntryTime + Side + Entry`；对每个家族报告总机会、独占机会、与其他家族共享机会及重叠比例。
3. 对每个家族只在其独占机会和与其他家族比较时，报告五个预注册时间块（2025-04~07、08~09、10~12、2026-01~04、05~07）的 1R/1.5R 触达与 1R 前止损率；这些是筛选指标，不伪装成 PnL。
4. 只有同时满足“足量独占机会、跨时间块方向一致、且相对重叠竞争家族存在增量质量”的入场家族，才可进入下一层：使用现有日志重建组合约束的边际贡献审计。没有进入 Smoke，也不改任何策略门禁。

## 禁止事项

- 禁止将同一物理机会的多个路径/止损变体当成独立样本。
- 禁止将 `ResearchOHLC` 命中率转换成 Actual 或实盘收益预测。
- 在完成唯一候选与组合边际贡献审计前，禁止新增路径、调整入场门禁或以本轮数据放开实盘。

## 入场家族归并与唯一机会审计结果

32 个路径标签应归并为 17 个入场家族。归并只消除同一入场机会的派生 Stop/退出标签，不删除任何研究路径：

| 入场家族 | 覆盖的路径标签数 | 审计结论 |
|---|---:|---|
| ObservationConfirm | 2 | 有大量独占机会；研究代理在五段均正，但尚无 Actual 组合证据。 |
| ZoneBirthResearch | 1 | 有大量独占机会；尤其不能因为其研究代理正向而推翻已知 Short Actual 负向证据。 |
| UnknownRegimeZoneTouch | 1 | 有大量独占机会；仅列为待 Actual 校准的候选来源。 |
| FailureReverse Invalidated | 2 | 两方向均有独占机会与跨段研究代理正向；可作为下一层的优先审计对象。 |
| FailureReverse RetestFailed | 2 | 两方向有独占机会；Short 在 2025-08~09 弱，不能直接放行。 |
| HTF Sweep-Reclaim | 2 | Long/Short 均有大量独占机会，但 1.5R 前止损代理为负，停止作为半自动候选池来源。 |
| HTF Trend Continuation | 2 | 两方向独占机会充足但研究代理为负，停止作为半自动候选池来源。 |
| ObservationStrict BullFresh | 2 | Long 全部嵌套于 OC，不是独立信号。 |
| ObservationStrict Other | 2 | 两方向全部嵌套于 OC，不是独立信号。 |
| BreakawayFvg | 2 | 两方向均嵌套于 ZoneBirthResearch，不是独立信号。 |
| ShadowCandidate | 1 | 仅少量独占机会，跨段不稳定。 |
| StructureConfirmShadow | 5 | 两方向研究代理为负或不稳定，停止作为半自动候选池来源。 |
| FailureReverse LongQualified | 1 | 完全是 FR Invalidated Long 的条件子集，不是独立信号。 |
| AlmostConfirmed | 1 | 样本不足，不能裁决。 |
| SignificantZoneFirstTouch | 2 | 独占样本不足，不能裁决。 |
| TrendPullbackConfirmed | 1 | 样本不足，不能裁决。 |
| BreakawayRetest | 3 | 样本极少且完全重叠，不能裁决。 |

唯一机会规模（家族、方向）说明了为什么不能把路径标签直接看作策略数量：OC Long/Short 的共享占比分别为 65.4%/64.0%；ObservationStrict BullFresh、ObservationStrict Other、BreakawayFvg、FR LongQualified 和 BreakawayRetest 的至少一个方向共享比例为 100%。

### 条件标签的增量检查

以父家族的同一物理机会、同一基础入场结果比较，以下条件标签均未表现为正向增量（数值为保守 `1.5R命中×1.5 − 1R前止损` 代理，非 PnL）：

| 条件标签 | 父家族 | 方向 | 子集机会 | 子集代理 | 父家族其余机会 | 差值 |
|---|---|---|---:|---:|---:|---:|
| ObservationStrict BullFresh | OC | Long | 666 | +0.215R | +0.343R | -0.128R |
| ObservationStrict Other | OC | Long | 212 | +0.271R | +0.305R | -0.034R |
| ObservationStrict Other | OC | Short | 649 | +0.228R | +0.374R | -0.146R |
| BreakawayFvg | ZoneBirthResearch | Long | 192 | +0.302R | +0.383R | -0.081R |
| BreakawayFvg | ZoneBirthResearch | Short | 94 | +0.266R | +0.312R | -0.046R |
| FailureReverse LongQualified | FR Invalidated | Long | 98 | +0.582R | +0.424R | +0.158R |

因此，前五项不能作为“质量加分”或额外 Alert 原因；FR LongQualified 是唯一呈正向条件增量的标签，但只有 98 个机会，仍必须经过 Actual 化的组合边际审计。

## 自动裁决

本轮不产生任何可实盘、可 Smoke 或可自动下单的路径晋级。

- 不再投入 Replay 的负向来源：`HtfSweepReclaimLong/Short`、`HtfTrendContinuationLong/Short`、`StructureConfirmShadow` 系列。它们保留代码和研究记录，但不进入半自动候选池。
- 仅作为下一层 Actual 化组合审计来源：`ObservationConfirm`、`ZoneBirthResearch`、`UnknownRegimeZoneTouch`、`FailureReverse Invalidated`、`FailureReverse RetestFailed`，以及 FR LongQualified 这个条件子集。
- 这些来源不是获准交易的名单；它们只是经过“唯一机会、研究代理、跨段可见性”第一层筛查后，仍值得用正确 Actual 口径检验的来源。

下一层必须重建的是同一机会在以下约束下的边际贡献：路径优先级、单/双槽占用、日单上限、日损、周门及真实成交/退出。现有 80 日 `ResearchOHLC` 行无法重建这些状态，因此不允许用其模拟收益来替代该审计。

## 已归档 Actual 基线的交叉复核

为避免只依赖 ResearchOHLC，本审计同时复核了冻结 `OPF_RESEARCH_2.31 / ACTUAL_EXEC_2.48` 跨年 Actual 带的 2,047 笔 Normal 交易。该带只有 13 个实际执行过的原始路径标签，故不能替代 32 路径普查；它只用于检验“研究代理正向”是否与真实组合执行同向。

| 入场家族 | 方向 | Actual笔数 | Actual Net | 2025春夏 / Q4 / 2026H1 / 7月压力月 |
|---|---|---:|---:|---|
| ObservationConfirm | Long | 825 | +$3,442.50 | +93 / -3,293 / +6,506 / +136 |
| ObservationConfirm | Short | 661 | +$2,927.90 | -2,469 / +1,476 / +4,085 / -164 |
| ZoneBirthResearch | Short | 119 | -$1,597.40 | -1,177 / -242 / -32 / -147 |
| FailureReverse Invalidated | Short | 108 | +$4,190.20 | -293 / -198 / +4,344 / +337 |
| BreakawayFvg | Short | 70 | +$6,344.50 | -1,109 / +279 / +6,673 / +501 |
| UnknownRegimeZoneTouch | Short | 66 | +$1,453.90 | +932 / +714 / -212 / +20 |
| ObservationStrict Other | Long | 60 | +$2,029.50 | -1,132 / +517 / +2,715 / -71 |
| AlmostConfirmed | Long | 48 | +$1,000.20 | +151 / +198 / +651 / 0 |
| FailureReverse Invalidated | Long | 41 | +$130.90 | -482 / -451 / +1,307 / -243 |
| FailureReverse RetestFailed | Short | 31 | -$168.60 | -445 / +20 / +256 / 0 |

这张表给出三个约束结论：

1. `ZoneBirthResearch Short` 的原始研究代理虽为正，但 Actual 在四段均负；故研究代理不能用于放行。
2. 所有有足够 Actual 样本的正向家族均有至少一个显著负的 Regime 段；没有一条达到“跨期稳定、可自动放行”的要求。
3. `FailureReverse Invalidated Short`、`BreakawayFvg Short` 与 `UnknownRegimeZoneTouch Short` 是实际净值较强但 Regime 依赖明确的候选来源。它们适合保留为人工可见的观察提示，不适合被解释为全自动 Alpha。

因此，前述“第一层筛查保留来源”进一步收窄为：用于半自动观察的候选来源可以优先展示上述三个 Short 家族及其完整风险/Regime 上下文；任何自动下单或扩大路径范围仍不成立。现有 `ManualAlert` 的 FR Invalidated Short 小范围技术验证与该证据一致，但尚未升级为前向交易资格。
