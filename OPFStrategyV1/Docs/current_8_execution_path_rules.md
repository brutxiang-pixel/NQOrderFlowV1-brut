# 当前 6 条实际下单路径规则与 2 条研究路径

更新时间：2026-10-26（北京时间），OPF_INTRADAY_2.9

## 2.9 两条路径转研究、NQ 入场质量与扩张阶段

`FailureReverse_ObservationInvalidated_WideStop1_5R` 与 `ObservationConfirm` 本版本停止实际下单，但不删除候选、漏斗、MFE/MAE 和研究退出记录。旧的实际执行配置在升级到 `ACTUAL_EXEC_2.53` 时会过滤这两条路径，避免旧配置自动合并回来。

`ESResponseAlignedBreakout` 和 `ESExpectedMoveExpansion` 在 NQ 上增加独立的自身 K 线确认：突破路径要求实体占本 K 线区间至少 50%、方向性收盘至少 80%；预期波动扩张路径要求实体至少 40%、方向性收盘至少 65%。MNQ 不套用这组 NQ 专属门禁。

预期波动扩张由原来的 `0.75-1.25` 倍锚定预期波动收敛为 `0.75-1.10` 倍；超过 1.10 倍记录 `OptionsGate:ExpectedMoveExpansionLate`，仍保留候选审计。

每条执行决策的 Reason 追加 `EntryQualityV29`，记录品种、路径、风险点数、K 线区间、实体比例、方向性收盘和 bar，供订单库按入场质量复盘。锁盈状态机本轮不改。

## 2.8 自动止盈与锁盈保护

本版本保留每笔订单原生 SL/TP 和 OCO 结构，不做分批平仓；在 1 手配置下，自动止盈仍由原生 TP 完成，新增阶段二只移动 SL 锁定部分浮盈。阶段二必须先收到阶段一 `PROTECT_*_APPLIED` 确认，且只允许朝降低风险方向改单；改单失败记录 `PROFIT_LOCK_MODIFY_FAILED_V210`，不把订单标记为已锁盈。

| 路径 | 阶段一保护 | 原生 TP | 阶段二锁盈触发 | 锁定位置 |
|---|---:|---:|---:|---:|
| ObservationConfirm | 0.75R | 1.5R | 1.25R | +0.25R |
| BreakawayFvg_Qualified | 0.75R | 1.5R | 1.25R | +0.5R |
| FailureReverse_ObservationInvalidated_WideStop1_5R | 1R | 4R | 2R | +0.5R |
| FailureReverse_RetestFailed | 1R | 1.5R | 1.25R | +0.5R |
| ESResponseAlignedBreakout | 0.75R | 1.5R | 1.25R | +0.5R |
| ESWallRejectionReversal | 1R | 1.5R | 1.25R | +0.5R |
| ESExpectedMoveExpansion | 1R | 1.5R | 1.25R | +0.5R |
| ESGammaConcentrationResponse | 1R | 1.5R | 1.25R | +0.5R |

保护事件按 `TRIGGERED → MODIFY_SEND → APPLIED` 记录；市场已经穿过目标保护价时记录 `PROFIT_LOCK_SKIPPED_MARKET_CROSSED_V210` 并等待后续行情，原 SL 仍权威。TP、SL、时间退出或人工平仓后仍走原有保护单清理；ES 数据失效不改变已成交订单的保护链。

## 2.7 实际执行行为更新

- `ESResponseAlignedBreakout` Long 的本地收盘位置确认要求为K线区间方向端至少0.75；Short仍为0.65。
- 正常 Breakout 止损后，同账户 NQ/MNQ 同方向候选共享事件冷却，必须等止损时间之后的新ES五分钟参考，并继续通过现有ES响应与本地确认门禁。异常隔离、异常强平不启动冷却。
- Breakout 达到0.75R后将保护止损移动到覆盖配置往返手续费并多锁一个最小跳动的位置；原1.5R目标不变。该保护价不保证覆盖成交滑点。
- 其他路径、Breakout Short确认阈值、FR Short目标及全局风险配置均不变。

