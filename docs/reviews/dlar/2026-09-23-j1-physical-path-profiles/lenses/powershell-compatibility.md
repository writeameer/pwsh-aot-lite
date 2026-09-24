# PowerShell semantic and compatibility lens

**Perspective:** **Jeffrey Snover-inspired perspective—not authored by or
attributed to Jeffrey Snover.** This is an evidence-backed design lens, not an
impersonation, endorsement, or statement by any real person.

**Verdict:** **BLOCK** pending the amendments recorded in
[reconciliation.md](../reconciliation.md).

**Scope reviewed:** the immutable J1 plan v1, retained J1 source facts,
upstream `CombinePathCommand.cs` / `ParsePathCommand.cs`, and this runner's
provider, diagnostic, and upstream-reuse contracts. The block makes the
claimed cmdlet surface exact; it does not recommend adding a provider engine.

## Strengths retained from the reviewed design

- It treats a missing profile as conversion evidence rather than an
  implementation failure, and accounts for all fourteen commands.
- It does not pretend `SessionState.Path` or provider calls can be represented
  by a lexical physical-path helper; `requires-substrate` is preferred to a
  fake compatible cmdlet.
- `Join-Path` preserves positional groups, including
  `ValueFromRemainingArguments`, rather than flattening them into an arbitrary
  argument list.
- `Split-Path` recognizes parameter-set binding rather than string slicing.
- It requires source hashes, parser fixtures, native proof, upstream oracle,
  and fail-closed diagnostics.

## Findings

### Exact static surface is missing

The proposal says “direct physical paths” but does not name every admitted
parameter, alias, parameter set, cardinality, or excluded upstream feature.
That could accept generated metadata then ignore a switch, or omit a lexical
feature without an explicit diagnostic.

- `Join-Path` exposes `Path`, `ChildPath`, `AdditionalChildPath`, `Resolve`,
  and `Extension`.
- `Split-Path` exposes `Path`, `LiteralPath` (`PSPath`/`LP`), `Parent`,
  `Leaf`, `LeafBase`, `Extension`, `Qualifier`, `NoQualifier`, `IsAbsolute`,
  and `Resolve`.

Before generation, every parameter and alias must be either admitted with
binding/null/empty semantics or excluded with a stable source-spanned AOT
diagnostic. `Split-Path` must enumerate every admitted direct-path alternative
and prove mutual exclusion/default behavior through binder fixtures.

### Lexical path work must remain distinct from resolution

The existing target resolver is for existing physical paths. Ordinary
`Join-Path` and non-`Resolve` `Split-Path` operations are lexical and can act
on paths that do not exist. A profile must therefore define separate, narrowly
named lexical compose/decompose operations that never check existence,
enumerate, expand wildcards, follow links, or consult providers.

The amended contract needs captured-root behavior for relative input, roots,
trailing separators, host separators, empty versus whitespace input, and
ordered multi-value output. It must compare admitted cases with stock
PowerShell and record every intentional variance.

## Non-negotiable guardrails

- Preserve upstream-generated cmdlet metadata; do not hand-copy aliases,
  positions, mandatory state, or parameter sets.
- Do not add a provider engine, drive/session-state model, wildcard expansion,
  dynamic parameter discovery, `PSObject`, or ambient filesystem fallback.
- `requires-substrate` is a pinned planning result, not an available cmdlet.
- Rejected runtime use must flow through the shared diagnostics contract.
- Preserve admitted pipeline/array cardinality or explicitly reject it.

For `Split-Path`, do not claim `Qualifier` or `NoQualifier` without a defined
direct-path qualifier model. Do not claim `-Resolve` without existing-path and
wildcard semantics. Both remain explicit exclusions until their own bounded
contracts exist.

## Smallest corrective action

Add source-derived P1/P2 profile-surface tables and a lexical `PathText`
contract to the J1 plan, then submit the amended packet to both lenses again.

## Provenance

This is a structured in-repository review record derived from the immutable
[original survey review](../evidence.md#immutable-external-evidence), SHA-256
`bba6133abbbe49e293a3701b850a1668aee53edf6c474c67c83b11380c604ce8`.
