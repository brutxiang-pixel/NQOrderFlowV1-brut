# v2.14压缩市场转折带与开发连接器复核

日期：2026-07-25

## 结论

v2.14两日采集层通过，压缩市场转折带已正式接入离线连接器，原开发Gate全部通过。该结论只证明`2026-01-06,2026-01-28`开发集上的采集与连接器复刻能力，不代表盲测通过，也不允许恢复盈利研究。

## 采集层

- Snapshot：2
- 候选：538/538
- M5 Bar：18,853
- 市场转折记录：259,253
- 候选Entry/Resolve序号有效：100%
- Execute与Preflight序号匹配：20/20
- 转折序号重复、逆序、非法价格、非法Kind：全部0
- ENTRY_SEND / PROTECTION_CLEANUP_DONE：20/20
- DangerEvents：0
- `CollectionGatePassed=True`

v2.14与v2.13的Actual交易身份20/20一致。总Gross由`$1,007.00`变为`$945.50`，唯一退出角色变化与v2.14转折证据的首触方向一致；未发现采集字段改变候选选择或破坏订单生命周期的证据。Historical Replay本身仍可能在同一日期产生订单角色或成交价抖动，因此不把两版金额完全相等作为采集零影响条件。

## 连接器修正

连接器没有读取Actual退出角色回填答案，按以下可部署输入复刻：

1. 候选入场边界使用`EntryMarketSequence`，只读取`EntryMarketSequence < Sequence <= ResolveMarketSequence`的市场转折。
2. 已成交基线交易只使用真实`EntryPrice/StopPrice`作为成交后风险锚；未成交候选继续使用Entry Bid/Ask。Actual退出时间、退出角色和盈亏不参与预测。
3. 单一边界触达沿用Historical Replay适配器实际使用的M5可达性；Stop与Target同柱同时可达时，使用压缩转折顺序裁决先后。该混合语义同时覆盖“转折价未完整复现M5极值”和“M5无法判断双触顺序”两类问题。
4. TimeStop按含入场柱计数，12根策略在`EntryBar + 11`使用M5 Close退出。
5. ZoneBirth按`2 Base + 1 Runner`复刻Base 2.5R、Runner 4R、Base TP后下一根M5起Runner BE，并保留同柱Runner原Stop。
6. ActiveTrade占用在清理完成时间与候选时间相等时释放，符合本轮实际事件顺序。

## 开发Gate结果

| 指标 | 结果 | 门槛 |
|---|---:|---:|
| Actual / 模拟交易 | 20 / 20 | - |
| 身份匹配 | 20/20 = 100% | >=95% |
| 退出角色匹配 | 20/20 = 100% | >=95% |
| 最大逐日笔数误差 | 0 | <=1 |
| Actual Gross | $945.50 | - |
| 模拟 Gross | $939.50 | - |
| Gross偏差 | -$6.00 / 0.63% | <=$100且<=10% |
| 生命周期缺陷 | 0 | 0 |

`ConnectorGatePassed=True`。

## 边界说明

- 538个候选中有26个走到36根研究窗口末端；这不是20笔Actual交易的生命周期缺陷，但盲测报告仍须继续输出该数量。
- 本轮使用已成交基线的真实入场与止损作为成交后锚，只适用于基线复刻和校准；反事实未成交候选没有真实成交信息，必须继续使用Quote锚。
- 当前两日已经参与开发，不能承担盲测职责。

## 下一步

1. v2.14两日证据已归档为`opf_v2.14_compressed_market_turn_connector_dev_passed_2snapshots_20260725`：46个证据文件、46,933,231字节，Manifest SHA256=`CF6731EFEAD64B853358D6B2716AB82D3C1BD608C76C5ECF957754E41D3A61AD`。活动日志已清空；因本地安全策略拒绝直接删除，原始活动文件以可逆方式移动到归档内`_active_originals_after_verified_copy`，不参与Manifest口径。
2. 冻结当前连接器代码和开发参数，连接器SHA256=`51FC82ED18473A3AD2A16C758DF3C6510DDBD8CB2C34C9FF670177FF57733B82`。两日盲测预注册为：`2026-02-24,2026-06-17`；两日均未参与本轮连接器修复，结果出来前不得用于调参。
3. 盲测仍要求身份>=90%、逐日笔数误差<=1、Gross绝对偏差<=$150且相对偏差<=15%、生命周期缺陷0；同时继续报告退出角色匹配率。
4. 盲测通过前，盈利研究、路径优化、盈利Smoke和H1大样本Replay继续暂停。
