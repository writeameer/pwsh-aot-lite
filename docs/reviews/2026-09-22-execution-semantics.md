# Review: Static execution semantics

Date: `2026-09-22`
Claim reviewed: `Pending independent review: the AOT runner supports one static per-invocation ErrorAction policy and explicitly emitted verbose/debug typed side streams without widening the object or dynamic execution boundary.`
Upstream commit: `1e53f6bbab4b8791eae782474d21889f9e5d6038`

## Evidence

- Source/provenance: `CommandParameterAst.Argument` in the pinned upstream
  parser remains associated with `AotParameterArgumentPlan`; no command text
  is reparsed. Runtime events are factory-only and reject invalid payload,
  blank side-message, non-positive sequence, and zero-row-success states.
  Common extraction occurs only after static executable-registry availability;
  catalog-only/unknown names retain `AOT2001`.
- Design: [Engine Runtime Core](../architecture/engine-runtime-core.md) and
  [Language Compatibility Core](../architecture/language-compatibility-core.md).
- Build and tests: `dotnet build -c Release --no-restore -v:q` and `dotnet
  run -c Release --no-build -- --self-test` passed, including Continue,
  SilentlyContinue, Stop/projector-owned typed terminating event, `-ea`, Boolean-only attached
  switch, side-stream sanitization/cancellation, zero-row suppression, and
  fail-closed policy assertions. Parser reuse guard passed.
- Native AOT evidence: a fresh self-contained `osx-arm64` publish to
  `artifacts/osx-arm64-phase8-execution` passed `--self-test`; focused
  SilentlyContinue and `-ea Stop` smoke commands produced the documented
  suppression/terminating diagnostic behavior. The inherited upstream-parser
  nullability and Homebrew dylib deployment-target warnings remain external to
  this slice.
- Differential parser evidence: fixtures `27` and `28` were generated from
  stock `pwsh`; all 28 parser differential baselines verify.

## Independent verdicts

| Persona | PASS / BLOCK | Evidence-backed finding | Resolution / accepted variance |
| --- | --- | --- | --- |
| Upstream Grammar Steward | PASS | Attached parameter expressions stay in the reused upstream AST and catalog/unknown command failures retain `AOT2001` precedence. | No parser or grammar widened. |
| Native AOT Boundary Sentinel | PASS | The registry remains the sole dispatch boundary; no reflection, runspace, runtime compilation, `PSObject`, or dynamic discovery was introduced. | Common-policy extraction is static and local to a bound native command. |
| Static Data-Plane & Binder Guardian | PASS | Event payloads are factory-validated; attached switch values are direct Boolean literals only; empty successes are rejected at the event boundary. | Unsupported common bindings fail closed. |
| Diagnostic Experience Guardian | PASS | `Stop` emits one typed terminating event and one rendered diagnostic; side messages are sanitized and cancellation-checked. | Preference variables and unadmitted stream policies remain explicit exclusions. |
| Compatibility Proof Adversary | PASS | Continue/SilentlyContinue/Stop, `-ea`, unknown-command precedence, terminal suppression, and all 28 parser fixtures were exercised on managed and native artifacts. | This is a deliberately narrow static contract, not full PowerShell stream compatibility. |
| Architecture Guard | PASS | Runtime, projection, common-parameter binding, and terminal sanitization retain one-way ownership with no compatibility façade. | Docs record the boundary and variances. |

## Outcome

`PASS — integrated` — the phase admits only static native-command
`-ErrorAction`/`-ea` policies `Continue`, `SilentlyContinue`, and `Stop`, plus
Boolean-literal `-Verbose`/`-Debug`. All other common parameters, preference
variables, `$Error`, stream-variable bindings, redirection/merging, full
warning/information/progress streams, and native common bindings on local
functions remain explicitly unsupported.
