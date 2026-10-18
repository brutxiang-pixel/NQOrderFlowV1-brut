param([string]$ArchivePath = "$env:APPDATA/ATAS/StrategyConfigs/OPF_es_options_proxy.jsonl", [Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$times = @()
if (Test-Path -LiteralPath $ArchivePath) {
    foreach ($line in [IO.File]::ReadLines($ArchivePath)) {
        try { $row = $line | ConvertFrom-Json } catch { continue }
        if ($row.Status -ne 'ModelReady' -or $row.AtmQuotesReady -ne $true -or $row.LocalQuoteCoverage -lt 0.5) { continue }
        $time = [datetime]::Parse($row.CapturedAtUtc,[Globalization.CultureInfo]::InvariantCulture,[Globalization.DateTimeStyles]::RoundtripKind)
        $times += $time.ToUniversalTime()
    }
}
$times = @($times | Sort-Object -Unique)
$windows = @(); $start = $null; $last = $null; $count = 0
foreach ($time in $times) {
    if ($last -and ($time - $last).TotalMinutes -gt 5) {
        if (($last - $start).TotalMinutes -ge 15) { $windows += [pscustomobject]@{ StartUTC=$start.ToString('o'); EndUTC=$last.ToString('o'); StartBJT=$start.AddHours(8).ToString('yyyy-MM-dd HH:mm:ss'); EndBJT=$last.AddHours(8).ToString('yyyy-MM-dd HH:mm:ss'); Snapshots=$count; Minutes=[Math]::Round(($last-$start).TotalMinutes,1); Meaning='Options archive coverage only; chart ticks and MBO availability not verified' } }
        $start = $null; $count = 0
    }
    if (!$start) { $start = $time }
    $last = $time; $count++
}
if ($last -and ($last-$start).TotalMinutes -ge 15) { $windows += [pscustomobject]@{ StartUTC=$start.ToString('o'); EndUTC=$last.ToString('o'); StartBJT=$start.AddHours(8).ToString('yyyy-MM-dd HH:mm:ss'); EndBJT=$last.AddHours(8).ToString('yyyy-MM-dd HH:mm:ss'); Snapshots=$count; Minutes=[Math]::Round(($last-$start).TotalMinutes,1); Meaning='Options archive coverage only; chart ticks and MBO availability not verified' } }
$windows | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'options-replay-windows.csv') -NoTypeInformation -Encoding UTF8
