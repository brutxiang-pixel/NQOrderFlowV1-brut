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
- `MNQ_2Contract_Evidence`
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

From `OPF_RESEARCH_1.17`, Actual execution keeps the v1.15/v1.16 entry/TP rules and tightens replay stop safety:

- Actual defaults now use `ACTUAL_EXEC_1.53`.
- New Actual entries are skipped with `ReplayStopGuard` / `SKIP_REPLAY_STOP_GUARD` during the expected replay stop window: 20:40 or later, and 16:40 or later on Friday/early-close style replay days.
- `OnStopped()` now retries protection cleanup up to three times before giving up, so stop-time cleanup does not depend only on a later `OnOrderChanged` callback.
- The expected validation question is whether June replay no longer produces `ACTIVE_ON_STOP` / `STOPPED` trades while preserving the rest of v1.15/v1.16 behavior.

From `OPF_RESEARCH_1.18`, Actual execution returns to the main volume/profitability track without adding new setup families:

- Actual defaults now use `ACTUAL_EXEC_1.54`.
- A low-volume-day `ObservationConfirm` quality rescue can execute only before the day reaches 5 Actual trades, with `SetupQualityScore >= 70`, risk `> 18` and `<= 22` points, and `EstimatedRR >= 0.8`.
- Executed rescue rows include `DailyVolumeQualityRescueV118` in `execution_decisions.csv` so the next replay can compare this subgroup against base `ObservationConfirm`, `DailyVolumeBaseRisk18`, and wide-stop fillers.
- The instrument hard risk cap is not raised, no new setup type is enabled, and TP behavior remains unchanged from v1.15-v1.17.
- The expected validation question is whether low-trade days gain enough extra high-quality Actual trades to move the June average closer to 5/day without making losing days such as 6/3, 6/17, 6/18, and 6/22 materially worse.

From `OPF_RESEARCH_1.19`, Actual execution keeps the v1.18 mainline volume/profitability track and makes one controlled low-volume expansion:

- Actual defaults now use `ACTUAL_EXEC_1.55`.
- The low-volume-day `ObservationConfirm` quality rescue remains enabled, but its max planned risk is tightened from `22` to `21.5` points to reduce entry-fill risk drift and avoid `ENTRY_FILLED_RISK_EXCEEDED` emergency flatten events.
- `FailureReverse_ObservationInvalidated_WideStop1_5R` is added as an Actual-eligible low-volume-day filler only before the day reaches 5 Actual trades, using the existing `DailyVolumeFloor` rules: `SetupQualityScore >= 48`, risk `<= 18`, and the relaxed floor RR check.
- Long immediate-failure entries remain disabled for both the normal and wide-stop invalidated paths. The v1.19 filler is therefore primarily a controlled Short failure supplement, not a new setup family.
- Executed wide invalidated filler rows include `FailureInvalidatedWideFillerV119` in `execution_decisions.csv`; quality rescue rows now include `DailyVolumeQualityRescueV119`.
- TP behavior, SL behavior, instrument hard risk cap, and broad `BreakawayFvg` disablement are unchanged.
- The expected validation question is whether June-like low-trade days move closer to 5 Actual trades/day without giving back the v1.18 rescue profit contribution or increasing abnormal flatten events.

From `OPF_RESEARCH_1.20`, Actual execution resets the volume experiment around the v1.15 stable skeleton and removes the ineffective v1.19 Failure wide filler:

- Actual defaults now use `ACTUAL_EXEC_1.56`.
- `FailureReverse_ObservationInvalidated_WideStop1_5R` is removed from Actual execution again after v1.19 produced zero executed trades from that path.
- `DailyVolumeQualityRescue` returns to the v1.18 planned-risk cap of `22` points and is retagged as `DailyVolumeQualityRescueV120`.
- Entry-fill risk handling separates planned risk from filled risk: if planned risk was within the path cap and the filled risk exceeds the cap by no more than `1` point, the strategy submits normal bracket protection and logs `ENTRY_FILLED_RISK_DRIFT_ACCEPTED` / `RiskDriftAcceptedV120` instead of emergency flattening.
- `AlmostConfirmed` and `ShadowCandidate` are added as Actual low-volume fillers only before the day reaches 5 Actual trades, with `SetupQualityScore >= 60`, risk `<= 18`, and `EstimatedRR >= 0.8`.
- Executed filler rows are tagged as `AlmostConfirmedFillerV120` or `ShadowCandidateFillerV120` in `execution_decisions.csv`.
- The expected validation question is whether the June replay can reach roughly `90+` trades, improve low-trade days toward the 5-trade target, and keep NetR near or above the v1.18 result while reducing emergency flatten events.

From `OPF_RESEARCH_1.21`, Actual execution makes the v1.20 volume experiment more aggressive so the project can collect enough samples before later tightening:

- Actual defaults now use `ACTUAL_EXEC_1.57`.
- `AlmostConfirmed` and `ShadowCandidate` are no longer limited to days with fewer than 5 Actual trades. They can execute throughout the session while still respecting `ActualMaxTradesPerDay`.
- The core guardrails remain: `SetupQualityScore >= 60`, risk `<= 18`, and `EstimatedRR >= 0.8`.
- Executed rows are retagged as `AlmostConfirmedFillerV121` or `ShadowCandidateFillerV121`.
- `DailyVolumeQualityRescueV120`, v1.15 entry/TP behavior, v1.16/v1.17 execution safety, and the `RiskDriftAcceptedV120` filled-risk tolerance remain unchanged.
- The expected validation question is whether this broader release can materially lift June trade count beyond the v1.20 target while keeping NetR positive enough to identify which expansion path deserves later tightening.

From `OPF_RESEARCH_1.22`, Actual execution becomes a deliberate volume-discovery build targeting roughly 200 June trades before the next quality-tightening phase:

