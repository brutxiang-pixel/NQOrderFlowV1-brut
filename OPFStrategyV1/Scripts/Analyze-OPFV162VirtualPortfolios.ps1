param(
    [string]$LogDirectory = (Join-Path $env:APPDATA 'ATAS\StrategyLogs\OPFStrategyV1'),
    [string[]]$ExcludeDates = @(
        '2026-01-02','2026-01-12','2026-01-19','2026-01-22','2026-01-28',
        '2026-02-04','2026-02-09','2026-02-26','2026-03-13',
        '2026-04-16','2026-04-17','2026-04-23','2026-04-24','2026-04-30',
        '2026-05-04','2026-05-07'
    ),
    [string]$OutputPath = ''
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $LogDirectory)) {
    throw "OPF log directory not found: $LogDirectory"
}

$tradeFiles = @(Get-ChildItem -LiteralPath $LogDirectory -File -Filter '*_execution_trades.csv')
$decisionFiles = @(Get-ChildItem -LiteralPath $LogDirectory -File -Filter '*_execution_decisions.csv')
$policyFiles = @(Get-ChildItem -LiteralPath $LogDirectory -File -Filter '*_exit_policy_evaluations.csv')
if ($tradeFiles.Count -eq 0 -or $decisionFiles.Count -eq 0 -or $policyFiles.Count -eq 0) {
    throw 'execution trades, decisions, and exit-policy files are all required'
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $LogDirectory 'v162_virtual_portfolios.csv'
}

function Is-ExcludedDate([string]$value) {
    if ([string]::IsNullOrWhiteSpace($value)) {
        return $false
    }

    $date = ([datetime]$value).ToString('yyyy-MM-dd')
    return $ExcludeDates -contains $date
}

function Row-Key($row) {
    return "$($row.SnapshotID)|$($row.SignalID)|$($row.ResearchPath)"
}

$decisionByKey = @{}
foreach ($file in $decisionFiles) {
    foreach ($row in Import-Csv -LiteralPath $file.FullName) {
        if (Is-ExcludedDate $row.Time) {
            continue
        }
        if ($row.Decision -ne 'Skip' -or $row.StrategyEligible -ne 'True' -or $row.ExecutionScope -ne 'ResearchOnly') {
            continue
        }

        $key = Row-Key $row
        if (-not $decisionByKey.ContainsKey($key)) {
            $decisionByKey[$key] = $row
        }
    }
}

$actualEvents = @()
foreach ($file in $tradeFiles) {
    foreach ($row in Import-Csv -LiteralPath $file.FullName) {
        if (Is-ExcludedDate $row.EntryTime) {
            continue
        }

        $actualEvents += [pscustomobject]@{
            SnapshotID = $row.SnapshotID
            EntryTime = [datetime]$row.EntryTime
            EntryBar = [int]$row.EntryBar
            ExitBar = [int]$row.ExitBar
            PnlR = [decimal]$row.PointsR
            Dollars = [decimal]$row.Dollars
            Source = 'ActualV162'
            Priority = 0
            Ambiguous = $false
        }
    }
}

$candidatePolicies = @()
$ambiguousByPolicyAndPath = @{}
foreach ($file in $policyFiles) {
    foreach ($row in Import-Csv -LiteralPath $file.FullName) {
        if (Is-ExcludedDate $row.EntryTime) {
            continue
        }

        $isObservationStrictLong = $row.ResearchPath -eq 'ObservationStrict_Other' -and $row.Side -eq 'Long'
        $isTrendPullbackShort = $row.ResearchPath -eq 'TrendPullbackConfirmed' -and $row.Side -eq 'Short'
        if (-not $isObservationStrictLong -and -not $isTrendPullbackShort) {
            continue
        }

        $key = Row-Key $row
        if (-not $decisionByKey.ContainsKey($key)) {
            continue
        }

        $decision = $decisionByKey[$key]
        if ([decimal]$decision.SetupQualityScore -lt [decimal]45 -or
            [decimal]$decision.InitialRiskPoints -gt [decimal]25 -or
            [decimal]$decision.EstimatedRR -lt [decimal]0.5) {
            continue
        }

        $ambiguous = $row.AmbiguousStopAndTargetSameBar -eq 'True'
        $ambiguousKey = "$($row.ExitPolicy)|$($row.ResearchPath)"
        if ($ambiguous) {
            if (-not $ambiguousByPolicyAndPath.ContainsKey($ambiguousKey)) {
                $ambiguousByPolicyAndPath[$ambiguousKey] = 0
            }
            $ambiguousByPolicyAndPath[$ambiguousKey]++
            continue
        }

        $exitBar = if ([string]::IsNullOrWhiteSpace($row.PolicyExitBar)) { [int]$row.ExitBar } else { [int]$row.PolicyExitBar }
        $candidatePolicies += [pscustomobject]@{
            SnapshotID = $row.SnapshotID
            EntryTime = [datetime]$row.EntryTime
            EntryBar = [int]$row.EntryBar
            ExitBar = $exitBar
            PnlR = [decimal]$row.PnL_R
            Dollars = [decimal]$row.PnLDollars
            Source = $row.ResearchPath
            Priority = 1
            ExitPolicy = $row.ExitPolicy
            Ambiguous = $false
        }
    }
}

