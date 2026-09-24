[CmdletBinding()]
param(
    [switch] $Verify,
    [string] $ManifestPath = (Join-Path $PSScriptRoot '../docs/campaign/phase10-built-in-cmdlets.json'),
    [string] $OutJson = (Join-Path $PSScriptRoot '../docs/campaign/phase10-batch-queue.json'),
    [string] $OutMarkdown = (Join-Path $PSScriptRoot '../docs/campaign/phase10-batch-queue.md')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# This is a scheduling queue, not a compatibility claim or a priority over a
# missing prerequisite.  Only these explicitly reviewed dependencies reorder
# the authoritative Phase 10 manifest.  Everything else remains in its source
# catalog category/wave and is sorted deterministically.
$alreadyProven = @(
    'Get-ChildItem',
    'Get-FileHash',
    'New-Guid',
    'New-TimeSpan',
    'Start-Sleep'
)

# Completed work remains visible in the queue, but its accounting class must
# remain honest.  The five calibration ports predate this campaign and belong
# to B00; commands completed while executing this campaign are recorded in a
# distinct, immutable integration batch instead of being retroactively called
# a baseline.
$integratedDuringCampaign = @(
    'Get-Item',
    'Test-Path',
    'Resolve-Path',
    'Convert-Path'
)

# B02 is retained as a stable, partially complete batch: the two completed J1
# lexical adapters stay with their original cohort rather than being moved into
# B01 retroactively. This preserves the historical schedule while keeping all
# current queue counts deterministic.
$partiallyCompletedBatchId = 'B02'
$partiallyCompletedCommands = @(
    'Join-Path',
    'Split-Path'
)

# The remaining front of the queue is the direct physical-filesystem cluster.
# Its first commands consume the captured-root/path seams proven by
# Get-ChildItem, Get-Item, Test-Path, and Resolve-Path. The JSON group remains
# deliberately pulled forward as W3a. Its J0 codec prerequisite is integrated;
# each JSON command adapter still has independent lifecycle/oracle gates.
$frontOfQueue = @(
    'Join-Path', 'Split-Path', 'Get-Content', 'Get-ItemProperty',
    'Get-ItemPropertyValue', 'Test-FileCatalog', 'ConvertFrom-Json',
    'ConvertTo-Json', 'Test-Json', 'Get-Location'
)

$queueOverrides = @{
    'Get-Item' = 'Reuses the proven direct physical-entry catalog and captured-root path policy.'
    'Test-Path' = 'Reuses the proven direct physical path resolver and captured-root policy.'
    'Resolve-Path' = 'Reuses the proven direct physical path resolver and captured-root policy.'
    'Convert-Path' = 'Reuses the proven direct physical path resolver and captured-root policy.'
    'Join-Path' = 'Reuses the direct physical path grammar/policy; no provider drive semantics are admitted.'
    'Split-Path' = 'Reuses the direct physical path grammar/policy; no provider drive semantics are admitted.'
    'Get-Content' = 'Extends the proven physical-file resolver with a reviewed bounded read/content seam.'
    'Get-ItemProperty' = 'Extends the physical-entry record through a reviewed static metadata projection.'
    'Get-ItemPropertyValue' = 'Depends on the Get-ItemProperty static metadata projection.'
    'Test-FileCatalog' = 'Extends the proven physical-file resolver/hash work with a reviewed catalog-validation seam.'
    'ConvertFrom-Json' = 'W3a pull-forward: J0 closed System.Text.Json-to-AotValue codec is integrated at aff09b3; the command adapter remains separately gated and admits no PSObject materialization.'
    'ConvertTo-Json' = 'W3a pull-forward: J0 closed AotValue-to-System.Text.Json codec is integrated at aff09b3; the command adapter remains separately gated and admits no arbitrary CLR serialization.'
    'Test-Json' = 'W3a pull-forward: J0 validation/diagnostic contract is integrated at aff09b3; Test-Json still requires its own static validation surface and oracle.'
}

$effectivePrerequisites = @{
    'ConvertFrom-Json' = @('J0 closed System.Text.Json-to-AotValue codec (integrated aff09b3)', 'separate generated descriptor, lifecycle/binding, output, and stock/native oracle gates')
    'ConvertTo-Json' = @('J0 closed AotValue-to-System.Text.Json codec (integrated aff09b3)', 'separate generated descriptor, lifecycle/binding, output, and stock/native oracle gates')
    'Test-Json' = @('J0 closed JSON validation and diagnostic contract (integrated aff09b3)', 'separate static validation surface and stock/native oracle gates')
}

$waveOrder = @{
# B02 preserves the remaining direct physical-filesystem dependency cluster.
    # The W1/W6 replacement backlog follows it; individual W1 work still
    # cannot proceed before its declared typed-data/formatter prerequisite.
    'W3' = 20
    'W1/W6' = 30
    'W4' = 40
    'W5' = 50
    'W7' = 60
    'W8' = 70
    'deferred' = 80
    'W2' = 90
}

function Get-OutcomeState([string] $category, [bool] $proven) {
    if ($proven) { return 'native-subset-proven' }

    switch ($category) {
        'native-port-candidate' { return 'native-subset' }
        'shared-substrate-dependency' { return 'native-subset-or-extend-shared-seam' }
        'native-replacement-required' { return 'extend-reviewed-static-seam-or-native-subset' }
        'sidecar-candidate' { return 'sidecar-candidate-only' }
        'explicitly-unsupported' { return 'explicitly-unsupported' }
        default { throw "Unknown Phase 10 category '$category'." }
    }
}

function Get-QueueRank([string] $command, [string] $wave) {
    $priorityIndex = [array]::IndexOf($frontOfQueue, $command)
    if ($priorityIndex -ge 0) { return $priorityIndex }

    if (-not $waveOrder.ContainsKey($wave)) {
        throw "Unknown Phase 10 wave '$wave' for '$command'."
    }

    return 1000 + [int]$waveOrder[$wave]
}

function New-QueuedRow($row) {
    $override = $queueOverrides[$row.command]
    return [pscustomobject][ordered]@{
        command = $row.command
        category = $row.category
        sourceWave = $row.sourceWave
        queueWave = if ($null -ne $override -and $row.command -match 'Json') { 'W3a' } else { $row.sourceWave }
        sourceRequiredPrerequisites = $row.requiredPrerequisites
        requiredPrerequisites = if ($effectivePrerequisites.ContainsKey($row.command)) { @($effectivePrerequisites[$row.command]) } else { $row.requiredPrerequisites }
        intendedOutcomeState = Get-OutcomeState $row.category $false
        status = 'queued'
        queueRationale = if ($null -ne $override) { $override } else { 'Manifest order: execute only after the listed prerequisite is implemented and independently reviewed.' }
        declarationIds = $row.declarationIds
        sourcePaths = $row.sourcePaths
        platformVariants = $row.platformVariants
    }
}

function ConvertTo-MarkdownCell([object] $value) {
    return ([string]$value).Replace('|', '\\|').Replace("`r", ' ').Replace("`n", '<br>')
}

if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
    throw "Phase 10 manifest was not found: $ManifestPath"
}

