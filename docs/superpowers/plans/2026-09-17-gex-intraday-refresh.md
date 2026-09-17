# GEX 盘中刷新 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把 GEX 快照同步从"每天 09:15 一次"补齐为"美盘前刷新 + 盘中每 30 分钟刷新 + 跨关键位/波动率突变刷新 + 超过 90 分钟显示 STALE"，且策略运行期间无需重启 ATAS 即可看到更新。

**Architecture:** 详见 `docs/superpowers/specs/2026-09-17-gex-intraday-refresh-design.md`。策略侧新增纯函数门禁与周期性重读；调度器侧新增多时段轮询与刷新请求消费；两者通过本地文件握手，互不联网越界。

**Tech Stack:** C# / .NET 10 / ATAS ChartStrategy / PowerShell 循环调度器。

---

### Task 1: 新增 GexRefreshScheduleGate 时段门禁（纯函数）

**Files:**
- Create: `OPFStrategyV1/Strategy/GexRefreshScheduleGate.cs`
- Modify: `OPFStrategyV1/Scripts/tests/LiveProtectionFailClosedTests/Program.cs`

- [ ] **Step 1: 写回归断言**

新增用例覆盖：`PreMarket` 槽位在 `08:59:59` 不触发、`09:00:00` 触发；`Intraday` 槽位在 `09:29:59` 不触发、`09:30:00`/`10:00:00`/`16:00:00` 触发、`16:00:01` 不触发；同一槽位键重复调用只应判定为"应触发"一次（由调用方结合状态文件去重，本函数只负责判定槽位边界与生成槽位键）。

- [ ] **Step 2: 最小实现**

```csharp
namespace OPFStrategyV1.Strategy;

public static class GexRefreshScheduleGate
{
    public static readonly TimeSpan PreMarketEastern = new(9, 0, 0);
    public static readonly TimeSpan IntradayStartEastern = new(9, 30, 0);
    public static readonly TimeSpan IntradayEndEastern = new(16, 0, 0);
    public const int IntradayIntervalMinutes = 30;

    public static string? ResolveSlotKey(DateTime easternTime)
    {
        var day = easternTime.DayOfWeek;
        if (day is DayOfWeek.Saturday or DayOfWeek.Sunday)
            return null;

        var t = easternTime.TimeOfDay;
        var dateKey = easternTime.ToString("yyyy-MM-dd");
        if (t == PreMarketEastern)
            return $"{dateKey}|PreMarket";

        if (t >= IntradayStartEastern && t <= IntradayEndEastern &&
            t.Minutes % IntradayIntervalMinutes == 0 && t.Seconds == 0)
            return $"{dateKey}|Intraday|{t:hh\\:mm}";

        return null;
    }
}
```

调用方（调度器）按分钟粒度轮询，只在整分钟第一次经过某个槽位时刻调用一次即可命中；因轮询周期是 60 秒，不需要秒级精确匹配，可将比较放宽为"当前分钟等于槽位分钟"。

- [ ] **Step 3: 重新构建并运行断言**

Run: `dotnet build OPFStrategyV1/OPFStrategyV1.csproj -c Debug && dotnet run --project OPFStrategyV1/Scripts/tests/LiveProtectionFailClosedTests/LiveProtectionFailClosedTests.csproj -c Debug`

Expected: 构建 0 错误，新增断言全部通过。

### Task 2: 新增 GexRefreshTriggerGate 事件触发门禁（纯函数）

**Files:**
- Create: `OPFStrategyV1/Strategy/GexRefreshTriggerGate.cs`
- Modify: `OPFStrategyV1/Scripts/tests/LiveProtectionFailClosedTests/Program.cs`

- [ ] **Step 1: 写回归断言**

覆盖：价格从参考线上方穿越到下方（或反向）触发 `KeyLevelCross`；价格停留在同一侧不触发；真实波幅达到 `atr14 * 2.5` 触发 `VolatilitySpike`；未达到不触发；`lastForcedRefreshUtc` 在冷却窗口内（默认 10 分钟）即使满足触发条件也返回 `null`；冷却窗口外恢复触发。

- [ ] **Step 2: 最小实现**

