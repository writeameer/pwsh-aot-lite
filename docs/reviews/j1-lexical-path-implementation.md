# Review: J1 lexical path implementation

Date: `2026-09-24`  
Claim reviewed: `Join-Path` and `Split-Path` are bounded POSIX-v1 lexical-text
adapters with no filesystem/provider authority.

## Evidence

- Source/provenance: generated `GeneratedCmdletPorts.JoinPath` / `.SplitPath`;
  upstream `CombinePathCommand.cs` / `ParsePathCommand.cs`.
- Managed: `dotnet build -c Release --no-restore` and `dotnet run -c Release -- --self-test` passed.
- Parser/campaign: parser reuse guard, 36-fixture parser baseline, Phase 10 campaign
  and queue verification passed.
- Native: fresh `osx-arm64` publish at `artifacts/osx-arm64-j1-final/PwshAotLite`;
  self-test and Join/Split smoke commands passed. SHA-256:
  `863e45b99eebf0d7d310e93e71895878f6540afb923877ec9bb0f53d04a3bd3b`.
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
