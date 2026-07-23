# v1.96 离线KNN基线归档

归档日期：2026-07-23

状态：用户降低晋级标准后明确接受为下一阶段基线。

口径说明：

- 样本：2025 Q4 62个Snapshot＋2026 H1 122个有效Snapshot。
- 组合约束：单ActiveTrade、每日15笔、日损`-$300`、美盘开盘半小时禁单、Globex平仓。
- 手续费：2手MNQ每笔`$2.40`。
- 最佳观察参数：K=40、类别权重0.75、收缩0、预测R门槛0.075。
- 最佳保守参数：K=40、类别权重0.75、收缩15、预测R门槛0.075。
- 观察结果：1399笔，Gross`+$21,172.64`，手续费`$3,357.60`，Net`+$17,815.04`。
- 保守结果：1418笔，Gross`+$20,067.08`，手续费`$3,403.20`，Net`+$16,663.88`。
- 2025-10至2026-06每个月在观察与保守口径下均为正Net。

该Tag表示离线组合基线通过，不表示策略DLL功能Smoke通过，也不表示ATAS Replay全量晋级通过。

## SHA256

```text
v196_candidate_priority_cross_period.csv 729A393ABE59CB0DDC09D91936D15CED9F5C8C6BD30FC2B48886D29A3BA187E8
v196_dynamic_policy_full_context_lomo.csv 91939AE448F33F59AD3B6F1DE21546889FE3137E5E1831951C49D2E25C5288FD
v196_dynamic_policy_signal_confirmation_lomo.csv 9E709BB10DC3E63401C6C71AD26A7FE5FF235716DA8EDF34BCE6AE778EA458D2
v196_dynamic_policy_ridge_multitarget_lomo.csv E281E753BCEF33B383108A8BB7F1785063D08A6667194E90EA07372CAB4F508E
v196_dynamic_policy_knn_lomo.csv 077716E6DBBC8F495DBAC94EC43AD85B1CE264552E29D46A999A70E99B826A4E
v196_dynamic_policy_knn_fine_lomo.csv B91E2D9E376B5FA1779B0A942247B9E37A106176BC1F723824A2C8B449585E7C
v196_dynamic_policy_knn_all_entry_features_lomo.csv EF40C7320D62BECEB8D7106F7C1236557EB69273618C99DC1F90B8CFA19568E7
v196_dynamic_policy_knn_best_monthly.csv CC272D2C896F9FCE4C65863AC7AC8B45D6E077BA9CBB9C900ABC303AC8E41193
```
