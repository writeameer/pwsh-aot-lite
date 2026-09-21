using System.Security.Cryptography;
using System.Text.Json;
using System.Management.Automation.Language;

namespace PwshAotLite.LanguageHost;

/// <summary>
/// Structural proof for the upstream-derived parser extraction. This host
/// parses checked-in fixture source and compares its own structural output to
/// a baseline produced by stock pwsh. It never lowers or executes an AST.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            var paths = TestPaths.Parse(args);
            AssertFixtureBaselines(paths);

            // This is intentionally a separate AOT policy suite. DSC is
            // explicitly excluded and is not a compatible syntax claim.
            AssertDynamicKeywordRegistryExcluded();
            AssertConfigurationExcluded("configuration Demo { Node localhost { } }");
            AssertConfigurationExclusionRecovery("configuration Demo { Node localhost { } }\nGet-Process");

            Console.WriteLine("PwshAotLite.Language structural parser smoke tests passed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Parser smoke test failed: {exception.Message}");
            return 1;
        }
    }

    private static void AssertFixtureBaselines(TestPaths paths)
    {
        var fixturePaths = Directory.EnumerateFiles(paths.FixtureRoot, "*.ps1", SearchOption.TopDirectoryOnly)
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .ToArray();
        if (fixturePaths.Length == 0)
        {
            throw new InvalidOperationException($"No parser fixtures found in '{paths.FixtureRoot}'.");
        }

        foreach (var fixturePath in fixturePaths)
        {
            var fixtureName = Path.GetFileName(fixturePath);
            var baselinePath = Path.Combine(paths.BaselineRoot, Path.ChangeExtension(fixtureName, ".json"));
            if (!File.Exists(baselinePath))
            {
                throw new InvalidOperationException($"Missing stock-pwsh parser baseline '{baselinePath}'.");
            }

            var expected = JsonSerializer.Deserialize(File.ReadAllBytes(baselinePath), ParserBaselineJsonContext.Default.StockParserBaseline)
                ?? throw new InvalidOperationException($"Could not deserialize '{baselinePath}'.");
            var actual = ParseFixture(fixturePath);
            if (!ParserBaselineComparer.TryCompare(expected, actual, out var difference))
            {
                throw new InvalidOperationException($"{fixtureName}: parser differential mismatch: {difference}");
            }

            Console.WriteLine($"PASS differential {fixtureName}: {actual.Tokens.Count} tokens, {actual.Diagnostics.Count} diagnostics");
        }
    }

    private static StockParserBaseline ParseFixture(string fixturePath)
    {
        var sourceBytes = File.ReadAllBytes(fixturePath);
        var ast = Parser.ParseInput(File.ReadAllText(fixturePath), fixturePath, out Token[] tokens, out ParseError[] errors)
            ?? throw new InvalidOperationException($"{fixturePath}: parser returned no AST.");
        var allNodes = ast.FindAll(static _ => true, searchNestedScriptBlocks: true).ToArray();

        return new StockParserBaseline
        {
            SchemaVersion = 1,
            Fixture = Path.GetFileName(fixturePath),
            SourceSha256 = Convert.ToHexString(SHA256.HashData(sourceBytes)).ToLowerInvariant(),
            Tokens = tokens.Select(ToTokenSnapshot).ToList(),
            Ast = ToAstSnapshot(ast, allNodes),
            Diagnostics = errors.Select(ToDiagnosticSnapshot).ToList(),
        };
    }

    private static TokenSnapshot ToTokenSnapshot(Token token) => new()
    {
        Kind = token.Kind.ToString(),
        Text = token.Text ?? string.Empty,
        Flags = token.TokenFlags.ToString(),
        Extent = ToExtentSnapshot(token.Extent),
    };

    private static DiagnosticSnapshot ToDiagnosticSnapshot(ParseError error) => new()
    {
        Id = error.ErrorId,
        Extent = ToExtentSnapshot(error.Extent),
    };

    private static AstSnapshot ToAstSnapshot(Ast node, IReadOnlyList<Ast> allNodes)
    {
        var children = new List<AstSnapshot>();
        foreach (var candidate in allNodes)
        {
            if (ReferenceEquals(candidate.Parent, node))
            {
                children.Add(ToAstSnapshot(candidate, allNodes));
            }
        }

        return new AstSnapshot
        {
            Type = node.GetType().Name,
            Extent = ToExtentSnapshot(node.Extent),
            Children = children,
        };
    }

    private static ExtentSnapshot ToExtentSnapshot(IScriptExtent extent) => new()
    {
        StartOffset = extent.StartOffset,
        EndOffset = extent.EndOffset,
        StartLine = extent.StartLineNumber,
        StartColumn = extent.StartColumnNumber,
        EndLine = extent.EndLineNumber,
        EndColumn = extent.EndColumnNumber,
    };

    private static void AssertConfigurationExcluded(string input)
    {
        var ast = Parser.ParseInput(input, out _, out ParseError[] errors);
        var foundExclusion = errors.Any(static error => string.Equals(error.ErrorId, "AotConfigurationExcluded", StringComparison.Ordinal));
        if (!foundExclusion || ast?.EndBlock?.Statements.Count != 1 || ast.EndBlock.Statements[0] is not ErrorStatementAst)
        {
            throw new InvalidOperationException("configuration-exclusion: expected an explicit excluded-feature error AST.");
        }

        Console.WriteLine("PASS AOT policy configuration-exclusion: AotConfigurationExcluded");
    }

    private static void AssertDynamicKeywordRegistryExcluded()
    {
        AssertDynamicKeywordRegistryApiExcluded(DynamicKeyword.Reset);
        AssertDynamicKeywordRegistryApiExcluded(DynamicKeyword.Push);
        AssertDynamicKeywordRegistryApiExcluded(DynamicKeyword.Pop);
        AssertDynamicKeywordRegistryApiExcluded(() => DynamicKeyword.RemoveKeyword("AotDelegateProbe"));

        var preParseCalls = 0;
        var keyword = new DynamicKeyword
        {
            Keyword = "AotDelegateProbe",
            PreParse = _ =>
            {
                preParseCalls++;
                return Array.Empty<ParseError>();
            },
        };

        try
        {
            DynamicKeyword.AddKeyword(keyword);
            throw new InvalidOperationException("dynamic-keyword-exclusion: registry unexpectedly accepted a parser-time delegate.");
        }
        catch (NotSupportedException exception)
            when (exception.Message.Contains("AotDynamicKeywordRegistryExcluded", StringComparison.Ordinal))
        {
            // The stable, explicit feature-policy diagnostic is the required
            // behavior. The delegate was never installed.
        }

        var ast = Parser.ParseInput("AotDelegateProbe value", out _, out ParseError[] errors);
        if (preParseCalls != 0
            || DynamicKeyword.ContainsKeyword("AotDelegateProbe")
            || DynamicKeyword.GetKeyword("AotDelegateProbe") is not null
            || DynamicKeyword.GetKeyword().Count != 0
            || ast?.EndBlock?.Statements.Count != 1
            || ast.EndBlock.Statements[0] is not PipelineAst
            || errors.Length != 0)
        {
            throw new InvalidOperationException("dynamic-keyword-exclusion: external registration reached parser-time behavior.");
        }

        Console.WriteLine("PASS AOT policy dynamic-keyword-exclusion: AotDynamicKeywordRegistryExcluded; delegate not invoked");
    }

    private static void AssertDynamicKeywordRegistryApiExcluded(Action operation)
    {
        try
        {
            operation();
            throw new InvalidOperationException("dynamic-keyword-exclusion: registry mutation/lifetime API unexpectedly succeeded.");
        }
        catch (NotSupportedException exception)
            when (exception.Message.Contains("AotDynamicKeywordRegistryExcluded", StringComparison.Ordinal))
        {
        }
    }

    private static void AssertConfigurationExclusionRecovery(string input)
    {
        var ast = Parser.ParseInput(input, out _, out ParseError[] errors);
        var foundExclusion = errors.Any(static error => string.Equals(error.ErrorId, "AotConfigurationExcluded", StringComparison.Ordinal));
        if (!foundExclusion
            || ast?.EndBlock?.Statements.Count != 2
            || ast.EndBlock.Statements[0] is not ErrorStatementAst
            || ast.EndBlock.Statements[1] is not PipelineAst pipeline
            || pipeline.PipelineElements.Count != 1)
        {
            throw new InvalidOperationException("configuration-recovery: parser did not resume after the excluded declaration.");
        }

        Console.WriteLine("PASS AOT policy configuration-recovery: subsequent pipeline preserved");
    }
}
