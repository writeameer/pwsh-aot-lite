# Findings

## Shared blockers

| ID | Finding | Required evidence before Step 2 |
| --- | --- | --- |
| `J1-DLAR-01` | Lexical path text must not reuse existing-path resolution. | A separately named pure lexical contract and non-existent-path oracle fixtures. |
| `J1-DLAR-02` | Neither profile has an exact admitted/rejected static surface. | Source-derived parameter/alias tables, static sets, binder fixtures, and diagnostic matrix. |
| `J1-DLAR-03` | Outputs and composition must remain closed typed values. | `AotValue.String`/Boolean shape declaration and `Join-Path` → `Split-Path` composition proof. |
| `J1-DLAR-04` | Provider/session/ambient authority must not leak into a lexical profile. | Explicit exclusions for providers, drives, wildcarding, `-Resolve`, and ambient current directory. |

## PowerShell-lens-specific findings

| ID | Finding |
| --- | --- |
| `J1-PS-01` | Generated metadata must preserve aliases, positions, mandatory state, and parameter-set rules. |
| `J1-PS-02` | Cardinality/pipeline behavior must be preserved for admitted surfaces or explicitly rejected. |
| `J1-PS-03` | `requires-substrate` must be source-pinned planning evidence, never an availability claim. |

## Typed-data/AOT-lens-specific findings

| ID | Finding |
| --- | --- |
| `J1-TD-01` | Host path dialect is fixed in immutable configuration; foreign spellings are not silently reinterpreted. |
| `J1-TD-02` | `Split-Path` output kind is declared per admitted selection; dynamic object shapes are prohibited. |
| `J1-TD-03` | Runtime rejection uses shared source-aware diagnostics; converter `requires-substrate` is not a runtime diagnostic. |
