param(
    [string[]]$ExcludeDates = @('2026-04-16')
)

$ErrorActionPreference = 'Stop'

$dir = Join-Path $env:APPDATA 'ATAS\StrategyLogs\OPFStrategyV1'

if (-not (Test-Path -LiteralPath $dir)) {
    Write-Host "OPF log directory not found: $dir"
    exit 0
}

$files = Get-ChildItem -LiteralPath $dir -File -Filter '*_research_outcomes.csv'
if ($files.Count -eq 0) {
    Write-Host "No research outcome files found: $dir"
    exit 0
}

$rows = @($files | ForEach-Object { Import-Csv -LiteralPath $_.FullName })
$signalFiles = Get-ChildItem -LiteralPath $dir -File -Filter '*_signals.csv'
$signalRows = @($signalFiles | ForEach-Object { Import-Csv -LiteralPath $_.FullName })
$noTradeFiles = Get-ChildItem -LiteralPath $dir -File -Filter '*_no_trade.csv'
$noTradeRows = @($noTradeFiles | ForEach-Object { Import-Csv -LiteralPath $_.FullName })
$candidateEvaluationFiles = Get-ChildItem -LiteralPath $dir -File -Filter '*_candidate_evaluations.csv'
$candidateEvaluationRows = @($candidateEvaluationFiles | ForEach-Object { Import-Csv -LiteralPath $_.FullName })
$confirmationEvaluationFiles = Get-ChildItem -LiteralPath $dir -File -Filter '*_confirmation_evaluations.csv'
$confirmationEvaluationRows = @($confirmationEvaluationFiles | ForEach-Object { Import-Csv -LiteralPath $_.FullName })
$regimeDailyFiles = Get-ChildItem -LiteralPath $dir -File -Filter '*_regime_daily.csv'
$regimeDailyRows = @($regimeDailyFiles | ForEach-Object { Import-Csv -LiteralPath $_.FullName })
$regimeFiles = Get-ChildItem -LiteralPath $dir -File -Filter '*_regime_changes.csv'
$regimeRows = @($regimeFiles | ForEach-Object { Import-Csv -LiteralPath $_.FullName })
$riskEvaluationFiles = Get-ChildItem -LiteralPath $dir -File -Filter '*_risk_evaluations.csv'
$riskEvaluationRows = @($riskEvaluationFiles | ForEach-Object { Import-Csv -LiteralPath $_.FullName })
$scoreBreakdownFiles = Get-ChildItem -LiteralPath $dir -File -Filter '*_score_breakdown.csv'
$scoreBreakdownRows = @($scoreBreakdownFiles | ForEach-Object { Import-Csv -LiteralPath $_.FullName })
$executionEventFiles = Get-ChildItem -LiteralPath $dir -File -Filter '*_execution_events.csv'
$executionEventRows = @($executionEventFiles | ForEach-Object { Import-Csv -LiteralPath $_.FullName })
$executionDecisionFiles = Get-ChildItem -LiteralPath $dir -File -Filter '*_execution_decisions.csv'
$executionDecisionRows = @($executionDecisionFiles | ForEach-Object { Import-Csv -LiteralPath $_.FullName })
$executionTradeFiles = Get-ChildItem -LiteralPath $dir -File -Filter '*_execution_trades.csv'
$executionTradeRows = @($executionTradeFiles | ForEach-Object { Import-Csv -LiteralPath $_.FullName })
$liveAccountPnlFiles = Get-ChildItem -LiteralPath $dir -File -Filter '*_live_account_pnl.csv'
$liveAccountPnlRows = @($liveAccountPnlFiles | ForEach-Object { Import-Csv -LiteralPath $_.FullName })
$exitPolicyFiles = Get-ChildItem -LiteralPath $dir -File -Filter '*_exit_policy_evaluations.csv'
$exitPolicyRows = @($exitPolicyFiles | ForEach-Object { Import-Csv -LiteralPath $_.FullName })
$snapshotFiles = Get-ChildItem -LiteralPath $dir -File -Filter '*_ConfigSnapshot.json'
$snapshots = @($snapshotFiles | ForEach-Object {
    $json = Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json
    [pscustomobject]@{
        SnapshotID = $json.SnapshotId
        StrategyVersion = $json.StrategyVersion
        ResearchSchemaVersion = $json.ResearchSchemaVersion
        InstrumentProfileName = $json.InstrumentProfile.Version
        ExecutionProfileName = $json.ExecutionProfile.Name
        ProfileCatalogVersion = $json.ProfileCatalogVersion
        RequestedInstrumentProfileName = $json.RequestedInstrumentProfileName
        RequestedExecutionProfileName = $json.RequestedExecutionProfileName
        UsedInstrumentProfileFallback = $json.UsedInstrumentProfileFallback
        UsedExecutionProfileFallback = $json.UsedExecutionProfileFallback
        ActualExecutionConfigStatus = $json.ActualExecutionConfigStatus
        ActualExecutionConfigPath = $json.ActualExecutionConfigPath
        ActualExecutionSettingsVersion = $json.ActualExecutionSettings.Version
        File = $_.Name
    }
})

function Row-Date($row) {
    foreach ($name in @('EntryTime', 'Time', 'Date')) {
        if ($row.PSObject.Properties.Name -contains $name) {
            $value = [string]$row.$name
            if (-not [string]::IsNullOrWhiteSpace($value)) {
                try {
                    return ([datetime]$value).ToString('yyyy-MM-dd')
                }
                catch {
                    if ($value.Length -ge 10) {
                        return $value.Substring(0, 10)
                    }
                }
            }
        }
    }

    return ''
}

function Filter-ExcludedDates($items, $dates, $excludedSnapshots) {
    if (($null -eq $dates -or $dates.Count -eq 0) -and ($null -eq $excludedSnapshots -or $excludedSnapshots.Count -eq 0)) {
        return @($items)
    }

    $exclude = @{}
    foreach ($date in $dates) {
        if (-not [string]::IsNullOrWhiteSpace($date)) {
            $exclude[$date] = $true
        }
    }

    return @($items | Where-Object {
        $snapshotId = ''
        if ($_.PSObject.Properties.Name -contains 'SnapshotID') {
            $snapshotId = [string]$_.SnapshotID
        }

        -not $excludedSnapshots.ContainsKey($snapshotId) -and -not $exclude.ContainsKey((Row-Date $_))
    })
}

$excludedSnapshotIds = @{}
if ($null -ne $ExcludeDates -and $ExcludeDates.Count -gt 0) {
    $excludeDateLookup = @{}
    foreach ($date in $ExcludeDates) {
        if (-not [string]::IsNullOrWhiteSpace($date)) {
            $excludeDateLookup[$date] = $true
        }
    }

    foreach ($daily in $regimeDailyRows) {
        if ($excludeDateLookup.ContainsKey((Row-Date $daily))) {
            $excludedSnapshotIds[[string]$daily.SnapshotID] = $true
        }
    }
}

