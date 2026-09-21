# Migration architecture and guardrails

## Parser reuse is a non-negotiable boundary

PowerShell syntax is a product contract. The target must preserve it by
extracting/adapting the existing handwritten PowerShell tokenizer, parser, and
structural AST from a pinned upstream commit—not by growing a second grammar.
The temporary `frontend-spike/` executable is an archived Native-AOT proof; it
is explicitly excluded from this host project and is not an implementation
candidate. See [PARSER-REUSE-GUARD.md](PARSER-REUSE-GUARD.md) for the required
source provenance, differential-test, and AOT-boundary gates.

## Shared language-tooling contract

The parser is not merely an execution pre-step. It is the shared PowerShell
language service for three independent consumers:

```text
PowerShell source
  └─ upstream parser facade
       ├─ AST + diagnostics ──────► AOT feature policy and execution lowerer
       ├─ tokens + source extents ► CLI syntax highlighting and editing
       └─ AST/tokens/diagnostics ─► future editor/LSP analysis
```

The phase-one execution kernel must consume that facade; it must not own,
reshape, or hide the source parser behind an execute-only API. The facade must
preserve stable source spans, token kind/text, structural AST access, and parse
diagnostics without executing source. A tooling consumer may inspect a partial
or malformed document and return diagnostics, but may not make a source line
executable, import a module, evaluate an expression, or load an extension.

No second grammar, lexer, semantic-token classifier, or completion parser may
be introduced where upstream parser output can provide the answer. A narrow
incomplete-input scanner is permitted only for non-executable metadata
completion and must remain explicitly non-authoritative, as documented below.
Detailed consumer boundaries and rollout milestones are in
[the language-tooling contract](docs/architecture/language-tooling-contract.md).
The currently executable statement/variable/conditional/closed-list-foreach subset and its strict
scope/binder boundary are recorded in the
[Language Compatibility Core](docs/architecture/language-compatibility-core.md).

## Diagnostic experience is part of the execution kernel

Big Rock 1 is not complete merely when the host can execute an AST subset. It
also establishes the single structured diagnostic path through parse, lowering,
binding, pipeline execution, and cmdlet/runtime failures. The host renders
those records as source-precise, actionable terminal output and later projects
the same records to JSON and editor/LSP clients.

No feature may use raw exception text, a command-specific `Console.WriteLine`,
or an unstructured string as its public failure contract. Every supported,
malformed, unsupported, binding, and runtime-error path needs a stable ID,
category/severity, actionable primary message, source extent when source is
available, and a negative snapshot test. The full contract and rollout are in
[the diagnostic-experience contract](docs/architecture/diagnostic-contract.md).
The host-owned ANSI policy, control-character safety rule, and future
interactive-highlighting boundary are in
[terminal presentation](docs/architecture/terminal-presentation.md).

The recurring independent-review roles, their required evidence, and their
blocking authority are defined in
[docs/architecture/reviewer-personas.md](docs/architecture/reviewer-personas.md).
Every parser, runtime, and compatibility claim must use those roles; a single
`BLOCK` prevents integration into the host executable path.

The source generator is a **contract extractor**, not a source-to-source
translator. It preserves what the existing cmdlet declares and identifies what
cannot cross the AOT boundary automatically. It must never silently claim that
a cmdlet is portable.

```text
PowerShell source
  └─ generated source contract (immutable metadata + blockers)
       └─ reviewed AOT adapter (one cmdlet)
            ├─ shared AOT cmdlet lifecycle/runtime
            └─ explicit platform/host services
```

## Preserved PowerShell design

For every discovered cmdlet, generated metadata retains:

- the command class and its base-type chain;
- `BeginProcessing`, `ProcessRecord`, `EndProcessing`, and `StopProcessing`;
- `Cmdlet` capabilities such as `SupportsShouldProcess` and remoting;
- output declarations;
- each parameter's .NET type, scalar/array/switch shape, aliases, wildcard
  support, validation attributes, and **each individual parameter-set binding**;
- mandatory, positional, and pipeline-by-value/property/remaining-arguments
  rules; and
- engine APIs encountered in the command body as migration blockers.

The shared `AotCmdletBase` mirrors the PowerShell lifecycle. Ports put command
logic in `ProcessRecord`; the runtime owns lifecycle ordering, cancellation, and
stream/error policy.

