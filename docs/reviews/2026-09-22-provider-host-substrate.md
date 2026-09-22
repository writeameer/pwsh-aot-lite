# Review: Provider and host substrate

Date: `2026-09-22`
Claim reviewed: `Reviewed native adapters and metadata control-plane projections receive a fixed, injectable AOT-safe substrate for already-admitted physical file, process-inspection, time/globalization, terminal, platform, closed configuration, and captured discovery-root dependencies; credentials and network are typed unavailable capabilities.`
Upstream commit: `1e53f6bbab4b8791eae782474d21889f9e5d6038`

## Evidence

- Source/provenance: `HostSubstrate.cs`; current portable seams in
  `Pipeline.cs`, installation safety seam in `ModuleInstaller.cs`; upstream
  behavior references are `Process.cs`, `GetHash.cs`, `EnvironmentProvider.cs`,
  and the explicitly excluded `SessionStateProviderBase.cs`,
  `FileSystemProvider.cs`, `Credential.cs`, and `NativeCommandProcessor.cs`.
- Build and tests: `dotnet build -c Release --no-restore -v:q` and `dotnet
  run -c Release --no-build -- --self-test` passed. The self-test exercises
  local composition, injected terminal/configuration/discovery-root fakes,
  throwing terminal probes, platform-gated owner lookup, injected installer
  roots, and injected help/module/repository catalogs.
- Native AOT evidence: fresh self-contained `osx-arm64` publish to
  `artifacts/osx-arm64-phase9-provider-host` passed `--self-test`, native
  `Get-Process -Name PwshAotLite | Select-Object Name, Id`, and native
  metadata completion. The required Homebrew OpenSSL/Brotli library search
  path was supplied only to the local linker environment.
- Differential parser evidence: no parser/AST change; parser reuse guard and
  all 28 stock-`pwsh` parser differential baselines passed.

## Independent verdicts

| Persona | PASS / BLOCK | Evidence-backed finding | Resolution / accepted variance |
| --- | --- | --- | --- |
| Upstream Grammar Steward | PASS | No parser, AST, accepted syntax, or binder grammar changed; parser guard and 28 baselines passed. | This phase is host-only. |
| Native AOT Boundary Sentinel | PASS | Closed configuration and captured discovery roots are composed once; no provider, service locator, reflection, dynamic loading, runspace, or generic process runner was introduced. | Fixed-path Unix ownership lookup remains a narrow documented seam. |
| Static Data-Plane & Binder Guardian | PASS | Existing record/binder contracts are untouched; catalogs are projections with injected inputs, not executable command discovery. | No data-plane or parameter-binding support widened. |
| Diagnostic Experience Guardian | PASS | Throwing terminal probes return a conservative no-ANSI fallback; unsupported process ownership retains `CouldNotRetrieveUserName`. | `AOT6101`/`AOT6102` are typed unavailable records, not fake cmdlets. |
| Compatibility Proof Adversary | PASS | Installer, help/module/repository, completion, terminal, and native process smokes exercise the injected composition while preserving existing output contracts. | Provider paths, session state, arbitrary environment access, credentials, and network remain unsupported. |
| Architecture Guard | PASS | `AotHostComposition` is the immutable production root and the authority map/AGENTS rule names each capability and exclusion. | Privileged installer write policy remains specialized. |

## Outcome

`PASS — integrated` — this substrate intentionally does not implement PowerShell
providers, session state, arbitrary environment access, generic process launch,
credentials, network transport, or a virtual filesystem. A later port must add
a capability only with an explicit authority/diagnostic design and fresh review.
