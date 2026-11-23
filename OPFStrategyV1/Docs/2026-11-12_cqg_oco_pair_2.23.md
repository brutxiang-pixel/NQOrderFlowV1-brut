# 2.23：CQG OCO成对提交根因修复

2026-11-12 08:33:00北京时间已部署，真实账户端到端待验收。最终DLL SHA256：`833DE12C8E492FD88657BD61B400CE311D05AF8F3E5D4408CA025896F323A5B9`。旧DLL备份：`bin/deployment-backups/before-2.23-20261112-083259/OPFStrategyV1.dll`。

## 根因与依据

官方经典版API说明：OpenOrderAsync是异步注册，须检查Order.Status()，不能仅因Task无异常就认定订单已接受；OnOrderChanged/OnNewMyTrade回报异步到达。

- https://docs.atas.net/en/chart-strategies/orders.html
- https://api5.atas.net/en/md_DataFeedsCore_2Docs_2en_20110__ManagingOrders.html
- https://docs.atas.net/api/ATAS.DataFeedsCore.Order.html

定向检查本机 `D:/Program Files (x86)/ATAS Platform/OFT.Cqg.dll` 的 `OFT.Cqg.CqgConnector.OnRegisterOrder`：OCOGroup为空时发送NewOrder；非空时以OCOGroup查暂存，若无同组单则仅缓存当前单，若已有则创建NewCompoundOrder，含两张CompoundOrderEntry后发送。IsSupportedServerOCO=true。这一连接器实现明确解释了夜间第一张SL无回报：代码先await SL再发送TP，第二张永远未到连接器，形成循环等待。并非已证明Rithmic行情/CQG账户架构不可用。2.22只处理超时，未处理这一根因。

## 修改

真实执行下，普通保护单、分批保护每腿均先启动SL与TP两个注册请求，再等待两者结果；每腿仍使用原有独立OCO身份及数量。CQG可据此将两张发送为原生复合订单。两腿不混组，不重复发送TP。任一注册失败整对失败，沿用2.22超时、阻止新单和紧急退出逻辑；极快成交时清理兄弟单。完整入场成交飞书仍在保护单等待前排队。

历史Replay虚拟目标不改为真实目标，保留原有顺序和逻辑。信号、数量、止损止盈价及活动配置未调整。当前实际代码已更新为2.23，但没有主动下真实验证单或发送外部测试消息。

## 验证

新增仿CQG测试：首单Task必须等次单提交才完成，成对提交在1秒内通过；单腿拒绝返回整对失败。LiveProtectionFailClosedTests及另外4组回归通过，Release 0警告0错误。ATAS退出后备份部署并验证SHA256一致。真实连接器成交及保护注册回报仍待启动验收。
