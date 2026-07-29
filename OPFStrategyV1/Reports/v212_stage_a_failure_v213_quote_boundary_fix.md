# v2.12开发回归失败与v2.13报价边界修复

日期：2026-07-25

## v2.12回归结果

开发日：`2026-01-06,2026-01-28`

- 538/538候选、18,853条逐Bar记录完整。
- 20次ENTRY_SEND与20次清理闭合，危险事件0。
- 候选报价有效率100%。
- Execute报价与Preflight匹配19/20=`95%`，未达到100%。
- Actual与模拟均20笔，但身份18/20=`90%`，退出角色18/20=`90%`。
- Actual Gross `$952.00`，模拟Gross `$818.28`，偏差`-$133.72 / 14.05%`。

因此v2.12开发Gate失败，不进入盲测。

## 根因

`2026-01-28 03:50 Short ObservationConfirm`在Tracker创建时记录Bid/Ask `26200.75/26201.25`，异步订单Preflight实际使用`26198.50/26199.00`。同一处理链内行情移动2.25点，使离线判断TP、Actual为SL。

候选Tracker创建发生在路径、风险、日损、日上限和ActiveTrade门禁之前；Execute订单随后进入异步队列。早期报价不能代表真实送单边界。

另一个角色差异为`2026-01-28 14:25`同柱TP/SL顺序，属于M5无法辨识的已知歧义。20笔允许1笔角色差异，因此报价边界才是当前主要缺陷。

## v2.13最小修复

- Research升级为`OPF_RESEARCH_2.13`，Actual保持`ACTUAL_EXEC_2.39`。
- 所有候选在最终Execute/Skip决策形成时刷新冻结Bid/Ask。
- Execute候选在异步Preflight成功、`OpenOrderAsync`之前，再用真实Preflight Bid/Ask覆盖。
- 原始Tracker创建报价只作为临时值，不再作为最终输出。
- Actual订单、门禁、数量、退出和风控未改变。

## 构建与证据

- v2.12失败证据归档：`opf_v2.12_entry_quote_stage_a_failed_2snapshots_20260725`
- 归档前文件：44
- 字节：10,097,403
- Manifest SHA256：`4106D94AE386FD154FAB573767594D4AE4BD74928F05E6A0C8F61EAC3836CED1`
- v2.13 Debug构建：0警告、0错误
- 项目与部署DLL SHA256：`BF917B8259AAD2CBA9F57753EFC26A197ABB3DA572A5DD7FB41FB026F3C512BA`
- 编译部署后活动日志：0文件

## 下一回归

仍使用开发日：

`2026-01-06,2026-01-28`

Gate保持不变，不因v2.12失败下修。通过后才预注册新的两日盲测。
