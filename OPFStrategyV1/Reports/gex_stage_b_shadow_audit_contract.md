# GEX 阶段 B：影子审计合同

## 范围

阶段 B 只读取阶段 A 的本地 GEX 快照，并记录候选与关键位的空间关系。审计结果不得参与路径准入、排序、目标/止损、日损、时段封控或任何订单提交。

## 归档

- `*_gex_snapshot.json`：启动时实际加载的原始快照。
- `*_gex_levels.csv`：校验后的快照元数据和每条 GEX 线。
- `*_gex_candidate_audit.csv`：候选时点价格、数据状态、数据日，以及相对 CW/PW/ZG/VT/最近 Cluster 的点数距离。

## 显示

- `GEX-CW`：Call Wall，红色实线。
- `GEX-PW`：Put Wall，绿色实线。
- `GEX-ZG`：Zero Gamma，紫色虚线。
- `GEX-VT`：Volatility Trigger，橙紫交替细虚线。
- `GEX-Cluster`：OI Cluster，低饱和短虚线。
- HUD 显示状态、数据日、发布时间、可用时间、等级数量和各参考线距离。

## 数据与安全

快照在策略启动时读取；同步器独立运行。文件缺失、过期、接口失败或字段校验失败只显示 `Unavailable/Stale`，策略继续按原有规则运行。阶段 B 不联网，不修改交易配置，不新增交易路径。

## 验收

Debug 编译 0 警告/0 错误；保护链、账户边界和 GEX 快照校验测试通过。任何 Replay 或实盘验证只比较审计字段，不以 GEX 结果改变成交结果。
