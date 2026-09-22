# Get-ChildItem port notes

## Status and source

**Complete for the bounded Wave 3 captured-root macOS-arm64 direct-physical
filesystem slice.** This is not a complete PowerShell provider or formatter
compatibility claim: it covers only the documented default/positional/`-Path`
immediate-child behavior and fixed Unix default view.

- Original: `src/Microsoft.PowerShell.Commands.Management/commands/management/GetChildrenCommand.cs`, `GetChildItemCommand` over `CoreCommandBase`.
- Generated contract: `GeneratedCmdletPorts.GetChildItem`.
- Target: `PhysicalChildItemCatalog.cs`: `IPhysicalChildItemCatalog`, `SystemPhysicalChildItemCatalog`, `MacOsPhysicalMetadata`, and `PhysicalChildItemRecord`; `Pipeline.cs`: `GetChildItemCmdlet` and its closed `AotTableLayout`.
- Review ledger: [get-childitem-wave3.md](../reviews/get-childitem-wave3.md).

## Port timing

| Field | Value |
| --- | --- |
| UTC work started | `2026-09-22T09:59:17.2280730Z` |
| UTC work ended | `2026-09-22T11:09:30.2144590Z` |
| Elapsed wall clock | `01:10:12` |
| Scope note | `Wave 3 direct physical path/default-dot immediate-child listing slice.` |

The start/end are shared with the aggregate timing ledger. The elapsed value
includes review and build waiting, not just active coding time.

## What transferred

- The generated `Path` contract is the sole active descriptor parameter. It
  retains source position zero and accepts `Get-ChildItem <path>` and
  `Get-ChildItem -Path <path>`.
- With no path, the source's default-dot intent maps to an immutable discovery
  root captured when the host substrate is composed. No command reads an
  ambient current directory during execution.
- An exact direct physical file emits one closed `PhysicalChildItemRecord`.
  An exact direct directory follows `FileSystemProvider.Dir`'s in-scope
  ordering: directories first, then files, with each collection sorted by
  current-culture case-insensitive name. This replaced the original invented
  ordinal full-path order after the controlled oracle exposed the mismatch.
- The default direct terminal view is a static transcription of upstream's
  Unix `childrenWithUnixStat` table: `UnixMode`, `User`, `Group`,
  `LastWriteTime`, `Size`, and `Name`, grouped as `Directory: <ParentPath>`.
  `LastWriteTime` follows the upstream `'{0:d} {0:HH}:{0:mm}'` projection.
- `MacOsPhysicalMetadata` supplies only mode, owner, group, size, and
  last-write facts required by that source view. The reviewed macOS-arm64
  bridge performs an all-components descriptor walk from `/`: each component
  is observed with `fstatat(..., AT_SYMLINK_NOFOLLOW)` and acquired from its
  already-acquired parent with `openat(..., O_NOFOLLOW)`, before descriptor
  `fstat` supplies the final facts. Thus a link in an ancestor directory is
  rejected as rigorously as a final link; `Path.GetFullPath` is lexical only.
  It never classifies with `File.GetAttributes` or re-opens through a
  link-following BCL call. `fdopendir`/`readdir` enumerate from the already
  no-follow-acquired directory descriptor; this remains neither a provider,
  object adapter, nor dynamic type/format lookup.
- Every direct-path failure is a typed, source-spanned nonterminating
  diagnostic (`AOT6201` through `AOT6210`) rather than a provider exception or
  raw BCL error.
- The catalog observes the host-owned stop token before physical work, for
  every directory entry and relative stat, and in the directories-first/files
  sort comparator. It owns no cancellation source or asynchronous work.

## Upstream reuse evidence matrix

