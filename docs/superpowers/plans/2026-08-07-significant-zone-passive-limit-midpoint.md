# 显著区被动限价中点与扩展止损 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将显著区被动限价双向路径改为中点限价、外沿外 1 点保护，并取消其专属 4 点风险拒绝。

**Architecture:** 仅修改 `TrySubmitSignificantZonePassiveLimit` 的价格几何。后续 `TrySubmitReplayExecution`、全局 25 点硬风险门禁和订单生命周期保持不变。

**Tech Stack:** C# / ATAS Strategy API / Python 静态回归 / .NET 10。

---

### Task 1: 添加中点与扩展缓冲回归断言

**Files:**
- Modify: `OPFStrategyV1/Scripts/tests/test_significant_zone_first_touch_wiring.py`

- [ ] **Step 1: 写入失败断言**

```python
assert "var entry = (zone.InnerBoundary + zone.OuterBoundary) / 2m;" in STRATEGY
assert "zone.OuterBoundary - 1m" in STRATEGY
assert "zone.OuterBoundary + 1m" in STRATEGY
assert "risk <= 0m" in STRATEGY
```

- [ ] **Step 2: 运行并确认失败**

Run: `python OPFStrategyV1/Scripts/tests/test_significant_zone_first_touch_wiring.py`

Expected: `AssertionError`，因为现有代码仍使用内沿和 0.5 点缓冲。

### Task 2: 修改被动限价价格几何

**Files:**
- Modify: `OPFStrategyV1/Strategy/OpeningPullbackFailureStrategy.cs:2913-2918`

- [ ] **Step 1: 最小实现**

```csharp
var entry = (zone.InnerBoundary + zone.OuterBoundary) / 2m;
var stop = side == TradeSide.Long ? zone.OuterBoundary - 1m : zone.OuterBoundary + 1m;
var risk = Math.Abs(entry - stop);
if (risk <= 0m)
    return;
```

- [ ] **Step 2: 运行静态回归**

Run: `python OPFStrategyV1/Scripts/tests/test_significant_zone_first_touch_wiring.py`

Expected: exit code 0。

### Task 3: 构建、部署和 Replay 验收

**Files:**
- Build: `OPFStrategyV1/OPFStrategyV1.csproj`
- Deploy: `%APPDATA%/ATAS/Strategies/OPFStrategyV1.dll`
- Deploy config: `%APPDATA%/ATAS/StrategyConfigs/OPFStrategyV1_actual_execution.json`

- [ ] **Step 1: 运行两项静态测试和构建**

Run: `test_eight_path_execution_policy.py`、`test_significant_zone_first_touch_wiring.py` 和 `dotnet build OPFStrategyV1/OPFStrategyV1.csproj -c Debug`。

Expected: 两项测试和构建均成功，构建为 0 错误。

- [ ] **Step 2: 部署并核验哈希**

复制 DLL 与 `OPFStrategyV1_eight_path_auto.json` 到 ATAS 目录后比较 SHA256。

Expected: DLL 与配置哈希均一致。

- [ ] **Step 3: 定点 Smoke**

Run: `2026-05-05`。

Expected: `Validate-SignificantZonePassiveLimitSmoke.ps1` 返回 `Passed : True`，并可从 execution decisions 中确认 entry 为区域中点、stop 为外沿外 1 点。
