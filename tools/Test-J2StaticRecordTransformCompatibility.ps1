[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$NativePwshPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$native = (Resolve-Path -LiteralPath $NativePwshPath).Path

function Invoke-Native {
    param([Parameter(Mandatory)][string]$Command)
    $output = (& $native -Command $Command 2>&1 | Out-String).TrimEnd()
    [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
}

function Require-Contains {
    param([Parameter(Mandatory)][string]$Text, [Parameter(Mandatory)][string]$Expected, [Parameter(Mandatory)][string]$Name)
    if (-not $Text.Contains($Expected, [StringComparison]::Ordinal)) {
        throw "$Name did not contain expected text: $Expected`nActual:`n$Text"
    }
}

# Eight controlled stock-oracle rows. Positive rows compare stable headers and
# values; reject/variance rows prove the documented boundary rather than
# pretending the differing engine mechanisms have byte-identical diagnostics.
$stockRows = @(
    @{ Id = 'J2-O01'; Command = 'Get-TimeZone -Id UTC | Select-Object Id'; Expected = 'UTC' },
    @{ Id = 'J2-O02'; Command = 'Get-TimeZone -Id UTC | Select-Object -Property Id'; Expected = 'UTC' },
    @{ Id = 'J2-O03'; Command = 'Get-TimeZone -Id UTC | Where-Object { $_.BaseUtcOffset.TotalMinutes -EQ 0 } | Select-Object Id'; Expected = 'UTC' },
    @{ Id = 'J2-O04'; Command = 'Get-TimeZone -Id UTC | Where-Object -Property BaseUtcOffset -EQ -Value ([TimeSpan]::Zero) | Select-Object -Property Id'; Expected = 'UTC' },
    @{ Id = 'J2-O05'; Command = 'Get-Culture | Select-Object Name'; Expected = [cultureinfo]::CurrentCulture.Name },
    @{ Id = 'J2-O06'; Command = 'Get-TimeZone -Id UTC | Select-Object Id -Property BaseUtcOffsetMinutes'; Expected = 'A positional parameter cannot be found' },
    @{ Id = 'J2-O07'; Command = 'Get-TimeZone -Id UTC | Select-Object -Property Id BaseUtcOffsetMinutes'; Expected = 'A positional parameter cannot be found' },
    @{ Id = 'J2-O08'; Command = 'Get-TimeZone -Id UTC | Select-Object I*'; Expected = 'UTC' }
)
foreach ($row in $stockRows) {
    $stock = (pwsh -NoProfile -Command $row.Command 2>&1 | Out-String).TrimEnd()
    Require-Contains -Text $stock -Expected $row.Expected -Name $row.Id
}

$positive = Invoke-Native 'Get-TimeZone -Id UTC | Where-Object BaseUtcOffsetMinutes -EQ 0 | Select-Object Id'
if ($positive.ExitCode -ne 0) { throw "J2-N02/J2-N03/J2-N04 failed with native exit code $($positive.ExitCode)." }
Require-Contains -Text $positive.Output -Expected 'Id' -Name 'J2-N02/J2-N03/J2-N04 header'
Require-Contains -Text $positive.Output -Expected 'UTC' -Name 'J2-N02/J2-N03/J2-N04 row'

foreach ($row in @(
    @{ Id = 'J2-N05a'; Command = 'Get-TimeZone -Id UTC | Select-Object Id -Property BaseUtcOffsetMinutes'; Column = 41 },
    @{ Id = 'J2-N05b'; Command = 'Get-TimeZone -Id UTC | Select-Object -Property Id BaseUtcOffsetMinutes'; Column = 51 },
    @{ Id = 'J2-N06'; Command = 'Where-Object CPU -GT 10'; Column = 1 }
)) {
    $result = Invoke-Native $row.Command
    Require-Contains -Text $result.Output -Expected 'error[AOT640' -Name $row.Id
    Require-Contains -Text $result.Output -Expected "--> <command>:1:$($row.Column)" -Name "$($row.Id) source span"
}

Write-Host 'J2 static record-transform stock/native compatibility checks passed.'
