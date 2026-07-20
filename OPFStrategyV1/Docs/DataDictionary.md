# OPFStrategyV1 Data Dictionary

This document describes the current research files produced by `OPF_RESEARCH_1.63`.

## Common Context

Key research files include these context columns:

- `SnapshotID`: Unique replay/run snapshot.
- `StrategyVersion`: Strategy rule version.
- `ResearchSchemaVersion`: CSV schema version.
- `InstrumentProfileName`: Actual instrument profile used.
- `ExecutionProfileName`: Actual execution profile used.
- `ProfileCatalogVersion`: Profile catalog version used to resolve profile names.

The source of truth for full configuration is always `*_ConfigSnapshot.json`.

## Config Snapshot

File pattern:

- `*_ConfigSnapshot.json`

Purpose:

- Records complete version and profile context for a replay run.
- Records requested profile names and fallback flags.

Important fields:

- `RequestedInstrumentProfileName`
- `RequestedExecutionProfileName`
- `UsedInstrumentProfileFallback`
- `UsedExecutionProfileFallback`
- `InstrumentProfile`
- `ExecutionProfile`

## signals.csv

File pattern:

- `*_signals.csv`

Purpose:

- Records signal lifecycle events that are meaningful for research.

Important fields:

- `SignalID`
- `Time`
- `Bar`
- `Stage`
- `Side`
- `SetupType`
- `ZoneID`
- `ZoneType`
- `ZoneFreshness`
- `RegimeScore`
- `SetupQualityScore`
- `SkipReasons`

## research_outcomes.csv

File pattern:

- `*_research_outcomes.csv`

Purpose:

- Records tracked post-signal outcome for research paths.

Important fields:

- `SignalID`
- `EntryTime`
- `EntryBar`
- `Side`
- `Entry`
- `Stop`
- `InitialRiskPoints`
- `PointValue`
- `TickSize`
- `TickValue`
- `PlannedContracts`
- `ActualContracts`
- `RiskPerContractDollars`
- `TotalInitialRiskDollars`
- `MFEPoints`
- `MAEPoints`
- `MFE_Dollars`
- `MAE_Dollars`
- `MFE_R`
- `MAE_R`
- `Hit1R`
- `Hit1_5R`
- `Hit2R`
- `Hit2_5R`
- `Hit3R`
- `ResearchPath`
- `StopBasis`
- `EntryDelayBars`
- `OutcomeClass`
- `ExitReason`
- `ExitEfficiency`
- `RunupCapturePct`
- `AdverseBeforeProfit_R`
- `ActualVerified`
- `ActualTradeID`
- `ActualExitRole`
- `ActualPnL_R`
- `ActualMFEPoints`
- `ActualMAEPoints`
- `ActualMFE_R`
- `ActualMAE_R`
- `ResolvedOutcomeClass`
- `OutcomeSource`
- `WouldTradeLive`
- `ResearchOnlySignal`
- `SkippedByDailyGuard`
- `ExecutionSkipReasons`
- `DailyTargetDollars`
- `DailyLossLimitDollars`
- `MaxContracts`

In `OPF_RESEARCH_0.19`, execution fields are research scaffolding. The strategy is still research-only, so outcomes normally use `WouldTradeLive=false`, `ResearchOnlySignal=true`, and `ExecutionSkipReasons=ResearchOnlyMode`.

From `OPF_RESEARCH_0.20`, `ResearchPath` may also include:

- `UnknownRegimeZoneTouch`: research-only tracking for touched zones when regime is `Unknown`.
- `ZoneBirthResearch`: research-only tracking for a zone touched on its creation bar.
- `ObservationConfirm`: research-only tracking after an observation signal receives reclaim / previous-bar-break confirmation within 3 bars.
- `ObservationConfirm_Strict`: research-only tracking after `ObservationConfirm` passes distance and heat quality checks.
- `ObservationStrict_BullFresh`: research-only tracking for strict observation signals in fresh bullish zones.
- `ObservationStrict_Other`: research-only tracking for strict observation signals outside the fresh bullish bucket.
- `FailureReverse_ObservationInvalidated`: research-only tracking for the opposite side after an observation setup invalidates before confirmation.
- `FailureReverse_RetestFailed`: research-only tracking after a failed setup retests its source zone and confirms away from it.

These paths are observation paths only. They do not promote signals to live candidates.

From `OPF_RESEARCH_0.25`, failure-reverse signals are written to `signals.csv` with `SetupType=FailureReverse`. `SkipReasons` includes `FailureSourceSignalID`, `FailureSource`, and `FailureSourceZoneFreshness`.

## no_trade.csv

File pattern:

- `*_no_trade.csv`

Purpose:

- Records why a potential setup or candidate did not progress.

Important fields:

- `SignalID`
- `Time`
- `Bar`
- `SetupStage`
- `SkipReasons`

`SkipReasons` uses `|` when multiple reasons apply.

## zones.csv

File pattern:

- `*_zones.csv`

Purpose:

- Records detected zones used by strategy and research logic.

Important fields:

- `ZoneID`
- `ZoneType`
- `Direction`
- `Low`
- `High`
- `CreatedTime`
- `CreatedBar`
- `Freshness`
- `TouchCount`
- `Mitigated`
- `Source`
- `DetectorVersion`

## candidate_evaluations.csv

File pattern:

- `*_candidate_evaluations.csv`

Purpose:

- Records every zone-touch candidate decision before confirmation.

Important fields:

- `Regime`
- `ZoneID`
- `ZoneType`
- `ZoneDirection`
- `ZoneFreshness`
- `TouchCount`
- `Mitigated`
- `Result`
- `Reason`
- `PullbackEpisodeID`
- `PullbackCountInRegime`
- `PullbackStartBar`
- `PullbackStatus`

From `OPF_RESEARCH_0.24`, pullback episode fields are research-only. They let replay analysis compare first and second pullbacks against later pullbacks before those rules become execution filters.

## score_breakdown.csv

File pattern:

- `*_score_breakdown.csv`

Purpose:

- Records score components for regime, setup, and zone-quality diagnostics.

From `OPF_RESEARCH_0.23`, `ScoreName=ZoneQualityScore` is emitted for strict observation attempts. Its first components are `ZoneFreshness`, `TouchCount`, `Mitigated`, and `ZoneType`.

Important fields:

- `ScoreName`
- `TotalScore`
- `Threshold`
- `Passed`
- `Component`
- `RawValue`
- `ComponentPassed`
- `Weight`
- `Contribution`
- `Reason`

## risk_evaluations.csv

File pattern:

- `*_risk_evaluations.csv`

Purpose:

- Records risk and estimated reward diagnostics for every research path entry.

Important fields:

- `SignalID`
- `ResearchPath`
- `Entry`
- `Stop`
- `InitialRiskPoints`
- `MaxRiskPointsHard`
- `RiskPassed`
- `EstimatedRewardPoints`
- `RewardModel`
- `EstimatedRR`
- `MinEstimatedRR`
- `EstimatedRRPassed`
- `RR_GE_1_0`
- `RR_GE_1_2`
- `RR_GE_1_5`
- `SkipReasonsIfApplied`

From `OPF_RESEARCH_0.27`, these fields are diagnostic only. They do not filter or change research signals yet.

From `OPF_RESEARCH_0.43`, RR tier fields show how many candidates would survive common execution thresholds. They are intended to compare `ActualMinEstimatedRr = 1.0`, `1.2`, and `1.5` without changing StrategyEngine entry logic.

From `OPF_RESEARCH_0.44`, `FailureReverse_ObservationInvalidated` can also be selected by Actual execution. It remains separately labeled so its results can be compared against the primary `ObservationConfirm` path instead of blending the two setups.

From `OPF_RESEARCH_0.45`, research rows that match a completed Actual order include Actual verification fields. `OutcomeClass` remains the OHLC research classification, while `ResolvedOutcomeClass` uses the actual fill result when `ActualVerified=true`. `IntraBarAmbiguous=true` marks rows where the research bar sequence cannot determine whether stop or target happened first.

From `OPF_RESEARCH_0.46`, zone-based signal IDs include a compact zone tag, for example `...-OBS-001077-Z000901`, to prevent collisions when multiple zones generate signals on the same bar. The summary script also audits missing or duplicate Actual verification rows.

From `OPF_RESEARCH_0.47`, `*_exit_policy_evaluations.csv` records research-only fixed-exit comparisons for `Fixed1_5R`, `Fixed2R`, and `Fixed2_5R`. These rows let one replay batch compare target policies without changing Actual order submission.

## exit_policy_evaluations.csv

File pattern:

- `*_exit_policy_evaluations.csv`

Purpose:

- Compares fixed R exit policies for every research outcome.

Important fields:

- `SignalID`
- `ResearchPath`
- `ExitPolicy`
- `ExitReason`
- `TargetR`
- `PnL_R`
- `PnLDollars`
- `AmbiguousStopAndTargetSameBar`
- `FirstStopBar`
- `FirstTargetBar`
- `MFE_R`
- `MAE_R`
- `PolicyExitBar`

From `OPF_RESEARCH_0.28`, selected research paths may have a `_WideStop1_5R` suffix. These paths use the same entry signal with a stop 1.5x wider than the base path, for stop-sensitivity research only.

## execution_events.csv

File pattern:

- `*_execution_events.csv`

Purpose:

- Records ATAS execution events when `Enable Actual Orders` is enabled.

Important fields:

- `SignalID`
- `TradeID`
- `Time`
- `Bar`
- `Event`
- `Role`
- `Side`
- `ResearchPath`
- `Price`
- `Quantity`
- `Message`

From `OPF_RESEARCH_0.30`, execution events include entry submission, fills, SL/TP submission, exit fills, skips, and cancels.

## execution_trades.csv

File pattern:

- `*_execution_trades.csv`

Purpose:

- Records completed ATAS replay execution trades.

Important fields:

- `SignalID`
- `TradeID`
- `EntryTime`
- `EntryBar`
- `ExitTime`
- `ExitBar`
- `Side`
- `ResearchPath`
- `Quantity`
- `EntryPrice`
- `ExitPrice`
- `StopPrice`
- `TargetPrice`
- `InitialRiskPoints`
- `TargetR`
- `PlannedTargetR`
- `TargetRDrift`
- `PointsR`
- `ExitRole`
- `Points`
- `Dollars`
- `RiskDollars`
- `TargetDollars`

From `OPF_RESEARCH_0.31`, this file is the fastest way to verify actual replay order PnL separately from research-only outcome statistics.

