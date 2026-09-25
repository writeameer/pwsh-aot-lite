# Measure-Object port notes

## Status and admitted surface

Integrated Native AOT text-measurement subset:

```powershell
Measure-Object -InputObject <one string> -Character [-Word] [-Line] [-IgnoreWhiteSpace]
```

At least one of `-Character`, `-Word`, or `-Line` is required. The command
emits the closed `Lines`, `Words`, `Characters`, `Property` record shape.

- Original: `src/Microsoft.PowerShell.Commands.Utility/commands/utility/Measure-Object.cs` (`MeasureObjectCommand`)
- Generated contract: `GeneratedCmdletPorts.MeasureObject`
- Target: `Pipeline.cs`: `MeasureObjectCmdlet` and `MeasureTextRecord`

## Port timing

| Field | Value |
| --- | --- |
| UTC work ended | `2026-09-25T09:16:41.7643560Z` |
| Scope note | Direct named one-string TextMeasure counters; managed and fresh Native AOT verification completed; integrated to `main` at `e2530bb`. |

## Reuse and boundary

The target retains the upstream text-counter rules: `CountChar` counts string
length or non-whitespace characters, `CountWord` counts whitespace-to-text
transitions, and `CountLine` counts newline-delimited lines with the source
final-line rule. The generated command metadata remains the authority for the
admitted names.

`PSPropertyExpression`, numeric statistics, pipeline `PSObject` input,
parameter sets, conversion, arbitrary object adaptation, and dynamic result
formatting do not transfer. The adapter accepts only the closed direct string
route and outputs a typed record through `PipelineValueAdapter`; it adds no
`PSObject`, reflection, provider, dynamic binder, or runtime compilation.

## Verification

```powershell
dotnet build -c Release -p:PowerShellSourceRoot=/Users/ameerdeen/progs/PowerShell/src
dotnet publish -c Release -r osx-arm64 --self-contained true -p:PowerShellSourceRoot=/Users/ameerdeen/progs/PowerShell/src -o ./artifacts/osx-arm64-measure-object
./artifacts/osx-arm64-measure-object/PwshAotLite --self-test
./artifacts/osx-arm64-measure-object/PwshAotLite -Command 'Measure-Object -InputObject "hello world" -Line -Word -Character'
./artifacts/osx-arm64-measure-object/PwshAotLite -Command 'Measure-Object -InputObject "a b c" -Character -IgnoreWhiteSpace'
```

Fresh native output matched the stock counter results: `1`, `2`, `11` for
`"hello world"`; `3` non-whitespace characters for `"a b c"`. No statistic
switch fails closed with `AOT6730`.
