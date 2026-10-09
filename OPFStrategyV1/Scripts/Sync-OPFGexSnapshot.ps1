param(
    [string]$Uri = 'https://deltapex.cn/data/signals.json'
)

$targets = @(
    (Join-Path $env:APPDATA 'ATAS\StrategyConfigs\OPFStrategyV1_gex_snapshot.json'),
    (Join-Path $env:APPDATA 'ATAS X\StrategyConfigs\OPFStrategyV1_gex_snapshot.json')
)

try {
    $response = Invoke-WebRequest -Uri $Uri -UseBasicParsing -TimeoutSec 30
    $json = $response.Content | ConvertFrom-Json
    $levels = @($json.symbols.NQ.levels)
    if ($levels.Count -eq 0) { throw 'symbols.NQ.levels is empty' }
    foreach ($level in $levels) {
        if ($level.price -lt 10000 -or $level.price -gt 50000 -or [math]::Abs(($level.price * 4) - [math]::Round($level.price * 4)) -gt 0.0001) {
            throw "Invalid NQ level price: $($level.price)"
        }
    }

    $utf8 = [System.Text.UTF8Encoding]::new($false)
    foreach ($target in $targets) {
        $dir = Split-Path -Parent $target
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
        $temp = "$target.tmp"
        [System.IO.File]::WriteAllText($temp, $response.Content, $utf8)
        Move-Item -LiteralPath $temp -Destination $target -Force
    }

    Write-Host "Saved validated GEX snapshot ($($levels.Count) NQ levels; dataDate=$($json.dataDate)): $($targets -join '; ')"
}
catch {
    Write-Error "GEX snapshot sync failed: $($_.Exception.Message)"
    exit 1
}
finally {
    foreach ($target in $targets) {
        $temp = "$target.tmp"
        if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Force }
    }
}

exit 0
