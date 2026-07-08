# OPFStrategyV1

Independent Opening Pullback Failure strategy project.

The first implementation goal is a clean research scaffold:

- StrategyEngine decides signal quality.
- ExecutionEngine handles live/replay constraints.
- ResearchEngine records signals, snapshots, score breakdowns, and outcomes.
- InstrumentProfile and ExecutionProfile keep product and execution targets out of the strategy core.

The root document `MNQ新策略规则_v0.1.md` remains the current working specification until it is split into Strategy, Architecture, Data Dictionary, and Configuration documents.

Current data fields are documented in `DataDictionary.md`.

## Profiles

The ATAS panel exposes these text parameters:

- `Instrument Profile`
- `Execution Profile`

Supported instrument profile names:

- `MNQ_0.1`
- `NQ_0.1`
- `ES_0.1`
- `MES_0.1`
- `GC_0.1`
- `MGC_0.1`

Supported execution profile names:

- `MNQ_1Contract_Target150_200`
- `MNQ_1Contract_Target300`
- `MNQ_2Contract_Target300`

The selected profiles are written into each `ConfigSnapshot.json`. The snapshot records requested profile names, actual profile objects, `ProfileCatalogVersion`, and fallback flags. If a name is unknown, the strategy falls back to the MNQ single-contract default and writes a warning line to the research log.

Profile selection is currently a research/configuration scaffold. It makes point value, contract count, and daily target traceable, but it does not mean every instrument has been separately validated.

From `OPF_RESEARCH_0.17`, the key research CSV files also include version and profile context columns:

- `signals.csv`
- `research_outcomes.csv`
- `no_trade.csv`

These files include `StrategyVersion`, `ResearchSchemaVersion`, `InstrumentProfileName`, `ExecutionProfileName`, and `ProfileCatalogVersion` next to `SnapshotID`.

From `OPF_RESEARCH_0.18`, `research_outcomes.csv` and `replay_index.csv` also include research sizing fields such as `PlannedContracts`, `ActualContracts`, `PointValue`, `RiskPerContractDollars`, `TotalInitialRiskDollars`, `MFE_Dollars`, and `MAE_Dollars`.

From `OPF_RESEARCH_0.19`, `research_outcomes.csv` and `replay_index.csv` also include execution research fields such as `WouldTradeLive`, `ResearchOnlySignal`, `SkippedByDailyGuard`, `ExecutionSkipReasons`, `DailyTargetDollars`, `DailyLossLimitDollars`, and `MaxContracts`.

From `OPF_RESEARCH_0.20`, the strategy tracks two additional research-only observation paths:

- `UnknownRegimeZoneTouch`
- `ZoneBirthResearch`

These paths measure whether `RegimeUnknown` and `ZoneJustCreated` are filtering out useful movement. They are not live-entry paths.

From `OPF_RESEARCH_0.21`, observation signals also start an `ObservationConfirm` research path if reclaim / previous-bar-break confirmation appears within 3 bars. This lets us compare immediate observation entry against delayed confirmation entry.

From `OPF_RESEARCH_0.22`, confirmed observation signals also attempt `ObservationConfirm_Strict`, which requires confirmation distance of at least `0.25R` and confirm-bar heat no more than `0.5R`. This is still research-only and exists to test whether stricter confirmation can reduce MAE and catastrophic outcomes.

From `OPF_RESEARCH_0.23`, strict observation signals write a separate `ZoneQualityScore` into `score_breakdown.csv` and split strict research outcomes into:

- `ObservationStrict_BullFresh`
- `ObservationStrict_Other`

This keeps the mainline moving from raw confirmation toward zone-quality research. `BullFresh` is only a research bucket, not a live-entry rule.

From `OPF_RESEARCH_0.24`, `candidate_evaluations.csv` includes pullback episode research fields:

- `PullbackEpisodeID`
- `PullbackCountInRegime`
- `PullbackStartBar`
- `PullbackStatus`

This is a research-only implementation of the main spec's first/second pullback tracking. It does not yet block third-or-later pullbacks.

From `OPF_RESEARCH_0.25`, an observation setup that invalidates before confirmation also starts a research-only failure-reverse path:

- `FailureReverse_ObservationInvalidated`

The generated signal uses `SetupType=FailureReverse` and records `FailureSourceSignalID`, `FailureSource`, and `FailureSourceZoneFreshness` in `SkipReasons`. This is only a research path and does not place live reverse orders.

From `OPF_RESEARCH_0.26`, the same failure signal also starts a stricter retest-failed research path:

- `FailureReverse_RetestFailed`

This waits up to 6 bars for price to retest the failed zone, then requires confirmation away from the zone before tracking. It exists to compare immediate failure reversal against the main spec's stricter retest concept.

From `OPF_RESEARCH_0.27`, every research path also writes `risk_evaluations.csv`. This records initial risk, profile hard risk limit, estimated reward, estimated RR, and the skip reasons that would apply if Risk / EstimatedRR filters were enabled. This is diagnostic only; it does not filter signals yet.

From `OPF_RESEARCH_0.28`, selected core research paths also start a `WideStop1_5R` variant with 1.5x the original initial risk:

- `ObservationConfirm_WideStop1_5R`
- `ObservationStrict_BullFresh_WideStop1_5R`
- `ObservationStrict_Other_WideStop1_5R`
- `FailureReverse_ObservationInvalidated_WideStop1_5R`
- `FailureReverse_RetestFailed_WideStop1_5R`

These variants test whether many catastrophic outcomes are caused by stops being too tight or entries being too early. They are research-only and do not replace the base paths.

From `OPF_RESEARCH_0.29`, OPF has a first replay execution scaffold. It is disabled by default and controlled from the ATAS panel:

- `Enable Actual Orders`
- `Replay Execution Path`
- `Replay Order Quantity`
- `Replay Target R`

When enabled, the strategy submits a market entry for the selected research path, then submits an OCO stop/target bracket after the entry fill. This is the first ExecutionEngine integration test and should be used in ATAS replay only until verified.

From `OPF_RESEARCH_0.30`, replay execution is broader:

- `Replay Execution Paths` accepts multiple paths separated by `|`, `,`, or `;`, and `*` means all paths.
- `Replay Max Trades Per Day` limits actual replay orders per session day.
- Executed signals are written to `signals.csv` with `Stage=Executed`.
- `execution_events.csv` records entry send, fills, SL/TP sends, exits, skips, and cancels.

From `OPF_RESEARCH_0.31`, completed replay executions also write `execution_trades.csv` with entry, exit, SL/TP role, points, and dollar PnL. `ENTRY_SEND` events now include `TradeID`.

From `OPF_RESEARCH_0.32`, replay execution applies the selected `ExecutionProfile` daily guards to actual replay orders:

- stop new replay orders after `DailyTargetDollars`
- stop new replay orders after `DailyLossLimitDollars`
- stop new replay orders after `MaxFullLossTradesPerDay`
- stop new replay orders after `MaxConsecutiveLossesPerDay`

Research logging continues after these guards; only actual replay order submission is skipped. The HUD now shows execution PnL versus daily target/loss, and `execution_trades.csv` includes daily PnL after each completed execution.

From `OPF_RESEARCH_0.33`, replay execution keeps daily target/loss guards active, but full-loss-count and consecutive-loss-count guards are controlled by ATAS panel switches:

- `Replay Use Full Loss Guard`
- `Replay Use Consecutive Loss Guard`

Both default to `false` so replay research can collect more actual order samples. Turn them on only when validating stricter live-like execution discipline.

From `OPF_RESEARCH_0.34`, replay execution separates executable paths from research observation paths. `Replay Execution Paths = *` now still respects the execution whitelist unless `Replay Allow Research Paths` is enabled.

Default executable paths are:

- `ObservationConfirm`
- `ObservationStrict_BullFresh`
- `ObservationStrict_Other`
- `FailureReverse_RetestFailed`

Research observation paths such as `ZoneBirthResearch` and `UnknownRegimeZoneTouch` continue to be logged, but default replay execution skips them with `SKIP_RESEARCH_ONLY_PATH`.

From `OPF_RESEARCH_0.35`, the code defaults match the current replay validation preset so the ATAS panel does not need manual setup each round:

