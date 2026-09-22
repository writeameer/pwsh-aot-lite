# Phase 10 deterministic batch queue

This queue accounts for every logical command in the checked Phase 10 manifest exactly once. It is a dependency-aware migration schedule, **not** a compatibility claim: a command proceeds only when its listed prerequisite exists and has passed the normal source-reuse, architecture, diagnostic, managed, parser, and fresh Native AOT gates.

- Authority: [`phase10-built-in-cmdlets.json`](phase10-built-in-cmdlets.json), SHA-256 `68DFD140AD61540C897A3ACD08A44442C96C15A4455E99DC5F56995A464C97CE`.
- Accounting: 288 logical commands; 5 completed baseline commands; 283 queued commands in 29 batches of ten (final queued batch may be smaller).
- `B00` is accounting-only: previously verified calibration ports are not scheduled again. `B01` starts with the direct physical-path seams proven by `Get-ChildItem` and `Get-FileHash`. `B02` deliberately pulls the JSON foundation forward as `W3a`; it remains blocked on J0, the closed JSON codec/value-plane seam.
- Outcomes are explicit: native subset, shared-seam extension, sidecar candidate, or explicitly unsupported. An unsupported parameter/path within an otherwise useful native subset is a successful bounded conversion, not a silent compatibility claim.

Regenerate or verify this queue:

```powershell
pwsh -NoProfile -File ./tools/Export-Phase10Campaign.ps1 -Verify
pwsh -NoProfile -File ./tools/Export-Phase10BatchQueue.ps1 -Verify
```

## B00 — completed-baseline (5 commands)

| Command | Source category / wave | Required seam or prerequisite | Intended outcome | Status |
| --- | --- | --- | --- | --- |
| `Get-ChildItem` | shared-substrate-dependency / baseline (source: W3) | Calibration/proof command retained for accounting only; it is not scheduled for duplicate implementation. already integrated and native-verified; retain its existing per-cmdlet evidence | `native-subset-proven` | `completed-proven` |
| `Get-FileHash` | shared-substrate-dependency / baseline (source: W3) | Calibration/proof command retained for accounting only; it is not scheduled for duplicate implementation. already integrated and native-verified; retain its existing per-cmdlet evidence | `native-subset-proven` | `completed-proven` |
| `New-Guid` | native-port-candidate / baseline (source: W2) | Calibration/proof command retained for accounting only; it is not scheduled for duplicate implementation. already integrated and native-verified; retain its existing per-cmdlet evidence | `native-subset-proven` | `completed-proven` |
| `New-TimeSpan` | native-port-candidate / baseline (source: W2) | Calibration/proof command retained for accounting only; it is not scheduled for duplicate implementation. already integrated and native-verified; retain its existing per-cmdlet evidence | `native-subset-proven` | `completed-proven` |
| `Start-Sleep` | native-port-candidate / baseline (source: W2) | Calibration/proof command retained for accounting only; it is not scheduled for duplicate implementation. already integrated and native-verified; retain its existing per-cmdlet evidence | `native-subset-proven` | `completed-proven` |

## B01 — queued (10 commands)

| Command | Source category / wave | Required seam or prerequisite | Intended outcome | Status |
| --- | --- | --- | --- | --- |
| `Get-Item` | shared-substrate-dependency / W3 (source: W3) | Reuses the proven direct physical-entry catalog and captured-root path policy. direct-path/provider rejection matrix; physical filesystem read capability | `native-subset-or-extend-shared-seam` | `queued` |
| `Test-Path` | shared-substrate-dependency / W3 (source: W3) | Reuses the proven direct physical path resolver and captured-root policy. direct-path/provider rejection matrix; physical filesystem read capability | `native-subset-or-extend-shared-seam` | `queued` |
| `Resolve-Path` | shared-substrate-dependency / W3 (source: W3) | Reuses the proven direct physical path resolver and captured-root policy. direct-path/provider rejection matrix; physical filesystem read capability | `native-subset-or-extend-shared-seam` | `queued` |
| `Convert-Path` | shared-substrate-dependency / W3 (source: W3) | Reuses the proven direct physical path resolver and captured-root policy. direct-path/provider rejection matrix; physical filesystem read capability | `native-subset-or-extend-shared-seam` | `queued` |
| `Join-Path` | shared-substrate-dependency / W3 (source: W3) | Reuses the direct physical path grammar/policy; no provider drive semantics are admitted. direct-path/provider rejection matrix; physical filesystem read capability | `native-subset-or-extend-shared-seam` | `queued` |
| `Split-Path` | shared-substrate-dependency / W3 (source: W3) | Reuses the direct physical path grammar/policy; no provider drive semantics are admitted. direct-path/provider rejection matrix; physical filesystem read capability | `native-subset-or-extend-shared-seam` | `queued` |
| `Get-Content` | shared-substrate-dependency / W3 (source: W3) | Extends the proven physical-file resolver with a reviewed bounded read/content seam. direct-path/provider rejection matrix; physical filesystem read capability | `native-subset-or-extend-shared-seam` | `queued` |
| `Get-ItemProperty` | shared-substrate-dependency / W3 (source: W3) | Extends the physical-entry record through a reviewed static metadata projection. direct-path/provider rejection matrix; physical filesystem read capability | `native-subset-or-extend-shared-seam` | `queued` |
| `Get-ItemPropertyValue` | shared-substrate-dependency / W3 (source: W3) | Depends on the Get-ItemProperty static metadata projection. direct-path/provider rejection matrix; physical filesystem read capability | `native-subset-or-extend-shared-seam` | `queued` |
| `Test-FileCatalog` | shared-substrate-dependency / W3 (source: W3) | Extends the proven physical-file resolver/hash work with a reviewed catalog-validation seam. direct-path/provider rejection matrix; physical filesystem read capability | `native-subset-or-extend-shared-seam` | `queued` |

## B02 — queued (10 commands)

| Command | Source category / wave | Required seam or prerequisite | Intended outcome | Status |
| --- | --- | --- | --- | --- |
| `ConvertFrom-Json` | shared-substrate-dependency / W3a (source: W7) | W3a pull-forward: blocked on J0, the closed System.Text.Json-to-AotValue codec; no PSObject materialization. J0 closed System.Text.Json-to-AotValue codec; typed JSON diagnostic/limits contract | `native-subset-or-extend-shared-seam` | `queued` |
| `ConvertTo-Json` | shared-substrate-dependency / W3a (source: W7) | W3a pull-forward: blocked on J0, the closed AotValue-to-System.Text.Json codec; no arbitrary CLR serialization. J0 closed AotValue-to-System.Text.Json codec; typed JSON diagnostic/limits contract | `native-subset-or-extend-shared-seam` | `queued` |
| `Test-Json` | native-replacement-required / W3a (source: W1/W6) | W3a pull-forward: depends on J0 validation/diagnostic behavior and the closed JSON value contract. J0 closed JSON validation and diagnostic contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `Get-Location` | shared-substrate-dependency / W3 (source: W3) | direct-path/provider rejection matrix; physical filesystem read capability | `native-subset-or-extend-shared-seam` | `queued` |
| `Get-PSDrive` | shared-substrate-dependency / W3 (source: W3) | direct-path/provider rejection matrix; physical filesystem read capability | `native-subset-or-extend-shared-seam` | `queued` |
| `Import-PowerShellDataFile` | shared-substrate-dependency / W3 (source: W3) | direct-path/provider rejection matrix; physical filesystem read capability | `native-subset-or-extend-shared-seam` | `queued` |
| `Invoke-Item` | shared-substrate-dependency / W3 (source: W3) | direct-path/provider rejection matrix; physical filesystem read capability | `native-subset-or-extend-shared-seam` | `queued` |
| `Out-File` | shared-substrate-dependency / W3 (source: W3) | direct-path/provider rejection matrix; physical filesystem read capability | `native-subset-or-extend-shared-seam` | `queued` |
| `Pop-Location` | shared-substrate-dependency / W3 (source: W3) | direct-path/provider rejection matrix; physical filesystem read capability | `native-subset-or-extend-shared-seam` | `queued` |
| `Push-Location` | shared-substrate-dependency / W3 (source: W3) | direct-path/provider rejection matrix; physical filesystem read capability | `native-subset-or-extend-shared-seam` | `queued` |

