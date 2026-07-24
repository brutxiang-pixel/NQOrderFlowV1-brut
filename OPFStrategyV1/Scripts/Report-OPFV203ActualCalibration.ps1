param(
    [Parameter(Mandatory = $true)]
    [string[]]$BaselineEvidenceDirectories,

    [Parameter(Mandatory = $true)]
    [string[]]$CandidateEvidenceDirectories,

    [Parameter(Mandatory = $true)]
    [string[]]$Dates,

    [string[]]$PrimaryDates = @(),

    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,

    [string]$ObservedSimulatorCsv,

    [string]$ConservativeSimulatorCsv
)

$ErrorActionPreference = 'Stop'
$widePath = 'ObservationConfirm_WideStop1_5R'
$commission = [decimal]2.4

function Decimal-Value($Value) {
    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) {
        return [decimal]0
    }

    return [decimal]::Parse([string]$Value, [Globalization.CultureInfo]::InvariantCulture)
}

function Date-Key($Value) {
    return ([datetime]$Value).ToString('yyyy-MM-dd')
}

function Trade-Key($Row) {
    return '{0}|{1}' -f $Row.SignalID, $Row.ResearchPath
}

function Sum-Value($Rows, [string]$Property) {
    [decimal]$sum = 0
    foreach ($row in @($Rows)) {
        $sum += Decimal-Value $row.$Property
    }
    return [math]::Round($sum, 2)
}

function Read-Evidence([string[]]$Directories, [string]$Label, [string[]]$SelectedDates) {
    $files = [Collections.Generic.List[object]]::new()
    foreach ($directory in $Directories) {
        if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
            throw "$Label evidence directory not found: $directory"
        }

        foreach ($kind in @('execution_trades', 'live_account_pnl', 'execution_decisions', 'execution_events', 'exit_policy_evaluations', 'regime_daily')) {
            Get-ChildItem -LiteralPath $directory -Recurse -File -Filter "*_${kind}.csv" | ForEach-Object {
                $files.Add([pscustomobject]@{ Kind = $kind; Path = $_.FullName })
            }
        }
    }

    $allEvents = foreach ($file in @($files | Where-Object Kind -eq 'execution_events')) {
        Import-Csv -LiteralPath $file.Path
    }
    $allDaily = foreach ($file in @($files | Where-Object Kind -eq 'regime_daily')) {
        Import-Csv -LiteralPath $file.Path
    }
    $snapshotDates = @($allDaily |
        Where-Object Date -in $SelectedDates |
        ForEach-Object { [pscustomobject]@{ SnapshotID = $_.SnapshotID; Date = $_.Date } })
    $eventSnapshotDates = @($allEvents |
        Where-Object { $_.Event -eq 'GLOBEX_TRADING_DAY_ROLLOVER' -and $_.Message -match 'current=(\d{4}-\d{2}-\d{2})' } |
        ForEach-Object { [pscustomobject]@{ SnapshotID = $_.SnapshotID; Date = $Matches[1] } } |
        Where-Object Date -in $SelectedDates)
    $knownSnapshots = @($snapshotDates.SnapshotID)
    $snapshotDates += @($eventSnapshotDates | Where-Object SnapshotID -notin $knownSnapshots)
    $snapshotDates = @($snapshotDates | Sort-Object SnapshotID, Date -Unique)
    $duplicates = @($snapshotDates | Group-Object Date | Where-Object Count -gt 1)
    if ($duplicates.Count -gt 0) {
        $description = ($duplicates | ForEach-Object { '{0}({1})' -f $_.Name, $_.Count }) -join ', '
        throw "$Label contains multiple snapshots for the same requested date: $description"
    }

    $selectedSnapshotIds = @($snapshotDates.SnapshotID)
    $dateBySnapshot = @{}
    foreach ($item in $snapshotDates) { $dateBySnapshot[$item.SnapshotID] = $item.Date }
    $result = @{}
    foreach ($kind in @('execution_trades', 'live_account_pnl', 'execution_decisions', 'execution_events', 'exit_policy_evaluations', 'regime_daily')) {
        if ($kind -eq 'execution_events') {
            $result[$kind] = @($allEvents | Where-Object SnapshotID -in $selectedSnapshotIds)
            continue
        }
        if ($kind -eq 'regime_daily') {
            $result[$kind] = @($allDaily | Where-Object SnapshotID -in $selectedSnapshotIds)
            continue
        }

        $rows = foreach ($file in @($files | Where-Object Kind -eq $kind)) {
            $snapshotId = [IO.Path]::GetFileName($file.Path) -replace "_${kind}\.csv$", ''
            if ($snapshotId -in $selectedSnapshotIds) {
                Import-Csv -LiteralPath $file.Path
            }
        }
        $result[$kind] = @($rows | ForEach-Object {
            $_ | Add-Member -NotePropertyName TradingDate -NotePropertyValue $dateBySnapshot[$_.SnapshotID] -PassThru
        })
    }

    return $result
}

