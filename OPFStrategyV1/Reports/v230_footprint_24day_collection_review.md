# v2.30 阶段 2：24 日 Footprint Data Only 采集验收与标签连接诊断

日期：2026-07-28  
范围：只验证数据采集、可用性和候选身份连接；不产生 alpha、G2 筛选或收益结论。

## 采集证据

首轮 2 日归档：

`opf_v230_footprint_dataonly_2day_gate_passed_2snapshots_20260727`

扩展 22 日归档：

`opf_v230_footprint_dataonly_24day_collection_passed_22new_snapshots_20260728`

两份归档均已逐文件哈希校验通过。扩展归档为 374 文件、50,480,399 bytes、`HashMismatch=0`，Manifest SHA256 为：

`7CD9310D0CFFF1DD922D28E2C5DC14A2534DF8213D82DB9FBF3C8FB1AAC758C1`

## 24 日采集 Gate

| 检查项 | 结果 |
|---|---:|
| Snapshot | 24 / 24 通过 |
| Footprint 候选行 | 6,381 |
| 完成预热行 | 6,362 |
| 预热未完成行 | 19 |
| Actual 订单文件 | 0 |
| 缺失字段 / 非法行 | 0 / 0 |
| 30 秒量大于 60 秒量 | 0 |
| 完整预热行 POC 无效 | 0 |
| 与同 Snapshot `risk_evaluations.csv` 键连接缺失 | 0 |

预热未完成的 19 行保留用于启动审计；任何特征研究、标签训练和阈值选择都必须排除它们。

结论：阶段 2 的 **Footprint Data Only 数据集 Gate 通过**。本结论仅说明：原始 `OnNewTrade` 输入、时间边界、特征持久化和候选键连接均完整；不说明任何特征有预测能力。

## 旧 Actual 档案的只读身份连接诊断

将完成预热的 6,362 行按：

`SignalID + DecisionTime/Time + DecisionBar/Bar + ResearchPath`

与以下旧 Actual 档案的 `execution_decisions.csv` 作只读连接：

- `opf_v2.18_2026-h1_full_replay_passed_122snapshots_20260726`
- `opf_v2.18_2026-july-mtd_oos-failed_17snapshots_20260726`
- `opf_v2.18_2026-july-mtd_paired-rerun-failed_17snapshots_20260726`

结果：6,358 / 6,362（99.94%）完成候选身份连接；连接行中有 215 笔 `Execute`，未发现可独立判定的 `Secondary/-S2` 额外标签。

这证明当前候选键和历史候选流高度稳定，故有必要准备同冻结 Actual 标签采集；但不得产生收益结论，原因如下：

| 当前 Footprint 数据 | 旧 Actual 标签 |
|---|---|
| `OPF_RESEARCH_2.30` | `OPF_RESEARCH_2.18` |
| `ACTUAL_EXEC_2.47` | 旧执行档案，非当前冻结配置 |
| Data Only，强制 Actual orders=0 | 含旧策略的真实订单生命周期 |

版本与执行生命周期均不同。将旧 PnL、TradeID 或 G2 状态硬拼给当前 Feature，会把旧执行规则当成当前策略的可部署结果，违反阶段 2 数据合同。

## 阶段裁决与下一步

1. 阶段 2 的 Data Only 采集通过；G2 Outcome Label Gate 尚未开始，不能进入微观结构 alpha 或阈值研究。
2. 下一步应为 **同冻结 Actual 标签的最小分层 Replay**：使用同一 DLL / 运行档案的 `OPF_RESEARCH_2.30 / ACTUAL_EXEC_2.47`，覆盖已采集的 24 日，不重新采 Footprint Data Only。
3. 每个标签 Snapshot 必须保存 `execution_decisions`、执行订单生命周期、TradeID 与结果证据；只有随后按同键严格连接成功的 Actual 结果，才是 G2 的正式 Outcome 标签。
4. 此标签 Replay 会产生 Actual 订单，需先设计唯一运行档案并在 DLL 改版时自动加载。当前不编译、不清理活动采集日志。
