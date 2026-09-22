namespace PwshAotLite;

// The engine emits a closed typed transcript. Construction is factory-only so
// an event cannot accidentally carry an output plus a diagnostic, a blank
// side message, or a zero-row success segment.
internal enum AotRuntimeEventKind { Success, Error, TerminatingError, Verbose, Debug }

internal sealed record AotInvocationFrame(
    string CommandName,
    AotSourceSpan? SourceSpan,
    int PipelinePosition,
    int PipelineLength,
    AotInvocationFrame? Parent = null);

internal sealed class AotRuntimeEvent
{
    private AotRuntimeEvent(
        long sequence,
        AotRuntimeEventKind kind,
        AotInvocationFrame? invocation,
        AotExecutionOutput? output,
        AotDiagnostic? diagnostic,
        string? message)
    {
        if (sequence <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence), "Runtime event sequence numbers are positive.");
        }

        Sequence = sequence;
        Kind = kind;
        Invocation = invocation;
        Output = output;
        Diagnostic = diagnostic;
        Message = message;
    }

    internal long Sequence { get; }
    internal AotRuntimeEventKind Kind { get; }
    internal AotInvocationFrame? Invocation { get; }
    internal AotExecutionOutput? Output { get; }
    internal AotDiagnostic? Diagnostic { get; }
    internal string? Message { get; }

    internal static AotRuntimeEvent Success(long sequence, AotExecutionOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (output.Batch.Records.Count == 0)
        {
            throw new ArgumentException("A zero-row output batch must not become a success runtime event.", nameof(output));
        }

        return new AotRuntimeEvent(sequence, AotRuntimeEventKind.Success, null, output, null, null);
    }

    internal static AotRuntimeEvent Error(long sequence, AotInvocationFrame? invocation, AotDiagnostic diagnostic) =>
        new(sequence, AotRuntimeEventKind.Error, invocation, null, diagnostic ?? throw new ArgumentNullException(nameof(diagnostic)), null);

    // A terminating diagnostic is transcript-visible and projected once by the
    // shared terminal projector. ScriptRunner catches the matching published
    // termination carrier only to select exit code 2; it never renders again.
    internal static AotRuntimeEvent TerminatingError(long sequence, AotInvocationFrame? invocation, AotDiagnostic diagnostic) =>
        new(sequence, AotRuntimeEventKind.TerminatingError, invocation, null, diagnostic ?? throw new ArgumentNullException(nameof(diagnostic)), null);

    internal static AotRuntimeEvent Stream(long sequence, AotRuntimeEventKind kind, AotInvocationFrame? invocation, string message)
    {
        if (kind is not AotRuntimeEventKind.Verbose and not AotRuntimeEventKind.Debug)
        {
            throw new ArgumentOutOfRangeException(nameof(kind), "Only the closed verbose/debug event kinds carry side-stream text.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return new AotRuntimeEvent(sequence, kind, invocation, null, null, AotTerminalTextSanitizer.SanitizeInline(message));
    }
}
