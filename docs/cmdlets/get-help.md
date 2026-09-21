# Get-Help port and architecture notes

## Status and source

**Static catalog and extension-discovery port complete.** `Get-Help` is the
AOT runner's command-discovery and migration-status surface, not a thin help
page for only the adapters that happen to execute today.

- Original cmdlet: [`HelpCommands.cs`](../../../../PowerShell/src/System.Management.Automation/help/HelpCommands.cs), `GetHelpCommand` (lines 26-740).
- Original help engine: [`HelpSystem.cs`](../../../../PowerShell/src/System.Management.Automation/help/HelpSystem.cs) and its alias, command, provider, class, script, help-file, and default-help providers.
- Original help model: [`HelpInfo.cs`](../../../../PowerShell/src/System.Management.Automation/help/HelpInfo.cs), plus MAML command-help parsing in `MamlCommandHelpInfo.cs` and `MamlNode.cs`.
- Generated source contract already available: `GeneratedCmdletPorts.GetHelp`.
- New required generated product: `GeneratedHelpCatalog`, one immutable entry for **every 290 discovered source cmdlets**, whether or not that cmdlet has an AOT adapter.

The current `GeneratedCmdletPorts` contract is useful input but not enough:
it has names, parameters, and some blockers, but does not carry `HelpUri`, the
command's default parameter set, XML documentation summaries, module/source
identity, authored MAML/Markdown content, or the AOT migration state.

## The target model

The static catalog is deliberately independent of the executable-command
registry:

```text
PowerShell source + pinned help content + AOT status manifest
                    │
                    ▼  (build-time generator)
        GeneratedHelpCatalog: all discovered command contracts
                    │
       ┌────────────┴────────────┐
       ▼                         ▼
Get-Help adapter            AOT command registry
all 290 topics              only implemented adapters
```

That separation is non-negotiable. For example, both commands below must be
discoverable without reflection, module loading, or PowerShell's help system:

```powershell
Get-Help Get-Process
Get-Help Get-ChildItem
```

The first should show its executable AOT surface, including known variances.
The second must show the generated PowerShell source contract and clearly say:

```text
AOT status: catalogued from source; no AOT adapter has been implemented.
```

Help therefore becomes the control plane for the porting campaign. It should
never make a catalogued command look executable, nor omit it just because it
is not executable yet.

## Generated HelpTopic contract

The generator should emit a typed immutable `HelpTopic` per source cmdlet.
This is compile-time data, not `PSObject`, reflection metadata, runtime XML
parsing, or a dynamically loaded module.

| Field | Build-time source | Why it is needed |
| --- | --- | --- |
| Canonical command name, source class, source-relative path, module/project | existing C# declaration and file path | generic discovery and an audit trail; basename alone is not enough |
| Command category and command aliases when an explicit static alias manifest supplies them | `[Cmdlet]` declaration plus optional checked-in alias input | `-Category Cmdlet` works immediately; aliases must not be invented from parameter aliases |
| Default parameter set, all parameter sets, parameter name/type/shape/aliases, position, requiredness, validation, wildcard and pipeline flags | `[Cmdlet]` / `[Parameter]` / validation attributes and current source contract | generate syntax and `-Parameter` help for all 290 commands |
| Output type **per parameter set** | `[OutputType]` attributes | accurate contract-level `OUTPUTS` section |
| `HelpUri` | named `HelpUri` in `[Cmdlet]` | safe `-Online` URL presentation without executing a browser |
| Synopsis and parameter summaries | C# XML documentation comments | useful baseline when authored help is absent |
| Description, examples, notes, links, component/role/functionality, parameter prose | pinned, versioned MAML/Markdown snapshot when supplied | richer authored documentation, clearly marked as available/unavailable |
| AOT status, enabled AOT parameters/sets, platform limits, variance IDs, port-note link | explicit checked-in AOT status manifest; default is `CataloguedNotPorted` | distinguish original contract from what this executable can run |
| Help content coverage and culture | generated content index | deterministic fallback and honest output when only contract help exists |

`AotPortStatus` must be a source-generator input, not inferred from the
runtime adapter registry. A compact manifest can default every generated
command to `CataloguedNotPorted` and override only reviewed ports. CI should
validate that an override points at its `docs/cmdlets/<name>.md` note and its
`port-variances.json` record. This makes status reviewable and prevents a
stale adapter discovery result from changing documentation.

## Implemented direct AOT scope

The first useful implementation is fully static and does **not** require the
original provider-based `HelpSystem`.

