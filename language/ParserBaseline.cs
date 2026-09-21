using System.Text.Json.Serialization;

namespace PwshAotLite.LanguageHost;

// Shared schema with tools/Export-PwshParserBaseline.ps1. Source generation
// keeps baseline JSON handling reflection-free under Native AOT.
internal sealed class StockParserBaseline
{
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; init; }
    [JsonPropertyName("fixture")] public string Fixture { get; init; } = string.Empty;
    [JsonPropertyName("sourceSha256")] public string SourceSha256 { get; init; } = string.Empty;
    [JsonPropertyName("tokens")] public List<TokenSnapshot> Tokens { get; init; } = [];
    [JsonPropertyName("ast")] public AstSnapshot Ast { get; init; } = new();
    [JsonPropertyName("diagnostics")] public List<DiagnosticSnapshot> Diagnostics { get; init; } = [];
}

internal sealed class TokenSnapshot
{
    [JsonPropertyName("kind")] public string Kind { get; init; } = string.Empty;
    [JsonPropertyName("text")] public string Text { get; init; } = string.Empty;
    [JsonPropertyName("flags")] public string Flags { get; init; } = string.Empty;
    [JsonPropertyName("extent")] public ExtentSnapshot Extent { get; init; } = new();
}

internal sealed class AstSnapshot
{
    [JsonPropertyName("type")] public string Type { get; init; } = string.Empty;
    [JsonPropertyName("extent")] public ExtentSnapshot Extent { get; init; } = new();
    [JsonPropertyName("children")] public List<AstSnapshot> Children { get; init; } = [];
}

internal sealed class DiagnosticSnapshot
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    [JsonPropertyName("extent")] public ExtentSnapshot Extent { get; init; } = new();
}

internal sealed class ExtentSnapshot
{
    [JsonPropertyName("startOffset")] public int StartOffset { get; init; }
    [JsonPropertyName("endOffset")] public int EndOffset { get; init; }
    [JsonPropertyName("startLine")] public int StartLine { get; init; }
    [JsonPropertyName("startColumn")] public int StartColumn { get; init; }
    [JsonPropertyName("endLine")] public int EndLine { get; init; }
    [JsonPropertyName("endColumn")] public int EndColumn { get; init; }
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = false)]
[JsonSerializable(typeof(StockParserBaseline))]
internal sealed partial class ParserBaselineJsonContext : JsonSerializerContext;

internal static class ParserBaselineComparer
{
    internal static bool TryCompare(StockParserBaseline expected, StockParserBaseline actual, out string difference)
    {
        if (expected.SchemaVersion != actual.SchemaVersion)
        {
            difference = $"schemaVersion expected {expected.SchemaVersion}, actual {actual.SchemaVersion}";
            return false;
        }

        if (!string.Equals(expected.Fixture, actual.Fixture, StringComparison.Ordinal))
        {
            difference = $"fixture expected '{expected.Fixture}', actual '{actual.Fixture}'";
            return false;
        }

        if (!string.Equals(expected.SourceSha256, actual.SourceSha256, StringComparison.Ordinal))
        {
            difference = "sourceSha256 differs; regenerate the stock-pwsh baseline deliberately after changing the fixture";
            return false;
        }

        if (!TryCompareSequence(expected.Tokens, actual.Tokens, TryCompareToken, "tokens", out difference)
            || !TryCompareAst(expected.Ast, actual.Ast, "ast", out difference)
            || !TryCompareSequence(expected.Diagnostics, actual.Diagnostics, TryCompareDiagnostic, "diagnostics", out difference))
        {
            return false;
        }

        difference = string.Empty;
        return true;
    }

    private static bool TryCompareToken(TokenSnapshot expected, TokenSnapshot actual, string path, out string difference)
    {
        if (!string.Equals(expected.Kind, actual.Kind, StringComparison.Ordinal)
            || !string.Equals(expected.Text, actual.Text, StringComparison.Ordinal)
            || !string.Equals(expected.Flags, actual.Flags, StringComparison.Ordinal))
        {
            difference = $"{path} expected {expected.Kind} '{expected.Text}' ({expected.Flags}), actual {actual.Kind} '{actual.Text}' ({actual.Flags})";
            return false;
        }

        return TryCompareExtent(expected.Extent, actual.Extent, $"{path}.extent", out difference);
    }