- `Enable Actual Orders = true`
- `Replay Execution Paths = *`
- `Replay Max Trades Per Day = 5`
- `Replay Allow Research Paths = false`
- `Replay Use Full Loss Guard = false`
- `Replay Use Consecutive Loss Guard = false`

This is a temporary code-level preset for replay research. A later version should move these values into a JSON configuration file and include the selected preset in `ConfigSnapshot.json`.

From `OPF_RESEARCH_0.36`, the ATAS panel labels and HUD use `Actual` instead of `Replay` for the order-submission controls. ATAS replay and live account execution use the same strategy order chain; selecting a live account means these controls can submit live orders. Internal member names still use the older replay naming until the JSON configuration layer replaces these panel defaults.

From `OPF_RESEARCH_0.37`, Actual order submission enforces the profile risk/reward checks that were previously diagnostic only:

- skip when `InitialRiskPoints > MaxRiskPointsHard`
- skip when `EstimatedRR < MinEstimatedRr`

Research tracking still records the signal and outcome. Actual execution writes `SKIP_RISK_RR` to `execution_events.csv` when a signal is blocked by these checks.

From `OPF_RESEARCH_0.38`, Actual execution defaults are loaded from JSON instead of relying only on ATAS panel defaults. The strategy reads:

`%APPDATA%\ATAS\StrategyConfigs\OPFStrategyV1_actual_execution.json`

If the file does not exist, the strategy creates it with the current research preset and uses that preset immediately. `ConfigSnapshot.json` records the config path, load status, and loaded `ActualExecutionSettings`.

The repo also keeps a source-controlled template at `OPFStrategyV1/Configs/OPFStrategyV1_actual_execution.default.json`. This template is for review and version control; the runtime file under `%APPDATA%` is the file ATAS uses.

From `OPF_RESEARCH_0.39`, the chart draws Actual order levels:

- Entry line in blue
- SL line in red
- TP line in green
- Exit line in gold after the order closes

The lines are controlled by `Show Actual Order Lines` and remain visible for `Actual Line Lookback Bars` after exit.

From `OPF_RESEARCH_0.40`, Actual execution has a dedicated decision ledger:

- `execution_decisions.csv` records every Actual execution decision.
- `Decision` is `Execute` or `Skip`.
- `Reason` records the execution-layer reason such as `ResearchOnlyPath`, `EstimatedRRTooLow`, `ActiveTrade`, or `TradeID`.
- Each row includes entry, stop, target, initial risk, estimated reward, estimated RR, daily PnL, and current daily trade count.

The chart also keeps multiple recent Actual order overlays, controlled by `Actual Max Visible Orders`.

From `OPF_RESEARCH_0.41`, Actual execution rows are easier to join and audit:

- `execution_decisions.csv` includes `TradeID` on executed rows.
- `execution_trades.csv` includes `InitialRiskPoints`, `TargetR`, `PointsR`, `RiskDollars`, and `TargetDollars`.
- The summary script reports Actual execution total R / average R and checks whether executed decisions have matching completed trade rows.

From `OPF_RESEARCH_0.42`, Actual bracket handling is safer:

- SL / TP are repriced from the actual entry fill, not only the planned signal close.
- If the SL leg is immediately done after submission, the TP leg is suppressed to avoid double exits.
- If a duplicate SL/TP fill is still received after an exit is complete, the strategy submits an emergency market flatten order in the opposite direction of the duplicate fill.

From `OPF_RESEARCH_0.43`, Actual execution runs a wider-stop experiment without changing the entry rules:

- Runtime config version is `ACTUAL_EXEC_0.2`.
- The default Actual path is `ObservationConfirm_WideStop1_5R`.
- `ActualMinEstimatedRr` defaults to `1.0` for this execution experiment.
- `ActualWideStopMultiplier` defaults to `1.5`.
- `risk_evaluations.csv` includes RR tier fields: `RR_GE_1_0`, `RR_GE_1_2`, and `RR_GE_1_5`.
- Existing `ACTUAL_EXEC_0.1` runtime config is upgraded to the new default when the strategy starts.

From `OPF_RESEARCH_0.44`, Actual execution moves the wider stop back to research-only and tests the two mainline executable paths in replay/live-account order submission:

- Runtime config version is `ACTUAL_EXEC_0.3`.
- The default Actual paths are `ObservationConfirm|FailureReverse_ObservationInvalidated`.
- `FailureReverse_ObservationInvalidated` becomes an execution-eligible controlled experiment because 0.43 showed it had enough RR>=1.0 candidates and strong MFE statistics.
- Wide-stop variants are still logged for research when enabled, but they are no longer the default Actual path.

From `OPF_RESEARCH_0.45`, research outcomes can be joined back to actual order results:

- `research_outcomes.csv` includes Actual verification fields such as `ActualVerified`, `ActualTradeID`, `ActualExitRole`, `ActualPnL_R`, `ResolvedOutcomeClass`, and `OutcomeSource`.
- `IntraBarAmbiguous` explicitly marks research rows where OHLC bars touched stop and target on the same bar.
- The summary script separates `ResearchOHLC` rows from `ActualTrade` verified rows so actual order fills can override ambiguous OHLC inference during analysis.

From `OPF_RESEARCH_0.46`, signal identity and research outcome audits are stricter:

- Zone-based `SignalID` values include a compact zone tag so multiple zones touched on the same bar do not collide.
- Research outcome writing has a duplicate guard per signal/path/entry/exit key.
- The summary lifecycle audit reports whether every completed Actual trade has exactly one verified research outcome row.

From `OPF_RESEARCH_0.47`, research outcomes also write fixed-exit policy evaluations:

- `exit_policy_evaluations.csv` compares `Fixed1_5R`, `Fixed2R`, and `Fixed2_5R` for every completed research tracker.
- Each row records `ExitReason`, `PnL_R`, dollars, first stop/target bars, and same-bar ambiguity.
- Actual order submission still uses the configured Actual target; these rows are research-only and do not change live/replay order behavior.

From `OPF_RESEARCH_0.48`, Actual execution applies a strategy-quality gate before risk/RR and daily guards:

- Actual defaults now use `ACTUAL_EXEC_0.4`.
- `ActualRequireTrendRegime=true` blocks Actual orders when the signal's regime score did not pass Trend.
- `ActualMinSetupQualityScore=70` blocks low-quality setups while keeping the same signal in Research.
- `execution_decisions.csv` includes `RegimeScore`, `SetupQualityScore`, and `StrategyEligible`.
- `execution_events.csv` writes `SKIP_STRATEGY_QUALITY` when this gate blocks an order.

From `OPF_RESEARCH_0.49`, Actual execution moves closer to the MD mainline setup design:

- Actual defaults now use `ACTUAL_EXEC_0.5`.
- The default Actual paths are `BreakawayFvg|ObservationConfirm|FailureReverse_RetestFailed`.
- `BreakawayFvg` and `BreakawayRetest` are execution-eligible paths when they pass strategy quality, risk, and RR checks.
- Failure reverse Actual orders require the retest-failed path by default; immediate invalidation remains research unless this guard is disabled.
- The summary prints `Eligible But Path Disabled` so high-quality blocked paths can be reviewed before changing execution defaults.

From `OPF_RESEARCH_0.50`, reward and RR evaluation becomes path-aware:

- `BreakawayFvg` and `BreakawayRetest` use the configured Actual target R as the estimated reward model.
- `FailureReverse_RetestFailed` uses the larger of nearest structure reward and a 1.2R minimum model.
- `ObservationStrict_BullFresh` also uses a nearest-structure / 1.2R hybrid model.
- High-quality `ObservationConfirm` can use a 1R minimum model when nearest structure is too close.
- `risk_evaluations.csv` and `execution_decisions.csv` include `RewardModel`.
- The summary reports risk/RR by reward model and TrendScore component diagnostics.

From `OPF_RESEARCH_0.51`, Actual execution keeps a post-exit protection cleanup state:

- After SL/TP fills, the strategy cancels all known Entry/SL/TP orders until they are no longer working.
- Immediate TP after bracket submission starts SL cleanup immediately.
- The HUD shows `cleanup=<n>` while known execution orders still look working.
- `execution_events.csv` writes `PROTECTION_CLEANUP_*` events for audit.

