# J2 static record-transform implementation review ledger

**Status:** pending independent review. This is a Step-7 code-evidence ledger,
not a migration or release claim.

## Scope

The branch contains exactly two descriptor-bound, post-source record stages:

1. `Where-Object`: literal record field + one generated numeric operator +
   finite numeric value.
2. `Select-Object`: one-or-more literal explicit record fields.

All other J2 commands, all source-position uses, `PSObject`/ETS, ScriptBlock
execution, providers, dynamic engine services, runtime discovery, and actual
transport endpoints remain outside the implementation.

## Required independent verdicts

| Lens | Reviewer | Status | Evidence to check |
| --- | --- | --- | --- |
| Architecture / AOT | pending | pending | `StaticRecordStages.cs`, closed `IAotRecordBatchStage`, generated metadata ownership, no transport/runtime registration |
| Diagnostics / compatibility | pending | pending | `AOT6401`–`AOT6406`, upstream spans, no `AOT400*` fallback, fail-closed fixtures |

## Evidence prepared

- Generated source metadata: `GeneratedCmdletPorts.WhereObject` and
  `GeneratedCmdletPorts.SelectObject`; no aliases are copied into the J2
  descriptor.
- Redirect: `UpstreamAstPipelineLowerer` removes direct structural
  `Where-Object`/`Select-Object` dispatch and binds the one stage registry
  route.
- Test-only transport contract proof: ordered
  `AotValue.List(AotValue.Record(...))` reconstruction equality, without an
  endpoint, listener, package, transport registration, or configuration path.
- Parser fixture/baseline: `38-j2-static-record-stage-redirects`.
- Per-command evidence: [Where-Object](../cmdlets/where-object.md) and
  [Select-Object](../cmdlets/select-object.md).
- Verification run: managed Release build/self-test; parser-reuse guard;
  38-fixture parser differential baseline; fresh self-contained `osx-arm64`
  publish at `/private/tmp/pwsh-aot-lite-j2-static-aot`; native self-test;
  positive `Get-TimeZone` filter/projection smoke; and native ScriptBlock
  rejection with `AOT6406`. The documented Homebrew OpenSSL/Brotli
  `LIBRARY_PATH` was required for the macOS linker.

## Gate

No merge, catalog availability change, migration count, timing end, or release
is permitted until both independent verdicts PASS and all required managed,
parser, fresh Native AOT, and focused smoke checks pass.