本文以当前实际配置和策略代码为准，记录当前自动下单的 6 条路径。`ObservationConfirm`、`FailureReverse_ObservationInvalidated_WideStop1_5R` 和 `NativeIcebergReaction` 仅记录研究，不实际下单。

当前实际配置：

```text
ObservationConfirm
BreakawayFvg_Qualified
FailureReverse_ObservationInvalidated_WideStop1_5R
FailureReverse_RetestFailed
ESResponseAlignedBreakout
ESWallRejectionReversal
ESExpectedMoveExpansion
ESGammaConcentrationResponse
```

## 一、全局实际下单条件

所有路径最终都要经过以下公共检查：

1. 路径在实际执行配置中启用。
2. 当前方向没有被路径方向门禁禁用。
3. 账户连接、策略运行状态和订单权限正常。
4. 没有人工干预锁或未解决的账户损益状态。
5. 没有达到每日交易笔数上限。
6. 没有触发日损、周损、尾盘封控或交易时段封控。
7. 止损方向正确，Long 止损必须低于入场价，Short 止损必须高于入场价。
8. 止损宽度不能超过结构风险硬上限 50 点。
9. 预期 RR 不低于当前配置要求，当前基础最低预期 RR 为 1.0。
10. 入场 K 线不能在入场前已经触发止损，不能出现无法判定的同柱止损/止盈冲突。
11. 实盘还必须通过连接、延迟、保护单和账户状态检查。

当前配置文件中的主要全局值：

| 项目        |          当前值 |
| --------- | -----------: |
| 自动模式      |           开启 |
| 单次下单数量    |          1 手 |
| 每日最大交易数   |         20 笔 |
| MNQ 日损限制  |       200 美元 |
| NQ 日损限制   | 测试阶段关闭，配置为 0 |
| 周损限制      |       当前配置关闭 |
| 最低预期 RR   |          1.0 |
| 趋势状态要求    |           开启 |
| 宽止损       |   开启，倍率 1.5R |
| 往返手续费模型   |   每手 1.20 美元 |
| ES 期权代理门禁 |           开启 |

## 二、ObservationConfirm

### 逻辑

显著区触达后，不立即追单，而是等待显著区仍然有效并出现确认。流程为：

```text
显著区触达
→ 区域未失效
→ 确认 K 线
→ 质量与风险检查
→ 实时反应确认
→ 下单
```

### Long

- 候选通常来自 Bull 方向显著区。
- 价格进入或触达 Bull 区。
- 后续 K 线没有收盘跌破显著区下沿。
- 收盘重新站上显著区上沿，或突破前一根 K 线高点。
- 基础质量分通常至少 56 分，实际还受风险扩量分支影响。
- 当前结构风险策略以统一 50 点硬上限替代旧的 11/12 点宽度拒绝；不能将旧配置值理解为当前实际止损上限。

### Short

- 候选通常来自 Bear 方向显著区。
- 价格触达 Bear 区。
- 后续没有收盘突破显著区上沿。
- 收盘跌破显著区下沿，或跌破前一根 K 线低点。
- 基础质量要求通常为 56 分。
- 中风险低质量 Short 会被额外拦截。

### 保护与退出

- Long 初始 SL：显著区下沿下方 0.5 点。
- Short 初始 SL：显著区上沿上方 0.5 点。
- 默认目标约 1.5R。
- 达到约 0.75R 后进入保护逻辑。
- 当前路径有约 6 根 K 线的时间退出边界。
- 特定风险带可能使用不同目标或动态退出政策，最终以执行事件记录为准。

## 三、BreakawayFvg_Qualified

### 逻辑

新 FVG 形成后进行严格 Qualified 筛选，和普通显著区回踩不是同一类逻辑。

当前 Qualified 筛选主要产生 Long：

- 必须是 Bull FVG。
- FVG 必须为新创建、Fresh。
- 区域不能已被缓解或失效。
- 趋势状态必须通过。
- Setup 质量分至少 80 分。
- 生成时结构风险不超过约 8.5 点。
- 入场前不利热度不超过风险的 0.5 倍。

