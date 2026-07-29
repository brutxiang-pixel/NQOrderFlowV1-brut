# v2.21七月Replay与H1离线续研复核

日期：2026-07-26

## 七月Replay裁决

- 17个Snapshot、391个文件完整，全部为`OPF_RESEARCH_2.21 / ACTUAL_EXEC_2.46 / 3手 / 15笔 / 日损250 / 周Long门500`。
- 账户65笔：63 Normal、1 Quarantine、1 Abnormal；Gross`+$795.50`、AccountNet`+$561.50`、PF`1.2187`、日级MaxDD`$1,104.20`、7/17盈利日。
- 周度：`+$265.50 / -$500.00 / +$371.20 / +$424.80`，3/4周盈利。
- `SKIP_PRIMARY_ZONE_BIRTH_QUALITY_V221=7`，全部为Primary语义、`ZoneBirthResearch`、score 38或41；Secondary误伤0，与离线预计7次完全一致。
- Breakaway Short Actual Gross`+$788.50`，离线`+$790.50`，核心路径仅差`-$2.00`。
- 1笔不可达SL被正确Quarantine；1笔成交后风险23.08点超过22点门，正确隔离并强平。65笔Entry/Exit数量均为3，Cleanup Pending/Done 65/65；Dormant TP、孤儿仓位、过量退出、Globex未确认、Readiness与订单状态失败均为0。
- 裁决：v2.21七月Replay功能与收益方向通过；金额不作为逐美元复刻承诺。

## 冻结离线基线

v2.21 H1：878笔、Gross`+$26,435.12`、Net`+$23,274.32`、PF`1.7062`、22/27盈利周、最差周`-$665.10`、MaxDD`$2,121.10`，六个月全正。所有续研脚本均先精确复刻该组数值，否则停止。

## 本轮四层研究

### 1. Primary路径×方向×质量/风险LOMO

- 只测Primary中至少20笔或当前净亏组，Breakaway保持不动。
- 最好规则为Short UnknownRegimeZoneTouch最大风险10点：Net仅增加`+$404.55`，只改善3个月。
- 唯一改善4个月的规则改变185个身份、减少71笔，却只增加Net`+$165.57`。
- 无双变量组合满足跨月、PF、最差周、MaxDD与7月压力约束；不进入Smoke。

### 2. Secondary边际风险

- 扫描第二槽周损/周回撤锁存、日损、每日/每周次数上限。
- 最好为第二槽周回撤`$300`：Net增加`+$284.70`，只影响1个月和4个身份。
- 其他多数方案减利；第二槽H1总体正期望，不能粗暴限额。

### 3. 全账户周风险

- 扫描周净损和周峰值回撤`$300-$1,200`。
- 周损`$500`把最差周从`-$665.10`改善至`-$609.90`，但H1 Net减少`-$418.25`，7月Net减少`-$452.17`。
- 更紧门显著减利；更宽门不触发。无晋级项。

### 4. 盈利后加仓诊断

- 假设父单先达到`+1R`后加1手，新增手止损放父单入场价，与父单同退出；初始入场风险不增加。
- 最好为高质量Breakaway Short：H1 Net增加`+$469.73`，但只改善2个月，7月恶化`-$694.40`且MaxDD增加。
- 该方向跨期失败，不实现Actual。

## 新路径诊断

- H1候选Tape中BreakawayFvg Short有76行，静态PredGross`+$8,171.00`；BreakawayRetest只有2行Long、0行Short。
- 代码没有禁止Short：每个Breakaway都会创建`PendingBreakawayRetest`，但窗口固定为6根M5；候选会因区块失效、确认质量或窗口到期被删除。
- 因此缺少Short Retest不是配置禁用，更可能是6-Bar窗口与当前确认/质量组合导致候选漏斗坍缩。

## 裁决与下一步

1. 本轮没有任何新规则进入Smoke；不改DLL、不清日志、不安排全量Replay。
2. 现有14路径上的微过滤、第二槽限额、全局周停和盈利加仓已无足够稳健增益，继续扩大网格属于过拟合。
3. 下一研究线改为Research-only BreakawayRetest并行影子：现有6-Bar规则保持不变，同时记录12/18-Bar窗口的触碰、确认、质量拒绝和完整虚拟生命周期。
4. 第一阶段只跑预注册24日分层采集，一次同时回答窗口6/12/18；只有Short Retest至少20笔、跨双月块直接期望为正且完整组合增益明显，才考虑H1采集或Actual实现。

## 证据

- `OPFStrategyV1/Reports/v221_h1_entry_lomo/`
- `OPFStrategyV1/Reports/v221_h1_secondary_risk/`
- `OPFStrategyV1/Reports/v221_h1_weekly_account_risk/`
- `OPFStrategyV1/Reports/v221_h1_profit_addon/`
- `OPFStrategyV1/Scripts/Analyze-OPFV221H1EntryLomo.py`
- `OPFStrategyV1/Scripts/Analyze-OPFV221H1SecondaryRisk.py`
- `OPFStrategyV1/Scripts/Analyze-OPFV221H1WeeklyAccountRisk.py`
- `OPFStrategyV1/Scripts/Analyze-OPFV221H1ProfitAddOn.py`

