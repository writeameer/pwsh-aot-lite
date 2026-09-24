# Join-Path port notes

## Status and source

**Implementation verified on branch; pending integration: bounded J1 lexical POSIX-v1 subset.**

- Original: `src/Microsoft.PowerShell.Commands.Management/commands/management/CombinePathCommand.cs`, `JoinPathCommand`.
- Generated contract: `GeneratedCmdletPorts.JoinPath`.
- Target: `LexicalPathText.cs`, J1-only registry binder, and `JoinPathCmdlet` in `Pipeline.cs`.

## Port timing

| Field | Value |
| --- | --- |
| UTC work started | `2026-09-24T19:50:10Z` |
| UTC work ended | `2026-09-24T20:06:38Z` |
| Elapsed wall clock | `00:16:28` |
| Scope note | J1 lexical path composition only; no item/provider authority. |

## Upstream reuse evidence matrix

| Surface | Exact upstream evidence | Reuse decision | AOT target / contract | Why direct copy is not possible | Variance ID | Verification |
| --- | --- | --- | --- | --- | --- | --- |
| Metadata/binding | `CombinePathCommand.cs`, generated `JoinPath` | adapted | generated parameters; closed `JoinPathSequential` binder | source uses `PSCmdlet`/SessionState binder | `j1-join-static-binder` | AST group fixtures/self-test |
| Composition | `CombinePathCommand.ProcessRecord` | shared-substrate replacement | pure `PathText.Compose` | source calls provider `SessionState.Path.Combine` | `j1-lexical-no-provider-authority` | non-existent text/native smoke |
| Scalar output | `WriteObject(joinedPath)` | adapted | `TextRecord` | arbitrary CLR/ETS output is excluded | `j1-lexical-text-record` | pipeline/native smoke |

## Deferred / fail-closed behavior

`Resolve`, `Extension`, `Credential`, providers, drives, wildcards, foreign separators,
transactions, property binding, and dynamic parameters are rejected. Only POSIX-v1 lexical
text is admitted. Values are validated before output, so an invalid child yields zero rows.

## Verification

Managed build/self-test, parser guard/baselines, campaign/queue verification, and a fresh
`osx-arm64` Native AOT smoke are recorded in the J1 review ledger.

## Next action

Merge the reviewed implementation branch; do not widen its lexical boundary.
