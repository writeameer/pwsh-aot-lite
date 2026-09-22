# New-TimeSpan port notes

## Status and source

**Complete only for the Wave 2 direct-component calibration slice.** The
adapter remains static-BCL-only: no PowerShell SDK, `PSObject`, reflection,
runtime compilation, dynamic load, or runspace crosses into the executable.

- Original: `src/Microsoft.PowerShell.Commands.Utility/commands/utility/NewTimeSpanCommand.cs`
- Generated contract: `GeneratedCmdletPorts.NewTimeSpan`
- Target: `Pipeline.cs`: `NewTimeSpanCmdlet` and `TimeSpanRecord`

## What transferred

- No parameters emits `TimeSpan.Zero`, as the upstream command's default
  `Date` parameter set starts and ends at the same instant.
- The generated `Time` parameter set's `Days`, `Hours`, `Minutes`, `Seconds`,
  and `Milliseconds` integer components directly call the BCL five-component
  `TimeSpan` constructor.
- Direct terminal output is the invariant standard `c` value format, one value
  per line. Structural pipelines use an explicit closed `TimeSpanRecord`, not
  CLR-member discovery. Its adapter declares `Value`, component fields,
  `Ticks`, and `TotalDays`/`TotalHours`/`TotalMinutes`/`TotalSeconds`/
  `TotalMilliseconds`.
- Bad component text produces source-spanned `AOT3005`; a combined duration
  outside `TimeSpan` emits source-spanned `AOT3006`.

The explicit record is a target data-plane contract, rather than a claim that
the upstream CLR `TimeSpan` object has acquired a `Value` property: the target's
`Value` is its invariant `c`-format text, and its total fields use the shared
finite table renderer (which may round displayed floating-point values). The
source binder can coerce some fractional numeric arguments, such as `1.2`, to
an `int`; this slice deliberately accepts only invariant integer text, rejecting
fractional values with `AOT3005` until scalar coercion has a shared contract.

## Deliberate boundary

The source `Date` parameter set is absent from the executable descriptor.
`Start` (including alias `LastWriteTime`), `End`, and positional date input are
rejected by the generated binder. A pipeline stage such as `Get-Date |
New-TimeSpan` is rejected one layer earlier by the structural pipeline lowerer
as `AOT1001`, because a typed-input command stage has not been admitted.
The source picks `DateTime.Now` for a missing endpoint; copying that behavior
without a typed DateTime input and conversion contract would only create a
misleading partial port.

The adapter intentionally does not add `TimeSpan` to `AotValue` or a generic
CLR-object path. The closed record exposes only the finite fields documented
above, so future consumers must explicitly choose a duration contract.

## Verification

```powershell
dotnet build -c Release --no-restore
dotnet run -c Release --no-build -- --self-test
pwsh -NoProfile -File ./tools/Test-ParserReuseGuard.ps1
pwsh -NoProfile -File ./tools/Export-PwshParserBaseline.ps1 -Verify
pwsh -NoProfile -File ./tools/Export-Phase10Campaign.ps1 -Verify

$env:LIBRARY_PATH = '/opt/homebrew/opt/openssl@3/lib:/opt/homebrew/opt/brotli/lib'
dotnet publish -c Release -r osx-arm64 --self-contained true -o artifacts/osx-arm64-new-timespan --no-restore
./artifacts/osx-arm64-new-timespan/PwshAotLite --self-test
./artifacts/osx-arm64-new-timespan/PwshAotLite -Command 'New-TimeSpan -Days 1 -Hours 2 -Minutes 3 -Seconds 4 -Milliseconds 5'
./artifacts/osx-arm64-new-timespan/PwshAotLite -Command 'New-TimeSpan -Seconds 2 | Select-Object Value, TotalSeconds'
```

Fixture `30-new-timespan-components.ps1` captures upstream token/AST shape
for direct components, negative and fractional values, projection, and
deliberately deferred date/positional spellings. The review ledger is
[new-timespan-wave2.md](../reviews/new-timespan-wave2.md).

## Variances and reusable learnings

See `New-TimeSpan` in `port-variances.json`. The component path proves that a
source scalar BCL output can remain typed in the port and pass through the
existing closed adapter without generalizing `AotValue`. Typed DateTime input,
source parameter-set resolution, and duration-aware expression semantics need
their own review before they are added.
