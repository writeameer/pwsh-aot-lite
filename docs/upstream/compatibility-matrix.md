# Upstream compatibility matrix

This matrix is the release-intake view of the Native AOT boundary. It is not a
list of PowerShell commands that are generally compatible: each row identifies
the exact, reviewed target subset at the stated upstream commit. The detailed
per-cmdlet note and `port-variances.json` remain authoritative for parameters,
diagnostics, output, and deferrals.

Current pinned upstream commit: `1e53f6bbab4b8791eae782474d21889f9e5d6038`.

| Surface | Current Native AOT subset | Upstream provenance / adaptation seam | Intake action when upstream changes |
| --- | --- | --- | --- |
| Parser and structural AST | Pinned extracted parser facade; static lowerer admits only the documented language subset | `language/UPSTREAM.md`; parser baselines | Differential token/AST/diagnostic oracle; review copied parser files before any baseline update |
| Generated cmdlet catalog | 290 declarations / 288 logical names as static metadata only | `GeneratedCmdletPorts.g.cs`, `Export-Phase10Campaign.ps1` | Regenerate contract and campaign; inspect every metadata/source-hash change |
| `Get-Process` | Reviewed process selection and documented option subset | generated contract + `docs/cmdlets/get-process.md`; process host seam | Direct-body review, native oracle, platform capability review |
| `Get-Uptime`, `Get-Culture`, `Get-UICulture`, `Get-TimeZone`, `Get-Date`, `Get-Verb` | Documented direct platform/BCL subsets | respective `docs/cmdlets/` note; platform/static-data seams | Body/metadata review; re-run per-note native evidence |
| `Get-FileHash` | Direct physical-file hash subset | `docs/cmdlets/get-filehash.md`; physical-file resolver seam | Provider-seam review and controlled fixture oracle |
| `New-Guid`, `New-TimeSpan`, `Start-Sleep` | Wave 2 BCL calibration subsets only | respective notes; typed scalar/delay seams | Direct-body and descriptor diff; preserve closed-input rejections unless expanded by review |
| `Get-Help`, `Get-Command`, `Get-Module` | Static generated catalog plus declarative extension metadata | respective notes; control-plane catalog seam | Metadata diff and catalog snapshot; never import module code to reconcile a change |
| `Find-Module`, `Install-Module` | Explicit local/offline control-plane subsets | respective notes; package/trust seam | Contract/body review and offline fixture evidence |
| Formatting and terminal presentation | Only reviewed static descriptors/projections; no ETS or runtime format discovery | `docs/architecture/terminal-presentation.md`, per-port format matrices | Require format trigger review and normal-`pwsh` vs Native-AOT display oracle |
| Providers, drives, sessions, type data, runspaces, arbitrary CLR objects | Not executable in-process | `ARCHITECTURE.md`, `docs/architecture/provider-host-substrate.md` | Keep rejected unless a separately approved static substrate/sidecar changes the boundary |

When an intake changes a row, add the candidate commit, review ledger, report
path, final decision, and any variance convergence/defer decision to
[the upgrade ledger](upgrade-ledger.md). Do not edit this matrix to make an
unsupported behavior appear compatible.