- Actual defaults now use `ACTUAL_EXEC_1.58`.
- `ActualMaxTradesPerDay` is set to `12`, making it an experiment cap for the June 200-trade target rather than the older broad safety ceiling of 20.
- `ZoneBirthResearch` is added to Actual execution with v1.22 guardrails: `SetupQualityScore >= 40`, risk `<= 25`, and `EstimatedRR >= 0.3`.
- `ObservationStrict_BullFresh` and `ObservationStrict_Other` are added to Actual execution with v1.22 guardrails: `SetupQualityScore >= 45`, risk `<= 25`, and `EstimatedRR >= 0.5`.
- These v1.22 volume paths bypass the trend-regime hard gate and ATR-adjusted risk gate, but still respect the instrument hard risk cap, active-trade protection, same-bar ambiguity guard, replay stop guard, and one-contract execution profile.
- `ShadowCandidate` remains enabled and is tagged `ShadowCandidateFillerV122`; `AlmostConfirmed` is disabled for Actual execution and skipped with `AlmostConfirmedActualDisabledV122`.
- Executed v1.22 expansion rows include `ZoneBirthVolumeV122` or `StrictObservationVolumeV122` in `execution_decisions.csv`.
- The expected validation question is no longer whether every day reaches exactly five trades. It is whether the full June replay reaches about 200 trades, which expansion path supplies the volume, and whether `ZoneBirthVolumeV122`, `StrictObservationVolumeV122`, and `ShadowCandidateFillerV122` should be retained, tightened, or removed by NetR, NetDollars, side, risk bucket, and daily distribution.

From `OPF_RESEARCH_1.23`, Actual execution replaces the v1.22 bad volume pool instead of simply shrinking trade count:

- Actual defaults now use `ACTUAL_EXEC_1.59`.
- The v1.23 path choice is based on the prior June research estimate before coding: removing `ObservationStrict_BullFresh` and `ZoneBirthResearch` Short would leave roughly 99 Actual trades; `BreakawayFvg` under `risk <= 25`, `SetupQualityScore >= 80`, `EstimatedRR >= 1.0` estimated about 37 additional candidates; `ObservationConfirm` Long under `risk <= 18`, `SetupQualityScore >= 56`, `EstimatedRR >= 0.8` estimated about 87 additional candidates.
- `ObservationStrict_BullFresh` is disabled for Actual execution and skipped with `StrictBullFreshActualDisabledV123`.
- `ZoneBirthResearch` Short is disabled for Actual execution and skipped with `ZoneBirthShortActualDisabledV123`; `ZoneBirthResearch` Long remains enabled under the v1.22 guardrails.
- `BreakawayFvg` can execute as `BreakawayVolumeV123` with `SetupQualityScore >= 80`, risk `<= 25`, and `EstimatedRR >= 1.0`; it bypasses the old broad-breakaway disable and the old 11-point breakaway cap, but not the instrument hard risk cap.
- `ObservationConfirm` Long can execute as `ObservationLongVolumeV123` in the 11-18 point risk band with `SetupQualityScore >= 56` and `EstimatedRR >= 0.8`; this bypasses the v1.11 Long risk-expansion disable only for this tagged v1.23 experiment.
- `ActualMaxTradesPerDay` remains `12` so the experiment can approach 200 June trades without removing the replay safety ceiling.
- The expected validation question is whether replacing the bad `StrictBullFresh` / `ZoneBirth Short` volume with `BreakawayVolumeV123` and `ObservationLongVolumeV123` reaches the 180-220 trade range while keeping NetR and NetDollars positive.

From `OPF_RESEARCH_1.24`, Actual execution keeps the June 200-trade mainline target and shifts from raw expansion to targeted volume replacement:

- Actual defaults now use `ACTUAL_EXEC_1.60`.
- `BreakawayVolumeV123` remains enabled and its experiment risk cap is raised from `25` to `30` points, keeping `SetupQualityScore >= 80` and `EstimatedRR >= 1.0`.
- `ObservationLongVolumeV123` is tightened from `SetupQualityScore >= 56` to `>= 70`, because the v1.23 June replay showed the lower-score Long expansion was the main losing pool while `Score >= 70` remained positive.
- `ZoneBirthVolumeV122` Long remains enabled, but its minimum setup quality is raised from `40` to `45` to remove the weakest v1.23 score bucket. `ZoneBirthResearch` Short remains disabled with `ZoneBirthShortActualDisabledV123`.
- `UnknownRegimeZoneTouch` is added as `UnknownMicroRiskVolumeV124` with `SetupQualityScore >= 25`, risk `<= 8.5`, and `EstimatedRR >= 1.5`; it bypasses the trend-regime hard gate only for this micro-risk volume experiment.
- `ObservationConfirm` Short is added as `ObservationShortVolumeV124` in the 11-18 point risk band with `SetupQualityScore >= 60` and `EstimatedRR >= 1.0`; this path is intended to add controlled Short volume, not to change the core setup family.
- The pre-change June research estimate for v1.24 was roughly: remove 22 low-score Observation Long trades and 15 low-score ZoneBirth trades, add about 17 wider Breakaway opportunities, about 49 Unknown micro-risk opportunities, and about 36 Observation Short volume opportunities, targeting roughly 200 Actual trades while keeping each new pool independently tagged.
- The expected validation question is whether v1.24 reaches about 180-220 June trades, keeps NetR/NetDollars positive, and whether `UnknownMicroRiskVolumeV124` and `ObservationShortVolumeV124` are good enough to retain after their first full-month evidence batch.

From `OPF_RESEARCH_1.25`, Actual execution keeps the profitable v1.24 skeleton and fixes the main volume path that did not actually execute:

- Actual defaults now use `ACTUAL_EXEC_1.61`.
- `UnknownRegimeZoneTouch` executions are retagged as `UnknownMicroRiskVolumeV125`.
- The Unknown micro-risk pool is tightened from `SetupQualityScore >= 25` to `>= 35` while keeping risk `<= 8.5`.
- `UnknownMicroRiskVolumeV125` now evaluates reward with the actual fixed target model, `1.5R`, instead of the research-only nearest-structure reward model. This aligns RR filtering with the order that will actually be sent.
- Stop-time executions with no confirmed entry fill no longer write synthetic `STOPPED_NO_ENTRY` rows into `execution_trades.csv`; they remain audit events only, so trade count and Other exits represent actual filled trades.
- `BreakawayVolumeV123`, `ObservationLongVolumeV123` at `Score >= 70`, `ZoneBirthVolumeV122` at `Score >= 45`, and `ObservationShortVolumeV124` are otherwise unchanged.
- The expected validation question is whether v1.25 restores meaningful volume from the Unknown micro-risk pool without giving back the v1.24 profit improvement, and whether no-entry stop events disappear from the trade statistics.

From `OPF_RESEARCH_1.26`, Actual execution keeps the v1.25 profitable skeleton and makes the Unknown micro-risk test measurable in a full-month replay:

