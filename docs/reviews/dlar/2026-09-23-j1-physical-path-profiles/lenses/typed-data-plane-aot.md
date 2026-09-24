# Typed data-plane and AOT lens

**Perspective:** **Sophia Turner-inspired user-requested perspective—not
authored by or attributed to any real person.** This is an evidence-backed
structured-data design lens, not an impersonation, endorsement, or statement
by Nushell contributors or any real person.

**Verdict:** **BLOCK** pending the amendments recorded in
[reconciliation.md](../reconciliation.md).

**Scope reviewed:** J1 plan v1 only. No runtime, converter, profile, or source
change was made or approved by this review.

## Strengths retained from the reviewed design

- The closed `AotValue` / `AotRecord` plane remains intact: no `PSObject`,
  reflection, dynamic providers, or second binder.
- The twelve other commands retain source-pinned `requires-substrate` outcomes
  rather than smuggling in content, session state, formatting, host launch, or
  security authority.
- `Join-Path` and `Split-Path` are credible first candidates because their
  source identities, parameter surfaces, and provider dependencies are pinned;
  the current plan also separates `-Resolve` from non-resolving work.

## Findings

### Keep path text separate from filesystem authority

Non-resolving `Join-Path` and `Split-Path` are lexical value transformations.
They must not call the existing-path resolver or require an item to exist.
`-Resolve`, wildcarding, provider navigation, drive resolution, and metadata
acquisition each require a separately admitted capability.

### Define the closed value contract

The plan needs a `PathText` policy: lexical host-path text represented as an
`AotValue.String` under static command metadata, not a `PathInfo`, `FileInfo`,
property bag, or dynamic CLR object. It must state accepted input, host dialect,
relative-path handling, separators, empty/whitespace segments, wildcard text,
provider-qualified input, and foreign platform spellings. It performs no
existence check.

For every admitted path input, one ordered output must be emitted unless an
explicitly documented mode changes the result kind (for example, a Boolean
`-IsAbsolute` mode). `Join-Path` output must compose into `Split-Path` and a
later direct path consumer through the existing single value boundary.

Host path dialect is captured once in immutable platform configuration. The
profile must not silently reinterpret Linux/macOS/Windows spellings or inspect
an ambient current directory. A relative-path rule may use the existing
captured root only if the selected lexical operation actually requires it.

### Static sets and diagnostics are part of the contract

`Join-Path` needs a supported/rejected table for `Path`, `ChildPath`,
`AdditionalChildPath`, `Extension`, and `Resolve`; the remaining-arguments
group remains atomic. `Split-Path` needs one static row per admitted
alternative, including default `Parent` and output kind. Unsupported provider
qualifiers, switches, invalid parameter sets, and unrepresentable `PathText`
need stable IDs, primary messages, source spans, and help.

Runtime `AOT2001` remains the diagnostic for commands that are unregistered.
`requires-substrate` belongs only in conversion evidence and is never a
user-facing cmdlet replacement.

## Smallest corrective action

Add the `PathText` policy, P1 operation table, P2 parameter-set table,
composition fixture plan, and diagnostic matrix to the J1 plan; then rerun
both DLAR lenses before any converter change.

## Provenance

This is a structured in-repository review record derived from the immutable
[original survey review](../evidence.md#immutable-external-evidence), SHA-256
`ee2cede2111bcabcfd0b0f76ae3b42b230aaf2005fad1c8435a27713fcc1ad54`.
