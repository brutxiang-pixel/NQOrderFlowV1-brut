# V5 日上限 Replay 漂移校准 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为 V5 的 13 笔日上限研究添加一个只读 Actual 校准报告，区分稳定匹配交易与 Historical Replay 漂移。

**Architecture:** 新增一个独立 Python 分析器，读取冻结归档和活动日志，不修改现有 `Analyze-OPFV500DailyGates.py` 的基线重建职责。分析器按 `SignalID + ResearchPath + Side` 连接候选与冻结 Normal 订单，并把候选独有、基线独有、角色变化与已隔离异常分别列报。

**Tech Stack:** Python 3、pandas、unittest、CSV、JSON。

---

### Task 1: 写出订单归因的失败测试

**Files:**
- Create: `OPFStrategyV1/Scripts/tests/test_analyze_opf_v500_daily_cap13_actual_calibration.py`
- Create: `OPFStrategyV1/Scripts/Analyze-OPFV500DailyCap13ActualCalibration.py`

- [ ] **Step 1: 写失败测试，定义稳定与漂移归因**

```python
def test_classify_trade_pairs_separates_stable_role_change_and_candidate_only():
    candidate = pd.DataFrame([
        {"SignalID": "same", "ResearchPath": "A", "Side": "Long", "ExitRole": "TP", "NetPnLDollars": 10.0, "Classification": "Normal"},
        {"SignalID": "role", "ResearchPath": "A", "Side": "Long", "ExitRole": "SL", "NetPnLDollars": -5.0, "Classification": "Normal"},
        {"SignalID": "new", "ResearchPath": "A", "Side": "Long", "ExitRole": "TP", "NetPnLDollars": 8.0, "Classification": "Normal"},
    ])
    baseline = pd.DataFrame([
        {"SignalID": "same", "ResearchPath": "A", "Side": "Long", "ExitRole": "TP", "NetPnLDollars": 9.0, "Classification": "Normal"},
        {"SignalID": "role", "ResearchPath": "A", "Side": "Long", "ExitRole": "TP", "NetPnLDollars": 6.0, "Classification": "Normal"},
        {"SignalID": "old", "ResearchPath": "A", "Side": "Long", "ExitRole": "SL", "NetPnLDollars": -4.0, "Classification": "Normal"},
    ])
    result = MODULE.classify_trade_pairs(candidate, baseline)
    assert result["StableMatched"].sum() == 1
    assert result["ExitRoleChanged"].sum() == 1
    assert result["CandidateOnly"].sum() == 1
    assert result["BaselineOnly"].sum() == 1


def test_classify_trade_pairs_keeps_two_paths_that_share_a_signal_id_separate():
    candidate = pd.DataFrame([
        {"SignalID": "shared", "ResearchPath": "A", "Side": "Long", "ExitRole": "TP", "NetPnLDollars": 10.0, "Classification": "Normal"},
        {"SignalID": "shared", "ResearchPath": "B", "Side": "Long", "ExitRole": "SL", "NetPnLDollars": -5.0, "Classification": "Normal"},
    ])
    result = MODULE.classify_trade_pairs(candidate, candidate.copy())
    assert result["StableMatched"].sum() == 2
```

- [ ] **Step 2: 运行测试并确认因函数不存在而失败**

Run: `python -m unittest OPFStrategyV1.Scripts.tests.test_analyze_opf_v500_daily_cap13_actual_calibration -v`  
Expected: `AttributeError` for `classify_trade_pairs`.

- [ ] **Step 3: 实现最小归因函数**

```python
def classify_trade_pairs(candidate: pd.DataFrame, baseline: pd.DataFrame) -> pd.DataFrame:
    left = candidate.merge(
        baseline[["SignalID", "ResearchPath", "Side", "ExitRole", "NetPnLDollars"]],
        on=["SignalID", "ResearchPath", "Side"], how="outer", suffixes=("Candidate", "Baseline"), indicator=True,
    )
    left["StableMatched"] = left["_merge"].eq("both") & left["ExitRoleCandidate"].eq(left["ExitRoleBaseline"])
    left["ExitRoleChanged"] = left["_merge"].eq("both") & ~left["StableMatched"]
    left["CandidateOnly"] = left["_merge"].eq("left_only")
    left["BaselineOnly"] = left["_merge"].eq("right_only")
    return left
```

