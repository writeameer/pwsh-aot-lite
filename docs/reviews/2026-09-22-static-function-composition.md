# Review: Static local-function composition and header binding

Date: `2026-09-22`
Claim reviewed: `A local function with exact closed header binding may relay one direct native-source record segment into the existing outer Where-Object/Select-Object tail.`

## Evidence

- Source/provenance: pinned upstream `FunctionDefinitionAst`, `ParameterAst`,
  `CommandAst`, and `CommandParameterAst` are lowered directly. The runner does
  not import the upstream compiler or parameter binder.
- Build and tests: Release build, managed `--self-test`, parser-reuse guard,
  22-fixture parser baseline verification, and `git diff --check` passed.
  `SelfTest` covers exact named, attached named, positional/default binding,
  transparent producer composition, cancellation-before-binding, and all
  declared fail-closed boundaries.
- Native AOT evidence: fresh self-contained `osx-arm64` publish and
  `--self-test` passed. The composition smoke projected `Get-Zones` through
  the outer typed tail; unknown named binding rendered source-aware `AOT5009`.
- Differential parser evidence: fixtures `20`–`22` cover composition,
  parameter AST forms, and malformed default syntax; stock-PowerShell
  verification matched all 22 fixtures.

## Independent verdicts

| Persona | PASS / BLOCK | Evidence-backed finding | Resolution / accepted variance |
| --- | --- | --- | --- |
| Upstream Grammar Steward | PASS | Direct pinned `FunctionDefinitionAst`/`ParameterAst`/`CommandAst` consumption, parser guard, and 22 fixture baselines passed. | No new grammar or compiler path exists. |
| Native AOT Boundary Sentinel | PASS | Fresh native publish/self-test and composition/diagnostic smokes passed without dynamic engine, reflection, runspace, `PSObject`, or runtime compilation. | Existing upstream-extraction linker warnings are unchanged. |
| Static Data-Plane & Binder Guardian | PASS | Header binding consumes only AST-derived closed values; `AotCmdletRegistry` remains the only cmdlet binder; raw typed rows reuse `PipelinePlan.ApplyTail`. | No object stream, table capture, or function pipeline input is admitted. |
| Diagnostic Experience Guardian | PASS | `AOT5009`–`AOT5012` are documented; exact renderer snapshots cover unknown named, missing named value, and non-transparent producer failures. | Rejected forms retain `AOT1001` or source-aware function-binding IDs. |
| Compatibility Proof Adversary | PASS | Managed/native probes cover named/default binding, composition, diagnostics, and cancellation-before-binding. | This is one transparent producer plus outer tail, not general PowerShell function/pipeline behavior. |
| Architecture Guard | PASS | Existing scope, recursion guard, cancellation, event/output contracts, record adapter, and transform seam are reused. | Producer eligibility remains deliberately static and narrow. |

## Outcome

`PASS — integrated` — function downstream stages, pipeline input, generalized
function output, arbitrary records/objects, aliases/abbreviations, parameter
sets, conversion, attributes/types, dynamic defaults, splatting, and return
values remain outside Phase 6.
