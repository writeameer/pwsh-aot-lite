# Where-Object port notes

## Status and source

**Integrated bounded native subset.** Lifecycle step 9 released this pipeline-only static numeric-property route on `main` at `0927d11`; it counts as one migrated cmdlet. All unadmitted routes remain explicitly deferred.

- Original: `src/System.Management.Automation/engine/InternalCommands.cs:1281-2515`, `WhereObjectCommand : PSCmdlet`.
- Generated contract: `GeneratedCmdletPorts.WhereObject`.
- Target: `StaticRecordStages.cs`, `AotRecordBatch.cs`, `AotLanguageCore.cs`, and `UpstreamAstPipelineLowerer.cs`.
- Review ledger: [J2 static record-transform implementation](../reviews/j2-static-record-transforms-implementation.md).

## Port timing

| Field | Value |
| --- | --- |
| UTC work started | `2026-09-24T19:52:33Z` |
| UTC work ended | `2026-09-25T01:02:31Z` |
| Elapsed wall clock | `05:09:58` |
| Scope note | J2 descriptor-bound numeric record predicate only; released with the shared `Select-Object` stage seam at `0927d11`. |

## Upstream reuse evidence matrix

| Surface | Exact upstream evidence | Reuse decision | AOT target / contract | Why direct copy is or is not possible | Variance ID | Verification |
| --- | --- | --- | --- | --- | --- | --- |
| Metadata, aliases, parameter sets | `InternalCommands.cs:1281-1407`; generated `WhereObject` metadata | static-data extracted | generated `Property`, `Value`, and six admitted comparison parameters | `PSCmdlet` binder and source parameter sets cannot enter Native AOT; aliases are read from generated metadata, never retyped | `j2-where-generated-metadata` | descriptor redirect self-test |
| Numeric property route | `InternalCommands.cs:2126-2515`, property/operator branches | shared-substrate replacement | immutable `AotRecordBatch` + `AotValueComparison` through `AotWhereStaticNumericStage` | source uses `PSObject`, dynamic property lookup and language conversion; target touches only declared closed record fields | `j2-where-static-record-predicate` | managed self-test + J2 fixture |
| Output/order | `InternalCommands.cs:2326-2387`, `WriteObject(InputObject)` | adapted | matching `AotRecord` rows retain input order and exact fields | no source object/ETS wrapper crosses the AOT boundary | `j2-where-closed-row-output` | predicate/projection self-test |
| ScriptBlock, type/pattern/collection/case-sensitive operators, direct InputObject | `InternalCommands.cs:1281-2515` | deferred | `AOT6406` before execution | those paths require a runspace, script execution, ETS, or unsupported source conversion | `j2-where-dynamic-routes-deferred` | redirect-boundary fixtures |

## What did not transfer

Only a preceding typed record batch may reach this adapter. A script block, `InputObject`, dynamic property lookup, quoted/string coercion, collection or type/pattern operators, and every case-sensitive operator fail closed. An unquoted numeric command atom is normalized from its already-parsed upstream AST provenance; it is not a general text conversion path.

## Verification

Both required independent reviews and the managed/parser/fresh-Native-AOT gates passed before the lifecycle step 9 merge `0927d11`. The released subset is counted; unsupported routes remain fail-closed.

## Variances and reusable learnings

- `j2-where-generated-metadata`: aliases are owned by the generated source contract, which avoids a second binder.
- `j2-where-static-record-predicate`: the existing closed record/filter substrate is reused; no `PSObject`, reflection, provider, or dynamic engine is introduced.

## Format-contract status

`Where-Object` is shape-preserving in the admitted route, so it introduces no default table/view. Terminal formatting remains the preceding source plus any explicit `Select-Object` projection.

### Runtime oracle comparison

| Surface compared | Stock `pwsh` command/version | Native command/artifact/RID | Fixture/input | Normalization | Result / variance ID |
| --- | --- | --- | --- | --- | --- |
| numeric predicate plus explicit projection | `Get-TimeZone -Id UTC \| Where-Object { $_.BaseUtcOffset.TotalMinutes -eq 0 } \| Select-Object Id` | `/private/tmp/pwsh-aot-lite-j2-static-aot/PwshAotLite`, `osx-arm64`: `Get-TimeZone -Id UTC \| Where-Object BaseUtcOffsetMinutes -EQ 0 \| Select-Object Id` | `UTC` | stock object property is mapped to the target adapter's explicit numeric `BaseUtcOffsetMinutes` field | one `UTC` row and `Id` projection match; numeric-field naming is the closed-record variance `j2-where-static-record-predicate` |
| named numeric predicate plus named projection | `Get-TimeZone -Id UTC \| Where-Object -Property BaseUtcOffset -EQ -Value ([TimeSpan]::Zero) \| Select-Object -Property Id` | `Get-TimeZone -Id UTC \| Where-Object -Property BaseUtcOffsetMinutes -EQ -Value 0 \| Select-Object -Property Id` | `UTC` | stock `TimeSpan` property is mapped to explicit closed numeric minutes | one `UTC` row and `Id` header match; closed-field naming remains `j2-where-static-record-predicate` |
| culture projection control | `Get-Culture \| Select-Object Name` | `Get-Culture \| Select-Object Name` | host culture `en-AE` | table whitespace only | `Name` / `en-AE` match; this is controlled host normalization in the eight-case oracle matrix |

## Next action

Future work may widen only an explicitly reviewed deferred route; it must not infer broader `Where-Object` compatibility from this released subset.
