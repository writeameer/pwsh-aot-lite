# Cmdlet port notes

This directory is the migration lab notebook. Each PowerShell cmdlet that is
examined gets one file under `cmdlets/`, whether it is completed, in progress,
or deferred. The notes are evidence for improving the porting factory; they
are not a compatibility claim.

## Rules

- Create `cmdlets/<command-name>.md` before starting a port.
- Start its UTC wall-clock record before research/design work, using
  [the per-cmdlet template](cmdlets/TEMPLATE.md), and add the same row to the
  [port timing ledger](cmdlets/port-timing.md). Record an end only after
  verification and integration; never invent historical times.
- Link the original source and the generated contract.
- Complete the mandatory **Upstream reuse evidence matrix** before implementation.
  It must give pinned source/base/format evidence, a permitted reuse decision,
  a concrete why-not-copy rationale for each replacement, and the matching
  variance ID. See [upstream reuse governance](architecture/upstream-reuse-governance.md).
- Record the implemented behavior, failed approaches, and every observed
  variance from PowerShell.
- Keep the structured, machine-readable entry in
  [`../port-variances.json`](../port-variances.json) in sync with the notes.
- Do not mark a port complete until its fixture coverage and a Native AOT run
  are recorded here.

## Index

The migration order and candidacy decisions are maintained in the
[cmdlet migration queue](queue.md).

| Cmdlet | State | Notes |
| --- | --- | --- |
| `Get-Process` | complete for the current macOS AOT target | [Get-Process port notes](cmdlets/get-process.md) |
| `Get-Uptime` | complete for the current AOT target | [Get-Uptime port notes](cmdlets/get-uptime.md) |
| `Get-UICulture` | complete for the current AOT target | [Get-UICulture port notes](cmdlets/get-uiculture.md) |
| `Get-Culture` | direct modes ported; pipeline binding deferred | [Get-Culture port notes](cmdlets/get-culture.md) |
| `Get-Verb` | complete for the current AOT target | [Get-Verb port notes](cmdlets/get-verb.md) |
| `Get-TimeZone` | complete for the current AOT target | [Get-TimeZone port notes](cmdlets/get-timezone.md) |
| `Get-Date` | direct modes ported; pipeline binding deferred | [Get-Date port notes](cmdlets/get-date.md) |
| `Get-FileHash` | direct physical-file modes ported; provider/stream binding deferred | [Get-FileHash port notes](cmdlets/get-filehash.md) |
| `Get-ChildItem` | complete for the bounded Wave 3 macOS-arm64 captured-root direct physical default/positional/`-Path` immediate-child slice, with a statically extracted upstream Unix default view | [Get-ChildItem port notes](cmdlets/get-childitem.md) |
| `Get-Item` | integrated bounded Wave 3 macOS-arm64 captured-root direct physical file-or-directory lookup slice; provider, wildcard, dynamic-parameter, pipeline, and interactive-prompt support remain deferred/fail closed | [Get-Item port notes](cmdlets/get-item.md) |
| `Test-Path` | integrated bounded Wave 3 macOS-arm64 captured-root direct physical existence/kind probe; provider, literal, wildcard, `IsValid`, pipeline, and dynamic-parameter support remain deferred/fail closed | [Test-Path port notes](cmdlets/test-path.md) |
| `Resolve-Path` | integrated bounded Wave 3 macOS-arm64 captured-root direct existing physical resolution; provider, literal, wildcard, relative/session, pipeline, and dynamic-parameter support remain deferred/fail closed | [Resolve-Path port notes](cmdlets/resolve-path.md) |
| `Convert-Path` | in-progress generated Path-only canonical-text projection reusing the Resolve-Path physical resolution seam | [Convert-Path port notes](cmdlets/convert-path.md) |
| `New-Guid` | default UUID v7 and generated `-Empty` ported; typed input/output deferred | [New-Guid port notes](cmdlets/new-guid.md) |
| `New-TimeSpan` | no-argument and typed direct component construction ported; date/pipeline input deferred | [New-TimeSpan port notes](cmdlets/new-timespan.md) |
| `Start-Sleep` | generated `-Milliseconds`/`-ms` ported; seconds, duration, positional, and pipeline input deferred | [Start-Sleep port notes](cmdlets/start-sleep.md) |
| `Get-Help` | complete for static catalog and extension discovery; advanced HelpSystem modes deferred | [Get-Help port notes](cmdlets/get-help.md) |
| `Get-Command` | complete for static built-in and extension command discovery; live session-state modes deferred | [Get-Command port notes](cmdlets/get-command.md) |
| `Get-Module` | complete for the static built-in and registered-extension inventory; live/module-path modes deferred | [Get-Module port notes](cmdlets/get-module.md) |
| `Find-Module` | complete for read-only local repository-index discovery; online repository/install modes deferred | [Find-Module control-plane notes](cmdlets/find-module.md) |
| `Install-Module` | complete for hash-verified local file-package installation; transport/trust/package-manager modes deferred | [Install-Module control-plane notes](cmdlets/install-module.md) |

The ordered campaign backlog is in the [cmdlet migration queue](queue.md).
Legacy-module registration experiments and their package contracts are indexed
in [extension notes](extensions/README.md). They are deliberately separate
from cmdlet ports: registration describes a module without claiming that its
commands execute inside the AOT host.

The canonical catalog/registry boundary, extension safety model, availability
states, and review checklist are in [the architecture guardrails](../ARCHITECTURE.md).
Read that before adding an adapter, a catalog query, or an extension package
field.

