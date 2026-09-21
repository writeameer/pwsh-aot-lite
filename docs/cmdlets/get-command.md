# Get-Command port and architecture notes

## Status and source

**Complete for the catalog-discovery AOT target.** This is intentionally not a
partial implementation of PowerShell's runtime command-discovery engine. It
is the companion query surface for the static built-in catalog and declarative
extension packages already used by `Get-Help`.

- Original cmdlet: [`GetCommandCommand.cs`](../../../../PowerShell/src/System.Management.Automation/engine/GetCommandCommand.cs), `GetCommandCommand`.
- Generated contract: `GeneratedCmdletPorts.GetCommand`.
- AOT implementation: `GetCommandCmdlet` in `HelpCatalog.cs`.
- Shared catalog: `CompositeHelpCatalog` combines all 290 generated source
  cmdlets and each active declarative `extensions/<module>/<version>` package.
  `ExtensionPackageCatalog` validates package identity/schema once and selects
  a deterministic active version; it never asks `AotCmdletRegistry` what it
  can run.

## What transferred

- Default and positional `Name`, including the current case-insensitive `*` /
  `?` wildcard subset.
- `-Name`, `-Module` (including the source `PSSnapin` alias), and
  `-CommandType Cmdlet|Function`.
- Typed output records with `Name`, `CommandType`, `ModuleName`, `Version`,
  `Source`, `Availability`, `Status`, and generated `Syntax` fields. The
  default view now includes `Availability`, so catalogued-only, bridge-pending,
  and blocked commands cannot look like runnable native commands.
- Discovery of registered script-function and compiled-cmdlet module contracts
  without importing a module, loading an extension assembly, or executing an
  extension command. `Microsoft.PowerShell.Archive`,
  `Microsoft.PowerShell.ThreadJob`, and `Microsoft.PowerShell.KubeCtl` are the
  regression fixtures.

Examples:

```powershell
Get-Command Get-ChildItem
Get-Command Start-ThreadJob
Get-Command -Module Microsoft.PowerShell.ThreadJob
Get-Command Get-Kube* -CommandType Function
Get-Command -Module Microsoft.PowerShell.Archive | Select-Object Name, CommandType, ModuleName, Version, Availability, Status
```

`PowerShell.BuiltIn` is the explicit module identity for source-generated
built-in contracts in this prototype. Per-original-assembly module identity is
a generator enhancement, not an inferred claim made by the runtime.

## What did not transfer

The original cmdlet calls `SessionState.InvokeCommand` and returns live
`CommandInfo` subclasses for aliases, applications, external scripts,
filters, functions, and dynamically imported modules. That model requires the
same session state and executable module loading that Native AOT must keep out
of process.

The following original options are deliberately rejected instead of silently
ignored: `-All`, `-ArgumentList`, `-ExcludeModule`, `-FullyQualifiedModule`,
`-FuzzyMinimumDistance`, `-ListImported`, `-Noun`, `-ParameterName`,
`-ParameterType`, `-ShowCommandInfo`, `-Syntax`, `-TotalCount`,
`-UseAbbreviationExpansion`, `-UseFuzzyMatching`, and `-Verb`. Command types
other than `Cmdlet` and `Function` are likewise rejected.

## Verification

Managed and published Native AOT verification must pass from outside the
repository so extension discovery proves its executable-anchored lookup:

```powershell
dotnet run -c Release -- --self-test

$pwshAot = '/Users/ameerdeen/progs/pwsh-spikes/pwsh-aot-lite/artifacts/osx-arm64/PwshAotLite'
Push-Location /tmp
& $pwshAot -Command 'Get-Command Get-ChildItem'
& $pwshAot -Command 'Get-Command Start-ThreadJob'
& $pwshAot -Command 'Get-Command -Module Microsoft.PowerShell.ThreadJob'
& $pwshAot -Command 'Get-Command Get-Kube* -CommandType Function'
Pop-Location
```

The self-test checks built-in source discovery, native-vs-catalogued-only
availability, compiled extension discovery, module filtering, wildcard/type
filtering, extension version propagation, and explicit rejection of
`-CommandType Alias`. It also proves that malformed-but-valid duplicate command
metadata, schema mismatches, and identity mismatches are ignored; the highest
valid dotted package version is the deterministic command/help winner.

## Variances and reusable learnings

See `Get-Command` in [`port-variances.json`](../../port-variances.json).

`Get-Command` validates the central split for this migration: a command is
discoverable because it has a static catalog entry, not because it is currently
in-process executable. `Availability` makes that distinction visible in the
default output: `native-aot`, `catalogued-only`, `legacy-bridge-pending`, or
`blocked`. The same package record can later select a native adapter, an
out-of-process bridge, or an explicit blocked status without changing discovery
or help.

## Next action

Add a versioned package execution endpoint only after its trust and process
protocol are specified. Then map native adapters and a legacy sidecar bridge
to the same catalog records; do not reintroduce in-process extension loading.
