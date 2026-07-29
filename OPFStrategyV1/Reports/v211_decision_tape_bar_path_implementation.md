# v2.11 Decision Tape逐Bar路径采集实现（2026-07-25）

## 目标

补齐v2.10只有汇总里程碑、没有逐Bar价格路径的缺口，使离线连接器能够按顺序重放TP、SL、BE、TimeStop和ActiveTrade占用。该版本不改变任何Actual交易规则。

## 版本与输出

- Research：`OPF_RESEARCH_2.11`
- Actual执行仍为：`ACTUAL_EXEC_2.39`
- 原`decision_tape_calibration.csv`保持不变。
- 新增`decision_tape_calibration_bars.csv`：每个候选记录Entry Bar（RelativeBar 0）及后续最多36根闭合M5的OHLC。
- Bar记录与汇总记录一样只在策略停止时批量落盘，避免Replay热路径文件IO。

## 冻结边界

- 不修改订单数量、路径门禁、入场、TP/SL、TimeStop、日损、日上限、异常隔离或生命周期代码。
- 本轮只验证采集完整性与Actual零影响，不看盈利。
- 校验器要求候选键100%、Bar路径无重复、RelativeBar连续、时间按5分钟连续、每个候选Bar行数=`BarsTracked+1`且最大RelativeBar不超过36。

## 补采日期

`2026-01-06,2026-01-28`

只有逐Bar采集Gate和原阶段A Actual零影响同时通过，才进入离线连接器开发。

## 构建与证据

- Debug构建：0警告、0错误。
- 项目DLL与ATAS部署DLL SHA256均为`015B4017C8B006F3F44AA41BFC142BC79A752E3383CEB7286D60116B002CF599`。
- v2.10阶段A证据已归档为`opf_v2.10_stage_a_collection_pass_bar_path_insufficient_2snapshots_20260725`：47个Manifest前文件、5,658,242字节、HashMismatch=0，Manifest SHA256=`3C26935CC89A4F2AABD6D158FE01DDEA241F512C142EAA860A0B6816DB947C31`。
- 重新编译部署后活动日志已清空。
