# Phase 10 engineering archetype inventory

This is a source-backed, mutually exclusive grouping of the **279 unconverted
survey outcomes**. It complements—not replaces—the authoritative
[Phase 10 manifest](phase10-built-in-cmdlets.json). It is planning evidence,
not an execution claim.

## Evidence and accounting

- Survey: [`all-builtins-survey-20260923/report.json`](../../../pwsh-aot-conversion-survey/runs/all-builtins-survey-20260923/report.json), completed `2026-09-23T12:47:29.6726310Z`.
- `missing-profile` is a **compiler status**: no exact executable converter profile was registered. It is not a shared technical root cause. Route derives from the pinned source path, class/base family, helper closure, and manifest prerequisites.
- The survey reports 279 `missing-profile` outcomes, but one, `Join-Path`, has a `related-foundation-only` binder profile rather than no profile at all. It remains part of this 279 because it has no executable profile.
- Exact accounting: **77 native replacement + 92 shared substrate + 62 sidecar + 48 unsupported = 279**. Command allocation below is exhaustive and mutually exclusive.

## Reusable-foundation order

1. **J0 closed JSON codec/value plane**: bounded `System.Text.Json` ↔ `AotValue`, typed diagnostics, no `PSObject`/Newtonsoft/arbitrary CLR serialization.
2. **Direct physical read/path foundation**: parser-derived multi-positional binding plus captured-root, provider-free path operations.
3. **Closed typed data plane**: record transforms, projections, ordering, aggregation, and text matching.
4. **Terminal and stream contracts**: static views plus output/error/information streams, never ETS.
5. **Physical mutation authority**: `ShouldProcess`, confirmation, atomic write/rollback, and trust policy.
6. **Local platform capabilities**: narrow process/service, host, and platform information interfaces with OS/error matrices.
7. **Security/transport authority**: explicit credential, crypto, certificate, HTTP, mail, and policy ownership.
8. **Trusted sidecar protocol**: typed wire schema and trust/lifecycle boundary for CIM, WSMan, remoting, jobs, and events.
9. **Dynamic-engine families remain unavailable** until a separately approved architecture exists.

## Native replacement — 77

### Typed pipeline transforms — 16 — high

**Evidence:** `InternalCommands.cs`, `ObjectCmdletBase`, `OrderObjectBase`, and utility transforms. **Route:** native replacement over closed records/pipeline.

`Add-Member`, `Compare-Object`, `ForEach-Object`, `Get-Member`, `Get-Random`, `Get-SecureRandom`, `Get-Unique`, `Group-Object`, `Join-String`, `Measure-Command`, `Measure-Object`, `Select-Object`, `Select-String`, `Sort-Object`, `Tee-Object`, `Where-Object`.

### Structured codecs — 18 — medium-high

**Evidence:** `XmlCommands.cs`, `CsvCommands.cs`, WebCmdlet JSON, string/document converters. **Route:** native replacement over a common closed codec plane; JSON is J0.

`ConvertFrom-CliXml`, `ConvertTo-CliXml`, `ConvertTo-Xml`, `Export-Clixml`, `Import-Clixml`, `Select-Xml`, `ConvertFrom-Csv`, `ConvertTo-Csv`, `Export-Csv`, `Import-Csv`, `ConvertFrom-Json`, `ConvertTo-Json`, `ConvertFrom-Markdown`, `ConvertFrom-SddlString`, `ConvertFrom-StringData`, `ConvertTo-Html`, `Import-LocalizedData`, `Test-Json`.

### Formatting and streams — 18 — high

**Evidence:** `FrontEndCommandBase`, formatter shapes, `OutConsole.cs`, `WriteOrThrowErrorCommand`. **Route:** static presentation and explicit stream replacement.

`Format-Custom`, `Format-Default`, `Format-Hex`, `Format-List`, `Format-Table`, `Format-Wide`, `Out-Default`, `Out-GridView`, `Out-LineOutput`, `Out-Null`, `Out-Printer`, `Out-String`, `Write-Error`, `Write-Information`, `Write-Output`, `Write-Progress`, `Write-Verbose`, `Write-Warning`.

### Metadata, modules, and tooling — 25 — high

**Evidence:** `ModuleCmdletBase`, help, format-data, trace, experimental-feature, markdown, catalog sources. **Route:** static catalog projections, not a dynamic module engine.