- Actual defaults now use `ACTUAL_EXEC_1.62`.
- `UnknownRegimeZoneTouch` executions are retagged as `UnknownMicroRiskVolumeV126`.
- The Unknown micro-risk pool is tightened to `SetupQualityScore >= 45` and keeps risk `<= 8.5`, because the v1.25 June replay showed the lower-score Unknown candidates were noisy while the small-risk pool was the intended volume source.
- `UnknownMicroRiskVolumeV126` uses the exact fixed `1.5R` target distance for Actual RR checks instead of rounding reward points before division. This removes the `EstimatedRR=1.499x` boundary skip that prevented otherwise valid `1.5R` micro-risk candidates from executing.
- No new setup family is added. `BreakawayVolumeV123`, `ObservationLongVolumeV123`, `ObservationShortVolumeV124`, and `ZoneBirthVolumeV122` remain unchanged so the next monthly replay can isolate whether the Unknown micro-risk fix adds useful volume.
- The expected validation questions are: how many `UnknownMicroRiskVolumeV126` trades execute, their Long/Short split, TP/SL, NetR and NetDollars; whether total monthly trades move meaningfully above v1.25's 95 trades; whether adjusted NetR remains positive after abnormal raw-fill accounting; and whether lifecycle integrity remains `ExecutedDecisions = ExecutionTrades` with no stale protection or no-entry synthetic trade rows.

From `OPF_RESEARCH_1.27`, Actual execution shifts the volume/profit experiment back to the strongest proven pool, Breakaway:

- Actual defaults now use `ACTUAL_EXEC_1.63`.
- `BreakawayFvg` executions are retagged as `BreakawayVolumeV127`.
- `BreakawayVolumeV127` keeps the high-quality gate at `SetupQualityScore >= 80` and `EstimatedRR >= 1.0`, but explicitly allows the high-score Breakaway risk band up to `30` points. This is the only path allowed to bypass the instrument `25` point hard-risk guard in this version, and it still has its own `risk <= 30` cap.
- The v1.26 June replay estimated roughly eight additional `25 < risk <= 30` Breakaway candidates. Their research profile was mixed but promising enough for a controlled full-month Actual test: 3 Excellent, 2 Good, and 3 Catastrophic.
- Same-bar target-touched Breakaway rows remain skipped. Although some were profitable in research, they are not enabled because the target may have been touched before the close-based entry could actually exist.
- `UnknownRegimeZoneTouch` Actual execution is disabled with `UnknownMicroRiskActualDisabledV127` after v1.26 showed that its small-risk candidates were almost entirely same-bar ambiguous. It remains available in research logs.
- The expected validation questions are: whether `BreakawayVolumeV127` raises total monthly trades from v1.26's 121 toward the next volume target; whether the added `25-30` risk band is positive by NetR, NetDollars, TP/SL, side, and day; whether the wider Breakaway risk increases abnormal fills; and whether disabling Unknown Actual reduces noisy skip analysis without reducing real trades.

From `OPF_RESEARCH_1.28`, Actual execution keeps the Breakaway expansion on the mainline but splits the wide-risk experiment by side:

- Actual defaults now use `ACTUAL_EXEC_1.64`.
- `BreakawayFvg` executions are retagged as `BreakawayVolumeV128`, except Short entries with `25 < risk <= 30`, which are tagged as `BreakawayShortWideV128` for separate attribution.
- Long Breakaway risk is capped back at `25` points after the v1.27 June replay showed `Long 25-30` was negative. Short Breakaway keeps the `30` point experimental cap because the same replay showed a small positive sample in that band.
- Breakaway no longer accepts entry-fill risk drift above its side-specific cap. A planned Breakaway at the cap that fills beyond the cap should log `ENTRY_FILLED_RISK_EXCEEDED` and flatten instead of writing a hidden over-cap trade.
- No new setup family is added. This version is a controlled cleanup of the v1.27 Breakaway experiment, not a new entry model.
- The expected validation questions are: whether monthly trade count remains acceptable after removing `Long 25-30`; whether `BreakawayShortWideV128` is positive by NetR, NetDollars, TP/SL, and day; whether there are zero Breakaway trades above their side-specific cap; whether abnormal execution count stays low; and whether the total result improves versus the v1.27 raw-fill baseline of `125` trades, `+25.99R`, and `+$866.5`.

From `OPF_RESEARCH_1.29`, Actual execution keeps the v1.28 Breakaway structure and removes a proven drag from Actual orders:

- Actual defaults now use `ACTUAL_EXEC_1.65`.
- `ObservationConfirm_WideStop1_5R` is removed from the default `ActualExecutionPaths`.
- If an older runtime config still enables `ObservationConfirm_WideStop1_5R`, the strategy skips it with `ObservationConfirmWideStopActualDisabledV129`.
- The path remains research-only, so monthly evidence can still compare its hypothetical outcome against actual trading without spending real/replay orders on it.
- No Breakaway rule is changed from v1.28. `BreakawayVolumeV128`, `BreakawayShortWideV128`, and the side-specific risk caps remain the main validation path.
- The expected validation questions are: whether removing the v1.28 `ObservationConfirm_WideStop1_5R` drag of `7` trades, `-1.80R`, and `-$43` improves total NetR/NetDollars; whether average daily trade count remains acceptable near 5/day; whether Breakaway remains the largest positive contributor; whether lifecycle integrity remains complete; and whether abnormal execution count does not increase.

From `OPF_RESEARCH_1.30`, Actual execution becomes a deliberate volume-discovery build with a hard monthly target near 200 trades:

- Actual defaults now use `ACTUAL_EXEC_1.66`.
- `AlmostConfirmed` is added back to `ActualExecutionPaths` and executes as `AlmostConfirmedVolumeV130` under the existing mainline filler guardrails: `SetupQualityScore >= 60`, risk `<= 18`, and `EstimatedRR >= 0.8`.
- `ObservationStrict_BullFresh` is re-enabled for Actual execution and tagged as `StrictBullFreshVolumeV130`. It uses the strict-volume guardrails: `SetupQualityScore >= 45`, risk `<= 25`, and `EstimatedRR >= 0.5`.
- `ObservationConfirm_WideStop1_5R` remains disabled for Actual execution with `ObservationConfirmWideStopActualDisabledV129`.
- The v1.28 Breakaway structure remains unchanged: `BreakawayVolumeV128`, `BreakawayShortWideV128`, and the side-specific risk caps stay active.
- This version intentionally accepts more noise to reach enough sample size. The added pools must be judged by their own tags, not blended into the base strategy.
- The expected validation questions are: whether the month reaches the 180-220 trade range; whether total NetR/NetDollars remains positive despite the volume expansion; how much volume and PnL come from `AlmostConfirmedVolumeV130` and `StrictBullFreshVolumeV130`; whether Breakaway remains positive after the added active-trade competition; whether low-trade days improve; and whether lifecycle integrity remains complete with no increase in stale/reject/cancel failures.

