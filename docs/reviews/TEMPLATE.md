# Review: <scope>

Date: `<YYYY-MM-DD>`  
Claim reviewed: `<what may be integrated or called supported>`  
Upstream commit: `<commit or N/A>`

## Evidence

- Source/provenance: `<links or paths>`
- Build and tests: `<exact commands and outcome>`
- Native AOT evidence: `<publish/run command and outcome, or N/A>`
- Differential parser evidence: `<stock pwsh corpus/tests, or N/A>`
- Upstream reuse matrix: `<link to the per-cmdlet matrix; N/A only when this is not a port>`
- Format/type-data evidence: `<upstream producer and view/type-data locations for every emitted default field/column; N/A only when no output/rendering changes>`
- Runtime oracle comparison: `<normal pwsh command/version, Native AOT artifact/RID, controlled fixture, allowed normalization, raw snapshot location, and match/variance result; N/A only when no output/rendering changes>`
- Approved AOT replacement exceptions: `<variance IDs, existing-service search, capability authority, and fail-closed boundary; none if fully copied/adapted>`

## Independent verdicts

| Persona | PASS / BLOCK | Evidence-backed finding | Resolution / accepted variance |
| --- | --- | --- | --- |
| Upstream Grammar Steward |  |  |  |
| Native AOT Boundary Sentinel |  |  |  |
| Static Data-Plane & Binder Guardian |  |  |  |
| Compatibility Proof Adversary |  |  |  |
| Upstream Reuse & Format-Contract Reviewer |  |  |  |

## Outcome

`<integrated | experiment-only | deferred>` — explain any remaining limitation
without presenting it as supported behavior.

The Upstream Reuse & Format-Contract Reviewer is required for every cmdlet port
or any change to emitted fields/default display. It may be performed by the
Static Data-Plane & Binder Guardian only for a behavior-only port with no
output/rendering change. A `PASS` attests that the linked matrix is complete,
every replacement has an approved AOT exception, and no field/column is
invented without an explicit variance. It also attests that every admitted
output/default-display surface has a repeatable normal-pwsh-versus-native oracle
comparison whose normalization does not erase semantic or presentation drift.
