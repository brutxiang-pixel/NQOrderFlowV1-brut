# v2.17 三日第二槽保护重评 Smoke 复核

日期：2026-07-26

## 裁决

v2.17通过本轮功能Smoke：第二槽保护后重评边界修复有效，旧版`currentBar=entryBar+1`误过期已消失；Globex终态、保护生命周期和风险硬门继续为零缺陷。

本裁决只代表v2.17双槽执行修复通过，不代表离线模拟器与Actual已经完成最终一致性验收，也不据此安排全量Replay。

## 证据范围

- 日期：`2026-01-02,2026-04-24,2026-05-05`
- Snapshot：`OPF-20260725-161545`、`OPF-20260725-161801`、`OPF-20260725-162011`
- 版本：`OPF_RESEARCH_2.17 / ACTUAL_EXEC_2.42`
- 每个Snapshot 23个文件，共69个文件；已复制并核对归档到`v217_3day_secondary_retry_smoke_evidence/`，活动日志原件未清理。

## 主Gate

- `SECONDARY_WAITING_FOR_PROTECTION_V216=8`
- `SECONDARY_PROTECTION_READY_RETRY_V216=7`
- `SECONDARY_PROTECTION_RETRY_REJECTED_V216=1`
- 旧错误`currentBar=entryBar+1`过期为0。
- 唯一拒绝为`closedBar=857|entryBar=856`，即下一Bar已经关闭仍未收到保护，符合真实超时合同。
- 7次Ready Retry中6次通过刷新报价及全门禁后形成第二槽`ENTRY_SEND`；1次`20260504-2235...OC`刷新后风险由19.50变19.75点，结构奖励仅0.50点，因`EstimatedRRTooLow:rr=0.0253,min=0.8`正确拒绝，证明重评没有绕过策略门。

## 生命周期与风险

- `ACTIVE_ON_STOP=0`
- `GLOBEX_CLOSEOUT_UNCONFIRMED=0`
- `TRADE_ORDER_MISMATCH=0`
- `PROTECTION_LOST_BEFORE_EXIT=0`
- `SKIP_MANAGED_POSITION_MISMATCH_V215=0`
- `PROTECTION_CLEANUP_PENDING=47`且`PROTECTION_CLEANUP_DONE=47`
- `ENTRY_QUOTE_RISK_EXCEEDED_V216=2`，均在账户下单前中止；严格风险带及第二槽报价合计风险未违规。
- 成交后单笔风险、严格风险带和`$300`并发风险兜底触发均为0。

## 账户与第二槽

- `ENTRY_SEND=49`：45笔Normal、2笔Quarantine、2笔报价风险前置中止。
- 账户47笔，Gross `+$2,036.50`、Net `+$1,867.30`；其中正常45笔，Quarantine 2笔仅手续费`-$7.20`。
- 第二槽13笔：TP 5笔/Gross`+$919.50`，SL 4笔/`-$242.50`，TimeStop 4笔/`+$306.50`，第二槽Gross合计`+$983.50`。

## 离线一致性状态

三日5分钟精确离线参考为51笔/15笔第二槽、Gross `+$2,716.45`、Net `+$2,532.85`。Actual账户为47笔/13笔第二槽、Net `+$1,867.30`，差`-$665.55`。

按`SignalID + ResearchPath + Lane`比较ENTRY_SEND身份：

- v2.16同三日：32/51完全一致，第二槽4/15一致。
- v2.17：38/51完全一致，第二槽10/15一致。
- 新增的6个第二槽精确身份与6次成功Ready Retry一一对应，证明本次代码修复方向正确。
- 仍有13个离线身份缺失及11个Actual新增/换位身份；共同身份的离线Net`+$1,911.37`、Actual Net`+$1,761.40`，直接尺度差`-$149.97`；身份/替代链净差`-$515.58`。

因此当前剩余问题已从“Actual漏掉保护后第二槽”收缩为“离线连接器尚未完整复刻刷新报价、门禁重算、报价前置中止及其后续占用链”。

## 下一步建议

不继续追加Replay，也不修改Actual DLL。先在离线连接器复刻本轮三日事件顺序：

1. 缓存同柱`PrimaryNotProtected`候选，允许到`entryBar+1`保护回调；下一Bar关闭仍无保护则超时。
2. Ready后使用当时Bid/Ask重算风险、EstimatedRR、路径门、G2、日门和`$300`并发风险；允许重评后正常拒绝。
3. 在ENTRY_SEND之后、账户成交之前建模报价风险前置中止，不能把被中止订单计为交易或占用槽位。
4. 用这三日作为校准集，要求复刻8 Wait/7 Ready/1 Timeout、6个重评第二槽发送、2个报价中止、49个ENTRY_SEND身份和13个第二槽身份；达到后再选未用于修模的日期做盲验。