From `OPF_RESEARCH_0.32`, `execution_trades.csv` also includes:

- `DailyPnlAfterDollars`
- `DailyTradeCount`
- `DailyConsecutiveLosses`

These fields let ExecutionEngine validation check whether daily target/loss and consecutive-loss guards are stopping only actual replay orders while research signals continue.

From `OPF_RESEARCH_0.41`, `execution_trades.csv` also includes R-based execution fields:

- `InitialRiskPoints`
- `TargetR`
- `PlannedTargetR`
- `TargetRDrift`
- `PointsR`
- `RiskDollars`
- `TargetDollars`

These fields let Actual order results be compared across products and contract counts without relying only on raw points or dollars.

From `OPF_RESEARCH_0.42`, `execution_events.csv` may include:

- `TP_SUPPRESSED`: SL was immediately done after submission, so TP was not sent.
- `DUPLICATE_EXIT_FILL`: another SL/TP fill arrived after the trade was already complete.
- `EMERGENCY_FLATTEN_SEND`: the strategy submitted a market order to flatten a duplicate-exit residual position.

From `OPF_RESEARCH_0.33`, `SKIP_DAILY_FULL_LOSS_LIMIT` and `SKIP_DAILY_CONSEC_LOSS_LIMIT` occur only when their replay panel switches are enabled. Daily target and daily loss guards remain active.

From `OPF_RESEARCH_0.34`, `execution_events.csv` may include `SKIP_RESEARCH_ONLY_PATH`. This means a signal was tracked by ResearchEngine but was not eligible for actual replay execution under the default ExecutionEngine whitelist.

From `OPF_RESEARCH_0.35`, replay execution panel defaults are preset in code for the current research cycle. The configuration remains visible through ATAS properties and the effective profile context remains recorded in `ConfigSnapshot.json`; a later JSON configuration layer should become the source of truth for these execution defaults.

From `OPF_RESEARCH_0.36`, user-facing execution controls are labeled as actual order controls because replay and live account execution share the same order-submission chain. Existing CSV file names and event names keep their current schema for continuity.

From `OPF_RESEARCH_0.37`, `execution_events.csv` may include `SKIP_RISK_RR`. This means the signal stayed in ResearchEngine, but Actual execution skipped it because initial risk exceeded the instrument hard limit or estimated RR was below the instrument minimum.

From `OPF_RESEARCH_0.38`, `ConfigSnapshot.json` also includes:

- `ActualExecutionConfigPath`
- `ActualExecutionConfigStatus`
- `ActualExecutionSettings`

The default config file is `%APPDATA%\ATAS\StrategyConfigs\OPFStrategyV1_actual_execution.json`. It is outside `StrategyLogs`, so normal log cleanup does not delete it.

From `OPF_RESEARCH_0.39`, Actual order Entry / SL / TP / Exit lines are chart overlays only. They do not change CSV schema or order behavior.

## execution_decisions.csv

Path pattern:

- `*_execution_decisions.csv`

Purpose:

- Records every Actual execution decision before order submission.

Fields:

- `SignalID`
- `Time`
- `Bar`
- `Decision`
- `Reason`
- `ExecutionScope`
- `Side`
- `SetupType`
- `ResearchPath`
- `RegimeScore`
- `SetupQualityScore`
- `StrategyEligible`
- `Entry`
- `Stop`
- `Target`
- `InitialRiskPoints`
- `EstimatedRewardPoints`
- `EstimatedRR`
- `DailyPnlDollars`
- `DailyTradeCount`
- `TradeID`

From `OPF_RESEARCH_0.40`, this file is the primary way to audit why an otherwise tracked signal did or did not become an Actual order.

From `OPF_RESEARCH_0.41`, executed rows include `TradeID` so decisions can be joined directly to `execution_events.csv` and `execution_trades.csv`.

From `OPF_RESEARCH_0.48`, decision rows include the strategy-quality gate context:

- `RegimeScore`: Directional TrendScore used by the signal.
- `SetupQualityScore`: Unified setup quality score used by Actual execution.
- `StrategyEligible`: Whether the signal passed the Actual strategy-quality gate before risk/RR and daily guards.

From `OPF_RESEARCH_0.49`, decision rows may include `FailureRequiresRetest:path=...` when a failure-reverse path passes strategy quality but is blocked from Actual execution because it has not reached the retest-failed confirmation stage.

From `OPF_RESEARCH_0.50`, `RewardModel` identifies how `EstimatedRewardPoints` was calculated, such as nearest structure, Breakaway target-R, FailureRetest hybrid, or Observation high-quality minimum-R.

From `OPF_RESEARCH_0.82`, `ExecutionScope` separates executed Actual orders, executable-but-skipped Actual candidates, and research-only/path-disabled rows.

From `OPF_RESEARCH_0.51`, `execution_events.csv` may include:

- `SL_CLEANUP_AFTER_IMMEDIATE_TP`: TP was already done immediately after submission, so the strategy started SL cleanup.
- `PROTECTION_CLEANUP_PENDING`: an exit or duplicate exit requires Entry/SL/TP cleanup.
- `PROTECTION_CLEANUP_CANCEL_SENT`: the strategy sent cancel requests for still-working known execution orders.
- `PROTECTION_CLEANUP_DONE`: all known Entry/SL/TP orders are no longer working.
- `PROTECTION_STALE_AFTER_EXIT`: at least one known Entry/SL/TP order still looked working after a cleanup attempt.
- `ZONEBIRTH_SPLIT_V172_READY`: both one-contract Base/Runner OCO groups were submitted for a fully filled two-contract ZoneBirth Short entry.
- `BASE_SL_SENT`, `BASE_TP_SENT`, `RUNNER_SL_SENT`, `RUNNER_TP_SENT`: individual v1.72 protection orders were submitted.
- `ZONEBIRTH_RUNNER_V172_BE_TRIGGERED`: the split execution first reached the closed-bar `1R` Runner trigger.
- `ZONEBIRTH_RUNNER_V172_BE_MODIFY_SEND` / `ZONEBIRTH_RUNNER_V172_BE_APPLIED`: the Runner SL break-even request was sent and attached.
- `ZONEBIRTH_RUNNER_V172_BE_SKIPPED_MARKET_CROSSED`: break-even was invalid relative to the trigger-bar close, so the original Runner SL remained active.
- `ZONEBIRTH_RUNNER_V172_BE_MODIFY_FAIL` / `ZONEBIRTH_RUNNER_V172_MODIFY_FAILED`: the asynchronous request or ATAS callback reported a failed Runner SL modification.
- `ZONEBIRTH_SPLIT_V173_READY`: the v1.73 Base `2.5R` and Runner `4R` OCO groups were both submitted.
- `ZONEBIRTH_RUNNER_V173_BASE_TP_TRIGGERED`: the Base TP actually filled while the Runner leg was still open, making the Runner eligible for break-even protection.
- `ZONEBIRTH_RUNNER_V173_BE_MODIFY_SEND` / `ZONEBIRTH_RUNNER_V173_BE_APPLIED`: the post-Base-TP Runner break-even request was sent and attached.
- `ZONEBIRTH_RUNNER_V173_BE_SKIPPED_MARKET_CROSSED`: break-even was invalid relative to the current replay candle close, so the original Runner SL remained active.
- `ZONEBIRTH_RUNNER_V173_BE_MODIFY_FAIL` / `ZONEBIRTH_RUNNER_V173_MODIFY_FAILED`: the asynchronous request or ATAS callback reported a failed v1.73 Runner SL modification.
- `ENTRY_FILL_QUARANTINED_V174`: an out-of-range entry fill was isolated, its normal daily counters were rolled back, and only the filled quantity was scheduled for emergency flattening.
- `ENTRY_FILL_QUARANTINE_COMPLETE_V174`: the quarantined filled quantity was flattened and cleanup could proceed without creating a normal execution-trade row.

From `OPF_RESEARCH_0.52`, StrategyEngine writes the mainline Trend Pullback lifecycle more explicitly:

- Candidate signals can advance to `Confirmed`, then a same-bar `Triggered` signal is written with `ResearchPath=TrendPullbackConfirmed`.
- `TrendPullbackConfirmed` is an Actual execution eligible path.
- Third and later pullbacks in the same regime are skipped with `ThirdPullback`.
- `FailureReverse_RetestFailed` signals are written as `Triggered` when the retest failure confirms.

From `OPF_RESEARCH_0.53`, mainline Trend Pullback candidates can start from a post-birth retest of a trend-aligned FVG. Actual exits also write their matching research outcome immediately, covering same-bar TP/SL fills.

From `OPF_RESEARCH_0.54`, research trackers that correspond to an active Actual order are kept alive beyond the normal research window until the Actual exit arrives, so `execution_trades.csv` and Actual-verified `research_outcomes.csv` can stay in one-to-one alignment.

From `OPF_RESEARCH_0.55`, mainline `TrendPullbackConfirmed` no longer triggers on the same confirmation bar. After `Confirmed`, it waits up to two closed bars for a conservative retrace back to the trade zone boundary. If no retrace appears, `no_trade.csv` records `NoRetraceAfterConfirm`; if it appears, the triggered signal includes `TriggeredAfterConfirmRetrace`.

Also from `OPF_RESEARCH_0.55`, risk evaluation uses the profile volatility-adjusted limit from the main spec: `MaxAllowedRiskPoints = min(MaxRiskPointsHard, max(12, ATR14 * AtrRiskMultiplier))`. `risk_evaluations.csv` now includes `ATR14`, `AtrRiskMultiplier`, and `MaxAllowedRiskPoints`; Actual execution can skip rows with `RiskTooWideVolAdjusted`.

From `OPF_RESEARCH_0.56`, `SetupQualityScore` no longer gives the `OrderFlow` component a fixed placeholder score. `OPF_OF_0.2` uses a price-flow proxy based on the signal bar body direction and close location within the bar range. This is a scoring component only, not a hard filter and not a final replacement for true ATAS delta/order-flow inputs.

From `OPF_RESEARCH_0.57`, `research_outcomes.csv` includes the main spec outcome-efficiency fields:

- `ExitEfficiency`: `ActualPnL_R / ActualMFE_R` when the outcome is Actual-verified; otherwise research-only rows keep the research OHLC context.
- `RunupCapturePct`: actual captured points divided by `MFEPoints`.
- `AdverseBeforeProfit_R`: heat before the first 1R move normalized by initial risk.

