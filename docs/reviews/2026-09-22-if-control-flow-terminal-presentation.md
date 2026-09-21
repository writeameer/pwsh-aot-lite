# Review: Conditional execution and terminal presentation

Date: `2026-09-22`  
Claim reviewed: `The AOT host may execute the documented closed if/elseif/else slice and render its structured diagnostics with the documented ANSI policy.`  
Upstream commit: `1e53f6bbab4b8791eae782474d21889f9e5d6038`

## Evidence

- Design and provenance: [Language Compatibility Core](../architecture/language-compatibility-core.md),
  [Terminal diagnostic presentation](../architecture/terminal-presentation.md),
  [Language tooling contract](../architecture/language-tooling-contract.md),
  [diagnostic contract](../architecture/diagnostic-contract.md), and the pinned
  [upstream extraction ledger](../../language/UPSTREAM.md). Conditions are lowered
  solely from upstream `IfStatementAst`/`CommandExpressionAst` nodes; no lexer or
  parser was added.
- Managed verification: `dotnet build -c Release --no-restore` completed with
  zero warnings/errors; `dotnet run -c Release --no-build -- --self-test` passed;
  `git diff --check` passed. Self-test covers selected/skip/elseif branches,
  same-scope assignment, case-sensitive comparison, segmented and streamed output,
  unsupported conditions, redirection shapes, terminal policy truth table, forced
  ANSI, source-control-character sanitization, and symmetric reserved-variable
  read/assignment rejection.
- Parser evidence: `pwsh -NoProfile -File tools/Test-ParserReuseGuard.ps1`
  passed; `pwsh -NoProfile -File tools/Export-PwshParserBaseline.ps1 -Verify`
  matched all 10 stock-`pwsh` token/AST/diagnostic fixtures, including three
  conditional fixtures.
- Native AOT evidence: fresh `osx-arm64` self-contained publish with the
  documented Homebrew OpenSSL/Brotli `LIBRARY_PATH` passed `--self-test`.
  The published executable selected the `$threshold -gt 0` branch, rendered
  forced-color `AOT5002` for `$PID = 1`, and independently rejected `$OFS`.
  Homebrew minimum-macOS linker warnings are environmental deployment warnings;
  the host publish completed successfully.

## Independent verdicts

| Persona | PASS / BLOCK | Evidence-backed finding | Resolution / accepted variance |
| --- | --- | --- | --- |
| Upstream Grammar Steward | **PASS** | `IfStatementAst` and direct condition forms are consumed from the pinned upstream AST; fixtures 08–10 matched the stock parser. | General expression grammar remains parsed but fail-closed. |
| Language Tooling Contract Guardian | **PASS** | The parser facade remains the shared AST/token/extent source. Terminal presentation consumes diagnostics only; no second lexer or execute-to-highlight path was introduced. | Interactive editable-buffer highlighting is deliberately deferred to a raw-key editor using this same parser projection. |
| Native AOT Boundary Sentinel | **PASS** | Closed plan/value types, static terminal policy, and a complete pinned `SpecialVariables.cs` transcription avoid dynamic engine, reflection, session state, and runtime code generation. Fresh native publish passed. | Host/profile/event variables fail with `AOT5002` until a designed host contract exists. |
| Static Data-Plane & Binder Guardian | **PASS** | Conditions evaluate closed `AotValue` values and arguments still cross one finite converter into the existing generated-metadata registry binder. | No coercion, collection comparison, or object binding is claimed. |
| Diagnostic Experience Guardian | **PASS** | `AOT5005` has source spans and a snapshot; rejected post-conditional redirection yields typed `AOT1001`; forced ANSI and control-character sanitization passed. | ANSI styles are presentation-only and can be disabled deterministically. |
| Compatibility Proof Adversary | **PASS** | Branch laziness, source ordering, same-scope assignment, output-before-later-error behavior, redirection failure, and all 10 parser fixtures passed. | Only Boolean/direct-comparison conditions are supported; loops, functions, general expressions, and redirection remain excluded. |
| Architecture Guard | **PASS** | The flow stays `upstream AST → closed block/condition plan → explicit scope → finite conversion → existing registry binder`; output-sink reuse preserves segment ordering and terminal concerns stay host-owned. | The complete reserved-variable catalog is a versioned static policy; future upstream changes require an explicit policy review. |

## Outcome

`integrated — closed conditional execution and safe terminal diagnostic presentation`.
This is not general PowerShell script execution or an interactive syntax editor.
Supported control flow is `if`/`elseif`/`else` with direct Boolean or closed
comparison conditions only. ANSI affects diagnostic rendering only; tables and
completion stay plain data.