function Build-TradeRows($Evidence) {
    $pnlByTrade = @{}
    foreach ($row in $Evidence.live_account_pnl) {
        $pnlByTrade[$row.TradeID] = Decimal-Value $row.NetPnLDollars
    }

    return @($Evidence.execution_trades |
        Where-Object { $_.IsAbnormalExecution -ne 'True' -and -not [string]::IsNullOrWhiteSpace($_.TradeID) } |
        ForEach-Object {
            $net = if ($pnlByTrade.ContainsKey($_.TradeID)) {
                $pnlByTrade[$_.TradeID]
            }
            else {
                (Decimal-Value $_.Dollars) - $commission
            }
            [pscustomobject]@{
                Date = $_.TradingDate
                SnapshotID = $_.SnapshotID
                Key = Trade-Key $_
                SignalID = $_.SignalID
                TradeID = $_.TradeID
                Side = $_.Side
                ResearchPath = $_.ResearchPath
                EntryTime = $_.EntryTime
                EntryBar = [int]$_.EntryBar
                ExitTime = $_.ExitTime
                ExitBar = [int]$_.ExitBar
                EntryPrice = Decimal-Value $_.EntryPrice
                InitialRiskPoints = Decimal-Value $_.InitialRiskPoints
                FilledRiskPoints = Decimal-Value $_.FilledRiskPoints
                ExitRole = $_.ExitRole
                Gross = Decimal-Value $_.Dollars
                AccountNet = $net
                BarsHeld = [int]$_.ExitBar - [int]$_.EntryBar
            }
        })
}