## B03 — queued (10 commands)

| Command | Source category / wave | Required seam or prerequisite | Intended outcome | Status |
| --- | --- | --- | --- | --- |
| `Unblock-File` | shared-substrate-dependency / W3 (source: W3) | direct-path/provider rejection matrix; physical filesystem read capability | `native-subset-or-extend-shared-seam` | `queued` |
| `Add-Member` | native-replacement-required / W1/W6 (source: W1/W6) | typed value/pipeline or formatter contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `Compare-Object` | native-replacement-required / W1/W6 (source: W1/W6) | typed value/pipeline or formatter contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `ConvertFrom-CliXml` | native-replacement-required / W1/W6 (source: W1/W6) | typed value/pipeline or formatter contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `ConvertFrom-Csv` | native-replacement-required / W1/W6 (source: W1/W6) | typed value/pipeline or formatter contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `ConvertFrom-Markdown` | native-replacement-required / W1/W6 (source: W1/W6) | typed value/pipeline or formatter contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `ConvertFrom-SddlString` | native-replacement-required / W1/W6 (source: W1/W6) | typed value/pipeline or formatter contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `ConvertFrom-StringData` | native-replacement-required / W1/W6 (source: W1/W6) | typed value/pipeline or formatter contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `ConvertTo-CliXml` | native-replacement-required / W1/W6 (source: W1/W6) | typed value/pipeline or formatter contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `ConvertTo-Csv` | native-replacement-required / W1/W6 (source: W1/W6) | typed value/pipeline or formatter contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |

## B04 — queued (10 commands)

| Command | Source category / wave | Required seam or prerequisite | Intended outcome | Status |
| --- | --- | --- | --- | --- |
| `ConvertTo-Html` | native-replacement-required / W1/W6 (source: W1/W6) | typed value/pipeline or formatter contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `ConvertTo-Xml` | native-replacement-required / W1/W6 (source: W1/W6) | typed value/pipeline or formatter contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `Disable-ExperimentalFeature` | native-replacement-required / W1/W6 (source: W1/W6) | direct body/helper review; typed value/pipeline contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `Enable-ExperimentalFeature` | native-replacement-required / W1/W6 (source: W1/W6) | direct body/helper review; typed value/pipeline contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `Export-Clixml` | native-replacement-required / W1/W6 (source: W1/W6) | typed value/pipeline or formatter contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `Export-Csv` | native-replacement-required / W1/W6 (source: W1/W6) | typed value/pipeline or formatter contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `Export-FormatData` | native-replacement-required / W1/W6 (source: W1/W6) | typed value/pipeline or formatter contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `Export-ModuleMember` | native-replacement-required / W1/W6 (source: W1/W6) | typed value/pipeline or formatter contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `ForEach-Object` | native-replacement-required / W1/W6 (source: W1/W6) | typed value/pipeline or formatter contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `Format-Custom` | native-replacement-required / W1/W6 (source: W1/W6) | typed value/pipeline or formatter contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |

## B05 — queued (10 commands)

| Command | Source category / wave | Required seam or prerequisite | Intended outcome | Status |
| --- | --- | --- | --- | --- |
| `Format-Default` | native-replacement-required / W1/W6 (source: W1/W6) | typed value/pipeline or formatter contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `Format-Hex` | native-replacement-required / W1/W6 (source: W1/W6) | typed value/pipeline or formatter contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `Format-List` | native-replacement-required / W1/W6 (source: W1/W6) | typed value/pipeline or formatter contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `Format-Table` | native-replacement-required / W1/W6 (source: W1/W6) | typed value/pipeline or formatter contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `Format-Wide` | native-replacement-required / W1/W6 (source: W1/W6) | typed value/pipeline or formatter contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `Get-Command` | native-replacement-required / W1/W6 (source: W1/W6) | direct body/helper review; typed value/pipeline contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `Get-Error` | native-replacement-required / W1/W6 (source: W1/W6) | typed value/pipeline or formatter contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `Get-ExperimentalFeature` | native-replacement-required / W1/W6 (source: W1/W6) | direct body/helper review; typed value/pipeline contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `Get-FormatData` | native-replacement-required / W1/W6 (source: W1/W6) | typed value/pipeline or formatter contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `Get-Help` | native-replacement-required / W1/W6 (source: W1/W6) | direct body/helper review; typed value/pipeline contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |

## B06 — queued (10 commands)

| Command | Source category / wave | Required seam or prerequisite | Intended outcome | Status |
| --- | --- | --- | --- | --- |
| `Get-MarkdownOption` | native-replacement-required / W1/W6 (source: W1/W6) | direct body/helper review; typed value/pipeline contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `Get-Member` | native-replacement-required / W1/W6 (source: W1/W6) | typed value/pipeline or formatter contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `Get-Module` | native-replacement-required / W1/W6 (source: W1/W6) | direct body/helper review; typed value/pipeline contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `Get-PSSubsystem` | native-replacement-required / W1/W6 (source: W1/W6) | direct body/helper review; typed value/pipeline contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `Get-Random` | native-replacement-required / W1/W6 (source: W1/W6) | direct body/helper review; typed value/pipeline contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `Get-SecureRandom` | native-replacement-required / W1/W6 (source: W1/W6) | direct body/helper review; typed value/pipeline contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `Get-TraceSource` | native-replacement-required / W1/W6 (source: W1/W6) | direct body/helper review; typed value/pipeline contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `Get-Unique` | native-replacement-required / W1/W6 (source: W1/W6) | typed value/pipeline or formatter contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `Get-Verb` | native-replacement-required / W1/W6 (source: W1/W6) | direct body/helper review; typed value/pipeline contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `Group-Object` | native-replacement-required / W1/W6 (source: W1/W6) | typed value/pipeline or formatter contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |

## B07 — queued (10 commands)

