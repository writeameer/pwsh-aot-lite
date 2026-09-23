# Review: J0 closed JSON codec foundation

Date: `2026-09-23`  
Claim reviewed: **implemented codec foundation only** for a pure, closed
`System.Text.Json` codec between UTF-8 JSON and existing immutable `AotValue`;
neither JSON cmdlet is supported or registered.
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
- Managed evidence: `dotnet build -c Release --no-restore -v:minimal` and
  `dotnet run -c Release --no-build -- --self-test` passed. The focused test
  covers one-root decode, ordering, `Int64`→`Decimal`→finite-double precedence,
  all `AOT6301`–`AOT6306` failure families, supplied spans, cancellation,
  exact-fit output budgets, and the whitespace-key variance.
- Parser/campaign evidence: parser reuse guard, 36-fixture stock differential
  baseline (including `36-json-key-boundaries`), Phase 10 manifest, and batch
  queue verification passed.
- Fresh native evidence: `artifacts/osx-arm64-json-codec-j0/PwshAotLite`,
  SHA-256 `a5cc3f7bd199cf687c7175d9dd2b696e3f9fb68682b83f62d99bd22c80522a1e`,
  was freshly published from this branch and passed `--self-test`. Direct
  `ConvertFrom-Json "{}"` remains rejected with `AOT2001`, proving J0 added no
  command registry adapter.
- Approved AOT replacement exception: `json-closed-value-codec` only. It is
  verified as a foundation; command adapters remain blocked on their own
  lifecycle, binding, output, and stock/native oracle evidence.

## Independent verdicts

| Persona | PASS / BLOCK | Evidence-backed finding | Resolution / accepted variance |
| --- | --- | --- | --- |
| Native AOT Boundary Sentinel | **PASS** | Independent implementation review found only manual `Utf8JsonReader`/`Utf8JsonWriter` traversal over the existing closed union; no Newtonsoft, `JsonSerializer`, `JsonNode`, `PSObject`, hashtable, arbitrary CLR-object boundary, reflection, dynamic loading, registry, or host dependency. Decode caps before conversion; the bounded writer enforces committed output bytes while allowing only its fixed 256-byte scratch floor. Fresh native self-test passed. | Codec foundation only; do not register either cmdlet. |
| Static Data-Plane & Binder Guardian | **PASS** | Independent implementation review verified the closed decode/encode API, one-root rule, per-container limits, `Int64`→`Decimal`→finite-double precedence, ordered records/lists, source-spanned `AOT6301`–`AOT6306`, exact-fit output budgets, cancellation propagation, and whitespace-key `AOT6304`. No binder or descriptor changed. | Codec foundation only; command lifecycle and descriptors remain deferred. |
| Diagnostic Experience Guardian | **PASS** | The focused managed and native self-tests prove typed diagnostics at the supplied caller span and preserve host-owned cancellation; no library exception reaches the execution host. `AOT6307` remains reserved because cancellation propagates unchanged. | Plain/ANSI command snapshots are required only when a command adapter exists. |
| Compatibility Proof Adversary | **PASS (foundation scope)** | No source cmdlet input/output or terminal presentation is claimed. The review verified that both JSON command names remain unregistered (`AOT2001`), while the grammar fixture is explicitly parser-only. The retained future oracle requirement includes the stock whitespace-key success versus AOT6304 rejection. | A controlled raw stock/native runtime oracle is mandatory before either JSON command can be supported. |
| Upstream Reuse & Format-Contract Reviewer | **PASS** | The implementation preserves the documented non-copyable `JsonObject` boundary, uses the approved closed replacement only, and records `json-case-collision`/`json-whitespace-key` under `AOT6304`. Stock accepts a whitespace-only property name while J0 rejects it fail-closed; the variance and future oracle are explicit. | No terminal/output-format compatibility is claimed; a future adapter requires retained stock/native raw-output or typed-enumeration oracle evidence for every admitted surface. |

## Outcome

`verified reusable foundation` — the codec is executable only through focused
self-tests. No registry change or JSON cmdlet compatibility claim is permitted
until an adapter has separate all-reviewer PASS verdicts and the required
controlled stock/native oracle.
