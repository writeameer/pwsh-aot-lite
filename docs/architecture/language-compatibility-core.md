# Language Compatibility Core — variables, statements, conditionals, and closed-list foreach

## Status

Implemented fourth slice. This extends the AOT Execution Kernel from one
pipeline to ordered statements with lexical values, narrowly defined
`if`/`elseif`/`else` selection, and closed-list `foreach`. It is not a claim of general PowerShell
expression, session-state, or script compatibility.

```text
upstream ScriptBlockAst
  └─ AotBlockPlan
       ├─ AotAssignmentPlan(name, closed expression plan)
       └─ AotPipelineStatementPlan(command/stage plans)
       └─ AotIfStatementPlan(closed conditions, nested statement blocks)
       └─ AotForEachStatementPlan(closed list, nested statement block)
       └─ AotFunctionDefinitionPlan(pre-lowered local function)
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

## Named local functions

The fourth slice accepts a sequential, root-script declaration and a direct
call of that name:

```powershell
function Get-CommonVerb($group) {
    Get-Verb -Group $group | Select-Object Verb
}

Get-CommonVerb Common
```

`FunctionDefinitionAst` is lowered once into an immutable
`AotLocalFunctionPlan`; executing its declaration installs that plan in the
current explicit `AotScope`. There is no source recompilation, `ScriptBlock`
execution, runspace, reflection, or dynamic command discovery. Declarations
therefore take effect in source order: a call before its declaration is the
ordinary static-registry unknown-command failure. The REPL's explicit session
scope retains a declaration between submissions; each `-Command` call starts
with a fresh scope.

Function names are case-insensitive and resolve before native adapters, so a
local function may shadow a built-in command. Invocation is direct and must be
the whole pipeline statement. Its body executes through the same context and
output sink, preserving cancellation and ordered output segments.

Each invocation creates `new AotScope(callerScope)`. Parameters and assignments
are local, while an unresolved variable reads through the caller chain. This
is the deliberately small dynamic-lookup behavior needed for ordinary local
functions, not a general `SessionState` or closure implementation.

The admitted signature is unscoped, untyped header parameters with closed
values only. Values stay closed—there is no stringification/reparse path.
Splatting, aliases/abbreviations, switches, parameter sets, non-trailing or
non-closed defaults, type/validation attributes, body `param`, `$args`,
`$input`, and `$PSBoundParameters` are not supported.
Direct or indirect recursive re-entry produces `AOT5007` rather than consuming
the native host stack. Local function calls admit exact case-insensitive named
parameters, positional values before named parameters, and trailing direct
literal/literal-list defaults. Their header binder works on AST-derived closed
values only; it is not a cmdlet binder and has no aliases, abbreviations,
parameter sets, type conversion, attributes, splatting, `$args`, `$input`, or
`$PSBoundParameters`. Missing/excess values use `AOT5008`; unknown, duplicate,
missing-named-value, and positional-after-named failures use `AOT5009` through
`AOT5012` with source spans.

An admitted local function may be the first source of an outer pipeline. Its
already-lowered supported body executes into a private ordered collection of
known typed output rows, which crosses once into an immutable `AotRecordBatch`.
Outer pipelines may apply up to four direct `Where-Object`/`Select-Object`
transforms, including repeated or mixed stages. Functions cannot be downstream
stages, receive pipeline rows, feed a native typed-input stage, expose rendered
terminal text, or create an object stream. Heterogeneous producer output needs
a last direct `Select-Object` to establish one rendering shape; otherwise it
fails closed. This remains static output-segment composition, not general
PowerShell function pipelines.

Transform order is semantically material in this subset. A direct
`Select-Object` narrows the immutable record shape immediately, so a following
`Where-Object` can use only retained numeric fields. The last direct projection
sets terminal columns; without one, a function producer uses the same default
column sequence/casing on every emitted segment or rejects the composition.

## Bare function return

Inside an admitted local-function body, bare `return` is a typed,
function-local control-flow result. It emits no output, preserves output
segments already emitted by earlier statements, and suppresses the remaining
body and enclosing closed `if`/`foreach` work. The invoked function consumes
that result, so its caller continues normally. Cancellation remains host-owned
control flow and is checked before every statement; it is not swallowed by a
pending return.

`return <value>` and `return <pipeline>` remain rejected. Stock PowerShell
writes those values into the current output pipeline, but this runner has not
yet admitted the required closed-value-to-typed-record projection. Root returns
are also rejected rather than being allowed to terminate the native host.

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

The iteration slice accepts unlabeled, synchronous `foreach` only when its
source is one direct closed expression that evaluates to an `AotValue` list:

```powershell
$verbs = 'Add', 'Get'
foreach ($verb in $verbs) {
    Get-Verb -Verb $verb | Select-Object Verb
}
```

The source list is evaluated once before the first loop-variable assignment and
is never re-evaluated. Each item is assigned to the surrounding scope and the
body uses the normal output sink, so results retain source order and a later
iteration failure does not hide an earlier completed pipeline. The loop variable
and body assignments remain visible afterward; an empty list leaves a previous
loop-variable value unchanged. A `$null` *item inside a list* is one item.

This is deliberately not general PowerShell enumeration: scalar values and
scalar `$null` produce `AOT5006`; command/pipeline sources, arbitrary CLR
`IEnumerable` values, ranges, and `IPipelineRecord` output do not cross into
the list plane. This boundary avoids reintroducing dynamic enumeration or an
implicit object adapter.

## Deliberate exclusions

The following parsed forms remain fail-closed: scoped/drive-qualified and
automatic variables, splatting, compound assignment, multi-target assignment,
interpolated strings/subexpressions, `@(...)`, hashtables, casts, member/index
access, operator expressions, assignment from commands/pipelines, redirection,
backgrounding, advanced/nested/conditional functions, general loops,
flow-control statements other than bare local-function `return`, script blocks, and
named PowerShell blocks. Conditional `-and`, `-or`, `-not`, invocation or
pipeline conditions, and all expressions beyond the direct condition matrix
above remain excluded. They need a dedicated reviewed plan; they must never
fall through to the dynamic PowerShell runtime.

Within the admitted root/non-function `foreach` shape, labels, `-parallel`,
`-throttlelimit`, pipeline/range sources, `break`, `continue`, and `return`
remain excluded. A bare `return` inside an admitted local-function `foreach`
body follows the function-return contract above.
`foreach`'s upstream automatic enumerator variable is not implemented; `$foreach`
remains reserved by the lexical-scope policy.

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
| `AOT5006` | A `foreach` collection resolves to a non-list closed value. |
| `AOT5007` | A local function recursively re-enters the active AOT call chain. |
| `AOT5008` | A local function call has missing or excess closed values. |
| `AOT5009` | A local function named argument does not match an exact declared header parameter. |
| `AOT5010` | A local function header parameter was supplied more than once. |
| `AOT5011` | A local function named header parameter has no following closed value. |
| `AOT5012` | A local function positional value occurs after a named header parameter. |

Static AST forms outside this slice retain `AOT1001`. Every scope/evaluation
failure points at the source use-site, not a previous assignment or the whole
command. Source-derived command value spans now flow through `CommandInvocation`
so port validation, such as `Get-Process -Id $value`, underlines `$value`.

## Evidence and next increment

Grammar fixtures `05`–`13` cover variables, valid conditional/foreach ASTs,
their boundaries, and malformed assignment/conditional/foreach input against
stock `pwsh`.
`SelfTest` covers block ordering, list value expansion, case-insensitive parent
scope lookup, explicit REPL scope reuse, fresh command isolation,
parameter-injection resistance, selected/skip/elseif behavior, same-scope
branch assignment, closed-list foreach source snapshot/order/nesting/final
scope behavior, output segment streaming, and typed diagnostic snapshots.

The next language increment should generalize the typed data plane beyond the
single transparent function-producer segment. It must remain a parser-facade
plan with the same closed value and diagnostic boundary.

The original variables-only admission is recorded in the
[Language Compatibility Core review ledger](../reviews/2026-09-22-language-compatibility-core.md).
Conditional execution and terminal presentation are admitted separately in the
[control-flow and terminal-presentation review ledger](../reviews/2026-09-22-if-control-flow-terminal-presentation.md).
Closed-list foreach is admitted separately in the
[foreach review ledger](../reviews/2026-09-22-foreach-closed-list.md).
Named local functions are admitted separately in the
[local-function review ledger](../reviews/2026-09-22-named-local-functions.md).
Bare function return is admitted separately in the
[function-return review ledger](../reviews/2026-09-22-function-return.md).
Static function composition and header binding are admitted separately in the
[function-composition review ledger](../reviews/2026-09-22-static-function-composition.md).