`Disable-ExperimentalFeature`, `Enable-ExperimentalFeature`, `Export-FormatData`, `Export-ModuleMember`, `Get-Command`, `Get-Error`, `Get-ExperimentalFeature`, `Get-FormatData`, `Get-Help`, `Get-MarkdownOption`, `Get-Module`, `Get-PSSubsystem`, `Get-TraceSource`, `Get-Verb`, `Import-Module`, `New-Module`, `New-ModuleManifest`, `Remove-Module`, `Save-Help`, `Set-MarkdownOption`, `Set-TraceSource`, `Show-Command`, `Show-Markdown`, `Test-ModuleManifest`, `Trace-Command`.

## Shared native substrate — 92

### Direct physical reads and navigation — 14 — medium

**Evidence:** `Navigation.cs`, content/property bases, `CombinePathCommand.cs`, `ParsePathCommand.cs`, `Out-File.cs`, catalog sources. **Route:** captured-root direct-path substrate; no providers/drives/ambient location. `Join-Path` first needs the generic multi-positional binder.

`Get-Content`, `Get-ItemProperty`, `Get-ItemPropertyValue`, `Get-Location`, `Get-PSDrive`, `Import-PowerShellDataFile`, `Invoke-Item`, `Join-Path`, `Out-File`, `Pop-Location`, `Push-Location`, `Split-Path`, `Test-FileCatalog`, `Unblock-File`.

### Physical filesystem mutations — 24 — high

**Evidence:** content/property/navigation bases, `NewTemporary*`, catalog sources. **Route:** shared mutation authority with confirmation, atomic-write/rollback, trust, and rejected provider modes.

`Add-Content`, `Clear-Content`, `Clear-Item`, `Clear-ItemProperty`, `Clear-RecycleBin`, `Copy-Item`, `Copy-ItemProperty`, `Move-Item`, `Move-ItemProperty`, `New-FileCatalog`, `New-Item`, `New-ItemProperty`, `New-PSDrive`, `New-TemporaryDirectory`, `New-TemporaryFile`, `Remove-Item`, `Remove-ItemProperty`, `Remove-PSDrive`, `Rename-Item`, `Rename-ItemProperty`, `Set-Content`, `Set-Item`, `Set-ItemProperty`, `Set-Location`.

### Process and service lifecycle — 13 — high

**Evidence:** concentrated `Process.cs` and `Service.cs` families. **Route:** fixed process/service capabilities and OS/error matrices, never generic process launching.

`Get-Process`, `Get-Service`, `New-Service`, `Remove-Service`, `Restart-Service`, `Resume-Service`, `Set-Service`, `Start-Process`, `Start-Service`, `Stop-Process`, `Stop-Service`, `Suspend-Service`, `Wait-Process`.

### Host interaction — 7 — medium-high

**Evidence:** console, host, transcript, switch-process, and `OutConsole.cs`. **Route:** narrow console/input/transcript capability, not `PSHost`.

`Get-Host`, `Out-Host`, `Read-Host`, `Start-Transcript`, `Stop-Transcript`, `Switch-Process`, `Write-Host`.

### Platform metadata and control — 19 — high

**Evidence:** ACL, computer, counter, culture/date, clipboard, timezone, hotfix, update sources. **Route:** local-platform capabilities and supported OS matrices.

`Get-Acl`, `Get-Clipboard`, `Get-ComputerInfo`, `Get-Counter`, `Get-Culture`, `Get-Date`, `Get-HotFix`, `Get-TimeZone`, `Get-UICulture`, `Get-Uptime`, `Rename-Computer`, `Restart-Computer`, `Set-Acl`, `Set-Clipboard`, `Set-Date`, `Set-TimeZone`, `Stop-Computer`, `Update-Help`, `Update-List`.

### Security, authority, and network — 15 — high

**Evidence:** secure-string, signature, certificate, CMS, credential, execution-policy, HTTP, mail, and connection sources. **Route:** explicit authority/trust substrate; no ambient credentials, sockets, proxy, or secret fallback.

`ConvertFrom-SecureString`, `ConvertTo-SecureString`, `Get-AuthenticodeSignature`, `Get-CmsMessage`, `Get-Credential`, `Get-ExecutionPolicy`, `Get-PfxCertificate`, `Invoke-RestMethod`, `Invoke-WebRequest`, `Protect-CmsMessage`, `Send-MailMessage`, `Set-AuthenticodeSignature`, `Set-ExecutionPolicy`, `Test-Connection`, `Unprotect-CmsMessage`.

## Trusted sidecar — 62

### CIM protocol — 12 — very high

**Evidence:** `Microsoft.Management.Infrastructure.CimCmdlets`. **Route:** typed CIM sidecar protocol/trust schema.

