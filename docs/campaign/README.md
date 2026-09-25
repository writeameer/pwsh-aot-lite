# Phase 10 campaign status and operating model

This is the canonical human-readable status page for the systematic built-in
cmdlet campaign. It separates **reusable foundation work** from the
deterministic ten-command execution queue. Neither a foundation nor a queued
row is a PowerShell compatibility claim.

## Current position

| Measure | Current state |
| --- | --- |
| Pinned upstream inventory | 290 declarations / 288 logical command names |
| Native cmdlets integrated | 24 bounded, independently verified ports |
| Completed foundations | J0 closed JSON codec/value plane, merged at `aff09b3` |
| Unprocessed command adapters | 213 |
| Explicit current boundaries | 3 J2 commands plus 48 J8 dynamic-engine commands |
| Next campaign release batch | J1 remaining direct path/read commands (12 commands) |
| Subsequent release batch | J0 command adapters, then J3 formatting/stream work |

Every release below is a bounded native subset; its per-cmdlet note and
variance record define exactly what is admitted and what fails closed.

J0 is complete as a reusable closed `System.Text.Json` ↔ `AotValue` codec. It
does **not** register `ConvertFrom-Json`, `ConvertTo-Json`, or `Test-Json`.
Those command adapters remain queued and require their own generated
descriptor subset, lifecycle/binding design, controlled stock/native oracle,
tests, variance evidence, and independent review.

## Canonical release status

This is the live count authority. It is reconciled from integration commits
and `docs/cmdlets/port-timing.md`, not from the currently stale generated
batch queue. “Boundary” means a current explicit no-route disposition; a
shared-seam prerequisite alone remains unprocessed.

| Batch | Total | Released / migrated | Explicit boundary | Unprocessed |
| --- | ---: | --- | --- | ---: |
| Pre-campaign baseline | 9 | 9 — `Get-ChildItem`, `Get-FileHash`, `New-Guid`, `New-TimeSpan`, `Start-Sleep`, `Get-Item`, `Test-Path`, `Resolve-Path`, `Convert-Path` | 0 | 0 |
| J0 — structured codecs, including JSON | 18 | 0 | 0 | 18 |
| J1 — direct physical paths/reads | 14 | 2 — `Join-Path`, `Split-Path` | 0 | 12 |
| J2 — typed data transforms | 16 | 13 — `Where-Object`, `Select-Object`, `Get-Random`, `Get-SecureRandom`, `Join-String`, `Compare-Object`, `Select-String`, `Measure-Object`, `Get-Unique`, `Group-Object`, `Sort-Object`, `ForEach-Object`, `Get-Member` | 3 — `Add-Member`, `Measure-Command`, `Tee-Object` | 0 |
| J3 — formatting, streams, metadata/modules | 43 | 0 | 0 | 43 |
| J4 — filesystem mutation | 24 | 0 | 0 | 24 |
| J5 — local platform/process/service/host | 39 | 0 | 0 | 39 |
| J6 — security and network | 15 | 0 | 0 | 15 |
| J7 — trusted sidecars | 62 | 0 | 0 | 62 |
| J8 — explicitly unsupported dynamic-engine families | 48 | 0 | 48 — explicitly unsupported | 0 |
| **Total** | **288** | **24** | **51** | **213** |

The campaign cohort excludes the nine baseline releases: it therefore contains
279 commands, 15 released, 51 current-boundary outcomes, and 213 unprocessed
commands. J8 is an accounting boundary, not an implementation queue. J0’s
foundation is complete, but no J0 cmdlet adapter is released.

## How the campaign works

1. The all-built-ins survey records the converter's deterministic outcome for
   every command. A `missing-profile` result is data about converter coverage,
   not a unique technical failure for each command.
2. The archetype inventory groups those results by shared upstream family and
   required AOT boundary. Foundation work adds one reusable, reviewed seam at
   a time.
3. The ten-command queue is then the dumb execution schedule: a command may
   start only after its named foundation is integrated. It is never permission
   to infer a dynamic substitute or accept unsupported parameters.
4. Every individual port still follows the repository port workflow:
   generated metadata, upstream-reuse matrix, narrow AOT implementation,
   variance record, managed/parser/native evidence, independent reviews, and
   non-fast-forward integration.

This ordering is deliberate: thinking happens in survey classification and
foundation design; repeated command work is made as mechanical as the proven
profile and shared seam allow.

## Dependency-aware campaign release order

This is the required order for remaining release work: **J1 → J0 → J3 → J4 →
J5 → J7 → J6**. J2 has no queued adapter: its thirteen bounded releases are
complete and its remaining three commands are explicit boundary outcomes. The
order does not change the historical date on which a reusable foundation
landed. In particular, J0's codec foundation is already integrated, but its
18 command adapters follow J1 in the release sequence.

J7 follows J5 because the trusted sidecar needs the local process/host
capability supplied there. It also follows J2 and J0 because its protocol must
carry the closed typed values and codec-backed wire format they establish. J6
is **not** a prerequisite of J7. J8 remains unavailable under the AOT
boundary.

| Release order | Batch | Command adapters in batch | Current adapter result |
| ---: | --- | ---: | --- |
| 1 | J1 — direct physical paths/reads | 12 unprocessed | 2 migrated |
| 2 | J0 — structured codecs, including JSON | 18 unprocessed | 0 migrated; codec foundation only |
| 3 | J3 — formatting, streams, metadata/modules | 43 unprocessed | 0 migrated |
| 4 | J4 — filesystem mutation | 24 unprocessed | 0 migrated |
| 5 | J5 — local platform/process/service/host | 39 unprocessed | 0 migrated |
| 6 | J7 — trusted sidecars | 62 unprocessed | 0 migrated |
| 7 | J6 — security and network | 15 unprocessed | 0 migrated |
| — | J2 — typed data transforms | 3 current boundaries | 13 migrated |
| — | J8 — dynamic-engine families | 48 | explicitly unsupported |

