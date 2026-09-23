# Review: J0 closed JSON codec foundation

Date: `2026-09-23`  
Claim reviewed: **design-only** proposal for a pure, closed `System.Text.Json`
codec between UTF-8 JSON and existing immutable `AotValue`; neither JSON cmdlet
is supported or registered.  
Upstream commit: `1e53f6bbab4b8791eae782474d21889f9e5d6038`

## Evidence

- Source/provenance: `ConvertFromJsonCommand.cs` and `ConvertToJsonCommand.cs`
  under the pinned upstream `WebCmdlet` directory have declaration hashes in
  the Phase 10 manifest. The required helper closure is also pinned at the
  same upstream commit: `JsonObject.cs` SHA-256
  `A58A42EEA05A4CDB93F3CC770C1638C09C3B52DCE3479F016AE3D43FD460F4B7`.
- Design: [J0 codec contract](../architecture/json-codec-foundation.md),
  [ConvertFrom-Json note](../cmdlets/convertfrom-json.md), and
  [ConvertTo-Json note](../cmdlets/convertto-json.md).
- Existing target boundary: `AotValue.cs`, `PipelineValueAdapter.cs`,
  `AotDiagnostics.cs`, and the generated source contracts.
- Build/tests/native/parser evidence: **not applicable yet**; this branch has
  no executable implementation.
- Approved AOT replacement exceptions: planned `json-closed-value-codec` only;
  it remains blocked until independent verdicts and verification evidence.

## Independent verdicts

| Persona | PASS / BLOCK | Evidence-backed finding | Resolution / accepted variance |
| --- | --- | --- | --- |
| Native AOT Boundary Sentinel | **PASS** | A hand-written `Utf8JsonReader`/`Utf8JsonWriter` traversal over the existing closed union is AOT-safe. Newtonsoft, `JsonSerializer`, `JsonNode`, `PSObject`, hashtable, arbitrary `object`, reflection, dynamic loading, and code generation remain prohibited. | Codec-only approval; do not register either cmdlet. |
| Static Data-Plane & Binder Guardian | **PASS** | The repaired design fixes a closed decode/encode API, one-root rule, concrete limits, `Int64`→`Decimal`→finite floating number precedence, ordered records/lists, case-colliding-key rejection, and explicit DateTime/Bytes rejection. Generated metadata remains evidence only; no second binder or `object` input exists. | Codec-only design approval; command lifecycle and descriptors remain deferred. |
| Diagnostic Experience Guardian | **PASS** | `AOT6301`–`AOT6307` reserve malformed/limit/key/number/value-kind/cancellation policy; non-cancellation library faults are contained behind typed source-spanned diagnostics, while host-owned cancellation preserves the existing exit-130 path. | Codec-only design approval; plain/ANSI snapshots are still required with implementation. |
| Compatibility Proof Adversary | pending | No output/input subset is claimed. | Require controlled stock/native oracle before an adapter is supported. |
| Upstream Reuse & Format-Contract Reviewer | **PASS** | The notes cite the full non-copyable `JsonObject` decode/encode closures, separate helper provenance from declaration hashes, and record `json-case-collision`/`AOT6304`, including the known exact-duplicate divergence and deferred `-AsHashtable`. No terminal/output-format compatibility is claimed. | Codec-only design approval; a future adapter requires retained stock/native raw-output or typed-enumeration oracle evidence for every admitted surface. |

## Outcome

`experiment-only` — no executable change, registry change, or compatibility
claim is permitted until all applicable reviewers issue PASS and the focused
verification plan has run.