- [ ] **Step 4: 重新运行测试并确认通过**

Run: `python -m unittest OPFStrategyV1.Scripts.tests.test_analyze_opf_v500_daily_cap13_actual_calibration -v`  
Expected: 1 test passed.

### Task 2: 写出八日金标汇总的失败测试并实现只读报告

**Files:**
- Modify: `OPFStrategyV1/Scripts/Analyze-OPFV500DailyCap13ActualCalibration.py`
- Modify: `OPFStrategyV1/Scripts/tests/test_analyze_opf_v500_daily_cap13_actual_calibration.py`

- [ ] **Step 1: 写失败测试，定义汇总字段**

```python
def test_summarize_calibration_counts_normal_and_isolated_abnormal_separately():
    ledger = pd.DataFrame([
        {"Classification": "Normal", "NetPnLDollars": 10.0},
        {"Classification": "Abnormal", "NetPnLDollars": -2.0},
    ])
    summary = MODULE.summarize_candidate_ledger(ledger)
    assert summary == {"NormalTrades": 1, "NormalNetDollars": 10.0, "IsolatedAbnormalTrades": 1, "AbnormalAccountNetDollars": -2.0}
```

- [ ] **Step 2: 运行测试并确认函数不存在而失败**

Run: `python -m unittest OPFStrategyV1.Scripts.tests.test_analyze_opf_v500_daily_cap13_actual_calibration -v`  
Expected: `AttributeError` for `summarize_candidate_ledger`.

- [ ] **Step 3: 实现最小账本汇总和 CLI**

```python
def summarize_candidate_ledger(ledger: pd.DataFrame) -> dict[str, float | int]:
    normal = ledger.loc[ledger["Classification"].eq("Normal")]
    abnormal = ledger.loc[ledger["Classification"].ne("Normal")]
    return {
        "NormalTrades": int(len(normal)),
        "NormalNetDollars": round(float(normal["NetPnLDollars"].sum()), 2),
        "IsolatedAbnormalTrades": int(len(abnormal)),
        "AbnormalAccountNetDollars": round(float(abnormal["NetPnLDollars"].sum()), 2),
    }
```

CLI 仅接受 `--archive-root`、`--active-log-dir`、`--output-dir`；输出 `summary.csv` 和 `trade_attribution.csv`。任何缺少的必要 CSV 均抛出 `FileNotFoundError`，不生成部分结果。

- [ ] **Step 4: 运行全部 V5 Python 测试**

Run: `python -m unittest OPFStrategyV1.Scripts.tests.test_analyze_opf_v500_daily_gates OPFStrategyV1.Scripts.tests.test_analyze_opf_v500_daily_cap13_actual_calibration -v`  
Expected: all tests passed.

### Task 3: 用八日 Actual 金标回归并更新研究结论

**Files:**
- Modify: `OPFStrategyV1/Reports/v500_daily_cap13_actual_calibration_contract.md`
- Create: `OPFStrategyV1/Reports/v500_daily_cap13_actual_calibration_review.md`

- [ ] **Step 1: 运行只读分析器**

Run: `python OPFStrategyV1/Scripts/Analyze-OPFV500DailyCap13ActualCalibration.py --archive-root "$env:APPDATA/ATAS/StrategyLogs/OPFStrategyV1_Archive" --active-log-dir "$env:APPDATA/ATAS/StrategyLogs/OPFStrategyV1" --output-dir OPFStrategyV1/Reports/v500_daily_cap13_actual_calibration`

Expected: eight Snapshot rows, 104 Normal trades, 100 candidate-side SignalID matches, 4 candidate-only SignalID rows, 2 isolated abnormal rows, and `$1,862.60` total Normal Net.

- [ ] **Step 2: 更新裁决文字**

报告必须明确：机制通过；收益方向弱正；逐日精确经济校准未通过；13 笔不升级为生产参数；不要求重跑。

- [ ] **Step 3: 再运行测试和检查工作区差异**

Run: `python -m unittest OPFStrategyV1.Scripts.tests.test_analyze_opf_v500_daily_gates OPFStrategyV1.Scripts.tests.test_analyze_opf_v500_daily_cap13_actual_calibration -v; git diff --check`  
Expected: tests pass; no whitespace errors.
