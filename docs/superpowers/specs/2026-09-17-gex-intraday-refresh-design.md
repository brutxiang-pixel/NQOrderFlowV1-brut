# GEX 盘中刷新设计

## 背景

阶段 A/B 已建立本地 GEX 快照读取与图表/HUD 展示，但当前 `Start-OPFGexSnapshotScheduler.ps1` 只在每天北京时间 09:15 之后成功同步一次，当天不再重复；策略侧 `LoadGexSnapshot()` 也只在 `OnStarted()` 调用一次，运行期间不会重新读盘。这与主线 MD 记录的"美盘前刷新 + 盘中每 30 分钟刷新 + 跨关键位/波动率突变刷新 + 超过 90 分钟显示 STALE"确认目标不一致，属于已知差距，本设计补齐该差距。

## 不变的安全边界（继承自 `gex_stage_b_shadow_audit_contract.md`）

- 策略本身**不联网**；所有对 `deltapex.cn` 的网络请求仍只发生在独立 PowerShell 进程（`Sync-OPFGexSnapshot.ps1`）中。
- GEX 数据仍是纯展示/审计信息，不参与路径准入、排序、目标/止损、日损、时段封控或任何订单提交判断。
- 快照缺失、过期、下载失败或校验失败时保留最后一份有效文件，HUD 显示 `Unavailable`/`Stale`，策略按原有规则继续运行。

## 架构：策略与调度器之间的"刷新请求"文件握手

策略拥有实时价格和 ATR，但不能联网；独立调度器能联网，但没有实时价格。因此新增一个单向信号文件，由策略写入、调度器消费：

- 文件：`%APPDATA%\ATAS\StrategyConfigs\OPFStrategyV1_gex_refresh_request.json`
- 内容：`{"requestedAtUtc": "...", "reason": "KeyLevelCross:GEX-ZG" | "VolatilitySpike:atrMultiple=3.1"}`
- 调度器每次轮询（沿用现有 60 秒周期）都会检查该文件的 `requestedAtUtc` 是否比"上一次已处理的请求时间"更新；如果是且满足冷却时间，则立即触发一次同步，而不必等待下一个固定时段。

策略端不直接调用 PowerShell、不等待同步完成；请求是"尽力而为"的异步提示。

## 时段刷新（调度器内实现，纯时钟驱动，不需要实时价格）

在现有"基线"时段之外新增两个纯时间驱动的时段，均以美东时间为基准（复用 `LiveGlobexCloseoutGate` 已建立的 Eastern 时区换算方式），周一至周五：

| 时段 | 触发时间（美东） | 频率 | 目的 |
|---|---|---|---|
| Overnight Baseline（既有，不变） | 北京时间 09:15 之后首次 | 每个自然日一次 | 沿用现有基线刷新，覆盖亚欧盘 |
| PreMarket | 09:00 | 每个交易日一次 | 美股现金开盘前捕捉最新 dealer 定位 |
| Intraday | 09:30 起每 30 分钟一个整点/半点槽位，至 16:00（含） | 每个交易日多次（14 个槽位） | 覆盖美股现金交易时段的 dealer 再平衡 |

每个槽位用"槽位键"（如 `2026-09-17|Intraday|10:30`）去重，同一槽位只成功同步一次；失败允许在同一槽位内重试，直到该槽位关闭。

## 事件触发刷新（策略内实现，需要实时价格/ATR）

新增纯函数 `GexRefreshTriggerGate`（静态、可单元测试，不接触网络或文件 I/O）：

- **关键位穿越**：仅统计 `GexReferenceLevels(price)` 返回的 CW/PW/ZG/VT 四条参考线；用"前一根已完成 K 线收盘价相对该线的方向"与"当前完成 K 线收盘价相对该线的方向"比较，方向反转即视为一次穿越。
- **波动率突变**：当前完成 K 线的真实波幅（High-Low）达到 `ATR14 * GexVolatilitySpikeAtrMultiplier`（默认 `2.5`）时触发。
- 两类触发共用同一个最小冷却时间 `GexForcedRefreshCooldown`（默认 10 分钟）：冷却期内即使再次满足触发条件，也不重复写入请求文件，避免频繁调用外部接口。

触发检测只在每根 K 线收盘时（`OnCalculate` 完成 bar 边界）评估一次，不逐 tick 评估。

## 策略内周期性重读快照（无需重启 ATAS）

新增 `MaybeReloadGexSnapshot(DateTime nowUtc)`：在每根完成 K 线上检查距离上次成功 `LoadGexSnapshot()` 是否已超过 `GexReloadIntervalMinutes`（默认 5 分钟）；超过则重新读取本地快照文件。文件未变化时重复读取只是幂等的本地文件 I/O，不产生副作用。

## 90 分钟 STALE 判定

`GexDailySnapshotLoader` 新增对本地同步状态文件 `OPFStrategyV1_gex_sync_state.json`（调度器写入，含 `lastSuccessAtUtc`）的读取：

- 若 `lastSuccessAtUtc` 缺失或无法解析，仍退回既有的 `dataDate` 超过 3 天判定。
- 若 `nowUtc - lastSuccessAtUtc > 90 分钟`，状态判为 `Stale`，`Detail` 记录 `lastSuccessOlderThan90Min`。
- 两个判定谁先命中就用谁的 `Detail`；`Ready` 要求两者都不成立。

这是纯显示层判定，不影响任何交易逻辑。非美股交易时段（亚欧盘）大部分时间会自然显示 `Stale`，这是预期行为，提示交易员当前 GEX 线可能不反映最新 dealer 头寸。

## 验收边界

- 编译 0 警告 / 0 错误。
- 新增纯函数（`GexRefreshScheduleGate`、`GexRefreshTriggerGate`）有独立回归断言，覆盖时段边界、穿越检测、波动率突变、冷却期。
- `GexDailySnapshotLoader` 的 90 分钟判定有独立回归断言。
- 调度器脚本改动仍保持"文件缺失/接口失败只记录，不抛出未捕获异常终止循环"的既有稳健性。
- 本设计不改变任何交易路径、风控门禁或订单提交逻辑；仅涉及 GEX 展示信息的新鲜度。