From `OPF_RESEARCH_0.58`, Actual execution defaults use `ACTUAL_EXEC_0.8` and lower `ActualTargetR` from `2.0R` to `1.5R`. This is a controlled exit-capture experiment only; StrategyEngine entry rules and executable paths are unchanged.

From `OPF_RESEARCH_0.59`, Actual execution defaults use `ACTUAL_EXEC_0.9` and add `FailureReverse_ObservationInvalidated` to the executable path whitelist. `ActualRequireFailureRetest` defaults to `false` for this controlled whitelist experiment, while `ActualTargetR` remains `1.5R`.

From `OPF_RESEARCH_0.60`, Actual execution defaults use `ACTUAL_EXEC_1.0`. `FailureReverse_ObservationInvalidated` uses a path-aware strategy-quality gate: it can bypass the original-trend regime requirement and uses `ActualFailureReverseMinSetupQualityScore=40`, while all other Actual paths keep `ActualMinSetupQualityScore=70`. Risk, estimated RR, active-trade, daily guard, and protection-cleanup checks are unchanged.

From `OPF_RESEARCH_0.61`, Actual execution defaults use `ACTUAL_EXEC_1.1`. `ObservationConfirm` is removed from the Actual path list and remains research-only. Immediate `FailureReverse_ObservationInvalidated` Long entries are skipped with `FailureImmediateLongDisabled`, while Short entries remain eligible. Research rows for all paths continue to be logged.

From `OPF_RESEARCH_0.62`, Actual execution defaults use `ACTUAL_EXEC_1.2`. `BreakawayFvg` Long entries are skipped with `BreakawayLongDisabled`, while Breakaway Short remains eligible. Research rows for Breakaway Long continue to be logged for comparison.

From `OPF_RESEARCH_0.63`, Actual execution defaults use `ACTUAL_EXEC_1.3`. If both protective orders are no longer working while an Actual entry remains open and no exit fill was recorded, the strategy logs `PROTECTION_LOST_BEFORE_EXIT`, sends an emergency `FLATTEN` order, and records the `FLATTEN` fill as an execution trade.

From `OPF_RESEARCH_0.64`, Actual execution defaults use `ACTUAL_EXEC_1.4`. This aligns the code default with the runtime JSON template and runs the protection-loss check immediately after bracket submission completes.

From `OPF_RESEARCH_0.65`, Actual execution defaults use `ACTUAL_EXEC_1.5`. If the strategy stops while an Actual execution is still open, it logs `ACTIVE_ON_STOP`, attempts a `FLATTEN`, and writes a `STOPPED` execution-trade row using the last known candle close.

From `OPF_RESEARCH_0.66`, Actual execution defaults use `ACTUAL_EXEC_1.6`. `TrendPullbackConfirmed` Long entries are skipped with `TrendPullbackLongDisabled`, while Trend Pullback Short remains eligible.

From `OPF_RESEARCH_0.67`, Actual execution defaults use `ACTUAL_EXEC_1.7`. `ObservationStrict_BullFresh` is added to the Actual path whitelist as a controlled sample-size expansion.

From `OPF_RESEARCH_0.68`, Actual execution defaults use `ACTUAL_EXEC_1.8`. `ShadowCandidate` is added to the Actual path whitelist and executable path allowlist as a second controlled sample-size expansion.

From `OPF_RESEARCH_0.69`, Actual execution defaults use `ACTUAL_EXEC_1.9`. `ShadowCandidate` uses the configured fixed target R as its estimated reward model for Actual RR checks so this early candidate path is not filtered solely by nearby structure.

From `OPF_RESEARCH_0.70`, Actual execution defaults use `ACTUAL_EXEC_1.10`. `ShadowCandidate` is removed from the Actual path whitelist and remains research-only, while `TrendPullbackConfirmed` Long entries are allowed again if they pass strategy-quality and risk/RR checks.

From `OPF_RESEARCH_0.71`, Actual execution defaults use `ACTUAL_EXEC_1.11`. `ObservationStrict_BullFresh` is removed from the Actual path whitelist and remains research-only. `BreakawayFvg` Long entries are allowed again if they pass strategy-quality and risk/RR checks.

From `OPF_RESEARCH_0.72`, stop-time execution rows use `STOPPED_NO_ENTRY` with zero PnL when the strategy stops before an entry fill is confirmed. This prevents synthetic stop handling from creating execution trades with `EntryPrice=0`.

From `OPF_RESEARCH_0.73`, Actual execution defaults use `ACTUAL_EXEC_1.13`. Broad `BreakawayFvg` remains research-only, and the narrower `BreakawayFvg_Qualified` path is eligible for Actual execution when the breakaway is a fresh bullish FVG with passed trend regime, setup quality at least 80, risk no wider than 8.5 points, and confirmation heat no more than 0.5R.

From `OPF_RESEARCH_0.74`, Actual execution defaults use `ACTUAL_EXEC_1.14`. `TrendPullbackConfirmed` is removed from the Actual whitelist and remains research-only. `FailureReverse_LongQualified` is added as a separately labeled executable path for immediate Long failure-reverse signals with risk no wider than 8.5 points and entry-bar heat no more than 0.5R.

From `OPF_RESEARCH_0.75`, Actual execution defaults use `ACTUAL_EXEC_1.15`. `FailureReverse_LongQualified` keeps the same path label but widens eligibility to immediate Long failure-reverse signals with risk no wider than 8.5 points. Entry-bar heat is recorded as `HeatR` in signal reasons for research instead of blocking the path.

From `OPF_RESEARCH_0.76`, Actual execution defaults use `ACTUAL_EXEC_1.16`. `FailureReverse_LongQualified` keeps risk no wider than 8.5 points and adds a heat band gate: `1.0 <= HeatR <= 2.0`. The signal still records `HeatR` in signal reasons for audit.

From `OPF_RESEARCH_0.77`, Actual execution defaults use `ACTUAL_EXEC_1.17`. Actual execution tracks its own runup and drawdown from the filled entry price and writes `ActualMFEPoints`, `ActualMAEPoints`, `ActualMFE_R`, and `ActualMAE_R` to both `execution_trades.csv` and Actual-verified `research_outcomes.csv`. This is a logging and interpretation change only; executable paths and entry filters are unchanged.

From `OPF_RESEARCH_0.78`, Actual execution defaults use `ACTUAL_EXEC_1.18`. Actual execution skips entries whose signal bar already touched the planned stop or target, logging `SKIP_SAME_BAR_AMBIGUOUS` with `EntryBarStopTouched` and/or `EntryBarTargetTouched`. Research rows are still recorded, but these ambiguous same-bar cases are not sent as Actual orders.

From `OPF_RESEARCH_0.79`, Actual execution defaults use `ACTUAL_EXEC_1.19`. `BreakawayFvg` is added back to `ActualExecutionPaths` as a controlled sample-size expansion for high-quality breakaway opportunities. The path remains subject to strategy-quality, risk/RR, daily guard, active-trade, and same-bar ambiguity checks.

After `OPF_RESEARCH_0.79`, research priorities are weighted toward evidence accumulation before further setup expansion: Edge Attribution and controlled variables 30%, Funnel diagnostics 20%, existing setup evidence depth 15%, Unknown regime decomposition 10%, time-of-day analysis 10%, execution stability and net PnL model 10%, and benchmark comparison 5%.

From `OPF_RESEARCH_0.80`, Actual execution defaults use `ACTUAL_EXEC_1.20`. This is a HUD-only execution visibility update: no CSV schema, setup, risk, or order behavior changes. The chart HUD now displays daily order counts, TP/SL/Other exit counts, daily NetR, active order levels, last order, and recent completed orders.

From `OPF_RESEARCH_0.81`, Actual execution defaults use `ACTUAL_EXEC_1.21` with unchanged execution parameters. The evidence-accumulation schema adds `*_edge_attribution.csv` and `*_funnel_events.csv`, expands `RegimeUnknown` skip reasons with failed trend-score components, and combines the HUD daily order/PnL display into one line.

From `OPF_RESEARCH_0.82`, Actual execution defaults use `ACTUAL_EXEC_1.22` with unchanged trading parameters. Actual-verified `ExitEfficiency` and `RunupCapturePct` use Actual execution MFE fields, and execution funnel rows include `ExecutionScope`.

From `OPF_RESEARCH_0.83`, Actual execution defaults use `ACTUAL_EXEC_1.23`. `ActualExecutionSettings` includes `ActualBreakawayMaxRiskPoints`, defaulting to `11`. Broad `BreakawayFvg` Actual orders with risk above this cap are skipped with `BreakawayRiskCapExceeded`, while research rows continue to be written for long-period comparison.

From `OPF_RESEARCH_0.84`, Actual execution defaults use `ACTUAL_EXEC_1.24`. `ObservationConfirm` is added to the Actual path whitelist for controlled volume expansion, `ActualMaxTradesPerDay` is raised to `20`, and `ActualExecutionSettings` includes `ActualObservationConfirmMaxRiskPoints`, defaulting to `11`. ObservationConfirm rows above this cap are skipped with `ObservationConfirmRiskCapExceeded`; eligible ObservationConfirm rows use the fixed `ActualTargetR=1.5` reward model for Actual RR checks.

From `OPF_RESEARCH_0.85`, Actual execution defaults use `ACTUAL_EXEC_1.25`. `ActualExecutionSettings` includes `ActualObservationConfirmMinSetupQualityScore`, defaulting to `56`, so ObservationConfirm can expand sample size without changing other paths' setup-quality floor. `execution_trades.csv` also includes `PlannedRiskPoints`, `FilledRiskPoints`, and `RiskDriftPoints` for auditing planned risk caps against filled bracket risk.

From `OPF_RESEARCH_0.86`, Actual execution defaults use `ACTUAL_EXEC_1.26`. `ActualExecutionSettings` includes `ActualFailureRetestMaxRiskPoints` and `ActualFailureRetestMinSetupQualityScore`, both used only by `FailureReverse_RetestFailed` Actual execution. Skips above this risk cap are logged with `FailureRetestRiskCapExceeded`.

From `OPF_RESEARCH_0.87d`, Actual execution defaults use `ACTUAL_EXEC_1.27`. `ActualExecutionSettings` includes `ActualObservationConfirmFillerMinSetupQualityScore`, `ActualObservationConfirmMaxFillerTradesPerDay`, and `ActualObservationConfirmFillerUntilDailyTrades`. These fields allow limited `ObservationConfirm` filler executions when daily order count is still below the target; executed filler rows are marked with `ObservationFiller` in `execution_decisions.csv`.

