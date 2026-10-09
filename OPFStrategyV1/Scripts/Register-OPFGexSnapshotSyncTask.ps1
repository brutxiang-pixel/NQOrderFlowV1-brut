param(
    [switch]$StartNow
)

$taskName = 'OPFStrategyV1-GexSnapshotScheduler'
$scriptPath = Join-Path $PSScriptRoot 'Start-OPFGexSnapshotScheduler.ps1'
if (-not (Test-Path -LiteralPath $scriptPath)) { throw "Scheduler script not found: $scriptPath" }

# The scheduler owns all baseline, pre-market, intraday and forced refreshes.
# Registering the old one-shot task beside it would create competing writers and
# make the freshness state ambiguous, so remove that legacy task if present.
Unregister-ScheduledTask -TaskName 'OPFStrategyV1-GexSnapshotSync' -Confirm:$false -ErrorAction SilentlyContinue

$powershell = Join-Path $PSHOME 'powershell.exe'
$action = New-ScheduledTaskAction -Execute $powershell -Argument "-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$scriptPath`""
$trigger = New-ScheduledTaskTrigger -AtLogOn -User "$env:USERDOMAIN\$env:USERNAME"
$principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType Interactive -RunLevel Limited
$restart = New-ScheduledTaskTrigger -AtStartup
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -MultipleInstances IgnoreNew -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1)

Register-ScheduledTask -TaskName $taskName -Action $action -Trigger @($trigger, $restart) -Principal $principal -Settings $settings -Description 'Keeps the OPF GEX snapshot scheduler running. The strategy reads only the validated local snapshot.' -Force -ErrorAction Stop | Out-Null

if ($StartNow) {
    Start-ScheduledTask -TaskName $taskName
    Write-Host "Registered and started $taskName."
} else {
    Write-Host "Registered $taskName to start at logon/startup. Run with -StartNow to start it immediately."
}
