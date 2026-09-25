# Join-String port notes

## Status and source

Release-pending Native AOT subset:

```powershell
Join-String -InputObject <string[]> -Separator <string>
```

Both parameters are named and required. `InputObject` contains one or more
direct string literals and `Separator` exactly one direct string literal. The
adapter emits their literal join as one text record.

- Original: `src/Microsoft.PowerShell.Commands.Utility/commands/utility/Join-String.cs` (`JoinStringCommand`)
- Generated contract: `GeneratedCmdletPorts.JoinString`
- Target: `Pipeline.cs`: `JoinStringCmdlet`

## Port timing

| Field | Value |
| --- | --- |
| UTC work started | `2026-09-25T08:28:19Z` |
| UTC work ended | `—` |
| Elapsed wall clock | `—` |
| Scope note | Direct named literal-string join with an explicit separator; no object adaptation or formatting route. |

## Upstream reuse evidence matrix

| Surface | Exact upstream evidence | Reuse decision | AOT target / contract | Why direct copy is or is not possible | Variance ID | Verification |
| --- | --- | --- | --- | --- | --- | --- |
| Command metadata | `Join-String.cs:22-95`; `GeneratedCmdletPorts.JoinString` | static-data extracted | Generated `InputObject`/`Separator` descriptors, made named-only | Generated metadata preserves source names/types while the binder admits only the declared closed form | `join-string-direct-literal-surface` | Named positive and unsupported-parameter native probes |
| Join lifecycle | `Join-String.cs:101-156` | adapted | `string.Join` into one `TextRecord` | Source consumes `PSObject[]`, runs property expressions/conversions, and owns prefix/suffix/quote formatting | `join-string-no-object-or-format-route` | Stock/native literal `a,b` comparison |
| Output | `Join-String.cs:156-159` | shared-substrate replacement | Existing terminal `TextRecord` projection | Source writes a PowerShell object to its dynamic pipeline; target has no generic CLR object pipeline | `join-string-no-object-or-format-route` | Fresh Native AOT smoke |

## What did not transfer

`PSObject` inputs, `Property`, `FormatString`, `OutputPrefix`, `OutputSuffix`,
`QuoteField`, `$OFS` defaulting, culture conversion, property expressions,
pipeline input, positional binding, and all dynamic formatting are outside the
descriptor and fail closed. No reflection, runspace, PSObject, dynamic binder,
or runtime compilation was added.

## Verification

```powershell
dotnet build -c Release -p:PowerShellSourceRoot=/Users/ameerdeen/progs/PowerShell/src
dotnet publish -c Release -r osx-arm64 --self-contained true -p:PowerShellSourceRoot=/Users/ameerdeen/progs/PowerShell/src -o ./artifacts/osx-arm64-join-string
./artifacts/osx-arm64-join-string/PwshAotLite --self-test
./artifacts/osx-arm64-join-string/PwshAotLite -Command 'Join-String -InputObject "a","b" -Separator ","'
./artifacts/osx-arm64-join-string/PwshAotLite -Command 'Join-String -InputObject "a","b"'
./artifacts/osx-arm64-join-string/PwshAotLite -Command 'Join-String -InputObject "a","b" -Separator "," -OutputPrefix "["'
```

Managed and fresh native output matched stock PowerShell exactly for the
admitted literal corpus: `a,b`. Native self-test passed. Missing `-Separator`
fails source-spanned `AOT6722`; `-OutputPrefix` is rejected by generated
binding as `AOT2002`.

## Runtime oracle comparison

| Surface compared | Stock `pwsh` command | Native command/artifact/RID | Fixture/input | Normalization | Result / variance ID |
| --- | --- | --- | --- | --- | --- |
| Direct literal join | `Join-String -InputObject "a","b" -Separator ","` | fresh `osx-arm64` artifact | `a`, `b`, `,` | none | `a,b` match |

## Next action

Merge and push the verified release; only then record completion timing and
count the cmdlet as migrated.
