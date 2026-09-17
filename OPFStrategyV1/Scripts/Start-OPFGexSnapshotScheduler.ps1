param([switch]$RunOnce)

# Implements the full "美盘前刷新 + 盘中每 30 分钟刷新 + 跨关键位/波动率突变刷新 + 超过 90 分钟 STALE"
# schedule described in docs/superpowers/specs/2026-09-17-gex-intraday-refresh-design.md.
#
# Four independent refresh mechanisms, all funneling into the same Sync-OPFGexSnapshot.ps1 call and
# the same OPFStrategyV1_gex_sync_state.json state file (whose `completedAt` field the strategy-side
# GexDailySnapshotLoader reads for its 90-minute staleness check):
#
#   1. Baseline   - existing behavior, unchanged: first success after Beijing 09:15, once per day.
#   2. PreMarket  - Eastern time 09:00, once per trading day.
#   3. Intraday   - Eastern time 09:30-16:00 inclusive, every 30 minutes, once per slot per day.
#   4. Forced     - triggered by the strategy writing OPFStrategyV1_gex_refresh_request.json when a
#                   completed bar crosses a tracked GEX level or spikes in range vs ATR14. Consumed at
#                   most once per distinct requestedAtUtc; no scheduler-side cooldown beyond that
#                   because the strategy already enforces GexRefreshTriggerGate's cooldown before it
#                   ever writes a new request.
#
# All four mechanisms retry every poll (60s) until they succeed; a persistently failing vendor call
# never throws out of the loop, and the last valid local snapshot file is left untouched by
# Sync-OPFGexSnapshot.ps1 on failure (it writes to a temp file and only moves it into place on
# success). This script never talks to the network itself; it only shells out to Sync-OPFGexSnapshot.ps1.

$syncScript = Join-Path $PSScriptRoot 'Sync-OPFGexSnapshot.ps1'
$configDirectory = Join-Path $env:APPDATA 'ATAS\StrategyConfigs'
$statePath = Join-Path $configDirectory 'OPFStrategyV1_gex_sync_state.json'
$requestPath = Join-Path $configDirectory 'OPFStrategyV1_gex_refresh_request.json'
$mutex = [Threading.Mutex]::new($false, 'Local\OPFStrategyV1-GexSnapshotScheduler')

$baselineTimeOfDay = [TimeSpan]'09:15:00'
$preMarketEastern = [TimeSpan]'09:00:00'
$intradayStartEastern = [TimeSpan]'09:30:00'
$intradayEndEastern = [TimeSpan]'16:00:00'
$intradayIntervalMinutes = 30

function Get-EasternNow {
    $tz = [System.TimeZoneInfo]::FindSystemTimeZoneById('Eastern Standard Time')
    return [System.TimeZoneInfo]::ConvertTimeFromUtc([DateTime]::UtcNow, $tz)
}

function Resolve-GexIntradaySlotKey([datetime]$easternNow) {
    if ($easternNow.DayOfWeek -eq [DayOfWeek]::Saturday -or $easternNow.DayOfWeek -eq [DayOfWeek]::Sunday) {
        return $null
    }

    $t = New-TimeSpan -Hours $easternNow.Hour -Minutes $easternNow.Minute
    $dateKey = $easternNow.ToString('yyyy-MM-dd')

    if ($t -eq $preMarketEastern) {
        return "$dateKey|PreMarket"
    }

    if ($t -ge $intradayStartEastern -and $t -le $intradayEndEastern -and ($t.Minutes % $intradayIntervalMinutes) -eq 0) {
        $slotLabel = '{0:00}:{1:00}' -f $t.Hours, $t.Minutes
        return "$dateKey|Intraday|$slotLabel"
    }

    return $null
}

function Read-GexSchedulerState {
    param([string]$Path)

    if (Test-Path -LiteralPath $Path) {
        try {
            $loaded = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
            if ($null -ne $loaded) {
                if (-not (Get-Member -InputObject $loaded -Name 'completedSlots' -MemberType NoteProperty)) {
                    $loaded | Add-Member -NotePropertyName 'completedSlots' -NotePropertyValue @()
                }
                if (-not (Get-Member -InputObject $loaded -Name 'lastProcessedRefreshRequestAtUtc' -MemberType NoteProperty)) {
                    $loaded | Add-Member -NotePropertyName 'lastProcessedRefreshRequestAtUtc' -NotePropertyValue ''
                }
                return $loaded
            }
        }
        catch {
            Write-Warning "GEX scheduler state file could not be parsed, starting fresh: $($_.Exception.Message)"
        }
    }

    return [pscustomobject]@{
        lastSuccessDate                  = ''
        completedAt                      = ''
        completedSlots                   = @()
        lastProcessedRefreshRequestAtUtc = ''
    }
}

