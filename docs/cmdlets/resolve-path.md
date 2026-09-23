# Resolve-Path port notes

## Status and source

**Integrated Wave 3 bounded slice.** This is a macOS-arm64 direct physical
`Path` adapter, not PowerShell provider or session-path compatibility.

- Original: `src/Microsoft.PowerShell.Commands.Management/commands/management/ResolvePathCommand.cs`, `ResolvePathCommand` over `CoreCommandWithCredentialsBase`.
- Generated contract: `GeneratedCmdletPorts.ResolvePath`.
- Target: `PhysicalChildItemCatalog.cs` (`ResolveExistingDirectPhysicalPath`, `DirectPhysicalPathResolution`, `DirectPhysicalPathRecord`, and `ResolvePathCmdlet`), `PipelineValueAdapter.cs`, and `ResolvePathPresentation`.
- Review ledger: [resolve-path-wave3.md](../reviews/resolve-path-wave3.md).

## Port timing

| Field | Value |
| --- | --- |
| UTC work started | `2026-09-23T07:49:52.419Z` |
| UTC work ended | `2026-09-23T08:31:39.0000000Z` |
| Elapsed wall clock | `00:41:46.5810000` |
| Scope note | `Wave 3 direct existing physical resolution; closed path outcome and static Path view.` |

## What transferred

- The only executable descriptor is
  `GeneratedCmdletPorts.ResolvePath.CreateAotDescriptor("Path")`. Its source
  position-zero mandatory `Path` metadata stays generated; no parameter or
  alias is copied by hand.
- The upstream record loop has one honest static analogue: each bound direct
  path becomes `Resolved(DirectPhysicalPathRecord)`, `Missing`, or `Rejected`.
  Only `Resolved` produces output. Missing/rejected values preserve their
  catalog diagnostic and do not stop later values.
- `IPhysicalChildItemCatalog.ResolveExistingDirectPhysicalPath` reuses the
  existing captured root and the sole descriptor-based all-component no-follow
  acquisition. It neither calls `GetDirectPhysicalItem`/`TryDescribe`, nor
  enumerates, calls `File.Exists`, or enters a provider.
- `DirectPhysicalPathRecord` contains only `Path`. Its explicit
  `PipelineValueAdapter` entry prevents a fallback CLR object/property bridge.
- `ResolvePathPresentation.PathTable` is a static one-column table. Its
  observed leading and final blank lines belong to that layout, not the
  generic renderer.

## Upstream reuse evidence matrix

| Surface | Exact upstream evidence | Reuse decision | AOT target / contract | Variance ID |
| --- | --- | --- | --- | --- |
| Metadata | `ResolvePathCommand.cs:18-105`; generated `GeneratedCmdletPorts.ResolvePath` | adapted | generated `Path` descriptor only | `resolvepath-generated-path-contract` |
| Resolution lifecycle | `:166-246`, `SessionState.Path.GetResolvedPSPathFromPSPath` | shared-substrate replacement | catalog closed direct physical resolution | `resolvepath-direct-existing-physical-resolution` |
| Output | `:220-246`, `PathInfo`/relative provider output | bounded replacement | `DirectPhysicalPathRecord(Path)` only | `resolvepath-closed-path-record-static-layout` |
| Default view | stock terminal observation, controlled direct paths | static data | `ResolvePathPresentation.PathTable` | `resolvepath-closed-path-record-static-layout` |

## Deferred / fail-closed behavior

`LiteralPath`/`PSPath`/`LP`, wildcards, provider/drives, `-Relative`,
`-RelativeBasePath`, `-Force`, transactions, provider ErrorRecords, and
pipeline/property-name binding remain deferred. The binder rejects inactive
parameters with `AOT2002`; provider/wildcard/link/hidden/access boundaries use
the existing source-spanned catalog diagnostics. Omitted `Path` gets AOT6211;
an explicitly empty path is distinct and gets AOT6213; whitespace is literal,
not root coercion.

## Verification

The immutable release implementation source is commit
`58060bfea4a872f6b7070a1469d0746218ff561f`. Fresh Native AOT artifact
`artifacts/osx-arm64-resolve-path-release-20260923-0833/PwshAotLite` passed
`--self-test` and the strict stock/native oracle; its SHA-256 is
`f6a7de65202c85b1c37967532238708decb316aea5c87775bd31e7d5e9d0bff9`.
The release artifact is intentionally not tracked in Git.

```powershell
dotnet build -c Release --no-restore
dotnet run -c Release --no-build -- --self-test
pwsh -NoProfile -File ./tools/Test-ParserReuseGuard.ps1
pwsh -NoProfile -File ./tools/Export-PwshParserBaseline.ps1 -Verify
pwsh -NoProfile -File ./tools/Export-Phase10Campaign.ps1 -Verify
pwsh -NoProfile -File ./tools/Export-Phase10BatchQueue.ps1 -Verify

$env:LIBRARY_PATH = '/opt/homebrew/opt/openssl@3/lib:/opt/homebrew/opt/brotli/lib'
dotnet publish -c Release -r osx-arm64 --self-contained true -o artifacts/osx-arm64-resolve-path --no-restore
./artifacts/osx-arm64-resolve-path/PwshAotLite --self-test
pwsh -NoProfile -File ./tools/Test-ResolvePathCompatibility.ps1 -NativePwshPath ./artifacts/osx-arm64-resolve-path/PwshAotLite
```

The grammar fixture `34-resolve-path-direct-physical.ps1` is intentionally an
upstream-parser baseline only: it proves admitted positional/named syntax and
deferred parameter syntax without creating a grammar.

## Variances and reusable learnings

This command establishes a reusable **resolution fact**, distinct from an
item record or a Boolean probe. Future direct-path ports may use the catalog
outcome when they need a canonical existing path; they must not recreate
provider/glob/session behavior. All current decisions are in
[`port-variances.json`](../../port-variances.json) under `Resolve-Path`.
