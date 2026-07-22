param(
    [Parameter(Mandatory = $true)]
    [string]$EvidenceDirectory,

    [switch]$UseV189ShortWideStopRiskBand,

    [ValidateSet('Observed', 'Conservative')]
    [string]$ShadowTargetMode = 'Observed',

    [switch]$UseProtectBE075Trigger,

    [string]$ProtectBE075Paths = '',

    [string]$OutputCsv,

    [string]$TraceCsv
)

# Phase A calibration scaffold. Baseline mode must reproduce archived Actual trades exactly.
# Counterfactual output is not strategy evidence until it also passes the documented v1.87 -> v1.89 net-change calibration.

$ErrorActionPreference = 'Stop'

$protectBE075PathSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($path in @($ProtectBE075Paths -split '\|' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })) {
    [void]$protectBE075PathSet.Add($path.Trim())
}

if (-not (Test-Path -LiteralPath $EvidenceDirectory)) {
    throw "Evidence directory not found: $EvidenceDirectory"
}

function Read-OpfCsv([string]$Pattern) {
    return @(Get-ChildItem -LiteralPath $EvidenceDirectory -Recurse -File -Filter $Pattern |
        ForEach-Object { Import-Csv -LiteralPath $_.FullName })
}

function Decimal-Value($Value) {
    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) {
        return [decimal]0
    }

    return [decimal]::Parse([string]$Value, [Globalization.CultureInfo]::InvariantCulture)
}

function DateTime-Value($Value) {
    return [datetime]::Parse([string]$Value, [Globalization.CultureInfo]::InvariantCulture)
}

function Candidate-Key($Row) {
    return '{0}|{1}|{2}|{3}|{4}' -f $Row.SnapshotID, $Row.SignalID, $Row.ResearchPath, $Row.Time, $Row.Bar
}

function Policy-Key($Row, [string]$Policy) {
    return '{0}|{1}|{2}|{3}|{4}' -f $Row.SnapshotID, $Row.SignalID, $Row.ResearchPath, $Row.Bar, $Policy
}

function Is-PortfolioBlockedCandidate($Row) {
    if ($Row.Decision -eq 'Execute') {
        return $true
    }

    return $Row.Reason -match '^(ActiveTrade:|DailyTradeLimit:|LiveDailyLoss:|DailyLoss:|DailyTarget:)'
}

function Is-V189RiskBandAllowed($Row) {
    if (-not $UseV189ShortWideStopRiskBand) {
        return $true
    }

    if ($Row.Side -ne 'Short' -or $Row.ResearchPath -ne 'ObservationConfirm_WideStop1_5R') {
        return $true
    }

    $risk = Decimal-Value $Row.InitialRiskPoints
    return $risk -gt [decimal]8 -and $risk -le [decimal]12
}

$decisions = Read-OpfCsv '*_execution_decisions.csv'
$trades = Read-OpfCsv '*_execution_trades.csv'
$policies = Read-OpfCsv '*_exit_policy_evaluations.csv'
$shadowTrades = Read-OpfCsv '*_shadow_trades.csv'

if ($decisions.Count -eq 0 -or $trades.Count -eq 0 -or $policies.Count -eq 0) {
    throw 'Evidence must contain execution_decisions, execution_trades, and exit_policy_evaluations CSV files.'
}

$tradeById = @{}
foreach ($trade in $trades) {
    if ($trade.IsAbnormalExecution -eq 'True' -or [string]::IsNullOrWhiteSpace($trade.TradeID)) {
        continue
    }

    $tradeById[$trade.TradeID] = $trade
}

$policyByKey = @{}
foreach ($policy in $policies) {
    $key = '{0}|{1}|{2}|{3}|{4}' -f $policy.SnapshotID, $policy.SignalID, $policy.ResearchPath, $policy.EntryBar, $policy.ExitPolicy
    if (-not $policyByKey.ContainsKey($key)) {
        $policyByKey[$key] = $policy
    }
}

$shadowByKey = @{}
foreach ($shadow in $shadowTrades) {
    $key = '{0}|{1}|{2}|{3}' -f $shadow.SnapshotID, $shadow.SignalID, $shadow.ResearchPath, $shadow.EntryBar
    if (-not $shadowByKey.ContainsKey($key)) {
        $shadowByKey[$key] = $shadow
    }
}

