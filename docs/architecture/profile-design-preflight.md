# Profile-design preflight

Run this deterministic design preflight before requesting a Dual-Lens
Architecture Review (DLAR) for a new converter-profile family or a
compatibility-affecting shared substrate.  Its purpose is to make review a
confirmation of a closed packet, not a discovery session.

The first exemplar is the J1 `Join-Path` / `Split-Path` design.  Its iteration
history is retained in the [accepted J1 design](j1-direct-lexical-path-profiles.md)
and its review packages.

## Required packet

| Check | Required record before DLAR | Why it matters |
| --- | --- | --- |
| Source identity | pinned upstream paths, hashes, extracted parameters/base/helpers | prevents a profile from drifting from its examined source |
| Authority boundary | named pure transformation versus named provider/filesystem/host capability | prevents lexical operations from acquiring ambient authority |
| Static contract | every admitted and rejected parameter, alias, set, cardinality, pipeline mode, output kind, and diagnostic | prevents generated metadata from being silently ignored |
| Closed dialect/grammar | ordered full-string acceptance and rejection rules; no prefix match or platform reinterpretation | prevents accidental provider/foreign-path semantics |
| Typed inputs | distinct input forms when their invariants differ, such as `PathText` and `PathTextChildFragment` | prevents a broad string type from smuggling invalid data across a boundary |
| Batch semantics | binding order, first-invalid rule, output cardinality, and zero-output atomicity | makes collection behavior deterministic and testable |
| Property binding | source-declared property routes, target decision, diagnostic, and source span | avoids quietly accepting dynamic object/property semantics |
| Proof design | target-specific fixtures, stock oracle where applicable, exact diagnostic/span/output assertions, authority-call assertions | proves the target contract rather than merely compiling it |
| Fixture accounting | fixture categories, unique IDs, and a mechanically derived total | prevents an unreviewable “enough tests” assertion |
| Deferred remainder | source-pinned `requires-substrate` or unsupported outcome with next approval point | distinguishes conversion evidence from runtime availability |

## Lessons carried forward from J1

1. **Text transformation and authority are separate.** A lexical profile must
   not reuse a resolver merely because both accept path-like text.
2. **The static contract is the feature.** Parameters, aliases, output kinds,
   binding and diagnostics are declared before code generation—not inferred by
   an adapter.
3. **Grammar stays closed.** A target dialect accepts full strings by an ordered
   contract.  It does not grow a second PowerShell grammar or let host APIs
   decide semantics.
4. **Types capture distinct invariants.** Use a separate closed input form when
   composing child text has different rules from accepting path text.
5. **Atomicity is observable behavior.** A mixed collection must specify its
   first failure, output count, span, and side-effect/authority count.
6. **Source property binding is never assumed transferable.** Its target choice
   is explicit and source-spanned.
7. **Fixtures are target-specific and counted mechanically.** The review packet
   names the cases that prove the bounded target, not a vague upstream test
   count.

## Handoff to DLAR

Attach the completed preflight to the DLAR packet.  A missing row is a `BLOCK`
until the claim is narrowed or the record is completed.  Passing DLAR still
does not authorize implementation beyond its stated claim; the ordinary
reviewer matrix and implementation evidence remain required.