| Command | Source category / wave | Required seam or prerequisite | Intended outcome | Status |
| --- | --- | --- | --- | --- |
| `Import-Clixml` | native-replacement-required / W1/W6 (source: W1/W6) | typed value/pipeline or formatter contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `Import-Csv` | native-replacement-required / W1/W6 (source: W1/W6) | typed value/pipeline or formatter contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `Import-LocalizedData` | native-replacement-required / W1/W6 (source: W1/W6) | typed value/pipeline or formatter contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `Import-Module` | native-replacement-required / W1/W6 (source: W1/W6) | typed value/pipeline or formatter contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `Join-String` | native-replacement-required / W1/W6 (source: W1/W6) | typed value/pipeline or formatter contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `Measure-Command` | native-replacement-required / W1/W6 (source: W1/W6) | typed value/pipeline or formatter contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `Measure-Object` | native-replacement-required / W1/W6 (source: W1/W6) | typed value/pipeline or formatter contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `New-Module` | native-replacement-required / W1/W6 (source: W1/W6) | direct body/helper review; typed value/pipeline contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `New-ModuleManifest` | native-replacement-required / W1/W6 (source: W1/W6) | direct body/helper review; typed value/pipeline contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `Out-Default` | native-replacement-required / W1/W6 (source: W1/W6) | typed value/pipeline or formatter contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |

## B08 — queued (10 commands)

| Command | Source category / wave | Required seam or prerequisite | Intended outcome | Status |
| --- | --- | --- | --- | --- |
| `Out-GridView` | native-replacement-required / W1/W6 (source: W1/W6) | typed value/pipeline or formatter contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `Out-LineOutput` | native-replacement-required / W1/W6 (source: W1/W6) | typed value/pipeline or formatter contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `Out-Null` | native-replacement-required / W1/W6 (source: W1/W6) | typed value/pipeline or formatter contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `Out-Printer` | native-replacement-required / W1/W6 (source: W1/W6) | typed value/pipeline or formatter contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `Out-String` | native-replacement-required / W1/W6 (source: W1/W6) | typed value/pipeline or formatter contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `Remove-Module` | native-replacement-required / W1/W6 (source: W1/W6) | direct body/helper review; typed value/pipeline contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `Save-Help` | native-replacement-required / W1/W6 (source: W1/W6) | typed value/pipeline or formatter contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `Select-Object` | native-replacement-required / W1/W6 (source: W1/W6) | typed value/pipeline or formatter contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `Select-String` | native-replacement-required / W1/W6 (source: W1/W6) | typed value/pipeline or formatter contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `Select-Xml` | native-replacement-required / W1/W6 (source: W1/W6) | typed value/pipeline or formatter contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |

## B09 — queued (10 commands)

| Command | Source category / wave | Required seam or prerequisite | Intended outcome | Status |
| --- | --- | --- | --- | --- |
| `Set-MarkdownOption` | native-replacement-required / W1/W6 (source: W1/W6) | direct body/helper review; typed value/pipeline contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `Set-TraceSource` | native-replacement-required / W1/W6 (source: W1/W6) | typed value/pipeline or formatter contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `Show-Command` | native-replacement-required / W1/W6 (source: W1/W6) | direct body/helper review; typed value/pipeline contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `Show-Markdown` | native-replacement-required / W1/W6 (source: W1/W6) | direct body/helper review; typed value/pipeline contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `Sort-Object` | native-replacement-required / W1/W6 (source: W1/W6) | typed value/pipeline or formatter contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `Tee-Object` | native-replacement-required / W1/W6 (source: W1/W6) | typed value/pipeline or formatter contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `Test-ModuleManifest` | native-replacement-required / W1/W6 (source: W1/W6) | direct body/helper review; typed value/pipeline contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `Trace-Command` | native-replacement-required / W1/W6 (source: W1/W6) | typed value/pipeline or formatter contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `Where-Object` | native-replacement-required / W1/W6 (source: W1/W6) | typed value/pipeline or formatter contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `Write-Error` | native-replacement-required / W1/W6 (source: W1/W6) | typed value/pipeline or formatter contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |

## B10 — queued (10 commands)

| Command | Source category / wave | Required seam or prerequisite | Intended outcome | Status |
| --- | --- | --- | --- | --- |
| `Write-Information` | native-replacement-required / W1/W6 (source: W1/W6) | typed value/pipeline or formatter contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `Write-Output` | native-replacement-required / W1/W6 (source: W1/W6) | typed value/pipeline or formatter contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `Write-Progress` | native-replacement-required / W1/W6 (source: W1/W6) | typed value/pipeline or formatter contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `Write-Verbose` | native-replacement-required / W1/W6 (source: W1/W6) | typed value/pipeline or formatter contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `Write-Warning` | native-replacement-required / W1/W6 (source: W1/W6) | typed value/pipeline or formatter contract | `extend-reviewed-static-seam-or-native-subset` | `queued` |
| `Add-Content` | shared-substrate-dependency / W4 (source: W4) | atomic-write/trust policy; physical filesystem write capability; ShouldProcess/confirmation | `native-subset-or-extend-shared-seam` | `queued` |
| `Clear-Content` | shared-substrate-dependency / W4 (source: W4) | atomic-write/trust policy; physical filesystem write capability; ShouldProcess/confirmation | `native-subset-or-extend-shared-seam` | `queued` |
| `Clear-Item` | shared-substrate-dependency / W4 (source: W4) | atomic-write/trust policy; physical filesystem write capability; ShouldProcess/confirmation | `native-subset-or-extend-shared-seam` | `queued` |
| `Clear-ItemProperty` | shared-substrate-dependency / W4 (source: W4) | atomic-write/trust policy; physical filesystem write capability; ShouldProcess/confirmation | `native-subset-or-extend-shared-seam` | `queued` |
| `Clear-RecycleBin` | shared-substrate-dependency / W4 (source: W4) | atomic-write/trust policy; physical filesystem write capability; ShouldProcess/confirmation | `native-subset-or-extend-shared-seam` | `queued` |

## B11 — queued (10 commands)

| Command | Source category / wave | Required seam or prerequisite | Intended outcome | Status |
| --- | --- | --- | --- | --- |
| `Copy-Item` | shared-substrate-dependency / W4 (source: W4) | atomic-write/trust policy; physical filesystem write capability; ShouldProcess/confirmation | `native-subset-or-extend-shared-seam` | `queued` |
| `Copy-ItemProperty` | shared-substrate-dependency / W4 (source: W4) | atomic-write/trust policy; physical filesystem write capability; ShouldProcess/confirmation | `native-subset-or-extend-shared-seam` | `queued` |
| `Move-Item` | shared-substrate-dependency / W4 (source: W4) | atomic-write/trust policy; physical filesystem write capability; ShouldProcess/confirmation | `native-subset-or-extend-shared-seam` | `queued` |
| `Move-ItemProperty` | shared-substrate-dependency / W4 (source: W4) | atomic-write/trust policy; physical filesystem write capability; ShouldProcess/confirmation | `native-subset-or-extend-shared-seam` | `queued` |
| `New-FileCatalog` | shared-substrate-dependency / W4 (source: W4) | atomic-write/trust policy; physical filesystem write capability; ShouldProcess/confirmation | `native-subset-or-extend-shared-seam` | `queued` |
| `New-Item` | shared-substrate-dependency / W4 (source: W4) | atomic-write/trust policy; physical filesystem write capability; ShouldProcess/confirmation | `native-subset-or-extend-shared-seam` | `queued` |
| `New-ItemProperty` | shared-substrate-dependency / W4 (source: W4) | atomic-write/trust policy; physical filesystem write capability; ShouldProcess/confirmation | `native-subset-or-extend-shared-seam` | `queued` |
| `New-PSDrive` | shared-substrate-dependency / W4 (source: W4) | atomic-write/trust policy; physical filesystem write capability; ShouldProcess/confirmation | `native-subset-or-extend-shared-seam` | `queued` |
| `New-TemporaryDirectory` | shared-substrate-dependency / W4 (source: W4) | atomic-write/trust policy; physical filesystem write capability; ShouldProcess/confirmation | `native-subset-or-extend-shared-seam` | `queued` |
| `New-TemporaryFile` | shared-substrate-dependency / W4 (source: W4) | atomic-write/trust policy; physical filesystem write capability; ShouldProcess/confirmation | `native-subset-or-extend-shared-seam` | `queued` |

