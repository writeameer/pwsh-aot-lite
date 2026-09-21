# Review: Parser-driven host and REPL projection

Date: `2026-09-22`
Claim reviewed: `The bounded Phase-3 host projects the one ordered runtime transcript to injected stdout/stderr writers, and the Console.ReadLine REPL uses only the pinned upstream parser's incomplete-input result to collect and execute multiline source.`
Upstream commit: `1e53f6bbab4b8791eae782474d21889f9e5d6038`

## Evidence

- Source/provenance: `AotScriptParser` copies `ParseError.IncompleteInput`
  from the pinned upstream parser result; `AotReplInputBuffer` has no token or
  delimiter scanner. `AotExecutionKernel.Compile(AotParseResult)` lowers the
  exact parser result accepted by the REPL. `AotTerminalEventProjector` is the
  sole `ScriptRunner` event subscription and receives `TextWriter` instances
  explicitly.
- Build and tests: `dotnet build -c Release --no-restore -v:q` and `dotnet run
  -c Release --no-build -- --self-test` cover a trailing-pipe continuation,
  multi-line source preservation, malformed-input reset, same-result lowering,
  stdout/stderr event projection, ANSI diagnostics, and EOF-style incomplete
  diagnostic handling.
- Native AOT evidence: fresh `osx-arm64` self-contained publish followed by
  `PwshAotLite --self-test` passed. Native stdin probes verified both a
  `Get-Verb |` / `Select-Object Verb` multiline submission and EOF rendering
  of a trailing-pipe diagnostic.
- Differential parser evidence: `Test-ParserReuseGuard.ps1` passed and
  `Export-PwshParserBaseline.ps1 -Verify` passed all 13 fixtures. The slice
  accepts no syntax and changes no parser/upstream extraction.

## Independent verdicts

| Persona | PASS / BLOCK | Evidence-backed finding | Resolution / accepted variance |
| --- | --- | --- | --- |
| Upstream Grammar Steward | PASS | The facade forwards upstream `ParseError.IncompleteInput`; no delimiter scanner, alternate lexer, or new accepted syntax exists. The baseline verifier passed all 13 fixtures. | EOF reparses unchanged buffered source through the same facade; identity caching is deferred because it changes no grammar behavior. |
| Language Tooling Contract Guardian | PASS | The REPL passes a completed `AotParseResult` to lowering unchanged; AST, tokens, extents, diagnostics, document identity, and parse-only access remain intact. | Editable raw-key highlighting remains explicitly deferred. |
| Diagnostic Experience Guardian | PASS | One projector sends completed output segments to stdout and typed diagnostics to stderr; malformed, unsupported, runtime, and EOF diagnostic probes were exactly-once. | A blank physical continuation line after `Get-Verb |` becomes the parser's blocking `EmptyPipeElement` error and clears the buffer. This is intentional parser-driven behavior, not a host heuristic. |
| Native AOT Boundary Sentinel | PASS | Writer injection and event projection add no dynamic execution, reflection-based discovery, or PowerShell SDK crossing. Fresh self-contained `osx-arm64` publish and native self-test passed. | Existing attributed parser extraction warnings remain tracked separately; this slice adds none. |
| Compatibility Proof Adversary | PASS | Live native multiline, malformed, EOF, output/error-routing, ANSI, and exit-code probes match the bounded Phase-3 claim. | The claim excludes a full PowerShell stream model, PSReadLine, raw editor/history, signal policy, redirection, jobs, and concurrency. |

## Outcome

`PASS — integrated` — the Phase-3 host claim is limited to the reviewed ordered
success/error transcript, typed stage contract, cooperative lifecycle, and
parser-driven `Console.ReadLine` projection. Raw-key editing/highlighting, all
PowerShell streams and redirection, `ErrorRecord`/preference variables,
terminal signal wiring, jobs, and concurrent execution remain outside Phase 3.
