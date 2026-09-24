# Independent reviewer personas and review ledger

These are recurring **read-only subagent roles**. They are intentionally
separate from the implementing agent: the reviewer supplies an evidence-backed
`PASS` or `BLOCK`; it does not repair its own findings. A `BLOCK` prevents
merging work into the `PwshAotLite` executable path or claiming feature support.
An experiment may continue only when it is visibly labelled non-production.

## Required roles

| Persona | Trigger | Must inspect | Required evidence | Blocks when |
| --- | --- | --- | --- | --- |
| **Upstream Grammar Steward** | Any lexer, parser, parser-facade, AST, lowerer, syntax-tooling, or newly accepted syntax change | Diff; pinned upstream commit; exact upstream source locations; parser corpus; parser/tooling call sites | Provenance/delta record; differential `pwsh` token text/kind/extents, AST shape, and diagnostic-ID results; facade API evidence; explicit unsupported-node result | New handwritten grammar is introduced where upstream behavior exists; syntax is reinterpreted as words; an execution-only parser boundary appears; or parity evidence is missing. |
| **Language Tooling Contract Guardian** | Parser-facade API; token/span/diagnostic exposure; syntax highlighting; semantic analysis; REPL editing; editor/LSP work | Diff; parser facade; execution/tooling call sites; contract tests | Proof execution, CLI, and editor-facing consumers can obtain AST/tokens/extents/diagnostics without executing source; partial/malformed input remains diagnostic-only | The parser is hidden behind execution, a second grammar/lexer is introduced, tooling executes/evaluates source, or source-span/diagnostic fidelity is lost. |
| **Diagnostic Experience Guardian** | Execution-kernel, binder, pipeline, cmdlet/runtime public error, or unsupported-feature change | Diff; diagnostic IDs; renderer snapshots; source-span and negative-path tests | Stable typed diagnostic contract; precise source extent where source exists; actionable message/help; plain and ANSI renderer evidence; raw exception containment | A public error is an ad-hoc string or leaked exception; an unsupported/malformed path lacks a stable ID or negative snapshot; an available source extent is discarded. |
| **Native AOT Boundary Sentinel** | Project/package/reference change; parser extraction; value-plane work; extension bridge | Diff; project files; dependency graph; publish output; tooling call paths where applicable | Forbidden-API scan; no reachability to SMA, `PSObject`, `Runspace`, `PowerShell`, expression compilation, Reflection.Emit, runtime assembly loading, or code generation; tooling is parse-only; Native AOT publish and run | A dynamic runtime dependency crosses into the AOT executable path, or a tooling path executes/evaluates/imports source to inspect it. |
| **Static Data-Plane & Binder Guardian** | `AotValue`; pipeline adapters; generic verbs; cmdlet/metadata/binding work | Diff; generated metadata; source command contract; variance entry | Value invariant and pipeline tests; explicit typed adapter; proof aliases and parameter sets remain from generated metadata | A second binder emerges; `object`/reflection fallback appears; typed semantics are flattened; or an unsupported semantic is hidden. |
| **Compatibility Proof Adversary** | Before a command, syntax, pipeline stage, or milestone is described as supported | Feature inventory; native artifact; test matrix; variance documentation | Positive, negative, malformed-input, and unsupported-feature tests; native executable evidence; declared variances | Unsupported behavior is presented as support, negative cases are absent, or compatibility has not been verified. |
| **Upstream Reuse & Format-Contract Reviewer** | Every cmdlet port; any emitted-field, default-column, or rendering change | Per-cmdlet upstream-reuse matrix; pinned cmdlet/base sources; upstream format/type data; target services and variance entries | Exact producer and format/view provenance per field/column; generated-metadata proof; existing-service search; approved AOT exceptions; repeatable normal-`pwsh` versus Native-AOT oracle output with controlled fixture and documented narrow normalization | An upstream behavior was clean-room reimplemented without evidence; a reusable target/upstream abstraction was duplicated; an output field/column lacks producer/view provenance; an invented display is claimed as compatible; an oracle/normalization is missing or hides drift; or a replacement lacks a concrete why-not-copy and fail-closed variance. |

## Dispatch matrix

| Change | Mandatory independent reviewers |
| --- | --- |
| Parser, AST, or lowerer | Upstream Grammar Steward + Native AOT Boundary Sentinel |
| Parser facade, source spans/tokens/diagnostics, syntax highlighting, completion analysis, REPL editing, or editor/LSP integration | Upstream Grammar Steward + Language Tooling Contract Guardian; add Native AOT Boundary Sentinel if executable dependencies change |
| Execution-kernel, binding, pipeline, cmdlet/runtime error, or unsupported-feature diagnostic | Diagnostic Experience Guardian; add the applicable Grammar Steward, Data-Plane & Binder Guardian, and Native AOT Boundary Sentinel reviewers |
| Pipeline, `AotValue`, generic data cmdlet, or binder | Native AOT Boundary Sentinel + Static Data-Plane & Binder Guardian |
| New port or cross-cutting control-plane feature | Static Data-Plane & Binder Guardian; add Native AOT Boundary Sentinel whenever dependencies/execution change |
| Cmdlet port, emitted record fields, default table columns, or rendering | Upstream Reuse & Format-Contract Reviewer + Static Data-Plane & Binder Guardian; add Native AOT Boundary Sentinel for a new dependency/capability and Diagnostic Experience Guardian for public error changes |
| Any "supported" or milestone claim | All three guardians + Compatibility Proof Adversary |

The owner may resolve a `BLOCK` only by changing the implementation, narrowing
the claim, or recording an explicit accepted variance. A reviewer cannot be
silently bypassed.

## Reviewer prompt contract

Every dispatched reviewer receives:

1. the scoped change and the claim being evaluated;
2. relevant source/reference locations and the pinned upstream commit, when
   language behavior is involved;
3. the applicable guard documents;
4. exact build, test, differential-test, and Native AOT evidence; and
5. the instruction to report `PASS` or `BLOCK`, findings with file/line
   evidence, and the smallest corrective action.

For a cmdlet port or output/rendering change, the dispatch additionally includes
the completed matrix required by
[upstream reuse governance](upstream-reuse-governance.md), the relevant pinned
upstream cmdlet/base/format locations, the result of the existing-target-service
search, and every proposed AOT exception. The reviewer must use the BLOCK
criteria in that document; an implementation cannot substitute a prose claim
that reuse was considered.

## Review ledger

Create one entry beneath `docs/reviews/` for every gated change, using
[TEMPLATE.md](../reviews/TEMPLATE.md). Link the entry from the affected port or
architecture note. The ledger is deliberately short: it preserves the decision,
evidence, blockers, and accepted variances without duplicating implementation
documentation.

## Dual-Lens Architecture Review (DLAR)

For a new cmdlet-profile family, shared substrate, pipeline/value boundary,
formatting/diagnostic contract, or compatibility-affecting behavior, dispatch
the two independent lenses in the [DLAR process](dual-lens-architecture-review.md)
before integration. DLAR supplements this dispatch matrix; it never replaces a
required guardian. Use [the compact DLAR template](../reviews/DLAR-TEMPLATE.md)
to retain the common evidence, verdicts, shared findings, and reconciliation.
A `BLOCK` from either lens has the same integration authority as a reviewer
`BLOCK`. The lenses are named design perspectives, not real-person
impersonations or endorsements.
