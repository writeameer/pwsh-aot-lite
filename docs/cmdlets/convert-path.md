# Convert-Path port notes

**Integrated bounded Wave 3 native subset.** `Convert-Path` is generated `Path` only
and is a thin consumer of `ResolveExistingDirectPhysicalPath`, not a provider
converter.

- Upstream: `ConvertPathCommand.cs`; generated contract:
  `GeneratedCmdletPorts.ConvertPath.CreateAotDescriptor("Path")`.
- Target: `ConvertPathCmdlet` in `PhysicalChildItemCatalog.cs`; it adds no
  filesystem interface and emits existing prose `TextRecord` values only.
- Whole-collection rule: before calling the resolver, scan bound `Path` values
  in order. The first exact empty string throws the sole source-spanned
  `AOT6213`; processing stops with zero rows, resolver calls, or other
  diagnostics. Whitespace is literal only after this gate passes.
- With no empty value, `Resolved` emits its canonical text path while
  `Missing`/`Rejected` retain catalog diagnostics and later values continue.

Deferred: literal aliases, providers/drives, wildcards, Force, pipeline and
property-name binding, transactions, and provider ErrorRecords.

| UTC work started | `2026-09-23T08:40:00Z` |
| UTC work ended | `2026-09-23T09:29:55Z` |
| Elapsed wall clock | `00:49:55` |

Verification uses `Test-ConvertPathCompatibility.ps1`: stock `pwsh` 7.6.6
versus fresh native direct file/directory/multi-value output exactly, plus
no-empty continuation, whole-collection first-empty, whitespace, captured-root
relative input, and provider/wildcard/link/LP/Force rejection.

The runner self-test has a counting closed-outcome catalog proof: the first
exact empty value at the beginning, middle, or end of a collection produces
only source-spanned `AOT6213` and makes zero resolver calls, rows, context
errors, or transcript events. A separate non-empty mixed sequence proves
resolved output order and Missing/Rejected per-value diagnostic spans. Fresh
native evidence is the immutable release-proof artifact
`artifacts/osx-arm64-convert-path-final/PwshAotLite` (SHA-256
`e199a2d9b31940956e0e66c4ab299c347620584090c1bda3cada51a207cab023`),
published from the `codex/convert-path` source delta over target base
`51697c7`.
