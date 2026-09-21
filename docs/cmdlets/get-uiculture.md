# Get-UICulture port notes

## Status and source

**Complete for the current macOS Native-AOT target.**

- Original: [`GetUICultureCommand.cs`](../../../../PowerShell/src/Microsoft.PowerShell.Commands.Utility/commands/utility/GetUICultureCommand.cs)
- Original base: `PSCmdlet`; output is written from `BeginProcessing`
- Generated contract: `GeneratedCmdletPorts.GetUICulture` (no parameters)
- AOT port: [`Pipeline.cs`](../../Pipeline.cs), `GetUICultureCmdlet`,
  `IHostCulture`, `SystemHostCulture`, and `CultureRecord`

## What transferred

- The command has no parameter surface; its generated descriptor intentionally
  carries no supported parameters.
- Its lifecycle placement was retained: the AOT port emits from
  `BeginProcessing`, and `AotCmdletBase` now gathers output from that phase.
- `CultureInfo.CurrentUICulture` is exposed as a typed culture record with
  `Name`, `DisplayName`, `EnglishName`, and `LCID` selectable fields.
- A narrow `IHostCulture` seam keeps host access testable; a deterministic
  `fr-FR` fixture verifies the source output shape without ambient host state.

## What did not transfer unchanged

PowerShell reads `PSHost.CurrentUICulture`. This standalone AOT executable has
no dynamic `PSHost`, so `SystemHostCulture` maps that request to
`CultureInfo.CurrentUICulture`. This is the correct initial host policy for a
local executable, but a future embedded host can supply its own
`IHostCulture` implementation.

The first run revealed that the project’s `InvariantGlobalization=true` made
all UI culture queries invariant-only. That was an incompatible target-wide
configuration, not a command-code limitation. The project now has
`InvariantGlobalization=false`, retaining Native AOT while using the platform
globalization data necessary for culture cmdlets. This is a deliberate binary
size/deployment trade-off and must remain visible in release packaging.

PowerShell formatting and the full `CultureInfo` object surface are not
recreated. The AOT output is a typed, selectable subset.

## Verification

Managed verification completed:

```powershell
dotnet build -c Release
dotnet run -c Release -- --self-test
dotnet run -c Release -- -Command "Get-UICulture | Select-Object Name, DisplayName, LCID"
```

The published macOS ARM64 binary is verified alongside this port using the
same command and `--self-test`.

## Variances

See command `Get-UICulture` in
[`port-variances.json`](../../port-variances.json): `PSHost` is a narrow host
adapter, object/formatting breadth is an explicit subset, and ICU deployment
is a target configuration decision.

## Reusable learnings

1. The shared lifecycle must permit output from `BeginProcessing`; forcing all
   commands into `ProcessRecord` loses original structure.
2. A host-facing dependency should become a tiny injectable interface, never a
   global engine object.
3. Native AOT is compatible with full globalization; invariant globalization
   is an optional packaging optimization that excludes a real cmdlet class.

## Next action

Reuse `IHostCulture` and `CultureRecord` for `Get-Culture`, where lookup and
enumeration add behavior but not a new execution-model dependency.
