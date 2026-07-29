# v2.26：1B Microstructure Audit 持久化与配置版本修复

## 失败证据归档

`opf_v225_microstructure_callback_presence_passed_csv_flush_and_actual_version_failed_3snapshots_20260727`

- 日期：`2026-03-03, 2026-01-16, 2026-01-27`
- 48 个文件，6,040,232 bytes；源/档 SHA-256 差异为 0。
- Manifest SHA-256：`578330C6E212D0AE01210614723369A0B4D3903192C9FEDCA7FD14C66C77F8D6`
- 结论：三日六类微观结构回调均到达、DepthSnapshot 错误为 0、Actual 订单为 0；但没有 `microstructure_audit_bars.csv`，故 1B 未通过。

## 根因与最小修复

1. `OnStopped()` 将聚合行交给 `ResearchLogger` 后未调用其 `FlushMicrostructureAudit()`，缓冲行在策略停止时丢失。
2. `ActualExecutionSettings.Default()` 仍声明 `ACTUAL_EXEC_2.46`，与默认 JSON 的 `ACTUAL_EXEC_2.47` 不一致；加载器据此回写活动配置。

修复仅为：停止时刷新 Microstructure CSV 缓冲；默认执行版本改为 2.47；Research Schema 升为 `OPF_RESEARCH_2.26`。不改变入场、退出、仓位、风控或订单生命周期规则。

## 构建与复测合同

- Build/ATAS deployed DLL SHA-256：`3FADB59293EEB49CC5D18E7167C46EFC01B6690547841477E4CC006C02A47F01`
- Build：0 warnings / 0 errors。
- 活动执行配置已恢复为 `ACTUAL_EXEC_2.47`，活动日志已清空。
- 复测同三日，设置：`Microstructure Audit Collection Only=true`、`Rich Bar Data Collection Only=false`、`Decision Tape Calibration Collection=false`。
- 必须同时满足：ConfigSnapshot=`2.26/2.47`；HUD `ActualExec: OFF`；无 Actual 订单；每 Snapshot 有 `microstructure_audit_bars.csv`；六 Source 覆盖、`depthSnapshotErrors=0`。随后才审计排序、Unknown、逐笔量与 M5 Volume 对账。