## Port rules

1. Generated metadata is the source of truth. Do not hand-copy parameter or
   alias declarations into a port.
2. An adapter opts into only the parameters it implements. Unsupported source
   parameters fail during binding; they are never accepted and ignored.
3. Replace PowerShell services with narrow interfaces (`IProcessCatalog` is the
   first example). Do not let `PSObject`, session state, providers, reflection,
   or dynamic code leak into an AOT adapter.
4. A generated blocker requires an explicit design choice: shared AOT runtime
   service, dedicated platform adapter, or documented out-of-scope feature.
5. Each cmdlet port requires fixture tests for binding, parameter-set selection,
   output, non-terminating/terminating errors, cancellation if applicable, and
   an actual Native AOT run.
6. Each cmdlet port requires an entry in `port-variances.json`. It records every
   retained behavior, replacement, subset, deferral, blocker, and unknown.
   Entries are structured so we can later cluster repeated variances into a
   shared runtime service or generator rule instead of repeating hand work.

## What is intentionally not automated

The generator cannot safely translate cmdlet method bodies. The source corpus
uses providers, remoting, `PSObject`, session state, formatting, and dynamic
parameters. Generating a fake implementation for those would create apparent
compatibility while losing behavior. The generated contract makes that remaining
work explicit and keeps the hand-written portion small and reviewable.

## Control-plane architecture

The host has two deliberately separate planes.  Do not collapse them as more
cmdlets are ported:

```text
                   build time                         runtime
PowerShell source ─────────────► built-in contract catalog ─┐
                                                            ├─► Get-Help
extension package JSON ─────────────────► extension catalog ┤
                                                            ├─► Get-Command
                                                            └─► metadata-only completion

reviewed AOT adapters ───────────────► executable registry ───► invocation only
extension execution endpoint (future) ─► bridge dispatcher ───► invocation only
```

- **Catalogs answer what is known.**  They contain every generated built-in
  source contract and declarative extension contracts, including commands that
  cannot execute in this host yet.
- **The executable registry answers what this binary can invoke.**  It contains
  only reviewed, in-process Native-AOT adapters.  It must not be used as a
  discovery shortcut; otherwise unported commands and registered extensions
  disappear from `Get-Help` and `Get-Command`.
- **`Get-Module` is package inventory, not command grouping.**  It reads the
  built-in logical package plus extension manifests/provenance without loading
  a module or assembly.  Its package availability describes an execution
  endpoint, while command availability describes an individual command.
- **Invocation is the only plane allowed to execute code.**  A future legacy
  sidecar or native plugin bridge is selected by an explicit execution endpoint
  and trust policy, never merely because a catalog entry exists.

### One availability model

Status is user-visible data, not prose inferred by each command.  The current
terms have these canonical meanings:

| Availability | Meaning |
| --- | --- |
| `native-aot` | A reviewed, in-process adapter is executable by this binary. |
| `catalogued-only` | A generated built-in contract is discoverable but has no adapter. |
| `legacy-bridge-pending` | A legacy module was registered, but no sidecar bridge exists. |
| `blocked` | A declaration-only package was registered without importing it; it cannot execute. |
| `registered-not-executable` | An extension's declared execution kind is unknown to this host. |

The logical built-in module can be `mixed` because it aggregates commands in
both `native-aot` and `catalogued-only` states.  Never present catalog
discovery as proof that a command or a module can execute.

### Metadata-only completion

Completion is another projection of the catalog plane, not a fourth command
discovery system and not a terminal concern. `CompletionService` depends only
on `IHelpCatalog` and `IModuleCatalog`; it provides command, parameter/alias,
direct literal `ValidateSet`, module, and help-topic suggestions through the
explicit `--complete <incomplete-line>` contract. The REPL's `complete` helper
is only a visible client of that contract.

The completion service may read source-generated built-in metadata and
declarative extension JSON through the existing catalog projections. It must
not import a module, load an extension assembly, invoke a script block or an
`ArgumentCompleter`, enumerate providers, or evaluate source metadata
expressions. Dynamic argument completion and terminal TAB bindings belong to a
future host/trust design. `CompletionInput` only tokenizes an incomplete line;
`UpstreamAstPipelineLowerer` is the sole executable syntax authority.
`ScriptParser` is a transitional upstream-AST-backed facade plus structural
stage semantic helper. It must not acquire source grammar behavior.

