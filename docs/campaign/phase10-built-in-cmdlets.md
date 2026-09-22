# Phase 10: systematic built-in cmdlet campaign

The checked [manifest](phase10-built-in-cmdlets.json) contains **290 source
declarations / 288 logical command names** from the pinned upstream commit.
`Restart-Computer` and `Stop-Computer` each have two declarations and remain
separate until a port proves its platform condition.

The manifest is a conservative planning decision, never an execution claim.

| Category | Declarations | Meaning |
| --- | ---: | --- |
| `native-port-candidate` | 3 | Only the reviewed W2 calibration allowlist; direct source review and an opt-in descriptor still apply. |
| `shared-substrate-dependency` | 102 | Needs a narrow physical/platform/process/host/credential/network capability first. |
| `native-replacement-required` | 75 | Needs a reviewed typed data, conversion, serialization, formatting, or projection contract. |
| `sidecar-candidate` | 62 | Stateful/remote behavior is eligible only through a separately designed trusted sidecar protocol. |
| `explicitly-unsupported` | 48 | Dynamic engine/session/type manipulation has no approved AOT execution route. |

Every row has a stable declaration ID, upstream path/class/source hash,
category, wave, rationale, evidence, prerequisites, platform variant, and
current catalog/execution state. The rules live in
[`tools/Export-Phase10Campaign.ps1`](../../tools/Export-Phase10Campaign.ps1).
The source generator supplies contract evidence; it must never auto-promote a
row to executable status.

## Inventory quality gate

```powershell
dotnet build -c Release --no-restore
pwsh -NoProfile -File ./tools/Export-Phase10Campaign.ps1 -Verify
```

This checks 290 declaration rows, 288 logical names, unique declaration IDs,
one valid category per row, source hashes, four platform-variant rows, and the
correct literal names `ForEach-Object`, `Invoke-CimMethod`, `Out-Null`,
`Sort-Object`, `Tee-Object`, and `Where-Object`. A source-hash change is a
review trigger: regenerate and inspect the changed rows, never silently carry
the classification forward.

Inherited migration blockers are evidence only, not a ranker: shared base
classes make `PSObject`, `SessionState`, `WriteObject`, and error APIs appear
on most declarations. Every port must inspect the direct body and its local
helper closure before accepting a subset.

## Ordered waves

1. **W0:** inventory/source-contract hygiene.
2. **W1:** reusable typed value, pipeline topology, conversion, serialization,
   and projection contracts.
3. **W2:** low-authority BCL calibration ports only: `New-Guid`,
   `New-TimeSpan`, and `Start-Sleep`. `New-Guid` is integrated for default UUID
   v7 and generated `-Empty` only; `InputObject`, positional, and pipeline
   behavior remain deferred. `Measure-Command` remains W1/W6 until its typed
   composition/timing contract is designed.
4. **W3:** physical filesystem reads only (`Get-ChildItem` is complete for its
   bounded captured-root direct-path macOS-arm64 slice; `Get-Item`,
   `Test-Path`, `Resolve-Path`, `Join-Path`, `Split-Path`, `Convert-Path`,
   `Get-Content`) through the Phase 9 substrate—never PS providers/drives.
5. **W4:** filesystem mutation only after reviewed `ShouldProcess`,
   confirmation, atomic write, and trust policy.
6. **W5:** process/service/platform commands through narrow capability and OS
   matrices; never a generic process runner.
7. **W6:** formatter/output and typed transform replacements, not ETS.
8. **W7:** security, credentials, and network only after explicit authority;
   `AOT6101`/`AOT6102` do not authorize a port.
9. **W8:** sidecar bridge as a separate trust/protocol project.

## Per-cmdlet branch contract

Each port uses its own `codex/<cmdlet-or-substrate>` branch. It must use the
generated descriptor with an opt-in parameter subset; typed AOT records and
services only; a variance entry and `docs/cmdlets/` note; positive, binding,
negative/diagnostic, stream, cancellation, and native smoke tests; parser
gates; all required independent reviews; and non-fast-forward release-steward
integration. Unsupported parameters remain rejected, never silently ignored.
