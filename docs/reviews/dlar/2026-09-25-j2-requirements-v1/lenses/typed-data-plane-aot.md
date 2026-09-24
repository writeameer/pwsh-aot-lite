# Typed data-plane and AOT lens

**Perspective:** typed structured-data and Native-AOT perspective—not authored
by or attributed to any real person. This is an evidence-backed design lens,
not an impersonation or endorsement.

**Verdict:** **PASS** for the narrow, design-only negative claim below.

## Claim reviewed

The J2 v1 plan may retain exactly sixteen source-pinned
`requires-substrate` outcomes at lifecycle Step 3. It admits no converter
profile, target adapter, command registration, executable behavior, or
migration-count change. A later proposal may introduce a closed static
replacement only after it supplies its own completed profile-design preflight
and receives a new DLAR verdict.

This is not a PASS for any J2 cmdlet implementation or for general PowerShell
object-pipeline compatibility.

## Evidence reviewed

- The plan has sixteen listed commands and sixteen `requires-substrate`
  dispositions in its machine-readable record:
  [plan.json](../../../../../pwsh-aot-conversion-survey/analysis/j2-conversion-plan-v1/plan.json).
- The raw extraction report, materialized source-fact index, and input
  manifest hashes were independently verified as respectively
  `3e7edfa5e0a731957c9a4da4b54b1ad41236cd7b7b63537b47ba2c9f7e8f99c7`,
  `ab0244ff27fb3cc94d8ffcb65eefd8210172ff43369e3526e9796b868ec391a8`, and
  `f1753cea7927104124df84c7fa48102605fa7389825334f39bfcae218e7e3dcf`.
  The exact per-cmdlet records are linked by the
  [materialized index](../../../../../pwsh-aot-conversion-survey/evidence/materialized/j2-source-facts-v1/index.json).
- The plan's shared classes isolate the actual design prerequisites rather
  than pretending they are transferable base classes: typed named-field
  comparison/projection, a finite AST-lowered predicate/function contract,
  and the separately pinned random base/helpers. See
  [source-reuse facts](../../../../../pwsh-aot-conversion-survey/analysis/j2-conversion-plan-v1/source-reuse-facts.md).
- The current target value plane is deliberately closed (`AotValue` and
  immutable `AotRecord`), has only explicit named-field access/comparison,
  and expressly excludes `PSObject`, CLR reflection, arbitrary adaptation,
  providers, serialization, and dynamic script blocks:
  [value-plane contract](../../../../architecture/value-plane.md).
- The fixed host substrate excludes runspaces, providers, ambient session
  state, and service-location/dynamic registration:
  [provider/host substrate](../../../../architecture/provider-host-substrate.md).

## AOT-boundary finding

**No blocker.** The plan does not smuggle a proposed implementation through a
shared label. Each requirement is explicitly bounded and rejects its dynamic
source dependency:

- `PSObject`/ETS/member-expression paths remain rejected for the record,
  comparison, grouping, projection, formatting, and metadata commands.
- `ScriptBlock`, dynamic binders, runtime compilation, jobs, and runspaces
  remain rejected for `ForEach-Object`, `Measure-Command`, and
  `Where-Object`.
- random behavior is deferred until its source base/helpers are pinned; it
  does not substitute ambient session state or an unreviewed RNG.
- `Select-String` and `Tee-Object` retain explicit direct-physical I/O and
  session-state prerequisites; they do not turn the host substrate into a
  provider or variable service.

The existing narrow AST-lowered `Where-Object`/`Select-Object` structural
tail is not a counterexample. It is a separately documented finite transform
over declared `AotRecord` fields; it does not implement either upstream J2
cmdlet surface. The plan correctly requires a new exact contract before any
J2 support claim can reuse that seam.

## Preflight and diagnostics

The profile-design preflight is **not applicable to this negative planning
claim**: J2 v1 introduces neither a new profile family nor a shared substrate.
It is mandatory before the next proposal that does either; the plan names that
next gate explicitly.

Likewise, no public runtime diagnostic is admitted by this plan. It must not
be represented as a command-level "unsupported" implementation. At Step 4,
the converter may emit only retained `requires-substrate` evidence. A later
executable subset must declare source-aware diagnostic IDs, spans, precedence,
and fixtures in its preflight and target-readiness packet.

## Smallest next action

No amendment is needed to approve this narrow disposition. Preserve all
sixteen requirement records unchanged through the converter result. Before
designing any executable subset, choose one requirement class, pin the
additional upstream sources it needs, complete every row of the
[profile-design preflight](../../../../architecture/profile-design-preflight.md),
and run a new two-lens DLAR package.
