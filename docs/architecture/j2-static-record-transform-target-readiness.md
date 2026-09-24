# J2 target implementation readiness: static record-transform adapters

> **Status:** ready for Dual-Lens Architecture Review (DLAR). This is a
> documentation-only lifecycle step-6 packet. It registers no command, changes
> no converter profile, implements no runtime code, and changes the migrated
> command count by **zero**.

**Work record:** started `2026-09-24T22:58:12Z`; ended
`2026-09-24T23:08:58Z`; elapsed `00:10:46`.

## Decision

The first J2 runtime slice is deliberately limited to two command adapters:

| Command | Exact admitted source-derived subset | Closed output |
| --- | --- | --- |
| `Where-Object` | pipeline-only `-Property <nonempty field>` plus exactly one of `-EQ`, `-NE`, `-GT`, `-GE`, `-LT`, or `-LE`, and a finite numeric `-Value` | input `AotRecord`, unchanged, for each matching row |
| `Select-Object` | pipeline-only `-Property <one-or-more explicit field names>` | immutable `AotRecord` with exactly those fields in request order |

The adapters reuse the closed `AotValue`, `AotRecord`, `AotRecordBatch`, finite
comparison, filter, projection, upstream parser, and generated-metadata binder
seams. The only new seam is a static record-batch command stage: it makes these
two operations discoverable as built-in command adapters. It is not a general
object pipeline, dynamic cmdlet host, or second parameter binder.

The pre-existing structural spellings named `Where-Object` and `Select-Object`
are not adapters and remain non-migration evidence until this slice is
implemented, verified, and released.

## Source-pinned scope

The accepted J2 v2 plan and deterministic conversion run retain all sixteen
commands as `requires-substrate`; those records remain authoritative. This
readiness packet narrows only these future candidates:

| Candidate | Upstream declaration / direct behavior evidence | Why this subset can reuse the closed plane |
| --- | --- | --- |
| `Where-Object` | `InternalCommands.cs:1279-1407` declares pipeline `InputObject`, `FilterScript`, `Property`, `Value`, and operator sets; `2126-2515` performs operand conversion, script execution, and dynamic property lookup. | The admitted property route is a strict subset of source `Property`/`Value`. Existing `AotRecord.TryGetValue` and `AotValueComparison.TryCompare` only use declared fields and finite values. |
| `Select-Object` | `Select-Object.cs:26-77` declares pipeline `InputObject`, positional `Property`, and excluded routes; `306-566` uses `PSPropertyExpression`, wildcards, expansion, and note-property mutation; `668-811` owns queue/lifecycle behavior. | The admitted route is explicit-field projection over an immutable record. `AotRecord.Project` and `AotRecordShape` already preserve field order and reject missing/case-colliding fields. |

The upstream revision is `/Users/ameerdeen/progs/PowerShell` commit
`1e53f6bbab4b8791eae782474d21889f9e5d6038`. Exact hashes and all sixteen
retained source records are in the [DLAR evidence packet](../reviews/dlar/2026-09-25-j2-target-readiness-v1/evidence.md).

## Static contracts

### `Where-Object` static numeric property route

- Pipeline stage only; it cannot be a source command or accept direct
  `InputObject`/property-name binding before an `AotRecordBatch` exists.
- `Property` is exactly one nonblank literal field name at position zero.
  Case-insensitive lookup is confined to the current `AotRecord`; no member
  enumeration, getter, dictionary probe, or CLR-property search occurs.
- `Value` is exactly one literal or reviewed closed-scope numeric value at
  position one: integer, decimal, or finite floating-point only. There is no
  string conversion, null/list/record/byte comparison, `NaN`, or infinity.
- Exactly one source operator is required: `EQ`/`IEQ`, `NE`/`INE`, `GT`/`IGT`,
  `GE`/`IGE`, `LT`/`ILT`, or `LE`/`ILE`. Source truthiness, case-sensitive,
  pattern, collection, and type operators are rejected.
- Matching rows are unchanged and retain original order. Missing or
  non-comparable fields stop before terminal projection.