$originalCounts = [pscustomobject]@{
    ResearchOutcomeRows = $rows.Count
    SignalRows = $signalRows.Count
    NoTradeRows = $noTradeRows.Count
    CandidateEvaluationRows = $candidateEvaluationRows.Count
    ConfirmationEvaluationRows = $confirmationEvaluationRows.Count
    RegimeDailyRows = $regimeDailyRows.Count
    RegimeChangeRows = $regimeRows.Count
    RiskEvaluationRows = $riskEvaluationRows.Count
    ScoreBreakdownRows = $scoreBreakdownRows.Count
    ExecutionEventRows = $executionEventRows.Count
    ExecutionDecisionRows = $executionDecisionRows.Count
    ExecutionTradeRows = $executionTradeRows.Count
    LiveAccountPnlRows = $liveAccountPnlRows.Count
    ExitPolicyRows = $exitPolicyRows.Count
}

$rows = @(Filter-ExcludedDates $rows $ExcludeDates $excludedSnapshotIds)
$signalRows = @(Filter-ExcludedDates $signalRows $ExcludeDates $excludedSnapshotIds)
$noTradeRows = @(Filter-ExcludedDates $noTradeRows $ExcludeDates $excludedSnapshotIds)
$candidateEvaluationRows = @(Filter-ExcludedDates $candidateEvaluationRows $ExcludeDates $excludedSnapshotIds)
$confirmationEvaluationRows = @(Filter-ExcludedDates $confirmationEvaluationRows $ExcludeDates $excludedSnapshotIds)
$regimeDailyRows = @(Filter-ExcludedDates $regimeDailyRows $ExcludeDates $excludedSnapshotIds)
$regimeRows = @(Filter-ExcludedDates $regimeRows $ExcludeDates $excludedSnapshotIds)
$riskEvaluationRows = @(Filter-ExcludedDates $riskEvaluationRows $ExcludeDates $excludedSnapshotIds)
$scoreBreakdownRows = @(Filter-ExcludedDates $scoreBreakdownRows $ExcludeDates $excludedSnapshotIds)
$executionEventRows = @(Filter-ExcludedDates $executionEventRows $ExcludeDates $excludedSnapshotIds)
$executionDecisionRows = @(Filter-ExcludedDates $executionDecisionRows $ExcludeDates $excludedSnapshotIds)
$executionTradeRows = @(Filter-ExcludedDates $executionTradeRows $ExcludeDates $excludedSnapshotIds)
$liveAccountPnlRows = @(Filter-ExcludedDates $liveAccountPnlRows $ExcludeDates $excludedSnapshotIds)
$exitPolicyRows = @(Filter-ExcludedDates $exitPolicyRows $ExcludeDates $excludedSnapshotIds)

$filteredCounts = [pscustomobject]@{
    ResearchOutcomeRows = $rows.Count
    SignalRows = $signalRows.Count
    NoTradeRows = $noTradeRows.Count
    CandidateEvaluationRows = $candidateEvaluationRows.Count
    ConfirmationEvaluationRows = $confirmationEvaluationRows.Count
    RegimeDailyRows = $regimeDailyRows.Count
    RegimeChangeRows = $regimeRows.Count
    RiskEvaluationRows = $riskEvaluationRows.Count
    ScoreBreakdownRows = $scoreBreakdownRows.Count
    ExecutionEventRows = $executionEventRows.Count
    ExecutionDecisionRows = $executionDecisionRows.Count
    ExecutionTradeRows = $executionTradeRows.Count
    LiveAccountPnlRows = $liveAccountPnlRows.Count
    ExitPolicyRows = $exitPolicyRows.Count
}

function To-Number($value) {
    if ([string]::IsNullOrWhiteSpace([string]$value)) {
        return 0.0
    }

    return [double]$value
}

function Is-True($value) {
    return [string]$value -eq 'True'
}

function Count-True($items, $name) {
    return @($items | Where-Object { Is-True (Field $_ $name) }).Count
}

function Count-Equals($items, $name, $value) {
    return @($items | Where-Object { (Field $_ $name) -eq $value }).Count
}

function Reason-Category($reason) {
    $text = [string]$reason

    if ([string]::IsNullOrWhiteSpace($text)) {
        return 'Unspecified'
    }

    if ($text -like '*Regime*') {
        return 'Regime'
    }

    if ($text -like '*Risk*') {
        return 'Risk'
    }

    if ($text -like '*Expired*') {
        return 'Expiry'
    }

    if ($text -like '*Zone*' -or $text -like '*FVG*' -or $text -like '*Touch*' -or $text -like '*Mitigated*') {
        return 'ZoneLifecycle'
    }

    if ($text -like '*Confirm*' -or $text -like '*Retest*' -or $text -like '*Reclaim*' -or $text -like '*Wait*') {
        return 'Confirmation'
    }

    if ($text -like '*Structure*' -or $text -like '*Swing*') {
        return 'Structure'
    }

    if ($text -like '*ResearchOnly*') {
        return 'ResearchMode'
    }

    return 'Other'
}

function Score-Bucket($score) {
    $n = To-Number $score
    if ($n -ge 90) { return '90+' }
    if ($n -ge 80) { return '80-89' }
    if ($n -ge 70) { return '70-79' }
    if ($n -ge 50) { return '50-69' }
    return '<50'
}

function Field($row, $name) {
    if ($null -eq $row) {
        return ''
    }

    if ($row.PSObject.Properties.Name -contains $name) {
        return $row.$name
    }

    return ''
}

$signalById = @{}
foreach ($signal in $signalRows) {
    if (-not [string]::IsNullOrWhiteSpace($signal.SignalID)) {
        $signalById[$signal.SignalID] = $signal
    }
}

$regimeDailyBySnapshotDate = @{}
foreach ($daily in $regimeDailyRows) {
    $regimeDailyBySnapshotDate["$($daily.SnapshotID)|$($daily.Date)"] = $daily
}

$snapshotById = @{}
foreach ($snapshot in $snapshots) {
    $snapshotById[$snapshot.SnapshotID] = $snapshot
}