From `OPF_RESEARCH_1.35`, Actual execution moves from volume discovery back to evidence-controlled profit optimization after the April/May/June review:

- Actual defaults now use `ACTUAL_EXEC_1.71`.
- The v1.34 selective `BreakawayFvg` Long restore is retired; all Breakaway Long Actual rows are skipped with `BreakawayLongActualDisabledV135`.
- The default Actual path list is narrowed to `ObservationConfirm`, `ObservationConfirm_WideStop1_5R`, Breakaway Short, and Failure Retest paths.
- `ShadowCandidate`, `ObservationStrict_Other`, and `StructureConfirmShadow_*` remain research paths but are blocked from Actual execution with `EvidenceFrozenActualPathV135`.
- This version does not add a new setup family and does not change the default `ActualTargetR=1.5`; the next replay should verify that the OC skeleton remains stable before testing OC-specific profit-extension rules.

From `OPF_RESEARCH_1.36`, Actual execution keeps the v1.35 OC skeleton and makes two-month replay analysis practical:

- Actual defaults now use `ACTUAL_EXEC_1.72`.
- `FailureReverse_RetestFailed_WideStop1_5R` is removed from Actual execution after the v1.35 May replay showed it was a small negative pool.
- Older runtime configs that still include that path are blocked with `FailureRetestWideStopActualDisabledV136`.
- `ResearchLogMode` defaults to `Compact`, which skips `score_breakdown.csv`, limits `funnel_events.csv` to execution decisions and research outcomes, and writes fixed-target exit-policy rows only for current Actual-skeleton paths.
- The compact mode still preserves the files needed for monthly/two-month analysis: execution trades, decisions, events, research outcomes, risk evaluations, no-trade, edge attribution, candidate/confirmation evaluations, regime summaries, zones, and config snapshots.
- The expected validation questions are whether removing the wide Failure Retest drag keeps May/June profitability intact, whether compact logs remain sufficient for daily/path/TP analysis, and whether file size stays manageable for two-month replay batches.

From `OPF_RESEARCH_1.37`, Actual execution keeps the v1.36 path set and makes one controlled quality cut before the April/May/June replay:

- Actual defaults now use `ACTUAL_EXEC_1.73`.
- Long `ObservationConfirm` Actual entries with `InitialRiskPoints > 12` are skipped with `LongObservationWideRiskCutV137`.
- Long `ObservationConfirm` entries in the narrow `11 < risk <= 12` band remain eligible and are tagged with `ObservationConfirmLongRisk12V137` when executed.
- Short `ObservationConfirm`, Breakaway Short, Failure Retest, target R, max daily trades, and compact logging are unchanged.
- This version does not add a new setup family. It tests whether removing the April/May drag from wide-risk Long `ObservationConfirm` improves NetR/NetDollars while keeping average trade count acceptable.
- The expected validation questions for the three-month replay are: whether April improves materially, whether May/June retain enough of their positive Long OC contribution, whether average daily trade count remains near or above 5, how many rows are skipped by `LongObservationWideRiskCutV137`, and whether abnormal execution rows remain isolated from normal PnL.

From `OPF_RESEARCH_1.38`, Actual execution keeps the v1.37 profitable baseline and prepares the next three-month replay for two-contract evidence accumulation without reducing trade count:

- Actual defaults now use `ACTUAL_EXEC_1.74`.
- `ActualOrderQuantity` defaults to `2`, and the strategy default execution profile is `MNQ_2Contract_Evidence`.
- `MNQ_2Contract_Evidence` uses two fixed MNQ contracts but keeps daily target and daily loss stops disabled so the replay does not reduce signal count during evidence accumulation.
- `ObservationConfirm` Long keeps the v1.37 planned-risk guard, and its post-fill risk drift is now capped at `12` points as well. If a fill reprices risk above that cap, the strategy records `ENTRY_FILLED_RISK_EXCEEDED` and emergency-flattens instead of accepting the over-cap trade.
- No setup family is added, and no profitable/negative path is removed in this version because trade count must not fall below the v1.37 baseline.
- Actual TP remains governed by the existing strategy target rules. A research-only `DynamicPathV138` exit policy is added to `exit_policy_evaluations.csv`: `ObservationConfirm=2R`, `ObservationConfirm_WideStop1_5R=3R`, and other current Actual-skeleton paths stay at `1.5R`.
- The expected validation questions are whether two-contract execution roughly doubles NetDollars without changing NetR structure, whether average trades/day remains near the v1.37 baseline, whether post-fill `ObservationConfirm` Long risk no longer exceeds `12`, and whether `DynamicPathV138` provides enough evidence to justify a future actual TP change.

From `OPF_RESEARCH_1.39`, Actual execution promotes the strongest v1.38 TP evidence into one controlled profit-capture test:

- Actual defaults now use `ACTUAL_EXEC_1.75`.
- `ActualOrderQuantity` remains `2`, and the default execution profile remains `MNQ_2Contract_Evidence`.
- `ObservationConfirm` Actual executions use `TargetR=2.0` for both Long and Short rows.
- `ObservationConfirm_WideStop1_5R`, Breakaway, Failure Retest, path whitelist, risk gates, max daily trades, compact logging, and the v1.37 Long OC risk guard are unchanged.
- The old Long-only v1.14 `3R/2.5R` TP override is no longer used for Actual execution, so this version isolates the v1.39 variable: base `ObservationConfirm` moves to `2R`, all other current Actual-skeleton paths stay at the default `1.5R`.
- Executed OC rows with the override include `ObservationConfirmTarget2RV139` in `execution_decisions.csv`, and `exit_policy_evaluations.csv` writes `DynamicPathV139` with the same policy.
- The expected validation questions for the three-month replay are whether v1.39 improves NetR/NetDollars versus the v1.37 profit baseline and v1.38 two-contract evidence batch, whether average daily trades stay near or above the v1.37 baseline, and whether OC 2R increases profit size without turning many former 1.5R winners into SL/Other exits.

