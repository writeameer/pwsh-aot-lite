# AOT Execution Kernel — foundation slice

## Status

Implemented foundation; language expansion is in progress. This is not a claim
that general PowerShell scripts execute yet.

The host now follows this explicit path:

```text
source document
  └─ AotScriptParser (upstream AST + tokens + parse diagnostics)
       └─ AotExecutionKernel.Compile
            └─ AotExecutionPlan
                 └─ existing reviewed typed command/pipeline executor
```

The first `AotExecutionPlan` established the AST-to-plan boundary. It now owns
an ordered block of assignment, conditional, closed-list foreach, and structural-pipeline plans;
scope, delayed variable binding, closed conditional selection, and closed-list
iteration are documented in the [Language Compatibility Core](language-compatibility-core.md).
General enumeration, parallel loops, and functions remain deliberately absent.

## Current supported execution shape

An unnamed upstream-parsed block with ordered assignments, reviewed
`if`/`elseif`/`else` statements, closed-list synchronous `foreach` statements,
and command pipelines. Each executable
pipeline has:

- one native-AOT source command;
- optionally one direct-property finite-numeric `Where-Object` predicate; and
- optionally one direct-field `Select-Object` projection.

The predicate value and command arguments may resolve through the reviewed
lexical scope; see the Language Compatibility Core for the exact expression
subset and exclusions.

The command binder remains `AotCmdletRegistry.BindCommand`; the kernel does not
introduce a second parameter binder. Typed cmdlet output still crosses the
generic pipeline boundary only through the existing explicit `AotValue`/
`AotRecord` adapter.

## New shared contracts

- `AotScriptParser` is the only production source parser facade. It returns
  the caller's source/document identity/version, upstream `ScriptBlockAst`,
  upstream tokens, and adapted parse diagnostics without running source.
- `AotExecutionKernel.Compile` is the only host compile boundary. It turns a
  successful parse result into an `AotExecutionPlan`.
- `AotDiagnostic` is the only new kernel failure contract. The first adapted
  paths are upstream parse errors, AOT unsupported syntax, and central command
  binding failures. Existing ports still have a transitional compatibility
  wrapper while their individual errors migrate to typed origin diagnostics.

## Diagnostic IDs in this foundation

| ID | Meaning |
| --- | --- |
| upstream parser ID, such as `EmptyPipeElement` | Original parser error, preserved with its source extent |
| `AOT1001` | Valid parsed construct is outside the reviewed AOT execution subset |
| `AOT2000` | Host invocation/usage error before a script is compiled |
| `AOT2001` | Command is not executable by the static registry |
| `AOT2002` | Unsupported command parameter |
| `AOT2003` | Parameter supplied more than once |
| `AOT2004` | Parameter requires a value |
| `AOT2005` | Positional argument is not supported |
| `AOT3001`–`AOT3004` | `Get-Process` validation failures with command source context |
| `AOT4001`–`AOT4008` | `Where-Object` / `Select-Object` structural-stage validation failures |
| `AOT5001`–`AOT5006` | Lexical scope, value-to-binder conversion, variable predicate, closed conditional, and closed-list foreach failures |
| `AOT3000` | Transitional typed wrapper around an untouched legacy runtime error; it still inherits the active command span |
| `AOT9000` | Unexpected host failure, with implementation detail withheld from normal output |

`AOT3000` is transitional only. Every port touched during the campaign must
replace it with a category-specific diagnostic at its originating boundary.

## Evidence

`SelfTest` verifies that the parser facade preserves source identity, AST,
tokens, and diagnostics; that `AOT1001` has a precise script-block extent and
stable plain-text rendering snapshot; and that malformed and binding paths
produce typed diagnostics with source spans. The existing full self-test
continues to verify all current cmdlet/control-plane behavior.

The independent grammar, AOT-boundary, and diagnostic verdicts are recorded in
the [foundation review ledger](../reviews/2026-09-22-aot-execution-kernel-foundation.md).

Run:

```powershell
dotnet build -c Release
dotnet run -c Release -- --self-test
pwsh -NoProfile -File tools/Test-ParserReuseGuard.ps1
pwsh -NoProfile -File tools/Export-PwshParserBaseline.ps1 -Verify
```

## Next increments

1. Add named local functions with independently proven scope semantics.
2. Migrate all existing port/runtime failures to typed origin diagnostics and
   add fixture-based snapshot coverage.
