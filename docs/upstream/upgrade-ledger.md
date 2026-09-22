# Upstream upgrade ledger

Every candidate upstream release gets one entry before source code is copied,
adapted, or pinned. This is an auditable decision log, not a changelog.

Use the candidate gate in
[upstream-release intake](../architecture/upstream-release-intake.md). A row
may become `accepted` only after all required review verdicts are `PASS` and
the candidate's compatibility matrix, campaign, variances, parser evidence,
and fresh Native AOT verification are checked in.

| Date opened (UTC) | Baseline SHA | Candidate SHA | State | Intake report | Changed seams | Review ledger | Decision / follow-up |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 2026-09-22 | `1e53f6bbab4b8791eae782474d21889f9e5d6038` | — | baseline established | — | Existing pinned provenance; no upgrade assessed | Existing per-slice ledgers | First formal matrix/ledger. Do not infer an upstream-release acceptance from this row. |

## Entry template

```text
| <UTC date> | <40-char SHA> | <40-char SHA> | in progress / accepted / rejected / deferred |
| docs/upstream/intake/<baseline>-to-<candidate>.json |
| metadata; body; format; provider-seam (or none) |
| docs/reviews/<ledger>.md |
| <exact acceptance/rejection, affected matrix rows, variance/campaign actions> |
```

Rejected and deferred candidates remain in this ledger. That history prevents a
future port from reintroducing an already-reviewed incompatible seam as an
unexamined shortcut.