$candidateByKey = @{}
foreach ($decision in $decisions) {
    if (-not (Is-PortfolioBlockedCandidate $decision)) {
        continue
    }

    $hasActualTrade = -not [string]::IsNullOrWhiteSpace($decision.TradeID) -and $tradeById.ContainsKey($decision.TradeID)
    if ($decision.Decision -eq 'Execute' -and -not $hasActualTrade) {
        continue
    }

    $key = Candidate-Key $decision
    if (-not $candidateByKey.ContainsKey($key)) {
        $candidateByKey[$key] = $decision
    }
}

$results = [Collections.Generic.List[object]]::new()
$traceRows = [Collections.Generic.List[object]]::new()
$missingOutcomes = 0
$filteredByRiskBand = 0
$blockedByActiveTrade = 0
$blockedByDailyLimit = 0
$blockedByDailyLoss = 0
$conservativeShadowAdjustments = 0
$earlyBreakEvenAdjustments = 0

function Add-TraceRow($Candidate, [bool]$HasActualTrade, [bool]$DivergedBefore, [datetime]$ActiveBefore, [int]$TradesBefore, [decimal]$NetBefore, [bool]$RiskBandAllowed, [string]$Disposition, [string]$OutcomeSource, [string]$ExitReason, $ExitTime, [decimal]$Gross, [decimal]$Net, [bool]$DivergedAfter, [datetime]$ActiveAfter, [int]$TradesAfter, [decimal]$NetAfter) {
    if ([string]::IsNullOrWhiteSpace($TraceCsv)) {
        return
    }

    $traceRows.Add([pscustomobject]@{
        SnapshotID = $Candidate.SnapshotID
        Time = $Candidate.Time
        Bar = $Candidate.Bar
        SignalID = $Candidate.SignalID
        TradeID = $Candidate.TradeID
        Side = $Candidate.Side
        ResearchPath = $Candidate.ResearchPath
        InitialRiskPoints = Decimal-Value $Candidate.InitialRiskPoints
        OriginalDecision = $Candidate.Decision
        OriginalReason = $Candidate.Reason
        HasActualTrade = $HasActualTrade
        PortfolioDivergedBefore = $DivergedBefore
        ActiveUntilBefore = if ($ActiveBefore -eq [datetime]::MinValue) { '' } else { $ActiveBefore.ToString('O') }
        DailyTradesBefore = $TradesBefore
        DailyNetBefore = [math]::Round($NetBefore, 2)
        RiskBandAllowed = $RiskBandAllowed
        Disposition = $Disposition
        OutcomeSource = $OutcomeSource
        ExitReason = $ExitReason
        ExitTime = if ($null -eq $ExitTime) { '' } else { ([datetime]$ExitTime).ToString('O') }
        GrossDollars = [math]::Round($Gross, 2)
        NetDollars = [math]::Round($Net, 2)
        PortfolioDivergedAfter = $DivergedAfter
        ActiveUntilAfter = if ($ActiveAfter -eq [datetime]::MinValue) { '' } else { $ActiveAfter.ToString('O') }
        DailyTradesAfter = $TradesAfter
        DailyNetAfter = [math]::Round($NetAfter, 2)
    })
}