| Mode | Phase 1 behavior | Notes |
| --- | --- | --- |
| `Get-Help [Name]` / `-Name` | exact and `*`/`?` name search across all generated built-ins and registered extension help | executable registry membership is never consulted |
| static source topics | syntax, parameters, outputs, source locator, and explicit AOT status for all 290 generated declarations | `Get-ChildItem` is intentionally discoverable despite having no adapter |
| extension topics | command contracts always come from `extensions/<module>/<version>/extension.json`; optional synopsis, description, and examples overlay from `help.json` | source-generated JSON only; missing/corrupt/identity-invalid authored help falls back visibly to generated manifest help and module DLLs are never loaded |
| terminal output | one typed `HelpRecord` renders as clean multiline prose | avoids a fake `PSObject`/format-data path |
| `-Component`, `-Role`, `-Functionality` | filter only values present in packaged authored help | source attributes do not reliably contain these labels; return no/unsupported rather than invent data |

`-Category`, `-Detailed`, `-Full`, `-Parameter`, `-Examples`, `-Online`, `-Path`, provider help, `HelpFile`, `Alias`, `Function`, `Filter`,
`ExternalScript`, `Class`, `DscResource`, `ShowWindow`, remote-runspace
rules, and dynamic script comments are deferred. They need a provider/session
or extension model and are not prerequisites for cmdlet help. `ShowWindow` is
also platform-specific in the original source and has no place in the minimal
headless AOT core.

## Content and localization policy

There are two honest coverage tiers:

1. **Contract help (required for every catalogued cmdlet).** Generated from
   source declarations and XML documentation: synopsis when available, syntax,
   parameters, output types, source locator, `HelpUri`, and AOT status. This is
   sufficient for generic discovery of all 290 commands.
2. **Authored help (optional per topic/culture).** A pinned, versioned MAML or
   Markdown input snapshot is parsed by the source generator at build time into
   a typed help section. No network retrieval, runtime MAML/XML parser,
   `ResourceManager`, or dynamic help-module discovery is allowed in the
   published executable.

Ship `en-US` authored content first. The generator should resolve culture
deterministically as `exact culture -> parent culture -> en-US -> contract`,
and display which tier/culture was selected. Additional cultures are a build
input decision: generating them all is technically feasible but grows the
native binary, so do not silently claim localised prose that was not packaged.
Source strings such as error text can remain separately localised; that is
distinct from authored help topic content.

The current PowerShell repository contains the help engine and test assets,
not a complete checked-in release MAML corpus for all built-in cmdlets. A
pinned documentation snapshot (or an AOT-owned normalized help input) is
therefore a required build input for rich help, not a runtime dependency to
hide behind `Get-Help`.

## What must change before or beside normal cmdlet ports

1. **Extend the generator contract.** Preserve source-relative path, module
   identity, `HelpUri`, default parameter set, parameter-set-aware output
   types, and XML-doc summaries/parameter descriptions. The existing manifest
   extracts only a subset of these.
2. **Add the explicit status/content inputs.** Introduce a generator-owned
   `aot-port-status.json` (or equivalent static input) and a pinned help
   content directory. Validate command names, doc links, variance IDs, and
   status values at build time.
3. **Generate `GeneratedHelpCatalog` independently.** It must compile all 290
   entries even when no adapter exists. The runtime registry stays responsible
   only for invocation.
4. **Add a narrow `IHelpCatalog` and `GetHelpCmdlet`.** It consumes static
   records and emits typed `HelpTopicRecord` / `HelpParameterRecord`; no
   `HelpSystem`, `PSObject`, `SessionState`, or provider APIs cross the AOT
   boundary.
5. **Add generic discovery/rendering.** The parser needs `Get-Help` and its
   supported modes; the renderer needs a stable help text/table layout. This
   is more valuable now than a general dynamic object system.
6. **Make every future port update status rather than hand-writing help.** A
   port may change only its AOT status override, supported parameter-set list,
   variances, and optional examples. The catalog itself continues to derive
   original syntax from the source.

These are feasible build-time/static-runtime tasks. The blockers are only the
original system's dynamic extensibility, runtime provider discovery, script
help parsing, and live help update/download; none block a generic built-in
cmdlet catalog.

## Verification

Managed and Native AOT fixtures prove:

```powershell
# Unimplemented command: still shows source contract and explicit status.
Get-Help Get-ChildItem

# Registered compiled extension: help and contract are dynamic JSON data.
Get-Help Start-ThreadJob
```

Fixtures assert that the catalog count is 290, `Get-ChildItem` resolves when no
executable adapter is registered, an unported topic never reports itself as
executable, and the registered binary `ThreadJob` package supplies authored
help without loading its assembly. Run them again from the published Native AOT
executable.

## Variances and reusable learnings

See `Get-Help` in [`port-variances.json`](../../port-variances.json).

The original `GetHelpCommand` is a dispatcher over the full dynamic PowerShell
help system. That dynamic layer is not the goal of this port. The right AOT
replacement is a complete, generated, provenance-carrying catalog plus a
small static query/rendering adapter. This is not merely another cmdlet: it is
the discovery contract that lets agents and users see the complete migration
surface without mistaking the current runtime subset for the repository.

## Next action

Add explicit parameter/category/view query support only after its behavior is
represented by a typed catalog query contract. Keep module registration and
execution bridging separate from help discovery.
