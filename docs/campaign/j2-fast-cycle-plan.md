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

For each rule-selected archetype, the runner must first generate and retain a
test manifest from the rule's exact commands, source hashes, signals, and
guard expectations. No code trial starts before that manifest validates.

## Deterministic cycle

1. Verify the source-fact report, per-command index, rule-pack schema, and
   rule-pack hashes.
2. Apply the five exhaustive family rules. Every one of the 16 command names
   must match exactly one family; a gap or overlap stops the cycle.
3. Apply all three cross-cutting guards to every relevant selected command.
   A guard failure stops that archetype before a trial.
4. Generate a test manifest before code from the matched rule IDs and command
   evidence. Validate unique test IDs, exact command membership, and required
   fail-closed assertions.
5. Start a monotonic 600-second trial timer. At the first build, oracle,
   native, diagnostic, or guard mismatch, stop immediately and retain the
   mismatch evidence. Do not retry inside this cycle.
6. At 5517 seconds from cycle start, stop all unstarted or active trials and
   write the cycle report with their automatic timing fields.
7. Only a trial with no mismatch, its complete generated manifest, and all
   required evidence may be marked `viable-for-normal-lifecycle`. That status
   is a handoff, not a profile, port, availability, or migration claim.

## Timing record contract

Each generated trial record carries `startedUtc`, `deadlineUtc`,
`endedUtc`, `elapsedSeconds`, `cycleElapsedSeconds`, `outcome`, and optional
first-mismatch fields. The runner calculates—never estimates—elapsed fields
from a monotonic clock and records UTC timestamps separately. `deadlineUtc`
is start plus 600 seconds; the cycle deadline is start plus 5517 seconds.

## Non-negotiable stop rules

- First mismatch wins. Preserve it; do not infer a repair or run another form.
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