function Simulate-Portfolio($events) {
    $selected = @()
    $blockedActual = 0
    $blockedCandidate = 0

    foreach ($snapshot in @($events | Group-Object SnapshotID)) {
        $activeUntilBar = [int]::MinValue
        $dailyTrades = 0
        foreach ($event in @($snapshot.Group | Sort-Object EntryBar, Priority)) {
            if ($dailyTrades -ge 12 -or $event.EntryBar -le $activeUntilBar) {
                if ($event.Source -eq 'ActualV162') { $blockedActual++ } else { $blockedCandidate++ }
                continue
            }

            $selected += $event
            $dailyTrades++
            $activeUntilBar = [math]::Max($event.EntryBar, $event.ExitBar)
        }
    }

    $netR = [decimal](($selected | Measure-Object PnlR -Sum).Sum)
    $netDollars = [decimal](($selected | Measure-Object Dollars -Sum).Sum)
    $grossWin = [decimal](($selected | Where-Object { $_.Dollars -gt 0 } | Measure-Object Dollars -Sum).Sum)
    $grossLoss = [decimal](($selected | Where-Object { $_.Dollars -lt 0 } | Measure-Object Dollars -Sum).Sum)
    $wins = @($selected | Where-Object { $_.Dollars -gt 0 }).Count
    $peak = [decimal]0
    $equity = [decimal]0
    $maxDrawdown = [decimal]0
    foreach ($event in @($selected | Sort-Object EntryTime, EntryBar)) {
        $equity += $event.Dollars
        $peak = [math]::Max($peak, $equity)
        $maxDrawdown = [math]::Max($maxDrawdown, $peak - $equity)
    }

    return [pscustomobject]@{
        Trades = $selected.Count
        CandidateTrades = @($selected | Where-Object { $_.Source -ne 'ActualV162' }).Count
        BlockedActual = $blockedActual
        BlockedCandidate = $blockedCandidate
        NetR = [math]::Round($netR, 2)
        NetDollars = [math]::Round($netDollars, 2)
        WinRate = if ($selected.Count -eq 0) { 0 } else { [math]::Round(100 * $wins / $selected.Count, 2) }
        ProfitFactor = if ($grossLoss -eq 0) { 0 } else { [math]::Round($grossWin / [math]::Abs($grossLoss), 3) }
        MaxDrawdownDollars = [math]::Round($maxDrawdown, 2)
    }
}

$lanes = @(
    [pscustomobject]@{ Name = 'ActualV162'; Paths = @() },
    [pscustomobject]@{ Name = 'ActualV162+ObservationStrictOtherLong'; Paths = @('ObservationStrict_Other') },
    [pscustomobject]@{ Name = 'ActualV162+TrendPullbackConfirmedShort'; Paths = @('TrendPullbackConfirmed') },
    [pscustomobject]@{ Name = 'ActualV162+BothResearchPools'; Paths = @('ObservationStrict_Other','TrendPullbackConfirmed') }
)

$results = @()
$policies = @(
    'Fixed1_5R','Fixed2R','Fixed2_5R','Fixed3R',
    'SplitBase_Runner2_5R_BE0_75R','SplitBase_Runner2_5R_BE1R',
    'SplitBase_Runner3R_BE0_75R','SplitBase_Runner3R_BE1R',
    'ProtectBE0_75R_Then2_5R','ProtectBE1R_Then2_5R','ProtectBE1R_Then3R',
    'Protect1RAfter1_5R_Then2_5R','Protect1RAfter1_5R_Then3R'
)
foreach ($policy in $policies) {
    foreach ($lane in $lanes) {
        $events = @($actualEvents)
        if ($lane.Paths.Count -gt 0) {
            $events += @($candidatePolicies | Where-Object { $_.ExitPolicy -eq $policy -and $lane.Paths -contains $_.Source })
        }

        $summary = Simulate-Portfolio $events
        $ambiguousRejected = 0
        foreach ($path in $lane.Paths) {
            $key = "$policy|$path"
            if ($ambiguousByPolicyAndPath.ContainsKey($key)) {
                $ambiguousRejected += $ambiguousByPolicyAndPath[$key]
            }
        }

        $results += [pscustomobject]@{
            ExitPolicy = $policy
            Portfolio = $lane.Name
            Trades = $summary.Trades
            CandidateTrades = $summary.CandidateTrades
            BlockedActual = $summary.BlockedActual
            BlockedCandidate = $summary.BlockedCandidate
            AmbiguousRejected = $ambiguousRejected
            NetR = $summary.NetR
            NetDollars = $summary.NetDollars
            WinRate = $summary.WinRate
            ProfitFactor = $summary.ProfitFactor
            MaxDrawdownDollars = $summary.MaxDrawdownDollars
        }
    }
}

$results | Export-Csv -LiteralPath $OutputPath -NoTypeInformation -Encoding UTF8
$results | Sort-Object ExitPolicy, Portfolio | Format-Table -AutoSize
Write-Host "Virtual portfolio report: $OutputPath"