From `OPF_RESEARCH_0.88`, Actual execution defaults use `ACTUAL_EXEC_1.28`. `ActualExecutionSettings` includes `ActualObservationConfirmVolumeMaxRiskPoints`, defaulting to `12.5`. This expanded risk cap applies only to `ObservationConfirm` rows with `SetupQualityScore >= ActualObservationConfirmMinSetupQualityScore` while the current daily Actual order count is still below `ActualObservationConfirmFillerUntilDailyTrades`; executions using it are marked with `ObservationRiskExpansion` in `execution_decisions.csv`.

From `OPF_RESEARCH_0.89`, Actual execution defaults use `ACTUAL_EXEC_1.29`. `ActualObservationConfirmVolumeMaxRiskPoints` is set back to `11`, disabling the v0.88 Observation risk expansion by default. `FailureReverse_RetestFailed_WideStop1_5R` is added to `ActualExecutionPaths` as an existing-path volume supplement and can execute only while the current daily Actual order count is below `ActualObservationConfirmFillerUntilDailyTrades`; these executions are marked with `FailureRetestWideFiller` in `execution_decisions.csv`.

From `OPF_RESEARCH_0.90`, Actual execution defaults use `ACTUAL_EXEC_1.30`. `execution_trades.csv` includes execution-quality fields: `IsAbnormalExecution`, `AbnormalReason`, `ExpectedExitPrice`, `ExitPriceDriftPoints`, `RawPoints`, `RawDollars`, and `RawPointsR`. Abnormal executions keep the original fill information in raw fields, while the normal `Points`, `Dollars`, and `PointsR` fields are zeroed so aggregate statistics can exclude bad replay fills by default. `execution_events.csv` also writes `ABNORMAL_EXECUTION` for these fills.

From `OPF_RESEARCH_0.91`, Actual execution defaults use `ACTUAL_EXEC_1.31`. The abnormal fill tolerance is widened to 16 ticks / 4 points, whichever is larger, so ordinary replay entry and stop slippage is not zeroed as corrupted execution data. Large TP/SL drift and abnormal exit roles remain isolated through the same `IsAbnormalExecution`, `AbnormalReason`, raw-value, and `ABNORMAL_EXECUTION` fields.

From `OPF_RESEARCH_0.92`, Actual execution defaults use `ACTUAL_EXEC_1.32`. Entry fills farther than the abnormal-fill tolerance from the planned entry are rejected before bracket submission. `execution_events.csv` writes `ENTRY_FILL_REJECTED`, no SL/TP bracket is submitted from the bad fill price, and the strategy sends an emergency flatten order for the filled quantity.

From `OPF_RESEARCH_0.93`, Actual execution defaults use `ACTUAL_EXEC_1.33`. `execution_events.csv` and `research.log` include richer order provenance for attach and fill callbacks, including `ExtId`, order state, type, direction, order price, trigger price, and unfilled quantity. Comment-matched trade callbacks whose order `ExtId` does not match the expected active entry, SL, or TP order write `TRADE_ORDER_MISMATCH` and are ignored.

From `OPF_RESEARCH_0.94`, Actual execution defaults use `ACTUAL_EXEC_1.34`. `StructureConfirmShadow_ConfirmBarStop_Min10` is promoted to Actual execution and `TrendPullbackConfirmed` is added to the default Actual path list. These are existing Trend Pullback / structure-confirmation research paths and keep the current strategy-quality, risk/RR, same-bar ambiguity, active-trade, and execution safety checks.

From `OPF_RESEARCH_0.95`, Actual execution defaults use `ACTUAL_EXEC_1.35`. `StructureConfirmShadow_ConfirmBarStop_Min10` uses fixed `ActualTargetR=1.5` for Actual RR checks rather than `NearestStructure`, and `StructureConfirmShadow_ConfirmBarStop_Wait1` is added to the default Actual path list. Both remain existing structure-confirmation variants and keep the current strategy-quality, risk, same-bar ambiguity, active-trade, and execution safety checks.

From `OPF_RESEARCH_0.96`, Actual execution defaults use `ACTUAL_EXEC_1.36`. `StructureConfirmShadow_ConfirmBarStop` and `StructureConfirmShadow_SwingStop` are added to the default Actual path list, and all `StructureConfirmShadow_*` paths use fixed `ActualTargetR=1.5` for Actual RR checks. `ObservationConfirm` filler widens to `50 <= SetupQualityScore < 56` with up to three filler executions per day before the daily 5-trade target is reached; the 11-point `ObservationConfirm` risk cap, target R, time policy, and setup families are unchanged.

From `OPF_RESEARCH_0.97`, Actual execution defaults use `ACTUAL_EXEC_1.37`. `ObservationConfirm` filler widens to `48 <= SetupQualityScore < 56` while keeping the three-filler daily cap and the daily 5-trade activation threshold. Executed filler rows with `SetupQualityScore < 50` include `DailyVolumeFiller48` in the `Reason` field of `execution_decisions.csv`; the 11-point `ObservationConfirm` risk cap, target R, time policy, and setup families are unchanged.

From `OPF_RESEARCH_0.98`, Actual execution defaults use `ACTUAL_EXEC_1.38` with unchanged trading rules from v0.97. Protection cleanup adds an in-progress guard to prevent overlapping `StrategyStopped` and `OnOrderChanged` cleanup attempts. A first post-cancel check that still sees working protection orders writes `PROTECTION_CLEANUP_RETRY_PENDING`; `PROTECTION_STALE_AFTER_EXIT` is reserved for repeated cleanup attempts that still cannot clear protection orders.

From `OPF_RESEARCH_0.99`, Actual execution defaults use `ACTUAL_EXEC_1.39`. `ActualObservationConfirmVolumeMaxRiskPoints` is raised from 11 to 12 for high-quality `ObservationConfirm` rows while the daily Actual count is still below 5. Lower-score filler rows still use the base 11-point cap. Executions using this band include `ObservationRiskExpansion` in the `Reason` field of `execution_decisions.csv`; same-bar ambiguity checks, target R, time policy, and setup families are unchanged.

From `OPF_RESEARCH_1.00`, Actual execution defaults use `ACTUAL_EXEC_1.40`. `ActualObservationConfirmVolumeMaxRiskPoints` is set back to 11, disabling the v0.99 `ObservationRiskExpansion` band. The v0.97 `DailyVolumeFiller48` rule and v0.98 protection-cleanup guard remain active; Actual paths, base risk caps, score thresholds, target R, time policy, and setup families are otherwise unchanged.

From `OPF_RESEARCH_1.01`, Actual execution defaults use `ACTUAL_EXEC_1.41`. `ObservationConfirm` filler widens to `42 <= SetupQualityScore < 56` and the daily filler cap rises to 5 while the daily Actual count is below 5. Executed filler rows with `SetupQualityScore < 48` include `DailyVolumeFiller42`; rows with `SetupQualityScore < 50` continue to include `DailyVolumeFiller48`. Same-bar target-only `ObservationConfirm` executions on low-trade days include `DailyVolumeSameBarTargetOnly`; rows that touch the stop on the entry bar remain skipped. Base risk caps, target R, time policy, and setup families are unchanged.

From `OPF_RESEARCH_1.02`, Actual execution defaults use `ACTUAL_EXEC_1.42`. `ObservationConfirm` filler returns to `48 <= SetupQualityScore < 56` with a daily cap of 3, and v1.01 `DailyVolumeFiller42` / `DailyVolumeSameBarTargetOnly` execution labels should no longer appear. Low-trade-day research fillers can execute `AlmostConfirmed`, `StructureConfirmShadow`, or `ZoneBirthResearch` only before the day reaches 5 Actual trades. These rows include `DailyVolumeResearchFiller:<path>` in `execution_decisions.csv` and must still pass normal trend, quality, RR, same-bar stop safety, and `risk <= 11` checks.

From `OPF_RESEARCH_1.03`, Actual execution defaults use `ACTUAL_EXEC_1.43`. Low-trade-day `ObservationConfirm` rows with `SetupQualityScore >= 56` can use `ActualObservationConfirmVolumeMaxRiskPoints=16` until the day reaches 5 Actual trades. Executed rows in the expanded 11-16 point band include `ObservationRiskExpansion` and `DailyVolumeRisk16` in `execution_decisions.csv`. If the actual entry fill changes risk beyond the saved per-trade limit, `execution_events.csv` records `ENTRY_FILLED_RISK_EXCEEDED` and the strategy sends `EMERGENCY_FLATTEN_SEND` instead of submitting bracket protection.

From `OPF_RESEARCH_1.04`, Actual execution defaults use `ACTUAL_EXEC_1.44`. The v1.03 `DailyVolumeRisk16` risk-band experiment is retired. Low-trade-day floor executions can use `ObservationConfirm_WideStop1_5R` or `FailureReverse_RetestFailed_WideStop1_5R` before the day reaches 5 Actual trades, with `SetupQualityScore >= 48`, `risk <= 18`, and `EstimatedRR >= 0.5`. These rows include `DailyVolumeFloor:<path>` in `execution_decisions.csv`; summarize them separately by path, side, TP/SL, NetR, and NetDollars.

From `OPF_RESEARCH_1.05`, Actual execution defaults use `ACTUAL_EXEC_1.45`. Low-volume-day base `ObservationConfirm` rows can use the 11-18 point risk band before the day reaches 5 Actual trades, with `SetupQualityScore >= 48` and normal `EstimatedRR >= 1.0`. These rows include `DailyVolumeBaseRisk18` in `execution_decisions.csv`. This label is separate from `DailyVolumeFloor`, which remains reserved for wide-stop path executions.

`OPF_RESEARCH_1.05` / `ACTUAL_EXEC_1.45` is the volume baseline and rollback target for the next optimization phase. Baseline comparisons should report daily trades, long/short counts, TP/SL/Other, NetR, NetDollars, abnormal executions, and `DailyVolumeBaseRisk18` subgroup performance.

From `OPF_RESEARCH_1.10`, Actual execution defaults use `ACTUAL_EXEC_1.46`. Broad `BreakawayFvg` Actual orders are skipped with `BreakawayBroadDisabledV110`, while `BreakawayFvg_Qualified` remains independently testable. Long `ObservationConfirm` rows in the expanded 11-18 point risk band are skipped with `LongObservationRiskExpansionQualityCutV110` when `SetupQualityScore < 70`. Research rows continue to be written for skipped signals. Actual-verified research outcome duplicate protection uses the Actual trade id so the lifecycle audit should report zero duplicate Actual-verified rows.

