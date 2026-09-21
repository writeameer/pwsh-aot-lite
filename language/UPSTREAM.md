# Upstream language extraction

This directory is the integrated, limited structural front end of the AOT
runner. It is **not** `frontend-spike/`, and does not share source with it.

## Pinned source

- Repository: `https://github.com/PowerShell/PowerShell.git`
- Commit: `1e53f6bbab4b8791eae782474d21889f9e5d6038`
- License: MIT; the Microsoft copyright/license headers in copied files are
  retained verbatim.

## Copied verbatim in this initial closure

| Target | Upstream source |
| --- | --- |
| `upstream/CharTraits.cs` | `src/System.Management.Automation/engine/parser/CharTraits.cs` |
| `upstream/Position.cs` | `src/System.Management.Automation/engine/parser/Position.cs` |
| `upstream/token.cs` | `src/System.Management.Automation/engine/parser/token.cs` |
| `upstream/tokenizer.cs` | `src/System.Management.Automation/engine/parser/tokenizer.cs` |
| `upstream/Parser.cs` | `src/System.Management.Automation/engine/parser/Parser.cs` |
| `upstream/ast.cs` | `src/System.Management.Automation/engine/parser/ast.cs` |
| `upstream/astVisitor.cs` | `src/System.Management.Automation/engine/parser/astVisitor.cs` |
| `upstream/PreOrderVisitor.cs` | `src/System.Management.Automation/engine/parser/PreOrderVisitor.cs` |
| `upstream/VariablePath.cs` | `src/System.Management.Automation/engine/VariablePath.cs` |

No grammar implementation has been written in this project. The exact source
closure compiles with only documented execution-engine removals or policy
gates; it is consumed by the runner through a narrow AST lowerer.

## Deliberate exclusions

`Compiler.cs`, `PSObject`, `PowerShell`, runspaces, reflection emit, dynamic
assembly loading, and runtime code generation must not be reachable from this
host. A successfully compiling extraction is not permission to execute an AST;
the current lowerer owns explicit supported-feature policy.

## Delta ledger

