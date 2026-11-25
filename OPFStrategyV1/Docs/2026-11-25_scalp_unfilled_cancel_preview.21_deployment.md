# 冰山剥头皮未成交入场委托撤销 preview.21 部署记录

部署时间：2026-11-25 12:11:11（北京时间）。版本：`3.0.0-preview.21`（文件版本 `3.0.0.21`）。

针对 `IcebergAbsorptionScalp` 入场限价单在连接器侧仍保持 Active、未成交委托没有按预期自动结束的问题，Active 且未成交时等待 250ms 后主动撤销余量。部分成交继续走既有保护处理，两根 K 线的兜底清理保留。日志新增 `SCALP_IOC_REMAINDER_CANCEL_REQUESTED`，平台回报仍需重启后观察。

构建 DLL：`OPFStrategyV1/bin/ATASX/Release/net10.0-windows/OPFStrategyV1.dll`。
部署 DLL：`C:/Users/Administrator/AppData/Roaming/ATAS X/Strategies/OPFStrategyV1.dll`。
SHA-256：`45FAEF6F6A6A07C56477D6BF9033C3BF44565F8294860FD6B764AE9872EAABBF`；构建产物和部署 DLL 哈希一致，文件大小均为 4,120,064 字节。

覆盖前 preview.20 DLL 备份：`OPFStrategyV1/bin/deployment-backups/before-preview.21-20261125-121111/OPFStrategyV1.dll`，SHA-256：`A2BC340276238BB5250E925CB8E8BEC99F78958C1119C6983DDD353B029BD8DB`。源码快照：`C:/Users/Administrator/Documents/NQOrderFlowV1-Backups/2026-11-25T04-08-07-546Z-e77aa0ed/`；其中额外归档的未跟踪二进制与报告共 19 个文件，归档 SHA-256：`F49D615324EF1BB1D28328C068CE1167F25DEEE92457C4BDD32931F10A471572`。

验证：`IntradayEntryTests`、`OptionsProxyGateTests`、`LiveProtectionFailClosedTests` 全部通过；ATAS X Release 构建 0 警告、0 错误；`git diff --check` 通过。

部署目录已更新；重启 ATAS X 后需核对运行版本为 preview.21，并观察 `SCALP_IOC_REMAINDER_CANCEL_REQUESTED`、`EXEC_CANCEL_SENT` 及后续撤单回报。此记录不代表实盘下单或运行验收已经完成。
