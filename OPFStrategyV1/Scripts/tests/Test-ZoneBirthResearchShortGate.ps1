$ErrorActionPreference = 'Stop'

$projectPath = Join-Path $PSScriptRoot 'ZoneBirthResearchShortGateTests\ZoneBirthResearchShortGateTests.csproj'
& 'C:\Program Files\dotnet\dotnet.exe' run --project $projectPath -c Debug
if ($LASTEXITCODE -ne 0) {
    throw "ZoneBirthResearchShortGate tests failed with exit code $LASTEXITCODE."
}
