# NQOrderFlowV1 Panel Config Versions

## Optimized_A_v1.10

Current baseline from `NQOrderFlowV1_config.json`.

- `EntryMode`: `MarketClose`
- `EnableRetraceEntry`: `false`
- `OrderFlowMinScoreThreshold`: `4`
- `MaxEntryDistanceFromZoneTicks`: `80`
- Purpose: baseline comparison for the latest backtest.

## Optimized_B_v1.25_retrace_marketclose

Next backtest candidate for v1.25.

- `EnableRetraceEntry`: `true`
- `MaxRetraceWaitBars`: `2`
- `RetraceEntryMaxDistanceTicks`: `8`
- `MaxEntryDistanceFromZoneTicks`: `48`
- `MaxPlaceDistanceFromZoneTicks`: `160`
- Keep `OrderFlowMinScoreThreshold`: `4`

Success check:

- Logs must contain `MKT_DELAY_SCHEDULED`, `MKT_DELAY_WAITING`, or `MKT_DELAY_FIRED`.
- Full-loss trades should drop meaningfully versus baseline `17 / 26`.
- Total trades may decrease; this is acceptable if NetR improves.

## Optimized_C_v1.26_of_strict

Code-backed strict OF test version.

- `EnableOrderFlowMismatchSoftening`: `false`
- `OrderFlowMismatchSofteningBars`: `3`
- Keep B version retrace settings as the parameter base.

Use this to test whether blocking opposite-direction OF improves the B-version full-loss profile.

## Optimized_D_v1.26_soft5_retrace3

Next comparison run after C-version over-filtering.

- `EnableOrderFlowMismatchSoftening`: `true`
- `OrderFlowMismatchSofteningBars`: `5`
- `MaxRetraceWaitBars`: `3`
- Keep C/B retrace distance and anti-chase settings unchanged.

Use this to recover enough trade samples while still avoiding immediate opposite-direction OF entries.

## v1.26 Code Notes

- Live Market entry brackets now use the actual fill average from `MyTrade.Price`, not the request-time close proxy.
- Trade CSV writes retry briefly and keep failed lines pending in memory for the next successful write.
- OF direction mismatch softening is controlled by panel parameters.

## v1.27 Code Notes

- Skip live TP modify requests when the TP order is no longer active.
- Add `RISK_WIDE_ENTRY` logs for trades with initial risk >= 30 points.
- Carry OF softening state into plan logs and trade CSV.
- Add `ExitClass`, `OFSoftened`, and `OFMismatchBars` to trade CSV.
- Normalize common OF text markers to ASCII in trade CSV.

## Optimized_v1.28_risk30_short_soft6

Small risk-gate version based on the D/v1.27 settings.

- `EnableMaxRiskPointsFilter`: `true`
- `MaxEntryRiskPoints`: `30`
- `EnableStrictShortSoftenedFilter`: `true`
- `MaxShortSoftenedMismatchBars`: `6`

This blocks entries with `RiskPts >= 30`, and blocks softened SHORT entries when `OFMismatchBars > 6`.

## Optimized_v1.29_risk30_short_soft_ge6

Small tightening version based on v1.28.

- Keep `RiskPts >= 30` blocked.
- Keep `MaxShortSoftenedMismatchBars`: `6`.
- Tighten softened SHORT blocking from `OFMismatchBars > 6` to `OFMismatchBars >= 6`.

## Optimized_v1.30_mnq_one_contract_daily_guard

MNQ one-contract target version based on v1.29.

- `MaxEntryRiskPoints`: `20`.
- `EnableDailyGuard`: `true`.
- `DailyProfitTargetDollars`: `150`.
- `DailyLossLimitDollars`: `100`.
- `MaxTradesPerDay`: `3`.
- `MaxLossesPerDay`: `2`.
- Keep strict softened SHORT blocking at `OFMismatchBars >= 6`.
- Add strict softened LONG blocking for `OFMismatchBars == 6` or `OFMismatchBars >= 10`.
- Re-check actual live fill risk before sending brackets; flatten immediately when actual `RiskPts >= MaxEntryRiskPoints`.
