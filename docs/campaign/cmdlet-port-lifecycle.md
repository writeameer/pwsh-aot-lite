# Cmdlet-port lifecycle

This is the required, canonical lifecycle for every campaign cmdlet. Read it
before research, converter work, or target implementation. The detailed
repository rules are in [AGENTS.md](../../AGENTS.md), the execution schedule is
the [Phase 10 campaign status](README.md), and review records live in the
[review index](../reviews/README.md).

A command **counts as migrated only after step 9**: the target implementation
has passed its native verification, every required review is PASS, and the
verified branch has been non-fast-forward merged and pushed to `main`. Plans,
profiles, generated handoffs, and implementation branches are evidence—not a
migration count.

| Step | Required output | Completion gate |
| --- | --- | --- |
| 1. Extract | Hash-pinned per-cmdlet upstream source and AST facts | Deterministic retained records and checksums |
| 2. Plan | Source-family plan plus an explicit disposition for every command | No unaccounted command or inferred route |
| 3. Design review | DLAR semantic and typed-data/AOT verdicts | Both lenses PASS; all BLOCK findings reconciled |
| 4. Update converter | Only approved deterministic profiles and explicit substrate outcomes | Managed and Native AOT compiler self-tests pass |
| 5. Run converter | One retained output per cmdlet | No silent skip or AI-selected fallback |
| 6. Target readiness | File-level substrate, adapter, diagnostics, fixture, and reuse design | DLAR PASS before target-runtime code |
| 7. Implement | Narrow shared substrate and/or generated adapter | No dynamic engine, duplicate binder/grammar, provider leakage, or unsupported silent behavior |
| 8. Verify | Stock-oracle subset, parser fixtures, managed tests, fresh Native AOT publish/self-test, focused smoke tests, and reviewer ledgers | Required architecture, AOT-boundary, diagnostics, and compatibility reviews PASS |
| 9. Release | Commit, non-fast-forward merge, push, status/variance/timing/evidence updates | Only this step changes the migrated count |

## Evidence locations

- `docs/cmdlets/<command>.md` — scope, source-reuse matrix, behavior boundary,
  and verification evidence.
- `docs/cmdlets/port-timing.md` — UTC start/end and elapsed wall-clock record.
- `port-variances.json` — machine-readable preserved behavior, replacements,
  subsets, and deferrals.
- `docs/reviews/` — ordinary review ledgers and structured DLAR packages.
- `docs/campaign/phase10-built-in-cmdlets.json` — source classification
  authority; `phase10-batch-queue.*` is its generated execution accounting.
- `../../../pwsh-aot-conversion-survey/` — immutable survey, extractor,
  converter, and retained per-cmdlet evidence.

## Branch and release discipline

Each implementation slice starts from current `main` on its own
`codex/<slice-name>` branch. Keep the slice narrow; resolve every review BLOCK
before integration. The release steward pushes the reviewed slice branch,
creates a non-fast-forward merge to `main`, pushes `main`, then updates the
campaign count. Do not mark a command migrated earlier in the lifecycle.

The implementation-specific checklist in [AGENTS.md](../../AGENTS.md#port-workflow)
expands lifecycle steps 6–9; it does not replace this lifecycle.
