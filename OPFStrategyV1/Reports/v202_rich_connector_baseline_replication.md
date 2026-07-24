# v2.02 Rich离线连接器与基线复刻

日期：2026-07-24

状态：连接器通过；Rich特征关闭时，v1.96严格逐月留一基线精确复刻通过。

## 数据源

- Rich归档：`opf_v2.02_rich_bar_final_184day_join_passed_20260724`
- Rich全量连接验收Q4：`opf_2025-q4_reconstructed_53v186_plus9v187_execution_passed_strategy_failed_62snapshots_20260721`
- KNN基线Q4：`opf_v1.91_2025-q4_actual_complete_shadow_incomplete_calibration_failed_62snapshots_20260722`
- H1：`opf_v1.94_2026-01_to_06_current_baseline_122snapshots_20260723`

KNN必须使用v1.91 Q4精确影子源。改用v1.87重建Actual源会把不同候选池混入模型，不能复刻v1.96结果。

## 连接验收

- 184个交易日：Q4 62日、H1 122日。
- Rich Bar 49,723行，`Time + Bar`重复键0。
- 重建Actual源：ExecutionDecision `46644/46644`，退出政策`315146/315146`，H1 Shadow `2283/2283`。
- KNN源：ExecutionDecision `46382/46382`，退出政策`313300/313300`，H1 Shadow `2283/2283`。
- 组件JSON错误0，Bull/Bear六组件完整。
- KNN源有1个边界候选位于`2025-10-21 22:35 / ZoneBirth Short`首个Rich Bar，展开为13条政策行；旧特征基线不受影响。后续Rich模型必须显式标记`RichUnavailable`并在训练折内处理缺失，不得把初始化0当作真实特征。

## 基线复刻

- Rich数据已连接并完成验收，但所有Rich列在本轮模型计算前剥离。
- 新结果：`v202_rich_connected_baseline_replication_lomo.csv`
- 原结果：`v196_dynamic_policy_knn_fine_lomo.csv`
- 两文件均为120行，逐行差异0。
- 两文件SHA256均为`B91E2D9E376B5FA1779B0A942247B9E37A106176BC1F723824A2C8B449585E7C`。
- 最佳观察：1399笔，Net`+$17,815.04`。
- 最佳保守：1418笔，Net`+$16,663.88`。

## 本阶段约束

- 未修改策略DLL，未编译，未清理或生成ATAS活动订单日志。
- 下一阶段才逐组启用Rich特征，继续严格逐月留一。
- 新结果未同时超过观察`+$17,815.04`和保守`+$16,663.88`前，不安排Smoke或ATAS Replay。

## 文件SHA256

```text
v202_rich_connector_validation.csv ACA40A70F8E40E1349D3747F7B7E66EA73711BF7966D17C7D786ED40430ECF7B
v202_rich_knn_source_join_validation.csv FA48ABDD27C7A48EFABC47865352BE878A1D346FAD432FBB5AE29E8516339DA2
v202_rich_connected_baseline_replication_lomo.csv B91E2D9E376B5FA1779B0A942247B9E37A106176BC1F723824A2C8B449585E7C
Connect-OPFRichFeatures.py 8E9FFC027462B73EC7A0D9A93CD7FBA62E8F9BB3526C22AA4CCBD7B43CC716D9
```
