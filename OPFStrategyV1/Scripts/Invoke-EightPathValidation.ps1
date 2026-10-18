param(
    [string]$LogDirectory = "$env:APPDATA/ATAS/StrategyLogs/OPFStrategyV1",
    [string]$OutputDirectory,
    [string]$BaselineDirectory,
    [string]$FixtureDirectory
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$repoRoot = Split-Path $projectRoot -Parent
$LogDirectory = [IO.Path]::GetFullPath($LogDirectory)
if ($BaselineDirectory) { $BaselineDirectory = [IO.Path]::GetFullPath($BaselineDirectory) }
if ($FixtureDirectory) { $FixtureDirectory = [IO.Path]::GetFullPath($FixtureDirectory) }
if (!$OutputDirectory) { $OutputDirectory = Join-Path $projectRoot ("Evidence/Validation/" + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$fixtures = Join-Path $OutputDirectory 'fixtures'
New-Item -ItemType Directory -Path $fixtures -Force | Out-Null
$source = if ($FixtureDirectory) { $FixtureDirectory } else { $LogDirectory }
$inputs = @(Get-ChildItem -LiteralPath $source -Filter '*_validation_*.jsonl' -File)
foreach ($inputFile in $inputs) { Copy-Item -LiteralPath $inputFile.FullName -Destination $fixtures }
# Immutable candidate packets are the baseline. Never copy configuration/webhook secrets.
$inputHashes = @(Get-ChildItem -LiteralPath $fixtures -File | ForEach-Object {
    [pscustomobject]@{ File = $_.Name; SHA256 = (Get-FileHash -LiteralPath $_.FullName).Hash; Bytes = $_.Length }
})
ConvertTo-Json -InputObject @($inputHashes) -Depth 5 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'input-manifest.json') -Encoding UTF8
$tests = @('ValidationReplayTests', 'IntradayEntryTests', 'OptionsProxyGateTests', 'LiveProtectionFailClosedTests', 'FeishuNotificationTests')
foreach ($test in $tests) {
    $testProject = Join-Path $PSScriptRoot "tests/$test/$test.csproj"
    $testOutput = & dotnet run --project $testProject -c Release 2>&1
    $exitCode = $LASTEXITCODE
    $testOutput | Set-Content -LiteralPath (Join-Path $OutputDirectory "$test.txt") -Encoding UTF8
    if ($exitCode -ne 0) { throw "$test failed. See output in $OutputDirectory" }
}
$syntheticFixtures = Join-Path $OutputDirectory 'synthetic-function-fixtures'
& dotnet run --project (Join-Path $PSScriptRoot 'tests/ValidationReplayTests/ValidationReplayTests.csproj') -c Release --no-build -- --write-synthetic $syntheticFixtures |
    Set-Content -LiteralPath (Join-Path $OutputDirectory 'synthetic-create.txt') -Encoding UTF8
if ($LASTEXITCODE -ne 0) { throw 'Synthetic fixture generation failed.' }
& dotnet run --project (Join-Path $PSScriptRoot 'tests/ValidationReplayTests/ValidationReplayTests.csproj') -c Release --no-build -- $syntheticFixtures (Join-Path $OutputDirectory 'synthetic-replay.json') |
    Set-Content -LiteralPath (Join-Path $OutputDirectory 'synthetic-replay.txt') -Encoding UTF8
if ($LASTEXITCODE -ne 0) { throw 'Synthetic file replay failed.' }
$replayResults = Join-Path $OutputDirectory 'gate-replay.json'
& dotnet run --project (Join-Path $PSScriptRoot 'tests/ValidationReplayTests/ValidationReplayTests.csproj') -c Release --no-build -- $fixtures $replayResults |
    Set-Content -LiteralPath (Join-Path $OutputDirectory 'gate-replay.txt') -Encoding UTF8
$replayExit = $LASTEXITCODE
if ($replayExit -notin @(0, 2)) { throw 'Frozen packet replay failed.' }
if ($BaselineDirectory) {
    $oldHashes = @{}
    foreach ($item in @(Get-Content -LiteralPath (Join-Path $BaselineDirectory 'input-manifest.json') -Raw | ConvertFrom-Json | ForEach-Object { foreach ($entry in $_) { if ($null -ne $entry) { $entry } } })) { $oldHashes[$item.File] = $item.SHA256 }
    if ($oldHashes.Count -ne $inputHashes.Count) { throw 'Baseline comparison requires the same fixed input files. Use -FixtureDirectory.' }
    foreach ($item in $inputHashes) { if ($oldHashes[$item.File] -ne $item.SHA256) { throw "Baseline input changed: $($item.File)" } }
    $previous = @{}
    foreach ($row in @(Get-Content -LiteralPath (Join-Path $BaselineDirectory 'gate-replay.json') -Raw | ConvertFrom-Json | ForEach-Object { foreach ($entry in $_) { if ($null -ne $entry) { $entry } } })) { $previous[$row.Key] = $row }
    $differences = @(foreach ($row in @(Get-Content -LiteralPath $replayResults -Raw | ConvertFrom-Json | ForEach-Object { foreach ($entry in $_) { if ($null -ne $entry) { $entry } } })) {
        if ($previous.ContainsKey($row.Key) -and $previous[$row.Key].Actual -ne $row.Actual) {
            [pscustomobject]@{ Key=$row.Key; Product=$row.Product; Path=$row.Path; Before=$previous[$row.Key].Actual; After=$row.Actual }
        }
    })
    $differences | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'gate-differences.csv') -NoTypeInformation -Encoding UTF8
}
& (Join-Path $PSScriptRoot 'Export-EightPathValidationAudit.ps1') -LogDirectory $LogDirectory -OutputDirectory $OutputDirectory
& (Join-Path $PSScriptRoot 'tests/Test-ValidationAudit.ps1') -OutputDirectory $OutputDirectory |
    Set-Content -LiteralPath (Join-Path $OutputDirectory 'audit-test.txt') -Encoding UTF8
& (Join-Path $PSScriptRoot 'Export-OptionsReplayWindows.ps1') -OutputDirectory $OutputDirectory
$buildOutput = & dotnet build (Join-Path $projectRoot 'OPFStrategyV1.csproj') -c Release --no-restore -p:DeployToATAS=false 2>&1
$buildExit = $LASTEXITCODE
$buildOutput | Set-Content -LiteralPath (Join-Path $OutputDirectory 'build.txt') -Encoding UTF8
if ($buildExit -ne 0) { throw 'Strategy build failed.' }
$dll = Join-Path $projectRoot 'bin/Release/net10.0-windows/OPFStrategyV1.dll'
Copy-Item -LiteralPath $dll -Destination $OutputDirectory
$deployed = Join-Path $env:APPDATA 'ATAS/Strategies/OPFStrategyV1.dll'
$replayed = @(Get-Content -LiteralPath $replayResults -Raw | ConvertFrom-Json | ForEach-Object { foreach ($entry in $_) { if ($null -ne $entry) { $entry } } })
[pscustomobject]@{ CreatedAt=(Get-Date).ToString('o'); DLL=(Get-FileHash -LiteralPath $dll).Hash;
    DeployedDLL=if(Test-Path -LiteralPath $deployed){(Get-FileHash -LiteralPath $deployed).Hash}else{$null};
    RealGatePackets=$replayed.Count; GateDifferences=@($replayed | Where-Object { !$_.Matches }).Count;
    Coverage='Production options/reaction gate functions only; candidate generation and broker callbacks not replayed';
    TestOrdersSent=0; DeploymentPerformed=$false; LogsRemoved=0 } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'manifest.json') -Encoding UTF8
Write-Output "Validation package: $OutputDirectory"
if ($replayExit -eq 2) { throw 'Gate behavior differs from frozen observations. Review gate-replay.json before deploying.' }
