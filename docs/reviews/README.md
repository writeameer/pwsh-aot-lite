# Review records

This directory holds review evidence for support claims and architecture
decisions. It is an index, not a dumping ground: use the appropriate
structured location below and link the record from the affected design note,
cmdlet note, profile decision, or campaign plan.

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
| `2026-09-23-j1-physical-path-profiles` | `Join-Path` / `Split-Path` profile design | **BLOCK** pending plan amendments | [package](dlar/2026-09-23-j1-physical-path-profiles/README.md) |

## Other review ledgers

Existing date- and cmdlet-named Markdown files predate the DLAR package
convention. Keep them as historical evidence; new DLAR work belongs under
`dlar/`, while ordinary reviewer-matrix ledgers continue to use
[TEMPLATE.md](TEMPLATE.md).
