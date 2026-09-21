using System.Text;

namespace PwshAotLite;

internal enum AotDiagnosticSeverity { Error, Warning, Information }

internal enum AotDiagnosticCategory { Parse, UnsupportedExecution, Binding, Runtime, Internal }

// The host-owned, immutable source location. It deliberately copies the
// parser extent into simple data so renderers and future JSON/LSP projections
// do not depend on parser object lifetime or parse implementation details.
internal sealed record AotSourceSpan(
    string? DocumentName,
    int StartOffset,
    int EndOffset,
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn);

internal sealed record AotDiagnostic(
    string Id,
    AotDiagnosticSeverity Severity,
    AotDiagnosticCategory Category,
    string PrimaryMessage,
    AotSourceSpan? Span = null,
    string? Label = null,
    string? Help = null,
    string? Detail = null);

internal static class AotDiagnostics
{
    internal static AotDiagnostic Unsupported(string detail, AotSourceSpan? span, string? help = null) =>
        new(
            "AOT1001",
            AotDiagnosticSeverity.Error,
            AotDiagnosticCategory.UnsupportedExecution,
            detail + " is parsed but not executable by the Native AOT structural subset.",
            span,
            "unsupported execution feature",
            help);

    internal static AotDiagnostic Binding(string message, AotSourceSpan? span = null) =>
        Binding("AOT2001", message, span);

    internal static AotDiagnostic Binding(string id, string message, AotSourceSpan? span = null) =>
        new(id, AotDiagnosticSeverity.Error, AotDiagnosticCategory.Binding, message, span, "binding failed");

    internal static AotDiagnostic HostUsage(string message) =>
        new("AOT2000", AotDiagnosticSeverity.Error, AotDiagnosticCategory.Binding, message);

    internal static AotDiagnostic Runtime(string message, AotSourceSpan? span = null) =>
        Runtime("AOT3000", message, span);

    internal static AotDiagnostic Runtime(
        string id,
        string message,
        AotSourceSpan? span = null,
        string? label = null,
        string? help = null) =>
        new(id, AotDiagnosticSeverity.Error, AotDiagnosticCategory.Runtime, message, span, label, help);

    // Scope/evaluation diagnostics are runtime diagnostics because a parsed
    // variable reference is only meaningful against the per-execution scope.
    // Keeping them here prevents scope code from inventing console strings.
    internal static AotDiagnostic Scope(
        string id,
        string message,
        AotSourceSpan? span,
        string? label = null,
        string? help = null) =>
        new(id, AotDiagnosticSeverity.Error, AotDiagnosticCategory.Runtime, message, span, label, help);

    internal static AotDiagnostic Internal(string message, string? detail = null) =>
        new("AOT9000", AotDiagnosticSeverity.Error, AotDiagnosticCategory.Internal, message, null, null, null, detail);

    // Existing ports predate the diagnostic contract and retain stable textual
    // IDs such as InstallHashMismatch. Preserve those IDs while all call sites
    // migrate to typed constructors; unprefixed legacy messages are runtime
    // diagnostics instead of public exception text.
    internal static AotDiagnostic FromLegacyMessage(string message)
    {
        int separator = message.IndexOf(": ", StringComparison.Ordinal);
        if (separator > 0 && message[..separator].All(static character => char.IsLetterOrDigit(character) || character is '-' or '_'))
        {
            return new(
                message[..separator],
                AotDiagnosticSeverity.Error,
                AotDiagnosticCategory.Runtime,
                message[(separator + 2)..]);
        }

        return Runtime(message);
    }
}

internal class AotDiagnosticException : Exception
{
    internal AotDiagnosticException(AotDiagnostic diagnostic, string? exceptionMessage = null)
        : base(exceptionMessage ?? diagnostic.PrimaryMessage)
    {
        Diagnostic = diagnostic;
    }

    internal AotDiagnostic Diagnostic { get; }
}

internal sealed record AotDiagnosticRenderOptions(bool UseAnsi = false, int? Width = null)
{
    internal int? ValidatedWidth => Width is >= 20 ? Width : null;
}

internal static class AotDiagnosticRenderer
{
    internal static string Render(AotDiagnostic diagnostic, string? source, string? fallbackDocumentName, bool useAnsi) =>
        Render(diagnostic, source, fallbackDocumentName, new AotDiagnosticRenderOptions(useAnsi));

    internal static string Render(
        AotDiagnostic diagnostic,
        string? source,
        string? fallbackDocumentName = null,
        AotDiagnosticRenderOptions? options = null)
    {
        AotDiagnosticRenderOptions renderOptions = options ?? new AotDiagnosticRenderOptions();
        string severity = diagnostic.Severity.ToString().ToLowerInvariant();
        string heading = $"{severity}[{diagnostic.Id}]: {diagnostic.PrimaryMessage}";
        StringBuilder output = new(renderOptions.UseAnsi ? "\u001b[31m" + heading + "\u001b[0m" : heading);

        if (diagnostic.Span is { } span && !string.IsNullOrEmpty(source))
        {
            string documentName = span.DocumentName ?? fallbackDocumentName ?? "<input>";
            string[] lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
            int lineIndex = span.StartLine - 1;
            if (lineIndex >= 0 && lineIndex < lines.Length)
            {
                string line = lines[lineIndex].Replace("\t", "    ", StringComparison.Ordinal);
                int caretStart = Math.Clamp(span.StartColumn - 1, 0, line.Length);
                int caretLength = span.StartLine == span.EndLine
                    ? Math.Max(1, span.EndColumn - span.StartColumn)
                    : Math.Max(1, line.Length - caretStart);
                caretLength = Math.Min(caretLength, Math.Max(1, line.Length - caretStart));
                string label = diagnostic.Label ?? diagnostic.Category.ToString().ToLowerInvariant();

                if (renderOptions.ValidatedWidth is int width && line.Length > width)
                {
                    int windowStart = Math.Clamp(caretStart - (width / 4), 0, Math.Max(0, line.Length - width));
                    int windowLength = Math.Min(width, line.Length - windowStart);
                    string prefix = windowStart == 0 ? string.Empty : "… ";
                    string suffix = windowStart + windowLength < line.Length ? " …" : string.Empty;
                    line = prefix + line.Substring(windowStart, windowLength) + suffix;
                    caretStart = prefix.Length + Math.Clamp(caretStart - windowStart, 0, windowLength);
                    caretLength = Math.Min(caretLength, Math.Max(1, line.Length - suffix.Length - caretStart));
                }

                output.AppendLine();
                output.Append("  --> ").Append(documentName).Append(':').Append(span.StartLine).Append(':').Append(span.StartColumn);
                output.AppendLine();
                output.AppendLine("   |");
                output.Append(span.StartLine.ToString()).Append(" | ").Append(line);
                output.AppendLine();
                output.Append("   | ").Append(' ', caretStart).Append('^', caretLength).Append(' ').Append(label);
            }
        }

        if (!string.IsNullOrWhiteSpace(diagnostic.Help))
        {
            output.AppendLine();
            output.Append("   = help: ").Append(diagnostic.Help);
        }

        return output.ToString();
    }
}
