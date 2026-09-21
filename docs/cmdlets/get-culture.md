# Get-Culture port notes

## Status and source

**Partially ported for the current macOS Native-AOT target.** Direct invocation
of every command mode is implemented; the source-declared `Name`
`ValueFromPipeline` behavior is deferred until the shared typed pipeline binder
exists.

- Original: [`GetCultureCommand.cs`](../../../../PowerShell/src/Microsoft.PowerShell.Commands.Utility/commands/utility/GetCultureCommand.cs)
- Generated contract: `GeneratedCmdletPorts.GetCulture`
- AOT port: [`Pipeline.cs`](../../Pipeline.cs), `GetCultureCmdlet` and
  `ICultureCatalog`

## What transferred

- Current culture, `-Name`, `-NoUserOverrides`, and `-ListAvailable`.
- The original three parameter-set behaviours and its stop-on-first-invalid
  `CultureNotFoundException` behavior, represented as a non-terminating
  `ItemNotFoundException` error ID.
- Real BCL culture enumeration and lookup, made possible by disabling
  invariant globalization for this Native-AOT target.
- Typed `CultureRecord` output shared with `Get-UICulture`.

## What did not transfer unchanged

`PSHost.CurrentCulture` becomes `IHostCulture.CurrentCulture`; PowerShell
`ErrorRecord` categories/localized text and the complete `CultureInfo` object
surface become stable error IDs and four typed columns. The current parser
does not implement general pipeline-by-value binding, so `-Name` from an
upstream non-culture command is explicitly unsupported rather than silently
accepted. The `ValidateSet` generator is redundant in the AOT runtime because
the BCL lookup is authoritative.

## Verification

`SelfTest` covers fixture name lookup, full-list output, and invalid-name
errors. Managed smoke checks run:

```powershell
dotnet build -c Release
dotnet run -c Release -- --self-test
dotnet run -c Release -- -Command "Get-Culture en-US | Select-Object Name, EnglishName, LCID"
dotnet run -c Release -- -Command "Get-Culture -ListAvailable | Select-Object Name, LCID"
```

The published macOS ARM64 binary has also passed `--self-test` and
`Get-Culture en-US | Select-Object Name, EnglishName, LCID`. Repeat that
focused pair on each release candidate.

## Variances and learnings

See `Get-Culture` in [`port-variances.json`](../../port-variances.json).
The reusable result is `ICultureCatalog`: culture resolution is OS/global data,
not cmdlet logic. It also reinforces that a generated binding contract needs a
future generic pipeline binder before pipeline-capable parameters can be
called fully ported across unrelated cmdlets.

## Next action

Move to `Get-Verb`, whose static catalogue is the next generator-focused
exercise; preserve its static source data rather than using engine reflection.
