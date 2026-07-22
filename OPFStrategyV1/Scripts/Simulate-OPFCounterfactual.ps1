param(
    [Parameter(Mandatory = $true)]
    [string]$EvidenceDirectory,

    [switch]$UseV189ShortWideStopRiskBand,

    [string]$OutputCsv
)

# Phase A calibration scaffold. Baseline mode must reproduce archived Actual trades exactly.
# Counterfactual output is not strategy evidence until it also passes the documented v1.87 -> v1.89 net-change calibration.

$ErrorActionPreference = 'Stop'

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
$missingOutcomes = 0
$filteredByRiskBand = 0
$blockedByActiveTrade = 0
$blockedByDailyLimit = 0
$blockedByDailyLoss = 0

foreach ($snapshotGroup in @($candidateByKey.Values | Group-Object SnapshotID | Sort-Object Name)) {
    $activeUntil = [datetime]::MinValue
    $dailyNet = [decimal]0
    $dailyTrades = 0
    $portfolioDiverged = $false

    foreach ($candidate in @($snapshotGroup.Group | Sort-Object @{ Expression = { DateTime-Value $_.Time } }, @{ Expression = { [int]$_.Bar } })) {
        $hasActualTrade = -not [string]::IsNullOrWhiteSpace($candidate.TradeID) -and $tradeById.ContainsKey($candidate.TradeID)
        if (-not $portfolioDiverged -and -not $hasActualTrade) {
            continue
        }

        $entryTime = DateTime-Value $candidate.Time
        if ($activeUntil -ne [datetime]::MinValue -and $entryTime -le $activeUntil) {
            $blockedByActiveTrade++
            continue
        }
        if ($dailyTrades -ge 15) {
            $blockedByDailyLimit++
            continue
        }
        if ($dailyNet -le [decimal]-300) {
            $blockedByDailyLoss++
            continue
        }
        if (-not (Is-V189RiskBandAllowed $candidate)) {
            $filteredByRiskBand++
            if ($hasActualTrade) {
                $portfolioDiverged = $true
            }
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
            $policyName = if ($candidate.ResearchPath -eq 'ZoneBirthResearch') {
                'SplitBase_Runner3R_BE1R'
            }
            else {
                'ProtectBE1R_Then3R'
            }
            $key = Policy-Key $candidate $policyName
            if (-not $policyByKey.ContainsKey($key)) {
                $missingOutcomes++
                continue
            }

            $policy = $policyByKey[$key]
            $exitReason = $policy.ExitReason
            $gross = Decimal-Value $policy.PnLDollars
            $barDelta = [math]::Max(1, [int]$policy.PolicyExitBar - [int]$candidate.Bar)
            $exitTime = $entryTime.AddMinutes(5 * $barDelta)
        }

        $net = $gross - [decimal]2.4
        $dailyTrades++
        $dailyNet += $net
        $activeUntil = $exitTime

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

$actualGross = ($tradeById.Values | Measure-Object -Property Dollars -Sum).Sum
$actualNet = (Decimal-Value $actualGross) - [decimal]2.4 * $tradeById.Count
$simulatedGross = ($results | Measure-Object -Property GrossDollars -Sum).Sum
$simulatedNet = ($results | Measure-Object -Property NetDollars -Sum).Sum

[pscustomobject]@{
    EvidenceDirectory = (Resolve-Path -LiteralPath $EvidenceDirectory).Path
    Mode = if ($UseV189ShortWideStopRiskBand) { 'V189ShortWideStopRiskBand' } else { 'Baseline' }
    CandidateRows = $candidateByKey.Count
    ActualTrades = $tradeById.Count
    ActualGrossDollars = [math]::Round((Decimal-Value $actualGross), 2)
    ActualNetDollars = [math]::Round($actualNet, 2)
    SimulatedTrades = $results.Count
    SimulatedGrossDollars = [math]::Round((Decimal-Value $simulatedGross), 2)
    SimulatedNetDollars = [math]::Round((Decimal-Value $simulatedNet), 2)
    ShadowTrades = @($results | Where-Object Source -eq 'ShadowPolicy').Count
    FilteredByRiskBand = $filteredByRiskBand
    BlockedByActiveTrade = $blockedByActiveTrade
    BlockedByDailyLimit = $blockedByDailyLimit
    BlockedByDailyLoss = $blockedByDailyLoss
    MissingOutcomes = $missingOutcomes
    OutputCsv = $OutputCsv
} | Format-List
