param(
    [Parameter(Mandatory = $true)]
    [string]$ObservedCsv,

    [Parameter(Mandatory = $true)]
    [string]$ConservativeCsv,

    [Parameter(Mandatory = $true)]
    [string]$ActualEvidenceDirectory,

    [string]$DetailCsv
)

$ErrorActionPreference = 'Stop'

foreach ($path in @($ObservedCsv, $ConservativeCsv, $ActualEvidenceDirectory)) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Path not found: $path"
    }
}

function Decimal-Value($Value) {
    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) {
        return [decimal]0
    }

    return [decimal]::Parse([string]$Value, [Globalization.CultureInfo]::InvariantCulture)
}

function Trade-Key($Row) {
    return '{0}|{1}|{2}' -f $Row.EntryTime, $Row.SignalID, $Row.ResearchPath
}

function Sum-Property($Rows, [string]$Property) {
    $sum = ($Rows | Measure-Object -Property $Property -Sum).Sum
    return [math]::Round((Decimal-Value $sum), 2)
}

$observed = @(Import-Csv -LiteralPath $ObservedCsv)
$conservative = @(Import-Csv -LiteralPath $ConservativeCsv)
$actual = @(Get-ChildItem -LiteralPath $ActualEvidenceDirectory -Recurse -File -Filter '*_execution_trades.csv' |
    ForEach-Object { Import-Csv -LiteralPath $_.FullName } |
    Where-Object { $_.IsAbnormalExecution -ne 'True' -and -not [string]::IsNullOrWhiteSpace($_.TradeID) } |
    ForEach-Object {
        $_ | Add-Member -NotePropertyName NetDollars -NotePropertyValue ((Decimal-Value $_.Dollars) - [decimal]2.4) -PassThru
    })

$observedByKey = @{}
$conservativeByKey = @{}
$actualByKey = @{}
foreach ($row in $observed) { $observedByKey[(Trade-Key $row)] = $row }
foreach ($row in $conservative) { $conservativeByKey[(Trade-Key $row)] = $row }
foreach ($row in $actual) { $actualByKey[(Trade-Key $row)] = $row }

$details = [Collections.Generic.List[object]]::new()
$allKeys = @($observedByKey.Keys + $conservativeByKey.Keys + $actualByKey.Keys | Sort-Object -Unique)
foreach ($key in $allKeys) {
    $observedRow = $observedByKey[$key]
    $conservativeRow = $conservativeByKey[$key]
    $actualRow = $actualByKey[$key]
    $sample = if ($null -ne $observedRow) { $observedRow } elseif ($null -ne $conservativeRow) { $conservativeRow } else { $actualRow }
    $observedNet = if ($null -eq $observedRow) { $null } else { Decimal-Value $observedRow.NetDollars }
    $conservativeNet = if ($null -eq $conservativeRow) { $null } else { Decimal-Value $conservativeRow.NetDollars }
    $actualNet = if ($null -eq $actualRow) { $null } else { Decimal-Value $actualRow.NetDollars }
    $stable = $null -ne $observedRow -and $null -ne $conservativeRow -and $null -ne $actualRow

    $details.Add([pscustomobject]@{
        EntryTime = $sample.EntryTime
        SignalID = $sample.SignalID
        ResearchPath = $sample.ResearchPath
        InObserved = $null -ne $observedRow
        InConservative = $null -ne $conservativeRow
        InActual = $null -ne $actualRow
        StableMatched = $stable
        ObservedSource = if ($null -eq $observedRow) { '' } else { $observedRow.Source }
        ObservedExit = if ($null -eq $observedRow) { '' } else { $observedRow.ExitReason }
        ActualExit = if ($null -eq $actualRow) { '' } else { $actualRow.ExitRole }
        ConservativeNet = $conservativeNet
        ObservedNet = $observedNet
        ActualNet = $actualNet
        ActualWithinCandidateRange = if (-not $stable) { $null } else {
            $actualNet -ge [math]::Min($conservativeNet, $observedNet) -and
            $actualNet -le [math]::Max($conservativeNet, $observedNet)
        }
    })
}

$observedMatched = @($details | Where-Object { $_.InObserved -and $_.InActual })
$observedOnly = @($details | Where-Object { $_.InObserved -and -not $_.InActual })
$actualOnly = @($details | Where-Object { $_.InActual -and -not $_.InObserved })
$stableMatched = @($details | Where-Object StableMatched)
$actualSourceMatched = @($observedMatched | Where-Object ObservedSource -eq 'ActualTrade')
$shadowSourceMatched = @($observedMatched | Where-Object { $_.ObservedSource -in @('ShadowTrade', 'ShadowPolicy') })

$stableLower = [decimal]0
$stableUpper = [decimal]0
foreach ($row in $stableMatched) {
    $stableLower += [math]::Min((Decimal-Value $row.ConservativeNet), (Decimal-Value $row.ObservedNet))
    $stableUpper += [math]::Max((Decimal-Value $row.ConservativeNet), (Decimal-Value $row.ObservedNet))
}
$stableActual = Sum-Property $stableMatched 'ActualNet'
$stableLower = [math]::Round($stableLower, 2)
$stableUpper = [math]::Round($stableUpper, 2)

