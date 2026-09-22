# New-Guid port notes

## Status and source

**Complete only for the Wave 2 `-Empty` calibration slice.** The executable
adapter is a static BCL implementation with no PowerShell SDK, reflection,
runtime compilation, dynamic loading, `PSObject`, or runspace.

- Original: `src/Microsoft.PowerShell.Commands.Utility/commands/utility/NewGuidCommand.cs`
- Generated contract: `GeneratedCmdletPorts.NewGuid`
- Target: `Pipeline.cs`: `NewGuidCmdlet`

## What transferred

- With no parameter, the adapter calls `Guid.CreateVersion7()` and emits a
  canonical D-format UUID.
- `-Empty` emits `Guid.Empty`.
- `-Empty:$false` is source-distinct from a bare `-Empty`. The shared binder
  retains the upstream `CommandParameterAst.Argument` association and carries
  only an AST-proven direct Boolean literal to the generated descriptor.
- Direct untransformed output uses the existing prose terminal contract: one
  UUID per line, with no table header.

## Deliberate boundary

`InputObject` is not in this executable descriptor. Its source behavior spans
positional input, `ValueFromPipeline`, string-to-Guid conversion, a
nonterminating `StringNotRecognizedAsGuid` error, and null-output behavior.
Supporting only a string-shaped fragment would invent a pseudo value contract.
Until a later review defines a typed Guid pipeline boundary, named InputObject,
positional GUID input, and `-Empty` plus `-InputObject` are rejected by the
generated binder.

Similarly, direct output is a canonical text presentation through the existing
closed `TextRecord`, not a claim that the generic pipeline carries a Guid CLR
object. A future typed Guid record/value must be an explicit value-plane slice.

Attached switch values other than direct Boolean literals fail closed. For
example `-Empty:'false'`, `-Empty:$null`, and variable/expression forms are not
accepted. This prevents the binder from treating a detached value as a new
positional argument or inferring Boolean semantics from text.

## Verification

```powershell
dotnet build -c Release --no-restore
dotnet run -c Release --no-build -- --self-test
dotnet run -c Release --no-build -- -Command 'New-Guid'
dotnet run -c Release --no-build -- -Command 'New-Guid -Empty'
dotnet run -c Release --no-build -- -Command 'New-Guid -Empty:$false'
pwsh -NoProfile -File ./tools/Test-ParserReuseGuard.ps1
pwsh -NoProfile -File ./tools/Export-PwshParserBaseline.ps1 -Verify
```

Fixture `29-new-guid-switch-attachments.ps1` captures the upstream syntax and
AST baseline for the admitted switch spelling and rejected binding shapes. The
port review ledger is [new-guid-wave2.md](../reviews/new-guid-wave2.md).

## Variances and next step

See `New-Guid` in `port-variances.json`. The next meaningful expansion is not
more string parsing: it is a reviewed typed Guid value/pipeline design that can
honestly cover `InputObject`, error stream semantics, and output shape.