From `OPF_RESEARCH_1.40`, Actual execution repairs the v1.39 profit-capture regression and returns to the v1.37/v1.38 target structure:

- Actual defaults now use `ACTUAL_EXEC_1.76`.
- `ActualOrderQuantity` remains `2`, and the runtime snapshot is forced to `MNQ_2Contract_Evidence` when the loaded JSON is the two-contract evidence configuration. This prevents an old ATAS panel value such as `MNQ_1Contract_Target150_200` from misleading profile consistency analysis.
- Base `ObservationConfirm` Actual executions return to the default `ActualTargetR=1.5`; the v1.39 all-OC `2R` override is disabled.
- `ObservationConfirm_WideStop1_5R`, Breakaway, Failure Retest, path whitelist, risk gates, max daily trades, compact logging, and the v1.37 Long OC risk guard are unchanged.
- `exit_policy_evaluations.csv` writes `DynamicPathV140`, matching the actual v1.40 policy: current Actual-skeleton paths use `1.5R`. Fixed `2R/2.5R/3R` policy rows remain available as research-only comparisons.
- The expected validation questions for the next three-month replay are whether v1.40 restores the v1.37 positive-R structure while keeping the two-contract NetDollars benefit, whether daily average trades remain near or above 5, and whether profile consistency now reports `MNQ_2Contract_Evidence`.

Current rollback state:

- The active running baseline is restored to `OPF_RESEARCH_1.37` / `ACTUAL_EXEC_1.73`.
- v1.37 is the strategy skeleton for the next optimization branch: one MNQ contract, fixed `ActualTargetR=1.5`, compact logging, v1.36 path set, and the Long `ObservationConfirm` `risk > 12` cut.
- v1.38-v1.40 remain evidence batches only. Their main findings are: two-contract execution can raise dollars but should not be mixed into the structural baseline; all-OC `2R` damaged the R structure; returning OC to `1.5R` repaired v1.39 but still did not beat v1.37 in R.
- The next version should be planned from v1.37, not from v1.40, and should target one controlled improvement: reduce the weak `OC_Base` subgroup while preserving `OC_Filler` and total daily trade count.

From `OPF_RESEARCH_1.41`, Actual execution starts the next optimization branch from the v1.37 skeleton:

- Actual defaults now use `ACTUAL_EXEC_1.77`.
- `ActualOrderQuantity` defaults to `2`, and the strategy default execution profile is `MNQ_2Contract_Evidence`.
- Actual target remains fixed at `ActualTargetR=1.5`; no dynamic TP or all-OC `2R` override is active.
- `OC_Filler`, `ObservationConfirmRisk22V132`, `ObservationConfirmLongRisk12V137`, wide-stop OC tags, Breakaway, Failure Retest, path whitelist, max daily trades, compact logging, and the v1.37 Long `risk > 12` cut are preserved.
- Only the weak untagged base `ObservationConfirm` subgroup is lightly filtered: base Long rows require `SetupQualityScore >= 60`, and base Short rows with `8 < risk <= 11` require `SetupQualityScore >= 70`.
- New skip reasons are `OCBaseLongQualityCutV141` and `OCBaseShortRisk8_11QualityCutV141`.
- The validation question for the next three-month replay is whether v1.41 improves NetR/NetDollars versus v1.37 while preserving the v1.37/v1.40 trade-count range. The first checks should be daily trades, Long/Short split, `OC_Filler` contribution, `OC_Base` skipped count, `OCBase...V141` avoided-loss quality, and abnormal execution rows.

From `OPF_RESEARCH_1.42`, Actual execution fully returns to the v1.37 rule skeleton for a clean two-contract replay:

- Actual defaults now use `ACTUAL_EXEC_1.78`.
- `ActualOrderQuantity` remains `2`, and `ActualTargetR` remains fixed at `1.5`.
- The v1.41 `OC_Base` quality cuts are removed; there are no `OCBase...V141` skip reasons in active execution.
- The retained skeleton is v1.37: compact logging, v1.36 path set, v1.37 Long `ObservationConfirm` `risk > 12` cut, and no all-OC `2R` target override.
- The profile snapshot bug is fixed for this replay: when the JSON settings are the two-contract evidence preset, the snapshot resolves `ExecutionProfileName` to `MNQ_2Contract_Evidence` even if the ATAS panel still contains an older one-contract profile string.
- The validation question is simple: compare v1.42 against v1.37 using the same 4/5/6 sample, with two contracts active and without v1.41 filter interference. First checks are schema/profile consistency, `Quantity=2`, daily trades, NetR, NetDollars, path/tag contribution, and abnormal execution rows.

From `OPF_RESEARCH_1.37_2C`, Actual execution is a clean rerun of the v1.37 rule skeleton with only the contract count changed for evidence:

- Actual defaults use `ACTUAL_EXEC_1.73_2C`.
- `ActualOrderQuantity=2`, `ActualTargetR=1.5`, `ActualMaxTradesPerDay=12`, guards disabled, and compact logging remain active.
- No v1.38 dynamic target test, v1.39 all-OC `2R`, v1.41 `OCBase...V141` quality cuts, or v1.42 strategy-side changes are part of this run.
- The validation question is whether the v1.37 skeleton itself scales to two contracts over the April/May/June replay when the only intended variable is execution size.

From `OPF_RESEARCH_1.37_2C_FIX1`, the strategy keeps the same v1.37 two-contract evidence configuration and fixes execution logging for multi-contract exits:

- Actual defaults use `ACTUAL_EXEC_1.73_2C_FIX1`.
- Multiple TP/SL fills for the same two-contract order are aggregated into one execution-trade row using the weighted average exit price.
- The strategy rules, path whitelist, target R, risk gates, and contract count are unchanged from `OPF_RESEARCH_1.37_2C`.
- The validation question is whether `execution_trades.csv` now has one normal row per `TradeID` and whether the clean two-contract replay moves closer to the original v1.37 baseline scaled by size.

From `OPF_RESEARCH_1.37_2C_FIX2`, strategy rules remain unchanged and replay TP/SL fill normalization is added:

- Actual defaults use `ACTUAL_EXEC_1.73_2C_FIX2`.
- If a TP/SL order role is correct, the entry fill is within tolerance, and ATAS replay reports an exit price far beyond the planned TP/SL price, the trade is scored at the expected TP/SL price and marked with `NormalizedReplayExitFill`.
- `FLATTEN` exits and trades with abnormal entry fill remain abnormal and contribute zero normal PnL/R.
- Raw replay fill impact remains available through `RawPoints`, `RawDollars`, and `RawPointsR`.

From `OPF_RESEARCH_1.43`, Actual execution keeps the restored v1.37 two-contract skeleton and tests one controlled bad-day stabilizer:

- Actual defaults use `ACTUAL_EXEC_1.79`.
- `ActualOrderQuantity=2`, `ActualTargetR=1.5`, `ActualMaxTradesPerDay=12`, guards disabled, compact logging, path whitelist, risk gates, and the v1.37 Long `ObservationConfirm` `risk > 12` cut remain unchanged.
- After two consecutive Actual losing exits during the same session day, the next eligible Actual orders use a temporary `1.0R` target instead of `1.5R` until a profitable exit resets the consecutive-loss counter.
- Executed rows with this target override include `DailyLossRecoveryTarget1RV143` and `ActualTargetOverride:targetR=1` in `execution_decisions.csv`.
- This is not a stop-trading rule and should not reduce trade count. The validation question is whether worst losing days improve without damaging the restored v1.37 two-contract NetR structure.

From `OPF_RESEARCH_1.44`, the v1.43 bad-day stabilizer is superseded before broad replay because it is too small for a three-month validation cycle. Actual execution keeps the restored v1.37 two-contract skeleton and tests selective profit expansion:

- Actual defaults use `ACTUAL_EXEC_1.80`.
- `ActualOrderQuantity=2`, `ActualMaxTradesPerDay=12`, guards disabled, compact logging, path whitelist, risk gates, and the v1.37 Long `ObservationConfirm` `risk > 12` cut remain unchanged.
- Base `ActualTargetR` remains `1.5R`, but selected stronger Short pools use `2.0R`:
  - `ObservationConfirm` Short with `risk <= 11`, tagged `OCShortBaseTarget2RV144`.
  - `BreakawayFvg` / `BreakawayFvg_Qualified` Short, tagged `BreakawayShortTarget2RV144`.
- All other paths stay at `1.5R`; the v1.43 `DailyLossRecoveryTarget1RV143` rule is not active in this version.
- Executed rows with the override include the path-specific v1.44 tag plus `ActualTargetOverride:targetR=2` in `execution_decisions.csv`.
- The validation question for the next three-month replay is whether selective 2R improves NetR/NetDollars versus `OPF_RESEARCH_1.37_2C_FIX2` without reducing trade count or turning many former 1.5R winners into SL.

From `OPF_RESEARCH_1.45`, Actual execution keeps the v1.37 two-contract skeleton and narrows the profit-expansion test to the only v1.44 subgroup with cross-month support:

- Actual defaults use `ACTUAL_EXEC_1.81`.
- `ObservationConfirm` Short no longer receives the v1.44 `2R` override; `OCShortBaseTarget2RV144` is removed and all `ObservationConfirm` paths return to the default `1.5R` target.
- `BreakawayFvg` / `BreakawayFvg_Qualified` Short keeps the `2R` target and is tagged `BreakawayShortTarget2RV145`.
- No entry rules, path whitelist, risk gates, daily guards, order quantity, max daily trades, or compact logging settings are changed.
- The validation question for the March-June replay is whether Breakaway Short `2R` improves NetR/NetDollars versus `OPF_RESEARCH_1.37_2C_FIX2` while the OC pool is no longer harmed by a full-position `2R` target.

From `OPF_RESEARCH_1.46`, `v1.45` is treated as the stable-profit candidate and Actual execution resumes controlled positive-expectancy volume expansion:

- Actual defaults use `ACTUAL_EXEC_1.82`.
- `ActualOrderQuantity=2`, base `ActualTargetR=1.5`, Breakaway Short `2R`, daily guards disabled, compact logging, and the existing OC / Breakaway / Failure execution skeleton remain unchanged.
- The Actual path list adds existing research paths `BreakawayRetest`, `TrendPullbackConfirmed`, and `StructureConfirmShadow_SwingStop`.
- Directional evidence gates keep only the positive sub-pools from the v1.45 research review:
  - `BreakawayRetest` can execute both sides.
  - `TrendPullbackConfirmed` executes Short only; Long is skipped with `TrendPullbackLongDisabledV146`.
  - `StructureConfirmShadow_SwingStop` executes Long only; Short is skipped with `StructureSwingShortDisabledV146`.
- Executed rows from these expansion pools include `PositiveExpansionV146` in `execution_decisions.csv`.
- The validation question for the next broad replay is whether volume expands beyond the v1.45 `396` normal trades / `79` effective days baseline while total NetR and NetDollars stay positive and improve.

From `OPF_RESEARCH_1.47`, `v1.45` remains the stable-profit candidate and `v1.46` is not promoted to a baseline. The next test expands the proven OC / Breakaway main pools more aggressively instead of relying on small side pools:

- Actual defaults use `ACTUAL_EXEC_1.83`.
- `ActualOrderQuantity=2`, base `ActualTargetR=1.5`, Breakaway Short `2R`, daily guards disabled, compact logging, and the max daily trade safety ceiling remain unchanged.
- The default Actual path list removes the v1.46 `TrendPullbackConfirmed` / `StructureConfirmShadow_SwingStop` execution test and keeps `BreakawayRetest` only as a small positive secondary path.
- `ObservationConfirm` filler expands from `48 <= score < 56`, max 3 filler trades, before daily trade 5 to `46 <= score < 56`, max 5 filler trades, before daily trade 7.
- `ObservationConfirm` volume risk expansion widens from risk `<=18` to risk `<=20`.
- High-quality `ObservationConfirm` daily-volume rescue can now take up to 4 trades per day instead of 2, still only before daily trade 7.
- `BreakawayFvg` volume expansion lowers the setup-quality threshold from `80` to `76`; Breakaway Long remains blocked by the existing v1.35 rule.
- Executed rows from the new volume test include one or more of `OCFillerExpansionV147`, `OCRiskExpansionV147`, `DailyVolumeQualityRescueV147`, and `BreakawayVolumeV147`.
- The validation question for the next broad replay is whether the strategy adds at least 40 trades versus the v1.45 `396` normal-trade baseline while the v1.47 expansion tags are positive on their own and total NetR/NetDollars stay above v1.45.

