[CmdletBinding(DefaultParameterSetName = 'Write')]
param(
    [string]$BaselineSourceRoot = (Join-Path $PSScriptRoot '../.upstream/PowerShell'),

    [string]$BaselineGeneratedContractPath = (Join-Path $PSScriptRoot '../obj/Generated/PwshAotPortGenerator/PwshAotPortGenerator.PortManifestGenerator/GeneratedCmdletPorts.g.cs'),

    [Parameter(Mandatory)]
    [string]$CandidateSourceRoot,

    [Parameter(Mandatory)]
    [string]$CandidateGeneratedContractPath,

    [string]$OutputPath,

    [Parameter(ParameterSetName = 'Write')]
    [switch]$Write,

    [Parameter(ParameterSetName = 'Verify', Mandatory)]
    [switch]$Verify
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-ExistingPath {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$Label)

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "$Label does not exist: $Path"
    }

    return (Resolve-Path -LiteralPath $Path).Path
}

function Get-VerifiedGitCommit {
    param([Parameter(Mandatory)][string]$SourceRoot, [Parameter(Mandatory)][string]$Label)

    if (-not (Test-Path -LiteralPath (Join-Path $SourceRoot '.git'))) {
        throw "$Label must be a Git checkout: $SourceRoot"
    }

    $commit = (& git -C $SourceRoot rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[0-9a-f]{40}$') {
        throw "Could not prove the $Label Git commit at '$SourceRoot'."
    }

    return $commit
}

function Get-FileSha256OrNull {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $null
    }

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

function Get-MetadataBlocks {
    param([Parameter(Mandatory)][string]$ContractPath)

    $contract = Get-Content -LiteralPath $ContractPath -Raw
    $startPattern = 'internal static SourceCmdletMetadata\s+(?<property>\w+)\s+\{\s*get;\s*\}\s*=\s*new\('
    $matches = [regex]::Matches($contract, $startPattern)
    if ($matches.Count -eq 0) {
        throw "No SourceCmdletMetadata declarations were found in '$ContractPath'."
    }

    $ordinals = @{}
    $entries = foreach ($match in $matches) {
        $index = $match.Index + $match.Length - 1
        $depth = 0
        $inString = $false
        $escaped = $false
        $end = -1
        for ($cursor = $index; $cursor -lt $contract.Length; $cursor++) {
            $character = $contract[$cursor]
            if ($inString) {
                if ($escaped) {
                    $escaped = $false
                }
                elseif ($character -eq '\\') {
                    $escaped = $true
                }
                elseif ($character -eq '"') {
                    $inString = $false
                }
                continue
            }

            if ($character -eq '"') {
                $inString = $true
                continue
            }
            if ($character -eq '(') {
                $depth++
                continue
            }
            if ($character -eq ')') {
                $depth--
                if ($depth -eq 0) {
                    $end = $cursor + 1
                    break
                }
            }
        }

        if ($end -lt 0) {
            throw "Unterminated SourceCmdletMetadata initializer for '$($match.Groups['property'].Value)' in '$ContractPath'."
        }

        $block = $contract.Substring($match.Index, $end - $match.Index)
        $identityMatch = [regex]::Match($block, '(?s)=\s*new\(\s*"(?<command>(?:\\.|[^"\\])*)"\s*,\s*"(?<source>(?:\\.|[^"\\])*)"\s*,\s*"(?<class>(?:\\.|[^"\\])*)"')
        if (-not $identityMatch.Success) {
            throw "Could not read SourceCmdletMetadata identity for '$($match.Groups['property'].Value)'."
        }

        $command = [regex]::Unescape($identityMatch.Groups['command'].Value)
        $source = [regex]::Unescape($identityMatch.Groups['source'].Value).Replace('\\', '/')
        $class = [regex]::Unescape($identityMatch.Groups['class'].Value)
        $stem = "$source|$class"
        $ordinal = 1 + [int]($ordinals[$stem] ?? 0)
        $ordinals[$stem] = $ordinal
        [pscustomobject][ordered]@{
            declarationId = "$source|$class|$ordinal"
            command = $command
            sourcePath = $source
            className = $class
            property = $match.Groups['property'].Value
            contractSha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($block)))
        }
    }

    return @($entries)
}

