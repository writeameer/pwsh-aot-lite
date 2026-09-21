namespace PwshAotLite;

// The engine emits typed events; the host decides whether to render, retain,
// redirect, or otherwise project them.  This first Runtime Core contract is
// intentionally only success output segments and non-terminating errors.
// It does not claim PowerShell's remaining streams or redirection policies.
internal enum AotRuntimeEventKind { Success, Error }

internal sealed record AotInvocationFrame(
    string CommandName,
    AotSourceSpan? SourceSpan,
    int PipelinePosition,
    int PipelineLength,
    AotInvocationFrame? Parent = null);

internal sealed record AotRuntimeEvent(
    long Sequence,
    AotRuntimeEventKind Kind,
    AotInvocationFrame? Invocation,
    AotExecutionOutput? Output = null,
    AotDiagnostic? Diagnostic = null)
{
    internal static AotRuntimeEvent Success(long sequence, AotExecutionOutput output) =>
        new(sequence, AotRuntimeEventKind.Success, null, Output: output);

    internal static AotRuntimeEvent Error(long sequence, AotInvocationFrame? invocation, AotDiagnostic diagnostic) =>
        new(sequence, AotRuntimeEventKind.Error, invocation, Diagnostic: diagnostic);
}
