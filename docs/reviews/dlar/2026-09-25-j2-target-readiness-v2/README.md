# DLAR: J2 target readiness v2

**Date:** `2026-09-25`

**Claim to review:** The corrected J2 target design has one descriptor-bound
route for future static `Where-Object`/`Select-Object` adapters and a closed,
unimplemented future batch-transport contract. It authorizes no runtime,
converter, registration, support, sidecar, or migration-count change.

**Outcome:** **PENDING** — fresh verdicts required.

## Packet

- [v2 corrected readiness design](../../../architecture/j2-static-record-transform-target-readiness-v2.md)
- [v1 base readiness design](../../../architecture/j2-static-record-transform-target-readiness.md)
- [v2 evidence](evidence.md)
- [checksums](checksums.json)
- [accepted J2 v2 plan](../../../../../../pwsh-aot-conversion-survey/analysis/j2-conversion-plan-v2/plan.json)
- [canonical conversion run](../../../../../../pwsh-aot-conversion-survey/runs/j2-conversion-run-v2/report.json)

## Preflight closure

| Closure | Evidence |
| --- | --- |
| source facts | v1 retained evidence plus v2 checksums |
| one command route | [amendment A](../../../architecture/j2-static-record-transform-target-readiness-v2.md#amendment-a-one-descriptor-bound-route-per-command-spelling) |
| transport boundary | [amendment B](../../../architecture/j2-static-record-transform-target-readiness-v2.md#amendment-b-contractual-unimplemented-batch-transport-v1) |
| grammar/types | source AST, generated descriptor, closed `AotValue.List(AotValue.Record)` payload |
| diagnostics/atomicity | `AOT6401`–`AOT6406`, no fallback, outer error/cancel frames |
| fixture count | 76 unique IDs, mechanically asserted during implementation |
| remainder | fourteen J2 commands and all J7 behavior remain unavailable |

## Fresh lenses

- [PowerShell semantic and compatibility](lenses/powershell-compatibility.md)
- [Typed data-plane and Native AOT](lenses/typed-data-plane-aot.md)
- [findings](findings.md)
- [reconciliation](reconciliation.md)

Each lens must issue exactly `PASS` or `BLOCK`. A BLOCK prevents code or a
support claim.