function Get-TreeFingerprint {
    param([Parameter(Mandatory)][string]$Root, [Parameter(Mandatory)][scriptblock]$Include)

    if (-not (Test-Path -LiteralPath $Root -PathType Container)) {
        return @()
    }

    return @(
        Get-ChildItem -LiteralPath $Root -Recurse -File |
            Where-Object $Include |
            ForEach-Object {
                [pscustomobject][ordered]@{
                    relativePath = [IO.Path]::GetRelativePath($Root, $_.FullName).Replace('\\', '/')
                    sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
                }
            } |
            Sort-Object relativePath
    )
}

function Get-ChangedTreePaths {
    param([object[]]$Baseline, [object[]]$Candidate)

    $baselineMap = @{}
    foreach ($entry in $Baseline) { $baselineMap[$entry.relativePath] = $entry.sha256 }
    $candidateMap = @{}
    foreach ($entry in $Candidate) { $candidateMap[$entry.relativePath] = $entry.sha256 }

    return @(
        @($baselineMap.Keys + $candidateMap.Keys | Sort-Object -Unique) |
            Where-Object { $baselineMap[$_] -ne $candidateMap[$_] }
    )
}

$BaselineSourceRoot = Resolve-ExistingPath -Path $BaselineSourceRoot -Label 'Baseline source root'
$CandidateSourceRoot = Resolve-ExistingPath -Path $CandidateSourceRoot -Label 'Candidate source root'
$BaselineGeneratedContractPath = Resolve-ExistingPath -Path $BaselineGeneratedContractPath -Label 'Baseline generated contract'
$CandidateGeneratedContractPath = Resolve-ExistingPath -Path $CandidateGeneratedContractPath -Label 'Candidate generated contract'

$baselineCommit = Get-VerifiedGitCommit -SourceRoot $BaselineSourceRoot -Label 'Baseline source root'
$candidateCommit = Get-VerifiedGitCommit -SourceRoot $CandidateSourceRoot -Label 'Candidate source root'
if ($baselineCommit -eq $candidateCommit) {
    throw 'Candidate and baseline resolve to the same commit; an intake comparison requires a distinct candidate commit.'
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $PSScriptRoot "../docs/upstream/intake/$baselineCommit-to-$candidateCommit.json"
}

$baselineEntries = Get-MetadataBlocks -ContractPath $BaselineGeneratedContractPath
$candidateEntries = Get-MetadataBlocks -ContractPath $CandidateGeneratedContractPath
$baselineMap = @{}
foreach ($entry in $baselineEntries) { $baselineMap[$entry.declarationId] = $entry }
$candidateMap = @{}
foreach ($entry in $candidateEntries) { $candidateMap[$entry.declarationId] = $entry }

$formatInclude = { $_.FullName -match '(?i)(?:^|[\\/])(FormatAndOutput|TypeTable)(?:[\\/]|$)|\.(?:format|types)\.ps1xml$' }
$baselineFormat = Get-TreeFingerprint -Root $BaselineSourceRoot -Include $formatInclude
$candidateFormat = Get-TreeFingerprint -Root $CandidateSourceRoot -Include $formatInclude
$changedFormatPaths = @(Get-ChangedTreePaths -Baseline $baselineFormat -Candidate $candidateFormat)

