# Evidence and provenance

## Immutable external evidence

The accepted plan and lens reports remain outside this repository in the survey
workspace.  They were not copied or modified.  The paths below are
repository-sibling-relative for local review; the SHA-256 values identify the
exact accepted packet.

| Artifact | Immutable source location | SHA-256 |
| --- | --- | --- |
| J1 v11 design README | [survey design](../../../../../pwsh-aot-conversion-survey/analysis/j1-conversion-plan-v11/README.md) | `7ac698edc485956b6dd4549ee87b09fd1a856c44558e329899fe071bdad4ec70` |
| J1 v11 machine-readable plan | [survey plan](../../../../../pwsh-aot-conversion-survey/analysis/j1-conversion-plan-v11/plan.json) | `cdc099ae571f8995a16bbf8ec5f141d0429dca22618cc8b4b787e168629b2af1` |
| J1 v11 checksum manifest | [survey checksums](../../../../../pwsh-aot-conversion-survey/analysis/j1-conversion-plan-v11/checksums.json) | `966bb040e89dc873092997d2f12414095ebc96583e86971ec8474cc1c1bb7ea5` |
| PowerShell compatibility PASS | [survey lens](../../../../../pwsh-aot-conversion-survey/analysis/j1-conversion-plan-v11/reviews/powershell-compatibility-lens.md) | `eff22d8f18e369ca49783b4ff107834bb8f057180b8d72c0f5911c8c45604511` |
| Typed structured-data/AOT PASS | [survey lens](../../../../../pwsh-aot-conversion-survey/analysis/j1-conversion-plan-v11/reviews/structured-data-aot-lens.md) | `80264a5d31dc93569345dd351d7f18de267d74343ac15fa57b6101809bc3b8a3` |
| J1 materialized source-fact index | [survey evidence index](../../../../../pwsh-aot-conversion-survey/evidence/materialized/j1-source-facts-v1/index.json) | `58b24de04577e710448a3ff7dfc7f3ee3802e3dcef7627f4c1d2c9cc270f7a5e` |
| J1 raw extractor report | [survey raw report](../../../../../pwsh-aot-conversion-survey/runs/j1-source-facts-v1/report.json) | `59a53a948d5644a2ec2478905f77a8c0e43fcd722628f1744c31ad216ab5611a` |

## Source pins in the accepted profile claim

| Command | Upstream source | SHA-256 |
| --- | --- | --- |
| `Join-Path` | `src/Microsoft.PowerShell.Commands.Management/commands/management/CombinePathCommand.cs` | `0182ef136e8c67e8c177daa1e601639f573be467ea448fb6d65c9fce53f48b09` |
| `Split-Path` | `src/Microsoft.PowerShell.Commands.Management/commands/management/ParsePathCommand.cs` | `11674c9e691a38198246c281712db1aae067b72bfbe7b110881ad6dd5848dd31` |

## Related target contracts

- [J1 accepted design](../../../architecture/j1-direct-lexical-path-profiles.md)
- [Profile-design preflight](../../../architecture/profile-design-preflight.md)
- [Provider and host substrate](../../../architecture/provider-host-substrate.md)
- [Diagnostic contract](../../../architecture/diagnostic-contract.md)
- [Upstream reuse governance](../../../architecture/upstream-reuse-governance.md)
- [Previous J1 BLOCK evidence](../2026-09-23-j1-physical-path-profiles/evidence.md)
