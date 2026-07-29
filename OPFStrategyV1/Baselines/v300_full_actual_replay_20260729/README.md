# V3.0 全量 Actual Replay 恢复基线

冻结日期：2026-07-29  
Git Tag：`opf-v3.0-full-replay-baseline-20260729`

## 固定对象

| 对象 | SHA256 |
|---|---|
| `OPFStrategyV1.dll`（已部署 ATAS DLL） | `92440EF4DE6A2360AF463A50925FE9F44EC4D29E7F1677ADBD8A2ECADCF00948` |
| `OPFStrategyV1_actual_execution.deployed.json` | `963A93E6F6AD7C624AB1D1995AABA78F5B2A6DBB5EDE7156F2194BAA627EF1C1` |
| `OPFStrategyV1_v300_full_replay_2026_01_to_07_24.json` | `963A93E6F6AD7C624AB1D1995AABA78F5B2A6DBB5EDE7156F2194BAA627EF1C1` |

归档 Replay 证据位于：

`%APPDATA%\ATAS\StrategyLogs\OPFStrategyV1_Archive\opf_v300_full_actual_replay_202601_to_20260728_20260729`

其中有 2,836 个运行文件、319,984,698 bytes，`SHA256SUMS.csv` 的 SHA256 为：

`6A2CD4BD352C897AF7D2485A0DA7FFCFE1095ABED0F6438C1E4F8F0F2FF34E84`

## 恢复步骤

1. 检出上述 Git Tag。
2. 将本目录的 `OPFStrategyV1.dll` 复制至 `%APPDATA%\ATAS\Strategies\OPFStrategyV1.dll`。
3. 将 `OPFStrategyV1_actual_execution.deployed.json` 复制至 `%APPDATA%\ATAS\StrategyConfigs\OPFStrategyV1_actual_execution.json`。
4. 分别校验 DLL 与配置 SHA256 必须等于本文件列出的值。
5. 完整重启 ATAS，并确认 HUD / ConfigSnapshot 为 `ACTUAL_EXEC_2.48`、`V300_LIVE_GATE_FULL_REPLAY_2026_01_TO_07_24`、`ActualExecution`、3 手、15 笔、-$250 日损、-$500 周 Long 门。

该恢复包不包含活动日志；日志证据使用上方独立归档恢复或只读审计。

