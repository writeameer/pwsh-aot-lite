[CmdletBinding()]
param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot)
)

$projectPath = Join-Path $Root 'PwshAotLite.csproj'
$guardPath = Join-Path $Root 'PARSER-REUSE-GUARD.md'
$archivedProofPath = Join-Path $Root 'frontend-spike/UPSTREAM-ORIGIN.md'
$baselineToolPath = Join-Path $Root 'tools/Export-PwshParserBaseline.ps1'
$baselineFixturePath = Join-Path $Root 'tests/grammar/fixtures'
$baselineOutputPath = Join-Path $Root 'tests/grammar/baselines'

foreach ($path in @($projectPath, $guardPath, $archivedProofPath, $baselineToolPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Parser reuse guard requires '$path'."
    }
}

$project = Get-Content -LiteralPath $projectPath -Raw
if ($project -notmatch '<Compile Remove="frontend-spike/\*\*/\*\.cs"\s*/>') {
    throw 'The host must exclude the archived frontend-spike source files.'
}

if ($project -match '<ProjectReference[^>]*frontend-spike') {
    throw 'The archived frontend-spike must not be referenced by the host.'
}

$proofOrigin = Get-Content -LiteralPath $archivedProofPath -Raw
if ($proofOrigin -notmatch 'archived proof only') {
    throw 'The frontend proof must retain its archived-proof marker.'
}

foreach ($path in @($baselineFixturePath, $baselineOutputPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Container)) {
        throw "Parser reuse guard requires '$path'."
    }
}

$fixtures = @(Get-ChildItem -LiteralPath $baselineFixturePath -Filter '*.ps1' -File)
$baselines = @(Get-ChildItem -LiteralPath $baselineOutputPath -Filter '*.json' -File)
if ($fixtures.Count -lt 4 -or $baselines.Count -ne $fixtures.Count) {
    throw 'Parser reuse guard requires a checked-in baseline for every grammar fixture.'
}

foreach ($fixture in $fixtures) {
    $expectedBaselineName = [System.IO.Path]::ChangeExtension($fixture.Name, '.json')
    if (-not ($baselines.Name -contains $expectedBaselineName)) {
        throw "Parser reuse guard requires '$expectedBaselineName' for '$($fixture.Name)'."
    }
}

if (-not ($fixtures.Name -contains '04-malformed-pipe.ps1')) {
    throw 'Parser reuse guard requires a malformed-syntax diagnostic fixture.'
}

Write-Host 'Parser reuse guard passed.'