$manifestFullPath = (Resolve-Path -LiteralPath $ManifestPath).Path
$manifest = Get-Content -LiteralPath $manifestFullPath -Raw | ConvertFrom-Json
if ($manifest.logicalCommandCount -ne 288) {
    throw "Expected the verified 288-logical-command Phase 10 manifest; found '$($manifest.logicalCommandCount)'."
}

$logical = @(
    $manifest.declarations |
        Group-Object -Property command |
        ForEach-Object {
            $rows = @($_.Group | Sort-Object declarationId)
            $categories = @($rows.category | Sort-Object -Unique)
            $waves = @($rows.wave | Sort-Object -Unique)
            if ($categories.Count -ne 1 -or $waves.Count -ne 1) {
                throw "Logical command '$($_.Name)' has incompatible category/wave declarations and needs an explicit planning decision."
            }

            [pscustomobject][ordered]@{
                command = $_.Name
                category = [string]$categories[0]
                sourceWave = [string]$waves[0]
                requiredPrerequisites = @($rows.requiredPrerequisites | ForEach-Object { $_ } | Sort-Object -Unique)
                declarationIds = @($rows.declarationId)
                sourcePaths = @($rows.sourcePath | Sort-Object -Unique)
                platformVariants = @($rows.platformVariant | Where-Object { $null -ne $_ } | Sort-Object -Unique)
            }
        }
)

