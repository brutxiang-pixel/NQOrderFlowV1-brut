# v2.13报价Gate通过但逐笔顺序数据缺失

日期：2026-07-25

## 结论

v2.13解决了候选报价捕获边界，但完整模拟器开发Gate仍未通过。剩余主要偏差不再来自Entry报价，而是M5 OHLC无法判断同柱Stop/Target的真实逐笔先后。

## 结果

- 候选：538/538
- 逐Bar：18,853
- 候选报价有效：100%
- Execute报价与Preflight：20/20=`100%`
- ENTRY_SEND / Cleanup：20 / 20
- DangerEvents：0
- Actual / 模拟：20 / 20
- 身份：18/20=`90%`
- 退出角色：19/20=`95%`
- Actual Gross：`$1,007.00`
- 模拟Gross：`$818.28`
- 偏差：`-$188.72 / 18.74%`

采集、报价和生命周期Gate通过；身份及Gross开发Gate失败，不进入盲测。

## 单笔归因

Gross偏差几乎全部来自`2026-01-28 14:25 Short ObservationConfirm`：

- Actual：TP `+$118.00`
- M5 StopFirst模拟：SL `-$78.00`
- 单笔差：`-$196.00`
- 其余共同交易合计只差`+$7.28`

该交易的Stop和1.5R目标在校准M5中首次同时出现于Bar 1290；M5无法判断顺序。Actual订单事件显示Historical Observed TP通过逐笔保护观察先检测到目标并在入场后立即退出。

开发证据中也存在相反的同柱场景，即Actual为SL而TargetFirst会错误。因此不能把全局StopFirst改成TargetFirst，也不能读取Actual角色后回填模拟结果。

## 必需的数据修复

下一采集层建议使用全局压缩逐笔极值带：

1. OnCalculate维护单调递增的市场事件序号。
2. 每个M5 Bar只在价格创出新的运行高点或低点时缓存一条事件；非新极值tick不记录。
3. 每个校准候选记录最终Decision报价序号；Execute候选在Preflight时更新为真实送单边界序号。
4. 离线只读取候选序号之后的极值事件，按真实顺序解析Stop、Target、BE及锁盈。
5. TimeStop仍使用M5 Close；不需要保存全部逐笔，也不需要为每个候选重复保存行情。

该方案是当前能够同时避免M5顺序歧义、Actual答案泄漏和候选级重复数据的最小方案。必须先通过两日数据完整性及Actual零影响Gate，再重新建立开发连接器；模拟器未通过前继续暂停盈利研究。

本轮未修改或重新编译DLL，活动日志保留，不归档、不清理。
