# v2.07四日订单生命周期定点Smoke复核（2026-07-25）

## 结论

- 四个Snapshot全部为`OPF_RESEARCH_2.07 / ACTUAL_EXEC_2.37`，覆盖`2026-01-27,2026-04-13,2026-05-01,2026-05-04`。
- 共47次`ENTRY_SEND`，对应44笔Normal、2笔Abnormal安全平仓和1笔Quarantine；47次持仓全部有`PROTECTION_CLEANUP_DONE`。四日AccountNet合计`+$151.70`，收益只作运行对账，不作策略晋级依据。
- `ORPHAN_POSITION_BLOCKED=0`、`DUPLICATE_EXIT_FILL=0`、保护退出回调超时=0。四日观察结果满足生命周期零越界。
- 但`EMERGENCY_FLATTEN_DUPLICATE_SUPPRESSED_V207=0`、`HISTORICAL_OBSERVED_TP_EXIT_DEFERRED_V207=0`、`HISTORICAL_OBSERVED_TP_EXIT_SUPPRESSED_STOP_TOUCH_V207=0`。本轮没有直接命中两个新增竞争抑制分支，因此只能判定“定点行为回归通过、异步竞争分支覆盖不足”，不能表述为并发压力验证完成。

## 四日结果

| 日期 | Normal | Abnormal | Quarantine | AccountNet | 定点结论 |
|---|---:|---:|---:|---:|---|
| 2026-01-27 | 12 | 1 | 0 | -$145.20 | 风险超限交易只发一张2手Flatten，账户回平后继续交易；无孤儿仓位 |
| 2026-04-13 | 12 | 0 | 0 | +$129.70 | 6个唯一TradeID完成v2.06目标Send/Sent/Filled链；无重复退出 |
| 2026-05-01 | 12 | 1 | 0 | +$237.80 | 原TradeID正常完成Base TP＋Runner TP；无SL/TP竞争和额外修复单 |
| 2026-05-04 | 8 | 0 | 1 | -$70.60 | 固定旧价保护成交继续写入Quarantine，仅计`-$2.40`手续费 |

## 两个原缺陷场景对照

### 2026-01-27 `20260127-0430-Long-1170`

- v2.06归档：`ENTRY_FILLED_RISK_EXCEEDED=2`、`EMERGENCY_FLATTEN_SEND=2`，随后产生重复退出及反向仓位。
- v2.07本轮：`ENTRY_FILLED_RISK_EXCEEDED=1`、`EMERGENCY_FLATTEN_SEND=1`、`DUPLICATE_EXIT_FILL=0`、`ORPHAN_POSITION_BLOCKED=0`。
- 结果满足合同，但第二次提交请求本轮没有发生，所以强幂等锁没有写出其专用抑制事件。

### 2026-05-01 `20260501-0640-Short-1207`

- v2.06归档入场均价`27604.50`，Runner在06:55出现SL Pending后又发送TP Market，造成重复退出。
- v2.07本轮入场均价`27603.50`，重定价后的Base/Runner目标分别为`27596.00/27591.50`，Runner TP已在06:50完成；06:55竞争窗口没有出现。
- 因Replay实际成交差1点导致路径时序变化，本轮证明该日没有回归，但没有直接验证“SL Pending时延后TP”或“同M5 Stop优先”分支。

## 判定与下一步

1. 四日功能回归可接受，v2.07未发现新的订单生命周期缺陷。
2. 不重新跑184日；v2.06的`AccountNet +$2,677.90 / Net PF 1.051`经济结论保持不变。
3. 不凭本轮直接打实盘通过Tag。若要把两条竞态修复升级为强证据，应采用可控的生命周期顺序测试；盲目重复Replay可能继续因成交时序漂移而无法命中新分支。