## B12 — queued (10 commands)

| Command | Source category / wave | Required seam or prerequisite | Intended outcome | Status |
| --- | --- | --- | --- | --- |
| `Remove-Item` | shared-substrate-dependency / W4 (source: W4) | atomic-write/trust policy; physical filesystem write capability; ShouldProcess/confirmation | `native-subset-or-extend-shared-seam` | `queued` |
| `Remove-ItemProperty` | shared-substrate-dependency / W4 (source: W4) | atomic-write/trust policy; physical filesystem write capability; ShouldProcess/confirmation | `native-subset-or-extend-shared-seam` | `queued` |
| `Remove-PSDrive` | shared-substrate-dependency / W4 (source: W4) | atomic-write/trust policy; physical filesystem write capability; ShouldProcess/confirmation | `native-subset-or-extend-shared-seam` | `queued` |
| `Rename-Item` | shared-substrate-dependency / W4 (source: W4) | atomic-write/trust policy; physical filesystem write capability; ShouldProcess/confirmation | `native-subset-or-extend-shared-seam` | `queued` |
| `Rename-ItemProperty` | shared-substrate-dependency / W4 (source: W4) | atomic-write/trust policy; physical filesystem write capability; ShouldProcess/confirmation | `native-subset-or-extend-shared-seam` | `queued` |
| `Set-Content` | shared-substrate-dependency / W4 (source: W4) | atomic-write/trust policy; physical filesystem write capability; ShouldProcess/confirmation | `native-subset-or-extend-shared-seam` | `queued` |
| `Set-Item` | shared-substrate-dependency / W4 (source: W4) | atomic-write/trust policy; physical filesystem write capability; ShouldProcess/confirmation | `native-subset-or-extend-shared-seam` | `queued` |
| `Set-ItemProperty` | shared-substrate-dependency / W4 (source: W4) | atomic-write/trust policy; physical filesystem write capability; ShouldProcess/confirmation | `native-subset-or-extend-shared-seam` | `queued` |
| `Set-Location` | shared-substrate-dependency / W4 (source: W4) | atomic-write/trust policy; physical filesystem write capability; ShouldProcess/confirmation | `native-subset-or-extend-shared-seam` | `queued` |
| `Get-Acl` | shared-substrate-dependency / W5 (source: W5) | AotHostSubstrate platform/process/host capability; OS/architecture/error matrix | `native-subset-or-extend-shared-seam` | `queued` |

## B13 — queued (10 commands)

| Command | Source category / wave | Required seam or prerequisite | Intended outcome | Status |
| --- | --- | --- | --- | --- |
| `Get-Clipboard` | shared-substrate-dependency / W5 (source: W5) | AotHostSubstrate platform/process/host capability; OS/architecture/error matrix | `native-subset-or-extend-shared-seam` | `queued` |
| `Get-ComputerInfo` | shared-substrate-dependency / W5 (source: W5) | AotHostSubstrate platform/process/host capability; OS/architecture/error matrix | `native-subset-or-extend-shared-seam` | `queued` |
| `Get-Counter` | shared-substrate-dependency / W5 (source: W5) | AotHostSubstrate platform/process/host capability; OS/architecture/error matrix | `native-subset-or-extend-shared-seam` | `queued` |
| `Get-Culture` | shared-substrate-dependency / W5 (source: W5) | AotHostSubstrate platform/process/host capability; OS/architecture/error matrix | `native-subset-or-extend-shared-seam` | `queued` |
| `Get-Date` | shared-substrate-dependency / W5 (source: W5) | AotHostSubstrate platform/process/host capability; OS/architecture/error matrix | `native-subset-or-extend-shared-seam` | `queued` |
| `Get-Host` | shared-substrate-dependency / W5 (source: W5) | AotHostSubstrate platform/process/host capability; OS/architecture/error matrix | `native-subset-or-extend-shared-seam` | `queued` |
| `Get-HotFix` | shared-substrate-dependency / W5 (source: W5) | AotHostSubstrate platform/process/host capability; OS/architecture/error matrix | `native-subset-or-extend-shared-seam` | `queued` |
| `Get-Process` | shared-substrate-dependency / W5 (source: W5) | AotHostSubstrate platform/process/host capability; OS/architecture/error matrix | `native-subset-or-extend-shared-seam` | `queued` |
| `Get-Service` | shared-substrate-dependency / W5 (source: W5) | AotHostSubstrate platform/process/host capability; OS/architecture/error matrix | `native-subset-or-extend-shared-seam` | `queued` |
| `Get-TimeZone` | shared-substrate-dependency / W5 (source: W5) | AotHostSubstrate platform/process/host capability; OS/architecture/error matrix | `native-subset-or-extend-shared-seam` | `queued` |

## B14 — queued (10 commands)

| Command | Source category / wave | Required seam or prerequisite | Intended outcome | Status |
| --- | --- | --- | --- | --- |
| `Get-UICulture` | shared-substrate-dependency / W5 (source: W5) | AotHostSubstrate platform/process/host capability; OS/architecture/error matrix | `native-subset-or-extend-shared-seam` | `queued` |
| `Get-Uptime` | shared-substrate-dependency / W5 (source: W5) | AotHostSubstrate platform/process/host capability; OS/architecture/error matrix | `native-subset-or-extend-shared-seam` | `queued` |
| `New-Service` | shared-substrate-dependency / W5 (source: W5) | AotHostSubstrate platform/process/host capability; OS/architecture/error matrix | `native-subset-or-extend-shared-seam` | `queued` |
| `Out-Host` | shared-substrate-dependency / W5 (source: W5) | AotHostSubstrate platform/process/host capability; OS/architecture/error matrix | `native-subset-or-extend-shared-seam` | `queued` |
| `Read-Host` | shared-substrate-dependency / W5 (source: W5) | AotHostSubstrate platform/process/host capability; OS/architecture/error matrix | `native-subset-or-extend-shared-seam` | `queued` |
| `Remove-Service` | shared-substrate-dependency / W5 (source: W5) | AotHostSubstrate platform/process/host capability; OS/architecture/error matrix | `native-subset-or-extend-shared-seam` | `queued` |
| `Rename-Computer` | shared-substrate-dependency / W5 (source: W5) | AotHostSubstrate platform/process/host capability; OS/architecture/error matrix | `native-subset-or-extend-shared-seam` | `queued` |
| `Resume-Service` | shared-substrate-dependency / W5 (source: W5) | AotHostSubstrate platform/process/host capability; OS/architecture/error matrix | `native-subset-or-extend-shared-seam` | `queued` |
| `Set-Acl` | shared-substrate-dependency / W5 (source: W5) | AotHostSubstrate platform/process/host capability; OS/architecture/error matrix | `native-subset-or-extend-shared-seam` | `queued` |
| `Set-Clipboard` | shared-substrate-dependency / W5 (source: W5) | AotHostSubstrate platform/process/host capability; OS/architecture/error matrix | `native-subset-or-extend-shared-seam` | `queued` |