```csharp
namespace OPFStrategyV1.Strategy;

public static class GexRefreshTriggerGate
{
    public static readonly TimeSpan ForcedRefreshCooldown = TimeSpan.FromMinutes(10);
    public const decimal VolatilitySpikeAtrMultiplier = 2.5m;

    public static string? DetectKeyLevelCross(decimal previousClose, decimal currentClose, decimal levelPrice, string levelTag)
    {
        var previousSide = Math.Sign(previousClose - levelPrice);
        var currentSide = Math.Sign(currentClose - levelPrice);
        if (previousSide != 0 && currentSide != 0 && previousSide != currentSide)
            return $"KeyLevelCross:{levelTag}";
        return null;
    }

    public static string? DetectVolatilitySpike(decimal barHigh, decimal barLow, decimal atr14)
    {
        if (atr14 <= 0m)
            return null;
        var range = barHigh - barLow;
        return range >= atr14 * VolatilitySpikeAtrMultiplier
            ? $"VolatilitySpike:atrMultiple={(range / atr14):0.00}"
            : null;
    }

    public static bool IsInCooldown(DateTime? lastForcedRefreshUtc, DateTime nowUtc) =>
        lastForcedRefreshUtc is not null && nowUtc - lastForcedRefreshUtc.Value < ForcedRefreshCooldown;
}
```

- [ ] **Step 3: 重新构建并运行断言**

Run: 同 Task 1。Expected: 构建 0 错误，全部断言通过。

### Task 3: 扩展 GexDailySnapshotLoader 支持 90 分钟 STALE

**Files:**
- Modify: `OPFStrategyV1/Core/MarketData/GexDailySnapshot.cs`
- Modify: `OPFStrategyV1/Scripts/tests/LiveProtectionFailClosedTests/Program.cs`

- [ ] **Step 1: 写回归断言**

覆盖：同步状态文件不存在时仍走既有 3 天 `dataDate` 判定；同步状态文件存在但 `lastSuccessAtUtc` 是 95 分钟前时判为 `Stale` 且 `Detail=lastSuccessOlderThan90Min`；`lastSuccessAtUtc` 是 10 分钟前且 `dataDate` 是今天时判为 `Ready`。

- [ ] **Step 2: 最小实现**

在 `GexDailySnapshotLoader.Load` 增加一个重载或新增参数 `string? syncStatePath`，读取 `{"lastSuccessDate": "...", "completedAt": "..."}`（沿用调度器现有写入格式的 `completedAt` 字段），解析为 `DateTimeOffset`；`nowUtc - completedAt > TimeSpan.FromMinutes(90)` 时覆盖 `Status="Stale"`、`Detail="lastSuccessOlderThan90Min"`，除非已经因 `dataDate` 判定为 `Stale`（两者取"先命中的"）。

- [ ] **Step 3: 接入调用点**

`OpeningPullbackFailureStrategy.Gex.cs` 的 `LoadGexSnapshot()` 传入同步状态文件路径 `%APPDATA%\ATAS\StrategyConfigs\OPFStrategyV1_gex_sync_state.json`。

- [ ] **Step 4: 重新构建并运行断言**

Run: 同 Task 1。Expected: 构建 0 错误，全部断言通过。

### Task 4: 策略内周期性重读快照 + 事件触发检测接线

**Files:**
- Modify: `OPFStrategyV1/Strategy/OpeningPullbackFailureStrategy.Gex.cs`
- Modify: `OPFStrategyV1/Strategy/OpeningPullbackFailureStrategy.cs`（在完成 K 线的既有位置调用新方法）

- [ ] **Step 1: 新增字段与周期性重读**

```csharp
private DateTime _gexLastReloadUtc = DateTime.MinValue;
private const int GexReloadIntervalMinutes = 5;
private DateTime? _gexLastForcedRefreshRequestUtc;

private void MaybeReloadGexSnapshot(DateTime nowUtc)
{
    if (nowUtc - _gexLastReloadUtc < TimeSpan.FromMinutes(GexReloadIntervalMinutes))
        return;
    LoadGexSnapshot();
    _gexLastReloadUtc = nowUtc;
}
```

- [ ] **Step 2: 事件触发检测与请求文件写入**

在完成 K 线处（沿用已有 `OnCalculate` 完成 bar 判断点，紧邻既有 `ScheduleGlobexCloseoutIfNeeded` 调用附近）新增：