if ($logical.Count -ne $manifest.logicalCommandCount) {
    throw "Manifest uniqueness regression: expected $($manifest.logicalCommandCount) logical commands but found $($logical.Count)."
}

$logicalByName = @{}
foreach ($item in $logical) { $logicalByName[$item.command] = $item }
foreach ($command in ($alreadyProven + $integratedDuringCampaign + $frontOfQueue | Sort-Object -Unique)) {
    if (-not $logicalByName.ContainsKey($command)) {
        throw "Queue plan references '$command', which is absent from the verified manifest."
    }
}

$provenRows = @(
    foreach ($command in $alreadyProven) {
        $row = $logicalByName[$command]
        [pscustomobject][ordered]@{
            command = $row.command
            category = $row.category
            sourceWave = $row.sourceWave
            queueWave = 'baseline'
            requiredPrerequisites = @('already integrated and native-verified; retain its existing per-cmdlet evidence')
            intendedOutcomeState = Get-OutcomeState $row.category $true
            status = 'completed-proven'
            queueRationale = 'Calibration/proof command retained for accounting only; it is not scheduled for duplicate implementation.'
            declarationIds = $row.declarationIds
            sourcePaths = $row.sourcePaths
            platformVariants = $row.platformVariants
        }
    }
)

$integratedRows = @(
    foreach ($command in $integratedDuringCampaign) {
        $row = $logicalByName[$command]
        [pscustomobject][ordered]@{
            command = $row.command
            category = $row.category
            sourceWave = $row.sourceWave
            queueWave = $row.sourceWave
            requiredPrerequisites = @('integrated after its recorded source-reuse, architecture, diagnostics, managed, parser, and fresh Native AOT gates; retain its per-cmdlet evidence')
            intendedOutcomeState = Get-OutcomeState $row.category $true
            status = 'completed-integrated'
            queueRationale = 'Campaign command completed as a bounded native subset; it is retained for exact accounting and is not scheduled again.'
            declarationIds = $row.declarationIds
            sourcePaths = $row.sourcePaths
            platformVariants = $row.platformVariants
        }
    }
)

$partiallyCompletedRows = @(
    foreach ($command in $frontOfQueue) {
        $row = $logicalByName[$command]
        if ($partiallyCompletedCommands -contains $command) {
            [pscustomobject][ordered]@{
                command = $row.command
                category = $row.category
                sourceWave = $row.sourceWave
                queueWave = $row.sourceWave
                requiredPrerequisites = @('integrated after its recorded source-reuse, architecture, diagnostics, managed, parser, and fresh Native AOT gates; retain its per-cmdlet evidence')
                intendedOutcomeState = Get-OutcomeState $row.category $true
                status = 'completed-integrated'
                queueRationale = 'Campaign command completed as a bounded native subset; it remains in B02 for exact historical batch accounting.'
                declarationIds = $row.declarationIds
                sourcePaths = $row.sourcePaths
                platformVariants = $row.platformVariants
            }
        }
        else {
            New-QueuedRow $row
        }
    }
)

$remaining = @(
    $logical |
        Where-Object { $alreadyProven -notcontains $_.command -and $integratedDuringCampaign -notcontains $_.command -and $frontOfQueue -notcontains $_.command } |
        Sort-Object @{ Expression = { Get-QueueRank $_.command $_.sourceWave } }, command |
        ForEach-Object { New-QueuedRow $_ }
)

$batches = [System.Collections.Generic.List[object]]::new()
$batches.Add([pscustomobject][ordered]@{
    batchId = 'B00'
    kind = 'completed-baseline'
    commandCount = $provenRows.Count
    completedCommandCount = $provenRows.Count
    queuedCommandCount = 0
    commands = $provenRows
})

