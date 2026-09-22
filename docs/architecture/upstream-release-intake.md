# Upstream release intake and compatibility gate

The upstream PowerShell tree is both the language-source provenance and the
source-contract authority for this project. An upstream bump is therefore a
compatibility change, not routine dependency maintenance. This gate makes that
fact operational: every candidate is restored separately, mechanically
compared with the currently pinned source, and reviewed at the seam where the
Native AOT host has deliberately adapted PowerShell.

This process never imports a new upstream binary into the Native AOT host. The
candidate tree is build-time evidence only.

## Immutable inputs

Keep the currently integrated source in `.upstream/PowerShell`. Restore a
candidate at a *different*, disposable Git checkout. Record both full commit
SHAs, repository URL, and the generator/tool hashes in the upgrade ledger.
Never point the normal build at a candidate and then silently rewrite the
configured pin.

The candidate must be restored by commit SHA. `eng/Restore-Upstream.ps1`
validates that property for the configured pin. For an intake candidate, use a
separate copy of that same restore procedure/configuration with the candidate
SHA, or a pre-existing checkout whose detached `HEAD` is verified locally.
Network access is an acquisition concern; the comparison and review gate below
does not require it.

## Required candidate gate

Run all steps in order on `codex/upstream-intake-<version-or-sha>` before
changing the pinned configuration.

1. Record the baseline and candidate full SHAs in
   [`../upstream/upgrade-ledger.md`](../upstream/upgrade-ledger.md), with the
   candidate state `in progress`.
2. Create a disposable worktree of *this* repository for the intake. Do not
   redirect `BaseIntermediateOutputPath` in the active worktree: project
   references own their intermediate paths and a mixed tree can contaminate
   generated inputs. Build the candidate from the disposable host worktree.
   This runs the existing compile-time source-contract generator without giving
   the running executable an upstream assembly:

   ```powershell
   git worktree add --detach /absolute/disposable/pwsh-aot-lite-intake HEAD
   Set-Location /absolute/disposable/pwsh-aot-lite-intake
   dotnet build -c Release -p:PowerShellSourceRoot=/absolute/candidate/PowerShell/src
   ```

3. Generate the static diff report (it is a review input, not an automatic
   approval):

   ```powershell
   pwsh -NoProfile -File tools/Test-UpstreamIntake.ps1 `
     -CandidateSourceRoot /absolute/candidate/PowerShell `
     -CandidateGeneratedContractPath obj/upstream-intake/candidate/Generated/PwshAotPortGenerator/PwshAotPortGenerator.PortManifestGenerator/GeneratedCmdletPorts.g.cs `
     -Write
   ```

   Locate the generated `GeneratedCmdletPorts.g.cs` beneath that worktree's
   `obj/Generated/` directory and pass its exact path. The script fails closed
   if a source root, generated contract, or Git commit cannot be proven.
4. Inspect every report row. It classifies review triggers into `metadata`,
   `body`, `format`, and `provider-seam`; a row can carry more than one. These
   labels identify *where human review is required*. They do not infer that a
   command is compatible or executable.
5. Re-run the parser oracle, never regenerate its baseline merely because the
   candidate differs:

   ```powershell
   pwsh -NoProfile -File tools/Export-PwshParserBaseline.ps1 -Verify
   pwsh -NoProfile -File tools/Test-ParserReuseGuard.ps1
   ```

   Where a copied/adapted upstream parser file changed, add focused fixtures
   and run a normal `pwsh` candidate parser oracle for those fixtures before
   accepting new baselines. Record token, AST, and diagnostic differences in
   the upgrade ledger.
6. Regenerate and inspect the campaign inventory against the candidate:

   ```powershell
   pwsh -NoProfile -File tools/Export-Phase10Campaign.ps1
   pwsh -NoProfile -File tools/Export-Phase10Campaign.ps1 -Verify
   ```

   A changed source hash, declaration count, source identity, or generated
   metadata is a reclassification trigger. Do not preserve a previous category
   just because the command name still exists.
7. For every changed implemented port, update its reuse matrix, normal-pwsh
   versus Native-AOT oracle evidence, timing/variance entry, and campaign row.
   Update [`../upstream/compatibility-matrix.md`](../upstream/compatibility-matrix.md)
   with the new upstream commit and the supported subset. A changed format row
   requires the Upstream Reuse & Format-Contract Reviewer; a changed
   provider-seam row requires an explicit provider/capability review.
8. Dispatch the reviewers named by the report plus the mandatory reviewers in
   `reviewer-personas.md`; retain `PASS`/`BLOCK` results in a review ledger.
   No block may be bypassed by changing a version number.
9. Run managed self-tests, parser guards, campaign verification, a fresh
   Native AOT publish/self-test, and focused native smoke/oracle tests for all
   changed implemented commands. Only then update the configured pin, restore
   it normally, regenerate checked-in contracts/manifests, and merge.

## Classification rules

`Test-UpstreamIntake.ps1` intentionally uses conservative static signals.

| Trigger | Evidence | Required review |
| --- | --- | --- |
| `metadata` | Cmdlet declaration identity or generated contract block changed; added/removed declaration | generated descriptor/parameter-set/binder review; reclassify campaign row |
| `body` | Source file hash changed | direct body-and-local-helper review for every implemented affected command |
| `format` | A format/type-data file changed, or changed command source carries format/type-table signals | output/format contract matrix plus normal-`pwsh` vs Native-AOT oracle |
| `provider-seam` | A changed command source carries provider/session/path/drive signals | capability-authority review; preserve direct-path rejection unless a reviewed substrate expands it |

The classifier prefers false positives. It must never be used to claim that an
unflagged command has no semantic change. Upstream source review remains
mandatory for any port being widened.

## Acceptance rule

An upstream intake is accepted only when all of the following exist:

- a checked report with baseline/candidate SHA provenance;
- an upgrade-ledger entry with the final decision and review links;
- updated compatibility-matrix rows for every integrated adapter touched by
  the candidate;
- regenerated generator/campaign evidence, reviewed classifications, parser
  differential evidence, and required native tests; and
- no unresolved `BLOCK` verdict.

Unknown upstream code is never silently copied. It remains catalogued-only,
is explicitly deferred, or receives a separately reviewed copied/adapted,
static-data, or shared-substrate decision under the existing
[upstream-reuse governance](upstream-reuse-governance.md).

## Why this supports future releases

The seam is intentional: the generator captures declarative source contracts;
the parser oracle protects copied language behavior; the campaign manifest
protects inventory/classification; and the compatibility matrix names the
finite AOT subset for each adapter. A newer upstream release therefore becomes
a bounded evidence-and-adaptation exercise rather than a second,
hand-maintained PowerShell implementation.