$enrichedRows = @(foreach ($row in $rows) {
    $entryDate = ([datetime]$row.EntryTime).ToString('yyyy-MM-dd')
    $signal = $signalById[$row.SignalID]
    $daily = $regimeDailyBySnapshotDate["$($row.SnapshotID)|$entryDate"]
    $snapshot = $snapshotById[$row.SnapshotID]

    [pscustomobject]@{
        Row = $row
        EntryDate = $entryDate
        SnapshotID = $row.SnapshotID
        SignalID = $row.SignalID
        ResearchPath = $row.ResearchPath
        ZoneType = if ($signal) { $signal.ZoneType } else { '' }
        ZoneFreshness = if ($signal) { $signal.ZoneFreshness } else { '' }
        SetupType = if ($signal) { $signal.SetupType } else { '' }
        BullTrendPct = if ($daily) { To-Number $daily.BullTrendPct } else { 0.0 }
        BearTrendPct = if ($daily) { To-Number $daily.BearTrendPct } else { 0.0 }
        UnknownPct = if ($daily) { To-Number $daily.UnknownPct } else { 0.0 }
        RegimeChangeCount = if ($daily) { [int]$daily.RegimeChangeCount } else { 0 }
        HasSignalMetadata = $null -ne $signal
        HasRegimeDaily = $null -ne $daily
        HasSnapshot = $null -ne $snapshot
        StrategyVersion = if ($snapshot) { $snapshot.StrategyVersion } else { $row.StrategyVersion }
        ResearchSchemaVersion = if ($snapshot) { $snapshot.ResearchSchemaVersion } else { $row.ResearchSchemaVersion }
        InstrumentProfileName = if ($snapshot) { $snapshot.InstrumentProfileName } else { $row.InstrumentProfileName }
        ExecutionProfileName = if ($snapshot) { $snapshot.ExecutionProfileName } else { $row.ExecutionProfileName }
        ProfileCatalogVersion = if ($snapshot) { $snapshot.ProfileCatalogVersion } else { $row.ProfileCatalogVersion }
    }
})

$replayIndexRows = @(foreach ($item in $enrichedRows) {
    $row = $item.Row
    $signal = $signalById[$row.SignalID]

    [pscustomobject]@{
        SnapshotID = $item.SnapshotID
        SignalID = $item.SignalID
        EntryTime = $row.EntryTime
        EntryBar = $row.EntryBar
        Side = $row.Side
        ResearchPath = $row.ResearchPath
        OutcomeClass = $row.OutcomeClass
        Hit1R = $row.Hit1R
        Hit2R = $row.Hit2R
        Hit2_5R = Field $row 'Hit2_5R'
        Hit3R = Field $row 'Hit3R'
        MFE_R = $row.MFE_R
        MAE_R = $row.MAE_R
        PointValue = Field $row 'PointValue'
        PlannedContracts = Field $row 'PlannedContracts'
        ActualContracts = Field $row 'ActualContracts'
        RiskPerContractDollars = Field $row 'RiskPerContractDollars'
        TotalInitialRiskDollars = Field $row 'TotalInitialRiskDollars'
        MFE_Dollars = Field $row 'MFE_Dollars'
        MAE_Dollars = Field $row 'MAE_Dollars'
        ActualVerified = Field $row 'ActualVerified'
        ActualTradeID = Field $row 'ActualTradeID'
        ActualExitRole = Field $row 'ActualExitRole'
        ActualPnL_R = Field $row 'ActualPnL_R'
        ActualPnLDollars = Field $row 'ActualPnLDollars'
        ExitEfficiency = Field $row 'ExitEfficiency'
        RunupCapturePct = Field $row 'RunupCapturePct'
        AdverseBeforeProfit_R = Field $row 'AdverseBeforeProfit_R'
        IntraBarAmbiguous = Field $row 'IntraBarAmbiguous'
        ResolvedOutcomeClass = Field $row 'ResolvedOutcomeClass'
        OutcomeSource = Field $row 'OutcomeSource'
        WouldTradeLive = Field $row 'WouldTradeLive'
        ResearchOnlySignal = Field $row 'ResearchOnlySignal'
        SkippedByDailyGuard = Field $row 'SkippedByDailyGuard'
        ExecutionSkipReasons = Field $row 'ExecutionSkipReasons'
        DailyTargetDollars = Field $row 'DailyTargetDollars'
        DailyLossLimitDollars = Field $row 'DailyLossLimitDollars'
        MaxContracts = Field $row 'MaxContracts'
        ZoneID = if ($signal) { $signal.ZoneID } else { '' }
        ZoneType = $item.ZoneType
        ZoneFreshness = $item.ZoneFreshness
        UnknownPct = $item.UnknownPct
        RegimeChanges = $item.RegimeChangeCount
        StrategyVersion = $item.StrategyVersion
        ResearchSchemaVersion = $item.ResearchSchemaVersion
        InstrumentProfileName = $item.InstrumentProfileName
        ExecutionProfileName = $item.ExecutionProfileName
        ProfileCatalogVersion = $item.ProfileCatalogVersion
    }
})

if ($replayIndexRows.Count -gt 0) {
    $replayIndexPath = Join-Path $dir 'replay_index.csv'
    try {
        $replayIndexRows | Export-Csv -LiteralPath $replayIndexPath -NoTypeInformation -ErrorAction Stop
    }
    catch {
        $fallbackReplayIndexPath = Join-Path $dir ("replay_index_{0}.csv" -f (Get-Date -Format 'yyyyMMdd_HHmmss'))
        try {
            $replayIndexRows | Export-Csv -LiteralPath $fallbackReplayIndexPath -NoTypeInformation -ErrorAction Stop
            $replayIndexPath = $fallbackReplayIndexPath
        }
        catch {
            $replayIndexPath = ''
            Write-Warning "Replay index could not be written. Summary will continue without exporting replay_index.csv."
        }
    }
}

Write-Host "=== OPF Research Summary ==="
Write-Host "Files: $($files.Count)  Rows: $($rows.Count)"
Write-Host "Excluded Dates: $($ExcludeDates -join '|')"
Write-Host "Excluded Snapshots: $((@($excludedSnapshotIds.Keys) | Sort-Object) -join '|')"
Write-Host ""
Write-Host "=== Exclusion Filter ==="
[pscustomobject]@{
    ResearchOutcomeRows = "$($originalCounts.ResearchOutcomeRows) -> $($filteredCounts.ResearchOutcomeRows)"
    SignalRows = "$($originalCounts.SignalRows) -> $($filteredCounts.SignalRows)"
    ExecutionEventRows = "$($originalCounts.ExecutionEventRows) -> $($filteredCounts.ExecutionEventRows)"
    ExecutionDecisionRows = "$($originalCounts.ExecutionDecisionRows) -> $($filteredCounts.ExecutionDecisionRows)"
    ExecutionTradeRows = "$($originalCounts.ExecutionTradeRows) -> $($filteredCounts.ExecutionTradeRows)"
    RegimeDailyRows = "$($originalCounts.RegimeDailyRows) -> $($filteredCounts.RegimeDailyRows)"
} | Format-List

if ($rows.Count -eq 0) {
    Write-Host "No research outcome rows remain after exclusion filter."
    exit 0
}
Write-Host ""

