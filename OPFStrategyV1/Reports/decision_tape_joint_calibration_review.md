# Decision Tape 联合校准复核（2026-07-25）

## 结论

同一 Decision Tape 引擎内的 `V206BaselineReplay` 与 `V208BugCompatible` 两个控制配置均已精确复刻标准答案。联合 Gate 通过，可以进入 `CorrectedResearch` H1离线盈利研究；旧v2.08缺陷事件只允许在控制配置中使用，禁止进入研究配置。

## v2.06 H1 控制臂

- Decision Tape 动态候选：1,930；Shadow Join：100%。
- Execute：1,075；身份匹配：1,075/1,075，缺失 0，模拟新增 0。
- Normal：1,041；正常 Gross：`+$8,198.50`；正常 AccountNet：`+$5,700.10`。
- 账户生命周期：1,060；账户总 Gross：`+$8,161.00`；账户总 AccountNet：`+$5,617.00`。
- 非正常 AccountNet：`-$83.10`；15 条入场提交中止按零成交、零占用、零损益处理。

## v2.08 五日控制臂

- Decision Tape 动态候选：95；唯一候选 87；8 条为 Deferred 重新门禁产生的合法重复决策事件；Shadow Join：100%。
- Execute：49；身份匹配：49/49，缺失 0，模拟新增 0。
- Normal：47；正常 Gross：`+$1,424.50`；正常 AccountNet：`+$1,255.30`。
- 账户总 Gross：`+$1,415.00`；账户总 AccountNet：`+$1,238.60`；非正常 AccountNet：`-$16.70`。
- `2026-02-11 02:45` 的 Deferred 异步时间倒置作为显式 Recorded Active 状态观察兼容，单独计 1 条，不扩散到政策模拟。

### V208BugCompatible 严格控制通过

- 控制配置读取`DEFERRED_CAPTURED_V208 / DEFERRED_REPLACED_V208 / DEFERRED_ACTIVATED_V208`，只用于复刻旧版单候选、固定寿命与激活顺序；它不进入反事实研究。
- Normal身份`47/47=100%`，缺失0、模拟新增0；47笔正常Gross精确为`+$1,424.50`，正常AccountNet`+$1,255.30`。
- 2笔非正常生命周期AccountNet`-$16.70`，账户总Gross精确为`+$1,415.00`，账户总AccountNet精确为`+$1,238.60`。
- 强制捕获28次、强制激活8次、状态不一致0；已知未来父交易时间倒置单独兼容1次。

## 本轮修正

1. 同一时间戳采用局部顺序：原始 Execute 行可在同刻退出回调之后决策，原始 ActiveTrade 行仍保持占用。
2. Baseline 从证据中的 `dailyCap=` / `DailyTradeLimit:max=` 自动读取日上限，v2.06 为 12，v2.08 为 10。
3. 区分 Normal、Abnormal、Quarantine 与 Abort：只有 Normal 计入正常单量；Abort 不形成持仓、不计损益；异常与隔离保留账户经济结果但不计正常单量。
4. 原始 ActiveTrade 引用被当作控制臂中的显式状态观察；该兼容只在 `BaselineMode` 生效，不能用于反事实政策获利。

## 联合 Gate 通过后的冻结目标

- 生产计价：3 手；ZoneBirth 必须保持可部署的 2 Base + 1 Runner。
- 主排序集：2026 H1；Q4 只作压力观察，不参与挑选。
- 进入 Smoke 前，保守 Gross 下界必须至少 `+$20,000`；同时 PF 至少 1.30、盈利周至少 70%，1-2月、3-4月、5-6月均为正。
- 保守下界必须扣除联合校准误差储备与未真实成交候选的执行风险储备；收益不得主要依赖 `CounterfactualRepriced`。
- `CorrectedResearch`不得读取v2.08强制捕获/激活标准答案。未同时达到盈利标准前继续离线分析，不编译 DLL、不清日志、不改版本、不 Smoke、不做中样本/全量 Replay、不打 Tag。