Qualified 候选进入实时确认，不是创建 FVG 后立即成交：

- 价格必须保持在有效触发范围内。
- 持续反应不足时记录 `WaitingHold`。
- 价格追得过远时记录 `ChaseTooFar`。
- 通过实时反应后才提交实际执行。

### 保护与退出

- Long 初始 SL：Bull FVG 下沿下方 0.5 点。
- 基础 Breakaway 的旧风险配置为 11 点；当前实际结构风险硬上限为 50 点，但 Qualified 候选生成仍有自己的风险条件。
- Qualified 生成条件通常使风险不超过 8.5 点。
- 默认目标约 1.5R；部分历史退出分支可能使用更高目标或保本保护。

## 四、FailureReverse_ObservationInvalidated_WideStop1_5R

### 逻辑

```text
原显著区建立
→ 原方向观察候选
→ 显著区被反向收盘突破
→ FailureReverse
→ 1.5 倍结构风险宽止损
→ 反向确认
→ 下单
```

当前实际执行主要是 Short：

- 原 Bull 区被价格向下收盘跌破。
- 原 Long 观察逻辑失效。
- 生成 Short FailureReverse。
- 通过宽止损、质量、风险和 RR 检查后执行。

Long FailureReverse 主要保留为研究或特殊扩量，不是当前常规对称执行方向。

### 保护与退出

- 基础 Short SL：原显著区上沿上方。
- 宽止损版本：基础结构风险乘以 1.5。
- 当前主要目标约 4R。
- 通常允许反转波段运行，不采用普通 1R 即保本的窄退出。
- 仍受 50 点结构风险硬上限及公共风控约束。

## 五、FailureReverse_RetestFailed

### 逻辑

```text
显著区失效
→ FailureReverse
→ 回测原区域
→ 回测未重新恢复原方向
→ 再次向反转方向确认
→ 下单
```

当前实际执行主要是 Short：

- 原 Bull 区失效并形成 Short FailureReverse。
- 价格必须重新触达原区域。
- 回测后再次跌破区域下沿或前一根 K 线低点。
- Long 方向当前明确禁用，记录 `LongFailureReverseRetestFailedDisabledV208`。

回测窗口约为 6 根 K 线：

- 未触达原区域：`FailureRetestExpired`。
- 触达但没有再次确认：`FailureRetestNoConfirm`。
- 确认成功：生成 `FailureReverse_RetestFailed`。

### 保护与退出

- Short SL 根据回测确认后的结构和原区域上沿确定。
- FailureRetest 的旧风险配置约为 15 点；当前实际结构风险硬上限为 50 点。
- 宽止损变体不是常规实际执行路径，只有特殊扩量分支才可能出现。
- 默认目标约 1.5R，达到保护阈值后可进入保本或保护逻辑。

## 六、ES 代理数据共同规则

以下四条路径使用 ES 实时行情和 ES 期权链代理模型指导 NQ/MNQ：

当前公共 ES 代理门禁仍检查前述四条旧路径。新版证据（EvidenceSchema=2）下，旧四条依赖有效的 ES 价格与五分钟参考，不再依赖 ATM/本地期权报价覆盖；Breakaway 仍拒绝显著反向的 ES 响应。新增 ES 四条仍要求 ModelReady 及相应期权报价。旧归档缺少独立证据时间时保留旧数据质量检查，不能当作新版证据验收。

- `ESResponseAlignedBreakout`
- `ESWallRejectionReversal`
- `ESExpectedMoveExpansion`
- `ESGammaConcentrationResponse`

ES 图表是 `SourceOnly`，不在 ES 图表上实际下单；NQ/MNQ 图表作为 Consumer 读取 ES 源发布的共享快照。

四条路径共同要求：

