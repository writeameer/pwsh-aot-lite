# Review: New-Guid Wave 2 calibration slice

Date: `2026-09-22`
Claim reviewed: `New-Guid is executable only for default UUID v7 generation and the generated -Empty switch; InputObject and all positional/pipeline GUID input remain rejected.`
Upstream commit: `1e53f6bbab4b8791eae782474d21889f9e5d6038`

## Evidence

- Source/provenance: `.upstream/PowerShell/src/Microsoft.PowerShell.Commands.Utility/commands/utility/NewGuidCommand.cs`; generated `GeneratedCmdletPorts.NewGuid`; `CommandParameterAst.Argument` is preserved by `AotParameterArgumentPlan`.
- Build and tests: `dotnet build -c Release --no-restore -v:q` and `dotnet
  run -c Release --no-build -- --self-test` passed. The self-test covers the
  generated contract, UUID v7 format/version nibble, Guid.Empty, direct prose,
  AST-attached `-Empty:$false`, and unsupported attached/positional/InputObject
  forms.
- Native AOT evidence: fresh self-contained `osx-arm64` publish to
  `artifacts/osx-arm64-new-guid` passed `--self-test`. Focused native smokes
  verified default v7 output, exact `-Empty` Guid.Empty output,
  `-Empty:$false` v7 output, and `AOT2002` rejection of InputObject.
- Differential parser evidence: fixture `29-new-guid-switch-attachments.ps1`
  was generated from stock `pwsh`; all 29 parser differential baselines and
  the parser-reuse guard passed.

## Independent verdicts

| Persona | PASS / BLOCK | Evidence-backed finding | Resolution / accepted variance |
| --- | --- | --- | --- |
| Upstream Grammar Steward | PASS | `NewGuidCommand.ProcessRecord` in the pinned source selects `Guid.Empty` only for a true `Empty` switch and otherwise calls `Guid.CreateVersion7()`. The generated contract still records the separate positional/pipeline `InputObject` set; fixture 29 confirms the upstream parser associates `-Empty:$false` with the switch parameter. | This slice admits only the generated `Empty` surface; the parser association is preserved through the existing lowerer rather than recreated from text. |
| Native AOT Boundary Sentinel | PASS | Fresh self-contained `osx-arm64` publish and `--self-test` passed. Native probes emitted a v7 UUID by default, exact Guid.Empty for `-Empty`, a v7 UUID for `-Empty:$false`, and `AOT2002` for `-InputObject`. Review found no reflection, dynamic loading, runspace, or generic Guid-object path. | The existing closed `TextRecord` prose presentation is retained. A typed Guid pipeline value is deliberately deferred. |
| Static Data-Plane & Binder Guardian | PASS | The shared binder consumes AST-derived attached-value evidence only for a direct Boolean switch argument. It retains `false` for `-Empty:$false`, rejects string/null/non-Boolean attachments as `AOT1001`, and does not re-tokenize command text or introduce a command-local binder. | The narrowly shared repair is reusable only for already-admitted static switch parameters; no generic conversion or object adaptation was added. |
| Diagnostic Experience Guardian | PASS | Managed and fresh native probes preserve source spans and stable diagnostic IDs: unsupported named `-InputObject` is `AOT2002`, unsupported positional input is `AOT2005`, and an attached non-Boolean switch value is the explicit `AOT1001` structural-subset error with corrective guidance. `-Empty:$false` remains attached and yields a non-empty UUID v7 rather than a detached positional value. | The source's `StringNotRecognizedAsGuid` non-terminating error and null output are not impersonated: the entire `InputObject` parameter set is rejected at the generated binding boundary. This is documented as deferred rather than a compatibility claim. |
| Compatibility Proof Adversary | PASS | The pinned `NewGuidCommand.ProcessRecord` selects `Guid.Empty` only when `Empty.ToBool()` is true and otherwise calls `Guid.CreateVersion7()`. Generated metadata proves the source `Empty` switch and separate positional/pipeline `InputObject` set. Managed build/self-test, parser guard, all 29 differential fixtures, and native `--self-test` passed; direct probes verified default v7, exact empty value, attached `-Empty:$false`, and fail-closed input rejection. | The admitted surface is exactly default generation plus `-Empty`/direct Boolean attachment. `InputObject`, positional and `ValueFromPipeline` conversion, invalid-input stream behavior, and a typed Guid output pipeline are deliberate deferred variances; canonical D-format text is a terminal-presentation choice, not a CLR Guid compatibility claim. |
| Architecture Guard | PASS | `NewGuidCmdlet` reuses generated `GeneratedCmdletPorts.NewGuid.CreateAotDescriptor("Empty")`, the existing `AotCmdletBase` lifecycle, closed `TextRecord` presentation, and the upstream AST-to-single-binder route. No second grammar, dynamic engine, generic CLR-object value plane, reflection, or local parameter binder was added. The shared attached-switch change was built and self-tested: `New-Guid -Empty:$false` remains an AST-attached Boolean and emits UUID v7; non-literal `-Empty:'false'` fails with the stable AOT1001 diagnostic. | Preserve the intentionally narrow descriptor: `InputObject`, positional/pipeline Guid conversion, null output, and typed Guid pipeline objects remain deferred until a separate value-plane slice. |

## Outcome

`PASS` — the released support claim is only the stated default/-Empty
calibration slice. `InputObject`, positional input, ValueFromPipeline,
invalid-Guid error semantics, a typed Guid pipeline value, and PowerShell
format/type behavior remain deferred.
