using System.Management.Automation.Language;

namespace PwshAotLite;

// The sole production source-parsing facade. It is intentionally parse-only:
// callers receive the upstream AST, tokens, extents, and diagnostics but no
// command binding, module lookup, or source execution occurs here.
internal sealed record AotParseResult(
    string Source,
    string? DocumentName,
    long DocumentVersion,
    ScriptBlockAst Ast,
    IReadOnlyList<Token> Tokens,
    IReadOnlyList<AotDiagnostic> Diagnostics,
    bool HasIncompleteInput,
    bool HasBlockingDiagnostics)
{
    // A host may wait for another physical line only when upstream's parser
    // identified every diagnostic as incomplete. A malformed program must be
    // rendered now rather than trapping the user in a continuation prompt.
    internal bool RequiresMoreInput => HasIncompleteInput && !HasBlockingDiagnostics;
}

internal static class AotScriptParser
{
    internal static AotParseResult Parse(string source, string? documentName = null, long documentVersion = 0)
    {
        ArgumentNullException.ThrowIfNull(source);
        ScriptBlockAst ast = documentName is null
            ? Parser.ParseInput(source, out Token[] tokens, out ParseError[] errors)
            : Parser.ParseInput(source, documentName, out tokens, out errors);
        AotDiagnostic[] diagnostics = errors.Select(error => ToDiagnostic(error, documentName)).ToArray();
        return new AotParseResult(
            source,
            documentName,
            documentVersion,
            ast,
            tokens,
            diagnostics,
            errors.Any(static error => error.IncompleteInput),
            errors.Any(static error => !error.IncompleteInput));
    }

    internal static AotSourceSpan ToSpan(IScriptExtent extent, string? fallbackDocumentName = null) =>
        new(
            extent.File ?? fallbackDocumentName,
            extent.StartOffset,
            extent.EndOffset,
            extent.StartLineNumber,
            extent.StartColumnNumber,
            extent.EndLineNumber,
            extent.EndColumnNumber);

    private static AotDiagnostic ToDiagnostic(ParseError error, string? documentName)
    {
        bool emptyPipe = error.ErrorId.Equals("EmptyPipeElement", StringComparison.Ordinal);
        return new AotDiagnostic(
            error.ErrorId,
            AotDiagnosticSeverity.Error,
            AotDiagnosticCategory.Parse,
            emptyPipe ? "A pipeline cannot end with '|'." : error.Message,
            ToSpan(error.Extent, documentName),
            error.IncompleteInput ? "incomplete input" : "parse error",
            emptyPipe ? "Add a command after '|', or remove the trailing pipe." : null,
            emptyPipe ? error.Message : null);
    }
}
