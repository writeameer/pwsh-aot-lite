# Review: Runtime stream contract

Date: `2026-09-22`
Claim reviewed: `The AOT host publishes one ordered typed transcript of completed output segments and non-terminating runtime diagnostics.`
Upstream commit: `1e53f6bbab4b8791eae782474d21889f9e5d6038`

## Evidence

- Design: [Engine Runtime Core](../architecture/engine-runtime-core.md) and
  [diagnostic contract](../architecture/diagnostic-contract.md). The bridge is
  `AotExecutionContext → AotRuntimeEvent → ScriptRunner`; no cmdlet writes to
  the terminal.
- Managed verification: `dotnet build --no-restore -v:q`, `dotnet run
  --no-build -- --self-test`, parser reuse guard, parser baseline verification,
  and `git diff --check` passed. The self-test
  proves Success/Error/Success ordering across real commands, monotonically
  increasing sequence numbers, subscription/history equality, active command
  source context, no error replay, and error-before-completed-segment behavior.
- Native AOT evidence: a fresh self-contained `osx-arm64` publish with the
  documented Homebrew OpenSSL/Brotli `LIBRARY_PATH` passed `--self-test`.
  The expected linker deployment-target warnings are external Homebrew dylib
  metadata, not runner diagnostics.
- Parser evidence: no parser, lexer, AST, or accepted-syntax change.

## Independent verdicts

| Persona | PASS / BLOCK | Evidence-backed finding | Resolution / accepted variance |
| --- | --- | --- | --- |
| Native AOT Boundary Sentinel | **PASS** | The event model uses closed typed records, lists, and delegates only; review found no new reflection, dynamic loading, runtime code generation, or PowerShell SDK dependency. Managed and fresh native AOT self-tests passed. | Native stream semantics remain explicitly narrow. |
| Static Data-Plane & Binder Guardian | **PASS** | The bridge transports existing `AotExecutionOutput`, `AotDiagnostic`, and source spans only; it adds no object plane, second binder, registry, metadata, or parser path. | Generic static stage handoff is a later slice. |
| Diagnostic Experience Guardian | **PASS** | Error events render exactly once at their emission boundary, preserve their diagnostic/span, and avoid host replay of `Errors`. The second-stage fixture proves input-stage provenance. | Completed success segments intentionally have no command attribution yet. |
| Compatibility Proof Adversary | **PASS** | Self-test proves ordered Success/Error/Success behavior, error-before-completed-segment behavior, monotonic sequences, subscriber/history agreement, and active source spans; host smoke test kept executing after the error. | No object-level streaming or PowerShell preference/stream behavior is claimed. |
| Architecture Guard | **PASS** | One context-owned event path serves host and programmatic consumers; no cmdlet writes to a terminal and the contract documents finite follow-on slices. | Before widening events, make invalid kind/payload states unconstructable and define any replay behavior. |

## Outcome

`integrated — ordered completed-output/error runtime bridge` — this must not be described as support for individual object
streaming, `$Error`, preference variables, redirection, extra PowerShell
streams, remoting, concurrency, or cancellation.
