# 冰山吸收策略 preview.16 部署记录

部署时间：2026-11-24 13:25:54 +08:00。部署版本：`3.0.0-preview.16`（文件版本 `3.0.0.16`）。

构建 DLL：`OPFStrategyV1/bin/ATASX/Release/net10.0-windows/OPFStrategyV1.dll`。
部署 DLL：`C:\Users\Administrator\AppData\Roaming\ATAS X\Strategies\OPFStrategyV1.dll`。
SHA-256：`8022399ACF326E9D0F431277DCF7F9F436CF6E188125D418AF8FD1846AB1ECC2`。部署文件与构建文件哈希一致，大小均为 4,119,040 字节。

旧 DLL 已备份至 `OPFStrategyV1/bin/deployment-backups/before-preview.16-20261124-132553/OPFStrategyV1.dll`，SHA-256：`146676F4256F7CBFC184C3FF85A6418E8623EB924A273297C6781765B4A9136F`。部署时 ATAS X 主程序未运行；没有修改活动配置。

验证：`IntradayEntryTests` 通过；ATAS X Release 构建成功，0 警告、0 错误。部署完成后尚未启动 ATAS X，实际运行和成交结果需在用户重启后观察。历史 8 笔交易样本不足以确认策略胜率。
