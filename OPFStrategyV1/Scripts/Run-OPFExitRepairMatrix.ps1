param(
    [Parameter(Mandatory = $true)]
    [string]$H1EvidenceDirectory,

    [Parameter(Mandatory = $true)]
    [string]$Q4EvidenceDirectory,

    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$simulator = Join-Path $PSScriptRoot 'Simulate-OPFCounterfactual.ps1'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

$combinations = @(
    [pscustomobject]@{ Name = 'Baseline'; W = $false; O = $false; S = $false },
    [pscustomobject]@{ Name = 'W';        W = $true;  O = $false; S = $false },
    [pscustomobject]@{ Name = 'O';        W = $false; O = $true;  S = $false },
    [pscustomobject]@{ Name = 'S';        W = $false; O = $false; S = $true  },
    [pscustomobject]@{ Name = 'W_O';      W = $true;  O = $true;  S = $false },
    [pscustomobject]@{ Name = 'W_S';      W = $true;  O = $false; S = $true  },
    [pscustomobject]@{ Name = 'O_S';      W = $false; O = $true;  S = $true  },
    [pscustomobject]@{ Name = 'W_O_S';    W = $true;  O = $true;  S = $true  }
)
$periods = @(
    [pscustomobject]@{ Name = 'H1'; Directory = $H1EvidenceDirectory },
    [pscustomobject]@{ Name = 'Q4'; Directory = $Q4EvidenceDirectory }
)
$manifest = [Collections.Generic.List[object]]::new()

foreach ($combination in $combinations) {
    $overrides = @(
        if ($combination.W) { 'Long|ObservationConfirm_WideStop1_5R|Fixed2_5R' }
        if ($combination.O) { 'Short|ObservationConfirm|Protect1RAfter1_5R_Then2_5R' }
    ) -join ';'

    foreach ($period in $periods) {
        foreach ($mode in @('Observed', 'Conservative')) {
            $stem = '{0}_{1}_{2}' -f $combination.Name.ToLowerInvariant(), $period.Name.ToLowerInvariant(), $mode.ToLowerInvariant()
            $trades = Join-Path $OutputDirectory ($stem + '_trades.csv')
            $trace = Join-Path $OutputDirectory ($stem + '_trace.csv')
            $summary = Join-Path $OutputDirectory ($stem + '_summary.csv')
            $arguments = @{
                EvidenceDirectory = $period.Directory
                ShadowTargetMode = $mode
                OutputCsv = $trades
                TraceCsv = $trace
                SummaryCsv = $summary
            }
            if (-not [string]::IsNullOrWhiteSpace($overrides)) {
                $arguments.ExitPolicyOverrides = $overrides
            }
            if ($combination.S) {
                $arguments.ExcludeUsNegativeShortPaths = $true
            }

            & $simulator @arguments | Out-Null
            $manifest.Add([pscustomobject]@{
                Combination = $combination.Name
                W = $combination.W
                O = $combination.O
                S = $combination.S
                Period = $period.Name
                Mode = $mode
                ExitPolicyOverrides = $overrides
                TradesCsv = $trades
                TraceCsv = $trace
                SummaryCsv = $summary
            })
        }
    }
}

$manifest | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'matrix_manifest.csv') -NoTypeInformation -Encoding utf8
$manifest