- ES 代理状态为 `ModelReady`。
- 快照年龄不超过 90 秒。
- 有有效 ATM 报价。
- 本地有效期权报价覆盖率至少 50%。
- 有 ES 当前价格。
- 有约 60 秒和 5 分钟价格参考。
- 60 秒参考年龄约 55 至 70 秒。
- 5 分钟参考年龄约 295 至 315 秒。
- NQ/MNQ 当前 K 线方向明确。
- K 线实体和收盘位置满足强度要求。
- ES 快速方向不能与下单方向冲突。
- 实时模式还需满足持续反应确认，不能凭单个瞬时价格触发。

2.0 补充：ES 价格与期权 Changed 回调分别记录本地观测时间；期权有效样本只使用90秒内收到更新的报价，不能用快照重新写入时间冒充报价更新。该观测时间不等于交易所原始报价时间。ES行情与期权模型分开更新，PriceReady 表示只有价格上下文可用。

墙位/集中位事件在实际进入触达带或跨越稳定水平后冻结，最多有效5分钟；模型水平移动本身不构成触达。价格在冻结水平一侧持续约2秒才形成 CrossHold（穿越保持）或 Reject（触达拒绝），随后仍须通过NQ/MNQ实时反应、质量及执行门禁。同一事件的NQ/MNQ触发价、SL和有效期固定。

Expected Move 锚点记录建立时间、来源及到期系列；同日同系列可恢复，换日/换系列重建。当前来源是 FirstValidSessionObservation，可能是中途启动基准，不能称为现金开盘锚点。候选生成和最终门禁均检查75%—125%范围。

候选因临时数据、延迟、上下文或持仓槽位阻塞时，在原有效期内最多每秒重新检查一次；仍重新验证结构及反应。人工干预、方向禁用、已提交或实际订单拒绝不会自动重发。

## 七、ESResponseAlignedBreakout

### Long

1. ES 过去约 5 分钟上涨。
2. ES 5 分钟涨幅达到响应阈值。
3. NQ/MNQ 当前 K 线为明显阳线。
4. 收盘突破近期结构高点。
5. 收盘位于当前 K 线较强的高位区域。
6. ES 60 秒变化不能明显反向。
7. ES 代理数据新鲜、报价质量合格。
8. 大风险突破先等待回踩，不直接追价。

### Short

1. ES 过去约 5 分钟下跌。
2. ES 5 分钟跌幅达到响应阈值。
3. NQ/MNQ 当前 K 线为明显阴线。
4. 收盘跌破近期结构低点。
5. 收盘位于当前 K 线较强的低位区域。
6. ES 60 秒变化不能反向上涨。
7. 通过期权数据质量和新鲜度门禁。
8. 大风险突破先等待回踩。

### 事件锚点和回踩

- 记录突破时间、突破价、触发价和止损。
- 风险较大时进入 `WaitingHold` 或 `RETEST_WAIT`。
- 回踩后风险缩小到允许范围，并重新越过触发价，才可继续。
- 追价过远记录 `ChaseTooFar`。
- 超过事件有效期记录 `RETEST_EXPIRED`。
- 同一事件只允许尝试一次。
- 实时确认要求约 2 秒持续反应，且订单流方向一致。

### 保护与退出

- SL 来自突破结构或事件锚点。
- 目标通常 1.5R。
- 达到约 1R 后进入保本保护。

## 八、ESWallRejectionReversal

### Long：Put Wall 支撑拒绝

1. ES 接近 Put Wall。
2. ES 真实触及或穿过 Put Wall 附近。
3. 触及后重新站回 Put Wall 上方。
4. ES 出现向上响应。
5. ES 5 分钟涨幅达到响应阈值。
6. NQ/MNQ 当前 K 线为有效阳线。
7. 收盘位置不能太弱。
8. Call/Put 两侧报价覆盖率都至少 50%。

### Short：Call Wall 阻力拒绝

1. ES 接近 Call Wall。
2. ES 真实触及或穿过 Call Wall 附近。
3. 触及后重新跌回 Call Wall 下方。
4. ES 出现向下响应。
5. NQ/MNQ 当前 K 线为有效阴线。
6. 收盘位置不能太弱。
7. 两侧期权报价覆盖率满足要求。

