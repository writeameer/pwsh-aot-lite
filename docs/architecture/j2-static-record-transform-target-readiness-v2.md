# J2 target readiness v2: single command route and future transport contract

> **Status:** ready for fresh Dual-Lens Architecture Review (DLAR). This is a
> documentation-only correction. It makes no runtime, converter, registration,
> availability, sidecar, or migration-count change.

**Work record:** started `2026-09-24T23:18:14Z`; ended `2026-09-24T23:24:32Z`; elapsed `00:06:18`.

This v2 packet supersedes [v1](j2-static-record-transform-target-readiness.md)
for the contracts below. It inherits v1's source pins, two candidate commands,
fourteen explicit deferrals, static parameter subsets, no-go boundary, and
fixture plan unless this document changes them.

## Amendment A: one descriptor-bound route per command spelling

The future implementation has exactly one route for any pipeline element named
`Where-Object` or `Select-Object`:

```text
upstream CommandAst + original atoms/extents
  -> AotCmdletRegistry name lookup
  -> generated SourceCmdletMetadata + descriptor-owned static binding mode
  -> IAotRecordBatchStage.Apply(...)
```

`UpstreamAstPipelineLowerer.LowerPipeline` must remove its direct
`AotFilterTailStagePlan` / `AotProjectionTailStagePlan` dispatch for these
names in the same commit that registers the two stage descriptors. It may not
try the legacy parser first, invoke a descriptor second, or fall back after a
bind/lower error. `AOT4001`–`AOT4008` remain historical pre-registration
structural-stage diagnostics; after redirect they have no execution ownership
for either command spelling.

| Input shape | Single owner after implementation | Result |
| --- | --- | --- |
| source at pipeline position 0 | existing executable `IAotCmdlet` registry lookup | unchanged source-command rules |
| either J2 name after a source/record stage | generated descriptor plus J2 binding mode | admitted subset reaches one record stage |
| either J2 name at pipeline position 0 | feature policy before stage binding | `AOT6401`, zero output |
| source-valid but unadmitted form | descriptor-associated lower/bind policy | `AOT6406`, no fallback |
| unknown command name | existing registry policy | existing `AOT2001` route |

The lowerer may inspect an upstream `ScriptBlockExpressionAst` only to create a
descriptor-associated unsupported J2 plan with the original span. It must not
lower/evaluate the block or send it to the legacy transform; that is feature
policy, not a second binder.

### Redirect diagnostic contract

| Concern | Post-redirect owner / diagnostic | Span |
| --- | --- | --- |
| no preceding typed source | `AOT6401` | command name |
| direct `Where-Object CPU -gt 10` or named equivalent | `J2WhereStaticNumeric` descriptor then stage | original property/operator/value atoms |
| invalid/missing/conflicting Where operator/value | `AOT6403` | first offending atom |
| ScriptBlock, case-sensitive/pattern/collection/type operator, direct `InputObject`, property-name bind | `AOT6406` | rejected element/parameter |
| direct `Select-Object Name,Id` or named equivalent | `J2SelectStaticFields` descriptor then stage | original property group/atoms |
| omitted property, wildcard/calculated/exclude/expand/queue/unique route | `AOT6404` or `AOT6406` | first failure element |
| duplicate case-insensitive selected field | `AOT6405` | second field atom |

The registry/help/completion catalog remains unchanged until the one descriptor
route passes release gates. Existing structural stages are not cmdlets; these
two names change campaign accounting only at lifecycle step 9.

### Redirect proof fixtures

The inherited fixture plan gains 12 named IDs: positional/named Where each
redirect once; positional/named Select groups each redirect once; no lowered
plan contains the legacy Where or Select tail class; ScriptBlock and omitted
Property produce one `AOT640*` not `AOT400*`; an excluded parameter is not
ignored; source-position stage is `AOT6401`; pre-release catalog remains
unchanged; and release accounting is the only count-changing fixture.

## Amendment B: contractual, unimplemented batch transport v1

This is a future protocol contract only. It does not add a package, endpoint,
listener, subprocess, gRPC dependency, environment setting, plugin, module
loader, or runtime registration.

### Canonical payload

`AotRecordBatchTransportV1` permits exactly one data payload:

```text
AotValue.List([ AotValue.Record(row0), AotValue.Record(row1), ... ])
```

The root is `List`; every immediate item is `Record`; an empty list is an empty
batch. Record field order/casing and each admitted nested `AotValue` kind are
preserved. No CLR object, `PSObject`, type name, adapter, ScriptBlock, command
name, module identity, capability token, or plugin identity can enter. The
conceptual `Encode(AotRecordBatch)` and `Reconstruct(AotValue)` operations are
unimplemented until a separately approved transport slice.

### Identical endpoint semantics

| Frame | Meaning |
| --- | --- |
| `Data(payload)` | one canonical payload |
| `Complete` | normal end after zero or more data frames |
| `Error(AotDiagnostic)` | existing structured diagnostic, outer boundary only |
| `Cancel` | host cancellation; never a row or ordinary error |

`Local` represents this union in memory; `Stdio` uses a fixed length-framed
outer envelope; `gRPC` uses a fixed versioned RPC envelope. They differ only in
outer framing, never payload semantics, ordering, error ownership, or cancel
outcome. There is no per-row invocation or callback protocol.

Endpoint selection is a compile-time artifact choice only: a generated/static
`AotRecordBatchTransportEndpoint` value of `Local`, `Stdio`, or `Grpc` is baked
into a designated build. Invocation arguments, variables, environment, config
files, URI strings, extensions, module manifests, and discovery cannot alter
it. No selection means no transport implementation.

Framing, diagnostic projection, and cancellation are outer-boundary work. A
cancelled operation publishes no partial batch. A transport error reconstructs
the same `AotDiagnostic` before ordinary terminal rendering; it is never a
dynamic exception or data row.

### Transport proof obligations

The inherited minimum becomes **76 unique fixture IDs**: 52 inherited, 12
redirect, and 12 transport IDs. Transport IDs cover ordered rows, field
order/casing, all closed scalar kinds, nested list/record/bytes, invalid roots,
encode/reconstruct equality, local/stdio/gRPC semantic equality, compile-time
endpoint selection, version rejection, diagnostic equality, zero-partial
cancellation, and absence of dynamic/plugin registration fields.

The required equivalence is:

```text
Reconstruct(Encode(batch)) == batch
```

Equality means identical row count/order, field count/order/casing, and
recursively identical closed `AotValue` kinds/values. This is a fixture contract
now, not executable behavior now.

## Revised preflight closure

| Required closure | v2 evidence |
| --- | --- |
| source pins/facts | v1 packet plus v2 checksums |
| authority | record stage has zero host authority; future transport frames closed batches only |
| static contract | v1 subset plus the redirect/diagnostic tables above |
| grammar/types | upstream AST and sole binder; closed list-of-records payload; no reparsing |
| batches/property | full-batch atomicity, no fallback, ordered payload, outer cancel/error |
| fixtures | mechanically counted 76 IDs |
| deferred remainder | 14 J2 commands and all J7 behavior remain unavailable |

## DLAR handoff

The immutable [v2 DLAR package](../reviews/dlar/2026-09-25-j2-target-readiness-v2/README.md)
needs fresh independent verdicts. A PASS remains documentation-only approval
for the future implementation order.
