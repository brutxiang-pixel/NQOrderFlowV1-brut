# v2.17 第二槽保护重评边界修复

日期：2026-07-26

## 修改

- 版本更新为`OPF_RESEARCH_2.17 / ACTUAL_EXEC_2.42`。
- `TryActivatePendingProtectedSecondaryV216`的过期判断由`lastSeenBar > entryBar`改为`lastSeenBar > entryBar + 1`，与`OpenReplayEntryAfterQuotePreflightAsync`既有的允许窗口完全一致。
- `ExpirePendingProtectedSecondaryV216`由`closedBar >= entryBar`改为`closedBar > entryBar`，避免同一entry Bar上的异步保护回调被清理竞争提前取消。
- 保留既有`...V216`事件名，便于与v2.16证据连续对账；Research/Actual版本号负责区分修复前后运行。

## 未改变

3手、18笔、日损`$450`、双槽同向、静态G2、严格风险排除带、单笔最大风险、`$300`合计初始风险、退出政策、Globex终态与风险前置逻辑均未改变。

## 验证

- 静态边界检查：`entryBar + 1`允许重评，`entryBar + 2`拒绝；关闭entry Bar不清理，关闭下一Bar仍清理真实超时。
- `dotnet build OPFStrategyV1/OPFStrategyV1.csproj -c Debug`：0警告、0错误。
- 构建与ATAS部署DLL的SHA256均为`0A47039C966607C287BB1413114732E9AD328ACFEF2B8DB100C1B802C63FCFA2`。
- v2.16活动日志92个文件与归档按文件名和长度核对无差异；编译部署后活动日志已清理为0，归档仍为92个文件。

## 下一步

先跑三日定点回归：`2026-01-02,2026-04-24,2026-05-05`。要求出现`SECONDARY_PROTECTION_READY_RETRY_V216 > 0`，不再出现`currentBar=entryBar+1`过期，同时Globex、保护和成交后风险缺陷继续为0；通过后再决定是否补`2026-02-11`。
