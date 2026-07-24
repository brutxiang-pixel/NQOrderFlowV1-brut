# v2.05旧策略语义三日补跑复核（2026-07-24）

## 结论

- 三个预注册Snapshot全部完成：`2025-12-12,2026-03-16,2026-05-04`。
- 版本一致为`OPF_RESEARCH_2.05 / ACTUAL_EXEC_2.35`；28个Execute全部带`LegacyV174PolicyV204`，形成27笔正常ExecutionTrade和1笔Quarantine。
- 普通`ObservationConfirm Long`执行6次；非Split `PROTECT_BE1R_*_V186`事件为0；路径TargetR恢复旧规则；ZoneBirth Split Runner保持原生命周期。
- 订单注册、Dormant TP激活、修改、回调超时、重复退出、决策—交易—ActualVerified连接等生命周期缺陷均为0。功能Smoke通过，盈利尚未晋级。

## 三日统计

| 日期 | 正常笔数 | Gross | 正常Net | AccountNet |
|---|---:|---:|---:|---:|
| 2025-12-12 | 10 | +$125.00 | +$101.00 | +$101.00 |
| 2026-03-16 | 10 | -$240.50 | -$264.50 | -$264.50 |
| 2026-05-04 | 7 | +$29.00 | +$12.20 | +$9.80 |
| 合计 | 27 | -$86.50 | -$151.30 | -$153.70 |

## `2026-05-04`安全复核

- 固定旧价`27979.00`仍出现在8次成交回调中。
- 3笔普通TP/SL旧价被正确归一化；1笔不可达SL被`PROTECTIVE_FILL_QUARANTINED_V177`隔离并仅扣`$2.40`手续费。
- 本轮没有Abnormal Safety Flatten，因此`HISTORICAL_ABNORMAL_SAFETY_FLATTEN_NORMALIZED_V205`没有动态触发。
- 上轮虚假异常平仓Net`+$227.60`没有再次进入AccountNet；当日AccountNet为`+$9.80`。
- 该日可作为旧价归一化、Quarantine和账户统计不泄漏的安全证据，但不得作为盈利证据。没有必要为了强行触发V205分支继续重复回放该日。

## 晋级状态

- v2.05：旧策略语义功能Smoke通过。
- v2.05：盈利未晋级，不打通过Tag。
- 下一步先做v1.74、v1.94与v2.05同日配对；只有配对方向支持旧策略移植，才安排20日中样本Actual Replay。

## 归档

- 名称：`opf_v2.05_legacy_policy_function_smoke_passed_3snapshots_20260724`
- 内容：3个Snapshot、61个活动日志文件及本报告，共62个清单文件、6,960,538字节。
- 源—归档逐文件SHA256不匹配：0。
- `manifest_sha256.csv` SHA256：`5FABC21E158B065C1FF81C0D9D42B86A853C1BFA5C59A9ED72D01C13DF3EE0FE`。
