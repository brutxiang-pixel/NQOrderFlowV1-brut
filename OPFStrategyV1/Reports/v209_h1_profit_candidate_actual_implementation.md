# v2.09 H1盈利候选Actual实现记录（2026-07-25）

## 状态

最终离线候选已按冻结合同实现为`OPF_RESEARCH_2.09 / ACTUAL_EXEC_2.39`。本版本当前仅为“已实现、已编译、待定点Smoke”，不代表策略盈利通过，不打Tag。

## Actual合同

- 3手；ZoneBirth为2 Base＋1 Runner。
- 每日正常交易上限10笔；日损`-$600`；手续费配置每手往返`$1.20`。
- Deferred Continuation硬关闭，不捕获、不激活、不进入交易链。
- 保留风险禁带`13.25 < InitialRiskPoints <= 16`、单ActiveTrade、美盘开盘30分钟禁单、Globex平仓和异常隔离。
- 禁用Long `FailureReverse_RetestFailed`、Long `ObservationStrict_BullFresh_WideStop1_5R`、Short `FailureReverse_ObservationInvalidated`、Short `FailureReverse_RetestFailed_WideStop1_5R`、Short `ShadowCandidate`。
- Short `BreakawayFvg`：4R，达到1.5R后下一根M5起BE，36 Bar TimeStop。
- Long `ObservationConfirm`：4R，无提前BE，36 Bar TimeStop。
- Long `ObservationConfirm_WideStop1_5R`：3R，无提前BE，12 Bar TimeStop。
- Long `ObservationStrict_Other`：3R，无提前BE，12 Bar TimeStop。
- Long `AlmostConfirmed`：4R，无提前BE，36 Bar TimeStop。
- Short `FailureReverse_ObservationInvalidated_WideStop1_5R`：4R，无提前BE，36 Bar TimeStop。
- 其他路径保持当前安全管线；ZoneBirth保持Base 2.5R、Runner 4R、Base TP后Runner BE。
- TimeStop在最后一根M5收盘触发；同柱先判断SL/TP，保持StopFirst。Historical Replay的TimeStop旧价成交按触发柱收盘参考价归一化，原始账户成交仍保留。

## 构建与部署

- Debug构建：0警告、0错误。
- 项目DLL与ATAS部署DLL SHA256：`5959B18E244AEEF1AF90F937FEBFA650DAD9280425DB7FF0CB6BCBE2472AC8B2`。
- 运行配置：`ACTUAL_EXEC_2.39`、3手、10笔、日损`-$600`。
- 编译部署后活动日志与旧版根日志均已清空，文件数为0。

## 定点Smoke

日期：`2026-01-06,2026-01-09,2026-01-12,2026-01-28,2026-02-05,2026-02-19`

离线观察参考只用于逐日对账，不作为Actual必须逐美元相等的通过条件：

| 日期 | 正常单 | Gross参考 | 主要覆盖 |
|---|---:|---:|---|
| 2026-01-06 | 10 | `+$730.50` | OC Long 4R、Wide Long 3R、Failure Wide Short 4R、ZoneBirth |
| 2026-01-09 | 10 | `-$0.75` | 两条12-Bar政策、日上限、非候选路径回归 |
| 2026-01-12 | 10 | `+$172.50` | Breakaway 1.5R后下一M5 BE、36-Bar TimeStop、Almost Long |
| 2026-01-28 | 10 | `+$688.50` | Failure Wide Short 36-Bar TimeStop、Wide Long TimeStop |
| 2026-02-05 | 6 | `-$694.50` | `-$600`日损门与Breakaway止损链 |
| 2026-02-19 | 9 | `+$100.00` | ZoneBirth Base TP后Runner BE回归 |

## Smoke通过标准

1. 六日全部写出`OPF_RESEARCH_2.09 / ACTUAL_EXEC_2.39`，配置为3手、10笔、日损`-$600`。
2. Execute均带`ActualExitPolicyV209`或明确沿用安全退出；六条候选的TargetR、BE与12/36 Bar参数正确。
3. Breakaway Short只能在1.5R达到后的下一根M5起移动到BE；其他五条新退出路径不得出现提前BE。
4. TimeStop的`bars=max`、触发柱收盘参考价、正常统计和AccountNet连接完整；同柱触及SL/TP时不得抢先TimeStop。
5. 五条禁用方向×路径Execute为0；Deferred捕获、激活和Deferred交易均为0。
6. 正常交易不超过10笔；异常/隔离单不占正常上限；达到AccountNet `<= -$600`后不得再开正常新单。
7. ZoneBirth保护数量必须为Base 2手＋Runner 1手，Base TP后Runner BE与OCO清理正确。
8. Execute、订单、Trade、ActualVerified、账户PnL和保护清理闭环；重复退出、孤儿仓位、订单失败、生命周期缺口均为0。