    private static bool TryCompareDiagnostic(DiagnosticSnapshot expected, DiagnosticSnapshot actual, string path, out string difference)
    {
        if (!string.Equals(expected.Id, actual.Id, StringComparison.Ordinal))
        {
            difference = $"{path}.id expected '{expected.Id}', actual '{actual.Id}'";
            return false;
        }

        return TryCompareExtent(expected.Extent, actual.Extent, $"{path}.extent", out difference);
    }

    private static bool TryCompareAst(AstSnapshot expected, AstSnapshot actual, string path, out string difference)
    {
        if (!string.Equals(expected.Type, actual.Type, StringComparison.Ordinal))
        {
            difference = $"{path}.type expected '{expected.Type}', actual '{actual.Type}'";
            return false;
        }

        if (!TryCompareExtent(expected.Extent, actual.Extent, $"{path}.extent", out difference))
        {
            return false;
        }

        return TryCompareSequence(expected.Children, actual.Children, TryCompareAst, $"{path}.children", out difference);
    }

    private static bool TryCompareExtent(ExtentSnapshot expected, ExtentSnapshot actual, string path, out string difference)
    {
        if (expected.StartOffset != actual.StartOffset || expected.EndOffset != actual.EndOffset
            || expected.StartLine != actual.StartLine || expected.StartColumn != actual.StartColumn
            || expected.EndLine != actual.EndLine || expected.EndColumn != actual.EndColumn)
        {
            difference = $"{path} differs";
            return false;
        }

        difference = string.Empty;
        return true;
    }

    private delegate bool CompareItem<T>(T expected, T actual, string path, out string difference);

    private static bool TryCompareSequence<T>(IReadOnlyList<T> expected, IReadOnlyList<T> actual, CompareItem<T> compareItem, string path, out string difference)
    {
        if (expected.Count != actual.Count)
        {
            difference = $"{path}.count expected {expected.Count}, actual {actual.Count}";
            return false;
        }

        for (var index = 0; index < expected.Count; index++)
        {
            if (!compareItem(expected[index], actual[index], $"{path}[{index}]", out difference))
            {
                return false;
            }
        }

        difference = string.Empty;
        return true;
    }
}

internal sealed class TestPaths
{
    private const string FixtureEnvironmentVariable = "PWSH_AOT_LANGUAGE_FIXTURE_ROOT";
    private const string BaselineEnvironmentVariable = "PWSH_AOT_LANGUAGE_BASELINE_ROOT";

    internal required string FixtureRoot { get; init; }
    internal required string BaselineRoot { get; init; }

    internal static TestPaths Parse(string[] args)
    {
        string? fixtureRoot = Environment.GetEnvironmentVariable(FixtureEnvironmentVariable);
        string? baselineRoot = Environment.GetEnvironmentVariable(BaselineEnvironmentVariable);
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--fixture-root" when index + 1 < args.Length:
                    fixtureRoot = args[++index];
                    break;
                case "--baseline-root" when index + 1 < args.Length:
                    baselineRoot = args[++index];
                    break;
                default:
                    throw new InvalidOperationException($"Unknown or incomplete argument '{args[index]}'. Expected --fixture-root PATH or --baseline-root PATH.");
            }
        }

        var root = FindRepositoryRoot();
        fixtureRoot ??= Path.Combine(root, "tests", "grammar", "fixtures");
        baselineRoot ??= Path.Combine(root, "tests", "grammar", "baselines");
        if (!Directory.Exists(fixtureRoot) || !Directory.Exists(baselineRoot))
        {
            throw new InvalidOperationException($"Grammar fixtures/baselines were not found. Set {FixtureEnvironmentVariable} and {BaselineEnvironmentVariable}, or pass their command-line equivalents.");
        }

        return new TestPaths { FixtureRoot = Path.GetFullPath(fixtureRoot), BaselineRoot = Path.GetFullPath(baselineRoot) };
    }

    private static string FindRepositoryRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, "tests", "grammar", "fixtures"))
                    && Directory.Exists(Path.Combine(directory.FullName, "language")))
                {
                    return directory.FullName;
                }
            }
        }

        return Directory.GetCurrentDirectory();
    }
}