Completion uses the **active** module view: `Get-Module` inventory retains all
valid package versions, while help, command discovery, and module completion
use `ExtensionPackageCatalog.LoadActive`'s deterministic winner (highest dotted
version, then lexical version and package path). A completion suggestion must
never pick an arbitrary inventory version.

### Command-name collision policy

The catalog does not silently impose module precedence for two active contracts
with the same command name. `CompositeHelpCatalog.Find` returns all matching
topics in deterministic order. `Get-Help` renders every candidate and
`Get-Command` returns every candidate, preserving module/status evidence.
Completion may suggest duplicate command names with their module descriptions,
but if a complete command token resolves to more than one contract it emits
`ambiguous-command` records and withholds parameter/value completion. A future
qualified-command syntax can resolve that ambiguity; until then no component
may select `First()` as an accidental precedence rule.

Completion output is a TSV protocol. Extension metadata is untrusted text, so
`CompletionWriter` escapes backslashes, tabs, line breaks, and all other
control characters in every field before writing a record. This preserves one
physical line per suggestion without rejecting a package during catalog reads.

The current proof has a temporary hard-coded built-in implemented-command set
in the catalog and a separate adapter list in `AotCmdletRegistry`. A fail-fast
set-equality assertion already prevents their names from drifting. Retain that
assertion until one typed registration source replaces the twin lists; a new
adapter must not accidentally remain documented as `catalogued-only`.

### Dynamic extension packages

An extension package is versioned, declarative data:

```text
extensions/<module>/<version>/
  extension.json   # identity, contracts, declared execution endpoint
  help.json        # normalized help content
  provenance.json  # how registration obtained the package
```

Runtime discovery is Native-AOT safe because it uses source-generated JSON
metadata and data-only file reads.  It must never load an extension assembly,
import a `.psm1`, or run an argument completer just to provide help, command
discovery, module inventory, or tab completion.

`extension.json` is the required, authoritative command contract. `help.json`
is an optional authored overlay, not a prerequisite for discovery: for every
manifest command the catalog produces baseline syntax, parameters, validation,
output, module, status, and execution data from the manifest. Missing,
unreadable, malformed, or identity-invalid authored help is discarded and the
baseline topic identifies its generated-manifest provenance. A malformed
manifest or provenance identity remains a rejected package; only optional help
has this resilient fallback. Future diagnostics should expose discarded-help
reasons without changing catalog discovery or executing package code.

`Register-PwshAotModule.ps1` is intentionally outside the AOT host.  Standard
registration performs an explicit isolated `pwsh -NoProfile -NonInteractive`
import to collect metadata; this remains a trust boundary because module import
can execute arbitrary code.  `-Mode DeclarationOnly` reads the manifest and
script AST without importing, emits lower-confidence metadata, and must remain
`blocked` until an execution bridge and policy exist.

Current provenance records *how* metadata was discovered, not a trust verdict.
Until signature verification, content hashes, repository identity, dependency
resolution, staging, and activation rules are implemented, registered packages
are unverified and `Get-Module` must say so.

### Extension evolution rules

1. Parse a package once into a validated package model and project it into the
   help, command, and module views.  Do not let three queries implement three
   subtly different scans of `extension.json`, `help.json`, and provenance.
2. Require and validate the manifest schema/version and ensure manifest,
   help, directory, module identity, and version agree.  Invalid packages may
   be ignored for resilience, but a future diagnostics command must expose the
   reason without importing code.
3. Define activation/version selection before accepting more than one version
   of a module.  Cataloguing every version is useful for inventory, but help
   and command lookup require an explicit active-version/winner policy so the
   same command does not become ambiguous.
4. Keep extension roots explicit for deployment.  Environment-configured roots
   are the durable mechanism; ancestor discovery is a development convenience,
   not an authorization or trust mechanism.
5. Bound untrusted data reads (path containment, file size, and JSON depth) as
   extensions move beyond a local proof.  Source-generated serialization solves
   AOT reflection, not malformed-package or resource-exhaustion policy.

### Repository discovery and installation boundary

`Find-Module` answers a third question that must stay separate from command and
installed-package discovery: **what does a configured repository advertise?**

```text
repository index JSON ──► RepositoryCatalog ──► Find-Module (read only)
                                                     │
                                                     └──► future installer staging
                                                              └──► ExtensionPackageCatalog
```