function Save-GexSchedulerState {
    param($State, [string]$Path)
    $State | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $Path -Encoding utf8
}

function Invoke-GexSync {
    & (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe') `
        -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $syncScript
    return ($LASTEXITCODE -eq 0)
}

function Complete-GexSlot {
    param($State, [string]$SlotKey, [string]$Today)

    # Prune slot keys from previous days on every successful sync so the file does not grow unbounded.
    $retained = @($State.completedSlots | Where-Object { $_ -like "$Today|*" })
    if ($SlotKey -and ($retained -notcontains $SlotKey)) {
        $retained += $SlotKey
    }
    $State.completedSlots = $retained
    $State.completedAt = [DateTime]::UtcNow.ToString('o')
    $State.lastSuccessDate = $Today
    return $State
}

if (-not $mutex.WaitOne(0)) { exit 0 }
try {
    New-Item -ItemType Directory -Force -Path $configDirectory | Out-Null
    while ($true) {
        $state = Read-GexSchedulerState -Path $statePath
        $now = Get-Date
        $today = $now.ToString('yyyy-MM-dd')

        # 1. Baseline: first success after Beijing 09:15, once per day. Uses system-local time, matching
        #    the pre-existing behavior this replaces.
        try {
            $baselineSlotKey = "$today|Baseline"
            $baselineDone = @($state.completedSlots) -contains $baselineSlotKey
            if ($now.TimeOfDay -ge $baselineTimeOfDay -and -not $baselineDone) {
                if (Invoke-GexSync) {
                    $state = Complete-GexSlot -State $state -SlotKey $baselineSlotKey -Today $today
                    Save-GexSchedulerState -State $state -Path $statePath
                }
            }
        }
        catch {
            Write-Warning "GEX baseline refresh check failed: $($_.Exception.Message)"
        }

        # 2/3. PreMarket + Intraday: Eastern-time slots, once per slot per trading day.
        try {
            $eastern = Get-EasternNow
            $slotKey = Resolve-GexIntradaySlotKey -easternNow $eastern
            if ($slotKey) {
                $slotDone = @($state.completedSlots) -contains $slotKey
                if (-not $slotDone) {
                    if (Invoke-GexSync) {
                        $state = Complete-GexSlot -State $state -SlotKey $slotKey -Today $today
                        Save-GexSchedulerState -State $state -Path $statePath
                    }
                }
            }
        }
        catch {
            Write-Warning "GEX intraday slot refresh check failed: $($_.Exception.Message)"
        }

        # 4. Forced refresh: consume the strategy's key-level-cross / volatility-spike request file at
        #    most once per distinct requestedAtUtc. The strategy already enforces its own 10-minute
        #    cooldown before writing a new request, so no additional cooldown is applied here.
        try {
            if (Test-Path -LiteralPath $requestPath) {
                $request = Get-Content -LiteralPath $requestPath -Raw | ConvertFrom-Json
                $requestedAtUtc = [DateTimeOffset]::Parse($request.requestedAtUtc).UtcDateTime
                $lastProcessed = if ([string]::IsNullOrWhiteSpace($state.lastProcessedRefreshRequestAtUtc)) {
                    [DateTime]::MinValue
                } else {
                    [DateTime]::Parse($state.lastProcessedRefreshRequestAtUtc).ToUniversalTime()
                }

                if ($requestedAtUtc -gt $lastProcessed) {
                    if (Invoke-GexSync) {
                        $state = Complete-GexSlot -State $state -SlotKey $null -Today $today
                        $state.lastProcessedRefreshRequestAtUtc = $requestedAtUtc.ToString('o')
                        Save-GexSchedulerState -State $state -Path $statePath
                    }
                    # On failure, requestedAtUtc is left unmarked so the same request keeps retrying
                    # every poll until it succeeds or the strategy supersedes it with a newer request.
                }
            }
        }
        catch {
            Write-Warning "GEX forced refresh request handling failed: $($_.Exception.Message)"
        }

        if ($RunOnce) { return }
        Start-Sleep -Seconds 60
    }
}
finally {
    $mutex.ReleaseMutex()
    $mutex.Dispose()
}
