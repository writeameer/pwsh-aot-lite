# PowerShell semantic and compatibility lens

**Perspective:** PowerShell design-principles-inspired perspective—not authored
by or attributed to any real person. This is an evidence-backed design lens,
not an impersonation or endorsement.

**Verdict:** **PASS** for the narrow, design-only Step-2 disposition.

## Claim reviewed

J2 v2 may retain exactly sixteen source-pinned `requires-substrate` outcomes,
approve zero conversion profiles, make no converter or runtime change, and
make no migration-count change. This is not a built-in cmdlet support claim.

## P1 re-review: resolved

The v2 plan explicitly reconciles the pre-existing, AST-lowered stages named
`Where-Object` and `Select-Object`. It identifies their exact bounded forms,
links the existing value-plane evidence, and states that neither is a generated
built-in adapter, J2 migration, or migration-count change. It retains the
entire upstream `WhereObjectCommand` and `SelectObjectCommand` surfaces as
`requires-substrate`, alongside the same outcome for the other fourteen
commands.

The reconciliation agrees with the existing target evidence: the stage is only
a direct finite-numeric predicate or direct immutable-record-field projection;
ScriptBlock, conversion, member enumeration, ETS, wildcard/script expressions,
note properties, and arbitrary object expansion remain excluded. See
[J2 v2 plan](../../../../../../pwsh-aot-conversion-survey/analysis/j2-conversion-plan-v2/plan.json)
(`1f1a18d3878c76eb560309f94a8393ee4b1d11f23c7d15252d5a6860c5932a55`),
[J2 v2 checksum manifest](../../../../../../pwsh-aot-conversion-survey/analysis/j2-conversion-plan-v2/checksums.json),
and the existing [value-plane boundary](../../../../architecture/value-plane-first-migration.md).

## Evidence-backed conclusion

- All sixteen v1 source facts remain pinned through the verified inherited
  plan checksum `8492fe0b3bdf0b79a718379e200b3c7b407efdf35d5998eae7fd8ba4e32c104c`.
- The original raw report and materialized index remain pinned as
  `3e7edfa5e0a731957c9a4da4b54b1ad41236cd7b7b63537b47ba2c9f7e8f99c7` and
  `ab0244ff27fb3cc94d8ffcb65eefd8210172ff43369e3526e9796b868ec391a8`.
- V2 records sixteen retained outcomes, each `requires-substrate`, with
  `approvedConversionProfileCount: 0` and `migrationCountChange: 0`.

No new profile family or shared substrate is proposed, so the profile-design
preflight is not applicable to this negative planning decision. It remains
mandatory before any future executable subset. That later design must state
every admitted parameter/alias/set/cardinality/output/diagnostic and prove it
against the relevant upstream source and stock oracle.

## Decision

**PASS** — P1 is resolved. Step 4 may emit only the sixteen retained
`requires-substrate` results. It may not infer or register a J2 cmdlet profile,
describe either structural stage as a migrated cmdlet, or claim broader
PowerShell compatibility.
