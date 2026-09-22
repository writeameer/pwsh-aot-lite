# Review: Named local functions

Date: `2026-09-22`
Claim reviewed: `The Native AOT kernel executes a narrow, pre-lowered subset of sequential root local-function declarations and direct positional calls, with child caller scope and ordered output forwarding.`
Upstream commit: `1e53f6bbab4b8791eae782474d21889f9e5d6038`

## Evidence

- Source/provenance: `FunctionDefinitionAst` and `ParameterAst` are consumed
  directly from the pinned upstream parser extraction. `AotLocalFunctionPlan`
  is an immutable execution plan; it never invokes upstream compiler or
  `ScriptBlock` APIs.
- Build and tests: `dotnet build -c Release --no-restore -v:q` and
  `dotnet run -c Release --no-build -- --self-test` passed. `SelfTest` covers declaration
  order, direct positional binding, caller reads/local writes, case-insensitive
  native-command shadowing, REPL-scope persistence, output ordering, wrong
  arity, named arguments, function pipelines, recursion, and rejected advanced
  definitions.
- Native AOT evidence: fresh self-contained `osx-arm64` publish to
  `artifacts/osx-arm64-phase4-functions` passed `--self-test`. Native probes
  passed for direct declaration/invocation, multiline REPL declaration followed
  by a later-session call, and the typed `AOT5007` recursion diagnostic.
- Differential parser evidence: fixtures `14`–`16` add valid, boundary, and
  malformed function shapes; `Export-PwshParserBaseline.ps1 -Verify` passed all
  16 fixtures and `Test-ParserReuseGuard.ps1` passed.

## Independent verdicts

| Persona | PASS / BLOCK | Evidence-backed finding | Resolution / accepted variance |
| --- | --- | --- | --- |
| Upstream Grammar Steward | PASS | The lowerer directly consumes pinned `FunctionDefinitionAst`/`ParameterAst`; 16 upstream parser baselines and the parser-reuse guard pass. | Root-only, sequential declaration is a deliberate feature policy, not a grammar change. |
| Native AOT Boundary Sentinel | PASS | Fresh native publish/self-test and native function/REPL/recursion probes pass; changed production files add no runspace, reflection, `PSObject`, dynamic compiler, or runtime code generation. | Existing attributed parser-extraction warnings are not introduced by this slice. |
| Static Data-Plane & Binder Guardian | PASS | Calls pass one evaluated closed `AotValue` per positional source argument; native cmdlets still use the existing generated-metadata binder. | Full PowerShell argument unrolling/coercion, named parameters, aliases, switches, and parameter sets remain excluded. |
| Diagnostic Experience Guardian | PASS | Wrong arity (`AOT5008`), recursion (`AOT5007`), named arguments/pipelines (`AOT1001`), and scope errors are typed, source-aware, and rendered through the existing stderr projector. | Function-specific labels can evolve with later parameter/return work without changing the stable IDs. |
| Compatibility Proof Adversary | PASS | Native and managed probes confirm declaration-before-use, case-insensitive shadowing, caller reads/local writes, session persistence, output order, and fail-closed advanced forms. | This is not a general PowerShell function claim; exact source-argument arity is intentionally narrower than PowerShell. |
| Architecture Guard | PASS | Immutable function plans, `AotScope`, `AotExecutionContext`, and the existing output sink are reused; no parallel function runtime or command registry was created. | `Get-Command`/`Get-Help` remain static catalog control-plane features in this slice. |

## Outcome

`PASS — integrated` — this Phase 4 claim excludes filters/workflows, nested or conditional
definitions, attributes/types/defaults/body `param`, named/splatted arguments,
`$args`, `$input`, `$PSBoundParameters`, value/pipeline or root `return`, recursion, function
pipelines, and control-plane discovery through `Get-Command` or `Get-Help`.
