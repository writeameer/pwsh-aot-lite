# Select-Object port notes

## Status and source

**Integrated bounded native subset.** Lifecycle step 9 released this pipeline-only static explicit-field projection on `main` at `0927d11`; it counts as one migrated cmdlet. All unadmitted routes remain explicitly deferred.

- Original: `src/Microsoft.PowerShell.Commands.Utility/commands/utility/Select-Object.cs:28-816`, `SelectObjectCommand : PSCmdlet`.
- Generated contract: `GeneratedCmdletPorts.SelectObject`.
- Target: `StaticRecordStages.cs`, `AotRecordBatch.cs`, `AotLanguageCore.cs`, and `UpstreamAstPipelineLowerer.cs`.
- Review ledger: [J2 static record-transform implementation](../reviews/j2-static-record-transforms-implementation.md).

## Port timing

| Field | Value |
| --- | --- |
| UTC work started | `2026-09-24T19:52:33Z` |
| UTC work ended | `2026-09-25T01:02:31Z` |
| Elapsed wall clock | `05:09:58` |
| Scope note | J2 descriptor-bound explicit record-field projection only; released with the shared `Where-Object` stage seam at `0927d11`. |

## Upstream reuse evidence matrix

| Surface | Exact upstream evidence | Reuse decision | AOT target / contract | Why direct copy is or is not possible | Variance ID | Verification |
| --- | --- | --- | --- | --- | --- | --- |
| Metadata, position and property group | `Select-Object.cs:28-77`; generated `SelectObject` metadata | static-data extracted | generated `Property` array metadata and AST-preserved comma group | `PSCmdlet` binding/parameter sets cannot enter Native AOT; no aliases/positions are copied into a new binder | `j2-select-generated-metadata` | descriptor redirect self-test |
| Explicit projection | `Select-Object.cs:306-566`, `PSPropertyExpression` selection/mutation | shared-substrate replacement | `AotRecord.Project`, `AotRecordShape`, `AotSelectStaticFieldsStage` | source mutates/wraps `PSObject` and evaluates property expressions; target admits literal closed record fields only | `j2-select-static-record-projection` | managed self-test + J2 fixture |
| Output fields/table | `Select-Object.cs:581-590`, `FilteredWriteObject`; source default view is ETS/selected-object dependent | deliberate-subset | requested fields in request spelling/order form the only output shape and table columns | selected-object type names, note properties and dynamic format data are excluded | `j2-select-explicit-shape-display` | projection/terminal self-test |
| Exclude/expand/unique/queue/index/first/last/skip/wait/InputObject and calculated/wildcard fields | `Select-Object.cs:28-816` | deferred | `AOT6404`/`AOT6406` before execution | source behavior needs ETS, queues, object expansion, source conversion, or broader stateful contracts | `j2-select-dynamic-routes-deferred` | redirect-boundary fixtures |

## What did not transfer

Only literal, nonblank, non-wildcard fields from a preceding `AotRecordBatch` are accepted. The AST-preserved binding boundary is exactly **one** positional `Property` group **or** exactly one generated `-Property` group: positional/named mixing and a second bare group reject with source-spanned `AOT6404`. Every property expression, `InputObject`, queue/selection modifier, source-object wrapper, note property, and formatter-added member is explicitly deferred.

## Verification

Both required independent reviews and the managed/parser/fresh-Native-AOT gates passed before the lifecycle step 9 merge `0927d11`. The released subset is counted; unsupported routes remain fail-closed.

## Variances and reusable learnings

- `j2-select-generated-metadata`: generation remains the authority for the source parameter surface; the new descriptor only selects the reviewed `Property` contract.
- `j2-select-static-record-projection`: `AotRecordShape` is reused for explicit closed field order rather than reimplementing a property engine.

## Format-contract status

The target intentionally displays exactly the requested field names and order. It does not claim the selected-object ETS/type-data view. This is the explicit `j2-select-explicit-shape-display` variance.

### Runtime oracle comparison

Native evidence artifact: fresh self-contained `osx-arm64` publish
`/private/tmp/pwsh-aot-lite-j2-static-aot/PwshAotLite`, SHA-256
`77ddd76474bb848ec8e773a81321cd1f13411656dc02576f9cc02bc174682881`.

| Surface compared | Stock `pwsh` command/version | Native command/artifact/RID | Fixture/input | Normalization | Result / variance ID |
| --- | --- | --- | --- | --- | --- |
| explicit `Id` projection after numeric filter | `Get-TimeZone -Id UTC \| Where-Object { $_.BaseUtcOffset.TotalMinutes -eq 0 } \| Select-Object Id` | native `osx-arm64`: `Get-TimeZone -Id UTC \| Where-Object BaseUtcOffsetMinutes -EQ 0 \| Select-Object Id` | `UTC` | stock table whitespace versus target static table whitespace only | one `UTC` row and `Id` header match; selected-object ETS formatting remains `j2-select-explicit-shape-display` |
| positional `Property` projection | `Get-TimeZone -Id UTC \| Select-Object Id` | `Get-TimeZone -Id UTC \| Select-Object Id` | `UTC` | table whitespace only | `Id` / `UTC` match |
| named `Property` projection | `Get-TimeZone -Id UTC \| Select-Object -Property Id` | `Get-TimeZone -Id UTC \| Select-Object -Property Id` | `UTC` | table whitespace only | `Id` / `UTC` match |
| mixed positional then named | `Get-TimeZone -Id UTC \| Select-Object Id -Property BaseUtcOffsetMinutes` | same command | controlled invalid binding | both reject; stock wording is normalized to rejection | stock says positional parameter cannot accept `Id`; target emits source-spanned `AOT6404` |
| mixed named then positional | `Get-TimeZone -Id UTC \| Select-Object -Property Id BaseUtcOffsetMinutes` | same command | controlled invalid binding | both reject; stock wording is normalized to rejection | stock says positional parameter cannot accept `BaseUtcOffsetMinutes`; target emits source-spanned `AOT6404` |
| wildcard `Property` | `Get-TimeZone -Id UTC \| Select-Object I*` | same command | `UTC` | documented subset variance | stock projects `Id`; target rejects source-spanned `AOT6404` under `j2-select-dynamic-routes-deferred` |

## Next action

Future work may widen only an explicitly reviewed deferred route; it must not infer broader `Select-Object` compatibility from this released subset.