From `OPF_RESEARCH_0.52`, the original mainline v0.51 setup work is included after the execution hotfix:

- Trend Pullback now writes an explicit `Candidate -> Confirmed -> Triggered` lifecycle.
- The triggered core path is `TrendPullbackConfirmed` and is eligible for Actual execution.
- Pullback count is enforced: the third and later pullbacks in the same regime are logged as `ThirdPullback` instead of traded.
- `FailureReverse_RetestFailed` is marked as `Triggered` once retest failure confirmation appears.
- Actual defaults now use `ACTUAL_EXEC_0.7` and include `TrendPullbackConfirmed`.
- Completed Actual order overlays are drawn as short trade-local segments; only active orders keep full-width lines.

From `OPF_RESEARCH_0.53`, the mainline repair closes two validation gaps:

- Trend-aligned FVG birth can now create a pending mainline pullback retest, which becomes a formal `Candidate` on the later zone retest.
- Actual SL/TP exits immediately write the matching research outcome when the tracker is still active, including same-bar TP/SL fills.

From `OPF_RESEARCH_0.54`, executed research trackers stay open while their Actual order is active, even after the normal research window. This keeps late SL/TP fills linked back to `research_outcomes.csv`.

From `OPF_RESEARCH_0.55`, the mainline Trend Pullback trigger follows the conservative-entry rule from the main spec:

- A `Confirmed` pullback waits up to two closed bars for a retrace back to the zone boundary.
- If the retrace appears, the triggered signal records `TriggeredAfterConfirmRetrace`.
- If it does not appear, `no_trade.csv` records `NoRetraceAfterConfirm`.
- Actual risk checks use `MaxAllowedRiskPoints = min(MaxRiskPointsHard, max(12, ATR14 * AtrRiskMultiplier))` and can skip orders with `RiskTooWideVolAdjusted`.

From `OPF_RESEARCH_0.56`, setup quality uses `OPF_OF_0.2` for the `OrderFlow` scoring component:

- The current implementation is a price-flow proxy from candle body direction and close location.
- It contributes 0, 3, 6, or 10 points to `SetupQualityScore`.
- It is not a hard filter and is not a final replacement for true ATAS order-flow inputs.

From `OPF_RESEARCH_0.57`, `research_outcomes.csv` includes outcome-efficiency fields:

- `ExitEfficiency`
- `RunupCapturePct`
- `AdverseBeforeProfit_R`

These fields are intended to separate entry quality problems from exit-capture problems before larger parameter tuning.

From `OPF_RESEARCH_0.58`, Actual execution runs a smaller fixed-target experiment:

- Actual defaults now use `ACTUAL_EXEC_0.8`.
- `ActualTargetR` is reduced from `2.0R` to `1.5R`.
- Entry logic, StrategyEngine scoring, and the executable path whitelist are unchanged.
- The purpose is to test whether the recent high-MFE-but-SL samples improve when the live/replay target is easier to capture.

From `OPF_RESEARCH_0.59`, Actual execution broadens the controlled smoke-test path set:

- Actual defaults now use `ACTUAL_EXEC_0.9`.
- `FailureReverse_ObservationInvalidated` is added to the executable path whitelist.
- `ActualRequireFailureRetest=false` for this whitelist experiment.
- `ActualTargetR` stays at `1.5R`.
- The goal is to raise 3-day smoke-test Actual sample size while checking that the order lifecycle remains clean.

From `OPF_RESEARCH_0.60`, the same smoke-test path set uses a path-aware strategy-quality gate:

- Actual defaults now use `ACTUAL_EXEC_1.0`.
- `FailureReverse_ObservationInvalidated` may bypass the original-trend regime requirement because it is testing a failed setup reversal, not a normal trend pullback.
- Only that immediate Failure path uses `ActualFailureReverseMinSetupQualityScore=40`; other Actual paths still use `ActualMinSetupQualityScore=70`.
- Risk, estimated RR, active-trade, daily limit, and protection-cleanup rules are unchanged.
- The goal is to produce enough Actual samples for the Full Backtest Readiness Gate without globally loosening the strategy.

From `OPF_RESEARCH_0.61`, Actual execution starts the first tuning pass from the April 1-10 replay batch:

- Actual defaults now use `ACTUAL_EXEC_1.1`.
- `ObservationConfirm` is removed from `ActualExecutionPaths` and remains research-only.
- `FailureReverse_ObservationInvalidated` is still in the Actual path list, but Long immediate-failure entries are blocked with `FailureImmediateLongDisabled`.
- `TrendPullbackConfirmed`, `BreakawayFvg`, and `FailureReverse_RetestFailed` remain eligible under the existing strategy-quality, risk/RR, daily-limit, and protection-cleanup rules.
- The goal is to compare a cleaner Actual path set against v0.60 without changing entry generation or research tracking.

From `OPF_RESEARCH_0.62`, Actual execution applies the second tuning pass from the same April 1-10 replay comparison:

- Actual defaults now use `ACTUAL_EXEC_1.2`.
- `BreakawayFvg` remains in the Actual path list, but Long entries are blocked with `BreakawayLongDisabled`.
- Breakaway Short, immediate Failure Short, Trend Pullback, and Failure Retest paths keep the v0.61 strategy-quality, risk/RR, daily-limit, and protection-cleanup rules.
- The goal is to test whether removing the weak Breakaway Long subset improves the v0.61 replay result without changing signal generation or research logging.

From `OPF_RESEARCH_0.63`, Actual execution adds a protection-loss safety check:

- Actual defaults now use `ACTUAL_EXEC_1.3`.
- If both SL and TP are no longer working while the entry is still open and no exit fill was recorded, the strategy waits briefly for normal OCO fill events and then sends an emergency `FLATTEN` order.
- `FLATTEN` fills are treated as executable exits and are written to `execution_trades.csv`.
- The goal is to prevent a replay/live position from remaining open when protective orders disappear without a matching exit fill.

From `OPF_RESEARCH_0.64`, Actual execution patches the v0.63 safety check:

- Actual defaults now use `ACTUAL_EXEC_1.4`.
- The code default version is aligned with the JSON template so runtime config is not downgraded during automatic upgrades.
- Protection-loss detection is also checked immediately after bracket submission completes, covering fast order-state callbacks that arrive before `BracketSubmitted=true`.

From `OPF_RESEARCH_0.65`, Actual execution adds a stop-time fallback:

- Actual defaults now use `ACTUAL_EXEC_1.5`.
- If the strategy stops while an Actual entry is still open, it logs `ACTIVE_ON_STOP`, sends a `FLATTEN` order, and writes a `STOPPED` execution-trade row from the last known candle close.
- This prevents `Execute` decisions from being left without an outcome when replay stops before TP/SL is filled.

From `OPF_RESEARCH_0.66`, Actual execution returns to path-level tuning:

- Actual defaults now use `ACTUAL_EXEC_1.6`.
- `TrendPullbackConfirmed` remains in the Actual path list, but Long entries are blocked with `TrendPullbackLongDisabled`.
- Trend Pullback Short, Breakaway Short, FailureReverse Short, and Failure Retest paths keep the v0.65 execution safety behavior.
- The goal is to test whether removing the weak Trend Pullback Long subset improves weekly results without reducing research visibility.

From `OPF_RESEARCH_0.67`, Actual execution starts a controlled sample-size expansion:

- Actual defaults now use `ACTUAL_EXEC_1.7`.
- `ObservationStrict_BullFresh` is added to `ActualExecutionPaths`.
- The goal is to add a higher-quality observation path that was positive in research while keeping weaker broad observation paths disabled.

From `OPF_RESEARCH_0.68`, Actual execution adds the second sample-size expansion path:

- Actual defaults now use `ACTUAL_EXEC_1.8`.
- `ShadowCandidate` is added to `ActualExecutionPaths` and to the executable path allowlist.
- The goal is to increase weekly trade count using a research-positive early candidate path without enabling broad Observation/Unknown paths.

From `OPF_RESEARCH_0.69`, Actual execution tunes the `ShadowCandidate` reward model:

