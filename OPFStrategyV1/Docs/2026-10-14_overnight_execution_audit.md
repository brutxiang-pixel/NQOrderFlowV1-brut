# 2026-10-14 夜间订单与保护单审计

审计来源：ATAS/StrategyLogs/OPFStrategyV1，实例 123710-43ecc（MNQ）和 144135-d146（NQ）。日志时间为 UTC；北京时间加 8 小时。

## 已完成订单

| UTC 入场 | 产品 | 路径 | MFE/R | 出场 | 毛盈亏 USD |
|---|---|---|---:|---|---:|
| 10-13 14:40 | MNQ | ESResponseAlignedBreakout Long | 0.9220 | SL | -68.50 |
| 10-13 16:25 | NQ | ObservationConfirm Long | 1.2435 | SL | -585.00 |
| 10-13 22:20 | NQ | ObservationConfirm Long | 0.7891 | SL | -630.00 |
| 10-13 22:25 | MNQ | ESResponseAlignedBreakout Long | 1.0550 | SL | -52.00 |
| 10-13 23:20 | MNQ | ESResponseAlignedBreakout Short | 0.8440 | SL | -54.50 |
| 10-14 00:50 | MNQ | ObservationConfirm Long S2 | 0.4085 | SL | -37.00 |

已完成样本合计：MNQ -212 USD，NQ -1215 USD，未扣手续费。另有 ENTRY_SEND，不能将未出现在 execution_trades 的持仓算成已完成。MFE 为记录的保护观察范围，不能单凭 MFE 重建逐笔先后顺序或保证保本成交。

## 关键问题

- 初始 SL/TP 有发送记录，样本未出现成功 BE 修改事件。OC Long 的原策略明确无 1R BE；ES 新路径受到 legacy policy 限制，未启用通用 BE。
- NQ 16:25 单在 execution_trades 为 1 手，但 live_account_pnl 为 21 手，记成 -12310.20 USD 净亏；重复 ENTRY 回调造成数量累加，是日损异常的明确线索。
- RestoreLiveDailyNetPnl 扫描目录内全部账户盈亏文件，旧记录未区分账户/Replay，当前旧日损不能直接当作真实账户损益。此次保留原始日志，未修改历史损益。
- NQ/MNQ 不保证同步出单：独立候选、结构价、持仓槽位和日损状态不同；NQ 1 手每点 $20，MNQ 每点 $2。当前多次拦截为 LiveDailyLoss。
- S2 的 SL 成交后记录了 TP CANCEL_SENT、TP Done 和 CLEANUP_DONE。用户明确说明 SL/TP 是人工取消，因此这些记录不能证明策略自动撤单成功。缓存对象核对和一次性 cleanup latch 存在遗漏活动保护单的漏洞。

## 本次修改

1. 飞书 OPEN/CLOSE 加产品根代码，保留北京时间和同笔标识。
2. 每笔交易按 MyTrade.Id + role 去重，保留不同成交 ID 的分批成交；缺 ID 不按价格/数量猜测合并。
3. OC Long 保留原目标，非原小风险静态退出分支新增 1R 移动止损至入场价；四条 ES 路径明确 1.5R TP、1R BE。BE 不含手续费，也不是持续跟踪止损；尚需模拟盘验证修改确认。
4. 保护清理核对 Connector.Orders 中同账户、同产品、同 TradeID 的活动订单；清理完成后若收到活动订单回调重新清理，并每秒随最新图表计算复查已完成交易。不会清理人工订单或别笔订单。
5. 其它六条路径未盲目放宽：FR WideStop 主要是结构 RR 不足、过期快照；RetestFailed 主要是质量 48/52 <56、日损；Gamma 有 WallQuotesIncomplete；Qualified/Wall/Expansion 本窗口未进入 execution_decisions，不能判为代码失效。

## 验证与限制

飞书离线格式及成交 ID 去重测试通过；OptionsProxyGateTests 通过；策略 Release 构建 0 警告、0 错误。旧 LiveProtectionFailClosedTests 存在 ConfigSnapshot.Create 五参调用失配及旧快照脱敏预期问题，不能宣称整套保护回归通过。

部署：AppData/Roaming/ATAS/Strategies/OPFStrategyV1.dll。没有清理日志、没有发测试单、没有发飞书测试消息。磁盘更新不替换运行中程序集；重新加载后在模拟账户验证：平仓后工作保护单为零、重复成交不增加数量、BE 修改有 APPLIED 确认。历史受污染日损仍需单独核对恢复，不能直接删除日志绕过封控。
