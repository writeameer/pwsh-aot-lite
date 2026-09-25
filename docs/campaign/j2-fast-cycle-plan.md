# J2 fast-cycle plan

**Scope:** planning control only. It does not change a converter, target
runtime, command availability, or migration count.

**Start:** `2026-09-25T07:29:25Z`

**Cycle stop threshold:** `01:31:57` (`5517` seconds) from the recorded start.
**Per-archetype trial limit:** `00:10:00` (`600` seconds).

## Input and output

The cycle reuses the immutable 16-command J2 source-fact report and its
per-command records. It reads the checked
[`j2-fast-cycle-rule-pack-v1.json`](j2-fast-cycle-rule-pack-v1.json), validated
by [`j2-fast-cycle-rule-pack-v1.schema.json`](j2-fast-cycle-rule-pack-v1.schema.json).

Each archetype moves through three strictly ordered layers. A failure in a
layer is the archetype result; it does not authorize a retry, repair, or a
later layer.

## Deterministic cycle

1. **A — behavior probe (under five seconds).** Run only stock `pwsh` and the
   current already-built native executable against the smallest positive and
   negative corpus. This layer creates no manifest, code, build, publish, or
   semantic substitute. An unsupported native route is a stopped command-
   viability result.
2. **B — deterministic feasibility screen (under one minute).** Only when A
   shows a concrete bounded behavioral route, verify the retained hashes,
   source/AST facts, generated metadata shape, and exactly one static native
   route. A missing route is a stopped feasibility result.
3. **C — disposable Native AOT proof (at most 600 seconds).** Only when A and
   B identify that concrete narrow adapter route, generate and validate a test
   manifest, then run the bounded stock/native proof. This is the only layer
   allowed to build or publish a disposable proof.
4. Apply the five exhaustive family rules. Every one of the 16 command names
   must match exactly one family; a gap or overlap stops the cycle.
5. Apply all three cross-cutting guards to every relevant selected command.
   A guard failure stops that archetype before a trial.
6. At 5517 seconds from cycle start, stop all unstarted or active trials and
   write the cycle report with their automatic timing fields.
7. Only a Layer C trial with no mismatch, its complete generated manifest, and all
   required evidence may be marked `viable-for-normal-lifecycle`. That status
   is a handoff, not a profile, port, availability, or migration claim.

## Timing record contract

Layer A and B records carry their own automatic timestamps and elapsed fields.
Layer C records carry `startedUtc`, `deadlineUtc`, `endedUtc`,
`elapsedSeconds`, `cycleElapsedSeconds`, `outcome`, and optional
first-mismatch fields. The runner calculates—never estimates—elapsed fields
from a monotonic clock and records UTC timestamps separately. The Layer C
`deadlineUtc` is start plus 600 seconds; the cycle deadline is start plus 5517
seconds. An environment-preflight failure (for example a missing linker
library) is retained separately and is excluded from command evidence and all
Layer C elapsed-trial metrics.

## Non-negotiable stop rules

- First mismatch at any layer wins. Preserve it; do not infer a repair or run
  another form.
- Missing evidence, source drift, a family overlap/gap, invalid generated test
  manifest, or expired timer is a stopped result.
- A rule may not create a dynamic engine, `PSObject`, reflection, a provider,
  session state, runtime compilation, or a second parser/binder.
- Promotion resumes the canonical
  [cmdlet-port lifecycle](cmdlet-port-lifecycle.md) at its applicable normal
  gate. It cannot skip DLAR, target readiness, verification, or release.

## Evidence basis

- J2 source facts and retained outcomes:
  `../../../pwsh-aot-conversion-survey/evidence/materialized/j2-source-facts-v1/`
  and `.../j2-conversion-run-v2/`.
- J2 source-family plan:
  `../../../pwsh-aot-conversion-survey/analysis/j2-conversion-plan-v1/plan.json`.
- J2 structural reconciliation:
  `../../../pwsh-aot-conversion-survey/analysis/j2-conversion-plan-v2/README.md`.
- Review-derived guards: J2 requirements/readiness DLAR packages and
  [`j2-static-record-transforms-implementation.md`](../reviews/j2-static-record-transforms-implementation.md).
