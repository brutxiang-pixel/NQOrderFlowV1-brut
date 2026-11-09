# NQOrderFlowV1：运行与证据入口

更新：2026-11-09。项目为经典 ATAS 图表策略，DLL 为 `OPFStrategyV1.dll`。Scalper、ATAS X 不属于本项目。

| 状态 | 策略全称 | 研究结构 | 执行配置 | 入口/证据 |
| --- | --- | --- | --- | --- |
| 当前部署，模拟账户 Auto | OPF_INTRADAY_2.17 | OPF_RESEARCH_2.34 | ACTUAL_EXEC_2.53 | 部署 DLL SHA256：`8B512738E740F91371541BC798C0732FF9E85AD896F14538645DDC98BD28DA30` |
| 本轮候选，尚未部署 | OPF_INTRADAY_2.19 | OPF_RESEARCH_2.34 | ACTUAL_EXEC_2.54 | [冻结配置](OPFStrategyV1/Configs/OPFStrategyV1_sim_forward_2.19.json)，配置参数与2.18候选相同，策略行为及DLL身份不同 |

版本引用必须使用表内全称；策略版本、研究 schema、执行配置版本互不等同。候选评分定义为 `OPF_SCORING_0.2`，入场扩展阈值和逐路径退出表为 `OPF_ENTRY_EXIT_2.19`，见 [参数表](OPFStrategyV1/Strategy/EntryExitPolicyParameters.cs)。`ActualTargetR` 仅是未匹配路径的兜底，七条实际路径使用版本化退出表；两手冰山另外执行1R/1.5R分批目标。冰山回踩、数量降级及结构保护见 [2.19交付说明](OPFStrategyV1/Docs/2026-11-09_iceberg_execution_2.19.md)。

活动配置文件：`%APPDATA%/ATAS/StrategyConfigs/OPFStrategyV1_actual_execution.json`。本轮读取的原文件 SHA256 为 `62158DAEE9E3DC523D072434D66064AF3280C9817D5E17AD887F9C58746C49A7`，未替换。最近已核对的 NQ/MNQ 启动快照为 2026-11-06 06:52 UTC，AccountScope=`DEMO2AC94`、Auto、七路径；这是已有会话证据，下一次启动仍须重新核对有效模式和账户。

七条实际路径：`BreakawayFvg_Qualified`、`FailureReverse_RetestFailed`、`ESResponseAlignedBreakout`、`ESWallRejectionReversal`、`ESExpectedMoveExpansion`、`ESGammaConcentrationResponse`、`CrossMarketIcebergOrderFlowReaction`。兼容名 `NativeIcebergReaction` 不额外算一条路径。NQ 最多一手、MNQ 最多两手。

2026-11-07 用户决策：模拟账户 Auto（含冰山）继续前向验证；日交易笔数上限为 0（不限），NQ/MNQ 周亏损门禁关闭。代码现有周门是 Long 专属门，本轮设为 0，不新增总周亏损门。其余日损、仓位、事件去重及订单保护保持原有行为。图表 UTC；候选开盘区间为美东现金开盘 09:30–10:00，自动处理夏令时。

| 证据 | 配置与样本 | 可支持的结论 |
| --- | --- | --- |
| 本轮工程测试与构建 | OPF_INTRADAY_2.19 / ACTUAL_EXEC_2.54 | 冰山回踩/数量、结构保护、分腿记账、时间映射及既有门禁回归；不证明收益 |
| 本轮冻结前向验证 | 模拟 Auto，至少20个未查看完整交易日 | 尚未开始计数，待部署与首个 ConfigSnapshot 验收；未完成，不宣称通过 |
| v300 历史汇总 +$26,768.42 | ACTUAL_EXEC_2.48，17路径、3手、15笔/日；2026-01至07，141日 | 旧配置样本内历史结果；不能为当前配置背书，未在本轮复算 |

废止作为当前收益证据的数字：v1.74 的 +$12,579.80、旧 KNN B91E… 口径；v300 仅保留历史适用范围。v300 原始证据位置、manifest 校验及复算限制见 [本轮记录](OPFStrategyV1/Docs/2026-11-07_review_remediation_2.18.md)。

[第三方评审](docs/reviews/REVIEW-2026-11-06.md) · [本轮处理及前向协议](OPFStrategyV1/Docs/2026-11-07_review_remediation_2.18.md) · [现有路径规则](OPFStrategyV1/Docs/current_8_execution_path_rules.md) · [编年史与恢复指南](项目恢复总览_继续开发指南.md)
