# `<Cmdlet-Name>` port notes

Use this file to start every cmdlet investigation, including one that will be
deferred. It is a migration evidence record, not a compatibility claim. Keep
the matching `port-variances.json` entry, the review ledger, and the index in
[`../README.md`](../README.md) synchronized with this note.

## Status and source

`<in progress | deferred | complete for an explicitly stated target subset>`

- Original: `<pinned upstream source path and command/base class>`
- Generated contract: `<GeneratedCmdletPorts member>`
- Target: `<target implementation files/types, or N/A while deferred>`
- Review ledger: `<docs/reviews/... or planned>`

## Port timing

| Field | Value |
| --- | --- |
| UTC work started | `<YYYY-MM-DDTHH:mm:ss.fffffffZ>` |
| UTC work ended | `<YYYY-MM-DDTHH:mm:ss.fffffffZ, or — while in progress>` |
| Elapsed wall clock | `<HH:MM:SS, or — while in progress>` |
| Scope note | `<one concise statement of the port slice timed>` |

Record the start when work begins on this cmdlet (research/design is included),
and the end only after the slice has passed its required verification and is
integrated to `main`. `Elapsed wall clock` is the UTC end minus UTC start; it
is not a claim about active engineering effort and includes review/build wait
time. Use UTC ISO 8601 timestamps with a `Z` suffix. For historical work whose
timestamps were not captured, write `unavailable (not recorded)` for all three
timing fields; never estimate or backfill a time. Add the same values to the
aggregate [port timing ledger](port-timing.md).

## What transferred

- `<source behavior/code reused or replaced through a named AOT service>`

## Upstream reuse evidence matrix

Complete this before implementation. Cite the pinned upstream source for every
in-scope behavior and emitted field. For default display columns, cite the
producer **and** upstream format/type-data view. The only permitted decisions
are `copied`, `adapted`, `static-data extracted`, `shared-substrate
replacement`, and `deferred`; see
[upstream reuse governance](../architecture/upstream-reuse-governance.md).

| Surface | Exact upstream evidence | Reuse decision | AOT target / contract | Why direct copy is or is not possible | Variance ID | Verification |
| --- | --- | --- | --- | --- | --- | --- |
| Command metadata | `<pinned source:line(s); generated member>` | `<...>` | `<...>` | `<...>` | `<...>` | `<...>` |
| Lifecycle/base behavior | `<...>` | `<...>` | `<...>` | `<...>` | `<...>` | `<...>` |
| Output field / display column | `<producer + format/type-data source>` | `<...>` | `<...>` | `<...>` | `<...>` | `<...>` |

If a source dependency cannot cross the AOT boundary, document the approved
replacement exception: searched existing target abstractions, the narrow
shared/static replacement, capability authority when applicable, fail-closed
deferred behavior, and a `port-variances.json` ID. “Simpler” or “temporary” is
not a rationale.

## What did not transfer

- `<engine dependency, failed approach, or explicit deferred behavior>`

## Verification

```powershell
# List commands actually run, including a focused Native AOT smoke.
```

## Variances and reusable learnings

- `<variance IDs, concrete why-not-copy rationale, and lessons that could become generator/shared-runtime work>`

## Format-contract status

- `<for each emitted default field/column: upstream producer + view evidence, or explicit display variance and convergence path>`
- `<native snapshot/smoke command proving the currently claimed output contract>`

### Runtime oracle comparison

Run normal `pwsh` and the published Native AOT binary against the same
controlled input/fixture. Preserve representative raw output or a checked
snapshot, then record only the narrow normalization allowed by
[upstream reuse governance](../architecture/upstream-reuse-governance.md).

| Surface compared | Stock `pwsh` command/version | Native command/artifact/RID | Fixture/input | Normalization (or `none`) | Result / variance ID |
| --- | --- | --- | --- | --- | --- |
| `<field/default table>` | `<...>` | `<...>` | `<...>` | `<explicit rule>` | `<match / variance / deferred>` |

## Next action

`<specific next action, or None only after the port is genuinely complete>`