- Actual defaults now use `ACTUAL_EXEC_1.9`.
- `ShadowCandidate` uses the configured fixed target R as its estimated reward model for Actual RR checks.
- The goal is to test whether v0.68 under-executed this early candidate path because `NearestStructure` was too conservative, while keeping the same risk limits and path whitelist.

From `OPF_RESEARCH_0.70`, Actual execution returns to the mainline path balance:

- Actual defaults now use `ACTUAL_EXEC_1.10`.
- `ShadowCandidate` is removed from `ActualExecutionPaths` and remains research-only.
- `TrendPullbackConfirmed` Long is no longer blocked by a path-level rule; it still must pass the strategy-quality and risk/RR gates.
- The goal is to improve profitable trade count from the core MD setup instead of continuing to tune the weaker early-candidate path.

From `OPF_RESEARCH_0.71`, Actual execution shifts the sample-size experiment back to core structure:

- Actual defaults now use `ACTUAL_EXEC_1.11`.
- `ObservationStrict_BullFresh` is removed from `ActualExecutionPaths` and remains research-only.
- `BreakawayFvg` Long is no longer blocked by `BreakawayLongDisabled`; it still must pass strategy-quality and risk/RR gates.
- The goal is to replace the weak observation Actual sample with a controlled Breakaway core-structure sample while keeping `ShadowCandidate` research-only.

From `OPF_RESEARCH_0.72`, stop-time execution auditing is hardened:

- If strategy stop happens before the entry order has any confirmed fill, the execution writes `STOPPED_NO_ENTRY` with zero PnL instead of calculating from an entry price of zero.
- Execution trade rows fall back to the planned created price only when a fill price is unavailable, preventing synthetic stop rows from polluting PnL.

From `OPF_RESEARCH_0.73`, Actual execution narrows Breakaway back into a qualified core-structure path:

- Actual defaults now use `ACTUAL_EXEC_1.13`.
- `BreakawayFvg` returns to research-only.
- `BreakawayFvg_Qualified` is added to `ActualExecutionPaths`.
- The qualified subset requires a fresh bullish FVG, passed trend regime, setup quality at least 80, risk no wider than 8.5 points, and confirmation heat no more than 0.5R.
- The goal is to test a stable Breakaway subset instead of treating the broad Breakaway research path as directly executable.

From `OPF_RESEARCH_0.74`, Actual execution shifts from weak Trend Pullback execution toward the Failure Reverse core:

- Actual defaults now use `ACTUAL_EXEC_1.14`.
- `TrendPullbackConfirmed` is removed from `ActualExecutionPaths` and remains research-only.
- `FailureReverse_LongQualified` is added as a separately labeled Actual path.
- The qualified Long subset requires immediate failure-reverse direction Long, risk no wider than 8.5 points, and entry-bar heat no more than 0.5R.
- The goal is to increase higher-quality executable sample size without re-enabling broad Breakaway or broad Long failure reversal.

From `OPF_RESEARCH_0.75`, `FailureReverse_LongQualified` is widened for sample-size balance:

- Actual defaults now use `ACTUAL_EXEC_1.15`.
- `FailureReverse_LongQualified` keeps the separate Actual path label, but no longer blocks on entry-bar heat.
- The qualified Long subset now requires immediate failure-reverse direction Long and risk no wider than 8.5 points.
- Entry-bar heat is still written into signal reasons as `HeatR` for later analysis instead of acting as a hard gate.
- The goal is to test whether controlled Long Failure Reverse can add enough profitable trades without falling back to broad, unlabeled Long failure execution.

From `OPF_RESEARCH_0.76`, `FailureReverse_LongQualified` uses the observed profitable heat band:

- Actual defaults now use `ACTUAL_EXEC_1.16`.
- `FailureReverse_LongQualified` still requires immediate failure-reverse direction Long and risk no wider than 8.5 points.
- It now requires `1.0 <= HeatR <= 2.0`, based on the v0.75 replay where this band was positive while `HeatR > 2.0` dragged performance.
- The goal is to keep some added Long Failure Reverse sample size while removing the weakest heat bucket.

From `OPF_RESEARCH_0.77`, Actual execution records its own excursion metrics:

- Actual defaults now use `ACTUAL_EXEC_1.17`.
- `execution_trades.csv` adds `ActualMFEPoints`, `ActualMAEPoints`, `ActualMFE_R`, and `ActualMAE_R`.
- Actual-verified `research_outcomes.csv` writes the same Actual excursion fields next to Actual PnL.
- Trading paths are unchanged; this version fixes the interpretability gap where an Actual TP could be correct while the research tracker `MFE_R` appeared too low.

From `OPF_RESEARCH_0.78`, Actual execution skips same-bar ambiguous entries:

- Actual defaults now use `ACTUAL_EXEC_1.18`.
- If the signal entry bar has already touched the planned SL or TP level, the signal remains in ResearchEngine but Actual execution is skipped with `SKIP_SAME_BAR_AMBIGUOUS`.
- This protects replay/live validation from cases where the bar-based research model enters at bar close but the execution simulator immediately fills a protective order using that same bar's already-formed high/low.

From `OPF_RESEARCH_0.79`, broad `BreakawayFvg` is re-enabled as a controlled Actual path:

- Actual defaults now use `ACTUAL_EXEC_1.19`.
- `ActualExecutionPaths` includes `BreakawayFvg` again, alongside `BreakawayFvg_Qualified` and the current failure-reverse paths.
- `BreakawayFvg` still must pass strategy quality, risk/RR, daily limits, active-trade checks, and the v0.78 same-bar ambiguity guard.
- The goal is to increase high-quality executable opportunity count without reopening broad Observation paths.

## Evidence Accumulation Phase

After `OPF_RESEARCH_0.79`, the project enters an evidence accumulation phase. The primary goal is no longer to add new setup types quickly; it is to identify which existing signal combinations have repeatable edge while keeping Actual execution stable.

Current research scope:

- Do not add new setup families until existing `Breakaway`, `TrendPullback`, and `Failure` paths have enough candidate evidence.
- Change at most 1-2 core variables per version.
- Treat 2-4 weeks of replay data as one evaluation cycle when judging a rule change.
- Keep research rows for skipped signals whenever possible so filters can be audited later.

Priority weights for upcoming versions:

1. Edge Attribution and controlled variables: 30%
   Attribute every signal and Actual trade by setup, path, regime, zone, side, risk bucket, estimated RR bucket, time bucket, and outcome. This answers where edge actually comes from before turning observations into hard rules.
2. Funnel diagnostics: 20%
   Measure where opportunities are lost from regime, zone, candidate, trigger, risk, score, execution, and outcome. This prevents fixing the wrong layer when trade count is low.
3. Existing setup evidence depth: 15%
   Build sample size for current setup families only. New entry types are paused so `Breakaway`, `TrendPullback`, and `Failure` can be evaluated cleanly.
4. Unknown regime decomposition: 10%
   Split `Unknown` into research reasons such as low trend score, excessive VWAP crossings, swing conflict, weak displacement, or volatility mismatch.
5. Time-of-day analysis: 10%
   Bucket signals by session/time window before adding more price filters. Time filters may become simpler and more stable than additional setup rules.
6. Execution stability and net PnL model: 10%
   Keep Actual order lifecycle clean and start separating gross PnL, estimated commission, slippage, and net PnL for future product/profile comparisons.
7. Benchmark comparison: 5%
   Add simple market benchmarks so strategy performance can be compared against basic session or opening-range behavior instead of being judged in isolation.

The v0.79 finding is treated as evidence, not a final rule: broad `BreakawayFvg` increased Actual sample size, but wide-risk trades drove the loss. `Risk <= 11` is a candidate hypothesis to test under controlled-variable discipline, not proof by itself.

From `OPF_RESEARCH_0.80`, the HUD adds execution visibility without changing strategy rules:

- Actual defaults now use `ACTUAL_EXEC_1.20`.
- HUD shows today's sent orders, completed exits, TP/SL/Other counts, daily NetR, daily dollars, full-loss/consecutive-loss counters, active order Entry/SL/TP, last order, and recent completed orders.
- Entry logic, Actual path whitelist, risk filters, and exits are unchanged.

From `OPF_RESEARCH_0.81`, the mainline evidence-accumulation logs are expanded without changing entry or execution rules:

