# V5 Daily-Gate Reconstruction Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reconstruct the frozen Actual daily trade-count and live-account daily-loss gates from archived execution events before evaluating any V5 daily-gate hypothesis.

**Architecture:** The analysis is read-only: it loads only the five frozen archive groups, treats every `Execute` decision as a counter event, and applies only recorded rollback and account-PnL events in timestamp order. The first non-negotiable acceptance check is exact reproduction of the 2,047 Normal trades and `+$19,904.30` Normal Net under the frozen 15-trade / -$250 configuration. Any stricter-gate result remains a frozen-candidate counterfactual and cannot enter Smoke until separately approved.

**Tech Stack:** Python 3.12, pandas, archived ATAS CSV logs, Python unittest.

---

### Task 1: Define the minimal event-state test

**Files:**
- Create: `OPFStrategyV1/Scripts/tests/test_analyze_opf_v500_daily_gates.py`
- Create: `OPFStrategyV1/Scripts/Analyze-OPFV500DailyGates.py`

- [ ] **Step 1: Write a failing test for entry, rollback, and realized account PnL ordering**

```python
def test_replay_state_rolls_back_aborted_entry_and_applies_exit_net_before_next_gate():
    events = pd.DataFrame([
        {"Time": "2026-01-01T00:00:00", "Kind": "Execute", "TradeID": "abort"},
        {"Time": "2026-01-01T00:00:00", "Kind": "Rollback", "TradeID": "abort"},
        {"Time": "2026-01-01T00:05:00", "Kind": "Execute", "TradeID": "normal"},
        {"Time": "2026-01-01T00:10:00", "Kind": "AccountPnl", "TradeID": "normal", "Net": -60.0},
        {"Time": "2026-01-01T00:15:00", "Kind": "Execute", "TradeID": "next"},
    ])

    states = MODULE.replay_gate_states(events)

    self.assertEqual(0, states.loc[states.TradeID.eq("normal"), "PreTradeCount"].item())
    self.assertEqual(-60.0, states.loc[states.TradeID.eq("next"), "PreDailyNet"].item())
```

- [ ] **Step 2: Run the test and verify it fails because the analysis module does not exist**

Run: `python -m unittest OPFStrategyV1/Scripts/tests/test_analyze_opf_v500_daily_gates.py -v`

Expected: `FAIL` or import error naming `Analyze-OPFV500DailyGates.py`.

- [ ] **Step 3: Implement only `replay_gate_states(events)`**

```python
def replay_gate_states(events: pd.DataFrame) -> pd.DataFrame:
    # Sort timestamp plus recorded event priority; Execute records pre-state,
    # Rollback decrements the count, AccountPnl adds realized net.
    ...
```

- [ ] **Step 4: Run the test and verify it passes**

Run: `python -m unittest OPFStrategyV1/Scripts/tests/test_analyze_opf_v500_daily_gates.py -v`

Expected: `OK`.

### Task 2: Load archived state events and prove the baseline

**Files:**
- Modify: `OPFStrategyV1/Scripts/Analyze-OPFV500DailyGates.py`
- Modify: `OPFStrategyV1/Scripts/tests/test_analyze_opf_v500_daily_gates.py`

- [ ] **Step 1: Write failing tests for classification of submission-abort / abnormal / quarantine rollback events and only recorded account PnL**

```python
def test_rollback_event_names_are_classified_without_treating_normal_exit_as_rollback():
    assert MODULE.is_counter_rollback("ENTRY_SUBMISSION_ABORTED_V178")
    assert MODULE.is_counter_rollback("ABNORMAL_SAFETY_FLATTEN_ISOLATED_V182")
    assert not MODULE.is_counter_rollback("LIVE_ACCOUNT_PNL")
```

- [ ] **Step 2: Run the focused tests and confirm failure from the missing classifier**

Run: `python -m unittest OPFStrategyV1/Scripts/tests/test_analyze_opf_v500_daily_gates.py -v`

Expected: `FAIL` naming `is_counter_rollback`.

- [ ] **Step 3: Implement archive loading and baseline acceptance checks**

```python
def load_gate_events(archive_root: Path) -> pd.DataFrame:
    # Read Execute decision rows, rollback lifecycle events, and all account-PnL rows.
    # Preserve SnapshotID and UTC event time; do not alter live files.
    ...

def validate_frozen_baseline(events: pd.DataFrame) -> dict:
    # Require 2,047 retained Normal account trades and +19,904.30 Normal Net.
    ...
```

- [ ] **Step 4: Run all V500 script tests and the archive analysis**

Run: `python -m unittest discover -s OPFStrategyV1/Scripts/tests -p "test_analyze_opf_v500*.py" -v`

Run: `python OPFStrategyV1/Scripts/Analyze-OPFV500DailyGates.py --archive-root "$env:APPDATA\\ATAS\\StrategyLogs\\OPFStrategyV1_Archive" --output-dir OPFStrategyV1/Reports/v500_daily_gate_reconstruction`

Expected: all tests `OK`; baseline report declares exact 2,047-trade / `+$19,904.30` reproduction.

### Task 3: Evaluate only pre-registered one-dimensional tighter gates

**Files:**
- Modify: `OPFStrategyV1/Scripts/Analyze-OPFV500DailyGates.py`
- Modify: `OPFStrategyV1/Scripts/tests/test_analyze_opf_v500_daily_gates.py`
- Create: `OPFStrategyV1/Reports/v500_daily_gate_reconstruction_review.md`

- [ ] **Step 1: Write failing test that a lower cap rejects an entry only when reconstructed pre-count reaches the cap**

```python
def test_cap_filter_uses_reconstructed_pre_trade_count():
    entries = pd.DataFrame([
        {"TradeID": "a", "PreTradeCount": 0, "PreDailyNet": 0.0},
        {"TradeID": "b", "PreTradeCount": 1, "PreDailyNet": 0.0},
    ])

    kept = MODULE.select_frozen_entries(entries, max_trades=1, loss_limit=250.0)

    self.assertEqual(["a"], kept.TradeID.tolist())
```

- [ ] **Step 2: Run the focused test and confirm failure**

Run: `python -m unittest OPFStrategyV1/Scripts/tests/test_analyze_opf_v500_daily_gates.py -v`

Expected: `FAIL` naming `select_frozen_entries`.

- [ ] **Step 3: Implement the frozen-candidate scan with no replacement candidates**

```python
def select_frozen_entries(entries, max_trades, loss_limit):
    # Retain only original actual entries passing reconstructed pre-state.
    # Never invent skipped candidates or execution prices.
    ...
```

- [ ] **Step 4: Run tests and write a result review only if baseline acceptance remains exact**

Run: `python -m unittest discover -s OPFStrategyV1/Scripts/tests -p "test_analyze_opf_v500*.py" -v`

Expected: `OK`; report labels every stricter gate as `frozen-candidate counterfactual`, with segment and weekly results.

