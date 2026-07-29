# v2.30 阶段 2 分层 24 日 Footprint 采集计划

已归档首轮通过证据；本计划共 24 日，其中 `2026-01-16,2026-07-07` 已完成并保留，其余 22 日待运行。

样本构成：2026 H1 共 19 日，按月覆盖高/中/低历史收益日；7 月共 5 日，作为压力样本。日期用于**数据可得性与特征稳健性**，不是按历史盈亏拟合阈值；任何 G2 门禁仍需在完成连接、按月留一验证后才可研究。

运行设置固定：

- `Footprint Data Collection Only=true`
- `Rich Bar Data Collection Only=false`
- `Microstructure Audit Collection Only=false`
- `Decision Tape Calibration Collection=false`
- 每日 06:00 至次日 05:00；启动策略并确认 HUD `ActualExec: OFF dataOnly=True` 后再播放。

通过后只做连接/缺失/时间边界审计；不进入 Smoke、不修改 DLL、不把 Footprint 特征写入交易决策。
