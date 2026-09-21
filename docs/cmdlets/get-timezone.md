# Get-TimeZone port notes

## Status and source

**Complete for the current Native AOT target**, with the explicitly bounded
wildcard and output policies below.

- Original: `src/Microsoft.PowerShell.Commands.Management/commands/management/TimeZoneCommands.cs`, `GetTimeZoneCommand` (lines 18–111).
- Generated contract: `GeneratedCmdletPorts.GetTimeZone`.
- Source contract: `PSCmdlet`; `Id[]` (`Id` parameter set), `ListAvailable`
  (`ListAvailable` parameter set), and positional/pipeline `Name[]` (`Name`
  parameter set); output is `TimeZoneInfo`.

## What transferred

- The three direct `ProcessRecord` paths transferred one-for-one: no parameter
  yields the host local zone; `-ListAvailable` yields the BCL system catalogue;
  `-Id` resolves each supplied identifier independently.
- `-Name` applies the source helper's policy: match `StandardName` or
  `DaylightName`, case-insensitively. It does not invent a display-name or ID
  matching rule.
- Missing IDs or names are non-terminating `TimeZoneNotFound` errors, allowing
  the remaining requested values to be processed.
- `TimeZoneInfo.ClearCachedData()` is preserved through `ITimeZoneCatalog.Refresh`.
- `TimeZoneRecord` carries `Id`, `DisplayName`, `StandardName`, `DaylightName`,
  `BaseUtcOffset`, and `SupportsDaylightSavingTime`; the service interface makes
  host zone data fixture-testable and Native-AOT-safe.

## What did not transfer

- `Name` has `ValueFromPipeline=true` in the original contract. The runner has
  no generic scalar/record binder yet, so direct arguments are implemented and
  cross-command pipeline binding is deferred.
- Original PowerShell uses `WildcardPattern`; the shared AOT matcher currently
  supports only case-insensitive `*` and `?`, not character classes, ranges, or
  escape semantics.
- The PowerShell output is a full `TimeZoneInfo` with ETS formatting. The target
  returns the typed projection above; this is intentional and not a claim of
  object-identity compatibility.
- Zone IDs are OS data: macOS/Linux usually expose IANA IDs and Windows exposes
  Windows IDs. The AOT port preserves BCL host behaviour rather than mapping
  identifiers across platforms.

## Verification

- Fixture coverage in `SelfTest` checks generated contract shape, refresh/local
  lifecycle, ID lookup, standard/daylight wildcard matching, non-terminating
  missing-ID errors, and parser/output columns.
- Native host smoke commands:

  ```powershell
  ./artifacts/osx-arm64/PwshAotLite -Command "Get-TimeZone | Select-Object Id, StandardName"
  ./artifacts/osx-arm64/PwshAotLite -Command "Get-TimeZone -ListAvailable | Select-Object Id, BaseUtcOffset"
  ```

## Variances

See `Get-TimeZone` in [`../../port-variances.json`](../../port-variances.json):
`timezone-catalog`, `timezone-name-wildcards`, `timezone-pipeline-binding`,
and `timezone-output-shape`.

## Reusable learnings

1. A BCL-backed catalogue should be injected as a small interface rather than
   embedded in a cmdlet; that preserves a direct source-body mapping and makes
   host/platform data testable.
2. The generated parameter contract tells us `Name` is a pipeline input, but it
   cannot create a scalar binder. Record that once as a shared-runtime gap,
   rather than adding another command-specific input path.
3. Platform-native identifiers belong in a host service; a future portability
   layer must be explicit about its mapping policy and error semantics.

## Next action

Upgrade the shared wildcard matcher and generic pipeline binder before claiming
full `-Name` compatibility. Neither is required for the direct AOT modes.