Write-Host "=== Schema And Profile Consistency ==="
[pscustomobject]@{
    Snapshots = $snapshots.Count
    StrategyVersions = (@($enrichedRows | Select-Object -ExpandProperty StrategyVersion -Unique) -join '|')
    ResearchSchemas = (@($enrichedRows | Select-Object -ExpandProperty ResearchSchemaVersion -Unique) -join '|')
    Instruments = (@($enrichedRows | Select-Object -ExpandProperty InstrumentProfileName -Unique) -join '|')
    Executions = (@($enrichedRows | Select-Object -ExpandProperty ExecutionProfileName -Unique) -join '|')
    ProfileCatalogs = (@($enrichedRows | Select-Object -ExpandProperty ProfileCatalogVersion -Unique) -join '|')
    OutcomesMissingSnapshot = ($enrichedRows | Where-Object { -not $_.HasSnapshot }).Count
    InstrumentFallbacks = ($snapshots | Where-Object { $_.UsedInstrumentProfileFallback -eq $true }).Count
    ExecutionFallbacks = ($snapshots | Where-Object { $_.UsedExecutionProfileFallback -eq $true }).Count
    ActualExecutionConfigStatuses = (@($snapshots | Select-Object -ExpandProperty ActualExecutionConfigStatus -Unique) -join '|')
    ActualExecutionSettingsVersions = (@($snapshots | Select-Object -ExpandProperty ActualExecutionSettingsVersion -Unique) -join '|')
} | Format-List

if ($replayIndexRows.Count -gt 0 -and -not [string]::IsNullOrWhiteSpace($replayIndexPath)) {
    Write-Host "Replay index written: $replayIndexPath"
}

Write-Host ""

$rows |
    Group-Object ResearchPath |
    ForEach-Object {
        $g = $_.Group
        $mfe = @($g | ForEach-Object { To-Number $_.MFE_R })
        $mae = @($g | ForEach-Object { To-Number $_.MAE_R })
        $risk = @($g | ForEach-Object { To-Number $_.InitialRiskPoints })
        $exitEfficiency = @($g | ForEach-Object { To-Number (Field $_ 'ExitEfficiency') })
        $runupCapture = @($g | ForEach-Object { To-Number (Field $_ 'RunupCapturePct') })
        $adverseBeforeProfit = @($g | ForEach-Object { To-Number (Field $_ 'AdverseBeforeProfit_R') })

        [pscustomobject]@{
            ResearchPath = $_.Name
            N = $g.Count
            Hit1R = Count-True $g 'Hit1R'
            Hit2R = Count-True $g 'Hit2R'
            Hit2_5R = Count-True $g 'Hit2_5R'
            Hit3R = Count-True $g 'Hit3R'
            StopBefore1R = Count-True $g 'StopHitBefore1R'
            Ambiguous = Count-True $g 'AmbiguousStopAndTargetSameBar'
            AvgRiskPts = [math]::Round(($risk | Measure-Object -Average).Average, 2)
            AvgMFE_R = [math]::Round(($mfe | Measure-Object -Average).Average, 2)
            AvgMAE_R = [math]::Round(($mae | Measure-Object -Average).Average, 2)
            AvgExitEfficiency = [math]::Round(($exitEfficiency | Measure-Object -Average).Average, 2)
            AvgRunupCapturePct = [math]::Round(($runupCapture | Measure-Object -Average).Average, 2)
            AvgAdverseBeforeProfit_R = [math]::Round(($adverseBeforeProfit | Measure-Object -Average).Average, 2)
        }
    } |
    Sort-Object ResearchPath |
    Format-Table -AutoSize

Write-Host ""
Write-Host "=== Outcome Class ==="
$rows |
    Group-Object ResearchPath, OutcomeClass |
    Sort-Object Name |
    Select-Object Count, Name |
    Format-Table -AutoSize

if ($rows[0].PSObject.Properties.Name -contains 'ActualVerified') {
    Write-Host ""
    Write-Host "=== Outcome Source / Actual Verification ==="
    $rows |
        Group-Object ResearchPath, OutcomeSource, ActualVerified |
        Sort-Object Name |
        Select-Object Count, Name |
        Format-Table -AutoSize

    Write-Host ""
    Write-Host "=== Resolved Outcome Class ==="
    $rows |
        Group-Object ResearchPath, ResolvedOutcomeClass |
        Sort-Object Name |
        Select-Object Count, Name |
        Format-Table -AutoSize

    Write-Host ""
    Write-Host "=== Actual Verified Research Rows ==="
    $actualRows = @($rows | Where-Object { Is-True (Field $_ 'ActualVerified') })
    if ($actualRows.Count -gt 0) {
        $actualRows |
            Select-Object SignalID, ResearchPath, OutcomeClass, ResolvedOutcomeClass, IntraBarAmbiguous, ActualTradeID, ActualExitRole, ActualPnL_R, ActualPnLDollars |
            Format-Table -AutoSize
    }
    else {
        Write-Host "None"
    }
}

Write-Host ""
Write-Host "=== Stop Basis ==="
$rows |
    Group-Object ResearchPath, StopBasis |
    Sort-Object Name |
    Select-Object Count, Name |
    Format-Table -AutoSize

Write-Host ""
Write-Host "=== By Entry Date And Path ==="
$rows |
    Group-Object { ([datetime]$_.EntryTime).ToString('yyyy-MM-dd') }, ResearchPath |
    ForEach-Object {
        $g = $_.Group
        $mfe = @($g | ForEach-Object { To-Number $_.MFE_R })
        $mae = @($g | ForEach-Object { To-Number $_.MAE_R })
        $risk = @($g | ForEach-Object { To-Number $_.InitialRiskPoints })

        [pscustomobject]@{
            DatePath = $_.Name
            N = $g.Count
            Hit1R = Count-True $g 'Hit1R'
            Hit2R = Count-True $g 'Hit2R'
            Hit2_5R = Count-True $g 'Hit2_5R'
            Hit3R = Count-True $g 'Hit3R'
            StopBefore1R = Count-True $g 'StopHitBefore1R'
            AvgRiskPts = [math]::Round(($risk | Measure-Object -Average).Average, 2)
            AvgMFE_R = [math]::Round(($mfe | Measure-Object -Average).Average, 2)
            AvgMAE_R = [math]::Round(($mae | Measure-Object -Average).Average, 2)
        }
    } |
    Sort-Object DatePath |
    Format-Table -AutoSize

Write-Host ""
Write-Host "=== By Snapshot And Path ==="
$rows |
    Group-Object SnapshotID, ResearchPath |
    ForEach-Object {
        $g = $_.Group
        [pscustomobject]@{
            SnapshotPath = $_.Name
            N = $g.Count
            Hit1R = Count-True $g 'Hit1R'
            Hit2R = Count-True $g 'Hit2R'
            Hit2_5R = Count-True $g 'Hit2_5R'
            Hit3R = Count-True $g 'Hit3R'
            StopBefore1R = Count-True $g 'StopHitBefore1R'
        }
    } |
    Sort-Object SnapshotPath |
    Format-Table -AutoSize

