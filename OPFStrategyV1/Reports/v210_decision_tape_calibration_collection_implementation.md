# v2.10 Decision Tape校准采集实现（2026-07-25）

## 唯一目标

在离线模拟器与ATAS Replay达到冻结一致性Gate前，停止全部盈利搜索、路径优化、Smoke晋级和全量Replay。v2.10只建立当前规则下的完整候选标准答案输入链，不改变任何实际交易规则。

## 版本与行为边界

- Research：`OPF_RESEARCH_2.10`
- Actual执行：继续使用`ACTUAL_EXEC_2.39`
- 订单数量、五条禁用路径、六条退出政策、ZoneBirth 2＋1、每日10笔、AccountNet日损`-$600`、开盘禁单、Globex平仓和异常隔离全部不变。
- 新增面板项：`Decision Tape Calibration Collection`，v2.10默认`true`。
- 该模式不关闭Actual订单、不改变决策结果、不参与门禁，只增加缓冲日志和候选生命周期跟踪。
- `Rich Bar Data Collection Only`必须保持`false`；否则Actual订单会被关闭，无法得到真实ActiveTrade、日损、日上限和订单回调标准答案。

## 新采集链

每个到达live-readiness边界、且通过基础路径可用性检查的候选，在策略质量、路径质量、风险/RR、同柱、日损、日上限和ActiveTrade门禁之前创建独立校准Tracker：

- 入场Bar立即纳入OHLC，保留ATAS Historical Replay的入场同柱StopFirst语义。
- 最长跟踪36根闭合M5；策略停止或Globex关闭时强制写出。
- 记录Entry/Stop/Risk、当前TargetR、MFE/MAE、First Stop、0.75R/1R/1.5R/2R/2.5R/3R/4R以及各BE/锁1R里程碑。
- 最终追加原始`Decision/Reason/TradeID`，因此静态Skip、动态Skip和Execute使用同一候选键对账。
- 输出文件：`*_decision_tape_calibration.csv`。写入采用内存缓冲，只在停止时批量落盘，降低1000倍Replay时的IO影响。

## 一致性Gate v1（冻结）

### 阶段A：采集链校准日

日期：`2026-01-06,2026-01-28`

必须全部满足：

1. `OPF_RESEARCH_2.10 / ACTUAL_EXEC_2.39`且面板三项为：`EnableReplayOrders=true`、`RichBarDataCollectionOnly=false`、`DecisionTapeCalibrationCollection=true`。
2. 可采集的Execution Decision键与校准行一一对应；重复、缺失和无Decision行均为0。
3. Execute候选的校准覆盖、Decision/Reason和TradeID匹配均为100%。
4. 所有校准行在36 Bar内完成或由策略停止/Globex明确Flush；`BarsTracked>36`为0。
5. `2026-01-06`稳定缺失的23:10、06:10、10:10候选必须进入校准表；23:20 Short Failure Wide必须记录入场Bar Stop里程碑。
6. ENTRY_SEND与PROTECTION_CLEANUP_DONE闭合；订单失败、回调超时、错配、重复退出和孤儿仓位为0。
7. Actual身份相对v2.09历史标准：1月6日目标10/10；1月28日允许因Cleanup抖动为9/10，但两日合计不得低于95%，且不得出现系统性Gross漂移。

阶段A只裁决采集模式是否零影响、数据是否完整，不裁决盈利。

### 阶段B：连接器校准

- 只使用阶段A两日开发新连接器。
- 当前配置必须从完整候选表重放，不能直接读取OriginalDecision作为模拟答案；OriginalDecision只用于评分。
- 两日合计身份至少95%，逐日笔数误差不超过1笔；匹配交易退出角色至少95%；Gross误差同时满足绝对值不超过`$100`且相对值不超过10%。
- 未通过前只修连接器，不继续向用户索要更多日期。

### 阶段C：未参与修复的验证日

预注册：`2026-03-10,2026-05-12`。

- 只有阶段B通过后才清日志运行。
- 连接器冻结后不得按验证结果改阈值。
- 两日合计身份至少90%，逐日笔数误差不超过1笔；Gross误差绝对值不超过`$150`且相对值不超过15%；订单生命周期零缺陷。
- 阶段C失败则模拟器继续判定不可用，不进入H1采集或盈利研究。

## 构建与日志

- Debug构建0警告、0错误。
- 项目与ATAS部署DLL SHA256均为`C6252F333080DCB8571332FA5C91F5F277AF1B1E27C0194F040F4EFA83AB6536`。
- 编译前活动两日证据已归档为`opf_v2.09_2day_profit_calibration_rerun_failed_2snapshots_20260725`：40个证据文件、4,917,016字节；含Manifest共41个文件、4,921,765字节；HashMismatch=0；Manifest SHA256=`AD5BEAE43659673EFBA65D55A682C13DC07E333CFD58D3A9B2924A1B5F0D250D`。
- 重新编译后已按规则清理活动日志，当前文件数为0。

## 本轮Smoke

只运行阶段A：`2026-01-06,2026-01-28`。

这不是盈利Smoke，不得依据PnL判断v2.10通过或失败。完成后先运行`Validate-OPFV210DecisionTapeCalibration.py`，再决定是否进入连接器开发。
