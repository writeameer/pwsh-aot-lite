[CmdletBinding()]
param(
    [switch]$Verify,
    [string]$GeneratedContractPath = (Join-Path $PSScriptRoot '../obj/Generated/PwshAotPortGenerator/PwshAotPortGenerator.PortManifestGenerator/GeneratedCmdletPorts.g.cs'),
    [string]$OutputPath = (Join-Path $PSScriptRoot '../docs/campaign/phase10-built-in-cmdlets.json')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-Classification {
    param([string]$Command, [string]$ClassName, [string]$Bases, [string]$SourcePath)

    $evidence = "$ClassName; base chain: $Bases; source: $SourcePath"
    $signal = "$Command $ClassName $Bases $SourcePath"

    # The only W2 candidates are an explicit reviewed calibration allowlist.
    # Identity evidence cannot safely promote any other declaration.
    if ($Command -in @('New-Guid', 'New-TimeSpan', 'Start-Sleep')) {
        return [pscustomobject]@{ Category = 'native-port-candidate'; Wave = 'W2'; Rationale = 'Reviewed low-authority calibration candidate; direct body/helper review remains mandatory before any adapter is added.'; Required = @('generated descriptor', 'typed record contract', 'direct body/helper review'); Evidence = $evidence }
    }

    if ($signal -match '(?i)Runspace|Debugger|Debug|Breakpoint|CallStack|StrictMode|Invoke-Expression|Add-Type|Register-ArgumentCompleter|Update-TypeData|Remove-TypeData|New-Object|PSSnapIn|Variable|Alias|History|Assembly|Provider') {
        return [pscustomobject]@{ Category = 'explicitly-unsupported'; Wave = 'deferred'; Rationale = 'The upstream behavior depends on dynamic engine, session/type manipulation, debugger, provider, or runtime code state with no approved static replacement.'; Required = @('none; catalog-only until a separately approved architecture exists'); Evidence = $evidence }
    }

    if ($signal -match '(?i)Cim|WSMan|PSSession|Remoting|ThreadJob|BackgroundJob|Job|Event|Subscription') {
        return [pscustomobject]@{ Category = 'sidecar-candidate'; Wave = 'W8'; Rationale = 'Remote/stateful execution must cross an explicit sidecar protocol rather than load the dynamic engine in-process.'; Required = @('sidecar protocol', 'trust policy', 'typed wire schema'); Evidence = $evidence }
    }

    if ($Command -match '(?i)Credential' -or $signal -match '(?i)Rest|Web|Mail|Connection|Dns|Cms|ExecutionPolicy|Certificate|Signature') {
        return [pscustomobject]@{ Category = 'shared-substrate-dependency'; Wave = 'W7'; Rationale = 'Credential, security, or network authority is explicitly unavailable today and requires a new approved trust design.'; Required = @('approved credential/network/security capability', 'trust/transport/error policy'); Evidence = $evidence }
    }

    if ($signal -match '(?i)Process|Service|Computer|Clipboard|Counter|HotFix|Acl|Host|Culture|TimeZone|Uptime|Date') {
        return [pscustomobject]@{ Category = 'shared-substrate-dependency'; Wave = 'W5'; Rationale = 'A narrow platform or host capability and an explicit OS/error matrix are required before this adapter can be honest.'; Required = @('AotHostSubstrate platform/process/host capability', 'OS/architecture/error matrix'); Evidence = $evidence }
    }

    if ($signal -match '(?i)CoreCommand|ContentCommand|ItemProperty|ItemCommand|Drive|Path|File|Directory|RecycleBin') {
        $isMutation = $Command -match '^(Add|Clear|Copy|Move|New|Remove|Rename|Set)-'
        return [pscustomobject]@{ Category = 'shared-substrate-dependency'; Wave = if ($isMutation) { 'W4' } else { 'W3' }; Rationale = if ($isMutation) { 'Physical filesystem mutation requires explicit ShouldProcess, confirmation, atomic-write, and trust policy.' } else { 'Physical filesystem read behavior requires a direct-path-only capability with provider modes rejected.' }; Required = if ($isMutation) { @('physical filesystem write capability', 'ShouldProcess/confirmation', 'atomic-write/trust policy') } else { @('physical filesystem read capability', 'direct-path/provider rejection matrix') }; Evidence = $evidence }
    }

    if ($signal -match '(?i)ForEach|Where|Select|Sort|Group|Measure|Compare|Object|Member|Format|Out-|Tee|Convert|Import|Export|Json|Csv|CliXml|String|Table|List|Wide|Unique|Error|Write-') {
        return [pscustomobject]@{ Category = 'native-replacement-required'; Wave = 'W1/W6'; Rationale = 'The behavior needs a reviewed typed data, conversion, serialization, or terminal-projection replacement instead of PSObject/ETS semantics.'; Required = @('typed value/pipeline or formatter contract'); Evidence = $evidence }
    }

    return [pscustomobject]@{ Category = 'native-replacement-required'; Wave = 'W1/W6'; Rationale = 'Identity-only evidence is insufficient to claim a native port candidate; require a reviewed typed replacement or direct-body reclassification.'; Required = @('typed value/pipeline contract', 'direct body/helper review'); Evidence = $evidence }
}

if (-not (Test-Path -LiteralPath $GeneratedContractPath)) {
    throw "Generated cmdlet contract was not found: $GeneratedContractPath. Run dotnet build first."
}

$contract = Get-Content -LiteralPath $GeneratedContractPath -Raw
$pattern = '(?ms)internal static SourceCmdletMetadata (?<property>\w+) \{ get; \} = new\(\s*"(?<command>(?:\\.|[^"\\])*)",\s*"(?<source>(?:\\.|[^"\\])*)",\s*"(?<class>(?:\\.|[^"\\])*)",\s*\[(?<bases>.*?)\],\s*new CmdletCapabilities'
$matches = [regex]::Matches($contract, $pattern)
if ($matches.Count -ne 290) {
    throw "Expected 290 generated cmdlet declarations; found $($matches.Count)."
}

$sourceRoot = Join-Path $PSScriptRoot '../.upstream/PowerShell'
$ordinals = @{}
$declarations = foreach ($match in $matches) {
    $command = [regex]::Unescape($match.Groups['command'].Value)
    $source = [regex]::Unescape($match.Groups['source'].Value).Replace('\', '/')
    $class = [regex]::Unescape($match.Groups['class'].Value)
    $bases = $match.Groups['bases'].Value
    $identityStem = "$source|$class"
    $ordinal = 1 + [int]($ordinals[$identityStem] ?? 0)
    $ordinals[$identityStem] = $ordinal
    $sourceFile = Join-Path $sourceRoot $source
    if (-not (Test-Path -LiteralPath $sourceFile)) {
        throw "Generated source identity no longer resolves: $source"
    }

    $classification = Get-Classification -Command $command -ClassName $class -Bases $bases -SourcePath $source
    [pscustomobject][ordered]@{
        declarationId = "$source|$class|$ordinal"
        command = $command
        className = $class
        sourcePath = $source
        sourceSha256 = (Get-FileHash -LiteralPath $sourceFile -Algorithm SHA256).Hash
        platformVariant = $null
        category = $classification.Category
        wave = $classification.Wave
        rationale = $classification.Rationale
        evidence = $classification.Evidence
        requiredPrerequisites = $classification.Required
        directBodyHelperReview = 'required-before-port'
        implementedSubset = $null
        currentState = if ($command -eq 'New-Guid') { 'integrated-reviewed-empty-switch subset; typed Guid input/output remains deferred' } elseif ($command -eq 'New-TimeSpan') { 'integrated-reviewed-components subset; date/positional/pipeline behavior remains deferred' } elseif ($command -in @('Get-Process', 'Get-Uptime', 'Get-UICulture', 'Get-Culture', 'Get-Verb', 'Get-TimeZone', 'Get-Date', 'Get-FileHash', 'Get-Help', 'Get-Command', 'Get-Module')) { 'existing-reviewed-adapter; reconcile before next port' } else { 'catalogued-only; no execution claim' }
    }
}

foreach ($entry in $declarations | Where-Object currentState -like 'existing-reviewed-adapter*') {
    $entry.directBodyHelperReview = 'completed-for-existing-subset; re-review before widening'
    $entry.implementedSubset = if ($entry.command -in @('Get-Help', 'Get-Command', 'Get-Module')) { 'Static metadata control-plane projection only; upstream live/session/module-path behavior remains unsupported.' } else { 'Reviewed current target subset; see port-variances.json and docs/cmdlets.' }
}

foreach ($entry in $declarations | Where-Object command -eq 'New-Guid') {
    $entry.directBodyHelperReview = 'reviewed-and-integrated-for-empty-switch subset'
    $entry.implementedSubset = 'Integrated: default UUID v7 and generated -Empty only; InputObject/positional/pipeline behavior remains rejected pending a typed Guid boundary.'
}

foreach ($entry in $declarations | Where-Object command -eq 'New-TimeSpan') {
    $entry.directBodyHelperReview = 'reviewed-and-integrated-for-time-components subset'
    $entry.implementedSubset = 'Integrated: no-argument zero duration and generated Days/Hours/Minutes/Seconds/Milliseconds components only; Start/LastWriteTime/End, positional, and pipeline DateTime behavior remain rejected pending a typed DateTime input boundary.'
}

$duplicateNames = $declarations | Group-Object command | Where-Object Count -gt 1
foreach ($group in $duplicateNames) {
    $variant = 0
    foreach ($entry in $declarations | Where-Object command -eq $group.Name) {
        $variant++
        $entry.platformVariant = "declaration-variant-$variant; inspect upstream conditional branch before port"
    }
}

$manifest = [pscustomobject][ordered]@{
    schemaVersion = 1
    generatedFrom = 'GeneratedCmdletPorts.g.cs after a fresh Release build'
    upstreamCommit = (& git -C $sourceRoot rev-parse HEAD).Trim()
    generatedContractSha256 = (Get-FileHash -LiteralPath $GeneratedContractPath -Algorithm SHA256).Hash
    classifierSha256 = (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash
    declarationCount = $declarations.Count
    logicalCommandCount = @($declarations.command | Sort-Object -Unique).Count
    categoryValues = @('native-port-candidate', 'shared-substrate-dependency', 'native-replacement-required', 'sidecar-candidate', 'explicitly-unsupported')
    declarations = @($declarations | Sort-Object command, sourcePath, className, declarationId)
}

function Test-Manifest {
    param($Value)
    if ($Value.declarationCount -ne 290 -or @($Value.declarations).Count -ne 290 -or @($Value.declarations.command | Sort-Object -Unique).Count -ne 288) {
        throw 'Campaign manifest cardinality regression.'
    }
    if (@($Value.declarations.declarationId | Sort-Object -Unique).Count -ne 290) {
        throw 'Campaign manifest declaration identities are not unique.'
    }
    if ($Value.upstreamCommit -notmatch '^[0-9a-f]{40}$' -or $Value.generatedContractSha256 -notmatch '^[0-9A-F]{64}$' -or $Value.classifierSha256 -notmatch '^[0-9A-F]{64}$') {
        throw 'Campaign manifest provenance is not immutable SHA evidence.'
    }
    if (@($Value.declarations | Where-Object { $_.category -notin $Value.categoryValues -or [string]::IsNullOrWhiteSpace($_.rationale) -or $_.requiredPrerequisites.Count -eq 0 -or [string]::IsNullOrWhiteSpace($_.directBodyHelperReview) }).Count -ne 0) {
        throw 'Campaign manifest contains an unclassified or unsupported row shape.'
    }
    $expectedLiteralNames = @('ForEach-Object', 'Invoke-CimMethod', 'Out-Null', 'Sort-Object', 'Tee-Object', 'Where-Object', 'Disable-PSRemoting', 'Enable-PSRemoting', 'Register-PSSessionConfiguration', 'Unregister-PSSessionConfiguration', 'Get-PSSessionConfiguration', 'Set-PSSessionConfiguration', 'Enable-PSSessionConfiguration', 'Disable-PSSessionConfiguration')
    if (@($expectedLiteralNames | Where-Object { $_ -notin $Value.declarations.command }).Count -ne 0 -or @($Value.declarations.command | Where-Object { $_ -match '"' }).Count -ne 0) {
        throw 'Cmdlet literal-verb extraction regression.'
    }
    $duplicateNames = @($Value.declarations | Group-Object command | Where-Object Count -gt 1 | Select-Object -ExpandProperty Name | Sort-Object)
    if ((@($duplicateNames) -join ',') -ne 'Restart-Computer,Stop-Computer' -or @($Value.declarations | Where-Object { $_.platformVariant }).Count -ne 4) {
        throw 'Expected two duplicated logical commands represented by four explicitly variant declarations.'
    }
    $candidateNames = @($Value.declarations | Where-Object category -eq 'native-port-candidate' | Select-Object -ExpandProperty command | Sort-Object -Unique)
    if (@($candidateNames) -join ',' -ne 'New-Guid,New-TimeSpan,Start-Sleep') {
        throw 'Only the reviewed W2 calibration allowlist may be a native-port candidate.'
    }
    $expectedBoundaries = @{
        'Get-Credential' = @('shared-substrate-dependency', 'W7')
        'Invoke-WebRequest' = @('shared-substrate-dependency', 'W7')
        'Set-Content' = @('shared-substrate-dependency', 'W4')
        'Get-ChildItem' = @('shared-substrate-dependency', 'W3')
        'Get-Process' = @('shared-substrate-dependency', 'W5')
        'Measure-Command' = @('native-replacement-required', 'W1/W6')
    }
    foreach ($command in $expectedBoundaries.Keys) {
        $row = @($Value.declarations | Where-Object command -eq $command)[0]
        if ($null -eq $row -or $row.category -ne $expectedBoundaries[$command][0] -or $row.wave -ne $expectedBoundaries[$command][1]) {
            throw "Campaign boundary classification regression for $command."
        }
    }
}

if ($Verify) {
    if (-not (Test-Path -LiteralPath $OutputPath)) {
        throw "Campaign manifest was not found: $OutputPath"
    }
    $checkedIn = Get-Content -LiteralPath $OutputPath -Raw | ConvertFrom-Json -Depth 16
    Test-Manifest $checkedIn
    $generatedJson = $manifest | ConvertTo-Json -Depth 16
    $checkedInJson = $checkedIn | ConvertTo-Json -Depth 16
    if ($generatedJson -ne $checkedInJson) {
        throw 'Campaign manifest is stale. Run tools/Export-Phase10Campaign.ps1 after reviewing source changes.'
    }
    Write-Host 'Phase 10 built-in cmdlet campaign manifest verified (290 declarations / 288 logical commands).'
    return
}

New-Item -ItemType Directory -Path (Split-Path -Parent $OutputPath) -Force | Out-Null
$manifest | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $OutputPath -NoNewline
Test-Manifest $manifest
Write-Host "Wrote $OutputPath (290 declarations / 288 logical commands)."
