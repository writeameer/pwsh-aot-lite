# Get-Process port notes

## Status

**Complete for the current macOS Native AOT target.** The port is a behavioral
port of the command, not reuse of `System.Management.Automation`. It is the
reference cmdlet for future ports.

## Source and target

- Original implementation: [`Process.cs`](../../../../PowerShell/src/Microsoft.PowerShell.Commands.Management/commands/management/Process.cs)
- Original base behavior: `ProcessBaseCommand` and `GetProcessCommand`
- AOT implementation: [`Pipeline.cs`](../../Pipeline.cs), `GetProcessCmdlet`
- Generated metadata: generated at build time by
  [`PwshAotPortGenerator`](../../../PwshAotPortGenerator/PortManifestGenerator.cs)
  into `obj/Generated/.../GeneratedCmdletPorts.g.cs`
- Structured variance record:
  [`port-variances.json`](../../port-variances.json), command `Get-Process`

## What transferred

The original command’s process-selection intent and command surface were
retained behind an AOT-specific contract:

- `Get-Process`, `-Name` / `-ProcessName`, and `-Id` / `-PID` selection.
- Selection ordering, duplicate removal, wildcard selection, and
  non-terminating missing-process errors.
- Pipeline `InputObject` behavior in the typed adapter when an AOT caller
  supplies a process record. The current upstream-AST runner does not yet admit
  a command-to-command pipeline stage; it admits a source command followed by
  direct `Where-Object` and `Select-Object` only.
- `-IncludeUserName`, `-Module`, `-FileVersionInfo`, and the valid
  `-Module -FileVersionInfo` combination.
- Parameter metadata (command name, aliases, parameter sets) comes from the
  build-time generated source contract rather than duplicated attributes.

The architecture replacement is intentionally shared:

| Original PowerShell dependency | AOT replacement |
| --- | --- |
| `Cmdlet` lifecycle | `AotCmdletBase` lifecycle |
| `WriteObject` | typed `IPipelineRecord` output |
| `WriteError` / `ErrorRecord` | `AotExecutionContext` non-terminating error |
| process/platform APIs mixed into cmdlet | `IProcessCatalog` platform boundary |
| runtime cmdlet discovery | static generated metadata and registry |

## What did not transfer unchanged

- `PSObject` extended type data and actual `System.Diagnostics.Process`
  instances are replaced with typed AOT process/module/file-version records.
- `WildcardPattern` is currently a case-insensitive `*` / `?` subset.
- Localized resource strings and PowerShell `ErrorRecord` categories are
  replaced by stable error IDs and plain messages.
- Providers, remoting, and PowerShell formatting/type extensions are outside
  this command port’s AOT runtime boundary.

No engine behavior is accepted and ignored: unsupported behavior must fail at
binding or remain explicitly documented as deferred.

## Verification completed

The native macOS ARM64 executable was exercised with the following command
shapes:

```powershell
./artifacts/osx-arm64/PwshAotLite --self-test
./artifacts/osx-arm64/PwshAotLite -Command "Get-Process -Name PwshAotLite"
./artifacts/osx-arm64/PwshAotLite -Command "Get-Process -Id 123"
./artifacts/osx-arm64/PwshAotLite -Command "Get-Process -IncludeUserName | Select-Object Name, Id, UserName"
./artifacts/osx-arm64/PwshAotLite -Command "Get-Process -Module | Select-Object ModuleName, ProcessId, FileName"
./artifacts/osx-arm64/PwshAotLite -Command "Get-Process -FileVersionInfo | Select-Object FileName, FileVersion, ProductVersion"
./artifacts/osx-arm64/PwshAotLite -Command "Get-Process -Module -FileVersionInfo | Select-Object ModuleName, FileVersion"
./artifacts/osx-arm64/PwshAotLite -Command "Get-Process -Name PwshAotLite | Where-Object CPU -ge 0 | Select-Object Name, Id"
```

The refreshed artifact also verified the parser boundary: a trailing pipe
fails with the preserved upstream `EmptyPipeElement` diagnostic; a script-block
predicate and a four-stage pipeline fail with `AOT1001`. These are deliberate
current-runner limits, not cmdlet behavior claims.

The project self-test covers binding, aliases, parameter-set handling, fixture
selection/errors, and pipeline behavior. The native commands above cover the
macOS platform boundary.

## Variance ledger

The authoritative structured entries are:

- `cmdlet-runtime`
- `parameter-binding`
- `wildcards`
- `output-shape`
- `advanced-process-options`
- `resource-errors`

See [`port-variances.json`](../../port-variances.json) for their status and
automation candidacy. Keep this prose and those entries synchronized.

## Reusable learnings

1. Extract metadata from the original source at compile time; do not manually
   duplicate attributes, aliases, or parameter sets.
2. Preserve a command’s base-class behavior through shared lifecycle/runtime
   services before porting command-specific logic.
3. Put OS access behind a narrow interface early. It makes behavior fixture
   testable and isolates platform variation from the port.
4. A repeated variance is a backlog item for the generator or a shared AOT
   service. Do not copy a workaround into the next cmdlet.

## Next action

None for this reference port. Use it as the acceptance template for the next
cmdlet, and update these notes if a later shared-runtime change alters its
behavior.
