# UnknownRegimeZoneTouch Long Manual Alert Observation Design

## Goal

Restore the approved non-ordering observation mode for the single v2.77-qualified offline candidate, `UnknownRegimeZoneTouch Long`.

## Scope

- Add `ManualAlertEnabled` to the existing execution configuration. Default: `false`.
- With the switch on, only `UnknownRegimeZoneTouch Long` can produce an alert.
- The alert contains the planned entry, stop, 1.5R target, configured quantity, initial dollar risk, setup score, regime score and zone context.
- The alert writes a `MANUAL_ALERT` execution event, a HUD-highlighted status and an ATAS notification.
- It must make no order API calls, create no replay execution state, and never manage, flatten, alter or account for a manual position.

## Admission boundary

The alert candidate must pass shared safety checks: a valid positive risk, the instrument hard-risk ceiling, Globex closeout lock, US-open blackout, latency lock, same-bar ambiguity and an empty account/strategy position.

The alert deliberately bypasses the automatic-execution whitelist and its path-specific historic policy rejects. Those rejects contain `UnknownMicroRiskActualDisabledV127`, which is an automatic-strategy policy—not evidence that the candidate is unsafe for manual observation. Daily/weekly limits remain HUD reference only because manual entries are not strategy-managed and therefore cannot be honestly counted by the strategy.

## Non-goals

- No new signal path, signal gate or quality threshold.
- No automatic entry, bracket order, cancellation, flattening or stop modification.
- No tracking of manual PnL, trade count or manual protection order state.
- No time-of-day mode switch and no general-purpose alert routing.

## Verification

1. `ManualAlertEnabled=false` preserves existing execution behavior and emits no `MANUAL_ALERT` event.
2. `ManualAlertEnabled=true` causes a matching Long candidate that passes the shared safety boundary to emit exactly one alert.
3. A matching alert has no `ENTRY_SEND` and no replay execution record.
4. Short, other path, non-flat account and shared-safety failures emit no alert.
