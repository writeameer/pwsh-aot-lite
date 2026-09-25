# Select-String port notes

## Status and source

Integrated Native AOT subset:

```powershell
Select-String -Path <file> -Pattern <string> -SimpleMatch -Raw [-CaseSensitive]
```

`Path` and `Pattern` are each named single direct values. The path cannot be a
wildcard; `SimpleMatch` and `Raw` are required. Matching file lines are emitted
as text in source order.

- Original: `src/Microsoft.PowerShell.Commands.Utility/commands/utility/MatchString.cs` (`SelectStringCommand`)
- Generated contract: `GeneratedCmdletPorts.SelectString`
- Target: `Pipeline.cs`: `SelectStringCmdlet`, reusing `IPhysicalFileResolver`

## Port timing

| Field | Value |
| --- | --- |
| UTC work started | `2026-09-25T08:48:18Z` |
| UTC work ended | `2026-09-25T08:57:41.9616620Z` |
| Elapsed wall clock | `00:09:23.9616620` |
| Scope note | Direct one-file, one-pattern Raw SimpleMatch line search. |

## Upstream reuse evidence matrix

| Surface | Exact upstream evidence | Reuse decision | AOT target / contract | Why direct copy is or is not possible | Variance ID | Verification |
| --- | --- | --- | --- | --- | --- | --- |
| Command metadata | `MatchString.cs:1184-1238`; `GeneratedCmdletPorts.SelectString` | static-data extracted | Generated `Path`, `Pattern`, `SimpleMatch`, `Raw`, and `CaseSensitive` metadata made named-only | Preserves source atoms without reproducing parameter-set or provider binding | `select-string-raw-simple-file` | Native positive and rejected-mode probes |
| File scan and literal match | `MatchString.cs:1425-1457,1472-1637,1759-1916` | adapted | Existing `IPhysicalFileResolver`, streamed lines, current-culture literal comparison | Source owns providers, wildcard expansion, MatchInfo/context, regex, encoding transforms, and host emphasis | `select-string-raw-simple-file` | Stock/native raw-line and case corpus |
| Raw output | `MatchString.cs:1679-1703` | shared-substrate replacement | Existing `TextRecord` prose projection | Source writes strings through a dynamic pipeline; target admits only closed text | `select-string-raw-text-output` | Fresh Native AOT smoke |

## What did not transfer

Regex, `MatchInfo`, pipeline input, providers, wildcard and literal-path
aliases, multi-path/pattern, encoding selection, context, culture selection,
emphasis, quiet/list/not-match/all-matches, and every non-Raw output route are
outside the descriptor and fail closed. No PSObject, reflection, provider
engine, runtime compilation, or dynamic formatter was added.

## Verification

```powershell
dotnet build -c Release -p:PowerShellSourceRoot=/Users/ameerdeen/progs/PowerShell/src
dotnet publish -c Release -r osx-arm64 --self-contained true -p:PowerShellSourceRoot=/Users/ameerdeen/progs/PowerShell/src -o ./artifacts/osx-arm64-select-string
./artifacts/osx-arm64-select-string/PwshAotLite --self-test
./artifacts/osx-arm64-select-string/PwshAotLite -Command "Select-String -Path '/private/tmp/pwsh-aot-lite-j2-select-string/Pipeline.cs' -Pattern 'class SelectStringCmdlet' -SimpleMatch -Raw"
./artifacts/osx-arm64-select-string/PwshAotLite -Command "Select-String -Path '/private/tmp/pwsh-aot-lite-j2-select-string/Pipeline.cs' -Pattern 'CLASS SELECTSTRINGCMDLET' -SimpleMatch -Raw -CaseSensitive"
```

The exact raw source line matched stock PowerShell; default matching is
case-insensitive and the case-sensitive miss also matched stock. Missing
`-SimpleMatch` fails `AOT6728`; `-Context` is rejected by generated binding as
`AOT2002`.

## Next action

None. The verified slice is integrated to `main` at `922f083`; its completion
timing is recorded in this follow-up release record.
