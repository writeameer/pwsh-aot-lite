# J2 requirement disposition v2 evidence

The J2 v2 plan is an immutable amendment to v1. This package records its
completed two-lens reassessment; it does not rewrite survey evidence.

| Artifact | Immutable location | SHA-256 |
| --- | --- | --- |
| amended J2 plan | [plan](../../../../../pwsh-aot-conversion-survey/analysis/j2-conversion-plan-v2/plan.json) | `1f1a18d3878c76eb560309f94a8393ee4b1d11f23c7d15252d5a6860c5932a55` |
| v2 plan checksum manifest | [checksums](../../../../../pwsh-aot-conversion-survey/analysis/j2-conversion-plan-v2/checksums.json) | source-pinned manifest |
| inherited v1 plan | [plan](../../../../../pwsh-aot-conversion-survey/analysis/j2-conversion-plan-v1/plan.json) | `8492fe0b3bdf0b79a718379e200b3c7b407efdf35d5998eae7fd8ba4e32c104c` |
| source-fact index | [index](../../../../../pwsh-aot-conversion-survey/evidence/materialized/j2-source-facts-v1/index.json) | `ab0244ff27fb3cc94d8ffcb65eefd8210172ff43369e3526e9796b868ec391a8` |
| raw extractor report | [report](../../../../../pwsh-aot-conversion-survey/runs/j2-source-facts-v1/report.json) | `3e7edfa5e0a731957c9a4da4b54b1ad41236cd7b7b63537b47ba2c9f7e8f99c7` |

The plan’s only v2 addition reconciles pre-existing, AST-lowered structural
stages with the same spellings as `Where-Object` and `Select-Object`.
[value-plane-first-migration](../../../architecture/value-plane-first-migration.md)
documents those finite record-only stages. They are not generated built-in
adapters, not J2 migrations, and do not change command accounting.