From `OPF_RESEARCH_1.48`, strategy rules remain unchanged from v1.47 and Actual execution fixes a stale unfilled-entry bug found during February replay:

- Actual defaults use `ACTUAL_EXEC_1.84`.
- If an Actual market entry has no confirmed fill after `2` closed bars, or the entry order becomes inactive before filling, the strategy logs `ENTRY_STALE_NO_FILL`, cancels the entry order, marks the execution as `NO_ENTRY_FILL`, and releases the active-trade block.
- If ATAS later reports a delayed entry fill for that completed no-entry execution, the strategy logs `LATE_ENTRY_AFTER_COMPLETED` and submits an emergency flatten order for the late-filled quantity.
- Pending unfilled entries are shown as `PENDING_ENTRY` on the chart and HUD. SL/TP lines are drawn only after an entry fill exists, so planned brackets are no longer mistaken for active protective orders.
- No entry paths, risk thresholds, target-R rules, order quantity, daily guards, or v1.47 expansion tags are changed.

From `OPF_RESEARCH_1.49`, strategy rules remain unchanged and Actual execution adds an account-position reconciliation guard:

- Actual defaults use `ACTUAL_EXEC_1.85`.
- If ATAS reports `CurrentPosition != 0` while OPF has no active execution, the strategy logs `ORPHAN_POSITION_DETECTED`, sends an `ORPHAN_FLATTEN_SEND` market order in the opposite direction, and skips new Actual entries with `SKIP_ORPHAN_POSITION` until the account position is flat.
- The HUD `ActualExec` line now includes `pos=...` so screenshots can distinguish internal execution state from the actual ATAS account position.
- This version targets the February replay case where HUD showed `active=False` while the ATAS account panel still showed a residual position. No entry paths, risk thresholds, target-R rules, order quantity, daily guards, or v1.47 expansion tags are changed.

From `OPF_RESEARCH_1.50`, strategy rules remain unchanged and Actual execution fixes the duplicate-flatten loop found in the February-June replay:

- Actual defaults use `ACTUAL_EXEC_1.86`, and the code default now matches the JSON template so `ConfigSnapshot.json` should no longer fall back to an older `ActualExecutionSettings.Version`.
- Duplicate `FLATTEN` fills after an execution is already completed are logged as `DUPLICATE_EXIT_FILL` with `duplicateFlattenIgnored`; they no longer submit another emergency flatten.
- Duplicate SL/TP fills can submit at most one residual-position flatten, and only when ATAS `CurrentPosition` is still non-zero.
- Rejected out-of-range entry fills submit emergency flatten only for the newly filled quantity that has not already been covered.
- Orphan-position flatten is sent once per residual-position episode and is unlocked only after `CurrentPosition` returns to zero.
- No entry paths, risk thresholds, target-R rules, order quantity, daily guards, or v1.47 expansion tags are changed.

From `OPF_RESEARCH_1.51`, strategy rules remain unchanged and Actual execution adds a daily abnormal-fill circuit breaker:

- Actual defaults use `ACTUAL_EXEC_1.87`.
- When an entry fill is rejected as `EntryFillOutOfRange`, the strategy still sends the emergency flatten for the filled quantity, then blocks further Actual order submissions for the same session day.
- Later eligible signals on that day are skipped with `SKIP_DAILY_ABNORMAL_FILL_GUARD`; research logging continues so the day can still be audited without creating more bad Actual orders.
- The HUD `ActualExec` line shows `abnormalGuard=<n>` while this guard is active.
- No entry paths, risk thresholds, target-R rules, order quantity, daily guards, or v1.47 expansion tags are changed.

From `OPF_RESEARCH_1.52`, Actual execution returns to the main profitability-and-volume objective by expanding the existing Breakaway pool:

- Actual defaults use `ACTUAL_EXEC_1.88`.
- The v1.51 execution safety guard remains active.
- Breakaway Short keeps the v1.45 `2R` target and lowers its volume-quality threshold from `SetupQualityScore >= 76` to `>= 72`.
- Breakaway Long is no longer globally blocked when it is a high-quality selective row: `SetupQualityScore >= 88`, risk `<= 22`, and `EstimatedRR >= 1.2`. These rows are tagged `BreakawayLongSelectiveV152`.
- OC, OC filler, wide-stop OC, Failure Retest, order quantity, daily guards, and base `ActualTargetR=1.5` are unchanged.
- The validation question for the next broad replay is whether Breakaway expansion increases trade count and total NetR/NetDollars while `BreakawayLongSelectiveV152` and the newly widened Short `BreakawayVolumeV147` rows are non-negative as standalone subgroups.

From `OPF_RESEARCH_1.53`, strategy rules remain unchanged and Actual execution fixes two-contract partial entry fills found during the April 6-9 smoke replay:

- Actual defaults use `ACTUAL_EXEC_1.89`.
- SL/TP brackets are submitted only after cumulative entry fills reach the requested order quantity, so split `1 + 1` fills receive a full two-contract bracket instead of a one-contract bracket.
- A partial entry that does not complete within the short fill-aggregation window is canceled and emergency-flattened. Later fills from the aborted entry are flattened instead of being left unprotected.
- `execution_events.csv` records `ENTRY_PARTIAL_FILL_WAITING` and `ENTRY_PARTIAL_FILL_ABORT` for auditing.
- No entry paths, quality thresholds, risk gates, target-R rules, order quantity, or v1.52 Breakaway expansion rules are changed.

From `OPF_RESEARCH_1.54`, strategy and order behavior remain unchanged and execution audit events distinguish expected OCO cleanup states from real failures:

- Actual defaults use `ACTUAL_EXEC_1.90`.
- After one protective leg fills, an ATAS `Failed` state on the inactive sibling is recorded as `OCO_SIBLING_INACTIVE` instead of `ORDER_STATE_FAILED`.
- A cleanup cancel that reports `Order ... for cancel not found` after the execution has already exited is recorded as `CANCEL_ALREADY_INACTIVE` instead of `CANCEL_FAIL`.
- Register failures, unexpected failed/rejected orders, and cancel failures outside completed-exit OCO cleanup remain failure events.
- No entry paths, quality thresholds, risk gates, target-R rules, order quantity, or v1.53 partial-fill behavior are changed.

