# v2.17 双槽离线连接器单日盲验复核

日期：2026-07-26

## 裁决

`2026-02-11`未参与v2.17连接器修模，本轮按7.81冻结门槛执行，身份、槽位、前置中止、退出角色和金额Gate全部通过。v2.17离线连接器一致性Gate正式关闭，可以恢复离线盈利研究；该结论不代表当前策略已经盈利晋级，也不触发全量Replay。

## 证据

- 日期：`2026-02-11`
- Snapshot：`OPF-20260725-170210`
- 版本：`OPF_RESEARCH_2.17 / ACTUAL_EXEC_2.42`
- 盲验证据：`v217_blind_20260211_evidence/`
- 文件：23个，共25,550,798字节；与活动日志同Snapshot逐文件SHA256核对，差异0。
- 活动日志：92个文件，未清理。

## 盲验前发现并修正的口径错误

首次冻结脚本输出退出角色`81.82%`。两笔差异均为Short `ZoneBirthResearch`：离线标记`SL`，Actual标记`SPLIT_BASE_SL_RUNNER_SL`，两者金额相同。

没有直接将二者标签归一化。代码核查确认：

- 盈利研究静态表为Short ZoneBirth R10/R20选择`F1.5_T12`；
- Actual执行器的`UsesZoneBirthSplitRunnerV172`优先提交`2.5R Base + 4R Runner`分拆OCO；
- 因此研究退出和当前可部署执行不是同一生命周期，简单标签归一化会掩盖盈利单的真实金额与持仓占用偏差。

连接器已改为：候选选择及G2仍使用冻结研究逻辑，但所有Short ZoneBirth成交结果和退出时间强制按Actual分拆OCO重放。新增相应回归测试，现有测试共4项全部通过。

以后所有“可部署”盈利模拟必须保留该重放规则；`F1.5_T12` ZoneBirth研究值不得直接计入可部署收益，除非未来单独修改Actual执行器并通过生命周期Smoke。

## 冻结Gate结果

- CandidateRows：12
- Predicted / Actual `ENTRY_SEND`：12 / 12
- 精确`SignalID + ResearchPath + Lane`身份：12 / 12，`100%`
- Missing / Extra：0 / 0
- Predicted / Actual Secondary：2 / 2
- Predicted / Actual PreflightAborted：1 / 1
- Actual Wait / Ready / Timeout：1 / 1 / 0
- 正常交易配对：11 / 11
- 退出角色：11 / 11，`100%`
- Quarantine：0

金额：

- 离线原始正常Gross：`-$456.75`
- Actual正常Gross：`-$480.00`
- 原始误差：`$23.25`
- 离线原始Net：`-$496.35`
- Actual AccountNet：`-$519.60`
- 原始Net误差：`$23.25`，约为Actual绝对Net的`4.47%`
- 执行缺口保守储备：`-$23.25`
- 储备后保守Net：`-$519.60`

即使不使用事后储备，原始金额误差也同时低于冻结的`$150`和15%门槛；本轮通过不依赖把误差调整为零。

## 下一步

1. 不追加校准Replay，不安排全量Replay。
2. 冻结v2.17连接器、Actual ZoneBirth分拆重放和4项回归测试。
3. 恢复2026 H1离线盈利研究，所有候选先过模拟器，再进入Smoke。
4. 下一轮离线研究必须同时输出原始观察值、执行缺口保守值及可部署退出覆盖率；不能再引用旧v2.14 Tape或不可部署的ZoneBirth `F1.5_T12`收益。