- Actual defaults now use `ACTUAL_EXEC_1.21` with the same paths, target, risk/RR, and daily guard settings as v0.80.
- `edge_attribution.csv` records signal/path attribution fields for setup, regime, zone, risk bucket, estimated RR bucket, time bucket, and outcome.
- `funnel_events.csv` records lightweight stage events from zone touch, confirmation, no-trade, execution decision, and research outcome.
- `RegimeUnknown` no-trade rows include failed trend-score component reasons so Unknown can be analyzed instead of treated as one bucket.
- HUD combines daily order result and daily PnL into one `Today:` line.

From `OPF_RESEARCH_0.82`, the v0.81 evidence logs are tightened without changing trading behavior:

- Actual defaults now use `ACTUAL_EXEC_1.22` with unchanged paths, target, risk/RR, and daily guard settings.
- Actual-verified research rows calculate `ExitEfficiency` and `RunupCapturePct` from Actual execution MFE, preventing TP rows from showing efficiency above 100% when the research tracker had not yet captured the Actual exit excursion.
- `execution_decisions.csv` and `funnel_events.csv` include `ExecutionScope` so Actual candidates can be separated from research-only/path-disabled rows during funnel analysis.

From `OPF_RESEARCH_0.83`, Actual execution starts a controlled long-period Breakaway risk-cap experiment:

- Actual defaults now use `ACTUAL_EXEC_1.23`.
- `BreakawayFvg` Actual orders are skipped when `InitialRiskPoints > ActualBreakawayMaxRiskPoints`.
- The default `ActualBreakawayMaxRiskPoints` is `11`.
- Skipped rows write `BreakawayRiskCapExceeded:risk=...,max=11` to `execution_decisions.csv`, while ResearchEngine continues to track the same Breakaway path for comparison.
- No Setup, Regime, TP/SL, path whitelist, strategy-quality threshold, or estimated-RR rule is changed.

From `OPF_RESEARCH_0.84`, Actual execution shifts from risk narrowing to controlled volume expansion:

- Actual defaults now use `ACTUAL_EXEC_1.24`.
- The current hard research constraint is at least about 5 Actual trades per trading day; trade count is now a primary gate before fine-tuning entry quality.
- `ObservationConfirm` is added back to `ActualExecutionPaths` because prior research showed it can provide enough existing-path sample size without adding a new setup family.
- `ObservationConfirm` uses a path-specific risk cap: skip when `InitialRiskPoints > ActualObservationConfirmMaxRiskPoints`, default `11`.
- `ObservationConfirm` uses the fixed `ActualTargetR=1.5` reward model for Actual RR checks, matching the current fixed-exit research comparison.
- `ActualMaxTradesPerDay` is raised from `5` to `20`; it is now a safety ceiling, not the desired daily trade count.
- The MNQ one-contract execution profile disables daily target and daily loss stops for this evidence-accumulation phase so replay can measure opportunity volume without daily guard truncation.
- Broad `BreakawayFvg` keeps the v0.83 `Risk <= 11` cap. No new setup family is added.

From `OPF_RESEARCH_0.85`, Actual execution tests the first volume-first threshold change:

- Actual defaults now use `ACTUAL_EXEC_1.25`.
- `ObservationConfirm` has its own setup-quality floor: `ActualObservationConfirmMinSetupQualityScore=56`.
- Other Actual paths keep the default `ActualMinSetupQualityScore=70`, and immediate failure-reverse keeps `ActualFailureReverseMinSetupQualityScore=40`.
- `ObservationConfirm` still requires `InitialRiskPoints <= 11` and uses fixed `ActualTargetR=1.5`.
- No time-of-day restriction is added; the current goal is to build enough daily opportunity count before applying safety controls.
- `execution_trades.csv` records `PlannedRiskPoints`, `FilledRiskPoints`, and `RiskDriftPoints` so planned risk caps can be audited against actual filled risk after bracket repricing.
- No new setup family is added.

From `OPF_RESEARCH_0.86`, Actual execution adds a safety-style Failure Retest volume path:

- Actual defaults now use `ACTUAL_EXEC_1.26`.
- `FailureReverse_RetestFailed` uses `ActualFailureRetestMinSetupQualityScore=56` and `ActualFailureRetestMaxRiskPoints=11`.
- `FailureReverse_RetestFailed` is treated as a failure-reversal path for execution gating, so it is not blocked by the TrendRegime hard gate.
- `ObservationConfirm` stays at `ActualObservationConfirmMinSetupQualityScore=56` and `ActualObservationConfirmMaxRiskPoints=11`.
- No time-of-day restriction is added.
- No new setup family is added; this version only promotes an existing researched Failure path to controlled Actual补量.

From `OPF_RESEARCH_0.87d`, Actual execution adds controlled ObservationConfirm filler entries:

- Actual defaults now use `ACTUAL_EXEC_1.27`.
- The main `ObservationConfirm` rule stays unchanged at `ActualObservationConfirmMinSetupQualityScore=56` and `ActualObservationConfirmMaxRiskPoints=11`.
- When today's Actual order count is still below `ActualObservationConfirmFillerUntilDailyTrades=5`, `ObservationConfirm` signals with `52 <= SetupQualityScore < 56` can execute as filler.
- `ActualObservationConfirmMaxFillerTradesPerDay=2` caps these lower-quality filler entries so bad days cannot be forced into unlimited trades.
- Filler executions are marked in `execution_decisions.csv` with `ObservationFiller`.
- No time-of-day restriction is added.
- No new setup family is added.

From `OPF_RESEARCH_0.88`, Actual execution tests a controlled ObservationConfirm volume risk band:

- Actual defaults now use `ACTUAL_EXEC_1.28`.
- The normal `ObservationConfirm` rule stays unchanged at `ActualObservationConfirmMinSetupQualityScore=56` and `ActualObservationConfirmMaxRiskPoints=11`.
- While today's Actual order count is still below `ActualObservationConfirmFillerUntilDailyTrades=5`, `ObservationConfirm` signals with `SetupQualityScore >= 56` can use `ActualObservationConfirmVolumeMaxRiskPoints=12.5`.
- This does not apply to lower-quality filler signals; the `52 <= SetupQualityScore < 56` filler pool still uses the base `ActualObservationConfirmMaxRiskPoints=11`.
- Executions that use the expanded risk band are marked in `execution_decisions.csv` with `ObservationRiskExpansion`.
- No time-of-day restriction is added.
- No new setup family is added.

From `OPF_RESEARCH_0.89`, Actual execution redirects volume expansion away from the failed Observation risk-band test:

- Actual defaults now use `ACTUAL_EXEC_1.29`.
- `ActualObservationConfirmVolumeMaxRiskPoints` is set back to `11`, so v0.88's `ObservationRiskExpansion` path is disabled by default.
- `FailureReverse_RetestFailed_WideStop1_5R` is added to `ActualExecutionPaths` as a controlled existing-path supplement.
- The wide Failure Retest path can execute only while today's Actual order count is still below `ActualObservationConfirmFillerUntilDailyTrades=5`.
- Wide Failure Retest executions are marked in `execution_decisions.csv` with `FailureRetestWideFiller`.
- The base `FailureReverse_RetestFailed` quality and risk rules still apply: `ActualFailureRetestMinSetupQualityScore=56` and `ActualFailureRetestMaxRiskPoints=11`.
- No time-of-day restriction is added.
- No new setup family is added.

From `OPF_RESEARCH_0.90`, Actual execution adds execution-quality isolation:

- Actual defaults now use `ACTUAL_EXEC_1.30`.
- Entry, TP, SL, and synthetic stop exits are checked against expected prices with a two-tick tolerance.
- Abnormal exits are still written to `execution_trades.csv`, but their normal `Points`, `PointsR`, and `Dollars` fields are set to zero so daily statistics are not polluted.
- Raw abnormal values remain available in `RawPoints`, `RawDollars`, and `RawPointsR`.
- `execution_trades.csv` adds `IsAbnormalExecution`, `AbnormalReason`, `ExpectedExitPrice`, and `ExitPriceDriftPoints`.
- `execution_events.csv` writes `ABNORMAL_EXECUTION` rows for abnormal fills.
- Abnormal actual outcomes use roles such as `ABNORMAL_TP`, so `research_outcomes.csv` does not classify them as valid excellent TP trades.
- No setup, risk, score, time-of-day, or path whitelist rule is changed.

