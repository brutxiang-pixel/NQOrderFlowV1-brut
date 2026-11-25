# MNQ 重复残余平仓竞态修复 preview.18 部署记录

部署时间：2026-11-25 08:06（北京时间）。版本：`3.0.0-preview.18`（文件版本 `3.0.0.18`）。

本次修复针对 MNQ 异常平仓竞态：第一张紧急市价平仓单已完全成交、但账户持仓回调尚未刷新时，残余检查不得再提交第二张同方向市价单。只有原平仓单未完全成交、账户仍有同一执行的剩余仓位时，才允许提交残余平仓。冰山策略的订单数量/频率门禁保持不变，继续保留样本收集。

构建 DLL：`OPFStrategyV1/bin/ATASX/Release/net10.0-windows/OPFStrategyV1.dll`。
部署 DLL：`C:\Users\Administrator\AppData\Roaming\ATAS X\Strategies\OPFStrategyV1.dll`。
SHA-256：`5EF759B50F869910E2BDFDAD67C46C1DEB563E9EFF65939C7D57A0B20E99E3BE`；构建 DLL 与部署 DLL 哈希一致，大小均为 4,116,480 字节。

覆盖前 DLL 备份：`OPFStrategyV1/bin/deployment-backups/before-preview.18-mnq-fix-20261125-080625/OPFStrategyV1.dll`，SHA-256：`A013A99046FC86348533154A9F541A3DFA5F711FF4FE8F85E95D290039B6A685`。

验证：`LiveProtectionFailClosedTests` 通过，包含新增的“已完成平仓单 + 持仓状态滞后”回归场景；ATAS X Release 构建成功，0 警告、0 错误。活动配置未修改。部署时 ATAS X 主程序仍在运行，用户重启后加载 preview.18。

部署前核对：用户已手动平掉此前异常 MNQ 空仓；本机 ATAS 日志最后记录 MNQ 持仓为 `0`。
