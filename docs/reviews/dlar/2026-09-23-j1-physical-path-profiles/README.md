# DLAR: J1 physical path profiles

**Review ID:** `2026-09-23-j1-physical-path-profiles`  
**Date:** `2026-09-23`  
**Claim reviewed:** The J1 plan may add two deterministic converter profile
families for `Join-Path` and `Split-Path`, then emit two conversion plans and
twelve source-pinned `requires-substrate` outcomes.  
**Outcome:** **BLOCK** — no profile generation, conversion run, implementation,
or support claim is authorized until the required amendments are resolved and
both lenses re-review the changed packet.

## Package index

| Record | Purpose |
| --- | --- |
| [evidence.md](evidence.md) | Immutable source packet, locations, and SHA-256 provenance |
| [PowerShell compatibility lens](lenses/powershell-compatibility.md) | Source-derived PowerShell contract review |
| [Typed data-plane and AOT lens](lenses/typed-data-plane-aot.md) | Closed-value, composition, and AOT-boundary review |
| [findings.md](findings.md) | Normalized shared and lens-specific findings |
| [reconciliation.md](reconciliation.md) | Required plan amendments and current disposition |

## Lens ledger

| Lens | Verdict | Blocking summary |
| --- | --- | --- |
| PowerShell semantic and compatibility | **BLOCK** | Define every admitted and excluded parameter/alias, then separate lexical path work from existing-path resolution. |
| Typed data-plane and AOT | **BLOCK** | Define the closed `PathText` contract, parameter-set/output shape, source-aware diagnostics, and composition proof. |

## Decision

The two proposed profiles remain a sound bounded direction only after the
plan has an exact static surface. The approved common boundary is pure lexical
physical-path text transformation; it must not silently use provider/session
state or the existing-path resolver. The remaining twelve J1 commands continue
to be deterministic `requires-substrate` planning results, not converted
cmdlets.

The external J1 plan and raw survey evidence are preserved unchanged. This
package records the review of that version and does not amend or supersede it.

## Related records

- [DLAR process](../../../architecture/dual-lens-architecture-review.md)
- [DLAR template](../../DLAR-TEMPLATE.md)
- [Review index](../../README.md)
- [J1 external plan and retained evidence](evidence.md#immutable-external-evidence)
