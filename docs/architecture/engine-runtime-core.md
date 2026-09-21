# Engine Runtime Core

## Status

Complete for the deliberately narrow Phase-3 contract. Ordered runtime events,
one typed-input stage, cooperative cancellation/lifecycle, and a parser-driven
terminal/REPL projection are integrated. This is not a claim of PowerShell's
full stream model or interactive host.

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

## Cooperative cancellation and lifecycle

`AotExecutionContext` accepts an optional, host-owned `CancellationToken`.
`AotBlockPlan`, closed foreach execution, pipeline materialization, and
`AotCmdletBase` check that token cooperatively. This is a control-flow signal:
it does not create an error event, append to `Errors`, or render a diagnostic.

A context already cancelled before a cmdlet starts invokes no lifecycle hook.
Once a command lifecycle has started, observed cancellation invokes that
command's `StopProcessing` exactly once, skips `EndProcessing`, and rethrows
the cancellation. A stop-hook exception is suppressed so it cannot replace the
original stop result, matching the relevant upstream `PipelineProcessor.Stop`
rule. Completed event segments remain in the transcript; the in-progress,
buffered segment is not emitted.

`ScriptRunner` maps a context cancellation to exit code `130`; its existing
success, user-error, and unexpected-failure results remain `0`, `2`, and `1`.
The runner does not attach a console signal handler in this slice: a later host
projection may supply a command-scoped token source without leaking terminal
policy into the engine.

## Host and REPL projection

`AotTerminalEventProjector` is the only console projection of an
`AotRuntimeEvent`: a completed `Success` segment is formatted to its supplied
stdout writer, and an `Error` (or a terminating diagnostic) is rendered to its
supplied stderr writer. `ScriptRunner` subscribes that projector to the
context; it does not replay `Errors` or format a competing event path. The
projector receives writers and render options explicitly, so an embedding can
project the same transcript elsewhere without changing engine code.

The ordinary `Console.ReadLine` REPL is line-editing-only. `AotReplInputBuffer`
accumulates physical lines and calls the shared `AotScriptParser`. It shows
`>> ` only when upstream `ParseError.IncompleteInput` marks the submitted
source incomplete **and** no non-incomplete parser error is present. A
malformed input therefore renders immediately rather than trapping the user in
continuation mode. When parsing succeeds, the REPL lowers and executes that
same `AotParseResult`; it has no delimiter scanner, alternate lexer, or
execution-only parser. EOF drains a pending buffer through the normal
diagnostic renderer.

This gives the host precise multiline behavior and batching at completed
output-segment boundaries. It does not attempt editable-buffer ANSI syntax
highlighting: `Console.ReadLine` cannot safely repaint a live buffer. A future
raw-key editor may render the already-exposed upstream tokens/extents, under
the language-tooling contract, without adding another lexer.

## Explicit exclusions

This slice does not support or imply `ErrorRecord`/`$Error`, preference
variables such as `ErrorActionPreference`, stream redirection or merging,
warning/verbose/debug/information/progress streams, record-by-record
streaming, remoting, background jobs, concurrency, async/preemptive
interruption, cancellation while parsing, cancellation of native/external
processes, or a `Console.CancelKeyPress` policy. An arbitrary blocking port
must be explicitly migrated to use the token; this contract does not interrupt
it from another thread.

## Planned finite slices

1. **Ordered success/error events** — integrated in the first Runtime Core slice.
2. **Typed stage composition** — integrated: one source command may feed one
   registered input-stage adapter, followed by the existing closed
   `Where-Object`/`Select-Object` transforms. The bridge accepts only a
   declared `IPipelineRecord` subtype; it has no `object`, `PSObject`, or
   property-name binding fallback. `Get-Process → Get-Process` is the proof
   adapter. Direct `-InputObject` remains rejected because text cannot honestly
   represent a static process record.
3. **Cancellation and lifecycle** — integrated: cooperative token propagation,
   exactly-once `StopProcessing`, and host exit code `130`.
4. **Host/REPL projection** — integrated: multiline input is driven by the
   pinned parser's incomplete-input result; one writer-injected terminal
   projector batches completed event segments. Syntax tooling remains a
   projection of the upstream parser, never a second lexer.

The independent evidence for slice one is in the
[runtime stream-contract review](../reviews/2026-09-22-runtime-stream-contract.md).
The typed-stage composition evidence is in the
[typed-stage review](../reviews/2026-09-22-typed-stage-composition.md).
The cancellation evidence is in the
[cancellation/lifecycle review ledger](../reviews/2026-09-22-cancellation-lifecycle.md).
The final host/REPL evidence is in the
[host/REPL projection review ledger](../reviews/2026-09-22-host-repl-projection.md).