The actual total is **24 migrated cmdlets**. A row contributes to that
total only after the lifecycle's release step; batch order, foundation work,
and approved plans do not count as migrations.

## Foundation inventory

| ID | Foundation | State | Purpose |
| --- | --- | --- | --- |
| J0 | Closed JSON codec/value plane | complete (`aff09b3`) | Bounded JSON ↔ closed `AotValue`, typed limits/diagnostics; no `PSObject` or arbitrary CLR serialization. |
| J1 | Direct physical read/path | lexical slice complete at `9a279d3`; 2/14 migrated, 12 unprocessed | Two closed, provider-free **lexical** path-text adapters (`Join-Path` / `Split-Path`); no resolver, captured-root, drive, or ambient-location authority. [Accepted profile design](../architecture/j1-direct-lexical-path-profiles.md); [implementation review](../reviews/j1-lexical-path-implementation.md). |
| J2 | Closed typed data plane | 13/16 migrated; 3 current boundaries | The released set is recorded in the canonical table above. `Add-Member`, `Measure-Command`, and `Tee-Object` remain explicit no-route outcomes; they need new object-schema, script-execution, or authority designs. |
| J3 | Terminal and stream contracts | queued | Static views and explicit output/error/information streams. |
| J4 | Physical mutation authority | queued | `ShouldProcess`, confirmation, atomic write/rollback, and trust policy. |
| J5 | Local platform capabilities | queued | Narrow process, service, host, and platform interfaces with OS/error matrices. |
| J6 | Security and transport authority | queued | Explicit credential, crypto, certificate, HTTP, mail, and policy ownership. |
| J7 | Trusted sidecar protocol | queued | Typed protocol/trust/lifecycle boundary for CIM, WSMan, remoting, jobs, and events. |
| J8 | Dynamic-engine families | unavailable | No approved in-process AOT route for dynamic runspace, debugger, and mutable session/type-system behavior. |

## B02 partial completion

J1's first bounded slice is complete: `Join-Path` and `Split-Path` implement
the accepted [direct lexical physical-path design](../architecture/j1-direct-lexical-path-profiles.md).
The generic multi-positional binder preserves parser-derived argument groups,
maps them atomically to declared static positions, and leaves the existing
binder unchanged for every previous descriptor. The lexical adapters do not add
providers, drives, captured-root resolution, ambient location, or an alternate
grammar. The other 12 J1 commands remain unprocessed shared-seam candidates.
Their earlier `requires-substrate` planning evidence is not a release or
terminal boundary disposition.

B02 is partially complete and follows J2 in the dependency-aware release
order:

`Join-Path` and `Split-Path` are integrated. `Get-Content`,
`Get-ItemProperty`, `Get-ItemPropertyValue`, `Test-FileCatalog`,
`ConvertFrom-Json`, `ConvertTo-Json`, `Test-Json`, and `Get-Location` remain
queued behind their individual prerequisites.

The batch is **not** a single atomic port. The remaining filesystem commands
need reviewed direct-path/read seams. The JSON rows can begin command-adapter
work because J0 is integrated, but J0 did not implement them. `Test-Json`
additionally needs a separate static validation contract.

## Authoritative evidence

- [Built-in manifest](phase10-built-in-cmdlets.md) and its checked JSON are
  the source classification authority.
- [Deterministic batch queue](phase10-batch-queue.md) retains historical
  execution order and prerequisites. Its generated completion counters are
  currently stale, so it is not count authority; use the canonical release
  status table above until its generator inputs are reconciled.
- [Engineering archetype inventory](phase10-archetype-inventory.md) explains
  how the original 279-command survey cohort is now a 264-command unported
  cohort grouped into reusable families.
- [Cmdlet-port lifecycle](cmdlet-port-lifecycle.md) is the required nine-step
  process and the only definition of when a command counts as migrated.
- [J2 fast-cycle plan](j2-fast-cycle-plan.md) and its checked
  [rule pack](j2-fast-cycle-rule-pack-v1.json) define the bounded,
  evidence-first trial control for learning from J2. They do not replace the
  lifecycle or make any port claim.
- [All-builtins survey report](../../../pwsh-aot-conversion-survey/runs/all-builtins-survey-20260923/report.json)
  and its per-command evidence are the converter-coverage baseline.
- [J0 codec contract](../architecture/json-codec-foundation.md) and
  [review ledger](../reviews/json-codec-foundation.md) record the completed
  foundation's exact boundary and verification.
- [Per-cmdlet notes](../cmdlets) and the
  [timing ledger](../cmdlets/port-timing.md) record scope, reuse, variance,
  verification, and elapsed work for each attempted port.
- [`port-variances.json`](../../port-variances.json) is the durable
  machine-readable compatibility/deferral record.

## Verification

```powershell
pwsh -NoProfile -File ./tools/Export-Phase10Campaign.ps1 -Verify
pwsh -NoProfile -File ./tools/Export-Phase10BatchQueue.ps1 -Verify
```

Run the standard parser-reuse, managed self-test, and fresh Native AOT gates
for every foundation or command integration; the queue verification alone is
only accounting and dependency-order evidence.
