param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$syntheticOutput = Join-Path $OutputDirectory 'synthetic-audit-test'
New-Item -ItemType Directory -Path $syntheticOutput -Force | Out-Null
& (Join-Path (Split-Path $PSScriptRoot -Parent) 'Export-EightPathValidationAudit.ps1') -LogDirectory (Join-Path $PSScriptRoot 'ValidationAuditFixtures') -OutputDirectory $syntheticOutput
$summary = @(Import-Csv -LiteralPath (Join-Path $syntheticOutput 'eight-path-summary.csv'))
$excluded = @(Import-Csv -LiteralPath (Join-Path $syntheticOutput 'excluded-trades.csv'))
if ($summary.Count -ne 1 -or $summary[0].DecisionCandidates -ne 1 -or $summary[0].ExecuteDecisions -ne 1 -or
    $summary[0].ClosedValid -ne 1 -or $summary[0].NetDollars -ne 9 -or $summary[0].AverageGivebackR -ne 0.5 -or
    $excluded.Count -ne 2) { throw 'Audit regression: unique candidates, net ledger, test/iceberg exclusion or invalid trade isolation failed.' }
Write-Output 'PASS synthetic audit: candidate dedup, test/iceberg excluded, quantity mismatch and missing ledger isolated, net fee and giveback correct'
