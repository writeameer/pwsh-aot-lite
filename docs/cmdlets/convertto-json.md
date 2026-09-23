# ConvertTo-Json port notes

## Status and source

**In progress — J0 design/review only; no executable adapter is registered.**

- Original: `../../.upstream/PowerShell/src/Microsoft.PowerShell.Commands.Utility/commands/utility/WebCmdlet/ConvertToJsonCommand.cs`, `ConvertToJsonCommand : PSCmdlet`; helper closure `JsonObject.cs`.
- Generated contract: `GeneratedCmdletPorts.ConvertToJson`.
- Target: proposed shared `AotJsonCodec`; no target cmdlet implementation.
- Review ledger: [json-codec-foundation.md](../reviews/json-codec-foundation.md).

## Port timing

| Field | Value |
| --- | --- |
| UTC work started | `2026-09-23T13:05:01.6890880Z` |
| UTC work ended | — |
| Elapsed wall clock | — |
| Scope note | J0 closed `AotValue`-to-JSON foundation research, including ConvertTo-Json source contract. |

## Upstream reuse evidence matrix

| Surface | Exact upstream evidence | Reuse decision | AOT target / contract | Why direct copy is or is not possible | Variance ID | Verification |
| --- | --- | --- | --- | --- | --- | --- |
| Metadata and lifecycle | `ConvertToJsonCommand.cs:18-115`; generated `ConvertToJson` metadata | static-data extracted | generated descriptor, not yet active | Metadata is compile-time data, but no source parameter is admitted yet. | `json-generated-contract` | generator/build gate pending |
| Arbitrary input/object aggregation | `ConvertToJsonCommand.cs:27-128` | deferred | no adapter | Source accepts arbitrary `object`, buffers it, and creates dynamic arrays; AOT accepts no generic CLR object plane. | `json-arbitrary-object-input` | fail-closed binding proof pending |
| Serialization/preprocessing | `ConvertToJsonCommand.cs:121-139`; `../../.upstream/PowerShell/src/Microsoft.PowerShell.Commands.Utility/commands/utility/WebCmdlet/JsonObject.cs:435-846` | shared-substrate replacement | implemented J0 `AotJsonCodec` from closed `AotValue` only; no cmdlet adapter | `ProcessValue`/`ProcessCustomObject` use Newtonsoft, PSObject/ETS, arbitrary CLR member reflection/getters, dictionaries, and enumerables; none can enter the executable. | `json-closed-value-codec` | J0 managed/native codec proof passed; command input/format oracle remains pending |
| Formatting/options/cancellation | `ConvertToJsonCommand.cs:38-143` | deferred | no output contract | `Depth`, `Compress`, `EnumsAsStrings`, `AsArray`, `EscapeHandling`, and cmdlet-owned cancellation have not been proven against a static closed-value contract. | `json-format-and-options` | stock oracle pending |

## What did not transfer

All source parameters and output formatting remain unimplemented. No arbitrary
CLR value, reflection, Newtonsoft, `PSObject`, or cmdlet-owned cancellation
source is admitted.

## Next action

Obtain independent J0 AOT/data-plane/diagnostic review verdicts, then decide
whether the proposed codec contract is sufficiently narrow to implement.