## B15 — queued (10 commands)

| Command | Source category / wave | Required seam or prerequisite | Intended outcome | Status |
| --- | --- | --- | --- | --- |
| `Set-Date` | shared-substrate-dependency / W5 (source: W5) | AotHostSubstrate platform/process/host capability; OS/architecture/error matrix | `native-subset-or-extend-shared-seam` | `queued` |
| `Set-Service` | shared-substrate-dependency / W5 (source: W5) | AotHostSubstrate platform/process/host capability; OS/architecture/error matrix | `native-subset-or-extend-shared-seam` | `queued` |
| `Set-TimeZone` | shared-substrate-dependency / W5 (source: W5) | AotHostSubstrate platform/process/host capability; OS/architecture/error matrix | `native-subset-or-extend-shared-seam` | `queued` |
| `Start-Process` | shared-substrate-dependency / W5 (source: W5) | AotHostSubstrate platform/process/host capability; OS/architecture/error matrix | `native-subset-or-extend-shared-seam` | `queued` |
| `Start-Service` | shared-substrate-dependency / W5 (source: W5) | AotHostSubstrate platform/process/host capability; OS/architecture/error matrix | `native-subset-or-extend-shared-seam` | `queued` |
| `Start-Transcript` | shared-substrate-dependency / W5 (source: W5) | AotHostSubstrate platform/process/host capability; OS/architecture/error matrix | `native-subset-or-extend-shared-seam` | `queued` |
| `Stop-Computer` | shared-substrate-dependency / W5 (source: W5) | AotHostSubstrate platform/process/host capability; OS/architecture/error matrix | `native-subset-or-extend-shared-seam` | `queued` |
| `Stop-Process` | shared-substrate-dependency / W5 (source: W5) | AotHostSubstrate platform/process/host capability; OS/architecture/error matrix | `native-subset-or-extend-shared-seam` | `queued` |
| `Stop-Service` | shared-substrate-dependency / W5 (source: W5) | AotHostSubstrate platform/process/host capability; OS/architecture/error matrix | `native-subset-or-extend-shared-seam` | `queued` |
| `Stop-Transcript` | shared-substrate-dependency / W5 (source: W5) | AotHostSubstrate platform/process/host capability; OS/architecture/error matrix | `native-subset-or-extend-shared-seam` | `queued` |

## B16 — queued (10 commands)

| Command | Source category / wave | Required seam or prerequisite | Intended outcome | Status |
| --- | --- | --- | --- | --- |
| `Suspend-Service` | shared-substrate-dependency / W5 (source: W5) | AotHostSubstrate platform/process/host capability; OS/architecture/error matrix | `native-subset-or-extend-shared-seam` | `queued` |
| `Switch-Process` | shared-substrate-dependency / W5 (source: W5) | AotHostSubstrate platform/process/host capability; OS/architecture/error matrix | `native-subset-or-extend-shared-seam` | `queued` |
| `Update-Help` | shared-substrate-dependency / W5 (source: W5) | AotHostSubstrate platform/process/host capability; OS/architecture/error matrix | `native-subset-or-extend-shared-seam` | `queued` |
| `Update-List` | shared-substrate-dependency / W5 (source: W5) | AotHostSubstrate platform/process/host capability; OS/architecture/error matrix | `native-subset-or-extend-shared-seam` | `queued` |
| `Wait-Process` | shared-substrate-dependency / W5 (source: W5) | AotHostSubstrate platform/process/host capability; OS/architecture/error matrix | `native-subset-or-extend-shared-seam` | `queued` |
| `Write-Host` | shared-substrate-dependency / W5 (source: W5) | AotHostSubstrate platform/process/host capability; OS/architecture/error matrix | `native-subset-or-extend-shared-seam` | `queued` |
| `ConvertFrom-SecureString` | shared-substrate-dependency / W7 (source: W7) | approved credential/network/security capability; trust/transport/error policy | `native-subset-or-extend-shared-seam` | `queued` |
| `ConvertTo-SecureString` | shared-substrate-dependency / W7 (source: W7) | approved credential/network/security capability; trust/transport/error policy | `native-subset-or-extend-shared-seam` | `queued` |
| `Get-AuthenticodeSignature` | shared-substrate-dependency / W7 (source: W7) | approved credential/network/security capability; trust/transport/error policy | `native-subset-or-extend-shared-seam` | `queued` |
| `Get-CmsMessage` | shared-substrate-dependency / W7 (source: W7) | approved credential/network/security capability; trust/transport/error policy | `native-subset-or-extend-shared-seam` | `queued` |

## B17 — queued (10 commands)

| Command | Source category / wave | Required seam or prerequisite | Intended outcome | Status |
| --- | --- | --- | --- | --- |
| `Get-Credential` | shared-substrate-dependency / W7 (source: W7) | approved credential/network/security capability; trust/transport/error policy | `native-subset-or-extend-shared-seam` | `queued` |
| `Get-ExecutionPolicy` | shared-substrate-dependency / W7 (source: W7) | approved credential/network/security capability; trust/transport/error policy | `native-subset-or-extend-shared-seam` | `queued` |
| `Get-PfxCertificate` | shared-substrate-dependency / W7 (source: W7) | approved credential/network/security capability; trust/transport/error policy | `native-subset-or-extend-shared-seam` | `queued` |
| `Invoke-RestMethod` | shared-substrate-dependency / W7 (source: W7) | approved credential/network/security capability; trust/transport/error policy | `native-subset-or-extend-shared-seam` | `queued` |
| `Invoke-WebRequest` | shared-substrate-dependency / W7 (source: W7) | approved credential/network/security capability; trust/transport/error policy | `native-subset-or-extend-shared-seam` | `queued` |
| `Protect-CmsMessage` | shared-substrate-dependency / W7 (source: W7) | approved credential/network/security capability; trust/transport/error policy | `native-subset-or-extend-shared-seam` | `queued` |
| `Restart-Computer` | shared-substrate-dependency / W7 (source: W7) | approved credential/network/security capability; trust/transport/error policy | `native-subset-or-extend-shared-seam` | `queued` |
| `Restart-Service` | shared-substrate-dependency / W7 (source: W7) | approved credential/network/security capability; trust/transport/error policy | `native-subset-or-extend-shared-seam` | `queued` |
| `Send-MailMessage` | shared-substrate-dependency / W7 (source: W7) | approved credential/network/security capability; trust/transport/error policy | `native-subset-or-extend-shared-seam` | `queued` |
| `Set-AuthenticodeSignature` | shared-substrate-dependency / W7 (source: W7) | approved credential/network/security capability; trust/transport/error policy | `native-subset-or-extend-shared-seam` | `queued` |

## B18 — queued (10 commands)