From `OPF_RESEARCH_1.55`, the v1.54 execution-safety baseline combines the full v1.52 profitability rollback with a verified end-of-replay lifecycle fix:

- Actual defaults use `ACTUAL_EXEC_1.91`.
- Breakaway Long Actual execution is disabled again. The April-June evidence showed `65` v1.52 Breakaway Long trades at `-9.75R` / `-$643.50`.
- Breakaway Short keeps its v1.45 `2R` target and restores the pre-v1.52 minimum setup-quality threshold from `72` to `76`.
- The existing replay stop guard still blocks new entries from `20:40` onward, or `16:40` on Friday. If an Actual execution remains open at that point, the strategy cancels the entry and protective orders, then sends a real `SESSION_FLATTEN` market order for the current account position.
- A `SESSION_FLATTEN` is completed only through an ATAS `MyTrade` fill callback, contributes its real PnL/R, and then performs normal protection cleanup. The old `STOPPED` synthetic path remains only as a failure fallback if proactive shutdown does not complete before `OnStopped`.
- v1.53 partial-entry protection, v1.54 OCO audit classification, OC filler/risk expansion, Failure paths, two-contract quantity, and all other risk/target rules remain unchanged.
- The focused validation is March 16 and March 30 plus representative normal days: there should be real `REPLAY_STOP_FLATTEN_SEND` / `SESSION_FLATTEN` fills, `PROTECTION_CLEANUP_DONE`, no `STOPPED`, and no Breakaway Long Actual executions.

From `OPF_RESEARCH_1.56`, the v1.55 strategy rules remain unchanged and the end-of-replay exit sequence prevents a duplicate emergency flatten:

- Actual defaults use `ACTUAL_EXEC_1.92`.
- While `ReplayStopExitPending` is active, canceled SL/TP orders are not treated as an unexpected protection loss, so the strategy does not submit an additional `EMERGENCY_FLATTEN` beside `SESSION_FLATTEN`.
- After the protective-order cancellation requests complete, `SESSION_FLATTEN` is sent immediately without the former 250 ms delay.
- Entry paths, quality thresholds, risk gates, target-R rules, order quantity, and all other v1.55 strategy behavior remain unchanged.
- Focused validation uses March 16 and March 30: expect two real `SESSION_FLATTEN` exits, normal `PROTECTION_CLEANUP_DONE`, and zero `EMERGENCY_FLATTEN_SEND`, `DUPLICATE_EXIT_FILL`, or `STOPPED` events.

From `OPF_RESEARCH_1.57`, v1.56 remains the frozen baseline while Actual execution adds one cautious high-quality volume pool and Compact research evaluates dynamic two-contract exits without changing real SL/TP orders:

- Actual defaults use `ACTUAL_EXEC_1.93`.
- Long `ObservationConfirm_WideStop1_5R` rows with `SetupQualityScore >= 70` can extend the v1.31 risk cap from `22` to `25` points. At most one such expansion trade can execute per session day, and executed rows are tagged `OCWideStopLongExpansionV157`.
- The expansion does not bypass estimated-RR, hard-risk, same-bar ambiguity, active-trade, daily-trade, or execution-safety gates. Its real target remains the existing `1.5R` baseline.
- Actual research trackers that exit before the 12-bar research window remain active for shadow-only post-exit observation. Actual fills and Actual outcome fields remain unchanged.
- Compact `exit_policy_evaluations.csv` adds `SplitBase_Runner2_5R_BE0_75R`, `SplitBase_Runner2_5R_BE1R`, `SplitBase_Runner3R_BE0_75R`, and `SplitBase_Runner3R_BE1R`.
- Each split policy models one contract at the existing path target and one runner contract at `2.5R` or `3R`. The runner moves to break-even only from the bar after first reaching `0.75R` or `1R`; these rows never submit or modify real orders.
- Breakaway, Failure paths, OC filler/risk expansion, two-contract quantity, and all other v1.56 Actual rules remain unchanged.

From `OPF_RESEARCH_1.58`, the v1.57 expansion pool uses its real fixed target for the Actual RR gate:

- Actual defaults use `ACTUAL_EXEC_1.94`.
- `OCWideStopLongExpansionV157` candidates calculate reward as `risk * ActualTargetR`, currently exactly `1.5R`, with reward model `OCWideStopLongExpansionV158TargetR:1.5`.
- Score, `22 < risk <= 25`, one-trade daily cap, hard-risk, same-bar, active-trade, daily-limit, real TP/SL, and Compact runner-shadow behavior remain unchanged.

From `OPF_RESEARCH_1.59`, v1.58 behavior is retained while logging and daily-cap evidence are corrected:

- Actual defaults use `ACTUAL_EXEC_1.95`.
- Writes to each snapshot's `research.log` are serialized so concurrent order callbacks cannot turn a logging file-share exception into `CANCEL_FAIL`.
- The wide-stop Long expansion candidate definition is independent of its one-trade daily availability. Candidates after the cap still report fixed-target reward model `OCWideStopLongExpansionV159TargetR:1.5`, while execution remains blocked by `OCWideStopLongExpansionV157DailyCap`.
- Entry selection, score/risk thresholds, daily cap, real TP/SL, order quantity, and Runner Shadow policies are unchanged.

## Full Backtest Readiness Gate

Before moving from smoke replay to broad backtest/tuning, the latest 3-day smoke batch should satisfy:

1. All snapshots use the same `ResearchSchemaVersion` and `ActualExecutionSettings.Version`.
2. Core CSV files are present: config snapshot, signals, research outcomes, risk evaluations, execution events, and execution trades. `score_breakdown.csv` is required only for scoring-component research; it is not required when `ResearchLogMode=Compact`.
3. `ExecutedDecisions = ExecutionTrades = ActualVerifiedUniqueTrades`, with no duplicate Actual-verified rows.
4. Every Actual exit has `PROTECTION_CLEANUP_DONE`, with no `STALE`, `REJECT`, `CANCEL_FAIL`, or `FAILED` events.
5. The tested configuration is frozen: MNQ, documented `ActualOrderQuantity`, default `ActualTargetR=1.5` with any documented per-version TP overrides, daily target/loss stops disabled for evidence accumulation, max-trades safety ceiling, and Actual path whitelist.
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
