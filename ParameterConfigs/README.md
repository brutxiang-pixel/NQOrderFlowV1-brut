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

## v1.26 Code Notes

- Live Market entry brackets now use the actual fill average from `MyTrade.Price`, not the request-time close proxy.
- Trade CSV writes retry briefly and keep failed lines pending in memory for the next successful write.
- OF direction mismatch softening is controlled by panel parameters.
