# Typed data-plane and Native AOT lens

**Perspective:** typed structured-data and Native-AOT perspective—not authored
by or attributed to any real person. This is an evidence-backed design lens,
not an impersonation or endorsement.

**Verdict:** **PASS (carried forward from v1)** for the narrow, design-only
Step-2 disposition.

## Carry-forward basis

This is not a new independent verdict. It carries forward the independent v1
typed-data/AOT PASS recorded at Git object
`origin/codex/j2-dlar-typed-aot:docs/reviews/dlar/2026-09-25-j2-requirements-v1/lenses/typed-data-plane-aot.md`
(`f176824`). The v2 amendment was verified as semantic-only before retaining
that verdict: its scope still has sixteen `requires-substrate` outcomes, zero
approved profiles, no converter or target change, and no migration-count
change. Its only added data is the explicit non-adapter/non-migration
reconciliation for the pre-existing finite structural stages with the
`Where-Object` and `Select-Object` spellings.

Because the amendment adds no value kind, record shape, authority, binder,
transport, registration, executable behavior, or AOT dependency, it does not
broaden the v1 typed-data/AOT claim. A new typed-data/AOT review is required
before any later proposal does introduce one.

## Carried-forward claim

J2 v2 retains exactly sixteen source-pinned `requires-substrate` outcomes,
approves zero converter profiles, and makes no converter, runtime,
registration, availability, or migration-count change. It is not a PASS for
any J2 cmdlet implementation or for general PowerShell object-pipeline
compatibility.

## Evidence reviewed

- [J2 v2 plan](../../../../../../pwsh-aot-conversion-survey/analysis/j2-conversion-plan-v2/plan.json)
  retains all sixteen outcomes and explicitly marks both same-named structural
  stages as non-adapters and non-migrations.
- The raw report and materialized source index remain pinned as
  `3e7edfa5e0a731957c9a4da4b54b1ad41236cd7b7b63537b47ba2c9f7e8f99c7` and
  `ab0244ff27fb3cc94d8ffcb65eefd8210172ff43369e3526e9796b868ec391a8`.
- The current [value plane](../../../../architecture/value-plane.md) remains
  closed to explicit `AotValue` and immutable `AotRecord` shapes; it excludes
  `PSObject`, reflection, arbitrary adaptation, providers, serialization, and
  dynamic script blocks.
- The [host substrate](../../../../architecture/provider-host-substrate.md)
  excludes runspaces, providers, ambient session state, service location, and
  dynamic registration.

## Boundary finding

**No new blocker.** The v2 clarification closes the ambiguity without admitting a
new object channel or quietly promoting a structural tail. The pre-existing
stages remain bounded finite transforms over declared records only; all
upstream `WhereObjectCommand` and `SelectObjectCommand` behavior remains
`requires-substrate`.

Each retained requirement rejects its dynamic source dependency:

- `PSObject`/ETS/member-expression behavior remains unavailable for record,
  comparison, grouping, projection, formatting, and metadata commands.
- ScriptBlock, dynamic binding, runtime compilation, jobs, and runspaces
  remain unavailable for `ForEach-Object`, `Measure-Command`, and
  `Where-Object`.
- random behavior remains deferred until its source base/helpers and static
  contract are separately pinned.
- `Select-String` and `Tee-Object` retain explicit direct-physical I/O and
  session-state requirements; neither promotes the host into a provider or
  variable service.

## Next gate

The profile-design preflight is not applicable to this negative disposition.
It is mandatory before any future profile family or shared substrate. Until
then the converter may emit only one retained `requires-substrate` record per
command; it must not add an executable unsupported-command facade.
