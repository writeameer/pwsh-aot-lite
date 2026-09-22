# Review: Phase 7 general typed data pipeline

Date: `2026-09-22`
Claim reviewed: `Known typed cmdlet and static-function output may cross once into an immutable ordered AotRecordBatch and apply up to four direct, AST-lowered Where-Object/Select-Object transforms.`
Upstream commit: `pinned checkout; Parser.cs and ast.cs are consumed through the existing language facade`

## Evidence

- Source/provenance: `AotRecordBatch.cs`, `PipelineValueAdapter.cs`, `Pipeline.cs`, `AotLanguageCore.cs`, and `UpstreamAstPipelineLowerer.cs`; no upstream source was modified.
- Build and tests: Release build, managed `--self-test`, parser-reuse guard,
  26-fixture parser verification, and `git diff --check` passed. Deterministic
  tests cover mid-conversion, mid-transform, and buffered terminal cancellation.
- Native AOT evidence: fresh self-contained `osx-arm64` publish and
  `--self-test` passed, including function-batch composition and direct help prose.
- Differential parser evidence: fixtures `23` through `26` cover repeated transforms, function record batches, transform boundaries, and function-producer shape boundaries; stock verification passed all 26 fixtures.

## Independent verdicts

| Persona | PASS / BLOCK | Evidence-backed finding | Resolution / accepted variance |
| --- | --- | --- | --- |
| Upstream Grammar Steward | PASS | Pinned upstream ASTs remain the sole syntax source; ordered tail plans preserve source order. | Four-stage cap is a documented feature policy. |
| Native AOT Boundary Sentinel | PASS | Canonical immutable batches/shapes and terminal-only compatibility projection introduce no dynamic path. | Transitional test-only compatibility adapter remains outside host execution. |
| Static Data-Plane & Binder Guardian | PASS | Explicit registered projectors, `AOT4009`, and the existing sole cmdlet binder retain a one-way typed boundary. | Records never become generic cmdlet input. |
| Diagnostic Experience Guardian | PASS | `AOT4009`/`AOT4011` renderer snapshots and cancellation/output tests prove stable fail-closed behavior. | Existing transform IDs retain their prior meanings. |
| Compatibility Proof Adversary | PASS | Ordered transforms, producer shape policy, help presentation, return output, and mid-loop cancellation are tested. | No object stream, script blocks, or general pipeline binding is claimed. |
| Architecture Guard | PASS | Function producers preserve canonical batches; only terminal projection creates compatibility rows. | Heterogeneous no-projection output fails closed. |

## Outcome

`PASS — integrated` — later `Where-Object` sees the preceding projection shape, and the last projection sets terminal shape. Heterogeneous producer output without a direct projection, general object streams, function input, reflection, and terminal-text capture remain unsupported. `AOT4010` means only incompatible registered typed-input records; `AOT4011` is the distinct explicit record-shape contract mismatch. Direct untransformed `Get-Help` (including through a direct local-function call) is prose, while ordinary `Value` output and transformed help remain tables.
