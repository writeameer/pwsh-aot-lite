# Findings

## Resolved former blockers

| Previous finding | v11 resolution |
| --- | --- |
| Lexical text was conflated with existing-path resolution | named `PathText` compose/decompose operations have zero authority seam calls and non-existent-path proofs |
| Static parameter/alias/set surface was incomplete | P1/P2 tables declare admitted routes, exclusions, cardinality, output, diagnostics, and spans |
| Output and composition were underspecified | output kinds are closed and `Join-Path | Split-Path -Leaf` is a required fixture |
| Dialect and child-text semantics were too broad | immutable dialect grammars and distinct `PathTextChildFragment` rules are ordered/full-string and fixture-covered |
| Collection failure and property binding were implicit | first-invalid/zero-output and each source property route are explicit |
| Fixture count was ambiguous | 20 child fragments + 2 mixed-invalid batches + 5 P2 property-binding cases = 27 mechanically accounted fixtures |

## Retained guardrails

- A profile does not create a provider, drive/session-state model, dynamic
  binder, `PSObject`, reflection path, or runtime code generation.
- `requires-substrate` is source-pinned planning data, not an executable or
  user-facing command state.
- A design PASS is not a runtime compatibility claim.