$batches.Add([pscustomobject][ordered]@{
    batchId = 'B01'
    kind = 'completed-integrated'
    commandCount = $integratedRows.Count
    completedCommandCount = $integratedRows.Count
    queuedCommandCount = 0
    commands = $integratedRows
})

$batches.Add([pscustomobject][ordered]@{
    batchId = $partiallyCompletedBatchId
    kind = 'partially-complete'
    commandCount = $partiallyCompletedRows.Count
    completedCommandCount = @($partiallyCompletedRows | Where-Object status -eq 'completed-integrated').Count
    queuedCommandCount = @($partiallyCompletedRows | Where-Object status -eq 'queued').Count
    commands = $partiallyCompletedRows
})

$batchNumber = 3
for ($offset = 0; $offset -lt $remaining.Count; $offset += 10) {
    $batchRows = @($remaining | Select-Object -Skip $offset -First 10)
    $batches.Add([pscustomobject][ordered]@{
        batchId = ('B{0:d2}' -f $batchNumber)
        kind = 'queued'
        commandCount = $batchRows.Count
        completedCommandCount = 0
        queuedCommandCount = $batchRows.Count
        commands = $batchRows
    })
    $batchNumber++
}

$flat = @($batches | ForEach-Object { $_.commands } | ForEach-Object { $_ })
if ($flat.Count -ne $manifest.logicalCommandCount) {
    throw "Queue accounting regression: expected $($manifest.logicalCommandCount) commands, found $($flat.Count)."
}
if ((@($flat.command | Sort-Object -Unique)).Count -ne $manifest.logicalCommandCount) {
    throw 'Queue accounting regression: a logical command appears more than once.'
}
foreach ($batch in @($batches | Where-Object { $_.kind -eq 'queued' })) {
    if ($batch.commandCount -ne 10 -and $batch.batchId -ne $batches[$batches.Count - 1].batchId) {
        throw "Only the final queued batch may be smaller than ten commands; '$($batch.batchId)' has $($batch.commandCount)."
    }
}
if ($partiallyCompletedRows.Count -ne 10 -or $batches[2].completedCommandCount -ne 2 -or $batches[2].queuedCommandCount -ne 8) {
    throw 'B02 partial-completion accounting regression.'
}

$manifestHash = (Get-FileHash -LiteralPath $manifestFullPath -Algorithm SHA256).Hash
$completedCommandCount = @($flat | Where-Object status -like 'completed-*').Count
$queuedCommandCount = @($flat | Where-Object status -eq 'queued').Count
$queuedBatchCount = @($batches | Where-Object queuedCommandCount -gt 0).Count
$queue = [pscustomobject][ordered]@{
    schemaVersion = 1
    generatedBy = 'tools/Export-Phase10BatchQueue.ps1'
    manifest = [pscustomobject][ordered]@{
        path = 'docs/campaign/phase10-built-in-cmdlets.json'
        sha256 = $manifestHash
        upstreamCommit = $manifest.upstreamCommit
        declarationCount = $manifest.declarationCount
        logicalCommandCount = $manifest.logicalCommandCount
    }
    batchSize = 10
    completedCommandCount = $completedCommandCount
    completedBaselineCommandCount = $provenRows.Count
    completedIntegratedCommandCount = $completedCommandCount - $provenRows.Count
    queuedCommandCount = $queuedCommandCount
    queuedBatchCount = $queuedBatchCount
    batches = @($batches)
}

