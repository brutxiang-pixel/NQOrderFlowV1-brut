# v2.20 三日周门与Historical虚拟目标连续回归复核

日期：2026-07-26

## 结论

本轮连续运行`2026-04-06,2026-04-07,2026-04-08`，三个Snapshot均为`OPF_RESEARCH_2.20 / ACTUAL_EXEC_2.45`，配置固定为3手、每日15笔、AccountNet日损`-$250`、周Long损失门`-$500`。

v2.20 Historical Replay虚拟TP适配器与v2.19周度Long损失门组合闭环通过。原v2.19故障交易`20260408-0740-Short-1127`已直接复现并正常完成，未再出现Dormant旧价成交、第4手退出、孤儿仓位或后续Readiness阻断。因此v2.20可标记为“功能与生命周期基线通过”，但本轮不是盈利晋级，也不据此安排H1全量Replay。

## 每日结果

| 日期 | 交易 | Long | Short | Gross | AccountNet |
|---|---:|---:|---:|---:|---:|
| 2026-04-06 | 2 | 1 | 1 | -$284.50 | -$291.70 |
| 2026-04-07 | 3 | 1 | 2 | -$248.50 | -$259.30 |
| 2026-04-08 | 4 | 0 | 4 | +$378.50 | +$364.10 |

4月6日交易数低于部分历史运行，可能来自1000倍Replay启动时间差或候选边界漂移；它不影响本轮对订单生命周期与跨Snapshot周门状态的裁决。

## 周门连续状态

- 4月7日启动时正确恢复4月6日：周AccountNet`-$291.70`、LongNet`-$161.60`。
- 4月7日02:20 Short退出后，周AccountNet达到`-$551.00`、LongNet为`-$245.20`，触发`WEEKLY_LONG_LOSS_GATE_TRIGGERED_V219`，Trigger=`Account`。
- 4月8日启动时正确恢复周AccountNet`-$551.00`、LongNet`-$245.20`。
- 4月8日记录7次`SKIP_WEEKLY_LONG_LOSS_V219`；Long被阻止，Short仍可继续交易。
- 4月8日最终完成4笔Short，Gross`+$378.50`、AccountNet`+$364.10`，证明周门没有演变为全局禁单。

## 原故障交易直接回归

TradeID：`20260408-0740-Short-1127`

- 07:40入场：Short 3手，实际均价`25145.75`。
- 真实SL：`25163.75`；虚拟TP：`25118.75`。
- 仅写入`HISTORICAL_VIRTUAL_TP_ARMED_V220`，没有向ATAS提交Historical Dormant Limit。
- 08:10真实SL成交3手，退出均价`25162.25`。
- EntryQty=3、ExitQty=3，随后正常完成`PROTECTION_CLEANUP_DONE`。
- 未出现旧价`24604`成交、额外第4手退出、Quarantine、孤儿仓位或Readiness阻断。

该交易之后策略继续运行：17:05 Short于18:10通过真实Market TP退出，Gross`+$478.50`；20:20 Short最终由`SESSION_FLATTEN`退出，Gross`+$108.00`。原故障导致的后续盈利路径阻断已解除。

## 生命周期审计

三日共9笔账户交易，全部为正常交易，且全部满足EntryQty=3、ExitQty=3：

| 事件 | 数量 |
|---|---:|
| `HISTORICAL_DORMANT_TP_SENT_V181` | 0 |
| `HISTORICAL_VIRTUAL_TP_ARMED_V220` | 9 |
| `HISTORICAL_OBSERVED_TP_EXIT_SEND_V220` | 1 |
| `HISTORICAL_OBSERVED_TP_EXIT_SENT_V220` | 1 |
| `PROTECTION_CLEANUP_DONE` | 9 |
| `PROTECTIVE_FILL_QUARANTINED_V177` | 0 |
| `EXIT_FILL_EXCEEDS_REMAINING_V220` | 0 |
| `ORPHAN_POSITION_BLOCKED` | 0 |
| `SKIP_LIVE_READINESS_BLOCKED` | 0 |
| `CANCEL_FAIL` | 0 |
| `ORDER_STATE_FAILED` | 0 |

数量不匹配为0，缺少Cleanup为0。

## 裁决与下一步

1. v2.20标记为“功能与生命周期基线通过”，不再修Historical Dormant TP。
2. 本轮不代表盈利晋级，不打盈利通过Tag。
3. 鉴于全量Replay成本高，不立即重复H1全量；恢复H1离线盈利研究时，以v2.20执行适配器作为新的Actual功能基线。
4. 新盈利候选必须先在已通过一致性Gate的离线模拟器中明显超过当前`WeeklyAccountOrLongAllLong500`，再进入Smoke和有限Replay。

## 证据归档

- 归档：`opf_v2.20_weekly-gate_virtual-target_regression-passed_3snapshots_20260726`
- 源证据：69份，79,917,557字节
- 归档证据：69份；加入Manifest后共70份文件
- 源—归档SHA256不一致：0
- `manifest_sha256.csv` SHA256：`33176D76C1CEB139D89BC9BCA065FEC699E28599D727FEDA3B410CF8AD6841FA`
- 归档校验后活动日志已清为0，旧根日志不存在。
