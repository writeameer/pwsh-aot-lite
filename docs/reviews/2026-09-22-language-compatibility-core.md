# Review: Language Compatibility Core — variables and top-level statements

Date: `2026-09-22`  
Claim reviewed: `The AOT host may execute the documented first lexical-variable slice: ordered, unnamed top-level '=' assignments with closed literal/list/direct-variable expressions, followed by the existing structural command-pipeline subset.`  
Upstream commit: `1e53f6bbab4b8791eae782474d21889f9e5d6038`

## Evidence

- Design and provenance: [Language Compatibility Core](../architecture/language-compatibility-core.md),
  [AOT Execution Kernel](../architecture/aot-execution-kernel.md),
  [parser-reuse guard](../../PARSER-REUSE-GUARD.md), and the pinned
  [upstream extraction ledger](../../language/UPSTREAM.md).
- Managed verification: `dotnet build -c Release --no-restore` completed with
  zero warnings/errors; `dotnet run -c Release --no-build -- --self-test`
  passed. The self-test includes variable lists, case-insensitive scoped
  lookup, fresh-command isolation, explicit REPL reuse, use-site diagnostics,
  parameter-injection resistance, fail-closed syntax, and a regression proving
  that a completed pipeline is written before a later terminating statement.
- Parser evidence: `pwsh -NoProfile -File tools/Test-ParserReuseGuard.ps1`
  passed; `pwsh -NoProfile -File tools/Export-PwshParserBaseline.ps1 -Verify`
  matched all seven stock-`pwsh` token/AST/diagnostic fixtures, including the
  new assignment, variable-form, and malformed-assignment fixtures.
- Native AOT evidence: fresh `osx-arm64` self-contained publish (with the
  documented Homebrew OpenSSL/Brotli `LIBRARY_PATH`) passed `--self-test`.
  The published executable ran a variable-driven `Get-Process |
  Where-Object | Select-Object` pipeline, produced source-precise `AOT5001`
  for an undefined variable, and emitted the successful `Get-Verb` table
  before a following undefined-variable failure. Homebrew minimum-macOS linker
  warnings are environmental and do not affect the host build result.

## Independent verdicts

| Persona | PASS / BLOCK | Evidence-backed finding | Resolution / accepted variance |
| --- | --- | --- | --- |
| Upstream Grammar Steward | **PASS** | The lowerer consumes the pinned upstream AST only; no lexer, parser, or source splitting was introduced. The seven-fixture differential harness passed. | Parsing fidelity is upstream-backed; execution remains the explicitly documented subset. |
| Language Tooling Contract Guardian | **PASS** | The parser facade remains parse-only and exposes the same AST/token/extent path to future tooling. The execution plan does not create a tool-specific grammar. | Editor/highlighting features are not implemented by this slice. |
| Native AOT Boundary Sentinel | **PASS** | Block, expression, and scope plans use closed static types. No dynamic execution, reflection dispatch, session state, module/assembly load, or runtime code generation is reachable. Native publish/self-test passed. | Dynamic PowerShell engine features remain excluded. |
| Static Data-Plane & Binder Guardian | **PASS** | Scope stores immutable closed `AotValue`; the finite argument converter is the only value-to-binder boundary; existing generated metadata and `AotCmdletRegistry` remain the sole binder. | Lists expand only as value atoms for an explicit AST parameter; they cannot create parameters. |
| Diagnostic Experience Guardian | **PASS** | `AOT5001`–`AOT5004` are typed, documented, source-spanned, and actionable; the host renders typed errors rather than raw exceptions. | Existing unrelated legacy-port diagnostics remain on their documented migration path. |
| Compatibility Proof Adversary | **PASS** | A completed output segment is now emitted immediately at the host boundary, then a later terminating error is rendered with exit 2. Scope order, isolation, and failure behavior passed independent checks. | Output is intentionally non-transactional, matching ordered statement execution. |
| Architecture Guard | **PASS** | The flow stays separated as `upstream AST → block/expression plan → explicit scope → finite conversion → existing registry binder`; existing value, pipeline, binder, and diagnostic services are reused. | Child scopes, control flow, and local functions require their own reviewed extension. |

## Outcome

`integrated — first lexical-variable subset` — this is not general PowerShell
script execution. The supported form is direct unnamed top-level `=` assignment
of closed literals/lists/direct variables and the existing structural pipeline,
with direct variable use only in command value positions and the numeric
`Where-Object` RHS. It deliberately excludes SessionState, scoped/automatic
variables, splatting, interpolation, arbitrary expressions, assignment from
commands, control flow, functions, redirection, and dynamic evaluation.
