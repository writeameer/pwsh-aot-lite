# DLAR: J2 static record-transform target readiness v1

**Date:** `2026-09-25`  
**Claim to review:** the target has a complete bounded design for a future
implementation containing only static-record adapters for `Where-Object` and
`Select-Object`. It authorizes no code, registration, converter profile,
support claim, or migration-count change.  
**Outcome:** **BLOCK** — both lenses found a design-only blocker. This packet
is superseded by [v2](../2026-09-25-j2-target-readiness-v2/README.md), which
must receive fresh verdicts.

## Required packet

- [J2 target readiness design](../../../architecture/j2-static-record-transform-target-readiness.md)
- [source pins and retained evidence](evidence.md)
- [accepted J2 v2 plan](../../../../../../pwsh-aot-conversion-survey/analysis/j2-conversion-plan-v2/plan.json)
- [canonical J2 v2 conversion run](../../../../../../pwsh-aot-conversion-survey/runs/j2-conversion-run-v2/report.json)
- [J2 v2 requirements review](../2026-09-25-j2-requirements-v2/README.md) (prior lifecycle step)
- [profile-design preflight](../../../architecture/profile-design-preflight.md)

## Preflight closure

| Required closure | Evidence / exact decision |
| --- | --- |
| Source pins/facts | [evidence](evidence.md#source-pins-and-retained-facts) pins all 16 command records and the v2 plan/run |
| Authority | [decision](../../../architecture/j2-static-record-transform-target-readiness.md#decision) is an authority-free static record-batch stage |
| Static parameters/output | [contracts](../../../architecture/j2-static-record-transform-target-readiness.md#static-contracts) |
| Grammar/types | [metadata/stage contract](../../../architecture/j2-static-record-transform-target-readiness.md#metadata-and-stage-contract) |
| Batches/property binding | [diagnostics/atomicity](../../../architecture/j2-static-record-transform-target-readiness.md#diagnostics-and-atomicity) |
| Fixtures/count | [fixture plan](../../../architecture/j2-static-record-transform-target-readiness.md#fixture-and-oracle-plan) fixes 52 unique IDs |
| Deferred remainder | [disposition](../../../architecture/j2-static-record-transform-target-readiness.md#j2-disposition-no-silent-promotion) accounts for all 16 |

## Lens verdicts

Both lenses returned `BLOCK`, cite evidence, and name the smallest corrective
action. These are design perspectives, not real-person participation or
endorsement.

- [PowerShell semantic and compatibility lens — BLOCK](lenses/powershell-compatibility.md)
- [Typed data-plane and Native AOT lens — BLOCK](lenses/typed-data-plane-aot.md)
- [Normalized findings](findings.md)
- [Reconciliation](reconciliation.md)

A BLOCK prevents implementation or a support claim. Fresh v2 PASS verdicts
still leave the ordinary reviewer matrix and all lifecycle release gates.
