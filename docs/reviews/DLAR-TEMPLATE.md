# DLAR package: <review-id>

Create the following package; do not put a new DLAR ledger loose in
`docs/reviews/`:

```text
docs/reviews/dlar/<review-id>/
  README.md
  evidence.md
  lenses/powershell-compatibility.md
  lenses/typed-data-plane-aot.md
  findings.md
  reconciliation.md
```

Use a lowercase, date-prefixed, scope-specific `<review-id>`. `README.md` uses
the ledger below. `evidence.md` pins source locations and SHA-256 hashes. Each
lens file must carry the exact perspective disclaimer, verdict, evidence,
findings, and smallest corrective action. `findings.md` deduplicates shared
findings and keeps lens-specific ones separate; `reconciliation.md` lists every
amendment and disposition. Copy external review content only with provenance;
do not modify external source evidence.

# DLAR: <scope>

Date: `<YYYY-MM-DD>`  
Claim reviewed: `<exact integration/support claim>`  
Upstream commit: `<commit or N/A>`

## Required packet

- Pinned source and source-fact evidence: `<links>`
- Generated metadata/profile/template: `<links or N/A>`
- Current target seam and capability authority: `<links>`
- Admitted/rejected behavior and variance table: `<links>`
- Managed/parser/oracle/Native AOT evidence: `<exact commands, outputs, or N/A>`

## Independent lens verdicts

| Lens | PASS / BLOCK | Evidence-backed finding | Smallest corrective action |
| --- | --- | --- | --- |
| PowerShell semantic and compatibility |  |  |  |
| Typed data-plane and AOT |  |  |  |

## Reconciliation

### Shared findings

`<finding, evidence links, and resolution; none only when both lenses have no shared finding>`

### Lens-specific findings

`<finding, owner, and resolution; none only when absent>`

## Outcome

`<PASS | BLOCK>` — `<exact bounded decision, remaining variance, and next approval point>`

Both lenses are design perspectives, not real people or endorsements. A single
`BLOCK` prevents integration or a supported-behavior claim. DLAR supplements,
but does not replace, the ordinary reviewer dispatch in
[`reviewer-personas.md`](../architecture/reviewer-personas.md).

The PowerShell lens must say: “PowerShell design-principles-inspired
perspective—not authored by or attributed to any real person.” If a
user-requested named-person framing is retained for historical provenance, it
must additionally say it is not authored by or attributed to that person. The
typed data-plane lens follows the same rule. Never imply a real person's
participation or endorsement.