```csharp
private void MaybeRequestGexForcedRefresh(OpfCandle candle, decimal atr14)
{
    if (_gexSnapshot.Levels.Count == 0 || _previousResearchCandle is null)
        return;
    if (GexRefreshTriggerGate.IsInCooldown(_gexLastForcedRefreshRequestUtc, DateTime.UtcNow))
        return;

    string? reason = null;
    foreach (var level in GexReferenceLevels(candle.Close))
    {
        reason = GexRefreshTriggerGate.DetectKeyLevelCross(_previousResearchCandle.Close, candle.Close, level.Price, GexTag(level));
        if (reason is not null) break;
    }
    reason ??= GexRefreshTriggerGate.DetectVolatilitySpike(candle.High, candle.Low, atr14);
    if (reason is null)
        return;

    WriteGexRefreshRequest(reason);
    _gexLastForcedRefreshRequestUtc = DateTime.UtcNow;
}

private void WriteGexRefreshRequest(string reason)
{
    var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ATAS", "StrategyConfigs", "OPFStrategyV1_gex_refresh_request.json");
    var payload = $"{{\"requestedAtUtc\":\"{DateTime.UtcNow:O}\",\"reason\":\"{reason}\"}}";
    File.WriteAllText(path, payload);
}
```

- [ ] **Step 3: 静态源码断言（Python，沿用既有模式）**

**Files:**
- Modify: `OPFStrategyV1/Scripts/tests/test_significant_zone_first_touch_wiring.py` 或新增 `test_gex_intraday_refresh_wiring.py`

断言 `MaybeReloadGexSnapshot`、`MaybeRequestGexForcedRefresh`、`WriteGexRefreshRequest` 在主策略源码中被正确调用（调用点在完成 bar 分支内，而非逐 tick 分支）。

- [ ] **Step 4: 构建并运行全部回归**

Run: `dotnet build NQOrderFlowV1.slnx -c Debug`，`dotnet run --project OPFStrategyV1/Scripts/tests/LiveProtectionFailClosedTests/LiveProtectionFailClosedTests.csproj`，以及 Python 测试套件。

Expected: 全部通过，0 警告 0 错误。

### Task 5: 调度器支持多时段 + 事件触发 + 冷却

**Files:**
- Modify: `OPFStrategyV1/Scripts/Start-OPFGexSnapshotScheduler.ps1`

- [ ] **Step 1: 状态文件升级**

`OPFStrategyV1_gex_sync_state.json` 增加字段：`lastSuccessDate`（沿用）、`completedAt`（沿用，供 90 分钟判定读取）、`lastFiredSlotKey`（记录最近一次成功触发的槽位键，防止同槽位重复）、`lastHandledRequestAtUtc`（记录最近一次已消费的刷新请求时间戳）。

- [ ] **Step 2: 轮询逻辑改写**

每次循环（仍保持 60 秒间隔）：
1. 换算当前美东时间，取整分钟；调用等效于 `GexRefreshScheduleGate.ResolveSlotKey` 的 PowerShell 版本判断逻辑（用 `[TimeZoneInfo]::FindSystemTimeZoneById('Eastern Standard Time')` 换算，避免与既有北京时间基线逻辑冲突）。
2. 若解析出槽位键且与 `lastFiredSlotKey` 不同，执行同步；成功则更新 `lastFiredSlotKey`、`completedAt`、`lastSuccessDate`。
3. 保留既有"北京时间 09:15 之后每日一次"基线逻辑（`lastSuccessDate` 判重），作为 Overnight Baseline 槽位，与新增槽位共用状态文件但用独立字段避免互相覆盖判重逻辑。
4. 读取 `OPFStrategyV1_gex_refresh_request.json`；若存在且 `requestedAtUtc` 比 `lastHandledRequestAtUtc` 新，且距离上次任意一次成功同步已超过 10 分钟冷却，则立即执行一次同步并更新 `lastHandledRequestAtUtc`。
5. 任何单次同步失败都只记录日志、不写入对应的去重状态，允许下一轮重试；不得抛出未捕获异常导致循环退出。

- [ ] **Step 3: 手工验证**

Run: `& .\OPFStrategyV1\Scripts\Start-OPFGexSnapshotScheduler.ps1 -RunOnce`（多次以不同系统时间的等效方式验证，或读代码走查确认槽位判定逻辑与 Task 1 的 C# 版本语义一致）。

Expected: 状态文件按预期更新，且不会在同一槽位内重复同步。

### Task 6: 主线 MD 更新

**Files:**
- Modify: `项目恢复总览_继续开发指南.md`

- [ ] **Step 1: 记录本轮实现范围与仍未验证的部分**

明确写出：策略侧刷新请求写入、周期性重读、90 分钟 STALE 判定、调度器多时段与事件消费均已实现且通过静态/断言测试；但"策略运行中不重启即可看到更新"、"调度器在真实盘中窗口下的手工值守验证"、"跨关键位穿越在真实行情下的实际触发频率与冷却是否合适"仍需要独立的 Smoke/实盘观察，不得直接宣称已完成实盘验收。
