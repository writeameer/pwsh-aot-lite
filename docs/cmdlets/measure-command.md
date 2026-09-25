# Measure-Command boundary note

## Status and source

Explicitly deferred at the Native AOT execution boundary; no adapter is
registered.

- Original: `src/Microsoft.PowerShell.Commands.Utility/commands/utility/TimeExpressionCommand.cs` (`MeasureCommandCommand`)
- Generated contract: `GeneratedCmdletPorts.MeasureCommand`
- Target: none

## Source-backed boundary

`Expression` is mandatory and typed as `ScriptBlock` (`TimeExpressionCommand.cs:29-34`).
For every `ProcessRecord`, the source calls `Expression.InvokeWithPipe(...)`
(`TimeExpressionCommand.cs:48-59`) and measures that arbitrary execution.
The only source behavior is therefore execution of a supplied script block;
`InputObject` is also a `PSObject` pipeline value.

Native observation: `Measure-Command -Expression { 1 + 1 }` reaches
source-spanned `AOT1001` at `ScriptBlockExpressionAst`. Replacing invocation
with a timer/no-op would claim a result without doing the work PowerShell
measures, so it is not an admissible subset.

## Next action

Do not port until a separately reviewed static script execution boundary exists.
