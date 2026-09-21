[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PublishDirectory,

    [Parameter(Mandatory)]
    [string]$StagingDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$publishRoot = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($PublishDirectory)
$stageRoot = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($StagingDirectory)

if (-not (Test-Path -LiteralPath $publishRoot -PathType Container)) {
    throw "Publish directory '$publishRoot' does not exist."
}

if (Test-Path -LiteralPath $stageRoot) {
    throw "Staging directory '$stageRoot' already exists; refuse to overwrite a release candidate."
}

New-Item -ItemType Directory -Path $stageRoot | Out-Null
Get-ChildItem -LiteralPath $publishRoot -Force | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $stageRoot -Recurse -Force
}

foreach ($runtimeData in @('extensions', 'repositories')) {
    $source = Join-Path $repositoryRoot $runtimeData
    if (-not (Test-Path -LiteralPath $source -PathType Container)) {
        throw "Required runtime data directory '$source' is missing."
    }

    Copy-Item -LiteralPath $source -Destination (Join-Path $stageRoot $runtimeData) -Recurse -Force
}

foreach ($document in @('README.md', 'LICENSE')) {
    Copy-Item -LiteralPath (Join-Path $repositoryRoot $document) -Destination $stageRoot -Force
}

Write-Host "Staged release candidate at $stageRoot"
