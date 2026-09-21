# Get-FileHash port notes

## Status and source

**Complete for the current direct physical-filesystem Native AOT target.**

- Original: `PowerShell/src/Microsoft.PowerShell.Commands.Utility/commands/utility/GetHash.cs`, `GetFileHashCommand`, `HashCmdletBase`, and `FileHashInfo`.
- Generated contract: `GeneratedCmdletPorts.GetFileHash`.
- Target: `Pipeline.cs`: `GetFileHashCmdlet`, `SystemPhysicalFileResolver`, and `FileHashRecord`.

The generated descriptor supplies `Path`, `LiteralPath`/`PSPath`/`LP`, and
`Algorithm` from the original source. The port intentionally does not add
`InputStream` to the command descriptor: there is no typed stream or generic
pipeline binder yet, so accepting a string-shaped substitute would be false
compatibility.

## What transferred

- `HashCmdletBase.Algorithm` transfers as the source's case-insensitive,
  upper-case normalized `SHA1`, `SHA256`, `SHA384`, `SHA512`, or `MD5` set,
  with default `SHA256`.
- Each hashing branch is a direct static BCL call (`SHA*.HashData(Stream)` or
  `MD5.HashData(Stream)`), producing upper-case hexadecimal through
  `Convert.ToHexString`, exactly as source does.
- `Path` and `LiteralPath` retain their mutually exclusive parameter-set
  meaning. A source-default positional argument selects `Path`, while `LP`
  remains a generated alias for `LiteralPath`.
- Direct physical `Path` resolves an explicit file or terminal filename
  wildcard (`*`/`?`), and orders wildcard results ordinally for deterministic
  AOT output. `LiteralPath` passes wildcard characters through literally.
- Missing direct files yield a nonterminating `FileNotFound`; access and I/O
  failures use source-aligned `UnauthorizedAccessError` and `FileReadError`.
- `FileHashRecord` statically projects the source `FileHashInfo` fields:
  `Algorithm`, `Hash`, and `Path`.

## What did not transfer

- PowerShell `SessionState.Path` can resolve provider paths, provider-qualified
  paths, wildcarded directory components, and provider errors. This port is
  deliberately *physical filesystem only*, with wildcards in the filename
  component only. A future virtual-file service must own provider semantics.
- Source `InputStream` hashes a .NET `Stream` in `EndProcessing`. The current
  command language cannot materialize a typed stream or bind one from a
  pipeline, so `-InputStream` is explicitly unsupported rather than accepted
  as text.
- `FileHashInfo` is a normal typed AOT record, not a `PSObject` decorated by
  PowerShell's type and formatting system.
- The source's native provider wildcard engine is not imported. The target's
  limited physical matcher uses BCL terminal-name search semantics.

## Verification actually run

```powershell
dotnet build -c Release
dotnet run -c Release -- --self-test
dotnet run -c Release -- -Command 'Get-FileHash README.md -Algorithm SHA256 | Select-Object Algorithm, Hash, Path'
dotnet run -c Release -- -Command 'Get-FileHash -LiteralPath README.md -Algorithm md5'
dotnet run -c Release -- -Command 'Get-FileHash no-such-file.txt'
```

`SelfTest` creates disposable no-BOM fixtures and verifies the known SHA256 of
`abc`, MD5 of a literal filename containing `*`, algorithm normalization,
direct path result paths, deterministic wildcard ordering, generated `LP`
alias binding, positional `Path`, and the missing-file error ID.

Published macOS ARM64 verification passed:

```powershell
$env:LIBRARY_PATH = '/opt/homebrew/opt/openssl@3/lib:/opt/homebrew/opt/brotli/lib'
dotnet publish -c Release -r osx-arm64 --self-contained true -o artifacts/osx-arm64
./artifacts/osx-arm64/PwshAotLite --self-test
./artifacts/osx-arm64/PwshAotLite -Command 'Get-FileHash README.md -Algorithm SHA512 | Select-Object Algorithm, Hash, Path'
./artifacts/osx-arm64/PwshAotLite -Command 'Get-FileHash no-such-file.txt'
```

The native executable produced a SHA512 row for `README.md` and the expected
nonterminating `FileNotFound` error for the absent file.

## Variances

See `Get-FileHash` in [`../../port-variances.json`](../../port-variances.json):
`filehash-physical-paths`, `filehash-stream-binding`, `filehash-output-shape`,
and `filehash-errors`.

## Reusable learnings

1. A command can reuse direct source business logic when provider resolution is
   placed behind a small host service. The service boundary is the migration
   unit, not a string-to-path shortcut inside every cmdlet.
2. The source generator preserves competing position-zero parameter metadata,
   but the compact parser must select the source default parameter set. The
   `CmdletDescriptor.DefaultParameterName` hook is reusable for that case.
3. A known-content fixture must write UTF-8 without a BOM; otherwise a hash
   test validates the wrong bytes. This is a general rule for binary-sensitive
   port fixtures.

## Next action

Design a typed stream/pipeline input boundary together with a virtual-file
provider service before enabling `InputStream` or PowerShell provider paths.