| Command | Source category / wave | Required seam or prerequisite | Intended outcome | Status |
| --- | --- | --- | --- | --- |
| `Set-ExecutionPolicy` | shared-substrate-dependency / W7 (source: W7) | approved credential/network/security capability; trust/transport/error policy | `native-subset-or-extend-shared-seam` | `queued` |
| `Test-Connection` | shared-substrate-dependency / W7 (source: W7) | approved credential/network/security capability; trust/transport/error policy | `native-subset-or-extend-shared-seam` | `queued` |
| `Unprotect-CmsMessage` | shared-substrate-dependency / W7 (source: W7) | approved credential/network/security capability; trust/transport/error policy | `native-subset-or-extend-shared-seam` | `queued` |
| `Connect-WSMan` | sidecar-candidate / W8 (source: W8) | sidecar protocol; trust policy; typed wire schema | `sidecar-candidate-only` | `queued` |
| `Disable-PSRemoting` | sidecar-candidate / W8 (source: W8) | sidecar protocol; trust policy; typed wire schema | `sidecar-candidate-only` | `queued` |
| `Disable-PSSessionConfiguration` | sidecar-candidate / W8 (source: W8) | sidecar protocol; trust policy; typed wire schema | `sidecar-candidate-only` | `queued` |
| `Disable-WSManCredSSP` | sidecar-candidate / W8 (source: W8) | sidecar protocol; trust policy; typed wire schema | `sidecar-candidate-only` | `queued` |
| `Disconnect-WSMan` | sidecar-candidate / W8 (source: W8) | sidecar protocol; trust policy; typed wire schema | `sidecar-candidate-only` | `queued` |
| `Enable-PSRemoting` | sidecar-candidate / W8 (source: W8) | sidecar protocol; trust policy; typed wire schema | `sidecar-candidate-only` | `queued` |
| `Enable-PSSessionConfiguration` | sidecar-candidate / W8 (source: W8) | sidecar protocol; trust policy; typed wire schema | `sidecar-candidate-only` | `queued` |

## B19 — queued (10 commands)

| Command | Source category / wave | Required seam or prerequisite | Intended outcome | Status |
| --- | --- | --- | --- | --- |
| `Enable-WSManCredSSP` | sidecar-candidate / W8 (source: W8) | sidecar protocol; trust policy; typed wire schema | `sidecar-candidate-only` | `queued` |
| `Enter-PSHostProcess` | sidecar-candidate / W8 (source: W8) | sidecar protocol; trust policy; typed wire schema | `sidecar-candidate-only` | `queued` |
| `Exit-PSHostProcess` | sidecar-candidate / W8 (source: W8) | sidecar protocol; trust policy; typed wire schema | `sidecar-candidate-only` | `queued` |
| `Export-PSSession` | sidecar-candidate / W8 (source: W8) | sidecar protocol; trust policy; typed wire schema | `sidecar-candidate-only` | `queued` |
| `Get-CimAssociatedInstance` | sidecar-candidate / W8 (source: W8) | sidecar protocol; trust policy; typed wire schema | `sidecar-candidate-only` | `queued` |
| `Get-CimClass` | sidecar-candidate / W8 (source: W8) | sidecar protocol; trust policy; typed wire schema | `sidecar-candidate-only` | `queued` |
| `Get-CimInstance` | sidecar-candidate / W8 (source: W8) | sidecar protocol; trust policy; typed wire schema | `sidecar-candidate-only` | `queued` |
| `Get-CimSession` | sidecar-candidate / W8 (source: W8) | sidecar protocol; trust policy; typed wire schema | `sidecar-candidate-only` | `queued` |
| `Get-Event` | sidecar-candidate / W8 (source: W8) | sidecar protocol; trust policy; typed wire schema | `sidecar-candidate-only` | `queued` |
| `Get-EventSubscriber` | sidecar-candidate / W8 (source: W8) | sidecar protocol; trust policy; typed wire schema | `sidecar-candidate-only` | `queued` |

## B20 — queued (10 commands)

| Command | Source category / wave | Required seam or prerequisite | Intended outcome | Status |
| --- | --- | --- | --- | --- |
| `Get-Job` | sidecar-candidate / W8 (source: W8) | sidecar protocol; trust policy; typed wire schema | `sidecar-candidate-only` | `queued` |
| `Get-PSHostProcessInfo` | sidecar-candidate / W8 (source: W8) | sidecar protocol; trust policy; typed wire schema | `sidecar-candidate-only` | `queued` |
| `Get-PSSessionCapability` | sidecar-candidate / W8 (source: W8) | sidecar protocol; trust policy; typed wire schema | `sidecar-candidate-only` | `queued` |
| `Get-PSSessionConfiguration` | sidecar-candidate / W8 (source: W8) | sidecar protocol; trust policy; typed wire schema | `sidecar-candidate-only` | `queued` |
| `Get-WinEvent` | sidecar-candidate / W8 (source: W8) | sidecar protocol; trust policy; typed wire schema | `sidecar-candidate-only` | `queued` |
| `Get-WSManCredSSP` | sidecar-candidate / W8 (source: W8) | sidecar protocol; trust policy; typed wire schema | `sidecar-candidate-only` | `queued` |
| `Get-WSManInstance` | sidecar-candidate / W8 (source: W8) | sidecar protocol; trust policy; typed wire schema | `sidecar-candidate-only` | `queued` |
| `Import-PSSession` | sidecar-candidate / W8 (source: W8) | sidecar protocol; trust policy; typed wire schema | `sidecar-candidate-only` | `queued` |
| `Invoke-CimMethod` | sidecar-candidate / W8 (source: W8) | sidecar protocol; trust policy; typed wire schema | `sidecar-candidate-only` | `queued` |
| `Invoke-Command` | sidecar-candidate / W8 (source: W8) | sidecar protocol; trust policy; typed wire schema | `sidecar-candidate-only` | `queued` |

## B21 — queued (10 commands)

| Command | Source category / wave | Required seam or prerequisite | Intended outcome | Status |
| --- | --- | --- | --- | --- |
| `Invoke-WSManAction` | sidecar-candidate / W8 (source: W8) | sidecar protocol; trust policy; typed wire schema | `sidecar-candidate-only` | `queued` |
| `New-CimInstance` | sidecar-candidate / W8 (source: W8) | sidecar protocol; trust policy; typed wire schema | `sidecar-candidate-only` | `queued` |
| `New-CimSession` | sidecar-candidate / W8 (source: W8) | sidecar protocol; trust policy; typed wire schema | `sidecar-candidate-only` | `queued` |
| `New-CimSessionOption` | sidecar-candidate / W8 (source: W8) | sidecar protocol; trust policy; typed wire schema | `sidecar-candidate-only` | `queued` |
| `New-Event` | sidecar-candidate / W8 (source: W8) | sidecar protocol; trust policy; typed wire schema | `sidecar-candidate-only` | `queued` |
| `New-PSRoleCapabilityFile` | sidecar-candidate / W8 (source: W8) | sidecar protocol; trust policy; typed wire schema | `sidecar-candidate-only` | `queued` |
| `New-PSSessionConfigurationFile` | sidecar-candidate / W8 (source: W8) | sidecar protocol; trust policy; typed wire schema | `sidecar-candidate-only` | `queued` |
| `New-PSSessionOption` | sidecar-candidate / W8 (source: W8) | sidecar protocol; trust policy; typed wire schema | `sidecar-candidate-only` | `queued` |
| `New-PSTransportOption` | sidecar-candidate / W8 (source: W8) | sidecar protocol; trust policy; typed wire schema | `sidecar-candidate-only` | `queued` |
| `New-WinEvent` | sidecar-candidate / W8 (source: W8) | sidecar protocol; trust policy; typed wire schema | `sidecar-candidate-only` | `queued` |