### `Select-Object` static field projection route

- Pipeline stage only; `InputObject` arrives through the preceding closed
  batch, never an object binder.
- `Property` is one-or-more literal, nonblank field names at position zero or
  through generated `Property` array metadata. The existing AST-derived group
  carrier preserves `-Property Name,Id`; no text split/reparse is allowed.
- Lookup is case-insensitive; request order/spelling is the output shape.
  A missing property or case-insensitive duplicate produces no terminal row.
- `ExcludeProperty`, `ExpandProperty`, `Unique`, `CaseInsensitive`, `First`,
  `Last`, `Skip`, `SkipLast`, `Index`, `SkipIndex`, `Wait`, and omitted
  `Property` are rejected; none is silently passed through.
- Output has no source-object wrapper, `Selected.*` type name, PS note
  property, or formatter-added member. Its table columns are precisely the
  requested fields. This is a named display variance pending extraction of the
  upstream formatting contract.

## Exact target seams

| File | Future change | Required reuse | Forbidden |
| --- | --- | --- | --- |
| `Pipeline.cs` | Add `IAotRecordBatchStage` plus two descriptor-owned adapters; retain `AotCmdletRegistry.BindCommand` as the sole binder. | descriptors, invocation, registry, common parameters, J1 AST value groups | second binder, text parser, object input, implicit source command |
| `AotRecordBatch.cs` | Add immutable full-batch stage application; existing filter/projection classes remain reused. | batch, transforms, cancellation | mutable rows, CLR object rehydration, terminal-row re-entry |
| `AotLanguageCore.cs`, `UpstreamAstPipelineLowerer.cs` | Lower a post-source command to a typed record-stage plan from upstream AST atoms/extents and the registry binder. | upstream AST, expression plans, spans | second grammar, retokenizing, ScriptBlock lowering, unbounded stages |
| `PipelineValueAdapter.cs` | No new conversion: stages consume/emit `AotRecordBatch`. | sole typed-record crossing | arbitrary wrapper registration or reverse adapter |
| `HelpCatalog.cs` | Register names only in the implementation that makes the stage executable. | registry/catalog equality checks | discovery without executable availability |
| `AotDiagnostics.cs` | Add structured, source-spanned J2 errors/snapshots only. | `AotDiagnostic`, renderer | raw exceptions or console-local errors |

## Metadata and stage contract

The implementation must use `GeneratedCmdletPorts.WhereObject` and
`GeneratedCmdletPorts.SelectObject` as metadata authority and select only the
parameters above. It may not hand-copy aliases, positions, sets, or validation.

`StaticBindingMode` may gain only `J2WhereStaticNumeric` and
`J2SelectStaticFields`. They are descriptor-owned, not a general parameter-set
engine. The first verifies one admitted operator/numeric value; the second
preserves one AST argument group for `Property` and rejects non-literal
expressions. Both use `CommandInvocation` spans and never reconstruct syntax
from text.

The only new interface is closed:

```text
Apply(AotExecutionContext, CommandInvocation, AotRecordBatch) -> AotRecordBatch
```

It is allowed only after a native source crossed `PipelineValueAdapter.ToRecord`.
It cannot emit arbitrary `IPipelineRecord`, invoke another cmdlet, mutate scope,
access a host capability, or change the batch boundary.

## Diagnostics and atomicity

| ID | Failure | Primary span | Result |
| --- | --- | --- | --- |
| `AOT6401` | J2 stage without a preceding typed source | command name | one error, zero output |
| `AOT6402` | missing/blank/unknown `Where-Object` field | property argument | one error, zero output |
| `AOT6403` | missing/conflicting/nonnumeric/nonfinite/noncomparable operator/value | offending atom | one error, zero output |
| `AOT6404` | no `Select-Object` fields, wildcard/calculated syntax, or missing field | offending property | one error, zero output |
| `AOT6405` | case-insensitive duplicate projection field | second field | one error, zero output |
| `AOT6406` | static-profile-excluded source parameter/route | parameter/property argument | one error, zero output |

