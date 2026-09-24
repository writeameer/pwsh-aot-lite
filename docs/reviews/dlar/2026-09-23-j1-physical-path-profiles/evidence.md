# Evidence and provenance

## Immutable external evidence

The original survey files are outside this repository and are intentionally
unchanged. Their content hashes below identify the exact packet reviewed.
Paths are repository-sibling-relative for local review; no target runtime or
converter source was changed by this DLAR package.

| Artifact | Immutable source location | SHA-256 |
| --- | --- | --- |
| PowerShell compatibility review | [survey review](../../../../../pwsh-aot-conversion-survey/analysis/j1-conversion-plan-v1/reviews/powershell-creator-compatibility-lens.md) | `bba6133abbbe49e293a3701b850a1668aee53edf6c474c67c83b11380c604ce8` |
| Structured-data review | [survey review](../../../../../pwsh-aot-conversion-survey/analysis/j1-conversion-plan-v1/reviews/nushell-structured-data-lens.md) | `ee2cede2111bcabcfd0b0f76ae3b42b230aaf2005fad1c8435a27713fcc1ad54` |
| J1 plan v1 | [survey plan](../../../../../pwsh-aot-conversion-survey/analysis/j1-conversion-plan-v1/README.md) | `def9122e618c4d861a49a7f971f37b10fa2f1d0df3fd63bce6eae51b7e6b7793` |
| J1 machine-readable plan | [survey plan JSON](../../../../../pwsh-aot-conversion-survey/analysis/j1-conversion-plan-v1/plan.json) | `fdc145e1890f1600c42efde6b155782c3b3e72579201f5477b18f06ffe0c5fc5` |
| J1 materialized source-fact index | [survey evidence index](../../../../../pwsh-aot-conversion-survey/evidence/materialized/j1-source-facts-v1/index.json) | `58b24de04577e710448a3ff7dfc7f3ee3802e3dcef7627f4c1d2c9cc270f7a5e` |

## Target contracts examined

- [DLAR process](../../../architecture/dual-lens-architecture-review.md)
- [Value plane](../../../architecture/value-plane.md)
- [Provider and host substrate](../../../architecture/provider-host-substrate.md)
- [Diagnostic contract](../../../architecture/diagnostic-contract.md)
- [Upstream reuse governance](../../../architecture/upstream-reuse-governance.md)

## Upstream sources named by the reviewed packet

- `src/System.Management.Automation/namespaces/Navigation.cs`
- `src/System.Management.Automation/namespaces/CombinePathCommand.cs`
- `src/System.Management.Automation/namespaces/ParsePathCommand.cs`

The exact upstream revision and file hashes are retained in the materialized
J1 source-fact index above. This package does not duplicate or reinterpret
those raw extraction records.