Write-Host ""
Write-Host "=== By Entry Date, Regime, And Path ==="
$enrichedRows |
    Group-Object SnapshotID, EntryDate, ResearchPath |
    ForEach-Object {
        $g = $_.Group
        $outcomes = @($g | ForEach-Object { $_.Row })
        $mfe = @($outcomes | ForEach-Object { To-Number $_.MFE_R })
        $mae = @($outcomes | ForEach-Object { To-Number $_.MAE_R })
        $first = $g[0]

        [pscustomobject]@{
            Date = $first.EntryDate
            ResearchPath = $first.ResearchPath
            N = $g.Count
            Hit1R = Count-True $outcomes 'Hit1R'
            Hit2R = Count-True $outcomes 'Hit2R'
            Hit2_5R = Count-True $outcomes 'Hit2_5R'
            Hit3R = Count-True $outcomes 'Hit3R'
            StopBefore1R = Count-True $outcomes 'StopHitBefore1R'
            UnknownPct = $first.UnknownPct
            RegimeChanges = $first.RegimeChangeCount
            AvgMFE_R = [math]::Round(($mfe | Measure-Object -Average).Average, 2)
            AvgMAE_R = [math]::Round(($mae | Measure-Object -Average).Average, 2)
        }
    } |
    Sort-Object Date, SnapshotID, ResearchPath |
    Format-Table -AutoSize

Write-Host ""
Write-Host "=== By Zone Type And Path ==="
$enrichedRows |
    Group-Object ResearchPath, ZoneType, ZoneFreshness |
    ForEach-Object {
        $g = $_.Group
        $outcomes = @($g | ForEach-Object { $_.Row })
        $mfe = @($outcomes | ForEach-Object { To-Number $_.MFE_R })
        $mae = @($outcomes | ForEach-Object { To-Number $_.MAE_R })

        [pscustomobject]@{
            ResearchPath = $g[0].ResearchPath
            ZoneType = $g[0].ZoneType
            Freshness = $g[0].ZoneFreshness
            N = $g.Count
            Hit1R = Count-True $outcomes 'Hit1R'
            Hit2R = Count-True $outcomes 'Hit2R'
            Hit2_5R = Count-True $outcomes 'Hit2_5R'
            Hit3R = Count-True $outcomes 'Hit3R'
            StopBefore1R = Count-True $outcomes 'StopHitBefore1R'
            Catastrophic = Count-Equals $outcomes 'OutcomeClass' 'Catastrophic'
            AvgMFE_R = [math]::Round(($mfe | Measure-Object -Average).Average, 2)
            AvgMAE_R = [math]::Round(($mae | Measure-Object -Average).Average, 2)
        }
    } |
    Sort-Object ResearchPath, ZoneType, Freshness |
    Format-Table -AutoSize

Write-Host ""
Write-Host "=== Signal Stage Funnel ==="
$signalRows |
    Group-Object Stage, SetupType |
    Sort-Object Name |
    Select-Object Count, Name |
    Format-Table -AutoSize

if ($noTradeRows.Count -gt 0) {
    Write-Host ""
    Write-Host "=== No Trade By Stage ==="
    $noTradeRows |
        Group-Object SetupStage |
        Sort-Object Count -Descending |
        Select-Object Count, Name |
        Format-Table -AutoSize

    Write-Host ""
    Write-Host "=== No Trade Reasons ==="
    $noTradeRows |
        ForEach-Object { ([string]$_.SkipReasons).Split('|', [System.StringSplitOptions]::RemoveEmptyEntries) } |
        Group-Object |
        Sort-Object Count -Descending |
        Select-Object Count, Name |
        Format-Table -AutoSize

    Write-Host ""
    Write-Host "=== No Trade Reason Categories ==="
    $noTradeRows |
        ForEach-Object { ([string]$_.SkipReasons).Split('|', [System.StringSplitOptions]::RemoveEmptyEntries) } |
        ForEach-Object { Reason-Category $_ } |
        Group-Object |
        Sort-Object Count -Descending |
        Select-Object Count, Name |
        Format-Table -AutoSize
}

if ($candidateEvaluationRows.Count -gt 0) {
    Write-Host ""
    Write-Host "=== Candidate Evaluation Funnel ==="
    $candidateEvaluationRows |
        Group-Object Result |
        Sort-Object Count -Descending |
        Select-Object Count, Name |
        Format-Table -AutoSize

    Write-Host ""
    Write-Host "=== Candidate Evaluation Reasons ==="
    $candidateEvaluationRows |
        Group-Object { Reason-Category $_.Reason }, Reason |
        Sort-Object Count -Descending |
        Select-Object Count, Name |
        Format-Table -AutoSize

    if ($candidateEvaluationRows[0].PSObject.Properties.Name -contains 'PullbackCountInRegime') {
        Write-Host ""
        Write-Host "=== Candidate Pullback Episodes ==="
        $candidateEvaluationRows |
            Where-Object { $_.PullbackEpisodeID } |
            Group-Object PullbackCountInRegime, Result, Reason |
            Sort-Object Name |
            Select-Object Count, Name |
            Format-Table -AutoSize
    }
}

if ($confirmationEvaluationRows.Count -gt 0) {
    Write-Host ""
    Write-Host "=== Confirmation Evaluation Funnel ==="
    $confirmationEvaluationRows |
        Group-Object Stage, Result |
        Sort-Object Count -Descending |
        Select-Object Count, Name |
        Format-Table -AutoSize

    Write-Host ""
    Write-Host "=== Confirmation Evaluation Reasons ==="
    $confirmationEvaluationRows |
        Group-Object { Reason-Category $_.Reason }, Reason |
        Sort-Object Count -Descending |
        Select-Object Count, Name |
        Format-Table -AutoSize
}

if ($riskEvaluationRows.Count -gt 0) {
    Write-Host ""
    Write-Host "=== Risk / Estimated RR Evaluation ==="
    $riskEvaluationRows |
        Group-Object ResearchPath |
        Sort-Object Name |
        ForEach-Object {
            $g = @($_.Group)
            [pscustomobject]@{
                ResearchPath = $_.Name
                N = $g.Count
                RiskPassed = Count-True $g 'RiskPassed'
                RRPassed = Count-True $g 'EstimatedRRPassed'
                RR_GE_1_0 = Count-True $g 'RR_GE_1_0'
                RR_GE_1_2 = Count-True $g 'RR_GE_1_2'
                RR_GE_1_5 = Count-True $g 'RR_GE_1_5'
                BothPassed = @($g | Where-Object { (Is-True $_.RiskPassed) -and (Is-True $_.EstimatedRRPassed) }).Count
                AvgRiskPts = [math]::Round((($g | ForEach-Object { To-Number $_.InitialRiskPoints }) | Measure-Object -Average).Average, 2)
                AvgEstimatedRR = [math]::Round((($g | ForEach-Object { To-Number $_.EstimatedRR }) | Measure-Object -Average).Average, 2)
            }
        } |
        Format-Table -AutoSize

    Write-Host ""
    Write-Host "=== Risk / RR Skip Reasons If Applied ==="
    $riskEvaluationRows |
        ForEach-Object { ([string]$_.SkipReasonsIfApplied).Split('|', [System.StringSplitOptions]::RemoveEmptyEntries) } |
        Group-Object |
        Sort-Object Count -Descending |
        Select-Object Count, Name |
        Format-Table -AutoSize

    if ($riskEvaluationRows[0].PSObject.Properties.Name -contains 'RewardModel') {
        Write-Host ""
        Write-Host "=== Risk / RR By Reward Model ==="
        $riskEvaluationRows |
            Group-Object RewardModel |
            ForEach-Object {
                $g = @($_.Group)
                [pscustomobject]@{
                    RewardModel = $_.Name
                    N = $g.Count
                    RiskPassed = Count-True $g 'RiskPassed'
                    RRPassed = Count-True $g 'EstimatedRRPassed'
                    BothPassed = @($g | Where-Object { (Is-True $_.RiskPassed) -and (Is-True $_.EstimatedRRPassed) }).Count
                    AvgReward = [math]::Round((($g | ForEach-Object { To-Number $_.EstimatedRewardPoints }) | Measure-Object -Average).Average, 2)
                    AvgRR = [math]::Round((($g | ForEach-Object { To-Number $_.EstimatedRR }) | Measure-Object -Average).Average, 2)
                }
            } |
            Sort-Object RewardModel |
            Format-Table -AutoSize
    }
}