Binding resolves the entire static contract before execution. Stages validate
the full input batch before a terminal event; the first input-order failure
wins. Cancellation is checked during bind, per row, and before publication.

## Fixture and oracle plan

| Category | Minimum | Proof |
| --- | ---: | --- |
| grammar/baseline | 12 | fields, AST groups, aliases, all six operators, malformed and ScriptBlock/wildcard lower rejection |
| binder/diagnostic | 14 | unsupported routes, missing/conflicting values, spans, plain/ANSI snapshots, no raw exception |
| batch semantics | 12 | lookup/order, missing/duplicate, mixed invalid atomicity, cancellation |
| stock oracle | 8 | controlled `Get-TimeZone`/`Get-Culture` rows, projected fields and table headers, documented host normalization only |
| Native AOT smoke | 6 | fresh artifact filtering, projection, composition, diagnostic, discovery, no-source-stage |

The implementation must mechanically assert **52 unique fixture IDs**. Future
per-cmdlet notes must retain stock/native commands, artifact hashes, source
producer/formatter evidence, and declared output variances.

## J2 disposition: no silent promotion

| Command | Status in this packet | Reason |
| --- | --- | --- |
| `Where-Object` | candidate only | static numeric field route; not migrated |
| `Select-Object` | candidate only | static field projection route; not migrated |
| `Add-Member` | deferred | immutable shape extension/member kind/collision/`-Force`/`-PassThru` contract missing; ETS mutation forbidden |
| `Compare-Object` | deferred | two collections, property expressions, sync window, result shape require separate contract |
| `ForEach-Object` | deferred | ScriptBlock/property-method/parallel routes need a static function language; no runspace/dynamic invoke |
| `Get-Member` | deferred | closed schema-introspection output/non-ETS semantics not designed |
| `Get-Random` | deferred | base/helper closure and deterministic seeded numeric contract not implementation-ready |
| `Get-SecureRandom` | deferred | distinct cryptographic random authority/contract required |
| `Get-Unique` | deferred | PSObject/type-name/current-culture equality has no approved closed policy |
| `Group-Object` | deferred | group/list/hash-table and dynamic property-expression result contract missing |
| `Join-String` | deferred | property expressions, formatting/culture, quote modes, `$OFS` session lookup excluded |
| `Measure-Command` | deferred | ScriptBlock timing cannot authorize dynamic command execution |
| `Measure-Object` | deferred | property expressions, conversion, parameter sets, and result shapes need numeric-statistics design |
| `Select-String` | deferred | physical text read, encoding, regex/context, match record contract missing |
| `Sort-Object` | deferred | ordering/comparer, buffering, `Top`/`Bottom`/`Unique`, and format policy need separate approval |
| `Tee-Object` | deferred | text write/encoding/file/session-variable authority outside zero-authority slice |

## Reuse and no-go boundary

The existing target seams searched are `AotValue.cs`, `AotRecordBatch.cs`,
`PipelineValueAdapter.cs`, `Pipeline.cs`, `AotLanguageCore.cs`,
`UpstreamAstPipelineLowerer.cs`, `AotDiagnostics.cs`, and `HelpCatalog.cs`.
No host/provider/process/filesystem capability is needed.

The slice must not add `PSObject`, ETS, reflection, `dynamic`, `ScriptBlock`,
`Compiler.cs`, runspaces, `PowerShell`, call-site binders, providers, runtime
code generation, session state, culture fallback, or an untyped object/dictionary
pipeline. Deferred options must fail closed.

## Handoff

After both DLAR lenses PASS, implementation proceeds in this order: per-cmdlet
notes/reuse matrices; grammar fixtures; one record-stage seam/static contracts;
two adapters/catalog/help; counted tests and stock oracle; managed/parser/native
verification and ordinary reviews; variance/timing evidence; non-fast-forward
release. Only release changes the migrated count.

The structured [DLAR packet](../reviews/dlar/2026-09-25-j2-target-readiness-v1/README.md)
is ready for review.