$actualSourceExpectedNet = Sum-Property $actualSourceMatched 'ObservedNet'
$actualSourceActualNet = Sum-Property $actualSourceMatched 'ActualNet'
$actualSourceErrorPercent = [math]::Round(
    100 * [math]::Abs($actualSourceActualNet - $actualSourceExpectedNet) /
    [math]::Max(1, [math]::Abs($actualSourceActualNet)),
    2)

[decimal]$shadowLower = 0
[decimal]$shadowUpper = 0
foreach ($row in $shadowSourceMatched) {
    $observedValue = Decimal-Value $row.ObservedNet
    $conservativeValue = if ($null -eq $row.ConservativeNet -or [string]::IsNullOrWhiteSpace([string]$row.ConservativeNet)) {
        $observedValue
    }
    else {
        Decimal-Value $row.ConservativeNet
    }
    $shadowLower += [math]::Min($conservativeValue, $observedValue)
    $shadowUpper += [math]::Max($conservativeValue, $observedValue)
}
$shadowLower = [math]::Round($shadowLower, 2)
$shadowUpper = [math]::Round($shadowUpper, 2)
$shadowActualNet = Sum-Property $shadowSourceMatched 'ActualNet'
$shadowActualWithinRange = $shadowActualNet -ge $shadowLower -and $shadowActualNet -le $shadowUpper

$observedNet = Sum-Property $observed 'NetDollars'
$conservativeNet = Sum-Property $conservative 'NetDollars'
$actualNet = Sum-Property $actual 'NetDollars'
$observedOnlyNet = Sum-Property $observedOnly 'ObservedNet'
$actualOnlyNet = Sum-Property $actualOnly 'ActualNet'
$candidateDriftNet = [math]::Round($actualOnlyNet - $observedOnlyNet, 2)
$countErrorPercent = [math]::Round(100 * [math]::Abs($observed.Count - $actual.Count) / [math]::Max(1, $actual.Count), 2)
$observedMatchRate = [math]::Round(100 * $observedMatched.Count / [math]::Max(1, $observed.Count), 2)
$stableActualWithinRange = $stableActual -ge $stableLower -and $stableActual -le $stableUpper
$totalActualWithinRange = $actualNet -ge [math]::Min($conservativeNet, $observedNet) -and $actualNet -le [math]::Max($conservativeNet, $observedNet)

if (-not [string]::IsNullOrWhiteSpace($DetailCsv)) {
    $details | Sort-Object EntryTime, SignalID, ResearchPath | Export-Csv -LiteralPath $DetailCsv -NoTypeInformation -Encoding utf8
}

[pscustomobject]@{
    ObservedTrades = $observed.Count
    ConservativeTrades = $conservative.Count
    ActualTrades = $actual.Count
    CountErrorPercent = $countErrorPercent
    ObservedNetDollars = $observedNet
    ConservativeNetDollars = $conservativeNet
    ActualNetDollars = $actualNet
    TotalActualWithinModeRange = $totalActualWithinRange
    ObservedMatchedTrades = $observedMatched.Count
    ObservedMatchRatePercent = $observedMatchRate
    StableMatchedTrades = $stableMatched.Count
    StableLowerNetDollars = $stableLower
    StableUpperNetDollars = $stableUpper
    StableActualNetDollars = $stableActual
    StableActualWithinRange = $stableActualWithinRange
    MatchedActualSourceTrades = $actualSourceMatched.Count
    MatchedActualSourceExpectedNet = $actualSourceExpectedNet
    MatchedActualSourceActualNet = $actualSourceActualNet
    MatchedActualSourceErrorPercent = $actualSourceErrorPercent
    MatchedShadowTrades = $shadowSourceMatched.Count
    MatchedShadowLowerNet = $shadowLower
    MatchedShadowUpperNet = $shadowUpper
    MatchedShadowActualNet = $shadowActualNet
    MatchedShadowActualWithinRange = $shadowActualWithinRange
    ObservedOnlyTrades = $observedOnly.Count
    ObservedOnlyNetDollars = $observedOnlyNet
    ActualOnlyTrades = $actualOnly.Count
    ActualOnlyNetDollars = $actualOnlyNet
    CandidateDriftNetDollars = $candidateDriftNet
    DirectionVolumePassed = $countErrorPercent -le 5
    ConditionalOutcomePassed = $observedMatchRate -ge 80 -and $actualSourceErrorPercent -le 1 -and $shadowActualWithinRange
    ExactProfitPassed = $totalActualWithinRange -and [math]::Abs($candidateDriftNet) -le [math]::Max(1, [math]::Abs($actualNet) * 0.1)
    DetailCsv = $DetailCsv
} | Format-List
