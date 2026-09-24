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
