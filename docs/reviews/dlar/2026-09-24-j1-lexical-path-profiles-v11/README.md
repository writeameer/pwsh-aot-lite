# DLAR: J1 lexical physical-path profiles v11

**Review ID:** `2026-09-24-j1-lexical-path-profiles-v11`

**Date:** `2026-09-24`

**Claim reviewed:** J1 may add exactly two deterministic converter profiles—
`direct-physical-path-compose` for `Join-Path` and
`direct-physical-path-decompose` for `Split-Path`—then produce two conversion
plans and twelve source-pinned `requires-substrate` outcomes.
**Outcome:** **PASS** for this design-only claim.  No converter, profile,
runtime, generated target, or support claim was reviewed or authorized.

## Package index

| Record | Purpose |
| --- | --- |
| [evidence.md](evidence.md) | immutable external packet and SHA-256 provenance |
| [PowerShell compatibility lens](lenses/powershell-compatibility.md) | PASS record for the static PowerShell surface |
| [Typed data-plane and AOT lens](lenses/typed-data-plane-aot.md) | PASS record for closed values and authority boundary |
| [findings.md](findings.md) | resolved former blockers and retained guardrails |
| [reconciliation.md](reconciliation.md) | amendment dispositions and next gate |

## Lens ledger

| Lens | Verdict | Evidence-backed conclusion |
| --- | --- | --- |
| PowerShell semantic and compatibility | **PASS** | every admitted/rejected P1/P2 binding route, alias, cardinality, output, diagnostic, and variance is declared |
| Typed data-plane and AOT | **PASS** | closed lexical dialects, typed child fragments, zero-authority boundary, atomic batch semantics, and fixture accounting are sufficiently specified |

## Decision

The earlier BLOCK package remains immutable historical evidence.  Its six
amendments are resolved in the external J1 v11 packet, which this package pins
by hash.  The accepted canonical target design is
[J1 direct lexical physical-path profiles](../../../architecture/j1-direct-lexical-path-profiles.md).

Step 2 may add only the two named converter profiles and explicit requirement
records for the other twelve J1 commands.  Any runtime implementation,
registration, or compatibility-support claim requires the normal repository
port workflow and independent implementation reviews.

## Related records

- [Immutable v11 evidence](evidence.md#immutable-external-evidence)
- [Prior J1 BLOCK package](../2026-09-23-j1-physical-path-profiles/README.md)
- [DLAR process](../../../architecture/dual-lens-architecture-review.md)
- [Profile-design preflight](../../../architecture/profile-design-preflight.md)
