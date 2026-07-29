param(
    [Parameter(Mandatory = $true)]
    [string]$ProfileFile
)

$source = Resolve-Path -LiteralPath $ProfileFile
$targetDirectory = Join-Path $env:APPDATA 'ATAS\StrategyConfigs'
$target = Join-Path $targetDirectory 'OPFStrategyV1_actual_execution.json'

New-Item -ItemType Directory -Path $targetDirectory -Force | Out-Null
Copy-Item -LiteralPath $source -Destination $target -Force
Get-FileHash -LiteralPath $source -Algorithm SHA256
Get-FileHash -LiteralPath $target -Algorithm SHA256
