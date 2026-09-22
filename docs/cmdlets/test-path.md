# Test-Path port notes

## Status and source

**Integrated bounded Wave 3 slice.** The implemented claim is
macOS-arm64, captured-root, direct physical `Path` lookup with static
`PathType` `Any`, `Container`, or `Leaf`, producing one typed Boolean per
non-null direct path. It is not provider-engine, wildcard, literal-path,
`IsValid`, dynamic-parameter, or pipeline compatibility.

- Original: `src/Microsoft.PowerShell.Commands.Management/commands/management/TestPathCommand.cs`, `TestPathCommand` over `CoreCommandWithCredentialsBase`.
- Generated contract: `GeneratedCmdletPorts.TestPath`.
- Target: `PhysicalChildItemCatalog.cs` (`ProbeDirectPhysicalItem`, `PhysicalItemProbeResult`, and `TestPathCmdlet`), `Pipeline.cs` (`BooleanRecord`), `PipelineValueAdapter.cs`, and `HelpCatalog.cs`.
- Review ledger: [test-path-wave3.md](../reviews/test-path-wave3.md).

## Port timing

| Field | Value |
| --- | --- |
| UTC work started | `2026-09-22T23:07:18.1558270Z` |
| UTC work ended | `2026-09-22T23:14:42.0000000Z` |
| Elapsed wall clock | `00:07:23.8441730` |
| Scope note | `Wave 3 direct physical existence/kind probe; closed Any/Container/Leaf Boolean projection.` |

## What transferred

- Generated source metadata remains the only descriptor authority:
  `GeneratedCmdletPorts.TestPath.CreateAotDescriptor("Path", "PathType")`.
  This retains position-zero mandatory `Path` and the generated `-Type` alias
  for `PathType`; no parameters or aliases are handwritten.
- The source decision is preserved in the admitted shape: `Any` is found,
  `Container` is found-directory, `Leaf` is found-file, and a no-follow
  missing item is `false` without an error record.
- The existing captured-root catalog owns the new closed result:
  `PhysicalItemProbeResult.Found(kind)`, `.Missing`, or `.Rejected`. It shares
  canonicalization and all-component descriptor/no-follow acquisition with
  `Get-ChildItem`/`Get-Item`, but does not call item description, produce Unix
  metadata, create a `PhysicalChildItemRecord`, or enumerate a directory.
- `BooleanRecord` is a finite typed scalar projection with an explicit
  `PipelineValueAdapter` case and prose terminal rendering. It does not turn
  the Boolean into a string/table imitation or a `PSObject`.

## Upstream reuse evidence matrix

| Surface | Exact upstream evidence | Reuse decision | AOT target / contract | Why direct copy is or is not possible | Variance ID | Verification |
| --- | --- | --- | --- | --- | --- | --- |
| Command metadata | `TestPathCommand.cs:45-137`; generated `GeneratedCmdletPorts.TestPath` | adapted | Generated `Path` + `PathType` descriptor | Source attributes/binder require `PSCmdlet`; the build generator preserves metadata and the shared binder remains the sole binder. | `testpath-generated-contract` | Generated-contract self-test and parser fixture |
| Provider test lifecycle | `TestPathCommand.cs:157-240` (`InvokeProvider.Item.Exists`, `IsContainer`) | shared-substrate replacement | `IPhysicalChildItemCatalog.ProbeDirectPhysicalItem` | `SessionState`, provider context, provider errors, and dynamic parameters cannot cross Native AOT. The existing catalog is extended rather than creating a second filesystem interface. | `testpath-direct-physical-probe` | Direct/missing/rejected probe self-tests and native oracle |
| Result decision | `TestPathCommand.cs:213-224` (`TestPathType.Any/Container/Leaf`) | adapted | `PhysicalItemProbeResult` kind → `BooleanRecord` | The direct source Boolean decision transfers mechanically once provider facts become a closed no-follow fact. | `testpath-closed-pathtype-and-boolean` | Any/Container/Leaf direct file/directory/missing oracle |
| Scalar output | `TestPathCommand.cs:241` (`WriteObject(result)`) | shared-substrate replacement | `BooleanRecord` + explicit adapter/prose presentation | A CLR `bool` cannot pass as an arbitrary runtime object; the finite Boolean record preserves scalar output without ETS/format discovery. | `testpath-closed-pathtype-and-boolean` | Adapter/self-test and line-for-line Boolean oracle |

