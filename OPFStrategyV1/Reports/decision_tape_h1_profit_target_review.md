# 2026 H1 三手盈利目标离线研究复核（2026-07-25）

## 结论

Decision Tape联合Gate与盈利Gate均已通过。当前候选在不计手续费的正常Gross口径下，观察值为`+$21,961.80`；扣除未真实成交候选执行风险储备`$1,423.41`与M5退出模型校准储备`$395.58`后，保守Gross下界为`+$20,142.81`，超过冻结目标`+$20,000`。

本结论只授权停止离线搜索并提交候选合同，不自动授权编译DLL、清日志、Smoke、Replay或Tag。

## 联合校准Gate

- v2.06 H1：1075/1075 Execute身份完全一致；正常Gross`+$8,198.50`；账户总AccountNet`+$5,617.00`。
- v2.08五日BugCompatible：47/47 Normal身份完全一致；正常Gross`+$1,424.50`；账户总AccountNet`+$1,238.60`。
- `CorrectedResearch`不读取v2.08强制捕获/激活事件；旧Deferred缺陷没有进入盈利研究。

## 最终候选合同

### 组合约束

- 数量：3手；ZoneBirth保持2 Base＋1 Runner。
- 每日正常交易上限：10笔；异常/隔离订单不计入正常上限。
- 日损：`-$600`，按已实现AccountNet判断。
- Deferred Continuation：关闭。
- 入场风险禁带：`13.25 < InitialRiskPoints <= 16`继续禁入。
- 保留单ActiveTrade、开盘30分钟禁单、Globex平仓及既有异常隔离。

### 禁用路径

- Long `FailureReverse_RetestFailed`。
- Long `ObservationStrict_BullFresh_WideStop1_5R`。
- Short `FailureReverse_ObservationInvalidated`。
- Short `FailureReverse_RetestFailed_WideStop1_5R`。
- Short `ShadowCandidate`。

### 路径退出

- Short `BreakawayFvg`：目标4R；达到1.5R后下一根M5起移动到BE；最长36根M5。
- Long `ObservationConfirm`：目标4R；不提前BE；最长36根M5。
- Long `ObservationConfirm_WideStop1_5R`：目标3R；不提前BE；最长12根M5。
- Long `ObservationStrict_Other`：目标3R；不提前BE；最长12根M5。
- Long `AlmostConfirmed`：目标4R；不提前BE；最长36根M5。
- Short `FailureReverse_ObservationInvalidated_WideStop1_5R`：目标4R；不提前BE；最长36根M5。
- 其他路径保持当前安全管线退出；ZoneBirth保持2.5R Base、4R Runner、Base TP后Runner BE。
- 同柱冲突统一StopFirst；TimeStop按最后一根M5收盘退出。

## 最终指标

| 指标 | 观察 | 保守 |
|---|---:|---:|
| 正常交易 | 854 | 854 |
| 正常Gross | `$21,961.80` | `$20,142.81` |
| PF | 1.4415 | 1.3845 |
| MaxDD | `$1,112.37` | `$1,161.87` |
| 盈利周 | 88.89% | 77.78% |
| 最差周 | `-$639.27` | `-$670.22` |
| 1-2月Gross | `$4,469.13` | `$3,871.80` |
| 3-4月Gross | `$8,758.13` | `$7,953.58` |
| 5-6月Gross | `$8,734.54` | `$8,317.43` |

854笔/122日约为7.00笔/日。观察正常AccountNet为`+$18,887.40`；账户非正常生命周期合计`-$740.37`，因此观察账户总AccountNet为`+$18,147.03`。按保守正常Gross减3手手续费`854×$3.60`并计入非正常账户结果，保守账户净值参考约为`+$16,328.04`。

## 保守储备

1. 未真实成交候选共142笔。使用v2.06 H1匹配Normal交易的`ShadowGross3-ActualGross3`，方向×路径样本至少20笔时取均值加1.645倍标准误的单侧95%上界，小样本使用全局上界；合计扣减`$1,423.41`。
2. M5退出模型用1,557条`ProtectBE1R_Then3R`精确Shadow tick生命周期校准：85.16%完全一致，平均偏差`-$0.79/笔`，单侧95%上界`+$0.9696/条RichExit`；最佳组合408条RichExit合计扣减`$395.58`。
3. ZoneBirth拆分腿模型用329条tick Shadow校准，退出结构与映射Gross 100%一致，额外储备为0。
4. 当前合同沿用用户此前“暂不考虑滑点”的冻结口径；若未来加入真实Market退出滑点，必须作为独立压力测试，不得把本报告称为含滑点结果。

## 研究过程结论

- 单纯放宽日上限/日损的观察Gross上限约`$14.2k`且PF下降，约束放宽本身无法达标。
- 五条路径组合消融把质量基座提升到正常Gross`$16,599.57`、PF`1.3364`，证明收益首先来自严格入场与组合级替代链重放。
- 动态退出组合完整重放后才越过目标；直接路径增益没有被简单相加。
- 模拟器热路径改为每日事件预索引＋Deferred结果缓存＋OutcomeOverrides；固定24组约束扫描从超过3分钟降到约20秒，32/128组退出组合约20-30秒完成，已实现减少无效Replay的目的。

## 下一步

等待用户确认候选合同。确认后先Review主线，再仅实现上述必要差异；编译DLL后按规则清理日志，然后输出少量定点Smoke日期。Smoke通过后才进入固定中样本Replay，全量Replay仍保留给最终候选。