From `OPF_RESEARCH_1.11`, Actual execution defaults use `ACTUAL_EXEC_1.47`. Long `ObservationConfirm` rows in the expanded 11-18 point risk band are skipped with `LongObservationRiskExpansionDisabledV111` regardless of setup score. Short expanded-risk `ObservationConfirm` rows remain allowed. Actual-verified research outcome duplicate protection keys directly on `ActualTradeID`, so adjacent tracker entry bars for the same actual trade should not create duplicate Actual-verified rows.

From `OPF_RESEARCH_1.12`, Actual execution defaults use `ACTUAL_EXEC_1.48`. Short `ObservationConfirm` rows with `8 < risk <= 15` and `SetupQualityScore < 60` are skipped with `ShortObservationMidRiskQualityCutV112`. The 15-18 point Short expanded-risk band remains allowed. This version is intended to test whether the negative Short mid-risk subgroup can be reduced without changing setup families or adding time filters.

From `OPF_RESEARCH_1.13`, Actual execution defaults use `ACTUAL_EXEC_1.49` with unchanged executable rules from v1.12. `research_outcomes.csv` adds `Hit2_5R`, `Hit3R`, `First2_5RBar`, and `First3RBar`. `exit_policy_evaluations.csv` adds `Fixed3R`. This version is a profit-extension research pass for deciding whether later versions should tier TP beyond the current fixed 1.5R actual target.

From `OPF_RESEARCH_1.14`, Actual execution defaults use `ACTUAL_EXEC_1.50`. Entry rules remain unchanged from v1.12/v1.13, but Long `ObservationConfirm` executions with `11 < InitialRiskPoints <= 15` use `TargetR=3`, and Long `ObservationConfirm_WideStop1_5R` executions with `11 < InitialRiskPoints <= 15` use `TargetR=2.5`. Other rows remain at the default `ActualTargetR=1.5`. Executed override rows include `LongProfitExtensionV114` in `execution_decisions.csv`, and `execution_trades.csv` records the per-trade `TargetR`.

From `OPF_RESEARCH_1.15`, Actual execution defaults use `ACTUAL_EXEC_1.51`. The final `TargetR` is recalculated after entry fill / bracket reprice from filled risk, and `execution_trades.csv` adds `PlannedTargetR` and `TargetRDrift`. `exit_policy_evaluations.csv` adds protected-extension policies `ProtectBE_Then2_5R`, `Protect1R_Then2_5R`, `ProtectBE_Then3R`, and `Protect1R_Then3R`; these are research-only rows and do not submit live/replay stop modifications.

From `OPF_RESEARCH_1.16`, Actual execution defaults use `ACTUAL_EXEC_1.52`. `OnStopped()` prevents new Actual entries while the strategy is closing, logging `StrategyStopping` / `SKIP_STRATEGY_STOPPING` instead. `execution_events.csv` also records `ORDER_STATE_FAILED` and `CANCEL_FAIL` so failed/rejected protection-order states and cancel failures are included in lifecycle audits.

From `OPF_RESEARCH_1.17`, Actual execution defaults use `ACTUAL_EXEC_1.53`. Actual entries during the replay stop guard window are skipped with `ReplayStopGuard` / `SKIP_REPLAY_STOP_GUARD`. Stop-time protection cleanup retries up to three times before leaving a pending/stale audit trail.

From `OPF_RESEARCH_1.18`, Actual execution defaults use `ACTUAL_EXEC_1.54`. Low-volume-day `ObservationConfirm` rows can execute as `DailyVolumeQualityRescueV118` when the day has fewer than 5 Actual trades, `SetupQualityScore >= 70`, risk is between 18 and 22 points, and `EstimatedRR >= 0.8`. This is a tagged volume experiment, not a new setup family.

From `OPF_RESEARCH_1.19`, Actual execution defaults use `ACTUAL_EXEC_1.55`. Low-volume-day `ObservationConfirm` quality rescue is retagged as `DailyVolumeQualityRescueV119` and uses a tighter `21.5` point planned-risk cap. `FailureReverse_ObservationInvalidated_WideStop1_5R` is added to Actual execution as a low-volume-day `DailyVolumeFloor` filler path and is tagged with `FailureInvalidatedWideFillerV119` when executed. Long immediate-failure invalidated entries remain disabled.

From `OPF_RESEARCH_1.20`, Actual execution defaults use `ACTUAL_EXEC_1.56`. `FailureReverse_ObservationInvalidated_WideStop1_5R` is removed from Actual execution. `DailyVolumeQualityRescueV120` uses the v1.18 `22` point planned-risk cap. `AlmostConfirmed` and `ShadowCandidate` can execute as low-volume fillers before 5 Actual trades/day with `SetupQualityScore >= 60`, risk `<= 18`, and `EstimatedRR >= 0.8`; executions are tagged `AlmostConfirmedFillerV120` or `ShadowCandidateFillerV120`. Filled-risk drift within `1` point of the path cap is logged as `ENTRY_FILLED_RISK_DRIFT_ACCEPTED` / `RiskDriftAcceptedV120` instead of emergency flattening.

From `OPF_RESEARCH_1.21`, Actual execution defaults use `ACTUAL_EXEC_1.57`. `AlmostConfirmed` and `ShadowCandidate` expansion is no longer restricted to days below 5 Actual trades; both paths can execute throughout the session under the same `SetupQualityScore >= 60`, risk `<= 18`, and `EstimatedRR >= 0.8` guardrails. Executions are tagged `AlmostConfirmedFillerV121` or `ShadowCandidateFillerV121`.

From `OPF_RESEARCH_1.22`, Actual execution defaults use `ACTUAL_EXEC_1.58`. The version is a volume-discovery build for the June 200-trade target. `ActualMaxTradesPerDay` defaults to `12`. `ZoneBirthResearch` can execute as `ZoneBirthVolumeV122` with `SetupQualityScore >= 40`, risk `<= 25`, and `EstimatedRR >= 0.3`. `ObservationStrict_BullFresh` and `ObservationStrict_Other` can execute as `StrictObservationVolumeV122` with `SetupQualityScore >= 45`, risk `<= 25`, and `EstimatedRR >= 0.5`. These paths bypass the trend-regime hard gate and ATR-adjusted risk cap, but still respect the instrument hard risk cap, active-trade protection, same-bar ambiguity checks, replay stop guard, and one-contract execution profile. `ShadowCandidate` remains enabled and is tagged `ShadowCandidateFillerV122`; `AlmostConfirmed` is disabled for Actual execution and skipped with `AlmostConfirmedActualDisabledV122`.

From `OPF_RESEARCH_1.23`, Actual execution defaults use `ACTUAL_EXEC_1.59`. The version keeps the June 200-trade volume goal but replaces the worst v1.22 pool. `ObservationStrict_BullFresh` Actual execution is disabled with `StrictBullFreshActualDisabledV123`, and `ZoneBirthResearch` Short Actual execution is disabled with `ZoneBirthShortActualDisabledV123`. `BreakawayFvg` can execute as `BreakawayVolumeV123` with `SetupQualityScore >= 80`, risk `<= 25`, and `EstimatedRR >= 1.0`. `ObservationConfirm` Long can execute as `ObservationLongVolumeV123` with `SetupQualityScore >= 56`, `11 < risk <= 18`, and `EstimatedRR >= 0.8`. `ActualMaxTradesPerDay` remains `12`.

From `OPF_RESEARCH_1.24`, Actual execution defaults use `ACTUAL_EXEC_1.60`. The version keeps the June 200-trade mainline target and tags each volume source separately. `BreakawayVolumeV123` raises its experiment risk cap to `30` points while keeping `SetupQualityScore >= 80` and `EstimatedRR >= 1.0`. `ObservationLongVolumeV123` is tightened to `SetupQualityScore >= 70`, `11 < risk <= 18`, and `EstimatedRR >= 0.8`. `ZoneBirthVolumeV122` raises its minimum setup quality to `45`; `ZoneBirthResearch` Short remains disabled. `UnknownRegimeZoneTouch` can execute as `UnknownMicroRiskVolumeV124` with `SetupQualityScore >= 25`, risk `<= 8.5`, and `EstimatedRR >= 1.5`. `ObservationConfirm` Short can execute as `ObservationShortVolumeV124` with `SetupQualityScore >= 60`, `11 < risk <= 18`, and `EstimatedRR >= 1.0`. These tags appear in `execution_decisions.csv` `Reason` so the next replay can compare volume and profitability by pool.

From `OPF_RESEARCH_1.25`, Actual execution defaults use `ACTUAL_EXEC_1.61`. `UnknownRegimeZoneTouch` executions are tagged as `UnknownMicroRiskVolumeV125`, require `SetupQualityScore >= 35`, risk `<= 8.5`, and use the actual fixed `1.5R` target model for `EstimatedRewardPoints`, `RewardModel`, and `EstimatedRR` in `execution_decisions.csv`. This replaces the v1.24 nearest-structure RR check that blocked the micro-risk pool from executing. Stop-time no-entry cancellations remain in `execution_events.csv` as `ACTIVE_ON_STOP` / `EXIT_ABORTED_NO_ENTRY`, but they no longer create synthetic `STOPPED_NO_ENTRY` rows in `execution_trades.csv`.

From `OPF_RESEARCH_1.26`, Actual execution defaults use `ACTUAL_EXEC_1.62`. `UnknownRegimeZoneTouch` executions are tagged as `UnknownMicroRiskVolumeV126`, require `SetupQualityScore >= 45`, risk `<= 8.5`, and use an exact fixed `1.5R` reward distance for Actual RR checks. The `RewardModel` is `UnknownMicroRiskVolumeV126TargetR:1.5`, which prevents valid fixed-target rows from being skipped by pre-division reward rounding such as `EstimatedRR=1.499x`. No CSV columns are added; analyze the experiment through existing `execution_decisions.csv` `Reason`, `EstimatedRewardPoints`, `RewardModel`, `EstimatedRR`, and `execution_trades.csv` PnL fields.

From `OPF_RESEARCH_1.27`, Actual execution defaults use `ACTUAL_EXEC_1.63`. `BreakawayFvg` executions are tagged as `BreakawayVolumeV127`, require `SetupQualityScore >= 80`, risk `<= 30`, and `EstimatedRR >= 1.0`. This tagged path bypasses the instrument `MaxRiskPointsHard=25` guard only for Breakaway candidates that remain within the explicit `30` point experiment cap. `UnknownRegimeZoneTouch` Actual execution is disabled and skipped with `UnknownMicroRiskActualDisabledV127`; research rows for that path continue to be written. No CSV columns are added.