From `OPF_RESEARCH_0.91`, Actual execution keeps the same strategy rules and refines only execution-quality isolation:

- Actual defaults now use `ACTUAL_EXEC_1.31`.
- The abnormal fill tolerance is widened to 16 ticks / 4 points, whichever is larger.
- This avoids treating ordinary replay market-entry or stop slippage as corrupted execution data.
- Large TP/SL drift, stale stop handling, and non-standard exit roles are still isolated from normal `Points`, `PointsR`, and `Dollars` statistics.
- No setup, risk, score, time-of-day, or path whitelist rule is changed.

From `OPF_RESEARCH_0.92`, Actual execution adds an entry-fill safety gate:

- Actual defaults now use `ACTUAL_EXEC_1.32`.
- If the market entry fill is farther than the abnormal-fill tolerance from the planned entry price, the fill is rejected for strategy accounting.
- Rejected entry fills write `ENTRY_FILL_REJECTED` to `execution_events.csv`.
- The strategy does not submit SL/TP brackets from the bad entry fill price.
- The strategy immediately submits an emergency flatten order for the filled quantity.
- No setup, risk, score, time-of-day, or path whitelist rule is changed.

From `OPF_RESEARCH_0.93`, Actual execution adds stronger order-attribution diagnostics:

- Actual defaults now use `ACTUAL_EXEC_1.33`.
- Order attach logs include `ExtId`, state, type, direction, price, trigger price, unfilled quantity, and order quantity.
- `MYTRADE` logs include the same order context so replay fill provenance can be audited.
- A trade callback is accepted only when the parsed `TradeID` matches the current execution and the callback order `ExtId` matches the expected entry, SL, or TP order when both IDs are available.
- Comment-matched but order-mismatched callbacks write `TRADE_ORDER_MISMATCH` and are ignored.
- No setup, risk, score, time-of-day, or path whitelist rule is changed.

From `OPF_RESEARCH_0.94`, Actual execution returns to the main evidence-accumulation objective of increasing high-quality trade count:

- Actual defaults now use `ACTUAL_EXEC_1.34`.
- `StructureConfirmShadow_ConfirmBarStop_Min10` is promoted from research-only to Actual execution.
- `TrendPullbackConfirmed` is added to the default Actual execution path list.
- Both paths are existing Trend Pullback / structure-confirmation variants, not new setup families.
- The version does not widen `ObservationConfirm` risk, does not add time-of-day filtering, and does not change target R.
- The expected validation question is whether these structure-confirmed paths lift average daily Actual trades toward 5/day without degrading NetR / NetDollars.

From `OPF_RESEARCH_0.95`, Actual execution keeps the same mainline objective and fixes the v0.94 structure-confirmation bottleneck:

- Actual defaults now use `ACTUAL_EXEC_1.35`.
- `StructureConfirmShadow_ConfirmBarStop_Min10` uses the same fixed `ActualTargetR=1.5` reward model for Actual RR checks as the executed TP/SL bracket, instead of `NearestStructure`.
- `StructureConfirmShadow_ConfirmBarStop_Wait1` is added to the default Actual execution path list as an existing structure-confirmation variant.
- The version does not widen `ObservationConfirm` risk, does not add a new setup family, does not add time-of-day filtering, and does not change target R.
- The expected validation question is whether the structure-confirmation family now contributes actual trades and lifts average daily Actual trades toward 5/day while keeping abnormal execution count at zero.

From `OPF_RESEARCH_0.96`, Actual execution pushes harder on the current hard volume gate before risk-control and profit optimization:

- Actual defaults now use `ACTUAL_EXEC_1.36`.
- `StructureConfirmShadow_ConfirmBarStop` and `StructureConfirmShadow_SwingStop` are added to the default Actual execution path list, alongside the existing `Min10` and `Wait1` structure-confirmation variants.
- All `StructureConfirmShadow_*` Actual RR checks use fixed `ActualTargetR=1.5`, matching the actual TP/SL bracket instead of `NearestStructure`.
- `ObservationConfirm` low-volume filler is widened from `52 <= SetupQualityScore < 56` to `50 <= SetupQualityScore < 56`.
- `ActualObservationConfirmMaxFillerTradesPerDay` is raised from 2 to 3, still only while today's Actual count is below `ActualObservationConfirmFillerUntilDailyTrades=5`.
- `ObservationConfirm` risk remains capped at 11 points, no time-of-day filter is added, no new setup family is added, and target R remains unchanged.
- The expected validation question is whether the batch now clears the 5 Actual trades/day gate with acceptable structure/filler attribution and zero abnormal execution.

From `OPF_RESEARCH_0.97`, Actual execution tests a controlled low-volume day filler:

- Actual defaults now use `ACTUAL_EXEC_1.37`.
- `ObservationConfirm` filler widens from `50 <= SetupQualityScore < 56` to `48 <= SetupQualityScore < 56`.
- The filler still runs only while today's Actual count is below `ActualObservationConfirmFillerUntilDailyTrades=5`.
- `ActualObservationConfirmMaxFillerTradesPerDay` stays at 3, so this version does not raise the daily filler cap.
- Filler executions with `SetupQualityScore < 50` are marked with `DailyVolumeFiller48` in `execution_decisions.csv`.
- `ObservationConfirm` risk remains capped at 11 points, target R remains 1.5, no time-of-day filter is added, and no new setup family is added.
- The expected validation question is whether 48-49 score filler helps low-trade days approach the 5-trade gate without becoming a negative-R subgroup.

From `OPF_RESEARCH_0.98`, Actual execution keeps the v0.97 trading rules and tightens strategy-stop cleanup:

- Actual defaults now use `ACTUAL_EXEC_1.38`.
- No Actual path, risk cap, score threshold, target R, time policy, or setup family is changed.
- Protection-order cleanup now has an in-progress guard so `StrategyStopped` and `OnOrderChanged` do not run overlapping cleanup attempts for the same trade.
- The first post-cancel check that still sees working protection orders is logged as `PROTECTION_CLEANUP_RETRY_PENDING` instead of immediately classifying it as stale.
- `PROTECTION_STALE_AFTER_EXIT` is reserved for repeated cleanup attempts that still cannot clear working protection orders.
- The expected validation question is whether strategy-stop exits no longer produce duplicate cleanup / stale protection events while the v0.97 volume results remain comparable.

From `OPF_RESEARCH_0.99`, Actual execution slightly relaxes low-trade-day volume limits without changing setup families:

- Actual defaults now use `ACTUAL_EXEC_1.39`.
- `ActualObservationConfirmVolumeMaxRiskPoints` is raised from 11 to 12.
- This only applies to `ObservationConfirm` while today's Actual count is below `ActualObservationConfirmFillerUntilDailyTrades=5`.
- It only applies when `SetupQualityScore >= ActualObservationConfirmMinSetupQualityScore=56`; lower-score filler signals still use the base 11-point risk cap.
- Executions using the expanded risk band are marked with `ObservationRiskExpansion` in `execution_decisions.csv`.
- Same-bar ambiguity checks, target R, time policy, and setup families are unchanged.
- The expected validation question is whether the 11-12 point high-quality `ObservationConfirm` band helps low-trade days reach the 5-trade gate without becoming a negative-R subgroup.

From `OPF_RESEARCH_1.00`, Actual execution creates a stable baseline candidate after the failed v0.99 risk-band test:

- Actual defaults now use `ACTUAL_EXEC_1.40`.
- `ActualObservationConfirmVolumeMaxRiskPoints` is set back from 12 to 11, disabling the v0.99 `ObservationRiskExpansion` band.
- The v0.97 `DailyVolumeFiller48` rule is retained.
- The v0.98 strategy-stop protection cleanup fix is retained.
- Actual paths, base risk caps, score thresholds, target R, time policy, and setup families are otherwise unchanged.
- The expected validation question is whether this baseline preserves the v0.97 volume/edge improvement while keeping the v0.98 execution cleanup behavior clean.

From `OPF_RESEARCH_1.01`, Actual execution runs a bolder single-day volume-gate experiment:

- Actual defaults now use `ACTUAL_EXEC_1.41`.
- `ObservationConfirm` filler widens from `48 <= SetupQualityScore < 56` to `42 <= SetupQualityScore < 56`.
- `ActualObservationConfirmMaxFillerTradesPerDay` is raised from 3 to 5, still only while today's Actual count is below 5.
- Filler executions with `SetupQualityScore < 48` are marked with `DailyVolumeFiller42`; `SetupQualityScore < 50` rows still include `DailyVolumeFiller48`.
- Same-bar checks remain strict for any bar that touches the stop. Only `ObservationConfirm` rows on low-trade days with target touched and stop not touched can execute, and these rows are marked `DailyVolumeSameBarTargetOnly`.
- The 11-point `ObservationConfirm` risk cap, target R, time policy, and setup families are unchanged.
- The expected validation question is whether each active trading day can reach at least 5 Actual trades, then whether `DailyVolumeFiller42` and `DailyVolumeSameBarTargetOnly` are acceptable or should be removed during the next quality phase.

From `OPF_RESEARCH_1.02`, Actual execution shifts the volume experiment back toward mainline research paths:

- Actual defaults now use `ACTUAL_EXEC_1.42`.
- `ObservationConfirm` filler is pulled back to `48 <= SetupQualityScore < 56`, with the daily filler cap back at 3.
- The v1.01 `DailyVolumeFiller42` and same-bar target-only relaxation are disabled because they added trades but were negative-R subgroups.
- Low-trade days can execute existing research paths `AlmostConfirmed`, `StructureConfirmShadow`, and `ZoneBirthResearch` only while today's Actual count is below 5.
- These research fillers still require the normal trend / quality gate, `SetupQualityScore >= 70`, `EstimatedRR >= 1`, same-bar stop safety, and `risk <= 11` points.
- Executed rows are marked with `DailyVolumeResearchFiller:<path>` so the next replay can decide whether this is a viable volume source or should be removed.
- The expected validation question is whether every active trading day reaches at least 5 Actual trades without `DailyVolumeResearchFiller` becoming a materially negative subgroup.

From `OPF_RESEARCH_1.03`, Actual execution returns the volume experiment to the core `ObservationConfirm` path:

- Actual defaults now use `ACTUAL_EXEC_1.43`.
- The v1.02 research filler paths `AlmostConfirmed`, `StructureConfirmShadow`, and `ZoneBirthResearch` are removed from Actual execution because they did not solve the daily 5-trade gate.
- `ActualObservationConfirmVolumeMaxRiskPoints` is raised to 16, only while today's Actual count is below 5 and only for `ObservationConfirm` rows with `SetupQualityScore >= 56`.
- Executed rows in the 11-16 point risk band include both `ObservationRiskExpansion` and `DailyVolumeRisk16`.
- `DailyVolumeFiller48` remains active, but `DailyVolumeFiller42` and same-bar target-only execution remain disabled.
- Actual fills now store the pre-trade maximum allowed risk. After entry fill and bracket reprice, if filled risk exceeds that saved limit, the strategy writes `ENTRY_FILLED_RISK_EXCEEDED` and sends an emergency flatten instead of submitting TP/SL.
- The expected validation question is whether every active trading day reaches at least 5 Actual trades, whether `DailyVolumeRisk16` is acceptable by NetR/NetDollars, and whether no filled trade exceeds its saved risk cap without flatten protection.

From `OPF_RESEARCH_1.04`, Actual execution makes a bolder low-volume-day floor test while staying inside existing setup families:

- Actual defaults now use `ACTUAL_EXEC_1.44`.
- The v1.03 `DailyVolumeRisk16` experiment is retired; `ActualObservationConfirmVolumeMaxRiskPoints` is set back to 11.
- `ObservationConfirm_WideStop1_5R` is added to `ActualExecutionPaths`.
- While today's Actual count is below `ActualObservationConfirmFillerUntilDailyTrades=5`, `ObservationConfirm_WideStop1_5R` and `FailureReverse_RetestFailed_WideStop1_5R` can execute as `DailyVolumeFloor` paths.
- `DailyVolumeFloor` requires `SetupQualityScore >= 48`, `risk <= 18`, and `EstimatedRR >= 0.5`. It may bypass the normal trend-regime gate and volatility-adjusted risk cap, but it still respects the instrument hard risk cap and same-bar safety checks.
- Executed floor rows are marked with `DailyVolumeFloor:<path>` in `execution_decisions.csv`.
- The expected validation question is whether every active trading day reaches at least 5 Actual trades, then which `DailyVolumeFloor` subgroup should be kept or removed by NetR, NetDollars, TP/SL count, and side.

From `OPF_RESEARCH_1.05`, Actual execution redirects low-volume expansion from wide-stop paths to the base `ObservationConfirm` risk band:

- Actual defaults now use `ACTUAL_EXEC_1.45`.
- `ActualObservationConfirmVolumeMaxRiskPoints` is raised to 18 only for low-volume-day `ObservationConfirm` rows before today's Actual count reaches 5.
- The risk-band gate uses `SetupQualityScore >= ActualObservationConfirmFillerMinSetupQualityScore=48`, `risk <= 18`, and the normal `EstimatedRR >= 1.0`.
- This low-volume risk band bypasses the volatility-adjusted risk cap but still respects the instrument hard risk cap and same-bar safety checks.
- Executed rows in the 11-18 point base Observation band include `ObservationRiskExpansion` and `DailyVolumeBaseRisk18`.
- The v1.04 `DailyVolumeFloor` wide-stop paths remain labeled separately, but the main validation question is whether `DailyVolumeBaseRisk18` gets 2026-03-23 and 2026-03-24 to at least 5 Actual trades without becoming worse than the existing Observation filler.
- `OPF_RESEARCH_1.05` / `ACTUAL_EXEC_1.45` is the volume baseline. It is the rollback target after the first 12-trading-day batch produced 85 Actual trades, +20.29R, and +$579.5 with acceptable average daily trade count. Later versions should compare against this baseline before keeping quality or risk-control changes.

From `OPF_RESEARCH_1.10`, Actual execution starts the quality-optimization phase while preserving the v1.05 volume baseline as the rollback target:

- Actual defaults now use `ACTUAL_EXEC_1.46`.
- Broad `BreakawayFvg` Actual execution is disabled after the May/June evidence batch showed the executed broad subgroup was negative. Research rows still continue, and `BreakawayFvg_Qualified` is not disabled.
- Low-volume-day Long `ObservationConfirm` executions in the 11-18 point expanded risk band now require `SetupQualityScore >= 70`. Skipped rows are marked `LongObservationRiskExpansionQualityCutV110`.
- Short `ObservationConfirm` and all base-risk `ObservationConfirm` rows keep the v1.05 rules. The 15-18 point risk band is not removed globally because it remained the strongest positive risk bucket across reviewed batches.
- Actual-verified research outcome de-duplication now uses `SignalID + ResearchPath + EntryBar + ActualTradeID`, preventing a completed Actual trade from also being written again at a later research-window exit bar.
- The expected validation question is whether v1.10 reduces `SL_no_MFE`, especially on Long expanded-risk ObservationConfirm entries, while keeping full-day average Actual trades near or above 5.

From `OPF_RESEARCH_1.11`, Actual execution tightens the v1.10 quality experiment without changing setup families:

- Actual defaults now use `ACTUAL_EXEC_1.47`.
- Long `ObservationConfirm` is no longer allowed to use the low-volume-day 11-18 point expanded risk band. Skipped rows are marked `LongObservationRiskExpansionDisabledV111`.
- Short `ObservationConfirm` can still use the v1.05 11-18 point expanded risk band because it remained positive in the v1.10 evidence batch.
- Base-risk Long `ObservationConfirm` rows keep the v1.05/v1.10 rules, so this does not disable Long trading globally.
- Actual-verified research outcome duplicate protection now keys directly on `ActualTradeID`, covering cases where the same Actual trade is associated with adjacent research tracker entry bars.
- The expected validation question is whether v1.11 keeps average Actual trades near or above 5/day while removing the negative Long `DailyVolumeBaseRisk18` subgroup and reducing duplicate Actual-verified rows to zero.

From `OPF_RESEARCH_1.12`, Actual execution keeps the v1.11 setup set and tests one Short quality-control variable:

