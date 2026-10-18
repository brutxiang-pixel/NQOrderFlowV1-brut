param([string]$LogDirectory = "$env:APPDATA/ATAS/StrategyLogs/OPFStrategyV1", [Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$paths = @('ObservationConfirm','BreakawayFvg_Qualified','FailureReverse_ObservationInvalidated_WideStop1_5R',
    'FailureReverse_RetestFailed','ESResponseAlignedBreakout','ESWallRejectionReversal','ESExpectedMoveExpansion','ESGammaConcentrationResponse')
$decisions = @(Get-ChildItem -LiteralPath $LogDirectory -Filter '*_execution_decisions.csv' -File | ForEach-Object { Import-Csv -LiteralPath $_.FullName } |
    Where-Object { $_.ResearchPath -in $paths } | Sort-Object SnapshotID,SignalID,ResearchPath,Time,Decision -Unique)
$pnl = @{}
Get-ChildItem -LiteralPath $LogDirectory -Filter '*_live_account_pnl.csv' -File | ForEach-Object {
    foreach ($row in (Import-Csv -LiteralPath $_.FullName)) { $pnl[$row.SnapshotID + '|' + $row.TradeID] = $row }
}
$closed = @(); $excluded = @()
Get-ChildItem -LiteralPath $LogDirectory -Filter '*_execution_trades.csv' -File | ForEach-Object {
    foreach ($row in (Import-Csv -LiteralPath $_.FullName)) {
        if ($row.ResearchPath -notin $paths) { continue }
        $ledger = $pnl[$row.SnapshotID + '|' + $row.TradeID]
        $reason = if (!$ledger) { 'MissingNetLedger' }
            elseif ($row.IsAbnormalExecution -eq 'True') { 'AbnormalExecution' }
            elseif ($ledger.Classification -ne 'Normal') { 'NonNormalClassification' }
            elseif ([decimal]$ledger.Quantity -ne [decimal]$row.Quantity) { 'QuantityMismatch' } else { '' }
        if ($reason) { $excluded += [pscustomobject]@{Snapshot=$row.SnapshotID; Trade=$row.TradeID; Path=$row.ResearchPath; Reason=$reason}; continue }
        $entry = [datetime]::Parse($row.EntryTime, [Globalization.CultureInfo]::InvariantCulture)
        $bjtHour = $entry.AddHours(8).Hour
        $session = if ($bjtHour -lt 8) { 'BJT00-08' } elseif ($bjtHour -lt 20) { 'BJT08-20' } else { 'BJT20-24' }
        $closed += [pscustomobject]@{ Snapshot=$row.SnapshotID; Trade=$row.TradeID; Strategy=$row.StrategyVersion;
            Product=$row.InstrumentProfileName; Path=$row.ResearchPath; Side=$row.Side; Session=$session;
            EntryUtc=$row.EntryTime; Net=[decimal]$ledger.NetPnLDollars; Gross=[decimal]$ledger.GrossPnLDollars;
            Fee=[decimal]$ledger.CommissionDollars; Quantity=[decimal]$row.Quantity;
            GivebackR=[Math]::Max([decimal]0, [decimal]$row.ActualMFE_R - [decimal]$row.PointsR);
            Scope=$ledger.AccountScope; Replay=$ledger.IsReplay }
    }
}
$closed = @($closed | Sort-Object Snapshot,Trade -Unique)
$summaries = @(foreach ($group in ($decisions | Group-Object StrategyVersion,InstrumentProfileName,ResearchPath,Side)) {
    $first = $group.Group[0]
    $candidates = @($group.Group | Group-Object SnapshotID,SignalID,ResearchPath)
    $executed = @($group.Group | Where-Object Decision -eq 'Execute' | Group-Object SnapshotID,SignalID,ResearchPath)
    $skips = @($group.Group | Where-Object Decision -eq 'Skip' | Group-Object Reason | Sort-Object Count -Descending)
    $trades = @($closed | Where-Object { $_.Strategy -eq $first.StrategyVersion -and $_.Product -eq $first.InstrumentProfileName -and $_.Path -eq $first.ResearchPath -and $_.Side -eq $first.Side })
    $net = ($trades | Measure-Object Net -Sum).Sum
    [pscustomobject]@{ Strategy=$first.StrategyVersion; Product=$first.InstrumentProfileName; Path=$first.ResearchPath; Side=$first.Side;
        DecisionCandidates=$candidates.Count; ExecuteDecisions=$executed.Count; MainSkip=if($skips.Count){$skips[0].Name}else{''};
        ClosedValid=$trades.Count; WinRate=if($trades.Count){[Math]::Round(@($trades|Where-Object Net -gt 0).Count/$trades.Count*100,2)}else{$null};
        NetDollars=$net; AverageNet=if($trades.Count){[Math]::Round($net/$trades.Count,2)}else{$null};
        AverageGivebackR=if($trades.Count){[Math]::Round(($trades|Measure-Object GivebackR -Average).Average,4)}else{$null} }
})
$summaries | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'eight-path-summary.csv') -NoTypeInformation -Encoding UTF8
$closed | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'closed-valid.csv') -NoTypeInformation -Encoding UTF8
$excluded | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'excluded-trades.csv') -NoTypeInformation -Encoding UTF8
$decisions | Group-Object StrategyVersion,InstrumentProfileName,ResearchPath,Side,Reason |
    Select-Object Name,Count | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'decision-funnel.csv') -NoTypeInformation -Encoding UTF8
$closed | Group-Object Strategy,Product,Path,Side,Session,Scope,Replay | ForEach-Object {
    [pscustomobject]@{ Layer=$_.Name; Count=$_.Count; Net=($_.Group|Measure-Object Net -Sum).Sum;
        WinRate=[Math]::Round(@($_.Group|Where-Object Net -gt 0).Count/$_.Count*100,2) }
} | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'session-account-mode.csv') -NoTypeInformation -Encoding UTF8
@'
DecisionCandidates counts unique candidates in execution decisions, not pre-generation opportunities. Execute is a submission decision, not a fill. ClosedValid requires a matching net ledger, equal quantities and a normal completed trade in the main eight paths. Test orders and independent iceberg trades are excluded. Legacy rows without account/mode fields are historical reference only. Session hours use UTC+8. Missing net ledgers are excluded, not estimated. Compare results within each version, account and mode; do not pool versions to claim improvement.
'@ | Set-Content -LiteralPath (Join-Path $OutputDirectory 'audit-boundaries.txt') -Encoding UTF8
