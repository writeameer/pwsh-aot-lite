# Upstream source origin

This is an isolated Native AOT front-end proof. It has no project or assembly
reference to System.Management.Automation, and it imports no execution code.

> **Architecture status: archived proof only.** This project is deliberately
> not a production parser and must never be referenced by `PwshAotLite.csproj`.
> Its reduced lexer/parser is useful evidence that an AOT-only front-end can be
> built, but extending it would create a competing PowerShell grammar. Production
> syntax must instead be extracted from the pinned upstream lexer/parser/AST
> according to [`../PARSER-REUSE-GUARD.md`](../PARSER-REUSE-GUARD.md).

## Pinned source

- Repository: https://github.com/PowerShell/PowerShell
- Commit: 1e53f6bbab4b8791eae782474d21889f9e5d6038
- Licence: MIT; see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)
- Tokenizer source: src/System.Management.Automation/engine/parser/tokenizer.cs
- Parser source: src/System.Management.Automation/engine/parser/Parser.cs
- Character support source: src/System.Management.Automation/engine/parser/CharTraits.cs

## Directly adapted front-end ideas

PowerShellPipelineLexer.cs follows the upstream scanner's explicit
index/peek/advance model and its SkipNewlines / SkipWhiteSpace trivia policy
(upstream tokenizer.cs, GetChar/PeekChar around lines 837-867 and
SkipNewlines/SkipWhiteSpace around lines 880-988). It preserves source
positions, comments in trivia position, CRLF normalization, string scanning,
pipe tokens, parameter tokens and variable tokens.

PowerShellPipelineParser.cs is a constrained structural adaptation of upstream
Parser.PipelineRule (around line 5990): it parses a command followed by zero or
more pipe-command tails and reports empty pipeline elements.

## Intentional changes and stubs

This is not an attempt to compile upstream Tokenizer, Parser, Token, or ast.cs
unchanged. Their AST closure reaches the complete language engine: IScriptExtent,
PSObject, LanguagePrimitives, CommandInfo, dynamic keywords/DSC, type
resolution, semantic checks, runspaces, formatting and resource strings.
Pulling that closure into this proof would conceal the exact runtime boundary
being tested.

The spike deliberately supports only one command-mode pipeline:

- words, parameters, variables and quoted literals;
- pipe, newline and semicolon terminators;
- line comments and source-position diagnostics.

It deliberately excludes expressions, script blocks, expandable/here strings,
redirection, invocation operators, all command discovery/binding, dynamic
keywords, AST visitor APIs, semantic checks and all execution.

Compiler.cs is not referenced, copied, or transitively imported. The result is
pure data (PipelineSyntax) that a precompiled AOT evaluator can consume.

## What this proves

We can reuse the shape and lexical rules of the upstream handwritten front end
without carrying runtime compilation. The next decision is whether to grow this
constrained, AOT-owned syntax tree or extract a larger upstream AST closure into
a separate frontend-only library. The latter needs a dependency-by-dependency
audit first; it is not a safe mechanical copy.
