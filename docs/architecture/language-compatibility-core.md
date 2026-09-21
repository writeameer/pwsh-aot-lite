# Language Compatibility Core — variables and top-level statements

## Status

Implemented first slice. This extends the AOT Execution Kernel from one
pipeline to an ordered, flat top-level block with lexical values. It is not a
claim of general PowerShell expression, session-state, or script compatibility.

```text
upstream ScriptBlockAst
  └─ AotBlockPlan
       ├─ AotAssignmentPlan(name, closed expression plan)
       └─ AotPipelineStatementPlan(command/stage plans)
             └─ execute with an explicit AotScope
                    └─ resolved atoms → existing AotCmdletRegistry binder
```

The upstream parser remains the only grammar authority. The block plan is a
feature-policy and execution layer over its AST; it does not tokenize, parse,
or evaluate PowerShell source itself.

## Supported shape

The current slice accepts an unnamed top-level `EndBlock` containing ordered
assignments and the existing structural command pipeline:

```powershell
$threshold = 10
$names = 'pwsh', 'dotnet'
Get-Process -Name $names | Where-Object CPU -gt $threshold | Select-Object Name, Id
```

Supported RHS values are null, Boolean, finite integer/decimal/floating-point
numbers, non-interpolated string constants, direct variable references, and
comma-array literals of those values. Variable names are case-insensitive.
Command arguments and the direct `Where-Object` predicate value may be direct
variables. A list expands only into value atoms for an already explicit AST
parameter; a variable string such as `'-Name'` cannot manufacture a parameter.

Each `-Command` invocation creates a fresh root scope. The REPL deliberately
owns one explicit session scope so a successful assignment can be used on a
later submission. Neither is `SessionState`; scope lookup never reads the
environment, providers, automatic variables, or CLR objects.

## Deliberate exclusions

The following parsed forms remain fail-closed: scoped/drive-qualified and
automatic variables, splatting, compound assignment, multi-target assignment,
interpolated strings/subexpressions, `@(...)`, hashtables, casts, member/index
access, operator expressions, assignment from commands/pipelines, redirection,
backgrounding, functions, control flow, script blocks, and named PowerShell
blocks. They need a dedicated reviewed plan; they must never fall through to
the dynamic PowerShell runtime.

`Select-Object` columns remain direct literals. The current `Where-Object`
property/operator form remains static; only its numeric value may resolve from
scope.

## Binding and value boundary

`AotExpressionPlan` evaluates only to the existing immutable `AotValue` model.
The `AotCommandArgumentConverter` is the single finite conversion to the
existing string-valued generated-metadata binder. It accepts null, Boolean,
finite numbers, strings, and lists of those values with invariant formatting;
bytes, records, dates, and arbitrary CLR objects are rejected. The binder has
no AST or scope dependency and remains the sole authority for aliases,
parameter sets, and value placement.

Output is retained and emitted as ordered segments (`rows + columns`) after
each completed pipeline statement. This avoids applying one command's table
shape to a later command with different output, and ensures output from an
earlier successful statement remains visible if a later statement terminates.

## Diagnostics

| ID | Meaning |
| --- | --- |
| `AOT5001` | A variable read has no value in the supplied lexical scope. |
| `AOT5002` | Scoped, automatic, splatted, or otherwise unsupported variable form. |
| `AOT5003` | A closed value kind cannot become a command argument. |
| `AOT5004` | A resolved `Where-Object` predicate value is not finite numeric. |

Static AST forms outside this slice retain `AOT1001`. Every scope/evaluation
failure points at the source use-site, not a previous assignment or the whole
command. Source-derived command value spans now flow through `CommandInvocation`
so port validation, such as `Get-Process -Id $value`, underlines `$value`.

## Evidence and next increment

Grammar fixtures `05-block-variables.ps1` and
`06-variable-syntax-boundaries.ps1` are compared against stock `pwsh` by the
existing differential harness. `SelfTest` covers block ordering, list value
expansion, case-insensitive parent scope lookup, explicit REPL scope reuse,
fresh command isolation, parameter-injection resistance, and typed diagnostic
snapshots.

The next language increments are direct `if`, `foreach`, then local functions.
They must create child scopes through `AotScope` and extend `AotBlockPlan`; they
must not bypass the parser facade, value plane, or central binder.

The admission evidence and independent verdicts for this slice are recorded in
the [Language Compatibility Core review ledger](../reviews/2026-09-22-language-compatibility-core.md).