From `OPF_RESEARCH_1.28`, Actual execution defaults use `ACTUAL_EXEC_1.64`. `BreakawayFvg` executions use `BreakawayVolumeV128` for normal Breakaway Actual trades and `BreakawayShortWideV128` for Short entries with `25 < risk <= 30`. Long Breakaway risk is capped at `25`; Short Breakaway risk is capped at `30`. Breakaway entry-fill risk drift above the applicable side cap is rejected with `ENTRY_FILLED_RISK_EXCEEDED` instead of being accepted by the general 1-point drift tolerance. No CSV columns are added.

From `OPF_RESEARCH_1.29`, Actual execution defaults use `ACTUAL_EXEC_1.65`. `ObservationConfirm_WideStop1_5R` is removed from default Actual execution and is skipped with `ObservationConfirmWideStopActualDisabledV129` if enabled by an older runtime config. Research rows for that path continue to be written. No CSV columns are added.

From `OPF_RESEARCH_1.30`, Actual execution defaults use `ACTUAL_EXEC_1.66`. `AlmostConfirmed` can execute as `AlmostConfirmedVolumeV130` with the mainline filler guardrails. `ObservationStrict_BullFresh` can execute as `StrictBullFreshVolumeV130` with the strict-volume guardrails. `ObservationConfirm_WideStop1_5R` remains Actual-disabled. No CSV columns are added; use `execution_decisions.csv` `Reason` tags to separate the new v1.30 volume pools.

From `OPF_RESEARCH_1.31`, Actual execution defaults use `ACTUAL_EXEC_1.67`. This is a June 200-trade volume recovery build after v1.30. `AlmostConfirmed` and `ObservationStrict_BullFresh` are disabled for Actual execution and skipped with `AlmostConfirmedActualDisabledV131` / `StrictBullFreshActualDisabledV131` even if an older runtime config includes them. `ObservationConfirm_WideStop1_5R` is reopened as the controlled volume pool with side-separated quality gates: Long requires `SetupQualityScore >= 70`, Short requires `SetupQualityScore >= 60`, planned/fill risk must be `<= 22`, and `EstimatedRR >= 0.8`. Executions are tagged in `execution_decisions.csv` as `OCWideStopLongRisk18V131`, `OCWideStopLongRisk22V131`, `OCWideStopShortRisk18V131`, or `OCWideStopShortRisk22V131`. The next monthly replay should compare these four tags against v1.28/v1.30 while confirming Breakaway remains positive and total monthly volume moves toward 180-220 trades.

From `OPF_RESEARCH_1.32`, Actual execution defaults use `ACTUAL_EXEC_1.68`. This version keeps the v1.31 profitable skeleton and adds two controlled low-risk volume layers for the May replay. Base `ObservationConfirm` rows with `SetupQualityScore >= 70`, `18 < risk <= 22`, and `EstimatedRR >= 0.8` can execute throughout the day and are tagged `ObservationConfirmRisk22V132`. `ObservationConfirm_WideStop1_5R` rows that do not already qualify for the v1.31 side-specific gate can execute as `OCWideStopLowRiskV132` when `SetupQualityScore >= 56`, `risk <= 18`, and `EstimatedRR >= 0.5`. These tags should be summarized separately from the v1.31 `OCWideStop...V131` tags so the next replay can verify whether the added volume improves total trades without damaging NetR/NetDollars.

From `OPF_RESEARCH_1.33`, Actual execution defaults use `ACTUAL_EXEC_1.69`. This is a protection build after the May v1.32 replay: v1.32 `ObservationConfirmRisk22V132` and `OCWideStopLowRiskV132` remain active, `BreakawayFvg` Short remains testable, but `BreakawayFvg` Long is skipped with `BreakawayLongActualDisabledV133` and `ZoneBirthResearch` Long is skipped with `ZoneBirthLongActualDisabledV133`. Research rows continue to be written for both disabled Long pools. The next monthly replay should verify whether removing the May drag from Breakaway Long and ZoneBirth Long improves NetR/NetDollars while keeping average daily trades near or above the 5-trade baseline.

From `OPF_RESEARCH_1.34`, Actual execution defaults use `ACTUAL_EXEC_1.70`. This version keeps the v1.33 protection structure but selectively reopens only the highest-quality `BreakawayFvg` Long subset for cross-month validation. Long Breakaway rows can execute as `BreakawayLongSelectiveV134` only when `SetupQualityScore >= 95`, planned/fill risk is `<= 18`, and `EstimatedRR >= 1.5`; lower-quality Long Breakaway rows are skipped with `BreakawayLongSelectiveV134QualityTooLow` or `BreakawayLongSelectiveV134RiskCapExceeded`. `BreakawayFvg` Short remains under the v1.28 rules, and `ZoneBirthResearch` Long remains disabled with `ZoneBirthLongActualDisabledV133`. The next April and May replays should compare whether this selective Long restore adds volume without bringing back the May Breakaway Long drag.

From `OPF_RESEARCH_1.35`, Actual execution defaults use `ACTUAL_EXEC_1.71`. After the April/May/June large-sample review, the v1.34 selective `BreakawayFvg` Long restore is retired and all Breakaway Long Actual orders are skipped with `BreakawayLongActualDisabledV135`; Breakaway Short remains testable. The default Actual path list is narrowed back to the profitable `ObservationConfirm` skeleton, its controlled wide-stop variant, Breakaway Short, and Failure Retest paths. `ShadowCandidate`, `ObservationStrict_Other`, and `StructureConfirmShadow_*` remain research paths but are blocked from Actual execution with `EvidenceFrozenActualPathV135` so the next replay focuses on OC profit efficiency instead of expanding weak pools.

From `OPF_RESEARCH_1.36`, Actual execution defaults use `ACTUAL_EXEC_1.72`. The v1.35 May replay showed `FailureReverse_RetestFailed_WideStop1_5R` was a small negative Actual pool, so it is removed from the default Actual path list and skipped with `FailureRetestWideStopActualDisabledV136` if an older runtime config still enables it. `ResearchLogMode` is added to `ActualExecutionSettings` and defaults to `Compact` for two-month replay batches: `score_breakdown.csv` is not written, `funnel_events.csv` is limited to execution decisions and research outcomes, and `exit_policy_evaluations.csv` keeps only fixed-target policies for current Actual-skeleton paths. Core files for analysis remain available: config snapshots, signals, no-trade, risk evaluations, research outcomes, execution decisions, execution events, execution trades, edge attribution, candidate/confirmation evaluations, zones, and regime summaries.

From `OPF_RESEARCH_1.37`, Actual execution defaults use `ACTUAL_EXEC_1.73`. Long `ObservationConfirm` rows with `InitialRiskPoints > 12` are skipped with `LongObservationWideRiskCutV137`; the `11 < risk <= 12` band remains eligible and is tagged `ObservationConfirmLongRisk12V137`. No setup family or target model is changed.

From `OPF_RESEARCH_1.38`, Actual execution defaults use `ACTUAL_EXEC_1.74`. `ActualOrderQuantity` defaults to `2`, the strategy default execution profile is `MNQ_2Contract_Evidence`, and daily target/loss guards remain disabled for evidence accumulation. Long `ObservationConfirm` post-fill risk drift above `12` points is rejected with `ENTRY_FILLED_RISK_EXCEEDED`. `exit_policy_evaluations.csv` adds `DynamicPathV138` for research-only TP comparison.

From `OPF_RESEARCH_1.39`, Actual execution defaults use `ACTUAL_EXEC_1.75`. `ObservationConfirm` Actual executions use `TargetR=2.0` and are tagged with `ObservationConfirmTarget2RV139` when the per-path TP override is active. `ObservationConfirm_WideStop1_5R`, Breakaway, Failure Retest, and other current Actual-skeleton paths stay at the default `ActualTargetR=1.5`. `exit_policy_evaluations.csv` writes `DynamicPathV139`, matching the v1.39 actual policy: base `ObservationConfirm=2R`, other current Actual paths `1.5R`.

From `OPF_RESEARCH_1.40`, Actual execution defaults use `ACTUAL_EXEC_1.76`. The v1.39 all-`ObservationConfirm` `2R` override is disabled, so current Actual-skeleton paths use the default `ActualTargetR=1.5`. When the runtime JSON is the two-contract evidence configuration, snapshots use `MNQ_2Contract_Evidence` instead of an old ATAS panel profile value. `exit_policy_evaluations.csv` writes `DynamicPathV140`, which matches the actual v1.40 `1.5R` policy while fixed `2R/2.5R/3R` rows remain research-only comparisons.

Current rollback state: the running baseline was restored to `OPF_RESEARCH_1.37` / `ACTUAL_EXEC_1.73`. Treat v1.38-v1.40 as evidence batches for future optimization, not as the active strategy skeleton.

From `OPF_RESEARCH_1.41`, Actual execution defaults use `ACTUAL_EXEC_1.77`. The active skeleton is v1.37 plus two fixed MNQ contracts through `MNQ_2Contract_Evidence`, fixed `ActualTargetR=1.5`, compact logging, v1.36 path set, and the v1.37 Long `ObservationConfirm` wide-risk cut. Base untagged `ObservationConfirm` Long rows with `SetupQualityScore < 60` are skipped with `OCBaseLongQualityCutV141`; base untagged `ObservationConfirm` Short rows with `8 < InitialRiskPoints <= 11` and `SetupQualityScore < 70` are skipped with `OCBaseShortRisk8_11QualityCutV141`. `OC_Filler`, `ObservationConfirmRisk22V132`, `ObservationConfirmLongRisk12V137`, wide-stop OC tags, Breakaway, Failure Retest, and the default `1.5R` target are unchanged.

From `OPF_RESEARCH_1.42`, Actual execution defaults use `ACTUAL_EXEC_1.78`. The active skeleton returns to v1.37 rules with two fixed MNQ contracts: compact logging, v1.36 path set, fixed `ActualTargetR=1.5`, and the v1.37 Long `ObservationConfirm` wide-risk cut. The v1.41 `OCBaseLongQualityCutV141` and `OCBaseShortRisk8_11QualityCutV141` filters are removed from active execution. Config snapshots resolve the execution profile to `MNQ_2Contract_Evidence` when the loaded JSON is the two-contract evidence preset, preventing stale ATAS panel profile text from labeling a two-contract replay as one-contract.

