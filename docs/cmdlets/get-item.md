# Get-Item port notes

## Status and source

**Integrated native subset.** The claim is limited to macOS-arm64,
captured-root, direct physical `Path` lookup
for one ordinary file or directory. It is not provider-engine, drive, wildcard,
credential, dynamic-parameter, pipeline, or general formatting support.

- Original: `src/Microsoft.PowerShell.Commands.Management/commands/management/Navigation.cs:1847-2003`, `GetItemCommand`.
- Generated contract: `GeneratedCmdletPorts.GetItem`.
- Shared target seam: `IPhysicalChildItemCatalog.GetDirectPhysicalItem` in `PhysicalChildItemCatalog.cs`.
- Review ledger: [get-item-wave3.md](../reviews/get-item-wave3.md).

## Port timing

| Field | Value |
| --- | --- |
| UTC work started | `2026-09-22T22:25:55.0487520Z` |
| UTC work ended | `2026-09-22T22:45:15.0000000Z` |
| Elapsed wall clock | `00:19:19.9512480` |
| Scope note | `Wave 3 captured-root direct physical file-or-directory lookup; shared Unix presentation; no provider, wildcard, dynamic parameter, or pipeline support.` |

The recorded interval includes investigation, implementation, independent
reviews, and verification for this bounded slice.

## What transferred

- `Path` remains generated metadata, including mandatory position zero. The
  executable descriptor is `GeneratedCmdletPorts.GetItem.CreateAotDescriptor("Path")`;
  no aliases or parameter rules were hand written.
- The direct filesystem lookup intent is preserved: `Get-Item <file>` and
  `Get-Item <directory>` each return exactly one physical item. In particular,
  the new catalog operation does **not** call child enumeration for a directory.
- `PhysicalChildItemRecord` and the Unix source table are shared with
  `Get-ChildItem`. `PhysicalItemPresentation` owns the static
  `childrenWithUnixStat` extraction, so a second command cannot fork its own
  headers, columns, ordering, widths, grouping, or timestamp renderer.

## Upstream reuse evidence matrix

| Surface | Exact upstream evidence | Reuse decision | AOT target / contract | Why direct copy is or is not possible | Variance ID | Verification |
| --- | --- | --- | --- | --- | --- | --- |
| Command metadata / `Path` | `Navigation.cs:1847-1907`; generated `GeneratedCmdletPorts.GetItem` | adapted | Generated `Path`-only descriptor | Attribute/binder source is extracted at build time; no handwritten metadata. | `getitem-generated-contract` | Generated-contract and parser/binder self-tests |
| Lifecycle and provider lookup | `Navigation.cs:1968-2003` (`ProcessRecord`, `InvokeProvider.Item.Get`) | shared-substrate replacement | `AotCmdletBase` + `IPhysicalChildItemCatalog.GetDirectPhysicalItem` | `InvokeProvider`, `SessionState`, provider context, dynamic parameters, and `PSObject` cannot enter this AOT binary. The existing child catalog was extended with one reusable item operation rather than copying lookup into the cmdlet. | `getitem-direct-physical-provider-boundary` | Direct file/directory, diagnostics, cancellation, and link tests |
| Filesystem item facts | `FileSystemProvider.cs:221-230,1253-1285` | shared-substrate replacement | Existing descriptor-safe `PhysicalChildItem`/`PhysicalChildItemRecord` | Upstream returns `FileInfo`/`DirectoryInfo` via dynamic providers and ETS. The finite physical record retains only already-reviewed facts. | `getitem-shared-closed-physical-record` | File/directory record assertions and native oracle |
| Unix facts | `CorePsPlatform.cs:665-748`; `TypeTable_Types_Ps1Xml.cs:9090-9127` | reused shared-substrate replacement | Existing `MacOsPhysicalMetadata` descriptor walk | Upstream private engine-native bridge and script properties cannot be copied. Reusing the reviewed macOS arm64 no-follow bridge prevents another OS metadata implementation. | `childitem-macos-unixstat-bridge` | Existing and direct-item no-follow/ABI tests |
| Default table | `FileSystem_format_ps1xml.cs:46-69` (`childrenWithUnixStat`) | static-data extracted / reused | `PhysicalItemPresentation.UnixDefaultTable` | Dynamic type/format discovery is outside scope. One source-backed immutable descriptor is shared; no columns are newly chosen. | `getitem-unix-format-static-extraction` | `Test-GetItemFormatCompatibility.ps1` normal/native file+directory oracle |