`RepositoryCatalog` does not feed `Get-Command`, `Get-Help`, completion, or
invocation. A search result is not installed, trusted, registered, or runnable.
The v1 proof accepts only local declarative index files through
`PWSH_AOT_REPOSITORIES_PATH` (or the development `repositories/` fixture), with
bounded source-generated JSON, schema validation, duplicate identity checks,
control-character rejection, HTTPS/file URIs without user-info, and no
credential field. An explicit environment path is authoritative, so nearby
development fixtures are not silently merged into a configured boundary. It
makes **no HTTP call**, downloads nothing, and imports or executes no module
code.

Do not turn `Find-Module` into a convenience installer. `Install-Module` is an
explicit opt-in staging operation, currently implemented only for an already
present local directory package:

```text
validated repository record (file URI + declared SHA-256)
  └─► explicit allowed package root containment
        └─► bounded/no-link content scan + hash verification
              └─► <resolved-extension-root>/.pwsh-aot-lite-staging/<guid>
                    └─► staged digest re-check + ExtensionPackageCatalog validation
                          └─► same-volume atomic activation
```

The source must fall inside `PWSH_AOT_PACKAGE_ROOTS`; activation requires
`PWSH_AOT_EXTENSIONS_ROOT`. Read-time ancestor discovery is never authority to
read arbitrary package locations or mutate a discovered `extensions/`
directory. Explicit configured roots are physically canonicalized (so `/tmp`/
`/var` aliases remain valid); every link **below** a resolved trusted root is
rejected so lexical containment cannot escape physically. Staging lives under
the resolved extension root, guaranteeing same-volume activation, while the
central `ExtensionPackageCatalog` explicitly skips that exact staging
directory during **every** recursive scan, including configured parent roots.
The proof rejects hosted file URIs, non-regular file objects (FIFOs/devices/
sockets), unsafe relative paths,
more than 32 files, files larger than 1 MiB, and more than 4 MiB total. It
rehashes both source and staged copies and fixed-time compares them with the
initial verified digest before validation/activation. An existing version with identical
verified content is idempotent; different content is an `InstallConflict`,
never an overwrite. Staging validates through the same
`ExtensionPackageCatalog` used by Get-Help/Get-Command/Get-Module, so manifest,
help, and provenance rules have exactly one authority. Cleanup after an
activated rename is best effort and must not change the reported result.

`file:` plus a SHA-256 is deliberately **not** a general trust solution. HTTP
transport, archive extraction, repository identity/signatures, user trust
decisions, dependency resolution, credentials, rollback/uninstall, and
multi-process locking remain deferred. Live PSGallery/PowerShellGet
compatibility belongs behind that transport/trust contract rather than a direct
cmdlet implementation. Installation still never imports a module, loads a DLL,
or runs a script; it only makes declarative metadata discoverable.

## Review checklist for every port and control-plane feature

Before marking work complete, a reviewer should verify:

1. The generated contract is used for command name, parameter shape, aliases,
   and parameter sets; no duplicate hand-maintained binding metadata appeared.
2. New reusable behavior is behind a narrow typed interface, not copied into a
   cmdlet.  The interface has fixture coverage for the behavior it owns.
3. Catalog discovery, executable registration, and invocation remain separate.
   Any availability/status change comes from the common model and is visible in
   `Get-Command`, `Get-Help`, and `Get-Module` where applicable.
4. Dynamic discovery is data-only and Native-AOT-safe: source-generated JSON,
   no reflection-driven deserialization, assembly loading, module import, or
   script execution on the read path.
5. Explicitly unsupported source behavior is rejected or marked deferred, never
   silently accepted.  `port-variances.json` and the per-feature note record
   the decision and the reusable lesson.
6. Tests cover the intended mode plus error/negative cases, and at least one
   published Native-AOT smoke test runs from outside the repository.
7. For parser, execution-kernel, REPL, completion-analysis, or editor-tooling
   work: AST, token, extent, and diagnostic access remains reusable by all
   language consumers; no execute-only parser wrapper or second grammar was
   added.
8. For any public failure behavior: a shared `AotDiagnostic` is emitted and
   snapshot-tested. It has a stable ID, actionable message, and source extent
   when available; no raw exception or ad-hoc console error became observable.
