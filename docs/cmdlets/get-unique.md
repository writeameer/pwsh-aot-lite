# Get-Unique port notes

## Status and admitted surface

Integrated Native AOT subset:

```powershell
<TextRecord-producing AOT command> | Get-Unique -AsString [-CaseInsensitive]
```

It preserves upstream adjacent-record suppression for static `TextRecord`
values. `-AsString` is mandatory; it is not a general object comparison port.

- Original: `src/Microsoft.PowerShell.Commands.Utility/commands/utility/GetUnique.cs` (`GetUniqueCommand`)
- Generated contract: `GeneratedCmdletPorts.GetUnique`
- Target: `Pipeline.cs`: `GetUniqueCmdlet`, reusing `AotPipelineInputCmdletBase<TextRecord>`

## Port timing

| Field | Value |
| --- | --- |
| UTC work ended | `2026-09-25T09:27:34.9777480Z` |
| Scope note | Explicit text pipeline, current-culture adjacent comparison; managed and fresh Native AOT verification completed; integrated to `main` at `449cc3d`. |

## Reuse and boundary

The adapter retains `GetUniqueCommand.ProcessRecord`'s adjacent-only state
machine and its `StringComparison.CurrentCulture` / `CurrentCultureIgnoreCase`
choice. It reuses the existing closed pipeline-input base rather than adding a
generic object binder.

Default object comparison, `-OnType`, `PSObject.InternalTypeNames`,
`ObjectCommandComparer`, direct arbitrary input, reflection, and ETS are not
admitted. A no-input `Get-Unique -AsString` invocation preserves the source
empty-output behavior.

## Verification

```powershell
dotnet build -c Release -p:PowerShellSourceRoot=/Users/ameerdeen/progs/PowerShell/src
dotnet publish -c Release -r osx-arm64 --self-contained true -p:PowerShellSourceRoot=/Users/ameerdeen/progs/PowerShell/src -o ./artifacts/osx-arm64-get-unique
./artifacts/osx-arm64-get-unique/PwshAotLite --self-test
./artifacts/osx-arm64-get-unique/PwshAotLite -Command 'Join-Path alpha beta | Get-Unique -AsString'
./artifacts/osx-arm64-get-unique/PwshAotLite -Command 'Join-Path alpha beta | Get-Unique'
```

The positive text route emitted `alpha/beta`; the omitted-`AsString` route
failed closed with source-spanned `AOT6731`.
