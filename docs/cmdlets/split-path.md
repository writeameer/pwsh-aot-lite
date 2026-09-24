# Split-Path port notes

## Status and source

**Integrated at `9a279d3`: bounded J1 lexical POSIX-v1 subset.**

- Original: `src/Microsoft.PowerShell.Commands.Management/commands/management/ParsePathCommand.cs`, `SplitPathCommand`.
- Generated contract: `GeneratedCmdletPorts.SplitPath`.
- Target: `LexicalPathText.cs`, J1-only registry binder, and `SplitPathCmdlet` in `Pipeline.cs`.

## Port timing

| Field | Value |
| --- | --- |
| UTC work started | `2026-09-24T19:50:10Z` |
| UTC work ended | `2026-09-24T20:06:38Z` |
| Elapsed wall clock | `00:16:28` |
| Scope note | J1 lexical Parent/Leaf/LeafBase/Extension/IsAbsolute only. |

## Upstream reuse evidence matrix

| Surface | Exact upstream evidence | Reuse decision | AOT target / contract | Why direct copy is not possible | Variance ID | Verification |
| --- | --- | --- | --- | --- | --- | --- |
| Metadata/set selection | `ParsePathCommand.cs`, generated `SplitPath` | adapted | generated parameters; one closed selector route | source delegates all sets to provider/session state | `j1-split-static-selector` | selector-conflict self-test |
| Parse operations | `ParsePathCommand.ProcessRecord` | shared-substrate replacement | pure `PathText` parent/leaf/last-dot-v1 | provider parsing has runtime authority | `j1-lexical-no-provider-authority` | lexical/native smoke |
| Output | source `WriteObject` string/bool | adapted | `TextRecord` or `BooleanRecord` | no arbitrary CLR/ETS plane | `j1-lexical-text-record` | typed composition/native smoke |

## Deferred / fail-closed behavior

`Qualifier`, `NoQualifier`, `Resolve`, `Credential`, dynamic/provider behavior and property
binding are rejected. Conflicting selectors fail with `AOT6213`; rooted/malformed inputs fail
before output.

## Verification

Managed build/self-test, parser guard/baselines, campaign/queue verification, and a fresh
`osx-arm64` Native AOT smoke are recorded in the J1 review ledger.

## Current boundary

The reviewed implementation is merged. Do not widen its lexical boundary
without a new lifecycle slice and review packet.
