# Language Compatibility Core — variables, statements, and conditionals

## Status

Implemented second slice. This extends the AOT Execution Kernel from one
pipeline to ordered statements with lexical values and narrowly defined
`if`/`elseif`/`else` selection. It is not a claim of general PowerShell
expression, session-state, or script compatibility.

```text
upstream ScriptBlockAst
  └─ AotBlockPlan
       ├─ AotAssignmentPlan(name, closed expression plan)
       └─ AotPipelineStatementPlan(command/stage plans)
       └─ AotIfStatementPlan(closed conditions, nested statement blocks)
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

The control-flow slice accepts `if`, zero or more `elseif` clauses, and an
optional `else`, provided every condition is one direct upstream
`CommandExpressionAst` containing either a Boolean value or one closed
comparison. Conditions are evaluated lazily in source order; only the selected
body executes.

```powershell
$threshold = 10
if ($threshold -gt 0) {
    $group = 'Common'
    Get-Verb -Group $group | Select-Object Verb
}
elseif ($false) {
    Get-Verb -Group Filter
}
else {
    Get-Verb -Group Common
}
```

Numeric values support `-eq`, `-ne`, `-gt`, `-ge`, `-lt`, and `-le` across the
closed numeric kinds. Strings and Boolean values support only `-eq`/`-ne`;
the default `I*` operators compare strings ordinal-ignore-case and `C*`
operators compare ordinal-case-sensitive. There is no PowerShell coercion,
truthiness, collection vectorization, or arbitrary object comparison.

`if` braces deliberately use the surrounding `AotScope`, matching PowerShell:
an assignment in the selected body remains visible afterwards. A future
scope-forming construct such as a local function must define its own scope rule
instead of inheriting this one accidentally.

## Deliberate exclusions

The following parsed forms remain fail-closed: scoped/drive-qualified and
automatic variables, splatting, compound assignment, multi-target assignment,
interpolated strings/subexpressions, `@(...)`, hashtables, casts, member/index
access, operator expressions, assignment from commands/pipelines, redirection,
backgrounding, functions, loops, flow-control statements, script blocks, and
named PowerShell blocks. Conditional `-and`, `-or`, `-not`, invocation or
pipeline conditions, and all expressions beyond the direct condition matrix
above remain excluded. They need a dedicated reviewed plan; they must never
fall through to the dynamic PowerShell runtime.

The lexical scope additionally reserves every name in the pinned upstream
`engine/SpecialVariables.cs` catalog plus the upstream event-action automatic
variables and host-created `$PROFILE` (for example `$PID`, `$PSCulture`,
`$Host`, `$PSBoundParameters`, `$OFS`, `$ErrorActionPreference`, and `$PWD`).
They fail with `AOT5002` on both read and assignment rather than becoming
invented local variables.

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
| `AOT5005` | An `if` condition or comparison uses a value/shape outside the closed condition subset. |

Static AST forms outside this slice retain `AOT1001`. Every scope/evaluation
failure points at the source use-site, not a previous assignment or the whole
command. Source-derived command value spans now flow through `CommandInvocation`
so port validation, such as `Get-Process -Id $value`, underlines `$value`.

## Evidence and next increment

Grammar fixtures `05`–`10` cover variables, valid conditional ASTs, condition
boundaries, and malformed assignment/conditional input against stock `pwsh`.
`SelfTest` covers block ordering, list value expansion, case-insensitive parent
scope lookup, explicit REPL scope reuse, fresh command isolation,
parameter-injection resistance, selected/skip/elseif behavior, same-scope
branch assignment, output segment streaming, and typed diagnostic snapshots.

The next language increments are `foreach`, then local functions. They must
extend `AotBlockPlan` through the parser facade, value plane, and central
binder; their scope semantics require a separate PowerShell-compatibility
decision and test proof.

The original variables-only admission is recorded in the
[Language Compatibility Core review ledger](../reviews/2026-09-22-language-compatibility-core.md).
Conditional execution and terminal presentation are admitted separately in the
[control-flow and terminal-presentation review ledger](../reviews/2026-09-22-if-control-flow-terminal-presentation.md).
