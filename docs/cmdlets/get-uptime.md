# Get-Uptime port notes

## Status and source

**Complete for the current Native-AOT target.**

- Original: [`GetUptime.cs`](../../../../PowerShell/src/Microsoft.PowerShell.Commands.Utility/commands/utility/GetUptime.cs)
- Original base: `PSCmdlet`
- Generated contract: `GeneratedCmdletPorts.GetUptime` (one `Since` switch)
- AOT port: [`Pipeline.cs`](../../Pipeline.cs), `GetUptimeCmdlet` and `UptimeRecord`

## What transferred

The original `ProcessRecord` body transfers directly to static .NET code:

- Reject a platform without `Stopwatch.IsHighResolution`.
- Derive uptime from `Stopwatch.GetTimestamp()` and `Stopwatch.Frequency`.
- Emit a duration normally, or current local time minus that duration for
  `-Since`.
- Bind `-Since` through generated parameter metadata, not a handwritten alias
  table.

This is the first port that does not produce a process record. `UptimeRecord`
is therefore a deliberately typed scalar output, with default `Value` display
and selectable `Uptime`/`Since` fields. The shared `IAotCmdlet.DefaultColumns`
contract lets each cmdlet declare its natural output without relying on a
synthetic process `Name`.

## What did not transfer unchanged

- PowerShell's rich formatting of `TimeSpan` and `DateTime` is replaced by a
  one-column invariant text table. The emitted pipeline data remains typed;
  detailed formatting is a shared presentation backlog, not cmdlet logic.
- `InternalTestHooks.StopwatchIsNotHighResolution` is a PowerShell test hook.
  It has no production counterpart in the AOT port; platform behavior is
  tested at the `Stopwatch.IsHighResolution` boundary.
- The original terminating `ErrorRecord` becomes a `ScriptException` with a
  stable message. No `PSObject`, session state, or dynamic execution is used.

## Verification

Fixture tests in `SelfTest.Run` cover the normal and `-Since` paths and parser
binding. The following commands have been run against the managed release
build:

```powershell
dotnet build -c Release
dotnet run -c Release -- --self-test
dotnet run -c Release -- -Command "Get-Uptime"
dotnet run -c Release -- -Command "Get-Uptime -Since | Select-Object Since"
```

The macOS ARM64 Native-AOT artifact was then published and exercised:

```powershell
$env:LIBRARY_PATH = '/opt/homebrew/opt/openssl@3/lib:/opt/homebrew/opt/brotli/lib'
dotnet publish -c Release -r osx-arm64 --self-contained true -o artifacts/osx-arm64
./artifacts/osx-arm64/PwshAotLite --self-test
./artifacts/osx-arm64/PwshAotLite -Command "Get-Uptime -Since | Select-Object Since"
```

## Variances

See command `Get-Uptime` in
[`port-variances.json`](../../port-variances.json): typed scalar output is a
shared runtime replacement; PowerShell formatting and its internal test hook
are explicit subsets/non-transfers.

## Reusable learnings

1. `DefaultColumns` belongs on the static cmdlet contract, not on a process
   special case in the parser.
2. Scalar output needs a typed record even when its renderer is deliberately
   modest; inventing process fields would make later generic pipeline work
   harder.
3. Inherited manifest blockers must be checked against the actual command
   body. This port did not require `SessionState`, `ShouldProcess`, or dynamic
   binding.

## Next action

Use the scalar-output boundary for `Get-UICulture`, then introduce a typed
culture service rather than hardcoding host state in the cmdlet.
