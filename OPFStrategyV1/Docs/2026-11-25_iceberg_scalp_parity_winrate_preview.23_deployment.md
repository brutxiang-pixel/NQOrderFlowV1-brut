# NQ/MNQ 冰山剥头皮门槛与 MNQ 风险优化 preview.23

部署时间：2026-11-25 20:07:10（北京时间）。部署到 ATAS X Strategies；程序未重启，运行验收待用户重启后进行。

## 运行证据与判断

核对 2026-11-25 15:05–16:53（北京时间）的 preview.22 日志：NQ 实际下单已启用、连接已恢复、MBO 订阅正常；21 根五分钟K线内 NQ 有 451 个推断补量段、18 个确认段，但 0 个信号和 0 笔成交。价差分布共 698 次：1–2 tick 只有 41 次，3–5 tick 有 403 次；原 2-tick 规则因此是主要执行门槛之一。

同段 MNQ 有 7,314 个推断补量段和 63 笔成交，价差观察 9,424 次，其中 1–2 tick 为 1,783 次，3 tick 及以上为 7,641 次。NQ 与 MNQ 的候选段数量也相差约 16 倍，说明行情/盘口输入的候选生成差异仍存在；不能将两者的成交差距全归因于一个门禁开关，也不能据此承诺成交数相同。preview.23 的目标是解除 NQ 主要价差拦截，同时保留 MNQ 成本门槛，并在重启后的漏斗日志中记录 quoteSpreadLimitTicks。

## MNQ 成交基线与规则

preview.22 的已平 MNQ 冰山剥头皮样本约 40 笔：11 胜、26 负、3 平，净额约 -$75。原始风险不超过 6 tick 的 21 笔为 8 胜、11 负、2 平，净额 -$16.50；超过 6 tick 的 19 笔为 3 胜、15 负、1 平，净额 -$58.50。样本小且为观察性分组，只支持限制宽止损入场的尝试，不证明新规则会提高胜率。

preview.23 的变更：

- NQ 价差上限 5 tick；MNQ 和 ES/MES/GC/MGC 保持 2 tick。费用门禁按实际价差或 2 tick（取较大值）加往返手续费计算。
- MNQ 冰山结构止损最大 6 tick（1.5 点）；NQ 及其他品种保持 8 tick。
- NQ/MNQ 恢复确认要求至少 3 笔同向主动成交，净主动量至少为观察成交量的 30%，最后两笔仍须同向且在冰山价位外。其他品种沿用原来的至少 2 笔、20% 净主动量。
- 固定目标 12 tick、保本触发 4 tick 与面板手数控制保持不变。

用于规则选择的研究：Cont、Kukanov、Stoikov，《The Price Impact of Order Book Events》（arXiv:1011.6402）；Gould、Bonart，《Queue Imbalance as a One-Tick-Ahead Price Predictor》（arXiv:1512.03492）。研究说明订单流/队列失衡在短周期可能提供预测信息，但不提供本策略在 MNQ/NQ 的胜率验证。

## 构建与部署

IntradayEntryTests：279 项通过；LiveProtectionFailClosedTests：通过；ATAS X Release：0 警告、0 错误。

新 DLL 文件版本：3.0.0.23；SHA-256：9266F483FC86AF78272FCEE89E708591223BA65791A1A66AEFAC51DD41FE8BDE。
部署路径：%APPDATA%/ATAS X/Strategies/OPFStrategyV1.dll。
覆盖前 preview.22 备份：OPFStrategyV1/bin/deployment-backups/before-preview.23-20261125-200710/OPFStrategyV1.dll；SHA-256：01E15F63CE7E4B1B0B02A42F27C69B3D30EF8CE355DB0D13A0B53CE1B220B1AC。

重启后需确认 NQ/MNQ 实例版本为 preview.23，查看两者 quoteSpreadLimitTicks 与 spread histogram，随后按 IcebergAbsorptionScalp 路径统计信号、提交、成交、退出类型、胜率和净额。当前 DLL 部署成功不等于交易规则或胜率已通过运行验收。
