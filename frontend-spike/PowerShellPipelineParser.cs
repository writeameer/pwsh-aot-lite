// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
//
// A deliberately constrained adaptation of Parser.PipelineRule. It keeps the
// stage/tail shape and empty-pipe diagnostics, while omitting expression mode,
// redirections, pipeline chains, background jobs, AST extents and all execution.

namespace PowerShellAotFrontEnd;

public sealed record CommandStage(string CommandName, IReadOnlyList<FrontEndToken> Arguments);
public sealed record PipelineSyntax(IReadOnlyList<CommandStage> Stages);
public sealed record ParseResult(
    IReadOnlyList<FrontEndToken> Tokens,
    PipelineSyntax? Pipeline,
    IReadOnlyList<FrontEndDiagnostic> Diagnostics);

public static class PipelineParser
{
    public static ParseResult Parse(string script)
    {
        var lexed = PowerShellPipelineLexer.Lex(script);
        var parser = new Parser(lexed.Tokens, lexed.Diagnostics);
        return parser.Parse();
    }

    private sealed class Parser(
        IReadOnlyList<FrontEndToken> tokens,
        IReadOnlyList<FrontEndDiagnostic> lexerDiagnostics)
    {
        private readonly List<FrontEndDiagnostic> _diagnostics = [.. lexerDiagnostics];
        private int _position;

        internal ParseResult Parse()
        {
            var stages = new List<CommandStage>();
            if (Current.Kind is TokenKind.EndOfInput or TokenKind.Terminator)
            {
                return Result(null);
            }

            while (true)
            {
                if (Current.Kind == TokenKind.Pipe)
                {
                    Error(Current, "Empty pipeline element.");
                    Advance();
                    continue;
                }

                var stage = ParseCommand();
                if (stage is not null)
                {
                    stages.Add(stage);
                }

                if (Current.Kind != TokenKind.Pipe)
                {
                    break;
                }

                var pipe = Current;
                Advance();
                if (Current.Kind is TokenKind.EndOfInput or TokenKind.Terminator)
                {
                    Error(pipe, "Empty pipeline element after '|'.");
                    break;
                }
            }

            if (Current.Kind == TokenKind.Terminator)
            {
                Advance();
            }

            if (Current.Kind != TokenKind.EndOfInput)
            {
                Error(Current, "This front-end spike accepts one pipeline only.");
            }

            return Result(stages.Count == 0 ? null : new PipelineSyntax(stages));
        }

        // Constrained command-mode counterpart of Parser.CommandRule, called
        // from the PipelineRule-style loop above.
        private CommandStage? ParseCommand()
        {
            if (Current.Kind is not TokenKind.Word and not TokenKind.String)
            {
                Error(Current, "Expected a command name.");
                return null;
            }

            var command = Current.Text;
            Advance();
            var arguments = new List<FrontEndToken>();
            while (Current.Kind is TokenKind.Word or TokenKind.String or TokenKind.Parameter or TokenKind.Variable)
            {
                arguments.Add(Current);
                Advance();
            }

            return new CommandStage(command, arguments);
        }

        private ParseResult Result(PipelineSyntax? pipeline) => new(tokens, pipeline, _diagnostics);
        private FrontEndToken Current => tokens[Math.Min(_position, tokens.Count - 1)];
        private void Advance() => _position = Math.Min(_position + 1, tokens.Count - 1);

        private void Error(FrontEndToken token, string message) =>
            _diagnostics.Add(new FrontEndDiagnostic(token.Offset, token.Line, token.Column, message));
    }
}

internal static class FrontEndSelfTest
{
    internal static void Run()
    {
        var parsed = PipelineParser.Parse("Get-Process -Name 'pwsh*' | Where-Object CPU -gt 10 | Select-Object Name, Id");
        Require(parsed.Diagnostics.Count == 0, "expected a valid pipeline");
        Require(parsed.Pipeline?.Stages.Count == 3, "expected three stages");
        Require(parsed.Pipeline!.Stages[0].CommandName == "Get-Process", "expected first command");
        Require(parsed.Pipeline.Stages[1].Arguments[1].Kind == TokenKind.Parameter, "expected -gt parameter token");

        var broken = PipelineParser.Parse("Get-Process |");
        Require(broken.Diagnostics.Any(static x => x.Message.Contains("Empty pipeline", StringComparison.Ordinal)), "expected pipe diagnostic");

        var quoted = PipelineParser.Parse("Get-Process -Name \"pwsh\u0060*\" # comment");
        Require(quoted.Diagnostics.Count == 0, "expected quoted argument and comment");
        Require(quoted.Pipeline?.Stages.Single().Arguments.Count == 2, "expected parameter and quoted argument");
    }

    private static void Require(bool value, string message)
    {
        if (!value)
        {
            throw new InvalidOperationException(message);
        }
    }
}
