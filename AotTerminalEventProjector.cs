namespace PwshAotLite;

// The terminal is one projection of the runtime transcript. Keeping this
// adapter outside the execution context makes table/diagnostic presentation
// testable and leaves embeddings free to retain, redirect, or serialize the
// same events instead.
internal sealed class AotTerminalEventProjector(
    string source,
    string documentName,
    AotDiagnosticRenderOptions renderOptions,
    TextWriter standardOutput,
    TextWriter standardError,
    AotExecutionContext projectionContext,
    Action? afterBufferedRowRendered = null)
{
    internal void Project(AotRuntimeEvent runtimeEvent)
    {
        ArgumentNullException.ThrowIfNull(runtimeEvent);
        if (runtimeEvent is { Kind: AotRuntimeEventKind.Success, Output: { } output })
        {
            projectionContext.ThrowIfCancellationRequested();
            // An event success is already a completed output segment. Render
            // the entire segment into a private buffer before writing stdout:
            // cancellation during a multi-row projection must not leave a
            // partial terminal table or prose block behind.
            output.Batch.ValidateForProjection(projectionContext, output.Shape, output.ShapeSpan);
            IReadOnlyList<IPipelineRecord> rows = output.Batch.ToTerminalRows(projectionContext);
            StringWriter rendered = new();
            if (output.Presentation is AotTerminalPresentation.Prose)
            {
                foreach (IPipelineRecord row in rows)
                {
                    projectionContext.ThrowIfCancellationRequested();
                    rendered.WriteLine(row.TextFor("Value"));
                    afterBufferedRowRendered?.Invoke();
                }
            }

            else
            {
                TableWriter.Write(rows, output.Columns, rendered, projectionContext, afterBufferedRowRendered, output.TableLayout);
            }

            projectionContext.ThrowIfCancellationRequested();
            standardOutput.Write(rendered.ToString());
            return;
        }

        if (runtimeEvent is { Kind: AotRuntimeEventKind.Error, Diagnostic: { } diagnostic })
        {
            WriteDiagnostic(diagnostic);
            return;
        }

        if (runtimeEvent is { Kind: AotRuntimeEventKind.TerminatingError, Diagnostic: { } terminatingDiagnostic })
        {
            // The terminating event is the one host-rendered path. The
            // dedicated carrier caught by ScriptRunner merely selects exit 2.
            WriteDiagnostic(terminatingDiagnostic);
            return;
        }

        if (runtimeEvent is { Kind: AotRuntimeEventKind.Verbose or AotRuntimeEventKind.Debug, Message: { } message })
        {
            // This static terminal projection intentionally keeps the two
            // admitted side streams separate from success-table output. A
            // different embedding may retain/route the same typed event.
            projectionContext.ThrowIfCancellationRequested();
            string rendered = runtimeEvent.Kind == AotRuntimeEventKind.Verbose
                ? $"VERBOSE: {message}"
                : $"DEBUG: {message}";
            projectionContext.ThrowIfCancellationRequested();
            standardError.WriteLine(rendered);
        }
    }

    internal void WriteDiagnostic(AotDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        standardError.WriteLine(AotDiagnosticRenderer.Render(diagnostic, source, documentName, renderOptions));
    }
}
