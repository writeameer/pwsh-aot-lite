# ConvertFrom-Json port notes

## Status and source

**In progress — J0 design/review only; no executable adapter is registered.**

- Original: `../../.upstream/PowerShell/src/Microsoft.PowerShell.Commands.Utility/commands/utility/WebCmdlet/ConvertFromJsonCommand.cs`, `ConvertFromJsonCommand : Cmdlet`; helper closure `JsonObject.cs`.
- Generated contract: `GeneratedCmdletPorts.ConvertFromJson`.
- Target: proposed shared `AotJsonCodec`; no target cmdlet implementation.
- Review ledger: [json-codec-foundation.md](../reviews/json-codec-foundation.md).

## Port timing

| Field | Value |
| --- | --- |
| UTC work started | `2026-09-23T13:05:01.6890880Z` |
| UTC work ended | — |
| Elapsed wall clock | — |
| Scope note | J0 closed JSON-to-`AotValue` foundation research, including ConvertFrom-Json source contract. |

## Upstream reuse evidence matrix

| Surface | Exact upstream evidence | Reuse decision | AOT target / contract | Why direct copy is or is not possible | Variance ID | Verification |
| --- | --- | --- | --- | --- | --- | --- |
| Metadata and lifecycle | `ConvertFromJsonCommand.cs:14-73`; generated `ConvertFromJson` metadata | static-data extracted | generated descriptor, not yet active | Generated source contract is AOT-safe data; no parameters are admitted yet. | `json-generated-contract` | generator/build gate pending |
| Input buffering and newline heuristic | `ConvertFromJsonCommand.cs:20-111` | deferred | no adapter | Source buffers pipeline text and performs multi-document heuristics; no static input-pipeline contract has been approved. | `json-input-lifecycle` | negative binding proof pending |
| Object materialization | `ConvertFromJsonCommand.cs:122-134`; `../../.upstream/PowerShell/src/Microsoft.PowerShell.Commands.Utility/commands/utility/WebCmdlet/JsonObject.cs:169-466` | shared-substrate replacement | proposed `AotJsonCodec` to closed `AotValue` | Upstream constructs `PSObject`/hashtable shapes through Newtonsoft and the PowerShell engine. | `json-closed-value-codec` | codec invariant/native proof pending |
| Duplicate/case-colliding object keys | `JsonObject.cs:101,246-289`; stock `pwsh` 7.6.6 rejects differing-case keys but accepts an exact duplicate with its latter value under the default object route | deferred | initial closed-record route rejects exact/case-colliding/blank keys with `AOT6304` | `AotRecord` is ordinal-ignore-case and cannot retain duplicate fields; exact duplicates are an explicit fail-closed variance, while `-AsHashtable` remains wholly unsupported. | `json-case-collision` | future stock/native exact, differing-case, nested, and empty-key fixtures; exact duplicate is a named divergence |
| Output enumeration | `ConvertFromJsonCommand.cs:130` | deferred | no output record | `WriteObject(result, !NoEnumerate)` depends on dynamic collection/object semantics. | `json-output-enumeration` | stock oracle pending |

## What did not transfer

`AsHashtable`, `DateKind`, `Depth`, `NoEnumerate`, dynamic JSON object
materialization, and all pipeline buffering semantics remain unimplemented.

## Next action

Obtain independent J0 AOT/data-plane/diagnostic review verdicts, then decide
whether the proposed codec contract is sufficiently narrow to implement.
