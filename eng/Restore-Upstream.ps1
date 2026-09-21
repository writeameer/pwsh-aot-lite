[CmdletBinding()]
param(
    [string]$Destination = (Join-Path (Split-Path -Parent $PSScriptRoot) '.upstream/PowerShell')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$configurationPath = Join-Path $PSScriptRoot 'upstream-powershell.env'
if (-not (Test-Path -LiteralPath $configurationPath -PathType Leaf)) {
    throw "Missing upstream configuration '$configurationPath'."
}

$configuration = Get-Content -LiteralPath $configurationPath |
    Where-Object { $_ -match '^[A-Za-z][A-Za-z0-9]*=' } |
    ForEach-Object {
        $parts = $_ -split '=', 2
        [pscustomobject]@{ Name = $parts[0]; Value = $parts[1] }
    } |
    Group-Object -AsHashTable -AsString -Property Name

$repository = $configuration['PowerShellUpstreamRepository'].Value
$commit = $configuration['PowerShellUpstreamCommit'].Value
if ([string]::IsNullOrWhiteSpace($repository) -or $commit -notmatch '^[0-9a-f]{40}$') {
    throw "Invalid upstream configuration '$configurationPath'."
}

if (Test-Path -LiteralPath $Destination) {
    if (-not (Test-Path -LiteralPath (Join-Path $Destination '.git'))) {
        throw "Refusing to use existing non-Git directory '$Destination'."
    }
}
else {
    $parent = Split-Path -Parent $Destination
    New-Item -ItemType Directory -Force -Path $parent | Out-Null
    & git clone --filter=blob:none --no-checkout $repository $Destination
    if ($LASTEXITCODE -ne 0) {
        throw "Could not clone PowerShell upstream into '$Destination'."
    }
}

& git -C $Destination fetch --depth 1 origin $commit
if ($LASTEXITCODE -ne 0) {
    throw "Could not fetch pinned PowerShell commit '$commit'."
}

& git -C $Destination checkout --detach $commit
if ($LASTEXITCODE -ne 0) {
    throw "Could not check out pinned PowerShell commit '$commit'."
}

$actualCommit = (& git -C $Destination rev-parse HEAD).Trim()
if ($actualCommit -ne $commit) {
    throw "Pinned PowerShell checkout mismatch: expected '$commit', got '$actualCommit'."
}

Write-Host "Restored PowerShell upstream $actualCommit to $Destination"
