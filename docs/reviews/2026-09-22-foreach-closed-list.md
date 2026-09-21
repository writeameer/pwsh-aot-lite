# Review: Closed-list foreach execution

Date: `2026-09-22`  
Claim reviewed: `The AOT host may execute the documented unlabeled, synchronous foreach subset when its source is a direct closed expression resolving to an AotValue list.`  
Upstream commit: `1e53f6bbab4b8791eae782474d21889f9e5d6038`

## Evidence

- Design and provenance: [Language Compatibility Core](../architecture/language-compatibility-core.md),
  [AOT Execution Kernel](../architecture/aot-execution-kernel.md),
  [parser-reuse guard](../../PARSER-REUSE-GUARD.md), and the pinned
  [upstream extraction ledger](../../language/UPSTREAM.md). The lowerer consumes
  upstream `ForEachStatementAst`; the parser semantic extraction preserves the
  upstream parallel/throttle diagnostics without importing any execution engine.
- Managed verification: `dotnet build -c Release --no-restore` completed with
  zero host warnings/errors; `dotnet run -c Release --no-build -- --self-test`
  passed; `git diff --check` passed. Self-test covers direct and variable lists,
  source snapshotting, shared scope/final variable, empty and null entries,
  nested same-name loops, ordered output, earlier output before later failure,
  target validation, and stable `AOT5006` diagnostics.
- Parser evidence: `pwsh -NoProfile -File tools/Test-ParserReuseGuard.ps1`
  passed; `pwsh -NoProfile -File tools/Export-PwshParserBaseline.ps1 -Verify`
  matched 13 stock-pwsh fixtures; and `dotnet run -c Release --project language
  --no-restore` matched all 13 token/AST/diagnostic baselines. Fixture 12 proves
  `KeywordParameterReservedForFutureUse` and
  `ThrottleLimitRequiresParallelFlag` parity for foreach options.
- Native AOT evidence: fresh `osx-arm64` self-contained publish (with the
  documented Homebrew OpenSSL/Brotli `LIBRARY_PATH`) passed `--self-test` and
  emitted ordered `Add`/`Get` table output for a closed-list foreach. A scalar
  collection produced source-spanned `AOT5006` and process exit 2. Homebrew
  minimum-macOS linker warnings are external deployment-target warnings only.

## Independent verdicts

| Persona | PASS / BLOCK | Evidence-backed finding | Resolution / accepted variance |
| --- | --- | --- | --- |
| Upstream Grammar Steward | **PASS** | `ForEachStatementAst`, flags, throttle, labels, and malformed forms are sourced from the pinned parser. The extracted foreach semantic checks now preserve all 13 differential fixtures. | Parser acceptance remains distinct from executable-subset admission. |
| Language Tooling Contract Guardian | **PASS** | The shared parser now retains token/AST/diagnostic parity for the new foreach fixtures; no second lexer or execution-aware tooling path was introduced. | Future interactive highlighting still projects the shared parser only. |
| Native AOT Boundary Sentinel | **PASS** | Iteration consumes immutable `AotValue` lists only and never calls CLR enumeration, reflection, runspaces, dynamic binding, or runtime code generation. | Arbitrary `IEnumerable`, pipeline records, scalar/null source, and command source remain excluded. |
| Static Data-Plane & Binder Guardian | **PASS** | The plan evaluates the source once, writes closed items to the existing scope, forwards the existing output sink, and leaves every command on `AotCmdletRegistry`. | There is no loop-specific binder or implicit record adapter. |
| Diagnostic Experience Guardian | **PASS** | Non-list source is source-spanned `AOT5006`; unsupported AST forms remain typed `AOT1001`; snapshot coverage proves the help/label contract. | Scalar PowerShell enumeration is an explicit future compatibility decision, not a silent approximation. |
| Compatibility Proof Adversary | **PASS** | Source snapshot, ordered output, same-scope assignment/final loop variable, empty list preservation, nested-variable semantics, null list items, and error/output ordering are covered. | The claim is closed-list synchronous foreach only—not general PowerShell enumeration. |
| Architecture Guard | **PASS** | The implementation reuses `ForEachStatementAst → AotExpressionPlan/AotValue → AotBlockPlan.ExecuteInto → AotCmdletRegistry`; no duplicate binder or dynamic object plane emerged. | Upstream parser changes and any collection widening require a fresh policy/review pass. |

## Outcome

`integrated — closed-list synchronous foreach`.
This does not support command/pipeline collection sources, scalar or arbitrary
object enumeration, ranges, labels, parallel/throttle execution, automatic
`$foreach`, or flow-control statements. Those forms remain upstream-parsed but
explicitly fail closed until a separate designed, reviewed runtime contract
exists.
