# Findings

## Resolved readiness blockers

1. The existing physical catalog resolves/acquires paths, so a separate pure
   lexical seam is required.
2. `AdditionalChildPath` retains upstream AST group boundaries; `Split-Path`
   has one closed static selector route.
3. Generated metadata, registry, availability, help, and completion use one
   declaration path to avoid discovery/execution drift.
4. Materialize/validate-first behavior, exact IDs/spans, and authority-call
   tripwires make atomicity observable.
5. The packet now names the AST-derived `AotCommandValueGroupPlan` /
   `CommandSyntaxValueGroup`, descriptor-owned `SequentialStatic` registry
   branch, and `TextRecord` / `BooleanRecord` output closure demanded by the
   typed-data/AOT review.
6. Compose now fixes named and positional group mapping: Path group to ordered
   base vector, ChildPath group to ordered shared child vector, and one
   post-child group to ordered additional fragments; no zip or cartesian route.

Both lenses PASS after this closure. The future implementation must retain the
exact group/vector fixtures and cannot widen this design into a general binder.

## Deferred decisions

- A narrow descriptor factory is permitted only after proving generic
  `CreateAotDescriptor` cannot represent the reviewed static contract.
- The Boolean scalar-record type name is implementation detail; it must remain
  a closed static record, not a CLR object.
- The other 12 J1 commands remain `requires-substrate`.