From `OPF_RESEARCH_1.37_2C`, Actual execution defaults use `ACTUAL_EXEC_1.73_2C`. This is a clean v1.37 skeleton rerun with only `ActualOrderQuantity=2` changed for evidence; `ActualTargetR=1.5`, compact logging, the v1.36 path set, and the v1.37 Long `ObservationConfirm` wide-risk cut remain unchanged. Treat this as a sizing validation batch, not a new strategy-logic optimization.

From `OPF_RESEARCH_1.37_2C_FIX1`, Actual execution defaults use `ACTUAL_EXEC_1.73_2C_FIX1`. Strategy rules are unchanged from `OPF_RESEARCH_1.37_2C`; execution logging now aggregates multiple TP/SL fills for the same two-contract order and writes one `execution_trades.csv` row per completed `TradeID` using the weighted average exit price.

From `OPF_RESEARCH_1.37_2C_FIX2`, Actual execution defaults use `ACTUAL_EXEC_1.73_2C_FIX2`. Strategy rules remain unchanged. Correct-role TP/SL replay fills with normal entry fills but abnormal exit drift are normalized to the expected TP/SL price for normal PnL/R and marked with `NormalizedReplayExitFill`; `RawPoints`, `RawDollars`, and `RawPointsR` retain the raw ATAS replay fill impact.

## edge_attribution.csv

File pattern:

- `*_edge_attribution.csv`

Purpose:

- Records a flat attribution row for every tracked research path and its final outcome so setup/path/regime/zone/risk/RR/time combinations can be grouped without joining many files first.

Important fields:

- `SignalID`
- `EventType`: `Tracked` or `Outcome`
- `Side`
- `SetupType`
- `ResearchPath`
- `RegimeScore`
- `RegimeBucket`
- `SetupQualityScore`
- `SetupQualityBucket`
- `ZoneType`
- `ZoneFreshness`
- `ZoneTouchCount`
- `RiskPoints`
- `RiskBucket`
- `EstimatedRR`
- `EstimatedRRBucket`
- `TimeBucket`
- `OutcomeClass`
- `ExitReason`
- `ActualVerified`
- `ActualExitRole`
- `ActualPnL_R`
- `ActualPnLDollars`

## funnel_events.csv

File pattern:

- `*_funnel_events.csv`

Purpose:

- Records lightweight stage events for funnel analysis from zone touch through confirmation, no-trade, execution decision, and research outcome.

Important fields:

- `Time`
- `Bar`
- `Stage`
- `SignalID`
- `ResearchPath`
- `Side`
- `SetupType`
- `Result`
- `Reason`
- `ExecutionScope`: blank for non-execution stages, `ActualExecuted`, `ActualCandidate`, or `ResearchOnly` for execution decisions.
- `TimeBucket`

## regime_daily.csv

File pattern:

- `*_regime_daily.csv`

Purpose:

- Records daily regime distribution and churn.

Important fields:

- `Date`
- `TotalBars`
- `BullTrendPct`
- `BearTrendPct`
- `UnknownPct`
- `RegimeChangeCount`
- `AvgBullScore`
- `AvgBearScore`

## replay_index.csv

File pattern:

- `replay_index.csv`

Generated by:

```powershell
powershell -ExecutionPolicy Bypass -File "C:\Users\Administrator\source\repos\NQOrderFlowV10629\NQOrderFlowV1\OPFStrategyV1\Scripts\Summarize-OPFResearch.ps1"
```

Purpose:

- Provides one compact row per research outcome for manual replay by `SignalID`.
- Joins outcome, signal zone metadata, daily regime context, and version/profile context.

Important fields:

- `SnapshotID`
- `SignalID`
- `EntryTime`
- `EntryBar`
- `Side`
- `ResearchPath`
- `OutcomeClass`
- `Hit1R`
- `Hit2R`
- `Hit2_5R`
- `Hit3R`
- `MFE_R`
- `MAE_R`
- `PointValue`
- `PlannedContracts`
- `ActualContracts`
- `RiskPerContractDollars`
- `TotalInitialRiskDollars`
- `MFE_Dollars`
- `MAE_Dollars`
- `ZoneID`
- `ZoneType`
- `ZoneFreshness`
- `UnknownPct`
- `RegimeChanges`
- `StrategyVersion`
- `ResearchSchemaVersion`
- `InstrumentProfileName`
- `ExecutionProfileName`
- `ProfileCatalogVersion`

From `OPF_RESEARCH_0.19`, replay index also includes:

- `WouldTradeLive`
- `ResearchOnlySignal`
- `SkippedByDailyGuard`
- `ExecutionSkipReasons`
- `DailyTargetDollars`
- `DailyLossLimitDollars`
- `MaxContracts`

From `OPF_RESEARCH_1.46`, `execution_decisions.csv` can include `PositiveExpansionV146` for controlled positive-expectancy volume expansion rows. The tag applies to Actual executions from `BreakawayRetest`, Short `TrendPullbackConfirmed`, and Long `StructureConfirmShadow_SwingStop`. Long `TrendPullbackConfirmed` rows are skipped with `TrendPullbackLongDisabledV146`, and Short `StructureConfirmShadow_SwingStop` rows are skipped with `StructureSwingShortDisabledV146`.

From `OPF_RESEARCH_1.47`, `execution_decisions.csv` can include the main-pool expansion tags `OCFillerExpansionV147`, `OCRiskExpansionV147`, `DailyVolumeQualityRescueV147`, and `BreakawayVolumeV147`. These identify the trades added by the v1.47 higher-volume test so their standalone NetR and NetDollars can be compared against the stable v1.45 baseline.

From `OPF_RESEARCH_1.48`, `execution_events.csv` may include:

- `ENTRY_STALE_NO_FILL`: an Actual entry order remained unfilled long enough to be treated as a no-entry execution and canceled.
- `EXIT_ABORTED_NO_ENTRY` with role `NO_ENTRY_FILL`: the execution was completed without PnL because no entry fill was confirmed.
- `LATE_ENTRY_AFTER_COMPLETED`: ATAS reported a delayed entry fill after the execution was already completed; the strategy sends an emergency flatten for the late-filled quantity.

From `OPF_RESEARCH_1.49`, `execution_events.csv` may include:

- `ORPHAN_POSITION_DETECTED`: ATAS `CurrentPosition` was non-zero while OPF had no active execution.
- `ORPHAN_FLATTEN_SEND`: OPF submitted a market order to flatten that residual account position.
- `SKIP_ORPHAN_POSITION`: an otherwise eligible Actual entry was skipped while the account still had a residual position.

From `OPF_RESEARCH_1.50`, duplicate exit handling is tightened:

- `DUPLICATE_EXIT_FILL` with `duplicateFlattenIgnored`: a duplicate `FLATTEN` fill arrived after the execution was already complete and was intentionally not answered with another flatten order.
- `DUPLICATE_EXIT_FLATTEN_SUPPRESSED`: a duplicate SL/TP fill did not require a residual-position flatten because the account was already flat or a duplicate-exit flatten had already been submitted.

From `OPF_RESEARCH_1.51`, abnormal entry-fill handling adds a session-day circuit breaker:

- `DAILY_ABNORMAL_FILL_GUARD_ON`: an `EntryFillOutOfRange` event activated the guard for the current session day after emergency flatten submission.
- `SKIP_DAILY_ABNORMAL_FILL_GUARD`: an otherwise eligible Actual entry was skipped because the same session day already had an abnormal entry fill.

From `OPF_RESEARCH_1.52`, `execution_decisions.csv` can include `BreakawayLongSelectiveV152` for high-quality Breakaway Long Actual executions. The Breakaway volume threshold is lowered to `SetupQualityScore >= 72`, so `BreakawayVolumeV147` rows now identify the newly widened `72 <= score < 80` Breakaway subgroup.

From `OPF_RESEARCH_1.53`, split Actual entry fills are accumulated before SL/TP bracket submission. `execution_events.csv` may include:

- `ENTRY_PARTIAL_FILL_WAITING`: cumulative entry quantity is still below the requested quantity, so bracket submission is waiting for the remaining fill.
- `ENTRY_PARTIAL_FILL_ABORT`: the remaining entry quantity did not fill within the aggregation window; the strategy cancels the remainder and emergency-flattens the filled quantity.

From `OPF_RESEARCH_1.54`, expected inactive OCO cleanup states use non-failure audit events:

- `OCO_SIBLING_INACTIVE`: the opposite protective order moved to ATAS `Failed` after SL or TP already completed the execution.
- `CANCEL_ALREADY_INACTIVE`: cleanup attempted to cancel an OCO sibling that ATAS had already removed.

From `OPF_RESEARCH_1.55`, end-of-replay open executions are closed before strategy shutdown through real order callbacks:

- `REPLAY_STOP_EXIT_PENDING`: the stop-guard window found an open Actual execution and started orderly shutdown.
- `REPLAY_STOP_FLATTEN_SEND`: protective orders were canceled and a market order with role `SESSION_FLATTEN` was submitted for the current account position.
- `REPLAY_STOP_POSITION_FLAT`: the account became flat during protective-order cancellation, so no additional session-close market order was required.
- `SESSION_FLATTEN` in `execution_trades.csv` is a normal real-fill exit role with actual PnL/R, not a synthetic `STOPPED` row.
- Breakaway Long Actual execution is disabled and the Breakaway Short setup-quality threshold is restored to `76`.

From `OPF_RESEARCH_1.56`, `ReplayStopExitPending` suppresses protection-loss emergency flatten detection while the strategy intentionally cancels SL/TP orders for a session-close exit. The `SESSION_FLATTEN` submission follows the completed cancellation requests immediately, preventing duplicate exit fills without changing the v1.55 strategy rules.

From `OPF_RESEARCH_1.57`, `ACTUAL_EXEC_1.93` adds a daily-capped Long `ObservationConfirm_WideStop1_5R` expansion for `SetupQualityScore >= 70` and `22 < InitialRiskPoints <= 25`. Executed expansion rows include `OCWideStopLongExpansionV157` in `execution_decisions.csv`, with at most one expansion execution per session day.

From `OPF_RESEARCH_1.58`, `ACTUAL_EXEC_1.94` evaluates that expansion pool against its real fixed `ActualTargetR=1.5` target. Its `EstimatedRewardPoints` equals `InitialRiskPoints * ActualTargetR`, and `RewardModel` is `OCWideStopLongExpansionV158TargetR:1.5`; all v1.57 eligibility and safety gates remain unchanged.