仅接近墙位但没有真实触达和收回，不构成该路径。

### 保护与退出

- SL 位于候选 K 线另一侧结构位置。
- 受结构风险 50 点上限限制。
- 目标通常 1.5R。
- 达到约 1R 后进入保护性保本。

## 九、ESExpectedMoveExpansion

系统在当日首次获得有效 Expected Move 时保存：

```text
ES ExpectedMove Anchor
ES ExpectedMove At Anchor
```

### Long

- 当前 ES 高于当日锚点。
- 偏离达到锚定 Expected Move 的至少 75%。
- 偏离不超过约 125%，避免追在扩张尾端。
- ES 5 分钟方向向上。
- NQ/MNQ 当前 K 线为有效阳线。
- ES 快速 1 分钟变化不能反向。
- 代理数据新鲜且报价质量合格。

### Short

- 当前 ES 低于当日锚点。
- 向下偏离达到 Expected Move 的至少 75%。
- 偏离不超过约 125%。
- ES 5 分钟方向向下。
- NQ/MNQ 当前 K 线为有效阴线。
- ES 快速变化不能反向。

### 保护与退出

- SL 按候选 K 线结构设置。
- 受结构风险和合约硬风险上限限制。
- 目标通常 1.5R。
- 达到约 1R 后进入保本保护。

## 十、ESGammaConcentrationResponse

该路径使用 Gamma 与 OI 的集中度，不代表完整的正负 Gamma 或做市商仓位方向。当前模型明确标记为：

```text
UnsignedGammaOi
DealerGammaUnknown
```

### Long

1. 存在 Gamma 集中价格。
2. 最大集中度至少 18%。
3. ES 当前价接近该集中价格。
4. 距离不超过 Expected Move 的约 20%。
5. ES 5 分钟响应向上。
6. NQ/MNQ 当前 K 线为有效阳线。
7. ES 快速响应不能反向。
8. 新版证据还要求冻结集中位上的 CrossHold 或 Reject 事件；不根据Call/Put类别推断做市商Gamma正负。

### Short

1. ES 当前价接近 Gamma 集中价格。
2. 集中度至少 18%。
3. ES 5 分钟响应向下。
4. NQ/MNQ 当前 K 线为有效阴线。
5. ES 快速响应不能反向。

### 保护与退出

- SL 按候选 K 线结构设置。
- 受结构风险和合约硬风险上限限制。
- 目标通常 1.5R。
- 达到约 1R 后进入保本保护。

## 十一、方向与历史路径说明

| 路径                                                 | 当前实际主要方向     | 核心触发                |
| -------------------------------------------------- | ------------ | ------------------- |
| ObservationConfirm                                 | Long / Short | 显著区触达后确认            |
| BreakawayFvg_Qualified                             | 主要 Long      | 新 Bull FVG 突破质量确认   |
| FailureReverse_ObservationInvalidated_WideStop1_5R | Short        | Bull 区失效后的反转        |
| FailureReverse_RetestFailed                        | Short        | 反转后的回测失败            |
| ESResponseAlignedBreakout                          | Long / Short | ES 与 NQ/MNQ 突破方向一致  |
| ESWallRejectionReversal                            | Long / Short | Put/Call Wall 触达后拒绝 |
| ESExpectedMoveExpansion                            | Long / Short | ES 相对锚点扩张           |
| ESGammaConcentrationResponse                       | Long / Short | Gamma 集中位附近响应       |

以下路径不在当前8条自动下单配置中：

- `SignificantZoneFirstTouchLong`
- `SignificantZoneFirstTouchShort`
- `NativeIcebergReaction`（研究记录，不实际下单）
- 普通 `BreakawayFvg`
- 其他历史研究、Shadow、ZoneBirth、UnknownRegime 和 HTF 路径。

注意：研究日志中出现候选，不等于实际下单。必须同时看到路径启用、方向允许、门禁通过和执行事件，才算实际订单路径。
