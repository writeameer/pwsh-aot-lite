# Review: Test-Path Wave 3 direct physical-probe slice

Date: `2026-09-22`  
Claim reviewed: `Working-branch bounded direct physical Path probe with static Any/Container/Leaf Boolean result; not integrated.`  
Upstream commit: `pinned .upstream/PowerShell reference; TestPathCommand`

## Evidence

- Source/provenance: `.upstream/PowerShell/src/Microsoft.PowerShell.Commands.Management/commands/management/TestPathCommand.cs`; generated `GeneratedCmdletPorts.TestPath`.
- Target boundary: `PhysicalChildItemCatalog.cs` (`ProbeDirectPhysicalItem`, `PhysicalItemProbeResult`, `TestPathCmdlet`), `Pipeline.cs` (`BooleanRecord`), `PipelineValueAdapter.cs`, and `HelpCatalog.cs`.
- Build/test: `dotnet build -c Release --no-restore -v:q` and `dotnet run -c Release --no-build -- --self-test` passed. Focused self-tests cover generated metadata, `Path`/`-Type` binding, Any/Container/Leaf file/directory behavior, missing/whitespace false, the direct closed probe, provider source span/rejection, ancestor-link rejection, inactive LiteralPath/IsValid, and invalid static PathType.
- Native AOT evidence: fresh `dotnet publish -c Release -r osx-arm64 --self-contained true -o artifacts/osx-arm64-test-path --no-restore -v:q` passed; `artifacts/osx-arm64-test-path/PwshAotLite --self-test` and `tools/Test-TestPathCompatibility.ps1` passed. The fresh executable SHA-256 is `69c743d4e4f62ba7da6fca49ce52c583cc72781df2c07e86fcdc44aa07aaec14`. It is a `codex/test-path` working-tree artifact based on `78e5b1083112a127fd335b9b79608184abe2a653`; release stewardship must bind replacement post-commit native evidence to the resulting commit.
- Differential parser evidence: `Test-ParserReuseGuard.ps1` and `Export-PwshParserBaseline.ps1 -Verify` passed with 33 fixtures, including the new syntax-only Test-Path fixture. `Export-Phase10Campaign.ps1 -Verify`, `Export-Phase10BatchQueue.ps1 -Verify`, and `git diff --check` passed.
- Upstream reuse matrix: [Test-Path port note](../cmdlets/test-path.md).
- Runtime oracle comparison: `tools/Test-TestPathCompatibility.ps1` used installed `pwsh` `7.6.6` and the fresh native artifact above. It compared direct ordinary file/directory/missing/whitespace input under Any/Container/Leaf with exact scalar Boolean output (no normalization), and separately asserted an ancestor link emits `AOT6205` and no Boolean.
- Approved AOT replacement exceptions: `testpath-direct-physical-probe`, `testpath-closed-pathtype-and-boolean`.

## Independent verdicts

| Persona | PASS / BLOCK | Evidence-backed finding | Resolution / accepted variance |
| --- | --- | --- | --- |
| Upstream Grammar Steward | pending | Baseline evidence is prepared; independent review is still required. | Do not integrate until a reviewer verifies the upstream parser remains the only grammar authority. |
| Native AOT Boundary Sentinel | **PASS** | `ProbeDirectPhysicalItem` shares the catalog's single `TryResolveDirectPhysicalPath` gate, then calls only `ProbeCanonicalDirectPhysicalItem` and `MacOsPhysicalMetadata.TryAcquireNoFollow`. The latter retains the reviewed all-components `/`-descriptor walk, `fstatat(..., AT_SYMLINK_NOFOLLOW)`, `openat(..., O_NOFOLLOW)`, and final descriptor `fstat`. The probe does **not** call `GetDirectPhysicalItem` or `TryDescribe`, enumerate, create Unix display metadata, construct an item record, use `File.Exists`/`Directory.Exists`, `FileInfo`/`DirectoryInfo`, provider dispatch, reflection, or a path-based fallback. Its closed result is sound: no-follow missing is the only `Missing`/no-error result; unsupported, provider/wildcard, link, hidden, access, and I/O boundaries emit their existing source-spanned diagnostic and return `Rejected`; `TestPathCmdlet` emits no Boolean for `Rejected`. I independently built the current working tree, ran `--self-test`, ran `git diff --check`, then ran the recorded fresh `osx-arm64-test-path` native executable's `--self-test` and exact scalar/no-follow oracle successfully. | Approved only for the documented macOS-arm64 captured-root, direct ordinary file/directory subset. `TryAcquireNoFollow` rejects non-regular/non-directory items before a `Found` result; hidden and link paths remain visible rejected boundaries, never false. |
| Static Data-Plane & Binder Guardian | pending | Generated descriptor and typed Boolean adapter evidence is prepared; independent review is still required. | Do not integrate until the closed Boolean/PathType contract is approved. |
| Compatibility Proof Adversary | pending re-review | I independently reran the stated macOS scalar oracle against `artifacts/osx-arm64-test-path/PwshAotLite` (SHA-256 `69c743d4e4f62ba7da6fca49ce52c583cc72781df2c07e86fcdc44aa07aaec14`), and the admitted file/directory/missing/whitespace `Any`/`Container`/`Leaf` cases match stock `pwsh` exactly. The implementation correctly models a rejected probe as no Boolean. Follow-up managed self-test evidence now includes a Test-Path-specific omitted-`Path` `AOT6211` source-span/plain-terminal snapshot and mixed multi-value provider/wildcard/link cases that assert rejected-value spans and accepted-sibling Boolean cardinality/order. Working-tree provenance is now explicit: `codex/test-path` based on `78e5b1083112a127fd335b9b79608184abe2a653`. | Re-run fresh Native AOT publish/self-test and the scalar oracle after the final Test-Path commit, record that exact commit beside the replacement artifact hash, then request compatibility re-review. |
| Upstream Reuse & Format-Contract Reviewer | pending | Matrix documents upstream provider/Boolean surfaces; independent review is still required. | Do not integrate until the no-new-interface/no-fake-format conclusion is approved. |

## Outcome

`working branch, review pending` — managed, parser, Native AOT, and oracle
gates have passed; no integration or expanded compatibility claim is permitted
until every independent verdict is PASS.
