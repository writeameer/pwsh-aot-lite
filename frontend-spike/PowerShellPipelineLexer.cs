// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
//
// This deliberately small lexer is adapted from the scanning model in
// PowerShell's engine/parser/tokenizer.cs. It retains its index/line tracking,
// trivia skipping, quote handling, command-parameter and pipe recognition, but
// intentionally excludes dynamic keywords, expandable strings, here strings,
// redirection, operators, type literals and expression-mode tokenization.
// See UPSTREAM-ORIGIN.md for the pinned origin and exact exclusions.

namespace PowerShellAotFrontEnd;

public enum TokenKind
{
    Word,
    String,
    Parameter,
    Variable,
    Pipe,
    Terminator,
    EndOfInput,
}

public sealed record FrontEndToken(TokenKind Kind, string Text, int Offset, int Line, int Column);

public sealed record FrontEndDiagnostic(int Offset, int Line, int Column, string Message);

public sealed record LexResult(IReadOnlyList<FrontEndToken> Tokens, IReadOnlyList<FrontEndDiagnostic> Diagnostics);

public static class PowerShellPipelineLexer
{
    public static LexResult Lex(string script)
    {
        ArgumentNullException.ThrowIfNull(script);
        var scanner = new Scanner(script);
        return scanner.Scan();
    }

    private sealed class Scanner
    {
        private readonly string _script;
        private readonly List<FrontEndToken> _tokens = [];
        private readonly List<FrontEndDiagnostic> _diagnostics = [];
        private int _index;
        private int _line = 1;
        private int _column = 1;

        internal Scanner(string script) => _script = script;

        internal LexResult Scan()
        {
            while (!AtEnd)
            {
                SkipTrivia();
                if (AtEnd)
                {
                    break;
                }

                var start = CaptureMark();
                var current = Peek();
                switch (current)
                {
                    case '|':
                        Advance();
                        Add(TokenKind.Pipe, start);
                        break;
                    case ';':
                        Advance();
                        Add(TokenKind.Terminator, start);
                        break;
                    case '\'':
                    case '"':
                        ScanQuotedString(start, current);
                        break;
                    case '-':
                        ScanParameterOrWord(start);
                        break;
                    case '$':
                        ScanVariable(start);
                        break;
                    default:
                        ScanWord(start);
                        break;
                }
            }

            _tokens.Add(new FrontEndToken(TokenKind.EndOfInput, string.Empty, _index, _line, _column));
            return new LexResult(_tokens, _diagnostics);
        }

        // The structure follows Tokenizer.SkipNewlines/SkipWhiteSpace: comments
        // only start in trivia position, and newline is preserved as a terminator.
        private void SkipTrivia()
        {
            while (!AtEnd)
            {
                if (Peek() is ' ' or '\t' or '\f' or '\v' or '\u00a0' or '\u0085')
                {
                    Advance();
                    continue;
                }

                if (Peek() == '#')
                {
                    while (!AtEnd && Peek() is not '\r' and not '\n')
                    {
                        Advance();
                    }

                    continue;
                }

                if (Peek() is '\r' or '\n')
                {
                    var start = CaptureMark();
                    ConsumeNewline();
                    Add(TokenKind.Terminator, start);
                    continue;
                }

                return;
            }
        }

        private void ScanQuotedString(Mark start, char quote)
        {
            Advance();
            var closed = false;
            while (!AtEnd)
            {
                if (Peek() == quote)
                {
                    Advance();
                    closed = true;
                    break;
                }

                // PowerShell uses backtick escaping in double-quoted strings.
                if (quote == '"' && Peek() == '\u0060' && PeekNext() != '\0')
                {
                    Advance();
                }

                if (Peek() is '\r' or '\n')
                {
                    ConsumeNewline();
                }
                else
                {
                    Advance();
                }
            }

            if (!closed)
            {
                _diagnostics.Add(new FrontEndDiagnostic(start.Offset, start.Line, start.Column, "Unterminated string literal."));
            }

            Add(TokenKind.String, start);
        }

        private void ScanParameterOrWord(Mark start)
        {
            Advance();
            if (char.IsLetter(Peek()) || Peek() == '?')
            {
                while (char.IsLetterOrDigit(Peek()) || Peek() is '-' or '?')
                {
                    Advance();
                }

                Add(TokenKind.Parameter, start);
                return;
            }

            ScanWordRest();
            Add(TokenKind.Word, start);
        }

        private void ScanVariable(Mark start)
        {
            Advance();
            if (Peek() == '{')
            {
                Advance();
                while (!AtEnd && Peek() != '}')
                {
                    Advance();
                }

                if (Peek() == '}')
                {
                    Advance();
                }
                else
                {
                    _diagnostics.Add(new FrontEndDiagnostic(start.Offset, start.Line, start.Column, "Unterminated braced variable."));
                }
            }
            else
            {
                while (char.IsLetterOrDigit(Peek()) || Peek() is '_' or '?' or ':' or '.')
                {
                    Advance();
                }
            }

            Add(TokenKind.Variable, start);
        }

        private void ScanWord(Mark start)
        {
            ScanWordRest();
            if (_index == start.Offset)
            {
                _diagnostics.Add(new FrontEndDiagnostic(start.Offset, start.Line, start.Column, $"Unexpected character '{Peek()}'."));
                Advance();
            }

            Add(TokenKind.Word, start);
        }

        private void ScanWordRest()
        {
            while (!AtEnd && !IsWordTerminator(Peek()))
            {
                Advance();
            }
        }

        private static bool IsWordTerminator(char c) =>
            c == '\0' || char.IsWhiteSpace(c) || c is '|' or ';' or '\'' or '"';

        private void Add(TokenKind kind, Mark start) =>
            _tokens.Add(new FrontEndToken(kind, _script[start.Offset.._index], start.Offset, start.Line, start.Column));

        private Mark CaptureMark() => new(_index, _line, _column);
        private bool AtEnd => _index >= _script.Length;
        private char Peek() => AtEnd ? '\0' : _script[_index];
        private char PeekNext() => _index + 1 >= _script.Length ? '\0' : _script[_index + 1];

        // Adapted from Tokenizer.GetChar/PeekChar/NormalizeCRLF behavior.
        private void Advance()
        {
            if (AtEnd)
            {
                return;
            }

            _index++;
            _column++;
        }

        private void ConsumeNewline()
        {
            if (Peek() == '\r')
            {
                Advance();
                if (Peek() == '\n')
                {
                    Advance();
                }
            }
            else
            {
                Advance();
            }

            _line++;
            _column = 1;
        }

        private readonly record struct Mark(int Offset, int Line, int Column);
    }
}
