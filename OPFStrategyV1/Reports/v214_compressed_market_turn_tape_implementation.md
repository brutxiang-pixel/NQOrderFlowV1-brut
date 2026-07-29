# v2.14压缩市场转折带采集实现

日期：2026-07-25

## 目标

补足M5 OHLC无法表达的同柱Stop/Target/BE逐笔顺序，同时避免保存全部Tick或为每个候选重复行情。当前仍只验证模拟器数据层，不进行盈利研究。

## 实现

- Research：`OPF_RESEARCH_2.14`
- Actual：继续`ACTUAL_EXEC_2.39`
- 新文件：`decision_tape_market_turns.csv`
- 全局`Sequence`在每次正价格`OnCalculate`回调递增。
- 只保存：`Start / BarStart / BarEnd / TurnHigh / TurnLow / End`。
- 同方向连续价格只更新端点，方向反转时才保存局部极值。
- 所有候选共享同一份转折带，不生成候选级重复Tick文件。
- `decision_tape_calibration.csv`新增：
  - `EntryMarketSequence`
  - `ResolveMarketSequence`
- 候选在最终Execute/Skip Decision时更新Entry序号；Execute在异步Preflight成功后再次覆盖为真实送单边界序号。
- `ENTRY_QUOTE_PREFLIGHT_OK_V178`增加`marketSequence`审计值。

Actual门禁、订单、3手、每日10笔、`-$600`日损、退出和风控均未改变。

## 校验

严格校验新增检查：

- 候选Entry/Resolve序号有效且顺序正确。
- Execute候选Entry序号与Preflight序号100%一致。
- 转折序号按Snapshot严格递增且不重复。
- 转折价格全部大于0，Kind只允许冻结枚举。
- 每个候选Entry/Resolve区间位于转折带边界内。

旧v2.13证据兼容校验通过。

## 构建与证据

- v2.13证据归档：`opf_v2.13_entry_quote_pass_tick_order_missing_2snapshots_20260725`
- 归档前文件：44
- 字节：10,100,263
- Manifest SHA256：`ECA0C654D0D3902224FBAF6648DC6D2E5B6BB5C83AD68D69446B879A898FDB0B`
- Debug构建：0警告、0错误
- 项目与部署DLL SHA256：`B14EBE09E8B0B7281A854F51B12128F6B64B59AAE1D776E9F69168A99F572DD7`
- 编译后活动日志：0文件

## 首轮采集回归

日期：

`2026-01-06,2026-01-28`

本轮只裁决压缩带完整性、Execute边界一致性、两类相反同柱案例是否可按转折顺序解释，以及Actual零影响。通过后才把转折带接入离线连接器；连接器再次通过开发Gate后才重新预注册盲测。
