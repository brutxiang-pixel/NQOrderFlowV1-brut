# v2.30 同冻结 Actual Outcome Label 运行档案

日期：2026-07-28  
目的：为阶段 2 的 24 日 Footprint 特征取得同冻结 Actual 结果标签；不修改 alpha、入场、退出、G2 或风控。

## 运行档案

部署档案：`Configs/OPFStrategyV1_v300_outcome_label_24day.json`

| 项目 | 固定值 |
|---|---|
| Research / Actual | `OPF_RESEARCH_2.30 / ACTUAL_EXEC_2.47` |
| RunProfile | `V300_OUTCOME_LABEL_24DAY` |
| RunMode | `ActualExecution` |
| Actual orders | 开启 |
| Footprint / Rich / Audit Data Only | 强制关闭 |
| 数量 / 日上限 / 日损 / 周 Long 门 | 3 手 / 15 / -$250 / -$500 |
| 第二槽 | 同向 G2；并发初始风险 $300 |

策略启动时统一 JSON 会覆盖 ATAS 面板残留的 Data Only 开关。HUD 新增 `RunProfile` 行，ConfigSnapshot 的 `ActualExecutionSettings` 也保留 `RunProfileId` 与 `RunMode`，因此不再需要用户逐日修改任何策略参数。

未知 RunMode 会安全地关闭 Actual orders；本轮指定的 `ActualExecution` 会恢复正常执行。此项仅消除运行配置漂移，不改任何候选或订单规则，故版本保持 `2.30/2.47`。

## 部署验证

- Debug 编译：0 warning / 0 error。
- 源 DLL 与 ATAS 已部署 DLL SHA256 一致：`DD92A2B14EEC46AB2B1C258E39BAF7C2F3528C9793CA217C777C93E1F762CC31`。
- 已部署档案读取结果：`V300_OUTCOME_LABEL_24DAY / ActualExecution / ACTUAL_EXEC_2.47`。
- 24 日 Footprint 证据先前已归档并校验；本次重编译后活动日志已清理（0 文件）。

## 标签 Replay 合同

运行预注册 24 个交易日。每个 Snapshot 只需运行正常 Actual Replay；**不得开启任何 Data Only 面板项，也无需重采 Footprint 数据**。验收时按：

`SignalID + DecisionTime + DecisionBar + ResearchPath`

将先前已归档、完成预热的 Footprint 行与本轮 `execution_decisions`、TradeID 及完整订单生命周期严格连接。连接失败、配置不符、生命周期异常均为 Outcome Label Gate 失败，不得直接进入 G2 微观结构研究。
