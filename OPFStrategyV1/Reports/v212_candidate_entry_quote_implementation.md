# v2.12 候选执行报价锚实现

日期：2026-07-25

## 目标

当前唯一主线仍是离线模拟器与ATAS Historical Replay一致性。在模拟器通过未参与校准的Replay验收前，盈利研究、路径优化、盈利Smoke和大规模Replay全部暂停。

v2.11阶段C已经证明选择、逐日笔数、退出角色和生命周期可以精确匹配，但计划Entry/Stop计价造成Gross相对误差27.07%。本版本只补足候选执行时刻的Bid/Ask锚，不修改任何交易规则。

## 证据与选择

v2.06当前执行管线1,653笔正常交易显示，静态成交修正不可用：计划风险到真实风险的方向会按月份变化；全局、方向或路径中位数修正均未稳定优于不修正。

策略已有的`ENTRY_QUOTE_PREFLIGHT_OK_V178`记录了真实入场前Bid/Ask。只用阶段B学习、阶段C作已消费诊断时：

- 阶段B Long相对Ask、Short相对Bid的额外成交偏移中位数均为0点。
- 阶段C使用Long=Ask、Short=Bid重算，Actual Gross `$193.00`、模拟Gross `$195.75`。
- Gross偏差从`+$52.25 / 27.07%`降至`+$2.75 / 1.42%`。
- 身份、逐日笔数和退出角色保持100%；单笔Gross MAE从`$7.13`降至`$2.94`。

阶段C已被查看，上述结果只证明报价锚值得实施，不恢复阶段C盲测资格。

## 最小实现

- Research：`OPF_RESEARCH_2.12`
- Actual：继续`ACTUAL_EXEC_2.39`
- `decision_tape_calibration.csv`新增：
  - `EntryBid`
  - `EntryAsk`
  - `EntryQuoteValid`
- 每个候选在其可执行`EntryTime/EntryBar`创建Tracker时冻结`BestBid/BestAsk`。
- 离线连接器：报价有效时Long使用Ask、Short使用Bid；不添加拟合滑点。旧v2.11证据缺少报价时仍回退原Entry，仅用于历史兼容。
- 校验器新增可选严格Gate：全部候选报价有效；Execute候选的Bid/Ask与同时间、同Bar的`ENTRY_QUOTE_PREFLIGHT_OK_V178`完全一致。
- Actual门禁、下单、数量、退出、日损、日上限及生命周期均未改变。

## 构建与证据处理

- Debug构建：0警告、0错误
- 项目与ATAS部署DLL SHA256：`2A223CB8F2C301035A7F393CAA731A69CD4E8FA8FFC8CBCA3827A233F909819F`
- v2.11阶段C失败证据归档：`opf_v2.11_calibration_connector_stage_c_amount_failed_2snapshots_20260725`
- 归档前文件：44
- 字节：7,670,347
- Manifest SHA256：`EE5A82E1B36BD87ECB7B7E94C43B6FF3B4C52085683BA4949DAB424F14CA068B`
- 重新编译并部署后活动日志文件数：0

## 首轮Gate

首轮只运行开发回归日：

`2026-01-06,2026-01-28`

要求：

1. 候选、逐Bar和生命周期原有采集Gate全部通过。
2. `EntryQuoteValid`覆盖100%。
3. Execute候选报价与Preflight报价匹配100%。
4. 相对v2.11同日Actual身份不低于95%、退出角色不低于95%、逐日笔数误差不超过1。
5. 报价锚连接器Gross绝对误差不超过`$100`且相对误差不超过10%。

首轮通过后才预注册新的两日盲测。盲测仍要求身份不低于90%、逐日笔数误差不超过1、Gross绝对误差不超过`$150`且相对误差不超过15%、生命周期零缺陷；未通过则继续修模拟器，不进入盈利研究。
