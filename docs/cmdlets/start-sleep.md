# Start-Sleep port notes

## Status and source

**Integrated Wave 2 generated `-Milliseconds` calibration slice; independent
review is recorded in the linked PASS ledger.**
The adapter has no output and consumes the one fixed `IAotDelay` host
capability. It does not use the PowerShell SDK, `PSObject`, reflection,
runtime compilation/loading, a runspace, `Thread.Sleep`, tasks, timers, or a
scheduler.

- Original: `src/Microsoft.PowerShell.Commands.Utility/commands/utility/StartSleepCommand.cs`
- Generated contract: `GeneratedCmdletPorts.StartSleep`
- Target: `Pipeline.cs`: `StartSleepCmdlet`; `HostSubstrate.cs`: `IAotDelay`

## What transferred

- Generated `-Milliseconds <int>` is mandatory for the admitted source
  parameter set; the generated `-ms` alias binds through the same descriptor.
- The value is one invariant non-negative integer in the source-declared
  `0..Int32.MaxValue` range. The adapter calls the injected delay exactly once
  and produces no pipeline record or stream event.
- `CancellationTokenDelay` waits on the execution context's existing,
  host-owned cancellation-token wait handle. Cancellation is cooperative and
  remains the existing host control-flow/exit-130 contract; this slice does
  not claim console Ctrl+C wiring.

`AOT3007` names a missing admitted duration, `AOT3008` invalid invariant
integer text, and `AOT3009` a negative or too-large integer. The shared binder
continues to provide source-spanned `AOT2003` duplicate, `AOT2004` missing
value, `AOT2002` deferred named-parameter, and `AOT2005` positional failures.

## Deliberate boundary

The source `Seconds` default positional/pipeline parameter set and `Duration`
(`-ts`) parameter set are absent from the executable descriptor. Although the
admitted source `Milliseconds` parameter also declares
`ValueFromPipelineByPropertyName`, that property-name binding is not admitted:
any pipeline stage is rejected earlier by the structural lowerer. Named
`-Seconds`, named `-Duration` and `-ts`, positional arguments, command aliases
such as `sleep`, and all typed/property-name pipeline input fail closed. Supporting seconds needs a reviewed floating-point conversion
and overflow policy; duration needs an explicit typed `TimeSpan` input
contract; property-name binding needs a shared typed-input contract. Neither
belongs in a wait adapter.

The target does not reproduce the upstream command's private `ManualResetEvent`
and `StopProcessing` synchronization arrangement. The shared execution kernel
already owns lifecycle stopping, and the host token wakes the one bounded local
wait. This is a replacement of the engine-bound implementation detail, not a
generic scheduling capability.

## Verification

```powershell
dotnet build -c Release --no-restore
dotnet run -c Release --no-build -- --self-test
pwsh -NoProfile -File ./tools/Test-ParserReuseGuard.ps1
pwsh -NoProfile -File ./tools/Export-PwshParserBaseline.ps1 -Verify
pwsh -NoProfile -File ./tools/Export-Phase10Campaign.ps1 -Verify

$env:LIBRARY_PATH = '/opt/homebrew/opt/openssl@3/lib:/opt/homebrew/opt/brotli/lib'
dotnet publish -c Release -r osx-arm64 --self-contained true -o artifacts/osx-arm64-start-sleep --no-restore
./artifacts/osx-arm64-start-sleep/PwshAotLite --self-test
./artifacts/osx-arm64-start-sleep/PwshAotLite -Command 'Start-Sleep -Milliseconds 0'
./artifacts/osx-arm64-start-sleep/PwshAotLite -Command 'Start-Sleep -ms 1'
```

Fixture `31-start-sleep-milliseconds.ps1` captures the upstream parser shape
for the admitted name/alias and deferred or invalid forms. The review ledger
is [start-sleep-wave2.md](../reviews/start-sleep-wave2.md).

## Variances and reusable learnings

See `Start-Sleep` in `port-variances.json`. This slice establishes a narrow
host-owned cancellable-delay seam reusable only by a later port with the same
bounded elapsed-time authority; it must not expand into a timer/scheduler or
general asynchronous execution service.