From `OPF_RESEARCH_1.59`, `ACTUAL_EXEC_1.95` serializes `research.log` writes to prevent callback-time file-share exceptions from being classified as order-cancel failures. Daily-capped wide-stop Long candidates continue to report fixed-target reward data after the first expansion trade, using `OCWideStopLongExpansionV159TargetR:1.5`; the one-trade cap and all execution rules are unchanged.

From `OPF_RESEARCH_1.60`, `ACTUAL_EXEC_1.96` serializes every ResearchLogger text and CSV write through one instance-level lock. This prevents concurrent callbacks from dropping execution-event rows after protection cleanup; no columns, strategy rules, targets, stops, or research policies change.

From `OPF_RESEARCH_1.61`, `ACTUAL_EXEC_1.97` applies `TargetR=2.0` only to base `ObservationConfirm` Long executions whose planned risk is greater than `8` and at most `11` points. Executed rows include `ObservationConfirmLongTarget2RV161` in `execution_decisions.csv`; `execution_trades.csv` records `TargetR=2`, while all entry and safety gates remain unchanged.

From `OPF_RESEARCH_1.62`, `ACTUAL_EXEC_1.98` raises the Long wide-stop expansion daily cap from one to two without changing its score/risk gates. The second execution is tagged `OCWideStopLongSecondExpansionV162`. Compact exit-policy rows also cover qualified `ObservationStrict_Other` Long and `TrendPullbackConfirmed` Short research candidates and add five full-position dynamic-protection policies. `PolicyExitBar` records the simulated policy's actual exit bar so offline portfolio analysis can model active-trade replacement instead of simply adding independent outcomes.

From `OPF_RESEARCH_1.63`, `ACTUAL_EXEC_1.99` restores the Long wide-stop expansion cap to one and promotes qualified `ObservationStrict_Other` Long / `TrendPullbackConfirmed` Short candidates to Actual execution with fixed `3R` targets. Executed rows include `SelectiveExpansionV163` and a path-specific target tag. Eligibility continues to use nearest-structure RR `>= 0.5`, independently of the real `3R` bracket. Compact exit-policy logging also includes `BreakawayRetest`.

From `OPF_RESEARCH_1.64`, `ACTUAL_EXEC_2.00` expands Actual research to eight independently tagged path/side sources. All use planned risk `<= 25` and real fixed `3R` targets, while eligibility continues to use nearest-structure reward evidence. Per-source score/RR thresholds are documented in `README.md`. Executed decisions contain `AggressiveExpansionV164` plus a source-specific tag; no new CSV columns are added. Compact exit-policy logging follows the same eight source rules.

From `OPF_RESEARCH_1.65`, `ACTUAL_EXEC_2.01` adds a single-bar delayed retry for qualified aggressive-expansion candidates blocked only by entry-bar stop/target touches. Scheduling, retry, rejection, and executed Wait1 rows use `AggressiveExpansionWait1ScheduledV165`, `EXPANSION_WAIT1_RETRY_V165`, `EXPANSION_WAIT1_REJECTED`, and `AggressiveExpansionWait1V165`. Stops, risk, RR, and all execution guards are recalculated on the retry bar. Wide-stop V131 fill validation also preserves its admitted 22-point cap instead of falling through to the 18-point low-risk cap. No CSV columns are added.

From `OPF_RESEARCH_1.66`, `ACTUAL_EXEC_2.02` changes only the Wait1 stop model. The delayed bar low/high plus a `0.5` point buffer becomes the base stop; WideStop variants apply `1.5x` to that new confirmation-bar risk. Executed retries include `AggressiveExpansionWait1ConfirmBarStopV166`, and retry diagnostics use `EXPANSION_WAIT1_RETRY_V166`. All v1.65 scheduling, follow-through, risk/RR, same-bar, ActiveTrade, and lifecycle guards remain active.

From `OPF_RESEARCH_1.67`, `ACTUAL_EXEC_2.03` promotes four fixed-3R positive-evidence directions: `FailureReverse_ObservationInvalidated` Short, `UnknownRegimeZoneTouch` Long, `ShadowCandidate` Short, and `AlmostConfirmed` Long. Each uses score `>= 35`, risk `<= 25`, and eligibility RR `>= 0.25`. Execute reasons add `BoldExpansionV167` and a source-specific tag. Wait1 remains logged and tracked through `EXPANSION_WAIT1_RESEARCH_ONLY_V167` but no longer submits Actual orders. No CSV columns are added.

From `OPF_RESEARCH_1.68`, `ACTUAL_EXEC_2.04` adds `ZoneBirthResearch` Short and `FailureReverse_ObservationInvalidated` Long to the aggressive pool with score `>= 35`, risk `<= 25`, and eligibility RR `>= 0.25`. ZoneBirth Short, both base FailureInvalidated directions, and AlmostConfirmed Long use real `2.5R` targets and include `DynamicExpansionV168` in Execute reasons. Unknown Long and Shadow Short retain `3R`. No CSV columns are added.

From `OPF_RESEARCH_1.69`, `ACTUAL_EXEC_2.05` disables only `UnknownRegimeZoneTouch` Long aggressive execution after its clean combined Smoke evidence fell below the per-source `-4R` floor. Unknown Short and every other v1.67/v1.68 source, target, threshold, log column, and safety rule remain unchanged.

From `OPF_RESEARCH_1.70`, `ACTUAL_EXEC_2.06` restricts ZoneBirth Short to planned risk `<= 8` or `12 < risk <= 18`. Rejected candidates use `ZoneBirthShortV170RiskBandExcluded`; executed candidates use `ZoneBirthShortDualRiskBandV170` with a band-specific tag. Entry-fill validation stores the corresponding `8` or `18` point cap. No CSV columns are added.

`OPF_RESEARCH_1.71`, `ACTUAL_EXEC_2.07` is retained only as rejected historical evidence after its 112-day full-position dynamic-protection replay underperformed v1.70.

From `OPF_RESEARCH_1.72`, `ACTUAL_EXEC_2.08` gives fully filled two-contract ZoneBirth Short executions separate Base and Runner OCO groups. Execution-event roles use `BASE_SL`, `BASE_TP`, `RUNNER_SL`, and `RUNNER_TP`; the final trade role combines both leg results, for example `SPLIT_BASE_SL_RUNNER_BE`. The Runner SL can move to break-even after a closed-bar `1R` trigger. Existing CSV columns are reused; `StopPrice` remains the original structural stop, while Runner modification details are carried by execution events.

From `OPF_RESEARCH_1.73`, `ACTUAL_EXEC_2.09` keeps the v1.72 independent OCO lifecycle but changes the profit logic: Base remains at `2.5R`, Runner targets `4R`, and the Runner SL becomes eligible for break-even only after the Base TP actually fills. The closed-bar `1R` trigger is removed. `TargetPrice`, `TargetR`, and `TargetDollars` continue to describe the Base/legacy target; the Runner `4R` target is recorded by `RUNNER_TP_SENT`, `ZONEBIRTH_SPLIT_V173_READY`, and the final combined split result.

From `OPF_RESEARCH_1.74`, `ACTUAL_EXEC_2.10` quarantines `EntryFillOutOfRange` orders individually instead of activating a whole-session abnormal-fill guard. The emergency flatten remains mandatory, but the quarantined order is omitted from `execution_trades.csv`, Actual verification, daily PnL/R, HUD TP/SL/Other counts, and normal daily capacity. Each quarantine raises an ATAS notification and updates the persistent intraday HUD anomaly line; repeated anomalies do not automatically disable later strategy entries. `Summarize-OPFResearch.ps1` reports `QuarantinedEntryFills` separately and treats a completed quarantine as an accounted executed decision.

From `OPF_RESEARCH_1.75`, `ACTUAL_EXEC_2.13` adds live-readiness reconciliation, connection and order-failure notifications, stop-only protection integrity checks, idempotent emergency flatten submission, confirmed stop-time exits, a fifteen-normal-trade daily cap, a `-$300` realized net daily loss gate, U.S. cash-open blackout windows, and real-time latency blocking. Quarantined abnormal entries remain excluded from the normal-trade cap. `live_account_pnl.csv` is an account-impact ledger and does not replace `execution_trades.csv`.

Historical Replay writes `HISTORICAL_REPLAY_CONNECTOR_BYPASS` when ATAS exposes no connector even though its simulated Actual order path is available. This bypass applies only when candle time is historical; real-time execution remains blocked without a connected connector.

`live_account_pnl.csv` fields:

- `TradeID`, `EntryTime`, `ExitTime`, `Side`, `Classification`
- `Quantity`, `EntryPrice`, `ExitPrice`
- `GrossPnLDollars`
- `CommissionDollars`: `Quantity * ActualCommissionPerContractRoundTrip`
- `NetPnLDollars`: gross PnL minus commission
- `DailyNetPnLDollars`: running realized account net PnL used by the live daily-loss gate
- `RawGrossPnLDollars`: unmodified PnL from callback entry/exit prices, retained for diagnosing stale Replay fills
- `PnLSource`: `LiveActualFill`, `HistoricalReplayNormalized`, `HistoricalReplayProtectiveFlattenActualFill`, or `HistoricalReplayQuarantineCommissionOnly`

`Classification=Normal` is also present in normal strategy Alpha files. `Classification=Quarantine` records actual account impact for abnormal entry isolation while remaining excluded from normal trade count, R, strategy PnL, HUD TP/SL/Other, and Actual verification.

For Historical Replay, `GrossPnLDollars` uses validated normalized PnL for normal trades. Quarantine gross PnL is zero and only commission affects the daily guard; `RawGrossPnLDollars` still preserves the simulator's stale-price impact. Real-time execution uses raw actual fills for both normal and quarantine account PnL.

Compact `exit_policy_evaluations.csv` also includes four research-only split-runner policies:

- `SplitBase_Runner2_5R_BE0_75R`
- `SplitBase_Runner2_5R_BE1R`
- `SplitBase_Runner3R_BE0_75R`
- `SplitBase_Runner3R_BE1R`

`Base` represents one contract exiting at the existing Actual path target. `Runner` represents the second contract targeting `2.5R` or `3R`, with a break-even stop eligible only from the bar after the configured `0.75R` or `1R` trigger. `PnLPoints`, `PnL_R`, and `PnLDollars` represent the combined two-contract average/total result. Actual order quantities, targets, stops, and protection orders are not changed by these shadow rows. Trackers with an early Actual exit continue until the normal 12-bar research-window boundary so the runner policies can observe post-exit price action; Actual verification fields still report the real fill lifecycle.
