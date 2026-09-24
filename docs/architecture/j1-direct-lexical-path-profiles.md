# J1 accepted design: direct lexical physical-path profiles

**Status:** accepted design; implementation has not started.

**Acceptance:** [J1 v11 DLAR package](../reviews/dlar/2026-09-24-j1-lexical-path-profiles-v11/README.md).
**Scope:** the two J1 converter-profile families only: `Join-Path` and
`Split-Path`.

This is the canonical project statement of the accepted design.  The complete
machine-readable packet remains external, immutable evidence; its exact paths
and SHA-256 identities are retained in the linked DLAR package.

## Boundary

The profiles are pure lexical transformations of direct physical-path **text**.
They do not resolve an item and must not consult the existing-path resolver,
filesystem, providers, drives, session location, current directory, wildcard
expansion, links, or ambient host state.  A path may therefore be transformed
even when no such item exists.

`PathText` is an `AotValue.String` interpreted by a fixed, immutable host
dialect.  The dialect uses an ordered full-string grammar; it never uses prefix
matching or a platform API to reinterpret an input.  POSIX accepts `/` and
Windows accepts `\\`; foreign separators, provider/drive-looking forms,
patterns, malformed roots, and NUL fail closed.  Relative text remains
relative.  No operation gains authority through text parsing.

`Join-Path` children use the narrower `PathTextChildFragment` input form.  It
is nonempty, cannot be rooted, cannot contain an empty interior segment, and
may retain trailing separators.  This distinction is intentional: a general
relative `PathText` is not automatically a legal child fragment.

## Profile contracts

| Profile | Command | Admitted static surface | Closed output | Explicit exclusions |
| --- | --- | --- | --- | --- |
| `direct-physical-path-compose` | `Join-Path` | `Path` / alias `PSPath` at position 0; `ChildPath` at position 1; `AdditionalChildPath` at position 2+ or one atomic remaining-arguments group; string pipeline for `Path` only | one `AotValue.String` per base path | `-Resolve`, `-Extension`, `-Credential`, common/dynamic/transaction parameters, and all property binding |
| `direct-physical-path-decompose` | `Split-Path` | `Path` at position 0 for `Parent` (default), `Leaf`, `LeafBase`, `Extension`, or `IsAbsolute`; `LiteralPath` / aliases `PSPath`, `LP` only for literal default-parent | `AotValue.String`, except `-IsAbsolute` emits `AotValue.Boolean` | `-Qualifier`, `-NoQualifier`, `-Resolve`, `-Credential`, common/dynamic parameters, and all property binding |

All collections are materialized and validated in binding order.  The first
invalid input emits one source-aware diagnostic and **zero output**.  `Split-Path`
admits one static parameter set only; a conflict is rejected rather than
selected dynamically.  `Join-Path` composes child strings lexically, preserving
the base and supplied trailing separators rather than normalizing them.
`Split-Path` uses the closed `last-dot-v1` rule for `LeafBase` and `Extension`;
it does not invoke provider or filesystem extension logic.

## Diagnostics and proof gate

| ID | Rejection |
| --- | --- |
| `AOT6210` | dialect, pattern, qualification, foreign spelling, or rooted-child violation |
| `AOT6211` | missing, null, empty required input, or empty child element |
| `AOT6212` | rejected parameter, alias, or source-derived property binding |
| `AOT6213` | conflicting `Split-Path` static parameter set |
| `AOT6214` | NUL, unrepresentable text, or parent-of-root |

Implementation may begin only with fixture evidence that asserts exact output
or diagnostic and primary script span, zero output on the first-invalid rule,
and zero calls to every excluded authority seam.  The acceptance packet requires
27 mechanically accounted fixtures: 20 child-fragment cases, 2 mixed-invalid
batches, and 5 property-binding rejection cases.  It also requires stock-PowerShell
oracles for the admitted subset, managed and fresh Native AOT runs, dialect truth
tables, set/binder cases, non-existent paths, and `Join-Path | Split-Path -Leaf`
composition.

## J1 batch disposition

Only the two commands above receive profiles in the next converter step.  The
other 12 J1 commands are retained as source-pinned planning results requiring a
shared substrate; this is not an availability or runtime-support claim.

`Get-Content`, `Get-ItemProperty`, `Get-ItemPropertyValue`, `Get-Location`,
`Get-PSDrive`, `Import-PowerShellDataFile`, `Invoke-Item`, `Out-File`,
`Pop-Location`, `Push-Location`, `Test-FileCatalog`, and `Unblock-File` remain
`requires-substrate` until a separate reviewed capability exists.

## Next approval point

The accepted design authorizes only the bounded Step 2 converter change:
introduce P1/P2 profiles and explicit requirement records.  It does not
authorize runtime registration or a support claim.  Those require the ordinary
generated-metadata, reuse-governance, diagnostic, oracle, managed, and Native
AOT gates.

## Related records

- [Accepted DLAR package](../reviews/dlar/2026-09-24-j1-lexical-path-profiles-v11/README.md)
- [Original J1 BLOCK package](../reviews/dlar/2026-09-23-j1-physical-path-profiles/README.md)
- [Profile-design preflight](profile-design-preflight.md)
- [Phase 10 campaign status](../campaign/README.md)
