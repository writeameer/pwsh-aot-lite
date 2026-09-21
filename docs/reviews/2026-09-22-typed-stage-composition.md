# Review: Typed pipeline-stage composition

Date: `2026-09-22`
Claim reviewed: `The AOT host may execute one source command followed by one registered statically typed input command, then the existing closed Where-Object and Select-Object stages.`
Upstream commit: `1e53f6bbab4b8791eae782474d21889f9e5d6038`

## Evidence

- Design: [Engine Runtime Core](../architecture/engine-runtime-core.md), the
  [runtime stream contract](2026-09-22-runtime-stream-contract.md), and
  upstream `Process.cs` `InputObject` metadata. `Get-Process` is the only
  current typed-input adapter; generated metadata remains descriptive, never a
  permission to execute every upstream pipeline declaration.
- Managed verification: `dotnet build -c Release --no-restore -v:q`, `dotnet
  run -c Release --no-build -- --self-test`, parser-reuse guard, parser
  baseline verification, and `git diff --check` passed. Self-test covers
  source/destination position and source spans, a `ProcessRecord` handoff,
  `AOT4010` type rejection, direct `-InputObject` rejection, and rejected
  unregistered, late, and repeated command stages.
- Native AOT evidence: fresh `./PwshAotLite.csproj` self-contained `osx-arm64`
  publish (with documented Homebrew OpenSSL/Brotli `LIBRARY_PATH`) passed
  `--self-test`, the positive `Get-Process | Get-Process | Select-Object Name,
  Id` proof, and the incompatible-record `AOT4010` rejection. Use the explicit
  project path: bare repository-root publish selects the solution and attempts
  to AOT-publish the netstandard generator.
- Parser evidence: the existing pinned AST lowerer alone accepts the new
  ordered command shape; no lexer, parser, or string grammar was added.

## Independent verdicts

| Persona | PASS / BLOCK | Evidence-backed finding | Resolution / accepted variance |
| --- | --- | --- | --- |
| Upstream Grammar Steward | **PASS** | The existing upstream-AST lowerer alone admits one registered input adapter in its ordered stage position; no lexer, parser, source splitting, or tooling facade changed. | Unregistered, late, and repeated stages remain `AOT1001`. |
| Native AOT Boundary Sentinel | **PASS** | The closed generic bridge accepts only `IPipelineRecord` and `record is TInput`, with no `object`, `PSObject`, reflection, dynamic binder, or generated-metadata execution fallback. Fresh native proof passed. | This is batch handoff, not object streaming. |
| Static Data-Plane & Binder Guardian | **PASS** | The registry—not source metadata—authorizes the sole `ProcessRecord` input adapter; direct `-InputObject` is rejected and `AOT4010` rejects a mismatched shape. | Value/property-name pipeline binding remains excluded. |
| Diagnostic Experience Guardian | **PASS** | Source/destination invocation spans and stage positions remain typed; Help/Completion now project executable direct parameter sets only and state typed input separately. | Source-only parameter declarations remain build evidence, not runnable syntax. |
| Compatibility Proof Adversary | **PASS** | Tests cover positive static composition, direct input rejection, shape rejection, stage ordering, Help/Completion truthfulness, and existing transforms; native proof passed. | No claim for arbitrary command pipelines or multiple input stages. |
| Architecture Guard | **PASS** | Input lifecycle, error repair, cancellation hook, and materialization share `AotCmdletBase`; `PipelinePlan` contains no concrete Get-Process type check. | Future adapters need their own closed record type and review. |

## Outcome

`integrated — one static typed input-stage adapter` — this is not arbitrary command pipelines, generic
ValueFromPipeline binding, property-name binding, `PSObject`, object streaming,
multiple input stages, redirection, or additional PowerShell streams.
