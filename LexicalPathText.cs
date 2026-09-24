namespace PwshAotLite;

// J1 is deliberately a value-plane operation.  This type has no filesystem,
// provider, current-directory, or platform API dependency.  The selected
// dialect is supplied as immutable data; the executable currently registers
// the POSIX-v1 profile only.
internal enum LexicalPathDialect { PosixV1, WindowsV1 }

internal readonly record struct PathText(string Value, LexicalPathDialect Dialect)
{
    internal static PathText Parse(string? value, LexicalPathDialect dialect, AotSourceSpan? span, bool child = false)
    {
        if (string.IsNullOrEmpty(value))
        {
            throw Fail("AOT6211", "A lexical path value cannot be empty.", span);
        }

        if (value.IndexOf('\0') >= 0)
        {
            throw Fail("AOT6214", "A lexical path value cannot contain NUL.", span);
        }

        char separator = dialect == LexicalPathDialect.PosixV1 ? '/' : '\\';
        char foreignSeparator = dialect == LexicalPathDialect.PosixV1 ? '\\' : '/';
        if (value.IndexOf(foreignSeparator) >= 0 || value.IndexOfAny(['*', '?', '[', ']']) >= 0 || value.Contains(':', StringComparison.Ordinal))
        {
            throw Fail("AOT6210", "The path is not admitted by the fixed lexical path dialect.", span);
        }

        if (child && value[0] == separator)
        {
            throw Fail("AOT6210", "A child path fragment cannot be rooted.", span);
        }

        // The v1 grammar has one root spelling and no empty interior segment.
        // A final separator is retained as data, but doubled separators are not.
        string[] segments = value.Split(separator);
        bool emptyInterior = child && segments.Take(segments.Length - (value.EndsWith(separator) ? 1 : 0)).Any(static part => part.Length == 0);
        if (value.Contains(new string(separator, 2), StringComparison.Ordinal) || emptyInterior)
        {
            throw Fail("AOT6210", "The path contains an empty interior segment.", span);
        }

        return new PathText(value, dialect);
    }

    internal PathText Compose(IReadOnlyList<PathText> children, AotSourceSpan? span)
    {
        string result = Value;
        char separator = Dialect == LexicalPathDialect.PosixV1 ? '/' : '\\';
        foreach (PathText child in children)
        {
            if (child.Dialect != Dialect)
            {
                throw Fail("AOT6214", "Lexical path dialects cannot be mixed.", span);
            }

            result = result.EndsWith(separator) ? result + child.Value : result + separator + child.Value;
        }

        return new PathText(result, Dialect);
    }

    internal bool IsAbsolute => Value.Length > 0 && Value[0] == (Dialect == LexicalPathDialect.PosixV1 ? '/' : '\\');

    internal string Parent(AotSourceSpan? span)
    {
        if (Value == "/" || Value == "\\")
        {
            throw Fail("AOT6214", "The root path has no parent in the lexical profile.", span);
        }

        char separator = Dialect == LexicalPathDialect.PosixV1 ? '/' : '\\';
        string trimmed = Value.TrimEnd(separator);
        int index = trimmed.LastIndexOf(separator);
        return index < 0 ? string.Empty : index == 0 ? separator.ToString() : trimmed[..index];
    }

    internal string Leaf()
    {
        char separator = Dialect == LexicalPathDialect.PosixV1 ? '/' : '\\';
        string trimmed = Value.TrimEnd(separator);
        int index = trimmed.LastIndexOf(separator);
        return index < 0 ? trimmed : trimmed[(index + 1)..];
    }

    internal string LeafBase()
    {
        string leaf = Leaf();
        int dot = leaf.LastIndexOf('.');
        return dot <= 0 ? leaf : leaf[..dot];
    }

    internal string Extension()
    {
        string leaf = Leaf();
        int dot = leaf.LastIndexOf('.');
        return dot <= 0 ? string.Empty : leaf[dot..];
    }

    private static ScriptException Fail(string id, string message, AotSourceSpan? span) =>
        new(AotDiagnostics.Runtime(id, message, span, "lexical path rejected"));
}
