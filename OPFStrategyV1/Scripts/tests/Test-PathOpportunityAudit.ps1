param([string]$OutputDirectory = (Join-Path $PSScriptRoot '../../Evidence/Validation/20261019-v2/opportunity-synthetic'))
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot '../Export-PathOpportunityAudit.ps1') -LogDirectory (Join-Path $PSScriptRoot 'ValidationAuditFixtures') -OutputDirectory $OutputDirectory
$result = Get-Content -LiteralPath (Join-Path $OutputDirectory 'path-opportunity-audit.json') -Raw | ConvertFrom-Json
if ($result.Resolved -ne 2 -or $result.StoppedIncomplete -ne 1 -or @($result.Overlaps).Count -ne 1) { throw 'Opportunity counts or overlap mismatch' }
$oc = @($result.Summary | Where-Object { $_.Group -like '*ObservationConfirm*' -and $_.Policy -eq 'FixedPoints' })
if ($oc.Count -ne 1 -or [decimal]$oc[0].NetPoints -ne 14.4) { throw 'Fee adjusted policy points mismatch' }
Write-Output 'PASS synthetic opportunity completion, incomplete exclusion, overlap and fee-adjusted points; not profit evidence'
