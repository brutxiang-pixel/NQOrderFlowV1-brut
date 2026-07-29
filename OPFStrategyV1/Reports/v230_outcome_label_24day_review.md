# v2.30 阶段 2：24 日同冻结 Actual Outcome Label Gate

日期：2026-07-28  
范围：将已归档 Footprint Data Only 特征与同冻结 Actual Replay 严格连接；不解释 alpha，不选择阈值。

## 运行与归档

24 / 24 个 Actual Snapshot 均为：

`OPF_RESEARCH_2.30 / ACTUAL_EXEC_2.47 / V300_OUTCOME_LABEL_24DAY / ActualExecution`

档案固定 3 手、15 笔、-$250 日损、-$500 周 Long 门、同向 G2、$300 并发初始风险。Data Only 模式均已关闭。

证据已归档：

`opf_v230_outcome_label_24day_gate_passed_24snapshots_20260728`

归档共 480 文件、58,650,725 bytes，逐文件 `HashMismatch=0`，Manifest SHA256：

`9E082607CC668197CCE88401EFBB898F04BC4D24588AD3800C738AD7BE87EC6E`

## 严格连接结果

连接键：

`SignalID + DecisionTime/Time + DecisionBar/Bar + ResearchPath`

| 项目 | 数量 |
|---|---:|
| 完成预热 Footprint 候选 | 6,362 |
| 严格连接 Actual 决策 | 6,358（99.9371%） |
| 未连接 | 4 |
| 正常 Actual 结果 | 192 |
| 其中同向 G2 第二槽 | 37 |
| 报价前置中止 | 12 |
| 已隔离异常，禁止训练 | 2 |
| NoEntry | 6,152 |

4 条未连接均为 `BreakawayRetest12/18Research`，发生于 `2026-02-23` 与 `2026-07-02`；它们与 Data Only 对旧 v2.18 的键稳定性诊断中相同，不是新引入的漂移。它们保留在 `unmatched_warm_features.csv`，且不可进入任何研究样本。

206 条 `Execute` 已完整划分为 192 条 `NormalOutcome`、12 条 `EntryAborted` 与 2 条 `ExcludedAbnormal`，没有未知终态。12 条中止均有 `ENTRY_SUBMISSION_ABORTED_V178`；两条异常均已进入账户审计并完成保护清理，绝不伪装为普通输赢标签。

## 生命周期验收

| 审计项 | 结果 |
|---|---:|
| Actual 账户影响 / 保护清理完成 | 194 / 194 |
| 保护回调 pending / resolved | 174 / 174 |
| Globex 强平 | 2 笔均有 `SESSION_FLATTEN` 正常结果与清理 |
| 孤儿仓位、未清理保护、未知终态 | 0 |

结论：**Outcome Label Gate 通过**。本次通过只授权使用 `NormalOutcome` 的 192 条可部署结果开展离线诊断；`EntryAborted`、`ExcludedAbnormal` 和 4 条未连接行必须永久排除出收益标签。

## 连接器与下一阶段限制

连接器：`Scripts/Build-OPFV230OutcomeLabels.py`。输出位于 `Reports/v230_outcome_label_validation/`：

- `outcome_labels.csv`：6,358 行、50 字段，保留完整 Footprint 特征和 Outcome 标签；
- `unmatched_warm_features.csv`：4 条显式排除记录；
- `summary.json`：可复核计数和通过状态。

G2 正常结果只有 37 条，虽覆盖 2026 年 1–7 月，但不足以支持多变量模型、自动调参或路径级阈值搜索。阶段 3 第一轮仅允许预注册的低自由度单变量、按月留一验证；任何结果均只是研究方向，不能改写 Actual 策略或进入 Smoke。