## B22 — queued (10 commands)

| Command | Source category / wave | Required seam or prerequisite | Intended outcome | Status |
| --- | --- | --- | --- | --- |
| `New-WSManInstance` | sidecar-candidate / W8 (source: W8) | sidecar protocol; trust policy; typed wire schema | `sidecar-candidate-only` | `queued` |
| `New-WSManSessionOption` | sidecar-candidate / W8 (source: W8) | sidecar protocol; trust policy; typed wire schema | `sidecar-candidate-only` | `queued` |
| `Receive-Job` | sidecar-candidate / W8 (source: W8) | sidecar protocol; trust policy; typed wire schema | `sidecar-candidate-only` | `queued` |
| `Receive-PSSession` | sidecar-candidate / W8 (source: W8) | sidecar protocol; trust policy; typed wire schema | `sidecar-candidate-only` | `queued` |
| `Register-CimIndicationEvent` | sidecar-candidate / W8 (source: W8) | sidecar protocol; trust policy; typed wire schema | `sidecar-candidate-only` | `queued` |
| `Register-EngineEvent` | sidecar-candidate / W8 (source: W8) | sidecar protocol; trust policy; typed wire schema | `sidecar-candidate-only` | `queued` |
| `Register-ObjectEvent` | sidecar-candidate / W8 (source: W8) | sidecar protocol; trust policy; typed wire schema | `sidecar-candidate-only` | `queued` |
| `Register-PSSessionConfiguration` | sidecar-candidate / W8 (source: W8) | sidecar protocol; trust policy; typed wire schema | `sidecar-candidate-only` | `queued` |
| `Remove-CimInstance` | sidecar-candidate / W8 (source: W8) | sidecar protocol; trust policy; typed wire schema | `sidecar-candidate-only` | `queued` |
| `Remove-CimSession` | sidecar-candidate / W8 (source: W8) | sidecar protocol; trust policy; typed wire schema | `sidecar-candidate-only` | `queued` |

## B23 — queued (10 commands)

| Command | Source category / wave | Required seam or prerequisite | Intended outcome | Status |
| --- | --- | --- | --- | --- |
| `Remove-Event` | sidecar-candidate / W8 (source: W8) | sidecar protocol; trust policy; typed wire schema | `sidecar-candidate-only` | `queued` |
| `Remove-Job` | sidecar-candidate / W8 (source: W8) | sidecar protocol; trust policy; typed wire schema | `sidecar-candidate-only` | `queued` |
| `Remove-WSManInstance` | sidecar-candidate / W8 (source: W8) | sidecar protocol; trust policy; typed wire schema | `sidecar-candidate-only` | `queued` |
| `Set-CimInstance` | sidecar-candidate / W8 (source: W8) | sidecar protocol; trust policy; typed wire schema | `sidecar-candidate-only` | `queued` |
| `Set-PSSessionConfiguration` | sidecar-candidate / W8 (source: W8) | sidecar protocol; trust policy; typed wire schema | `sidecar-candidate-only` | `queued` |
| `Set-WSManInstance` | sidecar-candidate / W8 (source: W8) | sidecar protocol; trust policy; typed wire schema | `sidecar-candidate-only` | `queued` |
| `Set-WSManQuickConfig` | sidecar-candidate / W8 (source: W8) | sidecar protocol; trust policy; typed wire schema | `sidecar-candidate-only` | `queued` |
| `Start-Job` | sidecar-candidate / W8 (source: W8) | sidecar protocol; trust policy; typed wire schema | `sidecar-candidate-only` | `queued` |
| `Stop-Job` | sidecar-candidate / W8 (source: W8) | sidecar protocol; trust policy; typed wire schema | `sidecar-candidate-only` | `queued` |
| `Test-PSSessionConfigurationFile` | sidecar-candidate / W8 (source: W8) | sidecar protocol; trust policy; typed wire schema | `sidecar-candidate-only` | `queued` |

## B24 — queued (10 commands)

| Command | Source category / wave | Required seam or prerequisite | Intended outcome | Status |
| --- | --- | --- | --- | --- |
| `Test-WSMan` | sidecar-candidate / W8 (source: W8) | sidecar protocol; trust policy; typed wire schema | `sidecar-candidate-only` | `queued` |
| `Unregister-Event` | sidecar-candidate / W8 (source: W8) | sidecar protocol; trust policy; typed wire schema | `sidecar-candidate-only` | `queued` |
| `Unregister-PSSessionConfiguration` | sidecar-candidate / W8 (source: W8) | sidecar protocol; trust policy; typed wire schema | `sidecar-candidate-only` | `queued` |
| `Wait-Event` | sidecar-candidate / W8 (source: W8) | sidecar protocol; trust policy; typed wire schema | `sidecar-candidate-only` | `queued` |
| `Wait-Job` | sidecar-candidate / W8 (source: W8) | sidecar protocol; trust policy; typed wire schema | `sidecar-candidate-only` | `queued` |
| `Add-History` | explicitly-unsupported / deferred (source: deferred) | none; catalog-only until a separately approved architecture exists | `explicitly-unsupported` | `queued` |
| `Add-Type` | explicitly-unsupported / deferred (source: deferred) | none; catalog-only until a separately approved architecture exists | `explicitly-unsupported` | `queued` |
| `Clear-History` | explicitly-unsupported / deferred (source: deferred) | none; catalog-only until a separately approved architecture exists | `explicitly-unsupported` | `queued` |
| `Clear-Variable` | explicitly-unsupported / deferred (source: deferred) | none; catalog-only until a separately approved architecture exists | `explicitly-unsupported` | `queued` |
| `Connect-PSSession` | explicitly-unsupported / deferred (source: deferred) | none; catalog-only until a separately approved architecture exists | `explicitly-unsupported` | `queued` |

## B25 — queued (10 commands)

| Command | Source category / wave | Required seam or prerequisite | Intended outcome | Status |
| --- | --- | --- | --- | --- |
| `Debug-Job` | explicitly-unsupported / deferred (source: deferred) | none; catalog-only until a separately approved architecture exists | `explicitly-unsupported` | `queued` |
| `Debug-Process` | explicitly-unsupported / deferred (source: deferred) | none; catalog-only until a separately approved architecture exists | `explicitly-unsupported` | `queued` |
| `Debug-Runspace` | explicitly-unsupported / deferred (source: deferred) | none; catalog-only until a separately approved architecture exists | `explicitly-unsupported` | `queued` |
| `Disable-PSBreakpoint` | explicitly-unsupported / deferred (source: deferred) | none; catalog-only until a separately approved architecture exists | `explicitly-unsupported` | `queued` |
| `Disable-RunspaceDebug` | explicitly-unsupported / deferred (source: deferred) | none; catalog-only until a separately approved architecture exists | `explicitly-unsupported` | `queued` |
| `Disconnect-PSSession` | explicitly-unsupported / deferred (source: deferred) | none; catalog-only until a separately approved architecture exists | `explicitly-unsupported` | `queued` |
| `Enable-PSBreakpoint` | explicitly-unsupported / deferred (source: deferred) | none; catalog-only until a separately approved architecture exists | `explicitly-unsupported` | `queued` |
| `Enable-RunspaceDebug` | explicitly-unsupported / deferred (source: deferred) | none; catalog-only until a separately approved architecture exists | `explicitly-unsupported` | `queued` |
| `Enter-PSSession` | explicitly-unsupported / deferred (source: deferred) | none; catalog-only until a separately approved architecture exists | `explicitly-unsupported` | `queued` |
| `Exit-PSSession` | explicitly-unsupported / deferred (source: deferred) | none; catalog-only until a separately approved architecture exists | `explicitly-unsupported` | `queued` |