## Deferred / fail-closed behavior

- `LiteralPath`/`PSPath`/`LP`, `Force`, `Filter`, `Include`, `Exclude`,
  credentials, provider drives/paths, wildcard expansion, dynamic parameters,
  pipeline binding, and provider-specific output are absent from the active
  descriptor or rejected by the direct catalog. Unsupported descriptor
  parameters fail at the shared binder (`AOT2002`); provider-qualified and
  wildcard direct paths fail with `AOT6201` and `AOT6202`.
- The source-mandatory `Path` remains in generated metadata. The small static
  binder does not synthesize PowerShell's interactive mandatory-parameter
  prompt, so the adapter explicitly fails closed with `AOT6211` rather than
  silently producing an empty result when no direct path is supplied. This is
  a deliberate, second-pass variance recorded as
  `getitem-mandatory-path-noninteractive`; `Pipeline.cs` snapshots the plain
  terminal diagnostic and its command source span.
- Hidden items, every symbolic-link component, unreadable paths, unsupported
  physical types, and unreviewed platforms retain the catalog's explicit
  `AOT6204`–`AOT6210` boundary. There is no fallback to `FileInfo`, provider
  dispatch, or link-following BCL access.

## Verification

Oracle identities: installed stock `pwsh` `7.6.6`; target source is the
`275b920` base plus the reviewed Get-Item working-tree changes; fresh native
artifact `artifacts/osx-arm64-get-item-release-20260923/PwshAotLite`, SHA-256
`43a405f7104498d5dd010f8bbeb23dbc8c933889e00e62f3c9395b4713b36438`.

```powershell
dotnet build -c Release --no-restore
dotnet run -c Release --no-build -- --self-test
pwsh -NoProfile -File ./tools/Test-ParserReuseGuard.ps1
pwsh -NoProfile -File ./tools/Export-PwshParserBaseline.ps1 -Verify
pwsh -NoProfile -File ./tools/Export-Phase10Campaign.ps1 -Verify

$env:LIBRARY_PATH = '/opt/homebrew/opt/openssl@3/lib:/opt/homebrew/opt/brotli/lib'
dotnet publish -c Release -r osx-arm64 --self-contained true -o artifacts/osx-arm64-get-item --no-restore
./artifacts/osx-arm64-get-item/PwshAotLite --self-test
./artifacts/osx-arm64-get-item/PwshAotLite -Command 'Get-Item -Path ./README.md'
./artifacts/osx-arm64-get-item/PwshAotLite -Command 'Get-Item -Path .'
pwsh -NoProfile -File ./tools/Test-GetItemFormatCompatibility.ps1 -NativePwshPath ./artifacts/osx-arm64-get-item/PwshAotLite
```

`Test-GetItemFormatCompatibility.ps1` creates one file, one directory with a
nested child, and a linked ancestor. It compares stock `pwsh` PlainText and
Native AOT tables exactly apart from ANSI/newline transport. It separately
asserts that the nested child is absent from `Get-Item <directory>` and that a
path through the linked ancestor fails with `AOT6205`.

The managed `--self-test` also checks the missing-Path adapter boundary with
the `getitem-missing-path.ps1` plain-terminal snapshot: the stable `AOT6211`
diagnostic points at the whole `Get-Item` command (`1:1-1:9`) and explains the
noninteractive direct-path requirement.

## Variances and reusable learnings

`GetDirectPhysicalItem` is the reusable result. It is adjacent to
`GetImmediateChildren`, shares path acquisition/metadata logic, and has a
different result cardinality. `Test-Path`, `Resolve-Path`, and metadata ports
can consume it without duplicating provider replacement logic. It must not be
broadened to emulate provider dispatch.
