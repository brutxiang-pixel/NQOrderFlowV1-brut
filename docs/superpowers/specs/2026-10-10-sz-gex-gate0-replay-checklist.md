# 显著区 Gate0：ATAS Replay 核对清单

目标：确认 SZ FirstTouch 的 GEX 影子审计已落盘，且 **关/开 GEX 时成交与执行决策 bit-identical**。

## 0. 准备

1. 编译并部署当前工作树 DLL 到 `%APPDATA%\ATAS\Strategies`（整进程重启 ATAS）。
2. HUD / ConfigSnapshot 确认：`ExecutionMode`、版本、`ActualExecutionPaths` 含两条 SignificantZoneFirstTouch。
3. 准备一份可用的 `OPFStrategyV1_gex_snapshot.json`（至少含 NQ 的 CallWall / PutWall / ZeroGamma，且 Status 能被策略判为 Ready）。
4. 记下日志目录：`%APPDATA%\ATAS\StrategyLogs\OPFStrategyV1\`。

## 1. 基线跑（无 GEX 或故意不可用）

1. 移走 / 改名 gex_snapshot，或使用 Status≠Ready 的快照。
2. 选一个历史上会出现 SZ FirstTouch 的 Replay 日（或你常用的 smoke 日）。
3. 跑完后保存：
   - `*_execution_decisions*.csv`（或现有执行决策日志）
   - trades / Account 汇总
4. 预期：若有 SZ Queued/Activated，审计 CSV 中 `ShadowTags` 应主要为 `SHADOW_PREFILTER_INACTIVE`（或不发明其它 soft 标签）。

## 2. 对照跑（有完整 GEX 快照）

1. 放回 Ready 快照（CW/PW/ZG 齐全）。
2. **同一 Replay 区间、同一配置**再跑一遍。
3. 对比：
   - 成交笔数、方向、入场/出场价时点、SL/TP
   - execution_decisions 的 Path / Reason / 是否 ENTRY
4. **必须一致**（允许仅多出 GEX 审计文件）。决策 Reason **不得**出现 `SHADOW_*` / `SKIP_GEX_*`。

## 3. 审计 CSV 抽查

打开 `{SnapshotId}_gex_candidate_audit.csv`：

| 检查项 | 期望 |
|---|---|
| 事件 | 同一 FirstTouch 至少有 `Queued` 与 `Activated`（若激活成功） |
| Path | `SignificantZoneFirstTouchLong` 或 `…Short` |
| GexRegime | 有 ZG 时可派生 Positive/Negative/Unknown |
| Dist* | CW/PW/ZG 距离有值或空（空=未知，禁止瞎填） |
| ShadowTags | 仅文档约定码；F0 失败时只有 `SHADOW_PREFILTER_INACTIVE` |
| SizeHint | 负Γ Long 可为 `0.5`；否则空/1 |
| EM 列 | 现阶段可空 |

## 4. 失败时

1. 先核对 DLL 是否为含 Gate0 的构建、ATAS 是否整进程重启。
2. 查 `OpeningPullbackFailureStrategy.Gex.cs` 是否仅在 Queued/Activated 调用审计。
3. 若成交不一致：立刻 `GexFilterMode`/`审计`回滚对比，Gate0 不得影响订单路径。

## 5. 通过标准

- [ ] 有/无 GEX：成交与 execution_decisions 一致
- [ ] 审计 CSV 出现 SZ Queued/Activated 行
- [ ] Reason 无 SHADOW/SKIP_GEX 硬拦截
- [ ] F0 失败不发明 NO_SPACE 等 soft 标签
