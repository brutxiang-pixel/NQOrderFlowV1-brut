# v2.24 二十日离线候选流差异复核

日期：2026-07-27  
目的：解释 v2.24 重组 Actual 相对冻结 v2.22/v2.23 Rich 候选多出的交易，判断是否由新门禁或单 ActiveTrade 占用造成。

## 比较口径

- 冻结离线候选：H1 使用 `LongOCRegimeMin=8;Breakaway=Both;ShortOCRegime2=False` 的 134 笔；7 月使用预注册 Rich Stress Candidate 的 7 笔；合计 141 笔/Gross `+$5,005.01`。
- Actual：v2.23 18 日证据加 v2.24 两日替换，159 笔/Gross `+$5,839.50`。
- 身份键：`SignalID + ResearchPath + Lane`。交易日按所属 Snapshot 归属，避免 23:00 跨 Globex 日的错误分组。

## 身份分解

| 项目 | 笔数 | Gross |
|---|---:|---:|
| 冻结候选 | 141 | +$5,005.01 |
| Actual | 159 | +$5,839.50 |
| 共同身份 | 131 | Actual +$4,683.00；离线 +$5,235.51 |
| Actual 独有 | 28 | +$1,156.50 |
| 离线独有 | 10 | -$230.50 |

因此 Actual 相对离线的 `+$834.49` 不由单一来源构成：共同身份的 Actual 退出/成交少 `$552.51`，但 Actual 独有交易增加 `$1,156.50`，同时没有发生的离线独有交易移除 `$230.50`。

身份覆盖率：冻结候选被 Actual 复现 `131/141=92.9%`；Actual 被冻结候选覆盖 `131/159=82.4%`。这满足方向级校验，不满足精确金额或精确候选流校验。

## 原因判断

1. 不是 Rich 门禁的系统性误伤或放宽。20 日只有 5 次 Long `RegimeBars` 拦截和 2 次 Breakaway Rich 拦截；两类门禁均仍只命中预注册目标路径。
2. 不是单 ActiveTrade/双槽占用可单独解释。`2026-04-10` 的冻结候选首笔为前一 Globex 日 22:35 Short，随后是 02:10 Short；Actual 在 00:40 已出现独有 Long 和 Secondary，早于首个共同候选，故原始候选流已经分叉。
3. 最大 Actual 独有贡献为 4 笔 Primary Long `AlmostConfirmed`，Gross `+$936.50`；其次为 5 笔 Primary Short `ObservationConfirm`，Gross `+$209.00`。这两类都不是 v2.23 新 Rich 门禁直接新增的路径。
4. 离线独有的 10 笔合计 Gross `-$230.50`，主要为 Long `ObservationConfirm` 与 Long WideStop；其未发生有利于本轮 Actual，但不能被归功于新门禁。

## 裁决

模拟器在该候选上仍只能做方向筛选，不能承诺交易数或金额；继续为消除这类跨 Replay 原始候选流差异而改策略或改模拟器，预期收益低且会重回已知的校准兔子洞。

v2.24 的订单能力、Rich 门禁边界和 20 日经济方向已经足以让 Replay 成为下一次 H1 的最终裁决器。H1 结果必须独立解释，不得用本 20 日的 `$5,267.10` 简单线性承诺。