| File | Delta | Why |
| --- | --- | --- |
| `AotEngineHookStubs.cs` | Explicit non-executing compatibility declarations for upstream AST/parser references. Every operation that would execute, bind, load, or dynamically compile throws `AotExecutionBoundary`. | The upstream front end is coupled at compile time to its execution engine. The stubs make that coupling visible; they are not an execution implementation. |
| `AotEngineHookStubs.cs` | Copies the numeric `BigInteger` narrowing helpers and `ParseBinary(ReadOnlySpan<char>, bool)` from upstream `engine/Utils.cs`, and the `Type.GetTypeCode` extension from upstream `utils/ExtensionMethods.cs`. | `tokenizer.cs` calls these helpers while assigning numeric token values; they are required for lexical fidelity and do not bind or execute PowerShell code. |
| `AotEngineHookStubs.cs` | Makes the `Ast.SafeGetValue`, `ScriptBlockToPowerShellConverter`, `Position` remoting, and `ReflectionTypeName` display hook signatures source-compatible but fail closed through `AotExecutionBoundary`. | These APIs evaluate ASTs, create runtime pipelines, serialize remoting data, or depend on ETS/type accelerators. None participates in syntax construction; the AOT lowerer owns supported execution. |
| `upstream/ast.cs` | Excludes only four upstream expression-compiler members: `AttributedExpressionAst` assignment get/set and `VariableExpressionAst` assignment get/set/type-layout methods. | They construct `DynamicExpression`, compiler visitors, and mutable tuple layouts. The AST constructors/traversal and all parser grammar methods remain unchanged; the AOT lowerer owns supported execution. |
| `upstream/ast.cs` | Gates `GenericTypeName.GetReflectionType`, `GenericTypeName.GetReflectionAttributeType`, and `ArrayTypeName.GetReflectionType` under the AOT feature constant. | The parser still constructs and traverses structural type-name AST nodes, but closed generic/array `Type` materialization uses reflection APIs that Native AOT cannot safely support. The methods fail closed until a deliberate static type policy exists. |
| `AotEngineHookStubs.cs` | Preserves upstream `VariableAnalysis.Unanalyzed = -1` as its exact structural sentinel while leaving analysis itself out of scope. | AST node construction requires the `int` sentinel; copying the variable-analysis/binder engine is neither necessary nor AOT-safe. |
| `PwshAotLite.Language.csproj` | Defines `PWSH_AOT_LANGUAGE_EXCLUDE_DSC` for this host. | Makes the DSC exclusion mechanically visible and prevents an accidental source-level re-enable. |
| `upstream/Parser.cs` | Gates the upstream `ConfigurationStatementRule` behind `PWSH_AOT_LANGUAGE_EXCLUDE_DSC`. The active path emits stable `AotConfigurationExcluded`, consumes the declaration through its balanced body, and returns an `ErrorStatementAst`. | DSC configuration parsing in upstream creates runspaces, invokes PowerShell, and loads dynamic DSC keywords. The AOT host must neither execute that work nor silently accept configuration as supported syntax. |
| `upstream/tokenizer.cs` | Gates `DynamicKeywordExtension.IsMetaDSCResource`; the active AOT branch fails closed. | Dynamic DSC keyword classification has no safe standalone meaning without the DSC subsystem and must never be reached from a supported parser path. |
| `upstream/tokenizer.cs` | Under `PWSH_AOT_LANGUAGE_EXCLUDE_DSC`, seals the public dynamic-keyword registry: all mutation/lifetime APIs throw stable `AotDynamicKeywordRegistryExcluded`; query APIs expose a permanent empty registry. | The upstream registry accepts `PreParse`/`PostParse`/`SemanticCheck` delegates which the parser can execute. Ordinary identifier scanning still queries the registry, so an empty read-only view preserves ordinary command parsing while preventing external registration from reaching parser-time execution. |
| `upstream/ast.cs` | Gates `ConfigurationDefinitionAst.GenerateSetItemPipelineAst` and `IsImportCommand`; active AOT branches fail closed. | These routines synthesize the DSC configuration command and statically bind `Import-DscResource`. They are execution/binding behavior, not ordinary grammar. |
| `AotEngineHookStubs.cs` | Adds the minimal `SystemPolicy`/`SystemEnforcementMode` and debug `ExtendedTypeSystem` type marker used by remaining non-DSC parser references. The policy selects `Enforce`; audit logging fails closed. | Allows the upstream class-policy branch and debug resource-key assertion to compile without implementing ETS, Windows policy, or audit logging. |
| `GeneratedResourceStubs.cs` | Generates compile-time diagnostic resource keys referenced by the copied parser/AST. | Keeps diagnostic identities visible while real localized resource extraction remains a separate, attributable step. |
| `AotEngineHookStubs.cs` | Copies the exact `VariablePathExtensions.IsAnyLocal` predicate from upstream `engine/parser/VariableAnalysis.cs`; `upstream/VariablePath.cs` is restored to its verbatim form. | `ast.cs` references the extension while evaluating a safe-variable predicate. This retains upstream semantics (`IsUnscopedVariable || IsLocal || IsPrivate`) without claiming the earlier, incorrect `IsLocal || IsUnqualified` approximation; full variable analysis/binding remains excluded. |

## Current status: integrated structural parser

This first extraction exposed a material truth about the source boundary:
`Parser.cs` and `ast.cs` are not a self-contained front end. Numeric lexical
support is copied; AST compiler/binder/remoting APIs are explicitly fail-closed;
and DSC configuration and dynamic-keyword registration are explicit parse-time
exclusions. The project builds
and its standalone host exercises the real `Parser.ParseInput` entry point
against every checked-in `tests/grammar/fixtures/*.ps1` source file. It
compares tokens (kind/text/flags/extents), AST hierarchy/extents, and
diagnostic IDs/extents to the stock-pwsh JSON oracle. DSC policy tests are
separate because their intentional `AotConfigurationExcluded` diagnostic is
not an upstream-compatibility result. Native AOT publication for `osx-arm64`
also runs this differential suite when the fixture and baseline roots are
provided explicitly.

The root runner references this project and lowers only its reviewed structural
subset to static command binding; the standalone host remains the differential
test harness. The remaining error catalog is deliberately retained as
`error-catalog.txt`. This is still not a usable PowerShell runtime and not full
syntax or diagnostic parity: `SymbolResolver`,
`SemanticChecks`, and `TypeResolver` remain structural placeholders, while
some type-name AST APIs remain structural only; generic and array reflection
materialization now fail closed, and type execution is unsupported until a
deliberate static type policy exists. Do not add a handwritten fallback parser.
