# Review: New-TimeSpan Wave 2 calibration slice

Date: `2026-09-22`  
Claim reviewed: `New-TimeSpan is executable only for no-argument zero duration and generated Days/Hours/Minutes/Seconds/Milliseconds component construction; source Date/Start/End/LastWriteTime, positional, and pipeline DateTime behavior remain rejected.`  
Upstream commit: `1e53f6bbab4b8791eae782474d21889f9e5d6038`

## Evidence

- Source/provenance: `.upstream/PowerShell/src/Microsoft.PowerShell.Commands.Utility/commands/utility/NewTimeSpanCommand.cs`; generated `GeneratedCmdletPorts.NewTimeSpan`; `TimeSpanRecord` and its closed `PipelineValueAdapter` case.
- Build and tests: `dotnet build -c Release --no-restore -v:q` and `dotnet run -c Release --no-build -- --self-test` passed. The self-test covers generated contract, zero default, direct components, invariant direct formatting, closed record fields, and source-spanned invalid/overflow/deferred binding failures.
- Native AOT evidence: fresh self-contained `osx-arm64` publish to `artifacts/osx-arm64-new-timespan` passed `--self-test`. Focused native smokes emitted `1.02:03:04.0050000`, projected `Value, TotalSeconds` through the explicit adapter, and rendered the source-spanned `AOT3005` invalid-component diagnostic.
- Differential parser evidence: fixture `30-new-timespan-components.ps1` was generated from stock `pwsh`; all 30 parser differential baselines, the parser-reuse guard, and the Phase 10 campaign manifest verification passed.

## Independent verdicts

| Persona | PASS / BLOCK | Evidence-backed finding | Resolution / accepted variance |
| --- | --- | --- | --- |
| Upstream Grammar Steward | PASS | Fixture 30 was regenerated from stock `pwsh` and covers direct components, a fractional literal, projection, and the deferred date/positional forms. The implementation only consumes the existing upstream AST/lowerer contract and extends no grammar. | Direct component construction preserves the pinned source body; pipeline DateTime input remains an explicit structural-lowerer rejection, not a new parsing or lowering rule. |
| Native AOT Boundary Sentinel | PASS | `NewTimeSpanCmdlet` uses the generated opt-in descriptor for only the five source `int` components, invariant `int.TryParse`, and the static five-component `TimeSpan` constructor. `TimeSpanRecord` encapsulates the BCL value while `PipelineValueAdapter` exposes a finite declared field set; the slice adds no reflection, `dynamic`, `PSObject`, runspace, runtime compilation, or generic CLR-object path. Fresh self-contained `osx-arm64` publish and focused direct/projection/diagnostic smokes passed. | Date/positional/pipeline conversion remains outside the descriptor and fails closed at binding; no additional AOT boundary is introduced. |
| Static Data-Plane & Binder Guardian | PASS | The adapter uses the generated opt-in descriptor as its sole binding authority. `TimeSpanRecord` and its one `PipelineValueAdapter` case declare exactly eleven fields; no second binder, generic object conversion, or CLR-member fallback was added. | The synthetic `Value` and finite totals are target data-plane fields, recorded as an explicit variance rather than a claim about source `TimeSpan` members. |
| Diagnostic Experience Guardian | PASS | **Re-review:** managed self-test and focused command probes lock source-spanned `AOT3005` for invalid and fractional components, `AOT3006` for overflow, `AOT2002` for direct `Start`/`End`/`LastWriteTime`, `AOT2005` for positional input, and `AOT1001` at the pipeline `New-TimeSpan` stage. | The port note and variance ledger distinguish generated-binder date rejections from the earlier structural-pipeline rejection. |
| Compatibility Proof Adversary | PASS | **Re-review:** named component construction matches the pinned source's five-component constructor, including zero and `1.02:03:04.0050000`. The port note and variance ledger expressly record the synthetic target `Value` projection/table precision and the invariant-integer rejection of source-coercible fractional input such as `1.2`. | The support claim remains only direct integer component construction. Date/positional/pipeline input and broad PowerShell scalar coercion remain deferred. |
| Architecture Guard | PASS | `NewTimeSpanCmdlet` constructs its descriptor exclusively from `GeneratedCmdletPorts.NewTimeSpan`, admits only the five source `Time` components, and uses the established `AotCmdletBase` lifecycle. `TimeSpanRecord` encapsulates the BCL value and extends the existing closed `PipelineValueAdapter` registry with finite declared fields; it adds neither CLR-member discovery nor a second binder/data plane. The explicit boundary leaves DateTime input for a separate reviewed contract, preserving a clean future `Start-Sleep` consumer choice. | Direct zero/component behavior is source-body-equivalent; Date/Start/End/LastWriteTime, positional, and pipeline DateTime modes remain deliberately deferred and are documented in the port note and variance ledger. |

## Outcome

`PASS` — the released support claim is only no-argument zero duration and
direct integer component construction. No broader `New-TimeSpan` support is
authorized by this ledger.
