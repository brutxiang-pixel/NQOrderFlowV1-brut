# 冰山剥头皮保本修复与 NQ 零信号诊断 preview.22 部署记录

部署时间：2026-11-25 15:01:12（北京时间）。版本：3.0.0-preview.22（文件版本 3.0.0.22）。ATAS X 启动快照尚待重启确认。

## NQ 面板与零信号

用户提供的 NQ 参数截图显示：实际开单已启用、订单数量为 1、冰山吸收剥头皮已启用。源码对 IcebergAbsorptionScalp 有单独的执行路径资格判断，因此通用路径白名单不包含它并不能解释零信号。

同日 06:52 UTC 的运行漏斗：NQZ6 有 419 个推断补量段、0 个信号、0 个提交；价差拦截 535 次，另有等待逐笔确认 20 次。实时报价快照中 NQ 买卖价差为 1.25 点，即 5 ticks；MNQ 为 0.75 点，即 3 ticks。源码要求价差不超过 2 ticks。NQ 原生 MBO 初始化日志中的 events=0 只代表初始化时的计数；当日原生归档随后记录到 NQZ6 刷新事件，不能把初始化状态当成整段运行无 MBO。

同一时段 MNQZ6 累计 321 个信号、316 个入场提交、249 个成交，同时也有 5,811 次宽价差拦截。品种的信号/成交差异来自逐时行情与候选确认过程，不能用手数或面板开关解释。为维持成本过滤，本候选没有放宽 2-tick 上限；新增 quoteSpreadTicks 漏斗分布日志，供重启后的逐品种核验。

## 已修复的 MNQ 保本门控

preview.21 的保本判断先查询路径专属退出策略，再以 UseLegacyV174StrategyPolicyV204 决定是否启用旧式保护。剥头皮有独立 4-tick、手续费保护触发值，但没有独立退出策略记录，因此 legacy 分支把它整体关掉；当日正常剥头皮成交中没有保护性保本移动事件。

preview.22 让 IcebergAbsorptionScalp 使用已有保本触发，同时保持普通路径的 legacy 行为；如果将来给该路径显式配置 0 触发，仍由显式退出配置关闭保本。

当日记录包含 160 笔正常已平剥头皮单：47 笔 TP、113 笔 SL，毛额合计 -$138.50；其中 28 笔 SL 曾有至少 1.0 点（4 ticks）的最大有利波动。该数值是修复前的筛查基线，不等于这些单子在真实保护单/滑点下必然能保本。

## 部署、验证与后续

构建 DLL：OPFStrategyV1/bin/ATASX/Release/net10.0-windows/OPFStrategyV1.dll。
部署 DLL：%APPDATA%/ATAS X/Strategies/OPFStrategyV1.dll。

- 构建与部署 SHA-256：01E15F63CE7E4B1B0B02A42F27C69B3D30EF8CE355DB0D13A0B53CE1B220B1AC。
- 覆盖前 preview.21 DLL 备份：OPFStrategyV1/bin/deployment-backups/before-preview.22-20261125-150111/OPFStrategyV1.dll；SHA-256：45FAEF6F6A6A07C56477D6BF9033C3BF44565F8294860FD6B764AE9872EAABBF。
- IntradayEntryTests 的 277 项检查通过；LiveProtectionFailClosedTests 通过；ATAS X Release 构建 0 警告、0 错误；git diff --check 通过。
- 请重启 ATAS X 后确认新启动快照为 preview.22，并观察 ICEBERG_SCALP_FUNNEL 的 quoteSpreadTicks、保本修改事件和交易结果。部署仅表示 DLL 已复制并校验哈希，不代表运行或绩效验收。
- 本轮未用回测结果推断胜率；新样本需按 NQ、MNQ、方向、原生/推断补量来源和价差档位分层统计。
