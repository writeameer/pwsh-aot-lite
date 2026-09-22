# Get-Item port notes

## Status and source

**In progress.** This is a bounded Wave 3 direct physical-item slice, not a
provider-engine compatibility claim.

- Original: `src/Microsoft.PowerShell.Commands.Management/commands/management/Navigation.cs:1847-2003`, `GetItemCommand` over `CoreCommandWithCredentialsBase`.
- Generated contract: `GeneratedCmdletPorts.GetItem`.
- Target: planned `IPhysicalChildItemCatalog.GetDirectPhysicalItem` shared capability and a `GetItemCmdlet` adapter; no runtime implementation exists yet.
- Review ledger: planned `docs/reviews/get-item-wave3.md`.

## Port timing

| Field | Value |
| --- | --- |
| UTC work started | `2026-09-22T22:25:55.0487520Z` |
| UTC work ended | `—` |
| Elapsed wall clock | `—` |
| Scope note | `Wave 3 captured-root direct physical file-or-directory lookup slice; no provider, wildcard, or dynamic-parameter support.` |

The end and elapsed fields are intentionally blank until reviewed Native AOT
verification has been integrated to `main`.

## What transferred

- Pending implementation: the generated source contract remains the sole
  authority for admitted metadata; the likely initial subset is the source
  `Path` parameter only.
- Pending implementation: the source command's one-item filesystem intent will
  be expressed through a shared direct physical-item catalog operation, rather
  than duplicating lookup, captured-root validation, symlink policy, Unix
  metadata, or formatting logic in the cmdlet.

## Upstream reuse evidence matrix

This start-gate matrix covers the known source surfaces. It must be expanded
with exact output and formatting evidence before implementation is admitted.

| Surface | Exact upstream evidence | Reuse decision | AOT target / contract | Why direct copy is or is not possible | Variance ID | Verification |
| --- | --- | --- | --- | --- | --- | --- |
| Command metadata | `Navigation.cs:1847-1907`; `GeneratedCmdletPorts.GetItem` | copied | Generated descriptor, narrowed only to implemented generated parameters | Cmdlet attributes and parameter-set metadata are source-extracted at build time; no handwritten aliases or binding contract | planned `getitem-generated-contract` | generated-contract inspection and binder self-tests |
| Lifecycle/base behavior | `Navigation.cs:1968-2003` (`ProcessRecord`, `InvokeProvider.Item.Get`, provider exceptions → `WriteError`) | shared-substrate replacement | `AotCmdletBase` plus typed runtime diagnostics and direct catalog capability | `InvokeProvider`, session state, dynamic parameters, `PSObject`, and provider context are outside the Native AOT boundary; existing shared lifecycle/diagnostic services replace only their admitted behavior | planned `getitem-direct-physical-provider-boundary` | focused managed/native negative diagnostics |
| Physical item output | `FileSystemProvider.cs:221-230`, `1253-1285`; source `InvokeProvider.Item.Get` at `Navigation.cs:1975` | adapted | Existing closed `PhysicalChildItemRecord`, if the direct catalog seam can return it without enumeration | Upstream returns `FileInfo`/`DirectoryInfo` via dynamic provider objects; the target must retain a finite typed record rather than import provider/type-system machinery | planned `getitem-closed-physical-record` | controlled filesystem fixture and normal-`pwsh` oracle |
| Default display view | `FileSystem_format_ps1xml.cs:46-69` (`childrenWithUnixStat`); `TypeTable_Types_Ps1Xml.cs:9092-9105` (`UnixMode`) | static-data extracted | Existing source-backed fixed Unix table descriptor, reused only after exact-item oracle evidence | Upstream dynamically selects a format/type table over `FileSystemInfo`; Native AOT must use a fixed compiled descriptor and cannot perform dynamic format discovery | planned `getitem-unix-format-static-extraction` | normalized normal-`pwsh` versus Native AOT direct-item table oracle |

Before runtime code is written, the implementation must document the searched
existing target abstractions, approved replacement exception, authority of the
host capability, and every fail-closed deferred source parameter.

## What did not transfer

- The provider/session-state dispatch in `InvokeProvider.Item.Get`, registry
  providers, wildcard expansion, dynamic parameters, credentials, filters,
  includes/excludes, and `PSObject` formatting remain out of scope unless a
  separately reviewed shared seam admits them.

## Verification

```powershell
# To be populated only with commands actually run after implementation.
```

## Variances and reusable learnings

- Pending: record the reusable direct physical-item catalog seam, including
  why it must be shared by `Get-Item`, `Test-Path`, and related filesystem
  ports rather than implemented inside this command.

## Format-contract status

- Pending controlled fixture/oracle evidence. No default output claim is made
  while the cmdlet is not implemented.

## Next action

Design and review `IPhysicalChildItemCatalog.GetDirectPhysicalItem` as a
single-item, captured-root, no-symlink physical lookup that reuses the
existing closed record and source-backed Unix table descriptor.