| Surface | Exact upstream evidence | Reuse decision | AOT target / contract | Why direct copy is or is not possible | Variance ID | Verification |
| --- | --- | --- | --- | --- | --- | --- |
| Command metadata and `Path` | `GetChildrenCommand.cs:35-48`; generated `GeneratedCmdletPorts.GetChildItem` | adapted | Generated opt-in descriptor retains only `Path` | The source class requires `PSCmdlet`, provider binding, and pipeline input. Generated source metadata remains the authority; unsupported source parameters fail at the shared binder. | `childitem-path-subset-and-provider-rejection` | Self-test binding/negative cases; parser baseline |
| Lifecycle/provider call | `GetChildrenCommand.cs`; `CoreCommandBase`; `FileSystemProvider.GetChildItems` at `FileSystemProvider.cs:1411-1417` | shared-substrate replacement | `AotCmdletBase` plus immutable `IPhysicalChildItemCatalog` | `SessionState` providers, dynamic parameters, drives, and ETS cannot enter the native AOT boundary. The existing `IPhysicalFileResolver` has a different reviewed `Get-FileHash` policy and was not widened. | `childitem-captured-direct-physical-catalog`, `childitem-filehash-policy-separation` | Catalog fixtures, native direct-path smoke |
| Immediate ordering | `FileSystemProvider.Dir` at `FileSystemProvider.cs:1644-1656,1668-1674` | adapted | Direct catalog enumerates directories then files and sorts each with `StringComparer.CurrentCultureIgnoreCase` | The source provider loop cannot be copied without its provider context; its physical enumeration/order policy can be transferred mechanically. | `childitem-captured-direct-physical-catalog` | Controlled pwsh/native format oracle |
| Unix display facts | `TypeTable_Types_Ps1Xml.cs:9090-9127`; mode/name logic `CorePsPlatform.cs:665-748` | shared-substrate replacement | Closed `MacOsPhysicalMetadata` fields: UnixMode, User, Group, LastWriteTime, Size | The source calls its private `libpsl-native` bridge and attaches script properties to `FileSystemInfo`; the target uses fixed reviewed macOS-arm64 `lstat`/no-follow descriptor/relative-stat ABI calls only and exposes no CLR object or script property. | `childitem-macos-unixstat-bridge` | Self-test no-follow/cancellation/ABI-gate assertions; Native AOT oracle |
| `UnixMode` | `CorePsPlatform.cs:665-707`; `FileSystem_format_ps1xml.cs:49,59` | adapted | Closed 10-character POSIX mode formatter | The source `CommonStat` type is engine-private; its ordinary file/directory permission-bit algorithm transfers without dynamic execution. Every path component is rejected if it is a symlink before the formatter. | `childitem-macos-unixstat-bridge` | Oracle fixture mode rows |
| `User` / `Group` | `TypeTable_Types_Ps1Xml.cs:9105-9119`; `CorePsPlatform.cs:715-745`; formatter `FileSystem_format_ps1xml.cs:50-51,60-61` | adapted | UID/GID name lookups with static cache | Direct calls replace upstream private native wrappers; failure is not replaced by a number or empty string and instead fails closed. | `childitem-macos-unixstat-bridge` | Oracle fixture user/group rows |
| `LastWriteTime` | `FileSystem_format_ps1xml.cs:52-55,62` | static-data extracted | Typed local `LastWriteTime` from the acquired stat plus fixed table value format | The upstream script block and dynamic formatter cannot run; its literal culture-aware format string and declared header width are compiled as data. | `childitem-unix-format-static-extraction` | Oracle fixture fixed timestamp |
| `Size` | `TypeTable_Types_Ps1Xml.cs:9121-9127`; `FileSystem_format_ps1xml.cs:56,63` | adapted | `stat.st_size` closed `long` field for files and directories | Source obtains `UnixStat.Size` through private engine interop. The static macOS bridge is the narrow fact source. | `childitem-macos-unixstat-bridge` | Oracle fixture file and directory size rows |
| `Name` / grouping / table headers | `FileSystemProvider.NameString` at `FileSystemProvider.cs:1952-1981`; `FileSystem_format_ps1xml.cs:46-65` | static-data extracted | Fixed `AotTableLayout`: source headers/alignments/widths, one-space TableControl separator, and `Directory` parent grouping | Source `NameString` normally relies on PSStyle and link/type data. This direct non-link slice emits the plain `Name` equivalent and the proof forces normal pwsh to `PlainText`; host-colored `NameString` is deferred. | `childitem-unix-format-static-extraction` | Exact geometry oracle (ANSI/newline transport normalization only) |

## What did not transfer

- `LiteralPath` and generated aliases `PSPath` / `LP` are intentionally absent
  from the active descriptor. They fail at the shared binder with `AOT2002`;
  accepting them before a literal/path policy is designed would be false
  compatibility.
