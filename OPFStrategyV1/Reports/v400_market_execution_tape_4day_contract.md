# V4 深层 Market Execution Tape：四日技术采集合同

日期固定为：`2026-01-16, 2026-02-11, 2026-04-13, 2026-07-07`。它们覆盖冬夏时制、不同月度、不同市场状态，并与已采 Zone Ledger/冻结基线证据可交叉连接。

唯一运行档案将为 `V400_MARKET_EXECUTION_TAPE_4DAY_DATA_ONLY`；启动强制 `actualOrders=false`。采集全市场逐笔 `Sequence/TickTime/M5Bar/Price/Volume/Direction`，并在停止时批量写盘；不采 DOM、不生成订单。

验收：四日各一有效 Snapshot；Config 身份一致；逐笔 Sequence 无重复且单调；逐笔量与 M5 Volume 对账；订单、账户成交、`ENTRY_SEND` 均为零；全局 Tick Tape、Candidate Scenario Tape、ConfigSnapshot 均存在。候选带必须记录每个候选/阻塞候选的稳定键、决策报价、计划风险、组合状态、阻塞原因及当时 Zone Context。通过后才允许做冻结 Actual 锚的离线校准；未通过只修链路，不扩大日期。
