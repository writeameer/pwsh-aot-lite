# Engine Runtime Core

## Status

In progress. The first integrated slice is an ordered, typed runtime event
bridge. It is deliberately narrower than PowerShell's full stream model.

## Runtime event contract

```text
cmdlet lifecycle / pipeline plan
          │
          ▼
 AotExecutionContext
   ├─ Success(AotExecutionOutput)  ──► host stdout/table projection
   └─ Error(AotDiagnostic)         ──► host stderr/diagnostic projection
```

`AotRuntimeEvent` has a monotonically increasing sequence number. Both a host
subscriber and programmatic callers observe the same transcript. A
non-terminating runtime error remains available in `AotExecutionContext.Errors`
for compatibility, but it is also published immediately as an `Error` event;
the host must never replay that list after execution.

Each cmdlet invocation has an immutable `AotInvocationFrame` carrying command
name, source extent, parent frame, and a reserved pipeline position/length.
The context attaches that source extent only when an origin diagnostic has no
more precise span. Error events carry their emitting invocation. Completed
output segments are intentionally unattributed in this slice because their
records may span more than one lifecycle callback; later generic stage
composition may attach richer provenance without inventing it today.

The present success event denotes a **completed pipeline output segment**, not
an individual object. Existing ports still materialize a segment before the
table host formats it. Therefore a source command that raises a non-terminating
error while forming its rows can emit `Error` before the segment's `Success`.
That ordering is intentional, testable, and honest.

## Explicit exclusions

This slice does not support or imply `ErrorRecord`/`$Error`, preference
variables such as `ErrorActionPreference`, stream redirection or merging,
warning/verbose/debug/information/progress streams, record-by-record
streaming, remoting, background jobs, concurrency, or cancellation.

## Planned finite slices

1. **Ordered success/error events** — integrated in the first Runtime Core slice.
2. **Typed stage composition** — integrated: one source command may feed one
   registered input-stage adapter, followed by the existing closed
   `Where-Object`/`Select-Object` transforms. The bridge accepts only a
   declared `IPipelineRecord` subtype; it has no `object`, `PSObject`, or
   property-name binding fallback. `Get-Process → Get-Process` is the proof
   adapter. Direct `-InputObject` remains rejected because text cannot honestly
   represent a static process record.
3. **Cancellation and lifecycle** — propagate cancellation, give
   `StopProcessing` exactly-once semantics, and return a stable host result.
4. **Host/REPL projection** — build multiline input and presentation batching
   over these events; syntax tooling remains a projection of the upstream
   parser, never a second lexer.

The independent evidence for slice one is in the
[runtime stream-contract review](../reviews/2026-09-22-runtime-stream-contract.md).
The typed-stage composition evidence is in the
[typed-stage review](../reviews/2026-09-22-typed-stage-composition.md).
