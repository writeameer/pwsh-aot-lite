# Get-Module port and architecture notes

## Status and source

**Complete for the static built-in and registered-extension inventory target.**
This is intentionally not a partial `PSModuleInfo` implementation. It is the
package-level control-plane query paired with the command and help catalogs.

- Original cmdlet: [`GetModuleCommand.cs`](../../../../PowerShell/src/System.Management.Automation/engine/GetModuleCommand.cs), `GetModuleCommand`.
- Generated contract: `GeneratedCmdletPorts.GetModule`.
- AOT implementation: `GetModuleCmdlet` in `HelpCatalog.cs`.
- Shared runtime seam: `CompositeModuleCatalog` and `ExtensionModuleCatalog`.
- Data inputs: the generated 290-command source catalog plus dynamic,
  declarative `extensions/<module>/<version>/extension.json` and optional
  `provenance.json` files.

## What transferred

- Positional `Name` and `-Name`, including the current case-insensitive `*` /
  `?` matcher.
- A useful module inventory, not merely command-derived grouping:
  `PowerShell.BuiltIn` represents the generated source catalog,
  `PwshAotLite.ControlPlane` represents explicit host commands such as
  `Find-Module`, and every valid registered extension package appears
  independently of its help file.
- Typed rows with `Name`, `Version`, `Origin`, `Availability`, `Status`,
  `Path`, `Trust`, and `Provenance` properties.
- Honest execution state: `native-aot`, `mixed`, `legacy-bridge-pending`,
  `blocked`, or `registered-not-executable` is a package fact, not a guess
  from whether one command happened to be runnable.
- Provenance is read as data only. A ThreadJob registration reports its
  isolated-sidecar inspection; KubeCtl reports declaration-only static
  analysis. Neither path imports the module during `Get-Module`.

Examples:

```powershell
Get-Module
Get-Module Microsoft.PowerShell.ThreadJob
Get-Module Microsoft.PowerShell.* | Select-Object Name, Version, Availability, Status
Get-Module Microsoft.PowerShell.ThreadJob | Select-Object Name, Path, Trust, Provenance
```

## What did not transfer

The original command exposes loaded/current-session modules, `$PSModulePath`
availability, refresh, remoting/CIM, fully-qualified specifications, editions,
and imported `PSModuleInfo` objects. Those require dynamic module discovery or
loading and do not belong in the native executable's safe catalog read path.

Only `Name` is enabled in the AOT descriptor. `-All`, `-ListAvailable`,
`-Refresh`, `-FullyQualifiedName`, `-PSSession`, `-CimSession`, `-PSEdition`,
and `-SkipEditionCheck` are rejected rather than accepted and ignored.

`Trust` is deliberately **not** a signature claim. The registrar currently
records provenance only; package signature verification, repository identity,
hash pinning, dependency resolution, and installation policy are future
`Install-Module` work.

## Reuse and design decisions

1. `CompositeModuleCatalog` is separate from `CompositeHelpCatalog` because
   package inventory has different data requirements: an extension may have a
   valid manifest even when authored help is absent or corrupt.
2. Help, command, and module inventory all use one validated
   `ExtensionPackageCatalog`, one extension-root resolver, and the same
   Native-AOT JSON source-generated serializer context. This prevents divergent
   lookup paths and avoids reflection/dynamic assembly loading.
3. Command-level availability is normalized in `GetCommandCmdlet`; module
   availability is normalized from the package execution endpoint. Do not
   derive a module's status by examining its first command.
4. Overlapping configured and executable-anchored roots are safely
   de-duplicated by full package path. Future registrars should write one
   versioned package directory rather than a global mutable module database.
5. The package catalog rejects manifest schema/version mismatches, duplicate
   command/help names, help-to-manifest command mismatches, and provenance
   source-module identity mismatches. Read failures or malformed JSON are
   skipped without making other packages unavailable. A future diagnostics
   command may surface rejected-package reasons, but `Get-Module` must not
   execute package code to diagnose them.
6. `Get-Module` inventories every valid installed version. `Get-Help` and
   `Get-Command` use one deterministic active winner per module: highest valid
   dotted `System.Version`, then lexical version and package path as stable
   tie-breakers. Do not make lookup depend on filesystem enumeration order.

## Verification

Managed fixtures cover:

- the logical built-in inventory with its native/catalogued count;
- registered Archive, ThreadJob, and declaration-only KubeCtl packages;
- version, availability, trust/provenance, package path, and `-Name`
  filtering;
- explicit rejection of `-ListAvailable`.
- duplicated command names, incompatible schema versions, and mismatched
  provenance identity are rejected without crashing the catalog; two valid
  versions remain in module inventory while command/help resolves the active
  winner.

Published Native AOT smoke test from outside the repository:

```powershell
$pwshAot = '/Users/ameerdeen/progs/pwsh-spikes/pwsh-aot-lite/artifacts/osx-arm64/PwshAotLite'
Push-Location /tmp
& $pwshAot -Command 'Get-Module'
& $pwshAot -Command 'Get-Module Microsoft.PowerShell.ThreadJob'
& $pwshAot -Command 'Get-Command Get-ChildItem'
& $pwshAot -Command 'Get-Command Get-Command'
Pop-Location
```

## Variances and future work

See `Get-Module` in [`port-variances.json`](../../port-variances.json).

The next control-plane step is metadata-only tab completion over the existing
command and module catalogs. `Find-Module` and `Install-Module` must then add a
repository index, package validation, trust policy, and staging/activation
without weakening this no-import discovery boundary.
