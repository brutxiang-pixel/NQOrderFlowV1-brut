# v2.03 Actual Calibration Report

Generated: 2026-07-24 15:19:26

Decision: `REGRESSION_ONLY_NO_PRIMARY_DATES`

| Set | Dates | Baseline trades | Candidate trades | Matched W | W direct | Portfolio delta | Retention | Projected 184 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| All | 4 | 45 | 49 | 18 | $518.00 | $4.40 | 0.85% | $202.40 |
| Q4 | 1 | 14 | 14 | 3 | $222.50 | $186.00 | 83.60% | $34,224.00 |
| H1 | 3 | 31 | 35 | 15 | $295.50 | $-181.60 | -61.46% | $-11,138.13 |

PrimaryDates controls the retention decision when supplied. All controls the 12-day economic-scale check. Q4 and H1 are explanatory only.

Details: `summary.csv`, `w_divergence_ledger.csv`, and `w_holding_bars.csv`. Optional simulator inputs add `sim_vs_actual.csv`.
