# Get-Date port notes

## Status and source

**Complete for the current direct-invocation AOT target; pipeline binding is deferred.** The port is a static BCL implementation with no PowerShell SDK, reflection, runtime compilation, or dynamic loading.

- Original: `PowerShell/src/Microsoft.PowerShell.Commands.Utility/commands/utility/GetDateCommand.cs`
- Generated contract: `GeneratedCmdletPorts.GetDate`, emitted by `PwshAotPortGenerator`
- Target: `Pipeline.cs`: `GetDateCmdlet`, `DateRecord`, `TextRecord`, and injectable `IClock`

The generated descriptor supplies the command name, parameter names, aliases, shapes, and parameter-set declarations. The AOT port deliberately enables all public `Get-Date` parameters rather than accepting an incomplete surface silently.

## What transferred

- `-Date` (including `-LastWriteTime`) and `-UnixTimeSeconds` (including `-UnixTime`) select the source time. Unix time uses the source’s inclusive range and `DateTimeOffset.FromUnixTimeSeconds`.
- `-Year`, `-Month`, `-Day`, `-Hour`, `-Minute`, `-Second`, and `-Millisecond` retain source order and offset-based `DateTime` mutation, including BCL calendar errors for impossible result dates.
- `-AsUTC`, `-DisplayHint`, standard .NET `-Format`, and `FileDate`, `FileDateUniversal`, `FileDateTime`, and `FileDateTimeUniversal` transfer directly.
- `-UFormat` is a static copy of the source’s fixed strftime-style mapping. Quoted pipes within the format are kept as command data by the pipeline splitter.
- Formatting branches emit `TextRecord`, matching the source’s string output. The unformatted branch emits `DateRecord` with a `Date`/`Time`/`DateTime` display policy.

## What did not transfer unchanged

PowerShell wraps an unformatted `DateTime` in a `PSObject` and attaches an ETS `DisplayHint` note property. The Native-AOT target uses typed `DateRecord` instead. The compact runner displays it in round-trip (`O`) form rather than importing PowerShell’s format-data system.

`-Date` input is direct arguments only. Source binding from pipeline by value/property name is deferred until a generic typed binder exists. It is rejected rather than ignored.

Empty `-UFormat` and a trailing `%` produce stable `ScriptException` errors. The source reaches an index error in those malformed cases; this is an intentional error-quality variance.

## Verification actually run

```powershell
dotnet build -c Release
dotnet run -c Release -- --self-test
dotnet run -c Release -- -Command "Get-Date -UnixTime 0 -AsUTC -Format FileDateTimeUniversal"
dotnet run -c Release -- -Command "Get-Date -Date 2024-02-29T12:34:56 -Format FileDate"
```

The self-test uses an injected clock and covers generated aliases/parameter metadata, component mutation, Unix time, UTC, FileDateTimeUniversal, UFormat (`%Y`, `%j`, `%V`), typed hints, and quoted UFormat pipeline splitting. Native AOT verification is recorded after the publish run below.

Published macOS ARM64 verification passed:

```powershell
$env:LIBRARY_PATH = '/opt/homebrew/opt/openssl@3/lib:/opt/homebrew/opt/brotli/lib'
dotnet publish -c Release -r osx-arm64 --self-contained true -o artifacts/osx-arm64
./artifacts/osx-arm64/PwshAotLite --self-test
./artifacts/osx-arm64/PwshAotLite -Command 'Get-Date -UnixTime 0 -AsUTC -Format FileDateTimeUniversal'
```

It produced `19700101T0000000000Z`; the published binary also produced `2024|02|060` from quoted `'+%Y|%m|%j'` UFormat input.

## Variances

See `Get-Date` in `port-variances.json`:

- `date-display-hint-record` — ETS note property becomes typed `DateRecord`.
- `date-formatting` — `StringUtil` becomes direct static BCL formatting/UFormat mapping.
- `date-pipeline-binding` — deferred shared generic binder.
- `date-invalid-uformat` — stable explicit malformed-format errors.

## Reusable learnings

1. Generated contracts are sufficient to build descriptors, but parameter-set declarations need deliberate port enforcement until a shared binder exists. This port rejects Date/UnixTime and Format/UFormat conflicts.
2. `IClock` makes `DateTime.Now` deterministic for fixtures. Future time-sensitive ports should reuse it.
3. Typed scalar records are a better AOT replacement than faking process objects or reintroducing `PSObject`.

## Next action

Build the generic typed pipeline binder, then enable the generated `ValueFromPipeline` and `ValueFromPipelineByPropertyName` contract for `Date`.
