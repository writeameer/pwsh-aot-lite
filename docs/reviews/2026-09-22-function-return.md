# Review: Bare local-function return

Date: `2026-09-22`
Claim reviewed: `A bare upstream ReturnStatementAst inside the admitted local-function body exits only that function through typed AOT control flow, preserving prior output and cancellation semantics.`
Upstream commit: `1e53f6bbab4b8791eae782474d21889f9e5d6038`

## Evidence

- Source/provenance: `ReturnStatementAst.Pipeline` is consumed from the pinned
  parser AST. `AotControlFlow` propagates only `Continue`/`Return`; no upstream
  compiler, `ScriptBlock`, exception transport, or dynamic evaluation is used.
- Build and tests: Release build and managed `--self-test` passed. `SelfTest`
  covers direct, conditional, foreach, callee-local, root/value/pipeline
  rejection, and pre-cancelled return behavior.
- Native AOT evidence: fresh self-contained `osx-arm64` publish and
  `--self-test` passed. Focused native early-return smoke emitted `Add` then
  caller `Get`, while suppressing the function tail.
- Differential parser evidence: fixtures `17`–`19` cover valid bare return,
  boundary return pipelines, and malformed return syntax; the 19-fixture
  baseline verification and parser-reuse guard passed.

## Independent verdicts

| Persona | PASS / BLOCK | Evidence-backed finding | Resolution / accepted variance |
| --- | --- | --- | --- |
| Upstream Grammar Steward | PASS | Direct upstream `ReturnStatementAst` lowering, parser reuse guard, and all 19 parser baselines passed. | Bare return is allowed only under the explicit local-function lowering context. |
| Native AOT Boundary Sentinel | PASS | Typed `AotControlFlow` propagates through existing plans and fresh native publish/self-test plus early-return smoke passed. | No dynamic engine, reflection, runspace, `PSObject`, or runtime compilation was added. |
| Static Data-Plane & Binder Guardian | PASS | Value/pipeline returns are rejected before execution and no return-value projection or secondary binder exists. | Typed function output remains a later phase. |
| Diagnostic Experience Guardian | PASS | Root, value, and pipeline return probes fail closed with source-aware `AOT1001`; cancellation retains the existing host contract. | Bare return produces no new stream or diagnostic side channel. |
| Compatibility Proof Adversary | PASS | Managed/native probes cover direct, branch, foreach, callee-local, root/value/pipeline rejection, and output preservation. | This is function-local bare return only, not general PowerShell return semantics. |
| Architecture Guard | PASS | Existing scope hierarchy, function recursion guard, cancellation checks, and output event sink are reused. | Statements after return still lower statically and may independently be rejected. |

## Outcome

`PASS — integrated` — root return, `return <value>`, `return <pipeline>`,
`break`, `continue`, `exit`, and broader function pipeline/output behavior
remain outside Phase 5 and fail closed.
