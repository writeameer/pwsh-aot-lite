# Evidence and provenance

## External evidence

| Artifact | Immutable source location | SHA-256 |
| --- | --- | --- |
| accepted J1 v11 plan | [survey plan](../../../../../pwsh-aot-conversion-survey/analysis/j1-conversion-plan-v11/plan.json) | `cdc099ae571f8995a16bbf8ec5f141d0429dca22618cc8b4b787e168629b2af1` |
| `Join-Path` handoff | [survey handoff](../../../../../pwsh-aot-conversion-survey/runs/j1-conversion-run-v1/items/08-join-path/compiler-artifacts/JoinPath.handoff.json) | `321d098e24ba56b1f41ffa295e9cc6702460742968714787da33e8a1518970fe` |
| `Split-Path` handoff | [survey handoff](../../../../../pwsh-aot-conversion-survey/runs/j1-conversion-run-v1/items/12-split-path/compiler-artifacts/SplitPath.handoff.json) | `3a358e19844856b1f41ffa295e9cc6702460742968714787da33e8a1518970fe` |
| explicit J1 substrate requirements | [survey requirements](../../../../../pwsh-aot-conversion-survey/runs/j1-conversion-run-v1/shared/requirements/J1Requirements.json) | `d01eb92cdf613be418ccfd35a35495abd606c2f3a732437ce633e38e51fa8b09` |

## Target seams inspected

- `AotValue.cs`: closed immutable value ingress.
- `Pipeline.cs`: lifecycle, generated descriptor construction, sole registry,
  scalar records, and self-test host.
- `AotCommonParameters.cs`: finite shared common-parameter extraction.
- `AotDiagnostics.cs`: structured diagnostics/source renderer.
- `HelpCatalog.cs`: catalog/availability integration.
- `HostSubstrate.cs` / `PhysicalChildItemCatalog.cs`: negative evidence; J1
  must not depend on these authority-bearing paths.

The prior accepted package pins `CombinePathCommand.cs` at
`0182ef136e8c67e8c177daa1e601639f573be467ea448fb6d65c9fce53f48b09` and
`ParsePathCommand.cs` at
`11674c9e691a38198246c281712db1aae067b72bfbe7b110881ad6dd5848dd31`.
The local compiler handoff is commit `00e530f`; it has no remote and is not a
target implementation assertion.