$rows = [Collections.Generic.List[object]]::new()
foreach ($declarationId in @($baselineMap.Keys + $candidateMap.Keys | Sort-Object -Unique)) {
    $baseline = $baselineMap[$declarationId]
    $candidate = $candidateMap[$declarationId]
    $signals = [Collections.Generic.List[string]]::new()
    $reasons = [Collections.Generic.List[string]]::new()
    $sourcePath = if ($null -ne $candidate) { $candidate.sourcePath } else { $baseline.sourcePath }
    $baselineSourceFile = Join-Path $BaselineSourceRoot $sourcePath
    $candidateSourceFile = Join-Path $CandidateSourceRoot $sourcePath
    $baselineSourceSha = Get-FileSha256OrNull -Path $baselineSourceFile
    $candidateSourceSha = Get-FileSha256OrNull -Path $candidateSourceFile

    if ($null -eq $baseline -or $null -eq $candidate) {
        $signals.Add('metadata')
        if ($null -eq $baseline) {
            $reasons.Add('declaration added')
        }
        else {
            $reasons.Add('declaration removed')
        }
    }
    elseif ($baseline.contractSha256 -ne $candidate.contractSha256 -or $baseline.property -ne $candidate.property) {
        $signals.Add('metadata')
        $reasons.Add('generated SourceCmdletMetadata block changed')
    }
    if ($baselineSourceSha -ne $candidateSourceSha) {
        $signals.Add('body')
        $reasons.Add('declaring source file hash changed')
    }

    $sourceText = @($baselineSourceFile, $candidateSourceFile) |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
        ForEach-Object { Get-Content -LiteralPath $_ -Raw }
    if ($sourceText -match '(?i)(?:FormatTable|TableControl|ViewDefinitions|TypeTable|\.format\.ps1xml|\.types\.ps1xml)') {
        $signals.Add('format')
        $reasons.Add('declaring source has format/type-data seam signal')
    }
    if ($sourceText -match '(?i)(?:Provider|SessionState|PSDrive|PathIntrinsics|ItemCmdletProvider|NavigationCmdletProvider)') {
        $signals.Add('provider-seam')
        $reasons.Add('declaring source has provider/session/path seam signal')
    }

    if ($signals.Count -gt 0) {
        $rows.Add([pscustomobject][ordered]@{
            declarationId = $declarationId
            command = if ($null -ne $candidate) { $candidate.command } else { $baseline.command }
            sourcePath = $sourcePath
            signals = @($signals | Sort-Object -Unique)
            reasons = @($reasons | Sort-Object -Unique)
            baseline = if ($null -eq $baseline) { $null } else { [pscustomobject]@{ contractSha256 = $baseline.contractSha256; sourceSha256 = $baselineSourceSha } }
            candidate = if ($null -eq $candidate) { $null } else { [pscustomobject]@{ contractSha256 = $candidate.contractSha256; sourceSha256 = $candidateSourceSha } }
        })
    }
}

if ($changedFormatPaths.Count -gt 0) {
    $rows.Add([pscustomobject][ordered]@{
        declarationId = '__format-system__'
        command = '__format-system__'
        sourcePath = 'src format/type-data tree'
        signals = @('format')
        reasons = @('format/type-data source tree changed')
        baseline = $null
        candidate = $null
        changedPaths = $changedFormatPaths
    })
}

$report = [pscustomobject][ordered]@{
    schemaVersion = 1
    purpose = 'Conservative static review triggers for a proposed PowerShell upstream bump; this report is not an approval or execution claim.'
    baseline = [pscustomobject][ordered]@{
        commit = $baselineCommit
        sourceRoot = $BaselineSourceRoot
        generatedContractSha256 = Get-FileSha256OrNull -Path $BaselineGeneratedContractPath
        declarationCount = $baselineEntries.Count
    }
    candidate = [pscustomobject][ordered]@{
        commit = $candidateCommit
        sourceRoot = $CandidateSourceRoot
        generatedContractSha256 = Get-FileSha256OrNull -Path $CandidateGeneratedContractPath
        declarationCount = $candidateEntries.Count
    }
    classifierSha256 = Get-FileSha256OrNull -Path $PSCommandPath
    changedFormatPaths = $changedFormatPaths
    changedDeclarationCount = $rows.Count
    reviewRows = @($rows | Sort-Object command, sourcePath, declarationId)
}

if ($Verify) {
    $OutputPath = Resolve-ExistingPath -Path $OutputPath -Label 'Existing intake report'
    $expected = (Get-Content -LiteralPath $OutputPath -Raw | ConvertFrom-Json -Depth 32) | ConvertTo-Json -Depth 32 -Compress
    $actual = ($report | ConvertTo-Json -Depth 32 | ConvertFrom-Json -Depth 32) | ConvertTo-Json -Depth 32 -Compress
    if ($expected -ne $actual) {
        throw "Upstream intake report is stale: $OutputPath. Regenerate it after reviewing the candidate diff."
    }
    Write-Host "Upstream intake report verified: $baselineCommit -> $candidateCommit ($($rows.Count) review rows)."
    return
}

$directory = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Path $directory -Force | Out-Null
$report | ConvertTo-Json -Depth 32 | Set-Content -LiteralPath $OutputPath -Encoding utf8NoBOM
Write-Host "Wrote upstream intake report: $OutputPath ($($rows.Count) review rows)."
