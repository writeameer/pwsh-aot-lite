# Dual-Lens Architecture Review (DLAR)

DLAR is a short, repeatable design gate for changes whose static AOT boundary
and PowerShell compatibility contract must agree. It supplements the required
independent reviewer roles in [reviewer-personas.md](reviewer-personas.md); it
does not replace them.

## Purpose

Use two independent design lenses to expose a mismatch before implementation:

1. **PowerShell semantic and compatibility lens** — preserves the admitted
   PowerShell command contract: binding, parameter sets, cardinality, output,
   diagnostics, and explicit variances from pinned upstream behavior.
2. **Typed data-plane and AOT lens** — protects closed values, typed pipeline
   shapes, capability authority, deterministic binding, and the Native AOT
   boundary.

These are **design perspectives, not people**. Never impersonate, attribute a
review to, or imply endorsement by a real person, project founder, or project.
An optional phrase such as “inspired by PowerShell design principles” belongs
only in scope context; the verdict is owned by the named lens and its evidence.

## When DLAR is required

Run DLAR before integration when a change introduces or materially changes any
of the following:

- a new cmdlet-profile family or converter template;
- a shared substrate/capability contract;
- a pipeline or value-plane boundary;
- a formatting or diagnostic contract; or
- compatibility-affecting admitted, rejected, binding, output, or error
  behavior.

It is required for a profile-design decision before that profile is generated
or used to claim a port. It is not required for a docs-only correction that
does not alter a contract, although linking the affected ledger is encouraged.

## Required inputs

Each lens receives the same fixed packet:

1. Scoped change and exact support/integration claim.
2. Pinned upstream sources, source hashes, generated metadata, and retained
   extraction/converter evidence where applicable.
3. Current target seams, dependency/capability map, and relevant architecture
   contracts.
4. Proposed admitted/rejected parameter, output, diagnostic, and variance
   tables.
5. Exact managed, parser/differential, stock-oracle, and Native AOT evidence
   available at the time of review.
6. A completed [profile-design preflight](profile-design-preflight.md) for a
   new profile family or shared substrate. It must establish the authority
   boundary, complete static contract, closed input forms/grammar, batch and
   property-binding semantics, target-specific proof design, fixture-accounting
   derivation, and deferred remainder before either lens reviews the packet.

An incomplete packet is a `BLOCK`, not an invitation to guess.

## Preflight before review

For profile families and shared substrates, complete the
[profile-design preflight](profile-design-preflight.md) **before** DLAR. This
turns iteration evidence into a repeatable gate: the lenses should validate a
closed contract rather than discover missing aliases, authority leakage,
untyped inputs, or ambiguous fixture counts. A missing preflight item is a
`BLOCK` until the design is narrowed or the evidence is supplied.

## Questions each lens must answer

| Lens | Required questions |
| --- | --- |
| PowerShell semantic and compatibility | Does the static contract preserve each admitted parameter set, alias, cardinality, output, diagnostic, and default behavior? Are every excluded behavior and variance explicit, source-derived, and fail-closed? Does the proposed oracle prove the claimed subset? |
| Typed data-plane and AOT | Are input and output closed, ordered typed values rather than dynamic CLR/PowerShell objects? Are text transformation and host/provider authority separated? Does the design reuse the generated binder and existing seams without reflection, runtime compilation, runspaces, providers, or ambient state? |

Both lenses must also state whether the public error and diagnostic behavior is
source-aware, stable, and actionable.

## Verdict and reconciliation rules

Each lens returns exactly `PASS` or `BLOCK`, concrete evidence, and the
smallest corrective action.

- A single `BLOCK` stops integration and any supported-behavior claim.
- A `PASS` means the scoped claim is sufficiently bounded; it is not blanket
  compatibility approval.
- Shared findings are recorded once as a shared blocker/requirement, with both
  lens references. They must be resolved before either verdict becomes `PASS`.
- Lens-specific findings stay separately attributed. If they conflict, narrow
  the claim to their common safe boundary or revise the design and rerun both
  lenses. The implementing agent may not choose one lens and ignore the other.
- Final `PASS` requires both lenses to pass and all ordinarily applicable
  reviewers in [reviewer-personas.md](reviewer-personas.md) to pass.

## Ledger and evidence

Create one dated DLAR package under `docs/reviews/dlar/<review-id>/`, using the
exact layout in [the review index](../reviews/README.md#dual-lens-architecture-review-packages)
and [DLAR-TEMPLATE.md](../reviews/DLAR-TEMPLATE.md). Link the package index
from the affected architecture note, cmdlet note, converter-profile decision,
or campaign plan. A completed package is immutable: a revised claim creates a
new dated package linked to the previous verdict. The package must retain:

- both verdicts and shared findings;
- source/metadata/capability/evidence links;
- admitted and rejected behavior tables or links;
- reconciled blockers and their resolution; and
- the exact integration decision and remaining variance.

## First exemplar: J1 physical path profiles

The first use of DLAR reviewed the proposed `Join-Path` and `Split-Path`
profiles. Both lenses returned `BLOCK`: lexical path text must be distinct from
existing-path resolution, and each static parameter/output/diagnostic surface
must be defined before profile generation.

- [Structured J1 DLAR package](../reviews/dlar/2026-09-23-j1-physical-path-profiles/README.md)
- [Immutable external evidence and hashes](../reviews/dlar/2026-09-23-j1-physical-path-profiles/evidence.md)

Those links are retained evidence only. They do not change the J1 conversion
plan or authorize implementation.

The revised packet resolved those blockers and both lenses passed its bounded
design-only claim. The accepted target design and immutable v11 evidence are
in [the accepted J1 DLAR package](../reviews/dlar/2026-09-24-j1-lexical-path-profiles-v11/README.md).