if ($scoreBreakdownRows.Count -gt 0) {
    Write-Host ""
    Write-Host "=== Trend Score Component Diagnostics ==="
    $scoreBreakdownRows |
        Where-Object { (Field $_ 'ScoreName') -in @('BullTrendScore', 'BearTrendScore') } |
        Group-Object ScoreName, Component |
        ForEach-Object {
            $g = @($_.Group)
            [pscustomobject]@{
                Name = $_.Name
                N = $g.Count
                Passed = Count-True $g 'ComponentPassed'
                PassPct = [math]::Round(100.0 * (Count-True $g 'ComponentPassed') / [math]::Max(1, $g.Count), 2)
                AvgContribution = [math]::Round((($g | ForEach-Object { To-Number $_.Contribution }) | Measure-Object -Average).Average, 2)
            }
        } |
        Sort-Object Name |
        Format-Table -AutoSize
}

if ($executionTradeRows.Count -gt 0) {
    Write-Host ""
    Write-Host "=== Replay Execution Trades ==="
    $points = @($executionTradeRows | ForEach-Object { To-Number $_.Points })
    $dollars = @($executionTradeRows | ForEach-Object { To-Number $_.Dollars })
    $normalExecutionTrades = @($executionTradeRows | Where-Object { -not (Is-True (Field $_ 'IsAbnormalExecution')) })
    [pscustomobject]@{
        Trades = $executionTradeRows.Count
        Normal = $normalExecutionTrades.Count
        Abnormal = @($executionTradeRows | Where-Object { Is-True (Field $_ 'IsAbnormalExecution') }).Count
        Long = Count-Equals $executionTradeRows 'Side' 'Long'
        Short = Count-Equals $executionTradeRows 'Side' 'Short'
        TP = Count-Equals $executionTradeRows 'ExitRole' 'TP'
        SL = Count-Equals $executionTradeRows 'ExitRole' 'SL'
        TotalPoints = [math]::Round(($points | Measure-Object -Sum).Sum, 2)
        TotalDollars = [math]::Round(($dollars | Measure-Object -Sum).Sum, 2)
        AvgDollars = [math]::Round(($dollars | Measure-Object -Average).Average, 2)
        TotalR = [math]::Round((($executionTradeRows | ForEach-Object { To-Number (Field $_ 'PointsR') }) | Measure-Object -Sum).Sum, 2)
        AvgR = [math]::Round((($executionTradeRows | ForEach-Object { To-Number (Field $_ 'PointsR') }) | Measure-Object -Average).Average, 2)
    } | Format-List

    Write-Host "=== Replay Execution By Path ==="
    $executionTradeRows |
        Group-Object ResearchPath |
        ForEach-Object {
            $g = @($_.Group)
            [pscustomobject]@{
                ResearchPath = $_.Name
                Trades = $g.Count
                Normal = @($g | Where-Object { -not (Is-True (Field $_ 'IsAbnormalExecution')) }).Count
                Abnormal = @($g | Where-Object { Is-True (Field $_ 'IsAbnormalExecution') }).Count
                Long = Count-Equals $g 'Side' 'Long'
                Short = Count-Equals $g 'Side' 'Short'
                TP = Count-Equals $g 'ExitRole' 'TP'
                SL = Count-Equals $g 'ExitRole' 'SL'
                Dollars = [math]::Round((($g | ForEach-Object { To-Number $_.Dollars }) | Measure-Object -Sum).Sum, 2)
                R = [math]::Round((($g | ForEach-Object { To-Number (Field $_ 'PointsR') }) | Measure-Object -Sum).Sum, 2)
            }
        } |
        Sort-Object ResearchPath |
        Format-Table -AutoSize

    Write-Host ""
    Write-Host "=== Replay Execution By Date ==="
    $executionTradeRows |
        Group-Object { ([datetime]$_.EntryTime).ToString('yyyy-MM-dd') } |
        ForEach-Object {
            $g = @($_.Group)
            $normal = @($g | Where-Object { -not (Is-True (Field $_ 'IsAbnormalExecution')) })
            [pscustomobject]@{
                Date = $_.Name
                Trades = $g.Count
                Normal = $normal.Count
                Abnormal = @($g | Where-Object { Is-True (Field $_ 'IsAbnormalExecution') }).Count
                Long = Count-Equals $g 'Side' 'Long'
                Short = Count-Equals $g 'Side' 'Short'
                TP = Count-Equals $g 'ExitRole' 'TP'
                SL = Count-Equals $g 'ExitRole' 'SL'
                Other = @($g | Where-Object { $_.ExitRole -ne 'TP' -and $_.ExitRole -ne 'SL' }).Count
                NetDollars = [math]::Round((($normal | ForEach-Object { To-Number $_.Dollars }) | Measure-Object -Sum).Sum, 2)
                NetR = [math]::Round((($normal | ForEach-Object { To-Number (Field $_ 'PointsR') }) | Measure-Object -Sum).Sum, 2)
                LastDailyPnl = Field $g[-1] 'DailyPnlAfterDollars'
            }
        } |
        Sort-Object Date |
        Format-Table -AutoSize

    $abnormalTrades = @($executionTradeRows | Where-Object { Is-True (Field $_ 'IsAbnormalExecution') })
    if ($abnormalTrades.Count -gt 0) {
        Write-Host ""
        Write-Host "=== Replay Abnormal Executions By Date ==="
        $abnormalTrades |
            Group-Object { ([datetime]$_.EntryTime).ToString('yyyy-MM-dd') } |
            ForEach-Object {
                [pscustomobject]@{
                    Date = $_.Name
                    Count = $_.Count
                    Paths = (@($_.Group) |
                        Group-Object ResearchPath |
                        Sort-Object Count -Descending |
                        ForEach-Object { "$($_.Name):$($_.Count)" }) -join ','
                }
            } |
            Sort-Object Date |
            Format-Table -AutoSize

        Write-Host ""
        Write-Host "=== Replay Abnormal Execution Reasons ==="
        $abnormalTrades |
            Group-Object AbnormalReason |
            Sort-Object Count -Descending |
            Select-Object Count, Name |
            Format-Table -AutoSize
    }
}

