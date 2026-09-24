# Review records

This directory holds review evidence for support claims and architecture
decisions. It is an index, not a dumping ground: use the appropriate
structured location below and link the record from the affected design note,
cmdlet note, profile decision, or campaign plan.

Every review is a gate within the required [cmdlet-port lifecycle](../campaign/cmdlet-port-lifecycle.md).
Only lifecycle step 9 changes the migrated count; a review PASS alone is never
an integration claim.

## Dual-Lens Architecture Review packages

Every Dual-Lens Architecture Review (DLAR) uses this immutable-on-completion
package layout:

```text
docs/reviews/dlar/<review-id>/
  README.md                 # index, ledger, claim, verdict and decision
  evidence.md               # source packet and provenance hashes
  lenses/
    powershell-compatibility.md
    typed-data-plane-aot.md
  findings.md               # normalized shared and lens-specific findings
  reconciliation.md         # required amendments and their disposition
```

`<review-id>` is lowercase, date-prefixed, and scope-specific, for example
`2026-09-23-j1-physical-path-profiles`. Do not overwrite a completed review
packet or alter its evidence links: issue a new dated package for a revised
scope, and link both packets. External evidence is copied only as an attributed
review record; its original source remains the immutable provenance artifact.

| Review ID | Scope | Outcome | Package |
| --- | --- | --- | --- |
| `2026-09-24-j1-target-implementation-readiness-v1` | target design packet for future lexical `Join-Path` / `Split-Path` adapters | **PASS** for documentation-only readiness claim | [package](dlar/2026-09-24-j1-target-implementation-readiness-v1/README.md) |
| `2026-09-25-j2-requirements-v2` | amended all-deferred J2 requirement disposition; no approved profile | **PASS** for documentation-only Step 2/3 claim | [package](dlar/2026-09-25-j2-requirements-v2/README.md) |
| `2026-09-25-j2-target-readiness-v1` | target design packet for future static-record `Where-Object` / `Select-Object` adapters | **BLOCK**, superseded by v2 | [package](dlar/2026-09-25-j2-target-readiness-v1/README.md) |
| `2026-09-25-j2-target-readiness-v2` | amended target design: descriptor redirect plus contractual future record-batch transport | **PASS** for documentation-only readiness claim | [package](dlar/2026-09-25-j2-target-readiness-v2/README.md) |
| `2026-09-24-j1-lexical-path-profiles-v11` | accepted closed lexical `Join-Path` / `Split-Path` profile design | **PASS** for design-only Step 2 claim | [package](dlar/2026-09-24-j1-lexical-path-profiles-v11/README.md) |
| `2026-09-23-j1-physical-path-profiles` | `Join-Path` / `Split-Path` profile design | **BLOCK** pending plan amendments | [package](dlar/2026-09-23-j1-physical-path-profiles/README.md) |

## Other review ledgers

Existing date- and cmdlet-named Markdown files predate the DLAR package
convention. Keep them as historical evidence; new DLAR work belongs under
`dlar/`, while ordinary reviewer-matrix ledgers continue to use
[TEMPLATE.md](TEMPLATE.md).

The current J1 runtime-support claim is recorded in the ordinary
[J1 lexical path implementation review](j1-lexical-path-implementation.md);
the J1 DLAR packages above remain design and readiness evidence.