foreach ($snapshotGroup in @($candidateByKey.Values | Group-Object SnapshotID | Sort-Object Name)) {
    $activeUntil = [datetime]::MinValue
    $dailyNet = [decimal]0
    $dailyTrades = 0
    $portfolioDiverged = $false

    foreach ($candidate in @($snapshotGroup.Group | Sort-Object @{ Expression = { DateTime-Value $_.Time } }, @{ Expression = { [int]$_.Bar } })) {
        $hasActualTrade = -not [string]::IsNullOrWhiteSpace($candidate.TradeID) -and $tradeById.ContainsKey($candidate.TradeID)
        $divergedBefore = $portfolioDiverged
        $activeBefore = $activeUntil
        $tradesBefore = $dailyTrades
        $netBefore = $dailyNet
        $riskBandAllowed = Is-V189RiskBandAllowed $candidate
        if (-not $portfolioDiverged -and -not $hasActualTrade) {
            Add-TraceRow $candidate $hasActualTrade $divergedBefore $activeBefore $tradesBefore $netBefore $riskBandAllowed 'IgnoredBeforeDivergence' '' '' $null 0 0 $portfolioDiverged $activeUntil $dailyTrades $dailyNet
            continue
        }

        $entryTime = DateTime-Value $candidate.Time
        if ($activeUntil -ne [datetime]::MinValue -and $entryTime -le $activeUntil) {
            $blockedByActiveTrade++
            Add-TraceRow $candidate $hasActualTrade $divergedBefore $activeBefore $tradesBefore $netBefore $riskBandAllowed 'BlockedByActiveTrade' '' '' $null 0 0 $portfolioDiverged $activeUntil $dailyTrades $dailyNet
            continue
        }
        if ($dailyTrades -ge 15) {
            $blockedByDailyLimit++
            Add-TraceRow $candidate $hasActualTrade $divergedBefore $activeBefore $tradesBefore $netBefore $riskBandAllowed 'BlockedByDailyLimit' '' '' $null 0 0 $portfolioDiverged $activeUntil $dailyTrades $dailyNet
            continue
        }
        if ($dailyNet -le [decimal]-300) {
            $blockedByDailyLoss++
            Add-TraceRow $candidate $hasActualTrade $divergedBefore $activeBefore $tradesBefore $netBefore $riskBandAllowed 'BlockedByDailyLoss' '' '' $null 0 0 $portfolioDiverged $activeUntil $dailyTrades $dailyNet
            continue
        }
        if (-not $riskBandAllowed) {
            $filteredByRiskBand++
            if ($hasActualTrade) {
                $portfolioDiverged = $true
            }
            Add-TraceRow $candidate $hasActualTrade $divergedBefore $activeBefore $tradesBefore $netBefore $riskBandAllowed 'FilteredByRiskBand' '' '' $null 0 0 $portfolioDiverged $activeUntil $dailyTrades $dailyNet
            continue
        }

        $source = 'ShadowPolicy'
        $exitReason = ''
        $gross = [decimal]0
        $exitTime = $entryTime

        if ($hasActualTrade) {
            $trade = $tradeById[$candidate.TradeID]
            $source = 'ActualTrade'
            $exitReason = $trade.ExitRole
            $gross = Decimal-Value $trade.Dollars
            $exitTime = DateTime-Value $trade.ExitTime
        }
        else {
            $shadowKey = '{0}|{1}|{2}|{3}' -f $candidate.SnapshotID, $candidate.SignalID, $candidate.ResearchPath, $candidate.Bar
            if ($shadowByKey.ContainsKey($shadowKey)) {
                $shadow = $shadowByKey[$shadowKey]
                $source = 'ShadowTrade'
                $exitReason = $shadow.ExitReason
                $gross = Decimal-Value $shadow.GrossDollars
                $exitTime = DateTime-Value $shadow.ExitTime
                if ($ShadowTargetMode -eq 'Conservative' -and $shadow.ExitReason -eq 'Target') {
                    if ($shadow.Policy -eq 'ProtectBE1R_Then3R') {
                        $gross = [decimal]0
                        $exitReason = 'TargetOrProtectBE:ConservativeProtectBE'
                        $conservativeShadowAdjustments++
                    }
                    elseif ($shadow.Policy -eq 'ZoneBirthSplit2_5R_4R_BEAfterBase') {
                        $gross = (Decimal-Value $shadow.InitialRiskPoints) * [decimal]5
                        $exitReason = 'BaseTarget|RunnerTargetOrProtectBE:ConservativeBaseOnly'
                        $conservativeShadowAdjustments++
                    }
                }
            }
            else {
                $policyName = if ($candidate.ResearchPath -eq 'ZoneBirthResearch') {
                    'SplitBase_Runner3R_BE1R'
                }
                else {
                    'ProtectBE1R_Then3R'
                }
                $key = Policy-Key $candidate $policyName
                if (-not $policyByKey.ContainsKey($key)) {
                    $missingOutcomes++
                    Add-TraceRow $candidate $hasActualTrade $divergedBefore $activeBefore $tradesBefore $netBefore $riskBandAllowed 'MissingOutcome' '' '' $null 0 0 $portfolioDiverged $activeUntil $dailyTrades $dailyNet
                    continue
                }

                $policy = $policyByKey[$key]
                $exitReason = $policy.ExitReason
                $gross = Decimal-Value $policy.PnLDollars
                $barDelta = [math]::Max(1, [int]$policy.PolicyExitBar - [int]$candidate.Bar)
                $exitTime = $entryTime.AddMinutes(5 * $barDelta)
            }
        }

        if ($UseProtectBE075Trigger -and
            ($protectBE075PathSet.Count -eq 0 -or $protectBE075PathSet.Contains($candidate.ResearchPath))) {
            $earlyBreakEvenKey = Policy-Key $candidate 'ProtectBE0_75R_Then2_5R'
            if ($policyByKey.ContainsKey($earlyBreakEvenKey)) {
                $earlyBreakEven = $policyByKey[$earlyBreakEvenKey]
                if ($earlyBreakEven.ExitReason -eq 'ProtectBE') {
                    $source = 'EarlyBE075Policy'
                    $exitReason = 'ProtectBE0_75R_Then3R'
                    $gross = [decimal]0
                    $barDelta = [math]::Max(1, [int]$earlyBreakEven.PolicyExitBar - [int]$candidate.Bar)
                    $exitTime = $entryTime.AddMinutes(5 * $barDelta)
                    $portfolioDiverged = $true
                    $earlyBreakEvenAdjustments++
                }
            }
        }

        $net = $gross - [decimal]2.4
        $dailyTrades++
        $dailyNet += $net
        $activeUntil = $exitTime
        Add-TraceRow $candidate $hasActualTrade $divergedBefore $activeBefore $tradesBefore $netBefore $riskBandAllowed 'Selected' $source $exitReason $exitTime $gross $net $portfolioDiverged $activeUntil $dailyTrades $dailyNet

        $results.Add([pscustomobject]@{
            SnapshotID = $candidate.SnapshotID
            EntryTime = $entryTime.ToString('O')
            ExitTime = $exitTime.ToString('O')
            SignalID = $candidate.SignalID
            TradeID = $candidate.TradeID
            Side = $candidate.Side
            ResearchPath = $candidate.ResearchPath
            InitialRiskPoints = Decimal-Value $candidate.InitialRiskPoints
            Source = $source
            ExitReason = $exitReason
            GrossDollars = [math]::Round($gross, 2)
            NetDollars = [math]::Round($net, 2)
            DailyTradeCount = $dailyTrades
            DailyNetDollars = [math]::Round($dailyNet, 2)
        })
    }
}

