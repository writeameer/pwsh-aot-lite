# Review: first generic value-plane migration

Date: `2026-09-21`  
Claim reviewed: `The existing upstream-AST structural pipeline may execute a direct finite-numeric Where-Object predicate and direct Select-Object projection through explicit, closed AotValue/AotRecord adapters, while cmdlet business logic remains typed.`  
Upstream commit: `1e53f6bbab4b8791eae782474d21889f9e5d6038`

## Evidence

- Source/provenance: [value-plane design](../architecture/value-plane.md),
  [first-migration design](../architecture/value-plane-first-migration.md),
  [parser reuse guard](../../PARSER-REUSE-GUARD.md), and the pinned
  [upstream extraction ledger](../../language/UPSTREAM.md).
- Build and tests: `dotnet build -c Release` completed with 0 warnings/errors;
  `dotnet run -c Release -- --self-test` passed. Self-test covers value
  immutability, case-insensitive lookup, ordered projection, finite comparison,
  adapter-only fields, missing fields, duplicate case-insensitive fields, and
  `NaN`/infinity rejection.
- Native AOT evidence: a fresh self-contained `osx-arm64` publish passed
  `--self-test`; lower-case `Get-TimeZone -ListAvailable | Where-Object
  baseutcoffsetminutes -ge 0 | Select-Object id, baseutcoffsetminutes` passed.
  Missing projection, `NaN`/infinity predicates, duplicate `id, ID`, script
  blocks, and an excess pipeline stage fail with stable exit-2 diagnostics.
  Homebrew OpenSSL/Brotli minimum-macOS linker warnings are pre-existing.
- Differential parser evidence: `tools/Test-ParserReuseGuard.ps1` passed and
  `tools/Export-PwshParserBaseline.ps1 -Verify` matched all four checked-in
  stock-`pwsh` token/AST/diagnostic fixtures.

## Independent verdicts

| Persona | PASS / BLOCK | Evidence-backed finding | Resolution / accepted variance |
| --- | --- | --- | --- |
| Upstream Grammar Steward | **N/A** | No grammar or lowerer acceptance change was introduced; input remains the pinned upstream AST path. | The existing structural syntax scope is unchanged. |
| Native AOT Boundary Sentinel | **PASS** | The closed adapter type-switch and compatibility renderer contain no reflection, `object` wrapper, `PSObject`, dynamic loading, or runtime code generation. The duplicate/finite-literal corrections are static collection/numeric checks only. | Unknown typed rows fail closed; general object adaptation remains excluded. |
| Static Data-Plane & Binder Guardian | **PASS** | Typed cmdlets cross one explicit adapter into immutable case-insensitive records. Projection is record-authoritative, preserves requested field order/casing, rejects missing and duplicate case-insensitive fields stably, and does not add a binder. | Direct finite numeric predicates and direct projection only; no general pipeline binding or ETS coercion. |
| Compatibility Proof Adversary | **PASS** | Native positive case-insensitive filter/projection and requested order passed. Missing field, `NaN`/infinity, duplicate projection, script block, and fourth-stage cases fail closed with stable exit-2 diagnostics. | This does not imply general PowerShell pipeline compatibility. |

## Outcome

`integrated — limited generic value-plane slice` — the existing structural
runner now uses the closed `AotValue`/`AotRecord` boundary for its direct
finite-numeric `Where-Object` and direct `Select-Object` stages. Cmdlet logic
and primary output remain typed; reflection-based object adaptation, script
blocks, conversion/member enumeration, arbitrary command pipelines, and full
PowerShell ETS semantics remain unsupported.