The parser must be reused from upstream rather than reimplemented; the
[parser-reuse guard](../PARSER-REUSE-GUARD.md) defines the required provenance
and differential-test gates.

The [stock PowerShell parser differential harness](architecture/parser-differential-harness.md)
provides the checked-in token, AST-shape, and diagnostic oracle for the pinned
upstream parser extraction now used by the runner. It remains the regression
gate for every adopted syntax slice.

The [upstream parser runner integration](architecture/upstream-parser-runner-integration.md)
records the intentionally narrow, parse-only-to-static-registry connection.
`ScriptParser` is retained only as an AST-derived structural-stage semantic
helper and compatibility facade; it is not a source parser.

[Reviewer personas and the review ledger](architecture/reviewer-personas.md)
make the parser, AOT-boundary, and reuse controls operational for future
agents.

The [upstream reuse and format-contract governance](architecture/upstream-reuse-governance.md)
is the mandatory anti-NIH evidence gate for every cmdlet port: it requires
pinned-source proof for reused behavior and for every output/default-display
surface, and approves only explicit AOT replacements.

The [language-tooling contract](architecture/language-tooling-contract.md)
keeps execution, CLI editing/highlighting, and future editor/LSP work on the
same upstream parser output instead of allowing separate grammars to emerge.

The [diagnostic-experience contract](architecture/diagnostic-contract.md)
makes Rust-style, source-precise diagnostics a non-optional part of the AOT
Execution Kernel rather than a later UI enhancement.

The [AOT Execution Kernel foundation](architecture/aot-execution-kernel.md)
records the actual AST-to-plan implementation slice, its stable diagnostics,
and the next language increments. It must be updated before widening an
execution-support claim.

The [Engine Runtime Core](architecture/engine-runtime-core.md) records the
ordered output/error event boundary, its deliberately narrow stream claim, and
the finite runtime slices that follow. Its
[stream-contract review ledger](reviews/2026-09-22-runtime-stream-contract.md)
must be updated before widening stream or host behavior.

The [Language Compatibility Core](architecture/language-compatibility-core.md)
records the executable statement/variable/conditional/closed-list-foreach slice,
its scope and binder boundaries, and the syntax intentionally deferred before
general loops and functions. Its
[independent review ledger](reviews/2026-09-22-language-compatibility-core.md)
records the parser, AOT, binder, diagnostics, compatibility, and architecture
gates that admitted this narrow slice.

The [terminal presentation policy](architecture/terminal-presentation.md)
documents ANSI-safe diagnostics and why interactive syntax highlighting remains
a future shared-parser projection rather than a second lexer.

The [conditional execution and terminal-presentation review ledger](reviews/2026-09-22-if-control-flow-terminal-presentation.md)
records the independent gates that admitted the closed `if` slice and safe ANSI
diagnostic policy.

The [closed-list foreach review ledger](reviews/2026-09-22-foreach-closed-list.md)
records the parser, AOT, data-plane, compatibility, diagnostics, and architecture
evidence for the synchronous iteration slice.

The closed, reflection-free pipeline data model and its first reviewed, limited
adapter slice are documented in the [structured value-plane design](architecture/value-plane.md)
and [first migration note](architecture/value-plane-first-migration.md).
Existing cmdlets retain typed `IPipelineRecord` business logic; only direct
generic pipeline stages cross the explicit `AotValue` adapter.

## Control-plane notes

| Capability | State | Notes |
| --- | --- | --- |
| Metadata completion | complete for static catalog discovery | [Completion design and evidence](control-plane/completion.md) |
| Repository discovery | complete for read-only local index discovery | [Find-Module control-plane notes](cmdlets/find-module.md) |
| Repository installation | complete for staged, local file-package proof | [Control-plane index](control-plane/README.md) and [Install-Module notes](cmdlets/install-module.md) |

## Extension-registration evidence

| Module | Shape | Registration result | Notes |
| --- | --- | --- | --- |
| `Microsoft.PowerShell.Archive` | script module | two exported functions catalogued | [Archive proof](extensions/microsoft-powershell-archive.md) |
| `Microsoft.PowerShell.ThreadJob` | binary module | one compiled cmdlet plus authored help catalogued | [ThreadJob proof](extensions/microsoft-powershell-threadjob.md) |
| `Microsoft.PowerShell.KubeCtl` | script module with `kubectl` import-time discovery | declaration-only package registered; execution blocked | [KubeCtl proof](extensions/microsoft-powershell-kubectl.md) |

## Per-cmdlet note shape

Start from [the per-cmdlet template](cmdlets/TEMPLATE.md). Use this structure
for every new file:

1. **Status, source, and port timing** — original files, base classes,
   generated contract, UTC start/end, elapsed wall clock, and scoped slice.
2. **What transferred** — code/behavior retained and its AOT counterpart.
3. **Upstream reuse evidence matrix** — exact upstream producer/base/format
   contract, permitted reuse decision, variance, why-not-copy rationale, and
   verification for every behavior/output surface.
4. **What did not transfer** — engine dependencies, failed approaches, and
   explicit deferrals.
5. **Verification** — fixture and Native AOT commands actually run.
6. **Variances and format-contract status** — concise summary plus links/IDs
   from `port-variances.json`, output/default-column provenance, and an explicit
   display convergence path where needed.
7. **Reusable learnings** — candidates for generator or shared-runtime work.
8. **Next action** — blank only when the port is genuinely complete.

This keeps repeated problems discoverable across ports: a pattern should become
a generator rule or shared runtime service, not a new one-off adapter.