function Build-Metrics([string]$SetName, [string[]]$SetDates, $Baseline, $Candidate) {
    $baselineSet = @($Baseline.Trades | Where-Object Date -in $SetDates)
    $candidateSet = @($Candidate.Trades | Where-Object Date -in $SetDates)
    $baselineByKey = @{}
    $candidateByKey = @{}
    foreach ($row in $baselineSet) { $baselineByKey[$row.Key] = $row }
    foreach ($row in $candidateSet) { $candidateByKey[$row.Key] = $row }

    $matchedKeys = @($baselineByKey.Keys | Where-Object { $candidateByKey.ContainsKey($_) })
    $matchedWKeys = @($matchedKeys | Where-Object {
        $candidateByKey[$_].Side -eq 'Long' -and $candidateByKey[$_].ResearchPath -eq $widePath
    })
    $matchedNonWKeys = @($matchedKeys | Where-Object { $_ -notin $matchedWKeys })
    $addedKeys = @($candidateByKey.Keys | Where-Object { -not $baselineByKey.ContainsKey($_) })
    $removedKeys = @($baselineByKey.Keys | Where-Object { -not $candidateByKey.ContainsKey($_) })

    [decimal]$wDirect = 0
    [decimal]$matchedNonW = 0
    [decimal]$wDirectGross = 0
    [decimal]$matchedNonWGross = 0
    foreach ($key in $matchedWKeys) { $wDirect += $candidateByKey[$key].AccountNet - $baselineByKey[$key].AccountNet }
    foreach ($key in $matchedNonWKeys) { $matchedNonW += $candidateByKey[$key].AccountNet - $baselineByKey[$key].AccountNet }
    foreach ($key in $matchedWKeys) { $wDirectGross += $candidateByKey[$key].Gross - $baselineByKey[$key].Gross }
    foreach ($key in $matchedNonWKeys) { $matchedNonWGross += $candidateByKey[$key].Gross - $baselineByKey[$key].Gross }
    $added = Sum-Value ($addedKeys | ForEach-Object { $candidateByKey[$_] }) 'AccountNet'
    $removedContribution = -1 * (Sum-Value ($removedKeys | ForEach-Object { $baselineByKey[$_] }) 'AccountNet')
    $addedGross = Sum-Value ($addedKeys | ForEach-Object { $candidateByKey[$_] }) 'Gross'
    $removedGrossContribution = -1 * (Sum-Value ($removedKeys | ForEach-Object { $baselineByKey[$_] }) 'Gross')
    $baselinePortfolio = Sum-Value ($Baseline.Pnl | Where-Object TradingDate -in $SetDates) 'NetPnLDollars'
    $candidatePortfolio = Sum-Value ($Candidate.Pnl | Where-Object TradingDate -in $SetDates) 'NetPnLDollars'
    $portfolioDelta = [math]::Round($candidatePortfolio - $baselinePortfolio, 2)
    $retention = if ($wDirect -gt 0) { [math]::Round(100 * $portfolioDelta / $wDirect, 2) } else { $null }
    $projected184 = if ($SetDates.Count -gt 0) { [math]::Round($portfolioDelta / $SetDates.Count * 184, 2) } else { $null }

    return [pscustomobject]@{
        Set = $SetName
        Dates = $SetDates.Count
        BaselineTrades = $baselineSet.Count
        CandidateTrades = $candidateSet.Count
        MatchedWTrades = $matchedWKeys.Count
        WDirectActual = [math]::Round($wDirect, 2)
        WDirectGross = [math]::Round($wDirectGross, 2)
        MatchedNonWTrades = $matchedNonWKeys.Count
        MatchedNonWDelta = [math]::Round($matchedNonW, 2)
        MatchedNonWGrossDelta = [math]::Round($matchedNonWGross, 2)
        AddedTrades = $addedKeys.Count
        AddedContribution = $added
        AddedGrossContribution = $addedGross
        RemovedTrades = $removedKeys.Count
        RemovedContribution = $removedContribution
        RemovedGrossContribution = $removedGrossContribution
        BaselineAccountNet = $baselinePortfolio
        CandidateAccountNet = $candidatePortfolio
        PortfolioDelta = $portfolioDelta
        RetentionPercent = $retention
        Projected184 = $projected184
        EconomicScalePassed = $projected184 -ge 2000
    }
}

$selectedDates = @($Dates | ForEach-Object { ([datetime]$_).ToString('yyyy-MM-dd') } | Sort-Object -Unique)
$primary = @($PrimaryDates | ForEach-Object { ([datetime]$_).ToString('yyyy-MM-dd') } | Sort-Object -Unique)
if ($primary.Count -gt 0 -and @($primary | Where-Object { $_ -notin $selectedDates }).Count -gt 0) {
    throw 'PrimaryDates must be a subset of Dates.'
}
foreach ($path in @($ObservedSimulatorCsv, $ConservativeSimulatorCsv)) {
    if (-not [string]::IsNullOrWhiteSpace($path) -and -not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Simulator CSV not found: $path"
    }
}

$baselineEvidence = Read-Evidence $BaselineEvidenceDirectories 'Baseline' $selectedDates
$candidateEvidence = Read-Evidence $CandidateEvidenceDirectories 'Candidate' $selectedDates
$baseline = [pscustomobject]@{
    Trades = Build-TradeRows $baselineEvidence
    Pnl = @($baselineEvidence.live_account_pnl)
}
$candidate = [pscustomobject]@{
    Trades = Build-TradeRows $candidateEvidence
    Pnl = @($candidateEvidence.live_account_pnl)
    Decisions = @($candidateEvidence.execution_decisions)
    Policies = @($candidateEvidence.exit_policy_evaluations)
}

