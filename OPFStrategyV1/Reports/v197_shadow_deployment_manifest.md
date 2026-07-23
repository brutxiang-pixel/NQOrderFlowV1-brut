# v1.97 KNN Shadow部署归档

## 版本边界

- Research Schema：`OPF_RESEARCH_1.97`
- Actual Execution：`ACTUAL_EXEC_2.32`
- KNN模型：`OPF_KNN_SHADOW_1.97`
- 模型SHA256：`A378B7478A0A4022A525CB66887BDC1D9C540E2A3A7D9C94FBCFA7735D8483D3`
- 最终DLL SHA256：`28D1C5C240671104CB5D625072D0B1F4A7DC655D8E6A3041A108B3AEF5C4A0A0`

该版本仅部署Shadow评分。KNN不参与Actual准入、订单、TP/SL、仓位或风控。

## 失败诊断证据

- 归档：`opf_v1.97_knn_shadow_parity_passed_actual_zero_impact_failed_18snapshots_20260723`
- 18 Snapshot，360个原始文件，41,447,713字节
- 逐文件SHA256不匹配：0
- `sha256_manifest.csv` SHA256：`59015AA07DB3EAD8ACE3B08E0683AAB6D37863AF67419DBDBB27099207862D85`
- 结论：4520条Shadow数学一致性通过，但同步KNN评分及CSV写盘干扰Historical Replay订单回调，Actual零影响失败。

## 最终通过证据

- 归档：`opf_v1.97_knn_shadow_zero_impact_passed_4snapshots_20260723`
- 日期：`2025-10-21,2025-12-02,2025-12-12,2026-02-18`
- 4 Snapshot，80个原始文件，9,670,403字节
- 逐文件SHA256不匹配：0
- `sha256_manifest.csv` SHA256：`39EB533177461F5F737BCFE6C27D3E618F4526AF93D1F3B3087E1959CA6EA23F`
- 1058个ExecutionDecision对应1058条KNN记录，独立复算差异为0
- 48个Actual Execute身份、48个TradeID和48个ExitRole与v1.94相同四日基线一致
- 48笔正常交易、48条账户记录、48次保护清理
- 异常账户影响、订单失败、模型加载失败和评分失败均为0

## 晋级结论

v1.97通过KNN Shadow数学一致性和Actual零影响Gate。该结论不代表KNN已控制真实下单，也不代表策略收益晋级。
