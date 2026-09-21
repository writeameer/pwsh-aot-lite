# Upstream parser runner integration

The executable runner now uses the pinned upstream-derived
`System.Management.Automation.Language.Parser` for its program syntax entry
point. The connection is deliberately narrow:

```text
script text
  -> AotScriptParser (upstream AST, tokens, extents, diagnostics)
  -> AotExecutionKernel / AotExecutionPlan
  -> PipelineAst / CommandAst lowerer
  -> existing AotCmdletRegistry + generated CmdletDescriptor binder
  -> typed IPipelineRecord cmdlet output
  -> explicit AotValue/AotRecord adapter for generic stages
```

`UpstreamAstPipelineLowerer` never executes an AST, creates a script block,
uses the dynamic compiler/binder, `PSObject`, or a runspace. It accepts only a
single top-level command pipeline with one native registered source command and
up to `Where-Object <property> <comparison> <number>` and
`Select-Object <property>[, <property>...]` stages. The first proof is:

```powershell
Get-Process | Where-Object CPU -gt 10 | Select-Object Name, Id
```

The lowerer translates only scalar/array command argument AST nodes into
`CommandSyntaxAtom` values. `AotCmdletRegistry.BindCommand` remains the single
parameter binder and obtains command names, aliases, parameter shapes, and
defaults from generated `CmdletDescriptor` metadata. No AST-specific binder
exists.

Every unsupported or invalid input follows the shared diagnostic contract:

- the original upstream parser ID (for example `EmptyPipeElement`) with its
  source extent for parser diagnostics; and
- `AOT1001` for syntax that parses but is outside the AOT executable subset,
  including script-block predicates, expressions, redirections, additional
  command stages, DSC, and dynamic keywords.

The host renders those typed diagnostics with source labels and actionable help
where a safe alternative exists. Binding failures use the `AOT200x` family and
underlines the precise source argument or parameter when available. See the
[AOT Execution Kernel foundation](aot-execution-kernel.md) for the ID registry
and scope.

The stock-pwsh fixture comparison remains in the isolated `language` project.
The runner connection is covered by `SelfTest` and a published-native command
smoke. There is no second source lexer/parser: `AotScriptParser` is the sole
production source-parser facade, and the former `ScriptParser` class contains
only AST-atom semantic helpers for the narrow `Where-Object` and
`Select-Object` stages. No new grammar may be added outside the upstream
language extraction.

The current independent review outcome is recorded in the
[upstream-parser runner-connection ledger](../reviews/2026-09-21-upstream-parser-runner-connection.md).
