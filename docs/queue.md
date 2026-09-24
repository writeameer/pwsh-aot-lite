# Cmdlet migration queue

> **Superseded for campaign scheduling.** Use the generated
> [Phase 10 deterministic batch queue](campaign/phase10-batch-queue.md) and
> the required [cmdlet-port lifecycle](campaign/cmdlet-port-lifecycle.md).
> This page is retained as historical pre-Phase-10 triage evidence; its states
> do not determine current migration counts or next work.

This is the deliberate next-work queue, not a claim that a command is ported.
It is ordered to add a small, reusable runtime capability at each step.  Every
candidate must get `cmdlets/<command-name>.md` and a `port-variances.json`
entry before implementation starts.

The contract references below are generated at build time in
`obj/Generated/PwshAotPortGenerator/PwshAotPortGenerator.PortManifestGenerator/GeneratedCmdletPorts.g.cs`.
They are evidence of the original public contract; they are not permission to
silently implement an incomplete parameter set.

## Recommended order

| Order | Cmdlet | State | Source and generated-contract evidence | Complexity | First AOT scope and reusable result | Known blocker / decision |
| --- | --- | --- | --- | --- | --- | --- |
| 0 | `Get-Help` | architecture-first; implementation pending | `src/System.Management.Automation/help/HelpCommands.cs` (`GetHelpCommand`) and `HelpSystem.cs`; `GeneratedCmdletPorts.GetHelp`. | high leverage | Generate a 290-entry static `GeneratedHelpCatalog`, independent from executable-adapter registration; direct command/parameter discovery with explicit AOT status. | The existing source contract lacks `HelpUri`, documentation, module identity, and migration status. Extend generator/input model before writing the renderer; provider/script/module discovery is separately deferred. See `cmdlets/get-help.md`. |
| 0a | `Get-Command` | complete for catalog discovery | `src/System.Management.Automation/engine/GetCommandCommand.cs`; `GeneratedCmdletPorts.GetCommand`; `CompositeHelpCatalog`. | high leverage | Query all 290 built-in contracts plus declarative extension packages with `Name`, `Module`, and `CommandType` filters, returning typed metadata records. | Live `SessionState` discovery, aliases/apps/scripts, fuzzy matching, and unregistered module discovery remain deferred. See `cmdlets/get-command.md`. |
| 0b | `Get-Module` | complete for catalog inventory | `src/System.Management.Automation/engine/GetModuleCommand.cs`; `GeneratedCmdletPorts.GetModule`; `CompositeModuleCatalog`. | high leverage | Inventory one logical compiled-in source module and registered `extension.json` packages, retaining availability, package path, and explicit unverified provenance/trust state. | This is not `PSModuleInfo` or `$PSModulePath` discovery: importing, `-ListAvailable`, remoting/CIM, refresh, edition checks, and dependency resolution are deferred. See `cmdlets/get-module.md`. |
| 0c | Metadata completion | complete for static catalog discovery | `CompletionService`; `CompositeHelpCatalog`; `CompositeModuleCatalog`; `docs/control-plane/completion.md`. | high leverage | Explicit `--complete <line>` and REPL `complete <line>` endpoints offer command, parameter/alias, direct `ValidateSet`, module, and help-topic suggestions from the shared catalog. | Dynamic argument completers, path/provider/value completion, source-expression evaluation, and terminal TAB binding remain deferred. |
| 0d | `Find-Module` | complete for read-only local repository-index discovery | Host control-plane contract; `IRepositoryCatalog` / `RepositoryCatalog`; `repositories/*.repository.json`. | high leverage | Search validated local, declarative repository indexes by Name/Repository and return typed package metadata without changing installed extensions. | PSGallery/PowerShellGet protocols, network transport, credentials, package download, hash/signature/dependency validation, and installation remain deferred. See `cmdlets/find-module.md`. |
| 0e | `Install-Module` | complete for local file-package proof | Host control-plane contract; `IModuleInstaller` / `LocalPackageModuleInstaller`; staged `ExtensionPackageCatalog` validation. | high leverage | Install one exact module from a hash-declared, `file:` URI inside an explicit package root into an explicit extension root using bounded scans, staging, and atomic activation. | HTTP/archive transport, repository signature/trust, dependencies, uninstall, legacy import/bridge activation, and package-manager compatibility remain deferred. See `cmdlets/install-module.md`. |
| 1 | `Get-Uptime` | complete | `src/Microsoft.PowerShell.Commands.Utility/commands/utility/GetUptime.cs` (68 lines); `GeneratedCmdletPorts.GetUptime`; one `Since` switch; `TimeSpan` / `DateTime` output. | low | Direct `Stopwatch` implementation and a typed scalar output record. | Native macOS ARM64 verification recorded in `cmdlets/get-uptime.md`. |
| 2 | `Get-UICulture` | complete | `src/Microsoft.PowerShell.Commands.Utility/commands/utility/GetUICultureCommand.cs` (23 lines); `GeneratedCmdletPorts.GetUICulture`; no parameters; `CultureInfo` output. | low | Runtime UI culture through `IHostCulture`, a typed culture record, and BeginProcessing output. | Full globalization is enabled deliberately; native verification recorded in `cmdlets/get-uiculture.md`. |
| 3 | `Get-Culture` | complete with pipeline binding deferred | `src/Microsoft.PowerShell.Commands.Utility/commands/utility/GetCultureCommand.cs` (124 lines); `GeneratedCmdletPorts.GetCulture`; `Name[]`, `ListAvailable`, `NoUserOverrides`; `CultureInfo` output. | low–medium | Reused `IHostCulture`/`CultureRecord`; added `ICultureCatalog`, named lookup, enumeration, and non-terminating not-found errors. | Generic cross-command pipeline binding remains a recorded shared-runtime deferral; see `cmdlets/get-culture.md`. |
| 4 | `Get-Verb` | complete for current target | `src/Microsoft.PowerShell.Commands.Utility/commands/utility/GetVerbCommand.cs` (47 lines); `GeneratedCmdletPorts.GetVerb`; generated 100-entry catalog from `Verbs.cs` and `VerbDescriptionStrings.resx`; `Verb[]`, `Group[]`; `VerbInfo` output. | medium | Source-generated static `VerbInfo` catalogue, typed `VerbRecord`, `-Verb`/`-Group` filtering, and generated description/alias data. | PowerShell wildcard character classes/escaping are not yet implemented; current shared matcher supports only case-insensitive `*` and `?`. |
| 5 | `Get-Date` | complete with pipeline binding deferred | `src/Microsoft.PowerShell.Commands.Utility/commands/utility/GetDateCommand.cs`; `GeneratedCmdletPorts.GetDate`; `Date`, `UnixTimeSeconds`/`UnixTime`, components, `AsUTC`, `Format`, `UFormat`, and `DisplayHint`. | medium | Static BCL calculation, generated aliases/descriptor, `IClock`, typed DateRecord/TextRecord, .NET/FileDate/UFormat formatting, explicit conflict/range validation. | Original Date input binding by value/property name remains an explicit generic-pipeline-binder deferral; see `cmdlets/get-date.md`. |
| 6 | `Get-Unique` | candidate | `src/Microsoft.PowerShell.Commands.Utility/commands/utility/GetUnique.cs` (120 lines); `GeneratedCmdletPorts.GetUnique`; pipeline `InputObject`, `AsString`, `OnType`, `CaseInsensitive`. | medium | Introduce generic pipeline input and a deterministic AOT comparison service; preserve the command’s adjacent-record semantics. | Source depends on `PSObject.InternalTypeNames` and `ObjectCommandComparer`. `OnType` cannot be claimed until the AOT value type model has an explicit type-name contract. |
| 7 | `Get-FileHash` | complete for direct physical-file target | `src/Microsoft.PowerShell.Commands.Utility/commands/utility/GetHash.cs` (303 lines); `GeneratedCmdletPorts.GetFileHash`; `Path[]`, `LiteralPath[]`, `InputStream`, `Algorithm`. | medium | `IPhysicalFileResolver`, static BCL SHA1/SHA256/SHA384/SHA512/MD5 hashing, typed `FileHashRecord`, direct terminal-name wildcard resolution, literal physical paths, and source-aligned nonterminating errors. | PowerShell providers, directory-component wildcard semantics, and `InputStream` remain explicit virtual-file/typed-stream shared-runtime deferrals; see `cmdlets/get-filehash.md`. |
| 8 | `Get-TimeZone` | complete for current target | `src/Microsoft.PowerShell.Commands.Management/commands/management/TimeZoneCommands.cs` (first `Get-TimeZone` command is lines 20–111); `GeneratedCmdletPorts.GetTimeZone`; `Id[]`, `Name[]`, `ListAvailable`; `TimeZoneInfo` output. | medium | `ITimeZoneCatalog` preserves BCL local/list/id paths, refresh behaviour, source standard/daylight name policy, typed `TimeZoneRecord`, and non-terminating not-found errors. | Generic `Name` pipeline binding and full `WildcardPattern` semantics are explicit shared-runtime deferrals; see `cmdlets/get-timezone.md`. |
| 9 | `Get-Random` | deferred | `src/Microsoft.PowerShell.Commands.Utility/commands/utility/GetRandomCommand.cs`; `GeneratedCmdletPorts.GetRandom`; `InputObject[]`, `Minimum`, `Maximum`, `Count`, `SetSeed`, `Shuffle`; base chain includes `GetRandomCommandBase`. | high | Revisit after generic pipeline values and conversion services exist. | The shared base owns substantial behavior and requires `LanguagePrimitives` conversion. A narrowed numeric-only command would be useful, but is not an honest port of this public contract yet. |
| 10 | `Measure-Object` | deferred | `src/Microsoft.PowerShell.Commands.Utility/commands/utility/Measure-Object.cs`; `GeneratedCmdletPorts.MeasureObject`; input, numeric statistics, text counters, and `PSPropertyExpression[]`. | high | Revisit after generic property access and conversion have static AOT equivalents. | `PSPropertyExpression` and `LanguagePrimitives` are exactly the dynamic object/property system the spike has not designed. Porting only `Count` now would hide the real blocker. |
| 11 | `Get-Clipboard` | deferred | `src/Microsoft.PowerShell.Commands.Management/commands/management/GetClipboardCommand.cs` (116 lines); `GeneratedCmdletPorts.GetClipboard`; `Raw`, `Delimiter[]`. | medium | Revisit with a cross-platform `IClipboard` host adapter. | The source uses platform clipboard facilities; macOS needs a separate, explicit adapter and a headless-session policy. No in-process or external-command dependency should be smuggled into a cmdlet body. |
| 12 | `Set-Clipboard` | deferred | `src/Microsoft.PowerShell.Commands.Management/commands/management/SetClipboardCommand.cs` (163 lines); `GeneratedCmdletPorts.SetClipboard`; `Value[]`, `Append`, `PassThru`, `AsOSC52`; source declares `SupportsShouldProcess`. | medium–high | Revisit after `IClipboard` and a shared confirmation policy exist. | It adds a real mutation and `ShouldProcess`/confirmation contract. `AsOSC52` also requires terminal-host capability negotiation; defer rather than implement an unsafe silent write. |

## Queue rules learned from this triage

1. Start with commands whose source logic is BCL-only after replacing the
   cmdlet lifecycle; `Get-Uptime` is the first test of the existing adapter.
2. Cluster ports around a reusable typed output boundary: scalar/date-time,
   then culture, then static catalogues, then generic input values.
3. A manifest that lists only the normal cmdlet blockers (`WriteObject`,
   `SessionState`, and so on) is not enough to rank a port. Inspect its base
   classes and helper APIs: `Get-Random` and `Get-Verb` show why.
4. A candidate becomes **deferred** as soon as its honest minimum behavior
   requires a runtime subsystem that has not been designed. Record the
   subsystem, then continue with the next independent command.

## Immediate next action

Continue the command-port campaign after the catalog control-plane foundation.
Completion, `Find-Module`, and local-package installation preserve the
no-code-on-discovery boundary. Future repository transport and trust work must
keep installation as an explicit staging operation rather than a discovery
side effect.