`Get-CimAssociatedInstance`, `Get-CimClass`, `Get-CimInstance`, `Get-CimSession`, `Invoke-CimMethod`, `New-CimInstance`, `New-CimSession`, `New-CimSessionOption`, `Register-CimIndicationEvent`, `Remove-CimInstance`, `Remove-CimSession`, `Set-CimInstance`.

### WSMan protocol — 13 — very high

**Evidence:** `Microsoft.WSMan.Management` connection, instance, CredSSP, action, quick-config, ping sources. **Route:** typed WSMan sidecar protocol/trust schema.

`Connect-WSMan`, `Disable-WSManCredSSP`, `Disconnect-WSMan`, `Enable-WSManCredSSP`, `Get-WSManCredSSP`, `Get-WSManInstance`, `Invoke-WSManAction`, `New-WSManInstance`, `New-WSManSessionOption`, `Remove-WSManInstance`, `Set-WSManInstance`, `Set-WSManQuickConfig`, `Test-WSMan`.

### Remoting and configuration — 21 — very high

**Evidence:** `CustomShellCommands.cs`, implicit remoting, host-process, Invoke-Command, session-configuration sources. **Route:** trusted remoting/configuration sidecar, not an in-process runspace.

`Disable-PSRemoting`, `Disable-PSSessionConfiguration`, `Enable-PSRemoting`, `Enable-PSSessionConfiguration`, `Enter-PSHostProcess`, `Exit-PSHostProcess`, `Export-PSSession`, `Get-PSHostProcessInfo`, `Get-PSSessionCapability`, `Get-PSSessionConfiguration`, `Import-PSSession`, `Invoke-Command`, `New-PSRoleCapabilityFile`, `New-PSSessionConfigurationFile`, `New-PSSessionOption`, `New-PSTransportOption`, `Receive-PSSession`, `Register-PSSessionConfiguration`, `Set-PSSessionConfiguration`, `Test-PSSessionConfigurationFile`, `Unregister-PSSessionConfiguration`.

### Jobs and events — 16 — very high

**Evidence:** SMA event/job families. **Route:** stateful job/event sidecar protocol, durable handles, cancellation, typed wire records.

`Get-Event`, `Get-EventSubscriber`, `Get-Job`, `Get-WinEvent`, `New-Event`, `New-WinEvent`, `Receive-Job`, `Register-EngineEvent`, `Register-ObjectEvent`, `Remove-Event`, `Remove-Job`, `Start-Job`, `Stop-Job`, `Unregister-Event`, `Wait-Event`, `Wait-Job`.

## Explicitly unsupported — 48

### Session-state mutation — 16

**Evidence:** alias, variable, history, provider/session-state sources. **Route:** unsupported; no approved static replacement.

`Add-History`, `Clear-History`, `Clear-Variable`, `Export-Alias`, `Get-Alias`, `Get-History`, `Get-PSProvider`, `Get-Variable`, `Import-Alias`, `Invoke-History`, `New-Alias`, `New-Variable`, `Remove-Alias`, `Remove-Variable`, `Set-Alias`, `Set-Variable`.

### Dynamic runtime and type system — 9

**Evidence:** Add-Type, Invoke-Expression, New-Object, type-data, strict-mode, argument-completion sources. **Route:** unsupported; requires dynamic compilation/evaluation or ETS mutation.

`Add-Type`, `Get-TypeData`, `Invoke-Expression`, `New-Object`, `Register-ArgumentCompleter`, `Remove-TypeData`, `Set-StrictMode`, `Update-FormatData`, `Update-TypeData`.

### Debugger, runspace, and live sessions — 23

**Evidence:** debug/runspace/breakpoint/PSSession families. **Route:** unsupported; a sidecar cannot silently recreate live debugger/runspace semantics.

`Connect-PSSession`, `Debug-Job`, `Debug-Process`, `Debug-Runspace`, `Disable-PSBreakpoint`, `Disable-RunspaceDebug`, `Disconnect-PSSession`, `Enable-PSBreakpoint`, `Enable-RunspaceDebug`, `Enter-PSSession`, `Exit-PSSession`, `Get-PSBreakpoint`, `Get-PSCallStack`, `Get-PSSession`, `Get-Runspace`, `Get-RunspaceDebug`, `New-PSSession`, `Remove-PSBreakpoint`, `Remove-PSSession`, `Set-PSBreakpoint`, `Set-PSDebug`, `Wait-Debugger`, `Write-Debug`.

## Campaign rule

Once a foundation is reviewed and integrated, the compiler may add a deterministic profile for a command in that family. A profile is not port proof: every command still requires generated metadata, upstream-reuse matrix, variance, focused tests, parser gates, fresh Native AOT artifact, and independent reviews.