if ($liveAccountPnlRows.Count -gt 0) {
    Write-Host ""
    Write-Host "=== Live Account PnL (Actual Fills, After Commission) ==="
    [pscustomobject]@{
        Trades = $liveAccountPnlRows.Count
        Normal = @($liveAccountPnlRows | Where-Object { (Field $_ 'Classification') -eq 'Normal' }).Count
        Quarantine = @($liveAccountPnlRows | Where-Object { (Field $_ 'Classification') -eq 'Quarantine' }).Count
        GrossDollars = [math]::Round((($liveAccountPnlRows | ForEach-Object { To-Number (Field $_ 'GrossPnLDollars') }) | Measure-Object -Sum).Sum, 2)
        RawGrossDollars = [math]::Round((($liveAccountPnlRows | ForEach-Object { To-Number (Field $_ 'RawGrossPnLDollars') }) | Measure-Object -Sum).Sum, 2)
        CommissionDollars = [math]::Round((($liveAccountPnlRows | ForEach-Object { To-Number (Field $_ 'CommissionDollars') }) | Measure-Object -Sum).Sum, 2)
        NetDollars = [math]::Round((($liveAccountPnlRows | ForEach-Object { To-Number (Field $_ 'NetPnLDollars') }) | Measure-Object -Sum).Sum, 2)
    } | Format-List
}

if ($exitPolicyRows.Count -gt 0) {
    Write-Host ""
    Write-Host "=== Exit Policy Evaluation ==="
    $exitPolicyRows |
        Group-Object ResearchPath, ExitPolicy |
        ForEach-Object {
            $g = @($_.Group)
            [pscustomobject]@{
                Name = $_.Name
                N = $g.Count
                Target = Count-Equals $g 'ExitReason' 'Target'
                Stop = Count-Equals $g 'ExitReason' 'Stop'
                TimeStop = Count-Equals $g 'ExitReason' 'TimeStop'
                Ambiguous = Count-True $g 'AmbiguousStopAndTargetSameBar'
                TotalR = [math]::Round((($g | ForEach-Object { To-Number $_.PnL_R }) | Measure-Object -Sum).Sum, 2)
                AvgR = [math]::Round((($g | ForEach-Object { To-Number $_.PnL_R }) | Measure-Object -Average).Average, 2)
                TotalDollars = [math]::Round((($g | ForEach-Object { To-Number $_.PnLDollars }) | Measure-Object -Sum).Sum, 2)
            }
        } |
        Sort-Object Name |
        Format-Table -AutoSize

    Write-Host ""
    Write-Host "=== Exit Policy By Date ==="
    $exitPolicyRows |
        Group-Object { ([datetime]$_.EntryTime).ToString('yyyy-MM-dd') }, ExitPolicy |
        ForEach-Object {
            $g = @($_.Group)
            [pscustomobject]@{
                Name = $_.Name
                N = $g.Count
                Target = Count-Equals $g 'ExitReason' 'Target'
                Stop = Count-Equals $g 'ExitReason' 'Stop'
                TimeStop = Count-Equals $g 'ExitReason' 'TimeStop'
                TotalR = [math]::Round((($g | ForEach-Object { To-Number $_.PnL_R }) | Measure-Object -Sum).Sum, 2)
                AvgR = [math]::Round((($g | ForEach-Object { To-Number $_.PnL_R }) | Measure-Object -Average).Average, 2)
            }
        } |
        Sort-Object Name |
        Format-Table -AutoSize
}

if ($executionEventRows.Count -gt 0) {
    Write-Host ""
    Write-Host "=== Replay Execution Events ==="
    $executionEventRows |
        Group-Object Event |
        Sort-Object Count -Descending |
        Select-Object Count, Name |
        Format-Table -AutoSize

    $qualityEvents = @($executionEventRows | Where-Object {
        $_.Event -match 'STALE|REJECT|CANCEL_FAIL|FAILED|ENTRY_FILLED_RISK_EXCEEDED|EMERGENCY'
    })
    if ($qualityEvents.Count -gt 0) {
        Write-Host ""
        Write-Host "=== Replay Execution Quality Events By Date ==="
        $qualityEvents |
            Group-Object { ([datetime]$_.Time).ToString('yyyy-MM-dd') }, Event |
            Sort-Object Name |
            Select-Object Count, Name |
            Format-Table -AutoSize

        Write-Host ""
        Write-Host "=== Replay Execution Quality Events By Path ==="
        $qualityEvents |
            Group-Object ResearchPath, Event |
            Sort-Object Count -Descending |
            Select-Object Count, Name |
            Format-Table -AutoSize
    }
}