$missingBaseline = @($selectedDates | Where-Object { $_ -notin @($baseline.Trades.Date | Sort-Object -Unique) })
$missingCandidate = @($selectedDates | Where-Object { $_ -notin @($candidate.Trades.Date | Sort-Object -Unique) })
if ($missingBaseline.Count -gt 0 -or $missingCandidate.Count -gt 0) {
    throw "Missing requested trade dates. Baseline=[$($missingBaseline -join ',')] Candidate=[$($missingCandidate -join ',')]"
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$sets = [Collections.Generic.List[object]]::new()
$sets.Add((Build-Metrics 'All' $selectedDates $baseline $candidate))
if ($primary.Count -gt 0) { $sets.Add((Build-Metrics 'Primary' $primary $baseline $candidate)) }
$q4Dates = @($selectedDates | Where-Object { $_ -lt '2026-01-01' })
$h1Dates = @($selectedDates | Where-Object { $_ -ge '2026-01-01' })
if ($q4Dates.Count -gt 0) { $sets.Add((Build-Metrics 'Q4' $q4Dates $baseline $candidate)) }
if ($h1Dates.Count -gt 0) { $sets.Add((Build-Metrics 'H1' $h1Dates $baseline $candidate)) }
$sets | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'summary.csv') -NoTypeInformation -Encoding utf8
$daily = foreach ($date in $selectedDates) {
    Build-Metrics $date @($date) $baseline $candidate
}
$daily | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'daily.csv') -NoTypeInformation -Encoding utf8

$baselineByKey = @{}; foreach ($row in $baseline.Trades) { $baselineByKey[$row.Key] = $row }
$candidateByKey = @{}; foreach ($row in $candidate.Trades) { $candidateByKey[$row.Key] = $row }
$policyByKey = @{}
foreach ($row in $candidate.Policies | Where-Object { $_.ExitPolicy -eq 'Fixed2_5R' }) {
    $policyByKey[(Trade-Key $row)] = $row
}
$ledger = foreach ($key in @($candidateByKey.Keys | Sort-Object)) {
    $actual = $candidateByKey[$key]
    if ($actual.Side -ne 'Long' -or $actual.ResearchPath -ne $widePath) { continue }
    $plan = $policyByKey[$key]
    $base = $baselineByKey[$key]
    $shadowExitNormalized = if ($null -eq $plan) { '' } elseif ($plan.ExitReason -eq 'Target') { 'TP' } elseif ($plan.ExitReason -eq 'Stop') { 'SL' } elseif ($plan.ExitReason -eq 'TimeStop') { 'TIME_STOP' } else { $plan.ExitReason }
    $actualExitNormalized = if ($actual.ExitRole -eq 'TP') { 'TP' } elseif ($actual.ExitRole -eq 'SL') { 'SL' } elseif ($actual.ExitRole -eq 'TIME_STOP') { 'TIME_STOP' } else { $actual.ExitRole }
    $exitDiverged = $null -ne $plan -and $shadowExitNormalized -ne $actualExitNormalized
    $entryReprice = $exitDiverged -and [math]::Abs([double]($actual.EntryPrice - (Decimal-Value $plan.Entry))) -gt 0.001
    $riskReprice = $exitDiverged -and [math]::Abs([double]($actual.FilledRiskPoints - (Decimal-Value $plan.InitialRiskPoints))) -gt 0.001
    $sameBar = $exitDiverged -and $plan.AmbiguousStopAndTargetSameBar -eq 'True'
    $blocked = @($candidate.Decisions | Where-Object {
        $_.Reason -eq "ActiveTrade:$($actual.TradeID)" -and
        [int]$_.Bar -ge $actual.EntryBar -and [int]$_.Bar -le $actual.ExitBar
    }).Count
    [pscustomobject]@{
        Date = $actual.Date
        SignalID = $actual.SignalID
        ResearchPath = $actual.ResearchPath
        BaselineMatched = $null -ne $base
        BaselineExit = if ($null -eq $base) { '' } else { $base.ExitRole }
        BaselineNet = if ($null -eq $base) { $null } else { $base.AccountNet }
        ActualVsBaselineDelta = if ($null -eq $base) { $null } else { [math]::Round($actual.AccountNet - $base.AccountNet, 2) }
        PlannedEntry = if ($null -eq $plan) { $null } else { Decimal-Value $plan.Entry }
        ActualEntry = $actual.EntryPrice
        PlannedRisk = if ($null -eq $plan) { $null } else { Decimal-Value $plan.InitialRiskPoints }
        ActualRisk = $actual.FilledRiskPoints
        ShadowExit = if ($null -eq $plan) { '' } else { $plan.ExitReason }
        ActualExit = $actual.ExitRole
        ExitDiverged = $exitDiverged
        ShadowExitBar = if ($null -eq $plan) { $null } else { [int]$plan.PolicyExitBar }
        ActualExitBar = $actual.ExitBar
        ShadowNet = if ($null -eq $plan) { $null } else { [math]::Round((Decimal-Value $plan.PnLDollars) - $commission, 2) }
        ActualNet = $actual.AccountNet
        PnLDifference = if ($null -eq $plan) { $null } else { [math]::Round($actual.AccountNet - ((Decimal-Value $plan.PnLDollars) - $commission), 2) }
        EntryReprice = $entryReprice
        RiskReprice = $riskReprice
        SameBarOrdering = $sameBar
        BarsHeld = $actual.BarsHeld
        ActiveTradeBlockedCandidates = $blocked
    }
}
$ledger | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'w_divergence_ledger.csv') -NoTypeInformation -Encoding utf8

