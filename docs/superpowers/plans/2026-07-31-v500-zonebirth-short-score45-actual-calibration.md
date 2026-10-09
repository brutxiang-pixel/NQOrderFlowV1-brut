# V5.0 ZoneBirthResearch Short Score 45 Actual Calibration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 仅禁用 `ZoneBirthResearch` 的 Short、且 SetupQualityScore 精确为 45 的 Actual 订单，以 Actual Replay 校准日损/日单数替代链。

**Architecture:** 在纯静态资格判断器中封装三条件精确匹配；`ActualExecutionPathSkipReasons` 在提交订单前调用它并写入专用原因。研究候选、影子数据、其他路径、Long 和非 45 分数均保持原行为。

**Tech Stack:** C# / .NET 10 / ATAS ChartStrategy / PowerShell 回归脚本。

---

### Task 1: 写并验证失败回归测试

**Files:**
- Create: `OPFStrategyV1/Scripts/tests/Test-ZoneBirthResearchShortGate.ps1`
- Create: `OPFStrategyV1/Strategy/ZoneBirthResearchShortGate.cs`

- [ ] **Step 1: 写失败测试**

测试将加载已构建 DLL 并断言未来的 `ZoneBirthResearchShortGate.ShouldDisable`：仅 `Short/ZoneBirthResearch/45m` 返回 true；`Long`、其他路径、`44.99m` 与 `45.01m` 返回 false。

- [ ] **Step 2: 运行测试确认失败**

Run: `& .\OPFStrategyV1\Scripts\tests\Test-ZoneBirthResearchShortGate.ps1`

Expected: 失败，原因是资格判断器尚不存在。

- [ ] **Step 3: 写最小实现**

```csharp
public static bool ShouldDisable(string side, string researchPath, decimal totalScore)
{
    return string.Equals(side, "Short", StringComparison.OrdinalIgnoreCase) &&
           string.Equals(researchPath, "ZoneBirthResearch", StringComparison.OrdinalIgnoreCase) &&
           totalScore == 45m;
}
```

- [ ] **Step 4: 重新构建并运行测试**

Run: `dotnet build .\OPFStrategyV1\OPFStrategyV1.csproj -c Debug; & .\OPFStrategyV1\Scripts\tests\Test-ZoneBirthResearchShortGate.ps1`

Expected: 构建 0 错误，全部断言通过。

### Task 2: 接入 Actual 下单前门并准备配置

**Files:**
- Modify: `OPFStrategyV1/Strategy/OpeningPullbackFailureStrategy.cs:4013-4020`
- Modify: `OPFStrategyV1/Core/Configuration/ActualExecutionSettings.cs`
- Modify: `OPFStrategyV1/Configs/OPFStrategyV1_actual_execution.default.json`
- Create: `OPFStrategyV1/Configs/OPFStrategyV1_v500_zonebirth_short_score45_actual_calibration.json`

- [ ] **Step 1: 在现有路径门之前接入专用跳过原因**

```csharp
if (ZoneBirthResearchShortGate.ShouldDisable(signal.Side.ToString(), researchPath, signal.SetupQualityScore.TotalScore))
    return new[] { "ZoneBirthShortMarginalQualityDisabledV500:score=45" };
```

- [ ] **Step 2: 只更新 Actual 执行配置版本和本轮 Profile**

默认配置保持 3 手、15 笔、-$250 日损、-$500 周 Long 门；版本变为 `ACTUAL_EXEC_2.51`，Profile 为 `V500_ZONEBIRTH_SHORT_SCORE45_ACTUAL_CALIBRATION`，使 DLL 自动升级配置。

- [ ] **Step 3: 回归验证**

Run: `dotnet build .\OPFStrategyV1\OPFStrategyV1.csproj -c Debug; & .\OPFStrategyV1\Scripts\tests\Test-ZoneBirthResearchShortGate.ps1`

Expected: 构建 0 错误，所有五个边界断言通过。

### Task 3: 日志隔离与部署证明

**Files:**
- Modify: `项目恢复总览_继续开发指南.md`

- [ ] **Step 1: 编译前归档并清理当前活动日志**

按现行日志规则，只归档未污染的现有活动文件；完成后清空活动日志。

- [ ] **Step 2: 构建并核对部署 DLL 与配置**

核对项目 DLL 与 `%APPDATA%\\ATAS\\Strategies` DLL 的 SHA256 一致，并将本轮 JSON 复制到 `%APPDATA%\\ATAS\\StrategyConfigs\\OPFStrategyV1_actual_execution.json`。

- [ ] **Step 3: 写入主线 MD 和 Replay 合同**

记录直接改善 `+$1,207.61`、51 个目标订单、124 个可能日门释放候选，及“未验证替代链前不晋级”。