if ($executionDecisionRows.Count -gt 0) {
    Write-Host ""
    Write-Host "=== Actual Execution Decisions ==="
    $executionDecisionRows |
        Group-Object Decision, ResearchPath |
        Sort-Object Count -Descending |
        Select-Object Count, Name |
        Format-Table -AutoSize

    Write-Host ""
    Write-Host "=== Actual Execution Decision Reasons ==="
    $executionDecisionRows |
        Group-Object Decision, Reason |
        Sort-Object Count -Descending |
        Select-Object Count, Name |
        Format-Table -AutoSize

    if ($executionDecisionRows[0].PSObject.Properties.Name -contains 'SetupQualityScore') {
        Write-Host ""
        Write-Host "=== Actual Strategy Quality Buckets ==="
        $qualityBucketRows = @($executionDecisionRows | ForEach-Object {
            [pscustomobject]@{
                Decision = $_.Decision
                Bucket = Score-Bucket (Field $_ 'SetupQualityScore')
            }
        })
        $qualityBucketRows |
            Group-Object Decision, Bucket |
            Sort-Object Name |
            Select-Object Count, Name |
            Format-Table -AutoSize

        Write-Host ""
        Write-Host "=== Actual Strategy Eligibility ==="
        $executionDecisionRows |
            Group-Object StrategyEligible, Decision |
            Sort-Object Count -Descending |
            Select-Object Count, Name |
            Format-Table -AutoSize

        Write-Host ""
        Write-Host "=== Eligible But Path Disabled ==="
        $executionDecisionRows |
            Where-Object { (Field $_ 'StrategyEligible') -eq 'True' -and (Field $_ 'Reason') -eq 'PathDisabled' } |
            Group-Object ResearchPath |
            Sort-Object Count -Descending |
            Select-Object Count, Name |
            Format-Table -AutoSize
    }

    Write-Host ""
    Write-Host "=== Actual Execution Lifecycle Audit ==="
    $executed = @($executionDecisionRows | Where-Object { $_.Decision -eq 'Execute' })
    $quarantinedEntryTradeIds = @($executionEventRows |
        Where-Object { (Field $_ 'Event') -eq 'ENTRY_FILL_QUARANTINE_COMPLETE_V174' } |
        ForEach-Object { Field $_ 'TradeID' } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Select-Object -Unique)
    $quarantinedProtectiveTradeIds = @($executionEventRows |
        Where-Object { (Field $_ 'Event') -in @('PROTECTIVE_FILL_QUARANTINED_V177', 'PROTECTION_SETUP_QUARANTINED_V178') } |
        ForEach-Object { Field $_ 'TradeID' } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Select-Object -Unique)
    $abortedEntryTradeIds = @($executionEventRows |
        Where-Object { (Field $_ 'Event') -eq 'ENTRY_SUBMISSION_ABORTED_V178' } |
        ForEach-Object { Field $_ 'TradeID' } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Select-Object -Unique)
    $abnormalSafetyFlattenTradeIds = @($executionEventRows |
        Where-Object { (Field $_ 'Event') -eq 'ABNORMAL_SAFETY_FLATTEN_COMPLETE_V182' } |
        ForEach-Object { Field $_ 'TradeID' } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Select-Object -Unique)
    $historicalAdapterTradeIds = @($executionEventRows |
        Where-Object { (Field $_ 'Event') -eq 'HISTORICAL_ADAPTER_ENTRY_V179' } |
        ForEach-Object { Field $_ 'TradeID' } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Select-Object -Unique)
    $historicalNormalizedEntryTradeIds = @($executionEventRows |
        Where-Object { (Field $_ 'Event') -eq 'HISTORICAL_FAVORABLE_ENTRY_NORMALIZED_V180' } |
        ForEach-Object { Field $_ 'TradeID' } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Select-Object -Unique)
    $historicalDormantTargetTradeIds = @($executionEventRows |
        Where-Object { (Field $_ 'Event') -eq 'HISTORICAL_DORMANT_TP_SENT_V181' } |
        ForEach-Object { Field $_ 'TradeID' } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Select-Object -Unique)
    $normalExecutionTradeRows = @($executionTradeRows | Where-Object { -not (Is-True (Field $_ 'IsAbnormalExecution')) })
    $actualVerifiedRows = @($rows | Where-Object { Is-True (Field $_ 'ActualVerified') })
    $actualVerifiedTradeIds = @($actualVerifiedRows | ForEach-Object { Field $_ 'ActualTradeID' } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique)
    [pscustomobject]@{
        ExecutedDecisions = $executed.Count
        ExecutionTrades = $executionTradeRows.Count
        NormalExecutionTrades = $normalExecutionTradeRows.Count
        QuarantinedEntryFills = $quarantinedEntryTradeIds.Count
        QuarantinedProtectiveFills = $quarantinedProtectiveTradeIds.Count
        AbortedEntrySubmissions = $abortedEntryTradeIds.Count
        AbnormalSafetyFlattens = $abnormalSafetyFlattenTradeIds.Count
        HistoricalAdapterTrades = $historicalAdapterTradeIds.Count
        HistoricalNormalizedFavorableEntries = $historicalNormalizedEntryTradeIds.Count
        HistoricalDormantTargetTrades = $historicalDormantTargetTradeIds.Count
        HistoricalDormantTargetActivationFailures = @($executionEventRows | Where-Object { (Field $_ 'Event') -eq 'HISTORICAL_DORMANT_TP_ACTIVATE_FAILED_V181' }).Count
        ActualVerifiedOutcomeRows = $actualVerifiedRows.Count
        ActualVerifiedUniqueTrades = $actualVerifiedTradeIds.Count
        ExecutedMissingTrade = @($executed | Where-Object {
            $id = Field $_ 'TradeID'
            [string]::IsNullOrWhiteSpace($id) -or
                (-not ($executionTradeRows | Where-Object { $_.TradeID -eq $id }) -and
                    -not ($quarantinedEntryTradeIds -contains $id) -and
                    -not ($abortedEntryTradeIds -contains $id) -and
                    -not ($abnormalSafetyFlattenTradeIds -contains $id))
        }).Count
        TradesMissingExecuteDecision = @($executionTradeRows | Where-Object {
            $id = $_.TradeID
            [string]::IsNullOrWhiteSpace($id) -or -not ($executed | Where-Object { (Field $_ 'TradeID') -eq $id })
        }).Count
        TradesMissingActualVerifiedOutcome = @($normalExecutionTradeRows | Where-Object {
            $id = $_.TradeID
            [string]::IsNullOrWhiteSpace($id) -or -not ($actualVerifiedTradeIds -contains $id)
        }).Count
        DuplicateActualVerifiedRows = $actualVerifiedRows.Count - $actualVerifiedTradeIds.Count
    } | Format-List
}

Write-Host ""
Write-Host "=== Data Quality ==="
[pscustomobject]@{
    OutcomeRows = $rows.Count
    SignalRows = $signalRows.Count
    NoTradeRows = $noTradeRows.Count
    CandidateEvaluationRows = $candidateEvaluationRows.Count
    ConfirmationEvaluationRows = $confirmationEvaluationRows.Count
    RiskEvaluationRows = $riskEvaluationRows.Count
    ScoreBreakdownRows = $scoreBreakdownRows.Count
    ExecutionEventRows = $executionEventRows.Count
    ExecutionDecisionRows = $executionDecisionRows.Count
    ExecutionTradeRows = $executionTradeRows.Count
    LiveAccountPnlRows = $liveAccountPnlRows.Count
    ExitPolicyRows = $exitPolicyRows.Count
    ActualVerifiedOutcomeRows = Count-True $rows 'ActualVerified'
    IntraBarAmbiguousRows = Count-True $rows 'IntraBarAmbiguous'
    RegimeDailyRows = $regimeDailyRows.Count
    OutcomesMissingSignalMetadata = ($enrichedRows | Where-Object { -not $_.HasSignalMetadata }).Count
    OutcomesMissingRegimeDaily = ($enrichedRows | Where-Object { -not $_.HasRegimeDaily }).Count
} | Format-Table -AutoSize

if ($regimeDailyRows.Count -gt 0) {
    Write-Host ""
    Write-Host "=== Regime Daily Summary ==="
    $regimeDailyRows |
        Sort-Object Date, SnapshotID |
        Select-Object Date, SnapshotID, TotalBars, BullTrendPct, BearTrendPct, UnknownPct, RegimeChangeCount, AvgBullScore, AvgBearScore |
        Format-Table -AutoSize
}

if ($regimeRows.Count -gt 0) {
    Write-Host ""
    Write-Host "=== Regime Changes By Snapshot ==="
    $regimeRows |
        Group-Object SnapshotID, Regime |
        Sort-Object Name |
        Select-Object Count, Name |
        Format-Table -AutoSize

    Write-Host ""
    Write-Host "=== Regime Changes By Date ==="
    $regimeRows |
        Group-Object { ([datetime]$_.Time).ToString('yyyy-MM-dd') }, Regime |
        Sort-Object Name |
        Select-Object Count, Name |
        Format-Table -AutoSize
}
