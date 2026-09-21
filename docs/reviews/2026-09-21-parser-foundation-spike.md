# Review: parser foundation spike

> Historical foundation review. Its statement that `AotValue` was not yet used
> by ports was true at the time; the first review-pending adapter migration is
> documented in [value-plane-first-migration.md](../architecture/value-plane-first-migration.md).

Date: `2026-09-21`  
Claim reviewed: `Whether the parser proof and closed value model may become the production AOT execution path.`  
Upstream commit: `1e53f6bbab4b8791eae782474d21889f9e5d6038`

## Evidence

- Source/provenance: [frontend proof origin](../../frontend-spike/UPSTREAM-ORIGIN.md),
  [parser reuse guard](../../PARSER-REUSE-GUARD.md), and
  [value-plane design](../architecture/value-plane.md).
- Build and tests:
  `dotnet build PwshAotLite.csproj -c Release`,
  `dotnet run --project PwshAotLite.csproj -c Release -- --self-test`, and
  `pwsh tools/Test-ParserReuseGuard.ps1` passed from an isolated working
  directory (the upstream checkout itself pins an unavailable .NET 11 preview).
- Native AOT evidence: both the host and proof published for `osx-arm64` and
  passed `--self-test`; host publishing uses the documented Homebrew
  `LIBRARY_PATH` for OpenSSL/Brotli.
- Differential parser evidence: intentionally absent. The proof was not an
  upstream AST extraction, so it cannot be admitted to the production path.

## Independent verdicts

| Persona | PASS / BLOCK | Evidence-backed finding | Resolution / accepted variance |
| --- | --- | --- | --- |
| Upstream Grammar Steward | **BLOCK** | `frontend-spike/PowerShellPipelineLexer.cs` and `PowerShellPipelineParser.cs` are a reduced handwritten grammar. It mishandles valid syntax such as single-quote escaping and treats script-block syntax as loose command words. | Archive the proof; extract the pinned upstream tokenizer/parser/AST closure before production lowering. |
| Native AOT Boundary Sentinel | **PASS** | The proof and dormant `AotValue` have no SMA reference, `PSObject`, runspace, compiler, runtime assembly load, or code generation path. Native publishing succeeded. | Keep the same boundary scan for the upstream extraction. |
| Static Data-Plane & Binder Guardian | **PASS, foundation only** | `AotValue` is closed and reflection-free, but it is not used by ports yet; its byte and collection invariants require focused tests before an adapter adopts it. | Add invariant tests and explicit typed output adapters in the first pipeline migration. |
| Compatibility Proof Adversary | **BLOCK** | The proof demonstrates AOT buildability, not PowerShell grammar compatibility; no differential AST/token/diagnostic evidence exists. | Do not call parser support implemented. Run corpus-based differential tests for each adopted syntax slice. |

## Outcome

`experiment-only` — the proof is excluded from the host project. The value
model remains a dormant foundation. The next implementation step is a real,
attributed upstream language extraction behind the parser-reuse guard.