- Actual defaults now use `ACTUAL_EXEC_1.48`.
- Short `ObservationConfirm` rows with `8 < risk <= 15` require `SetupQualityScore >= 60`. Skipped rows are marked `ShortObservationMidRiskQualityCutV112`.
- Short `15-18` expanded-risk `ObservationConfirm` remains allowed because the v1.11 evidence batch showed that bucket was positive.
- Long rules remain unchanged from v1.11.
- The expected validation question is whether v1.12 reduces Short `SL_no_MFE` and improves Short NetR/NetDollars without dropping full-day average Actual trades below the 5/day baseline.

From `OPF_RESEARCH_1.13`, Actual execution keeps the v1.12 quality candidate baseline and expands profit-extension research only:

- Actual defaults now use `ACTUAL_EXEC_1.49`; entry rules, risk filters, path whitelist, SL, and live/replay TP remain unchanged.
- `research_outcomes.csv` adds `Hit2_5R`, `Hit3R`, `First2_5RBar`, and `First3RBar`.
- `exit_policy_evaluations.csv` adds a `Fixed3R` policy row alongside `Fixed1_5R`, `Fixed2R`, and `Fixed2_5R`.
- The expected validation question is whether v1.12-quality entries have enough post-1.5R extension to justify later TP tiering, especially by side, path, and risk bucket.

From `OPF_RESEARCH_1.14`, Actual execution starts a controlled profit-capture test without changing entry rules:

- Actual defaults now use `ACTUAL_EXEC_1.50`.
- Base `ActualTargetR` remains `1.5R` for all paths unless explicitly overridden by this version.
- Long `ObservationConfirm` rows with `11 < InitialRiskPoints <= 15` use an Actual target of `3R`.
- Long `ObservationConfirm_WideStop1_5R` rows with `11 < InitialRiskPoints <= 15` use an Actual target of `2.5R`.
- Short rows, Long rows outside the 11-15 risk bucket, path whitelist, SL logic, risk filters, setup families, and time policy are unchanged.
- Executed rows using this override include `LongProfitExtensionV114` in `execution_decisions.csv`; `execution_trades.csv` records the per-trade `TargetR`.
- The expected validation question is whether Long profit extension increases NetR and NetDollars without materially increasing abnormal exits or reducing the average Actual trade count near 5/day.

From `OPF_RESEARCH_1.15`, Actual execution keeps the v1.14 entry set and fixes profit-extension measurement:

- Actual defaults now use `ACTUAL_EXEC_1.51`.
- The final Actual `TargetR` is selected after entry fill / bracket reprice using filled risk, so the executed TP bucket matches `FilledRiskPoints` rather than only the pre-fill planned risk.
- `execution_trades.csv` adds `PlannedTargetR` and `TargetRDrift` so fills that move a trade into or out of a profit-extension bucket are auditable.
- `exit_policy_evaluations.csv` adds protected-extension research rows: `ProtectBE_Then2_5R`, `Protect1R_Then2_5R`, `ProtectBE_Then3R`, and `Protect1R_Then3R`.
- Protected-extension research is diagnostic only: actual orders are still bracket orders, and no live/replay stop movement is submitted by this version.
- Protection is modeled conservatively: after a signal first reaches `1.5R`, the BE or 1R protected stop can trigger only from the next bar onward.
- The expected validation question is whether protected extension keeps most of the 1.5R profit while preserving enough upside to improve NetR / NetDollars versus fixed 1.5R and bare 2.5R/3R targets.

From `OPF_RESEARCH_1.16`, Actual execution keeps the v1.15 entry/TP rules and fixes the stop/replay boundary audit:

- Actual defaults now use `ACTUAL_EXEC_1.52`.
- When the strategy is stopping, `OnStopped()` marks Actual execution as closing before processing the last bar. Research still flushes, but new Actual entries from that final bar are skipped with `StrategyStopping` / `SKIP_STRATEGY_STOPPING`.
- `execution_events.csv` now records `ORDER_STATE_FAILED` and `CANCEL_FAIL` when ATAS reports failed/rejected protection orders or cancel failures, so the readiness gate no longer misses failures that previously only appeared in `research.log`.
- The expected validation question is whether a 6/18-focused replay ends with `ExecutedDecisions = ExecutionTrades = ActualVerifiedUniqueTrades`, every Actual exit has `PROTECTION_CLEANUP_DONE`, and there are no unclosed `ACTIVE_ON_STOP`, `CANCEL_FAIL`, `ORDER_STATE_FAILED`, `STALE`, `REJECT`, or `FAILED` audit events.

## Full Backtest Readiness Gate

Before moving from smoke replay to broad backtest/tuning, the latest 3-day smoke batch should satisfy:

1. All snapshots use the same `ResearchSchemaVersion` and `ActualExecutionSettings.Version`.
2. Core CSV files are present: config snapshot, signals, research outcomes, risk evaluations, score breakdown, execution events, and execution trades.
3. `ExecutedDecisions = ExecutionTrades = ActualVerifiedUniqueTrades`, with no duplicate Actual-verified rows.
4. Every Actual exit has `PROTECTION_CLEANUP_DONE`, with no `STALE`, `REJECT`, `CANCEL_FAIL`, or `FAILED` events.
5. The tested configuration is frozen: MNQ, one contract, default `ActualTargetR=1.5` with any documented per-version TP overrides, daily target/loss stops disabled for evidence accumulation, max-trades safety ceiling, and Actual path whitelist.
6. Full-day average Actual trades should stay near 5/day; individual low-trade days are acceptable only when profitability improves and the missed volume is explainable by research/skipped-signal evidence.
7. Results are explainable through `MFE_R`, `MAE_R`, `ExitEfficiency`, `RunupCapturePct`, and no-trade reasons, even if the batch is not yet strongly profitable.

## Research workflow

Before each replay batch, clear OPF output logs:

```powershell
powershell -ExecutionPolicy Bypass -File "C:\Users\Administrator\source\repos\NQOrderFlowV10629\NQOrderFlowV1\OPFStrategyV1\Scripts\Clear-OPFLogs.ps1"
```

After replay, summarize research outcomes:

```powershell
powershell -ExecutionPolicy Bypass -File "C:\Users\Administrator\source\repos\NQOrderFlowV10629\NQOrderFlowV1\OPFStrategyV1\Scripts\Summarize-OPFResearch.ps1"
```

The summary groups outcomes by `ResearchPath`, `OutcomeClass`, `StopBasis`, entry date, snapshot, regime changes, entry-date regime quality, and zone metadata. This is the first pass before inspecting individual signals.

The summary also checks schema/profile consistency and writes `replay_index.csv` into the OPF log directory. Use that index to locate a signal by `SignalID`, `EntryTime`, `EntryBar`, path, zone, and regime context.

The summary also prints signal-stage, no-trade, candidate-evaluation, and confirmation-evaluation funnels, which are useful before tuning individual paths.

Recommended review order after each replay batch:

1. Check `Data Quality`; missing signal metadata or missing regime daily rows means the batch is not reliable enough for strategy conclusions.
2. Check `Schema And Profile Consistency`; mixed schemas or fallback profiles mean the batch should not be used for strategy conclusions.
3. Check `Signal Stage Funnel`, `No Trade Reasons`, and candidate/confirmation funnels to see where opportunities are being blocked.
4. Check `By Entry Date, Regime, And Path`; this links path performance to daily `UnknownPct` and regime churn.
5. Check `By Zone Type And Path`; this shows whether a path only works on specific zone types or freshness states.
6. Use the base path summary only after the context tables look clean.

`Summarize-OPFResearch.ps1` defaults to excluding `2026-04-16` because that ATAS replay day produced stale / abnormal order fill prices that were confirmed outside the strategy logic. The script excludes the whole snapshot that contains the excluded date, not only individual calendar-date rows, so overnight rows from that replay session do not leak into evidence accumulation. Override with `-ExcludeDates @()` only when deliberately auditing that abnormal replay session.

The execution summary reports daily `Trades`, `Normal`, `Abnormal`, `Long`, `Short`, `TP`, `SL`, `Other`, `NetR`, and `NetDollars`. Use this table as the first volume gate before tuning entry quality.
