$ErrorActionPreference = 'Stop'

$root = Join-Path $env:APPDATA 'ATAS\StrategyLogs'
$dir = Join-Path $root 'OPFStrategyV1'
$legacyLogs = @('NQOrderFlowV1.log', 'NQOrderFlowV1_trades.csv')

if (Test-Path -LiteralPath $dir) {
    Get-ChildItem -LiteralPath $dir -File | Remove-Item -Force
}
else {
    New-Item -ItemType Directory -Path $dir | Out-Null
}

foreach ($name in $legacyLogs) {
    $path = Join-Path $root $name
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Force
    }
}

Get-ChildItem -LiteralPath $dir -Force | Select-Object Mode, FullName, Length, LastWriteTime
