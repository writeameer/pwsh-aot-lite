# Compare-Object port notes

## Status and source

Release-pending Native AOT subset:

```powershell
Compare-Object -ReferenceObject <string[]> -DifferenceObject <string[]> -SyncWindow 0 [-CaseSensitive]
```

All inputs are named direct string literals; `SyncWindow 0` is required. The
adapter emits the source command's default `InputObject`/`SideIndicator` rows
in its pairwise, zero-window order.

- Original: `src/Microsoft.PowerShell.Commands.Utility/commands/utility/Compare-Object.cs` (`CompareObjectCommand`)
- Generated contract: `GeneratedCmdletPorts.CompareObject`
- Target: `Pipeline.cs`: `CompareObjectCmdlet`; `PipelineValueAdapter.cs`: `CompareObjectRecord`

## Port timing

| Field | Value |
| --- | --- |
| UTC work started | `2026-09-25T08:39:31Z` |
| UTC work ended | `—` |
| Elapsed wall clock | `—` |
| Scope note | Direct string, `SyncWindow 0` pairwise comparison only. |

## Upstream reuse evidence matrix

| Surface | Exact upstream evidence | Reuse decision | AOT target / contract | Why direct copy is or is not possible | Variance ID | Verification |
| --- | --- | --- | --- | --- | --- | --- |
| Command metadata | `Compare-Object.cs:17-93`; `GeneratedCmdletPorts.CompareObject` | static-data extracted | Generated `ReferenceObject`, `DifferenceObject`, `SyncWindow`, and `CaseSensitive` descriptors, made named-only | Metadata is reused; source PSObject arrays are not an AOT value boundary | `compare-object-direct-string-sync-zero` | Native positive, negative, and case probes |
| Pairwise algorithm | `Compare-Object.cs:140-254,399-449` | adapted | direct static-string zero-window comparison | `SyncWindow 0` emits unmatched difference then reference rows; source's OrderByProperty/comparer is not crossed | `compare-object-direct-string-sync-zero` | Stock/native ordered corpus |
| Output shape | `Compare-Object.cs:309-356` | shared-substrate replacement | `CompareObjectRecord(InputObject, SideIndicator)` and explicit pipeline value adapter | Source creates/mutates a PSObject note-property bag and dynamic pipeline object | `compare-object-closed-record-output` | Fresh Native AOT table smoke |

## What did not transfer

Any nonzero/default `SyncWindow`, `PSObject` conversion, `Property`, `Culture`,
`IncludeEqual`, `ExcludeDifferent`, `PassThru`, pipeline input, and dynamic
formatter/object output are rejected or absent. The target adds no PSObject,
ETS, reflection, `OrderByProperty`, runspace, or runtime compilation path.

## Verification

```powershell
dotnet build -c Release -p:PowerShellSourceRoot=/Users/ameerdeen/progs/PowerShell/src
dotnet publish -c Release -r osx-arm64 --self-contained true -p:PowerShellSourceRoot=/Users/ameerdeen/progs/PowerShell/src -o ./artifacts/osx-arm64-compare-object
./artifacts/osx-arm64-compare-object/PwshAotLite --self-test
./artifacts/osx-arm64-compare-object/PwshAotLite -Command 'Compare-Object -ReferenceObject "a","b" -DifferenceObject "b","c" -SyncWindow 0'
./artifacts/osx-arm64-compare-object/PwshAotLite -Command 'Compare-Object -ReferenceObject "A" -DifferenceObject "a" -SyncWindow 0 -CaseSensitive'
./artifacts/osx-arm64-compare-object/PwshAotLite -Command 'Compare-Object -ReferenceObject "a" -DifferenceObject "b"'
./artifacts/osx-arm64-compare-object/PwshAotLite -Command 'Compare-Object -ReferenceObject "a" -DifferenceObject "b" -SyncWindow 0 -Property Name'
```

Managed and fresh native output matched stock for the admitted zero-window
corpus. The ordered mismatch example emits `b =>`, `a <=`, `c =>`, `b <=`.
Default case-insensitive and explicit case-sensitive `A`/`a` outcomes match
stock. Missing `-SyncWindow 0` fails `AOT6725`; `-Property` fails `AOT2002`.

## Next action

Merge and push the verified release; only then record completion timing and
count the cmdlet as migrated.
