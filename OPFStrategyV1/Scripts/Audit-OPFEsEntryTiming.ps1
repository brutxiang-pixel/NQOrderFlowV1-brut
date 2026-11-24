param(
    [string]$LogRoot = "$env:APPDATA\ATAS\StrategyLogs\OPFStrategyV1",
    [string]$LedgerPath = "$env:APPDATA\ATAS\OrderKnowledgeBase\order_ledger.csv",
    [string]$OutputDirectory,
    [datetime]$SinceUtc = [datetime]'2026-10-20'
)
$ErrorActionPreference = 'Stop'
if (!$OutputDirectory) { throw 'OutputDirectory is required; original logs remain read-only.' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
function Utc($value) {
    if ($value -is [datetime]) {
        if ($value.Kind -eq [DateTimeKind]::Unspecified) { return [datetime]::SpecifyKind($value, [DateTimeKind]::Utc) }
        return $value.ToUniversalTime()
    }
    [datetimeoffset]::Parse($value, [cultureinfo]::InvariantCulture, [Globalization.DateTimeStyles]::AssumeUniversal).UtcDateTime
}
function Age($decision, $observed) { if ($observed) { [math]::Round(($decision - (Utc $observed)).TotalSeconds, 3) } }
$paths = @('ESResponseAlignedBreakout','ESWallRejectionReversal','ESExpectedMoveExpansion','ESGammaConcentrationResponse')
$trades = @(Import-Csv $LedgerPath | Where-Object { $_.Path -in $paths -and (Utc $_.EntryTimeUtc) -ge $SinceUtc })
$result = foreach ($snapshot in ($trades | Group-Object SourceSnapshot)) {
    $events = @(Get-ChildItem $LogRoot -Filter ($snapshot.Name + '*execution_events.csv') | ForEach-Object { Import-Csv $_.FullName })
    $packets = @(Get-ChildItem $LogRoot -Filter ($snapshot.Name + '*validation_options.jsonl') | ForEach-Object { Get-Content $_.FullName | Where-Object { $_.Trim() } | ForEach-Object { $_ | ConvertFrom-Json } })
    foreach ($trade in $snapshot.Group) {
        $entry = $events | Where-Object { $_.TradeID -eq $trade.TradeID -and $_.Event -eq 'ENTRY_SEND' } | Sort-Object { Utc $_.Time } | Select-Object -First 1
        $sent = if ($entry) { Utc $entry.Time } else { Utc $trade.EntryTimeUtc }
        # Match the exact signal and source snapshot; never borrow a nearby candidate.
        $packet = if ($entry) { $packets | Where-Object { $_.SignalId -eq $entry.SignalID -and $_.Path -eq $trade.Path } | Sort-Object { Utc $_.DecisionUtc } | Select-Object -First 1 }
        $context = $packet.Context
        $decision = if ($packet) { Utc $packet.DecisionUtc }
        $anchorAt = $null
        if ($entry -and $entry.SignalID -match '^ESResponseAlignedBreakout-(Long|Short)-([0-9]{21})') {
            $anchorAt = [datetime]::ParseExact($Matches[2], 'yyyyMMddHHmmssfffffff', [cultureinfo]::InvariantCulture)
        }
        $sign = if ($trade.Side -eq 'Long') { [decimal]1 } else { [decimal]-1 }
        [pscustomobject]@{
            TradeID=$trade.TradeID; Version=$trade.StrategyVersion; Instrument=$trade.Instrument; Path=$trade.Path
            EntryUtc=$trade.EntryTimeUtc; GrossDollars=$trade.GrossDollars; RiskDollars=$trade.RiskDollars
            MFER=$trade.MFER; MAER=$trade.MAER; ExitRole=$trade.ExitRole
            Evidence=if (!$entry) {'MissingEntryEvent'} elseif (!$packet) {'MissingExactOptionsPacket'} else {'ExactSignalPacket'}
            GateReason=if ($packet) { if ($packet.ExpectedReason) {$packet.ExpectedReason} else {'Allow'} }
            Status=$context.Status; SnapshotAgeSeconds=if ($packet) { Age $decision $context.CapturedAtUtc }
            QuoteAgeSeconds=if ($packet) { Age $decision $context.OptionQuotesObservedAtUtc }
            UnderlyingAgeSeconds=if ($packet) { Age $decision $context.UnderlyingObservedAtUtc }
            AtmReady=$context.AtmQuotesReady; LocalCoverage=$context.LocalQuoteCoverage
            DecisionToSendSeconds=if ($packet) {[math]::Round(($sent-$decision).TotalSeconds,3)}
            BreakoutAnchorToSendSeconds=if ($anchorAt) {[math]::Round(($sent-$anchorAt).TotalSeconds,3)}
            LevelTouchToSendSeconds=if ($context.EventTouchedAtUtc) {Age $sent $context.EventTouchedAtUtc}
            AdverseFillPoints=if ($entry) {$sign * ([decimal]$trade.EntryPrice-[decimal]$entry.Price)}
            ExpectedMoveRatio=if ($context.ExpectedMoveAtAnchor -gt 0 -and $null -ne $context.UnderlyingPrice -and $null -ne $context.ExpectedMoveAnchor) {[math]::Round([math]::Abs($context.UnderlyingPrice-$context.ExpectedMoveAnchor)/$context.ExpectedMoveAtAnchor,4)}
        }
    }
}
$result | Sort-Object EntryUtc,TradeID | Export-Csv (Join-Path $OutputDirectory 'es_entry_timing.csv') -NoTypeInformation -Encoding utf8
$result | Group-Object Version,Instrument,Path | ForEach-Object {
    $group=$_.Group
    [pscustomobject]@{Group=$_.Name; Trades=$group.Count; ExactPackets=@($group|Where-Object Evidence -eq 'ExactSignalPacket').Count; Gross=($group|Measure-Object GrossDollars -Sum).Sum; Below1R=@($group|Where-Object {$_.MFER -ne '' -and [decimal]$_.MFER -lt 1}).Count}
} | Export-Csv (Join-Path $OutputDirectory 'es_entry_timing_summary.csv') -NoTypeInformation -Encoding utf8
$funnel = foreach ($file in (Get-ChildItem $LogRoot -Filter '*_research.log' | Where-Object { $_.Name -match '^OPF-(\d{8})' -and [datetime]::ParseExact($Matches[1], 'yyyyMMdd', [cultureinfo]::InvariantCulture) -ge $SinceUtc.Date })) {
    foreach ($line in (Select-String -Path $file.FullName -Pattern 'ES_CROSS_MARKET_REJECT path=|ES_CROSS_MARKET_FUNNEL stage=DataBlocked')) {
        $path = if ($line.Line -match 'path=([^ ]+)') { $Matches[1] } else { 'ESDataAvailability' }
        $reason = if ($line.Line -match 'reason=([^ ]+)') { $Matches[1] } elseif ($line.Line -match 'status=([^ ]+)') { $Matches[1] } else { 'Unknown' }
        $date = if ($line.Line -match '^(\d{4}-\d{2}-\d{2})') { $Matches[1] } else { '' }
        $bar = [regex]::Match($line.Line, 'bar=(\d+)').Groups[1].Value
        $side = [regex]::Match($line.Line, 'side=([^ ]+)').Groups[1].Value
        $bucket = if ($path -eq 'ESDataAvailability' -and $line.Line -match '^(\S+)') { [math]::Floor(([datetimeoffset]::Parse($Matches[1])).ToUnixTimeSeconds() / 5) } else { '' }
        $eventKey = if ($path -eq 'ESDataAvailability') { "$date|$path|$reason|$bucket" } else { "$date|$path|$bar|$side|$reason" }
        [pscustomobject]@{ Snapshot=[IO.Path]::GetFileNameWithoutExtension($file.Name) -replace '_research$',''; Path=$path; Reason=$reason; EventKey=$eventKey }
    }
}
$funnel | Sort-Object EventKey -Unique | Group-Object Path,Reason | ForEach-Object {
    [pscustomobject]@{Snapshot=$_.Group[0].Snapshot; Path=$_.Group[0].Path; Reason=$_.Group[0].Reason; Count=$_.Count}
} | Export-Csv (Join-Path $OutputDirectory 'es_candidate_rejection_funnel.csv') -NoTypeInformation -Encoding utf8
$result | Group-Object { if ([double]$_.QuoteAgeSeconds -le 60) {'Quote<=60s'} elseif ([double]$_.QuoteAgeSeconds -le 90) {'Quote61-90s'} elseif ($_.QuoteAgeSeconds) {'Quote>90s'} else {'QuoteAgeMissing'} },Instrument,Path | ForEach-Object {
    $group=$_.Group
    [pscustomobject]@{Group=$_.Name; Trades=$group.Count; Gross=($group|Measure-Object GrossDollars -Sum).Sum; Wins=@($group|Where-Object {[decimal]$_.GrossDollars -gt 0}).Count; Losses=@($group|Where-Object {[decimal]$_.GrossDollars -lt 0}).Count; MfeBelow1R=@($group|Where-Object {$_.MFER -ne '' -and [decimal]$_.MFER -lt 1}).Count}
} | Export-Csv (Join-Path $OutputDirectory 'es_quote_freshness_by_path.csv') -NoTypeInformation -Encoding utf8
Write-Output "Audited $($result.Count) fills and $(($funnel | Sort-Object EventKey -Unique | Measure-Object).Count) unique funnel events; output: $OutputDirectory"
