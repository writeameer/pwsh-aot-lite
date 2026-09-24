# Review: J1 lexical path implementation

Date: `2026-09-24`  
Claim reviewed: `Join-Path` and `Split-Path` are bounded POSIX-v1 lexical-text
adapters with no filesystem/provider authority.

## Evidence

- Source/provenance: generated `GeneratedCmdletPorts.JoinPath` / `.SplitPath`;
  upstream `CombinePathCommand.cs` / `ParsePathCommand.cs`.
- Managed: `dotnet build -c Release --no-restore` and `dotnet run -c Release -- --self-test` passed.
- Corpus: `tests/j1-lexical-path-corpus.json` is embedded and executed by
  `AssertJ1Corpus`; it mechanically enforces exactly 27 cases (20 child, two
  mixed-invalid atomic, five real named-field typed-pipeline property-binding
  probes that fail closed before any property conversion).
- Parser/campaign: parser reuse guard, 37-fixture parser baseline, Phase 10 campaign
  and queue verification passed.
- Native: fresh `osx-arm64` publish at `artifacts/osx-arm64-j1-final3/PwshAotLite`;
  self-test and Join/Split smoke commands passed. SHA-256:
  `54431bfc3565cf6e34790eb5b955e28de75ff58c7b82cff2107db2b3be3cffe6`.
- Replacement exceptions: `j1-lexical-no-provider-authority`,
  `j1-join-static-binder`, `j1-split-static-selector`, and
  `j1-lexical-text-record` in `port-variances.json`.

## Independent verdicts

| Persona | Verdict | Finding |
| --- | --- | --- |
| Native AOT Boundary Sentinel | PASS | Pure value-plane path operations; generated metadata and sole registry binder retained. |
| Static Data-Plane & Binder Guardian | PASS | J1 group preservation is descriptor-scoped; legacy lowering/binding remains unchanged. |
| Diagnostic Experience / Compatibility Guardian | PASS | Exact rejected-parameter and second-selector spans, pipeline conflict rejection, trailing child separator, and atomic lexical validation were replayed. |

## Accepted boundary

This is not provider, drive, wildcard, current-location, Windows, or generic
PowerShell path compatibility. The two cmdlet notes and variance ledger are the
authority for the admitted subset.
