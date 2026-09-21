# Get-Verb port notes

## Status and source

**Complete for the current macOS Native-AOT target.**

- Original cmdlet: [`GetVerbCommand.cs`](../../../../PowerShell/src/Microsoft.PowerShell.Commands.Utility/commands/utility/GetVerbCommand.cs)
- Original static data/helpers: [`Verbs.cs`](../../../../PowerShell/src/System.Management.Automation/utils/Verbs.cs) and [`VerbDescriptionStrings.resx`](../../../../PowerShell/src/System.Management.Automation/resources/VerbDescriptionStrings.resx)
- Generated contract/catalogue: `GeneratedCmdletPorts.GetVerb` and `GetVerbCatalog`
- AOT port: [`Pipeline.cs`](../../Pipeline.cs), `GetVerbCmdlet` and `VerbRecord`

## What transferred

- The generated command contract: `Verb[]` and `Group[]`, their positions,
  pipeline metadata, and the original `ValidateSet` metadata for `Group`.
- All 100 canonical verbs, in PowerShell's group/order: Common,
  Communications, Data, Diagnostic, Lifecycle, Other, then Security.
- Alias prefixes and invariant English descriptions from the original source
  and resource file, generated during compilation rather than hand-copied.
- `-Verb` filtering, `-Group` filtering, their intersection, default table
  columns, and a typed `VerbRecord` replacement for `VerbInfo`.

## What did not transfer unchanged

The original `Verbs.FilterByVerbsAndGroups` discovers constant fields with
reflection and loads descriptions through `ResourceManager`. Those are build
time inputs to `PwshAotPortGenerator` now; the published executable only has a
static generated catalogue. This removes dynamic resource/reflection work
without changing the catalogued data.

`-Group` is explicitly validated against the source's seven values. The
shared AOT wildcard matcher is intentionally smaller than PowerShell's
`WildcardPattern`: it supports case-insensitive `*` and `?`, but not character
classes, ranges, escaping, or other PowerShell wildcard features. This is a
known shared-runtime variance, not a `Get-Verb`-specific approximation.

## Verification

`SelfTest` verifies the generated count, metadata shape, `Get` alias/group/
description, filtered execution, parser binding, and invalid group rejection.
Managed checks run:

```powershell
dotnet build -c Release
dotnet run -c Release -- --self-test
dotnet run -c Release -- -Command "Get-Verb Get* -Group Common | Select-Object Verb, AliasPrefix, Group"
```

The expected filtered result is one record: `Get`, `g`, `Common`. The macOS
ARM64 Native-AOT publish has passed both `--self-test` and that filtered command.

## Variances and learnings

See `Get-Verb` in [`port-variances.json`](../../port-variances.json).
This is the first port where the generator transfers source *data* as well as
cmdlet metadata. The reusable pattern is: source constants + static resources
become generated immutable records; reflection/resource lookup must not leak
into a Native-AOT command adapter.

## Next action

Upgrade `SimpleWildcard` as a shared runtime service before claiming full
PowerShell wildcard compatibility for any command.