- Provider paths / drives, dynamic parameters, wildcard expansion, filters,
  include/exclude, recurse/depth, `Name`, `Force`, hidden entries, symbolic
  link traversal, pipeline input, and provider-specific output objects remain
  out of scope. Provider-qualified syntax is rejected before BCL path handling;
  wildcard paths are rejected; hidden and link entries are neither enumerated
  nor traversed.
- This does not reuse or widen `IPhysicalFileResolver`: `Get-FileHash` keeps
  its independently reviewed terminal-wildcard contract. The new child catalog
  is a distinct, fixed host capability with its own captured-root authority.
- The output is a closed AOT record, not `FileInfo`, `DirectoryInfo`,
  `PSObject`, ETS formatting data, `SessionState`, or a runspace/provider.
- Linux, Windows, unknown platforms, and macOS architectures other than the
  reviewed arm64 ABI reject this Unix display slice with `AOT6209`; a no-follow
  acquisition or metadata bridge failure rejects the item with `AOT6210`. They
  are deliberately not rendered with guessed user/group fields. Colorized
  `NameString`, links, provider grouping metadata, and terminal-width behavior
  beyond the fixed source descriptor remain deferred.

## Verification

```powershell
dotnet build -c Release --no-restore
dotnet run -c Release --no-build -- --self-test
pwsh -NoProfile -File ./tools/Test-ParserReuseGuard.ps1
pwsh -NoProfile -File ./tools/Export-PwshParserBaseline.ps1 -Verify
pwsh -NoProfile -File ./tools/Export-Phase10Campaign.ps1 -Verify

$env:LIBRARY_PATH = '/opt/homebrew/opt/openssl@3/lib:/opt/homebrew/opt/brotli/lib'
dotnet publish -c Release -r osx-arm64 --self-contained true -o artifacts/osx-arm64-get-childitem --no-restore
./artifacts/osx-arm64-get-childitem/PwshAotLite --self-test
./artifacts/osx-arm64-get-childitem/PwshAotLite -Command 'Get-ChildItem -Path .'
pwsh -NoProfile -File ./tools/Test-GetChildItemFormatCompatibility.ps1 -NativePwshPath ./artifacts/osx-arm64-get-childitem/PwshAotLite
```

Fixture `32-get-childitem-direct-paths.ps1` and its checked parser baseline
capture direct positional/named paths, source-declared but deferred literal
path, and recurse syntax without introducing a new grammar.

## Variances and reusable learnings

See the `Get-ChildItem` entry in `port-variances.json`. The key reusable
learnings are that child enumeration must be a separate immutable capability,
and that upstream format/type data must be treated as source material—not a
license to pick convenience columns. The controlled normal/native oracle is
`tools/Test-GetChildItemFormatCompatibility.ps1`; it creates a fixed local
fixture, captures both raw default tables, and compares the directory header,
headers, Unix metadata values, row order, and every table column start. Its
only normalization removes ANSI and normalizes newline/terminal-trailing-blank
transport. It does not
mask padding, names, timestamps, sizes, ownership, modes, grouping paths, or
order. It also creates a direct child behind a symlinked ancestor and requires
the native command to emit `AOT6205` without projecting that child.

## Format-contract status

The macOS-arm64 direct non-link fixture now matches normal pwsh's default Unix
view after ANSI/newline terminal transport normalization only. The
remaining intentional differences are scope boundaries, not a second format
engine: only the one compiled table descriptor exists, it applies only to a
direct untransformed result, and transformed pipeline shapes retain the shared
generic table renderer.

| Surface compared | Stock `pwsh` command/version | Native command/artifact/RID | Fixture/input | Normalization | Result / variance ID |
| --- | --- | --- | --- | --- | --- |
| Unix default view, grouping, headers, facts, order, and raw table geometry | `pwsh -NoProfile`, default `Get-ChildItem -LiteralPath <fixture>` with `PSStyle.OutputRendering='PlainText'` | `PwshAotLite -Command "Get-ChildItem -Path <fixture>"`, reviewed `osx-arm64` | Three fixed-mode/timestamp direct children | ANSI/newline and terminal trailing blank transport only | Match required; `childitem-unix-format-static-extraction`, `childitem-macos-unixstat-bridge` |

## Next action

This slice is integrated. Reuse its captured-root direct-physical catalog and
static Unix format descriptor only after checking the upstream reuse matrix;
the remaining provider, drive, wildcard, recurse, link, and dynamic-format
surfaces remain explicit future work.