$holding = foreach ($source in @('Baseline', 'Candidate')) {
    $rows = if ($source -eq 'Baseline') { $baseline.Trades } else { $candidate.Trades }
    $rows | Where-Object { $_.Side -eq 'Long' -and $_.ResearchPath -eq $widePath } | ForEach-Object {
        [pscustomobject]@{ Source = $source; Date = $_.Date; SignalID = $_.SignalID; BarsHeld = $_.BarsHeld; ExitRole = $_.ExitRole; AccountNet = $_.AccountNet }
    }
}
$holding | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'w_holding_bars.csv') -NoTypeInformation -Encoding utf8

$simRows = [Collections.Generic.List[object]]::new()
$baselineDateBySnapshot = @{}
foreach ($row in $baseline.Trades) { $baselineDateBySnapshot[$row.SnapshotID] = $row.Date }
foreach ($item in @(@{ Mode = 'Observed'; Path = $ObservedSimulatorCsv }, @{ Mode = 'Conservative'; Path = $ConservativeSimulatorCsv })) {
    if ([string]::IsNullOrWhiteSpace($item.Path)) { continue }
    $sim = @(Import-Csv -LiteralPath $item.Path | Where-Object {
        $date = if ($baselineDateBySnapshot.ContainsKey($_.SnapshotID)) { $baselineDateBySnapshot[$_.SnapshotID] } else { Date-Key $_.EntryTime }
        $date -in $selectedDates
    })
    $simNet = if ($sim.Count -eq 0) { [decimal]0 } elseif ($sim[0].PSObject.Properties.Name -contains 'NetDollars') { Sum-Value $sim 'NetDollars' } else { Sum-Value $sim 'AccountNet' }
    $actualNet = ($sets | Where-Object Set -eq 'All').CandidateAccountNet
    $simByKey = @{}
    foreach ($row in $sim) { $simByKey[(Trade-Key $row)] = $row }
    $actualByKey = @{}
    foreach ($row in $candidate.Trades) { $actualByKey[$row.Key] = $row }
    $matchedKeys = @($simByKey.Keys | Where-Object { $actualByKey.ContainsKey($_) })
    [decimal]$matchedDelta = 0
    foreach ($key in $matchedKeys) {
        $simValue = if ($simByKey[$key].PSObject.Properties.Name -contains 'NetDollars') { Decimal-Value $simByKey[$key].NetDollars } else { Decimal-Value $simByKey[$key].AccountNet }
        $matchedDelta += $simValue - $actualByKey[$key].AccountNet
    }
    $simOnlyKeys = @($simByKey.Keys | Where-Object { -not $actualByKey.ContainsKey($_) })
    $actualOnlyKeys = @($actualByKey.Keys | Where-Object { -not $simByKey.ContainsKey($_) })
    [decimal]$simOnlyNet = 0
    foreach ($key in $simOnlyKeys) {
        $value = if ($simByKey[$key].PSObject.Properties.Name -contains 'NetDollars') { Decimal-Value $simByKey[$key].NetDollars } else { Decimal-Value $simByKey[$key].AccountNet }
        $simOnlyNet += $value
    }
    $actualOnlyContribution = -1 * (Sum-Value ($actualOnlyKeys | ForEach-Object { $actualByKey[$_] }) 'AccountNet')
    $simRows.Add([pscustomobject]@{
        Mode = $item.Mode
        SimulatorTrades = $sim.Count
        SimulatorNet = $simNet
        ActualTrades = $candidate.Trades.Count
        ActualNet = $actualNet
        MatchedTrades = $matchedKeys.Count
        MatchedNetDifference = [math]::Round($matchedDelta, 2)
        SimulatorOnlyTrades = $simOnlyKeys.Count
        SimulatorOnlyContribution = [math]::Round($simOnlyNet, 2)
        ActualOnlyTrades = $actualOnlyKeys.Count
        ActualOnlyContribution = $actualOnlyContribution
        NetDifference = [math]::Round($simNet - $actualNet, 2)
    })
}
if ($simRows.Count -gt 0) {
    $simRows | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'sim_vs_actual.csv') -NoTypeInformation -Encoding utf8
}

