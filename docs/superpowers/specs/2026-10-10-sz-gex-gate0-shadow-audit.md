# SignificantZone × GEX Gate0 Shadow Audit

- **Date**: 2026-10-10 (Asia/Shanghai)
- **Scope**: `SignificantZoneFirstTouchLong` / `SignificantZoneFirstTouchShort` only
- **Mode**: audit / logging only — **zero order impact**
- **Contract inheritance**: `OPFStrategyV1/Reports/gex_stage_b_shadow_audit_contract.md`

## Goal

When a SignificantZone FirstTouch candidate is **queued** or **activated**, append one GEX candidate audit row with space/regime soft tags. Tags never feed `IsExecutionEligiblePath`, ManualAlert fire conditions, stops/targets, path priority, size, or order submission.

## When written

| Phase | Call site | `AuditPhase` |
|---|---|---|
| Queued | `TryQueueSignificantZoneFirstTouch` after pending add | `Queued` |
| Activated | `TryActivateSignificantZoneFirstTouches` before risk/submit | `Activated` |

Also (unchanged Stage B):

- `AppendGexSnapshot` on each distinct snapshot SHA load → `{SnapshotId}_gex_snapshot.json` + `{SnapshotId}_gex_levels.csv`
- Candidate rows → `{SnapshotId}_gex_candidate_audit.csv` under `%APPDATA%/ATAS/StrategyLogs/OPFStrategyV1/`

## CSV fields (candidate audit)

Context columns (SnapshotID / versions / profiles) plus:

| Field | Source | Empty means |
|---|---|---|
| Time, Bar, SignalID, ResearchPath, Side, AuditPhase, Price | candidate | — |
| GexStatus, GexDetail, DataDate, UpdatedAt, LevelCount, Sha256 | snapshot | Unavailable/missing |
| GexRegime | price vs ZeroGamma if ZG present | no ZG |
| PrefilterActive | `Ready` ∧ CW∧PW∧ZG present | `false` |
| ShadowTags | pipe-joined codes below | none |
| SizeHint | `0.5` only with neg-Γ Long soft tag | unavailable / N/A |
| RoleFlipState | wall-state feed | always `Unknown` until feed exists |
| CwPrice, PwPrice, ZgPrice, VtPrice | nearest tagged levels | missing type |
| DistCw/Pw/Zg/Vt | `price - level` | missing type |
| EmUsedPct, EmRemainingUpper, EmRemainingLower | enrich feed | **not in current snapshot** |
| NearWallPoints | const `40` (Gate0) | — |

## Shadow tag codes

Emitted **only** when `PrefilterActive=true` (except `SHADOW_PREFILTER_INACTIVE`):

| Code | Condition |
|---|---|
| `SHADOW_PREFILTER_INACTIVE` | Status≠Ready or CW/PW/ZG missing — **no other soft tags** |
| `SHADOW_NO_SPACE` | Long: near/above Call Wall (≤40pt) or near EM upper if present; Short: near/below Put Wall or near EM lower; Short also if `EmUsedPct>70` when present |
| `SHADOW_NEG_GAMMA_LONG_HALF` | NegGamma + SZ Long → also `SizeHint=0.5` |
| `SHADOW_POS_GAMMA_SHORT_STRICT` | PosGamma + SZ Short |
| `SHADOW_ROLE_FLIP_CONFLICT` | only if `RoleFlipState=Conflict` from feed — **never invented** |

## Bit-identical order guarantee

1. Soft tags are written to CSV only; no branch reads them for Skip/Execute.
2. `OpeningPullbackFailureStrategy.Gex.cs` must not call `TrySubmitReplayExecution` / `AppendExecutionDecision` / order APIs (wiring test).
3. `IsExecutionEligiblePath`, ManualAlert, stops (`Outer±0.5`), path priority unchanged by this spec.
4. Replay with identical snapshot + GEX file present vs absent (or Off) must keep the same execution_decisions / trades for SZ paths; only audit CSV differs.

## Known gaps (do not invent)

- Expected Move remaining edges / EmUsedPct
- Wall four-state + role-flip history
- ProxyChain / coverage / wall-weight / expiry window enrich (full F0)
- Gamma Kings / multi-window ladder
- Calendar windows (data day / expiry last hour)

When those arrive via snapshot enrich, fill the empty columns; keep Gate0 tag-only until Gate1 HardSkip is approved.