## B26 — queued (10 commands)

| Command | Source category / wave | Required seam or prerequisite | Intended outcome | Status |
| --- | --- | --- | --- | --- |
| `Export-Alias` | explicitly-unsupported / deferred (source: deferred) | none; catalog-only until a separately approved architecture exists | `explicitly-unsupported` | `queued` |
| `Get-Alias` | explicitly-unsupported / deferred (source: deferred) | none; catalog-only until a separately approved architecture exists | `explicitly-unsupported` | `queued` |
| `Get-History` | explicitly-unsupported / deferred (source: deferred) | none; catalog-only until a separately approved architecture exists | `explicitly-unsupported` | `queued` |
| `Get-PSBreakpoint` | explicitly-unsupported / deferred (source: deferred) | none; catalog-only until a separately approved architecture exists | `explicitly-unsupported` | `queued` |
| `Get-PSCallStack` | explicitly-unsupported / deferred (source: deferred) | none; catalog-only until a separately approved architecture exists | `explicitly-unsupported` | `queued` |
| `Get-PSProvider` | explicitly-unsupported / deferred (source: deferred) | none; catalog-only until a separately approved architecture exists | `explicitly-unsupported` | `queued` |
| `Get-PSSession` | explicitly-unsupported / deferred (source: deferred) | none; catalog-only until a separately approved architecture exists | `explicitly-unsupported` | `queued` |
| `Get-Runspace` | explicitly-unsupported / deferred (source: deferred) | none; catalog-only until a separately approved architecture exists | `explicitly-unsupported` | `queued` |
| `Get-RunspaceDebug` | explicitly-unsupported / deferred (source: deferred) | none; catalog-only until a separately approved architecture exists | `explicitly-unsupported` | `queued` |
| `Get-TypeData` | explicitly-unsupported / deferred (source: deferred) | none; catalog-only until a separately approved architecture exists | `explicitly-unsupported` | `queued` |

## B27 — queued (10 commands)

| Command | Source category / wave | Required seam or prerequisite | Intended outcome | Status |
| --- | --- | --- | --- | --- |
| `Get-Variable` | explicitly-unsupported / deferred (source: deferred) | none; catalog-only until a separately approved architecture exists | `explicitly-unsupported` | `queued` |
| `Import-Alias` | explicitly-unsupported / deferred (source: deferred) | none; catalog-only until a separately approved architecture exists | `explicitly-unsupported` | `queued` |
| `Invoke-Expression` | explicitly-unsupported / deferred (source: deferred) | none; catalog-only until a separately approved architecture exists | `explicitly-unsupported` | `queued` |
| `Invoke-History` | explicitly-unsupported / deferred (source: deferred) | none; catalog-only until a separately approved architecture exists | `explicitly-unsupported` | `queued` |
| `New-Alias` | explicitly-unsupported / deferred (source: deferred) | none; catalog-only until a separately approved architecture exists | `explicitly-unsupported` | `queued` |
| `New-Object` | explicitly-unsupported / deferred (source: deferred) | none; catalog-only until a separately approved architecture exists | `explicitly-unsupported` | `queued` |
| `New-PSSession` | explicitly-unsupported / deferred (source: deferred) | none; catalog-only until a separately approved architecture exists | `explicitly-unsupported` | `queued` |
| `New-Variable` | explicitly-unsupported / deferred (source: deferred) | none; catalog-only until a separately approved architecture exists | `explicitly-unsupported` | `queued` |
| `Register-ArgumentCompleter` | explicitly-unsupported / deferred (source: deferred) | none; catalog-only until a separately approved architecture exists | `explicitly-unsupported` | `queued` |
| `Remove-Alias` | explicitly-unsupported / deferred (source: deferred) | none; catalog-only until a separately approved architecture exists | `explicitly-unsupported` | `queued` |

## B28 — queued (10 commands)

| Command | Source category / wave | Required seam or prerequisite | Intended outcome | Status |
| --- | --- | --- | --- | --- |
| `Remove-PSBreakpoint` | explicitly-unsupported / deferred (source: deferred) | none; catalog-only until a separately approved architecture exists | `explicitly-unsupported` | `queued` |
| `Remove-PSSession` | explicitly-unsupported / deferred (source: deferred) | none; catalog-only until a separately approved architecture exists | `explicitly-unsupported` | `queued` |
| `Remove-TypeData` | explicitly-unsupported / deferred (source: deferred) | none; catalog-only until a separately approved architecture exists | `explicitly-unsupported` | `queued` |
| `Remove-Variable` | explicitly-unsupported / deferred (source: deferred) | none; catalog-only until a separately approved architecture exists | `explicitly-unsupported` | `queued` |
| `Set-Alias` | explicitly-unsupported / deferred (source: deferred) | none; catalog-only until a separately approved architecture exists | `explicitly-unsupported` | `queued` |
| `Set-PSBreakpoint` | explicitly-unsupported / deferred (source: deferred) | none; catalog-only until a separately approved architecture exists | `explicitly-unsupported` | `queued` |
| `Set-PSDebug` | explicitly-unsupported / deferred (source: deferred) | none; catalog-only until a separately approved architecture exists | `explicitly-unsupported` | `queued` |
| `Set-StrictMode` | explicitly-unsupported / deferred (source: deferred) | none; catalog-only until a separately approved architecture exists | `explicitly-unsupported` | `queued` |
| `Set-Variable` | explicitly-unsupported / deferred (source: deferred) | none; catalog-only until a separately approved architecture exists | `explicitly-unsupported` | `queued` |
| `Update-FormatData` | explicitly-unsupported / deferred (source: deferred) | none; catalog-only until a separately approved architecture exists | `explicitly-unsupported` | `queued` |

## B29 — queued (3 commands)

| Command | Source category / wave | Required seam or prerequisite | Intended outcome | Status |
| --- | --- | --- | --- | --- |
| `Update-TypeData` | explicitly-unsupported / deferred (source: deferred) | none; catalog-only until a separately approved architecture exists | `explicitly-unsupported` | `queued` |
| `Wait-Debugger` | explicitly-unsupported / deferred (source: deferred) | none; catalog-only until a separately approved architecture exists | `explicitly-unsupported` | `queued` |
| `Write-Debug` | explicitly-unsupported / deferred (source: deferred) | none; catalog-only until a separately approved architecture exists | `explicitly-unsupported` | `queued` |
