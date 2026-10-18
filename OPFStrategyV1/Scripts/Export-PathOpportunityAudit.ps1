param(
    [string]$LogDirectory = "$env:APPDATA/ATAS/StrategyLogs/OPFStrategyV1",
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../Evidence/PathOpportunityAudit')
)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$completed = @()
$incomplete = 0
foreach ($file in Get-ChildItem -LiteralPath $LogDirectory -Filter '*_validation_opportunity.jsonl' -File) {
    foreach ($line in [IO.File]::ReadLines($file.FullName)) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $packet = $line | ConvertFrom-Json
        if ($packet.Stage -eq 'Complete') {
            $completed += [pscustomobject]@{ File=$file.Name; Product=$packet.Product; Account=$packet.Account; Candidate=$packet.Opportunity }
        } elseif ($packet.Stage -eq 'StoppedIncomplete') { $incomplete++ }
    }
}
$summary = @(foreach ($group in $completed | Group-Object { $_.Product + '|' + $_.Candidate.Path + '|' + $_.Candidate.IsLong }) {
    foreach ($policy in @('FixedPoints','EarlyPoints','ProtectedPoints','LockedPoints')) {
        $values = @($group.Group | ForEach-Object { [decimal]$_.Candidate.$policy - [decimal]$_.Candidate.FeePoints })
        [pscustomobject]@{ Group=$group.Name; Policy=$policy; Candidates=$values.Count;
            Positive=@($values | Where-Object { $_ -gt 0 }).Count;
            NetPoints=($values | Measure-Object -Sum).Sum;
            Note='Forward observed hypothetical points after fee model; not broker PnL or portfolio replacement simulation' }
    }
})
$overlaps = @()
for ($i = 0; $i -lt $completed.Count; $i++) {
    $left = $completed[$i]
    for ($j = $i + 1; $j -lt $completed.Count; $j++) {
        $right = $completed[$j]; $a = $left.Candidate; $b = $right.Candidate
        if ($left.Product -ne $right.Product -or $left.Account -ne $right.Account -or $left.File -ne $right.File -or
            $a.Path -eq $b.Path -or $a.IsLong -ne $b.IsLong) { continue }
        if ([Math]::Abs((([datetime]$a.Time) - ([datetime]$b.Time)).TotalSeconds) -le 30 -and
            [Math]::Abs([decimal]$a.Stop - [decimal]$b.Stop) -le .5 -and
            [Math]::Abs([decimal]$a.Entry - [decimal]$b.Entry) -le [Math]::Max(.5, [Math]::Min([decimal]$a.Risk, [decimal]$b.Risk) * .25)) {
            $overlaps += [pscustomobject]@{ Product=$left.Product; Account=$left.Account; Left=$a; Right=$b; Policy='AuditOnly; no hindsight winner selected' }
        }
    }
}
[pscustomobject]@{ CreatedAt=(Get-Date).ToString('o'); Resolved=$completed.Count; StoppedIncomplete=$incomplete;
    Summary=$summary; Overlaps=$overlaps; ActualOrdersChanged=$false;
    Limitation='Requires orderly strategy stop to flush; no exact counterfactual fills, slippage or replacement portfolio claim' } |
    ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'path-opportunity-audit.json') -Encoding UTF8
Write-Output "Resolved=$($completed.Count) Incomplete=$incomplete Overlaps=$($overlaps.Count); $OutputDirectory"