## Deferred / fail-closed behavior

- `LiteralPath`/`PSPath`/`LP`, `Filter`, `Include`, `Exclude`, credentials,
  provider drives/paths, wildcards, `IsValid`, dynamic parameters, provider
  error records, link/hidden access, and pipeline/property-name binding remain
  absent or explicitly rejected. The shared binder rejects inactive generated
  parameters with `AOT2002`; rejected physical boundaries emit their existing
  source-spanned `AOT6201`–`AOT6210` diagnostic and emit no Boolean.
- Only the exact case-insensitive vocabulary `Any`, `Container`, and `Leaf`
  is admitted. Upstream enum abbreviation/numeric coercion is deliberately
  deferred; an outside value fails closed with `AOT6212`.
- Blank or whitespace direct paths short-circuit to `false`, matching source
  non-null blank input and avoiding the catalog's otherwise useful dot-path
  default. The noninteractive host cannot prompt for completely omitted
  mandatory `Path`, so that form fails closed with `AOT6211`.

## Verification

```powershell
dotnet build -c Release --no-restore
dotnet run -c Release --no-build -- --self-test
pwsh -NoProfile -File ./tools/Test-ParserReuseGuard.ps1
pwsh -NoProfile -File ./tools/Export-PwshParserBaseline.ps1 -Verify
pwsh -NoProfile -File ./tools/Export-Phase10Campaign.ps1 -Verify
pwsh -NoProfile -File ./tools/Export-Phase10BatchQueue.ps1 -Verify

$env:LIBRARY_PATH = '/opt/homebrew/opt/openssl@3/lib:/opt/homebrew/opt/brotli/lib'
dotnet publish -c Release -r osx-arm64 --self-contained true -o artifacts/osx-arm64-test-path --no-restore
./artifacts/osx-arm64-test-path/PwshAotLite --self-test
pwsh -NoProfile -File ./tools/Test-TestPathCompatibility.ps1 -NativePwshPath ./artifacts/osx-arm64-test-path/PwshAotLite
```

The grammar fixture `33-test-path-direct-probe.ps1` is intentionally only an
upstream-parser baseline: it covers admitted positional/named/type syntax and
deferred literal/`IsValid` syntax without adding a grammar.

Working-branch verification passed: managed Release build and `--self-test`,
parser-reuse guard, 33-fixture parser differential baseline, Phase 10 manifest
and queue verification, plus `git diff --check`. Post-implementation-commit
verification for `c473b377e035d992300970e75d62ee10bf4a7793` produced a fresh
`osx-arm64` native artifact at
`artifacts/osx-arm64-test-path-final-20260922/PwshAotLite`; it passed
`--self-test` and the controlled strict scalar oracle and has SHA-256
`238d357f39a8ba140d770aeb6d91db119df26a0df66b9871e1e0c31ed0d4205b`.
The stock oracle was installed `pwsh` `7.6.6`. This evidence was independently
reviewed and integrated with the bounded-slice claim above.

## Variances and reusable learnings

The reusable seam is a **probe fact**, not another item API. Future path
cmdlets can reuse it only where their source behavior genuinely needs
found/missing/kind; a command that needs metadata must use the existing item
operation or a separately reviewed static fact. `Rejected` never masquerades
as `false`, which keeps provider and security boundaries visible.

## Format-contract status

There is no dynamic PowerShell view: upstream emits a scalar Boolean. The
direct untransformed native command uses the existing prose terminal route, so
the normal/native oracle compares exact `True`/`False` lines with no semantic
normalization.

| Surface compared | Stock `pwsh` command/version | Native command/artifact/RID | Fixture/input | Normalization | Result / variance ID |
| --- | --- | --- | --- | --- | --- |
| Direct physical Boolean scalar | `pwsh` `7.6.6`, `Test-Path -Path <fixture> -PathType <Any\|Container\|Leaf>` | `artifacts/osx-arm64-test-path-final-20260922/PwshAotLite`, `osx-arm64`, commit `c473b377e035d992300970e75d62ee10bf4a7793`, SHA-256 recorded above | ordinary file, directory, missing path, and whitespace input for each admitted type | none | integrated exact-match proof passed; `testpath-closed-pathtype-and-boolean` |

## Next action

No open action for this bounded slice. Revisit only when widening the direct
physical path contract through a separately reviewed variance.
