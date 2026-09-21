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
    TextWriter standardError)
{
    internal void Project(AotRuntimeEvent runtimeEvent)
    {
        ArgumentNullException.ThrowIfNull(runtimeEvent);
        if (runtimeEvent is { Kind: AotRuntimeEventKind.Success, Output: { } output })
        {
            // An event success is already a completed output segment. Format
            // it as one table, never as speculative per-record host output.
            TableWriter.Write(output.Rows, output.Columns, standardOutput);
            return;
        }

        if (runtimeEvent is { Kind: AotRuntimeEventKind.Error, Diagnostic: { } diagnostic })
        {
            WriteDiagnostic(diagnostic);
        }
    }

    internal void WriteDiagnostic(AotDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        standardError.WriteLine(AotDiagnosticRenderer.Render(diagnostic, source, documentName, renderOptions));
    }
}
