# v2.22 BreakawayRetest 并行影子采集实施说明

## 范围

- 保留原 `BreakawayRetest` 的 6 根 M5 等待窗口、SignalID、日志和 Actual 提交行为。
- 新增两个独立 Research-only 路径：`BreakawayRetest12Research`、`BreakawayRetest18Research`。
- 12/18 根窗口使用独立 SignalID 后缀 `-RT12` / `-RT18`，避免与原 6 根路径或彼此冲突。
- 新路径在启动完整虚拟生命周期时显式 `submitReplayExecution=false`；不进入 Actual 路径名单，不消耗仓位槽位、日损、周门、日单上限或 OCO 状态。

## 漏斗证据

每个新窗口独立写入 Research 日志，保留以下节点及窗口值：

1. `WaitingRetestTouch`：首根等待触碰 Bar；
2. `BreakawayZoneInvalidated`：区块在确认前失效；
3. `BreakawayRetestConfirmTooClose` / `BreakawayRetestConfirmHeatTooHigh` 等确认质量拒绝；
4. `BreakawayRetestExpired`：窗口到期；
5. 通过确认后：完整虚拟交易生命周期与退出政策记录。

## 固定 28 日分层采集

来源：`v218_simulator_repair_h1_anchored/candidate_tape.csv` 的有效 H1 交易日。

H1 固定种子：`v222-breakaway-retest-24day-20260726`。每月按 `SHA256(seed|yyyy-MM-dd)` 升序取前四日；日期不按收益或候选数挑选。

`2026-01-06,2026-01-08,2026-01-12,2026-01-29,2026-02-03,2026-02-12,2026-02-25,2026-02-27,2026-03-05,2026-03-10,2026-03-11,2026-03-24,2026-04-01,2026-04-09,2026-04-21,2026-04-23,2026-05-06,2026-05-11,2026-05-15,2026-05-28,2026-06-08,2026-06-11,2026-06-16,2026-06-30`

7 月补充池来自已完成的 17 个有效 Replay 日；独立种子 `v222-breakaway-retest-july-4day-20260726`，同样按 SHA256 排序取四日：`2026-07-06,2026-07-08,2026-07-16,2026-07-17`。

## 采集验收（不构成 Actual 晋级）

- 分别报告 6/12/18 窗口的 Long/Short 候选数和四类漏斗原因；
- 12 或 18 窗口的 Short Retest 合计至少 20 笔；
- 报告直接期望，以及 1-2 / 3-4 / 5-6 月三段稳定性；
- 将通过初筛的候选重新接入组合模拟器，报告替代链影响；
- 未满足以上要求时，不改 Actual 策略、不安排 Smoke。
