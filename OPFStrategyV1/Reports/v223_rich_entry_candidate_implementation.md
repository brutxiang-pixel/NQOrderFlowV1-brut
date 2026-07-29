# v2.23 Rich路径专属候选实施与Smoke准备

## 实施范围

- Primary Long `ObservationConfirm`：仅当`RegimeBars >= 8`允许下单。
- Primary Short `BreakawayFvg`：同时要求`SetupQualityScore >= 88`、实际风险`>=12`、该方向的`VWAPSide`趋势组件通过。
- 门禁位于既有日损、周Long门、日单量与双槽数判断之后，Secondary判定之前；Secondary、退出、仓位和其它路径均未改动。

## 可观测性

- Long拒绝：`SKIP_PRIMARY_OC_REGIME_BARS_V223`。
- Breakaway拒绝：`SKIP_PRIMARY_BREAKAWAY_RICH_V223`。
- 每条拒绝均写入Decision和Event，携带门禁输入值，供Replay逐笔核对。

## 构建与日志

- `OPF_RESEARCH_2.23`。
- Debug构建：0警告、0错误。
- 本地与ATAS部署DLL SHA256：`B4BD584650FA5D3EBA8805B2571F171075552B7911458FBA23BC3C08B76A0165`。
- 编译前证据已归档为`opf_v2.22_rich_july_dataonly_collected_pre_v223_35snapshots_20260727`：756源文件、35个Config、7个Rich Bar；归档Manifest SHA256为`6D27B921CAE7D498DBFC1088C19AE6387B4254B4EFA6AFF777E0FE28E44222E1`。
- 归档后活动OPF目录和旧根日志均已清空。

## 6日Actual Smoke

`2026-01-27,2026-02-24,2026-03-20,2026-05-08,2026-07-07,2026-07-24`

运行配置：`EnableReplayOrders=true`、`Rich Bar Data Collection Only=false`、`Decision Tape Calibration Collection=true`。

通过条件：Schema均为2.23；两类V223拒绝仅命中Primary目标路径；订单生命周期零缺陷、保护清理完整；每日正常交易/AccountNet可解释；与冻结候选的门禁方向一致。Smoke只验证功能和方向，不要求逐美元复刻离线模拟。