if (-not [string]::IsNullOrWhiteSpace($OutputCsv)) {
    $results | Export-Csv -LiteralPath $OutputCsv -NoTypeInformation -Encoding utf8
}
if (-not [string]::IsNullOrWhiteSpace($TraceCsv)) {
    $traceRows | Export-Csv -LiteralPath $TraceCsv -NoTypeInformation -Encoding utf8
}

$actualGross = ($tradeById.Values | Measure-Object -Property Dollars -Sum).Sum
$actualNet = (Decimal-Value $actualGross) - [decimal]2.4 * $tradeById.Count
$simulatedGross = ($results | Measure-Object -Property GrossDollars -Sum).Sum
$simulatedNet = ($results | Measure-Object -Property NetDollars -Sum).Sum

[pscustomobject]@{
    EvidenceDirectory = (Resolve-Path -LiteralPath $EvidenceDirectory).Path
    Mode = if ($UseV189ShortWideStopRiskBand) { 'V189ShortWideStopRiskBand' } else { 'Baseline' }
    ShadowTargetMode = $ShadowTargetMode
    ProtectBE075Trigger = [bool]$UseProtectBE075Trigger
    ProtectBE075Paths = if ($protectBE075PathSet.Count -eq 0) { '*' } else { $ProtectBE075Paths }
    CandidateRows = $candidateByKey.Count
    ActualTrades = $tradeById.Count
    ActualGrossDollars = [math]::Round((Decimal-Value $actualGross), 2)
    ActualNetDollars = [math]::Round($actualNet, 2)
    SimulatedTrades = $results.Count
    SimulatedGrossDollars = [math]::Round((Decimal-Value $simulatedGross), 2)
    SimulatedNetDollars = [math]::Round((Decimal-Value $simulatedNet), 2)
    ShadowTrades = @($results | Where-Object { $_.Source -in @('ShadowTrade', 'ShadowPolicy') }).Count
    ExactShadowTrades = @($results | Where-Object Source -eq 'ShadowTrade').Count
    FilteredByRiskBand = $filteredByRiskBand
    BlockedByActiveTrade = $blockedByActiveTrade
    BlockedByDailyLimit = $blockedByDailyLimit
    BlockedByDailyLoss = $blockedByDailyLoss
    MissingOutcomes = $missingOutcomes
    ConservativeShadowAdjustments = $conservativeShadowAdjustments
    EarlyBreakEvenAdjustments = $earlyBreakEvenAdjustments
    OutputCsv = $OutputCsv
    TraceRows = $traceRows.Count
    TraceCsv = $TraceCsv
} | Format-List