$all = $sets | Where-Object Set -eq 'All'
$primaryResult = if ($primary.Count -gt 0) { $sets | Where-Object Set -eq 'Primary' } else { $all }
$decision = if ($primary.Count -eq 0) {
    'REGRESSION_ONLY_NO_PRIMARY_DATES'
}
elseif ($primaryResult.WDirectActual -le 0) {
    'FAIL_W_DIRECT_NON_POSITIVE'
}
elseif (-not $all.EconomicScalePassed) {
    'FAIL_ECONOMIC_SCALE'
}
elseif ($primaryResult.RetentionPercent -ge 60) {
    'PASS_TO_20_DAY_PANEL'
}
elseif ($primaryResult.RetentionPercent -ge 45) {
    'BOUNDARY_USE_ONLY_FROZEN_8_DAYS'
}
else {
    'FAIL_RETENTION'
}

$report = @(
    '# v2.03 Actual Calibration Report',
    '',
    "Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')",
    '',
    ('Decision: `{0}`' -f $decision),
    '',
    '| Set | Dates | Baseline trades | Candidate trades | Matched W | W direct | Portfolio delta | Retention | Projected 184 |',
    '|---|---:|---:|---:|---:|---:|---:|---:|---:|'
)
foreach ($row in $sets) {
    $retentionText = if ($null -eq $row.RetentionPercent) { 'N/A' } else { '{0:N2}%' -f $row.RetentionPercent }
    $report += ('| {0} | {1} | {2} | {3} | {4} | ${5:N2} | ${6:N2} | {7} | ${8:N2} |' -f
        $row.Set,
        $row.Dates,
        $row.BaselineTrades,
        $row.CandidateTrades,
        $row.MatchedWTrades,
        $row.WDirectActual,
        $row.PortfolioDelta,
        $retentionText,
        $row.Projected184)
}
$report += @(
    '',
    'PrimaryDates controls the retention decision when supplied. All controls the 12-day economic-scale check. Q4 and H1 are explanatory only.',
    '',
    'Details: `summary.csv`, `daily.csv`, `w_divergence_ledger.csv`, and `w_holding_bars.csv`. Optional simulator inputs add `sim_vs_actual.csv`.'
)
$report | Set-Content -LiteralPath (Join-Path $OutputDirectory 'report.md') -Encoding utf8

$sets | Format-Table -AutoSize
Write-Output "Decision=$decision"
Write-Output "OutputDirectory=$OutputDirectory"
