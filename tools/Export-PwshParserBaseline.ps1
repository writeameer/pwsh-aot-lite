[CmdletBinding(DefaultParameterSetName = 'Export')]
param(
    [string]$FixtureRoot = (Join-Path (Split-Path -Parent $PSScriptRoot) 'tests/grammar/fixtures'),

    [string]$OutputRoot = (Join-Path (Split-Path -Parent $PSScriptRoot) 'tests/grammar/baselines'),

    [Parameter(ParameterSetName = 'Export')]
    [switch]$Write,

    [Parameter(ParameterSetName = 'Verify', Mandatory)]
    [switch]$Verify
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# This is deliberately a stock-pwsh oracle, not part of the Native AOT host.
# It captures the public Language parser output without invoking the script under
# test. A future pinned upstream language extraction consumes these JSON files
# and compares its own tokens, AST shape, and parse diagnostics against them.
if (-not ('PwshAotLite.ParserBaselineVisitor' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Management.Automation.Language;

namespace PwshAotLite
{
    public sealed class ParserBaselineVisitor : AstVisitor
    {
        public readonly List<Ast> Nodes = new List<Ast>();

        public override AstVisitAction DefaultVisit(Ast ast)
        {
            Nodes.Add(ast);
            return AstVisitAction.Continue;
        }
    }
}
'@
}

function Get-ExtentShape {
    param([Parameter(Mandatory)][System.Management.Automation.Language.IScriptExtent]$Extent)

    return [ordered]@{
        startOffset = $Extent.StartOffset
        endOffset = $Extent.EndOffset
        startLine = $Extent.StartLineNumber
        startColumn = $Extent.StartColumnNumber
        endLine = $Extent.EndLineNumber
        endColumn = $Extent.EndColumnNumber
    }
}

function Get-NodeShape {
    param(
        [Parameter(Mandatory)][System.Management.Automation.Language.Ast]$Node,
        [Parameter(Mandatory)][System.Management.Automation.Language.Ast[]]$AllNodes
    )

    # Ast.Parent is public. Using it rather than reconstructing hierarchy from
    # source spans preserves nodes with identical extents, such as ScriptBlock
    # and NamedBlock.
    $children = @(
        $AllNodes |
            Where-Object { $_.Parent -eq $Node } |
            ForEach-Object { Get-NodeShape -Node $_ -AllNodes $AllNodes }
    )

    return [ordered]@{
        type = $Node.GetType().Name
        extent = Get-ExtentShape -Extent $Node.Extent
        children = $children
    }
}

function Get-ParserBaseline {
    param([Parameter(Mandatory)][System.IO.FileInfo]$Fixture)

    $tokens = $null
    $parseErrors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile(
        $Fixture.FullName,
        [ref]$tokens,
        [ref]$parseErrors)

    $visitor = [PwshAotLite.ParserBaselineVisitor]::new()
    $null = $ast.Visit($visitor)
    $allNodes = [System.Management.Automation.Language.Ast[]]$visitor.Nodes

    $tokenShapes = @(
        foreach ($token in $tokens) {
            [ordered]@{
                kind = $token.Kind.ToString()
                text = $token.Text
                flags = $token.TokenFlags.ToString()
                extent = Get-ExtentShape -Extent $token.Extent
            }
        }
    )

    $diagnosticShapes = @(
        foreach ($parseError in $parseErrors) {
            [ordered]@{
                id = $parseError.ErrorId
                extent = Get-ExtentShape -Extent $parseError.Extent
            }
        }
    )

    return [ordered]@{
        schemaVersion = 1
        fixture = $Fixture.Name
        sourceSha256 = (Get-FileHash -LiteralPath $Fixture.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        tokens = $tokenShapes
        ast = Get-NodeShape -Node $ast -AllNodes $allNodes
        diagnostics = $diagnosticShapes
    }
}

if (-not (Test-Path -LiteralPath $FixtureRoot -PathType Container)) {
    throw "Fixture root '$FixtureRoot' does not exist."
}

$fixtures = @(
    Get-ChildItem -LiteralPath $FixtureRoot -Filter '*.ps1' -File |
        Sort-Object -Property Name
)

if ($fixtures.Count -eq 0) {
    throw "Fixture root '$FixtureRoot' contains no .ps1 files."
}

if ($Write) {
    $null = New-Item -ItemType Directory -Path $OutputRoot -Force
}

$failures = [System.Collections.Generic.List[string]]::new()
foreach ($fixture in $fixtures) {
    $baseline = Get-ParserBaseline -Fixture $fixture
    $json = $baseline | ConvertTo-Json -Depth 100
    $outputPath = Join-Path $OutputRoot ($fixture.BaseName + '.json')

    if ($Verify) {
        if (-not (Test-Path -LiteralPath $outputPath -PathType Leaf)) {
            $failures.Add("missing baseline: $outputPath")
            continue
        }

        $expected = (Get-Content -LiteralPath $outputPath -Raw | ConvertFrom-Json) | ConvertTo-Json -Depth 100 -Compress
        $actual = ($json | ConvertFrom-Json) | ConvertTo-Json -Depth 100 -Compress
        if ($expected -ne $actual) {
            $failures.Add("baseline differs: $($fixture.Name)")
        }
    }
    elseif ($Write) {
        Set-Content -LiteralPath $outputPath -Value $json -Encoding utf8NoBOM
        Write-Host "Wrote $outputPath"
    }
    else {
        Write-Output $json
    }
}

if ($Verify) {
    if ($failures.Count -gt 0) {
        throw ("PowerShell parser differential baseline verification failed:`n - " + ($failures -join "`n - "))
    }

    Write-Host "PowerShell parser differential baselines verified ($($fixtures.Count) fixtures)."
}
