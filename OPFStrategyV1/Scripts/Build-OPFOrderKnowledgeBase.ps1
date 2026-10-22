[CmdletBinding()]
param(
    [string]$LogDirectory = (Join-Path $env:APPDATA 'ATAS/StrategyLogs/OPFStrategyV1'),
    [string]$OutputDirectory = (Join-Path $env:APPDATA 'ATAS/OrderKnowledgeBase')
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

$tradeRows = @()
Get-ChildItem -LiteralPath $LogDirectory -File -Filter '*_execution_trades.csv' |
    ForEach-Object {
        try { $tradeRows += Import-Csv -LiteralPath $_.FullName } catch { }
    }

$eventRows = @()
Get-ChildItem -LiteralPath $LogDirectory -File -Filter '*_execution_events.csv' |
    ForEach-Object {
        try { $eventRows += Import-Csv -LiteralPath $_.FullName } catch { }
    }

$decisionRows = @()
Get-ChildItem -LiteralPath $LogDirectory -File -Filter '*_execution_decisions.csv' |
    ForEach-Object {
        try { $decisionRows += Import-Csv -LiteralPath $_.FullName } catch { }
    }

$trades = @($tradeRows |
    Where-Object { $_.TradeID -and $_.ResearchPath -ne 'StartupTestOrder' } |
    Group-Object TradeID |
    ForEach-Object { $_.Group | Select-Object -First 1 })

$ledger = foreach ($trade in $trades) {
    $events = @($eventRows | Where-Object { $_.TradeID -eq $trade.TradeID })
    $eventNames = @($events | Select-Object -ExpandProperty Event -Unique)
    $decisions = @($decisionRows | Where-Object { $_.TradeID -eq $trade.TradeID })
    $decisionReasons = @($decisions | Select-Object -ExpandProperty Reason -Unique | Where-Object { $_ })
    $entryEvents = @($events | Where-Object { $_.Event -in @('ENTRY_SEND','MYTRADE') -and $_.Role -eq 'ENTRY' })
    $exitEvents = @($events | Where-Object { $_.Event -in @('EXIT_FILLED','MYTRADE') -and $_.Role -in @('SL','TP','EXIT') })
    $protectEvents = @($events | Where-Object { $_.Event -like 'PROTECT_*' })
    $cleanupEvents = @($events | Where-Object { $_.Event -like 'PROTECTION_CLEANUP*' -or $_.Event -eq 'CANCEL_SENT' })
    $gross = [decimal]$trade.Dollars
    $mfeR = [decimal]$trade.ActualMFE_R
    $maeR = [decimal]$trade.ActualMAE_R
    $risk = [decimal]$trade.RiskDollars
    $absGross = [math]::Abs($gross)

    if ($trade.IsAbnormalExecution -eq 'True') {
        $outcomeClass = 'ABNORMAL_QUARANTINE'
        $inference = 'The execution is marked abnormal and is excluded from normal path performance.'
    } elseif ($gross -gt 0) {
        $outcomeClass = 'PROFIT'
        $inference = if ($mfeR -ge 1.4) { 'Fact: profitable exit after reaching near target.' } else { 'Fact: profitable exit below 1.4R; inspect exit events to distinguish target and protection.' }
    } elseif ($gross -eq 0) {
        $outcomeClass = 'FLAT'
        $inference = 'Fact: zero gross PnL; distinguish protective exit, manual close, or other exit.'
    } elseif ($risk -gt 0 -and $absGross -lt $risk * 0.35 -and $mfeR -ge 0.7) {
        $outcomeClass = 'BE_PROTECTED_SMALL_LOSS'
        $inference = 'Hypothesis: substantial MFE followed by a small loss; verify stop amendments and broker fills.'
    } elseif ($risk -gt 0 -and $absGross -ge $risk * 0.75) {
        $outcomeClass = 'INITIAL_RISK_LOSS'
        $inference = 'Outcome pattern: loss is near planned risk; trade ledger does not prove the market or gate cause.'
    } else {
        $outcomeClass = 'SMALL_LOSS'
        $inference = 'Fact: loss is below planned risk; verify exit events and broker fills.'
    }

    [pscustomobject]@{
        TradeID = $trade.TradeID
        EntryTimeUtc = $trade.EntryTime
        ExitTimeUtc = $trade.ExitTime
        Instrument = ($trade.InstrumentProfileName -split '_')[0]
        Side = $trade.Side
        Path = $trade.ResearchPath
        StrategyVersion = $trade.StrategyVersion
        Quantity = $trade.Quantity
        EntryPrice = $trade.EntryPrice
        ExitPrice = $trade.ExitPrice
        StopPriceAtExit = $trade.StopPrice
        TargetPrice = $trade.TargetPrice
        ExitRole = $trade.ExitRole
        OutcomeClass = $outcomeClass
        GrossPoints = $trade.Points
        GrossDollars = $trade.Dollars
        RiskDollars = $trade.RiskDollars
        TargetR = $trade.TargetR
        MFEPoints = $trade.ActualMFEPoints
        MAEPoints = $trade.ActualMAEPoints
        MFER = $trade.ActualMFE_R
        MAER = $trade.ActualMAE_R
        ExitPriceDriftPoints = $trade.ExitPriceDriftPoints
        ProtectionMoved = [bool]($eventNames -contains 'PROTECT_BE1R_APPLIED_V186' -or $eventNames -contains 'PROTECT_BE075R_APPLIED')
        ProtectionEventCount = @($eventNames | Where-Object { $_ -like 'PROTECT_*' }).Count
        EntryEventTimeUtc = ($entryEvents | Select-Object -First 1 -ExpandProperty Time)
        EntryEventPrice = ($entryEvents | Select-Object -First 1 -ExpandProperty Price)
        ExitEventTimeUtc = ($exitEvents | Select-Object -Last 1 -ExpandProperty Time)
        ExitEventPrice = ($exitEvents | Select-Object -Last 1 -ExpandProperty Price)
        ProtectionEventTimesUtc = (($protectEvents | ForEach-Object { $_.Time + ':' + $_.Event + ':' + $_.Price }) -join '|')
        CleanupEventTimesUtc = (($cleanupEvents | ForEach-Object { $_.Time + ':' + $_.Event + ':' + $_.Message }) -join '|')
        DecisionReasons = ($decisionReasons -join '|')
        DecisionCount = @($decisions).Count
        EventNames = ($eventNames -join '|')
        Inference = $inference
        EvidenceStatus = if ($outcomeClass -in @('PROFIT','INITIAL_RISK_LOSS')) { 'FACT_WITH_LIMITED_INFERENCE' } else { 'FACT_PENDING_ORDER_EVENT_REVIEW' }
        SourceSnapshot = $trade.SnapshotID
    }
}

$ledgerPath = Join-Path $OutputDirectory 'order_ledger.csv'
$ledger | Sort-Object EntryTimeUtc, TradeID | Export-Csv -LiteralPath $ledgerPath -NoTypeInformation -Encoding UTF8

$analysisPath = Join-Path $OutputDirectory 'optimization_queue.csv'
$ledger |
    Where-Object { $_.OutcomeClass -in @('INITIAL_RISK_LOSS','BE_PROTECTED_SMALL_LOSS','FLAT','ABNORMAL_QUARANTINE') } |
    Select-Object TradeID,Instrument,Side,Path,StrategyVersion,OutcomeClass,GrossDollars,RiskDollars,MFER,MAER,ProtectionMoved,ProtectionEventTimesUtc,ExitRole,DecisionReasons,EvidenceStatus,Inference |
    Sort-Object EntryTimeUtc, TradeID |
    Export-Csv -LiteralPath $analysisPath -NoTypeInformation -Encoding UTF8

$meta = [pscustomobject]@{
    GeneratedAtUtc = [datetime]::UtcNow.ToString('O')
    LogDirectory = $LogDirectory
    TradeCount = @($ledger).Count
    OptimizationQueueCount = @($ledger | Where-Object { $_.OutcomeClass -in @('INITIAL_RISK_LOSS','BE_PROTECTED_SMALL_LOSS','FLAT','ABNORMAL_QUARANTINE') }).Count
    Rules = 'Fact fields come from execution_trades/events. Inference is a hypothesis for verification. Startup test orders are excluded. Trades are deduplicated by TradeID.'
}
$meta | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'README.json') -Encoding UTF8
Write-Output ("ORDER_KB_BUILT trades={0} queue={1} path={2}" -f $meta.TradeCount, $meta.OptimizationQueueCount, $OutputDirectory)