$json = $queue | ConvertTo-Json -Depth 12
$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add('# Phase 10 deterministic batch queue')
$lines.Add('')
$lines.Add('This queue accounts for every logical command in the checked Phase 10 manifest exactly once. It is a dependency-aware migration schedule, **not** a compatibility claim: a command proceeds only when its listed prerequisite exists and has passed the normal source-reuse, architecture, diagnostic, managed, parser, and fresh Native AOT gates.')
$lines.Add('')
$lines.Add(('- Authority: [`phase10-built-in-cmdlets.json`](phase10-built-in-cmdlets.json), SHA-256 `{0}`.' -f $manifestHash))
$lines.Add("- Accounting: $($manifest.logicalCommandCount) logical commands; $completedCommandCount completed commands ($($provenRows.Count) baseline and $($completedCommandCount - $provenRows.Count) integrated during this campaign); $queuedCommandCount queued commands in $queuedBatchCount batches with queued work (final queued batch may be smaller).")
$lines.Add('- `B00` is accounting-only: previously verified calibration ports are not scheduled again. `B01` records the four campaign commands completed before J1. `B02` is deliberately retained as a **partially complete** historical batch: `Join-Path` and `Split-Path` are integrated; its eight remaining commands, including pulled-forward JSON rows as `W3a`, remain separately gated.')
$lines.Add('- Outcomes are explicit: native subset, shared-seam extension, sidecar candidate, or explicitly unsupported. An unsupported parameter/path within an otherwise useful native subset is a successful bounded conversion, not a silent compatibility claim.')
$lines.Add('- The [engineering archetype inventory](phase10-archetype-inventory.md) explains the current 277-command unconverted cohort and its original 279-command survey baseline. `missing-profile` means no exact compiler profile, not that every command has the same blocker.')
$lines.Add('')
$lines.Add('Regenerate or verify this queue:')
$lines.Add('')
$lines.Add('```powershell')
$lines.Add('pwsh -NoProfile -File ./tools/Export-Phase10Campaign.ps1 -Verify')
$lines.Add('pwsh -NoProfile -File ./tools/Export-Phase10BatchQueue.ps1 -Verify')
$lines.Add('```')

foreach ($batch in $batches) {
    $commandNoun = if ($batch.commandCount -eq 1) { 'command' } else { 'commands' }
    $lines.Add('')
    $batchProgress = if ($batch.kind -eq 'partially-complete') { "; $($batch.completedCommandCount) complete, $($batch.queuedCommandCount) queued" } else { '' }
    $lines.Add("## $($batch.batchId) — $($batch.kind) ($($batch.commandCount) $commandNoun$batchProgress)")
    $lines.Add('')
    $lines.Add('| Command | Source category / wave | Required seam or prerequisite | Intended outcome | Status |')
    $lines.Add('| --- | --- | --- | --- | --- |')
    foreach ($row in $batch.commands) {
        $categoryWave = "$($row.category) / $($row.queueWave) (source: $($row.sourceWave))"
        $prerequisites = (@($row.requiredPrerequisites) -join '; ')
        if ($row.queueRationale -ne 'Manifest order: execute only after the listed prerequisite is implemented and independently reviewed.') {
            $prerequisites = "$($row.queueRationale) $prerequisites"
        }
        $lines.Add("| ``$($row.command)`` | $(ConvertTo-MarkdownCell $categoryWave) | $(ConvertTo-MarkdownCell $prerequisites) | ``$($row.intendedOutcomeState)`` | ``$($row.status)`` |")
    }
}

$markdown = ($lines -join "`n") + "`n"

if ($Verify) {
    foreach ($path in @($OutJson, $OutMarkdown)) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Generated queue artifact is missing: $path. Run this script without -Verify."
        }
    }

    $actualJson = [System.IO.File]::ReadAllText((Resolve-Path -LiteralPath $OutJson).Path)
    $actualMarkdown = [System.IO.File]::ReadAllText((Resolve-Path -LiteralPath $OutMarkdown).Path)
    if ($actualJson -ne ($json + "`n") -or $actualMarkdown -ne $markdown) {
        throw 'Phase 10 batch queue is stale. Regenerate it with tools/Export-Phase10BatchQueue.ps1 and inspect the dependency/order diff.'
    }

    Write-Host "Phase 10 batch queue verified: $($manifest.logicalCommandCount) logical commands; $completedCommandCount complete and $queuedBatchCount batches with queued work."
    return
}

[System.IO.Directory]::CreateDirectory((Split-Path -Parent $OutJson)) | Out-Null
[System.IO.File]::WriteAllText($OutJson, $json + "`n", [System.Text.UTF8Encoding]::new($false))
[System.IO.File]::WriteAllText($OutMarkdown, $markdown, [System.Text.UTF8Encoding]::new($false))
Write-Host "Wrote $OutJson and $OutMarkdown ($($manifest.logicalCommandCount) logical commands; $completedCommandCount complete and $queuedBatchCount batches with queued work)."
