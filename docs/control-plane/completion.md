# Metadata-only completion

## Status

Complete for the catalog-discovery target. `CompletionService` provides
testable suggestions through the explicit CLI endpoint:

```powershell
PwshAotLite --complete "Get-Verb -Group "
PwshAotLite --complete "Get-Command -Module Microsoft.PowerShell.Th"
PwshAotLite --complete "Get-Help Start-Th"
```

The interactive REPL exposes the same service with:

```text
complete Get-Process -In
```

The output is tab-separated `text`, `kind`, and `description`, intended as a
stable inspection seam. Untrusted field tabs, line breaks, backslashes, and
other control characters are escaped so every suggestion remains one physical
TSV line. Terminal key binding is deliberately not part of this
prototype: it is host-specific plumbing, while the service is independent of
the terminal and reusable by a future interactive host.

## What transfers

`CompletionService` projects the established metadata authority rather than
creating a fourth discovery implementation:

```text
CompositeHelpCatalog     -> command names, help topics, parameter names/aliases
CompositeModuleCatalog   -> active registered module names
HelpParameter validation -> direct ValidateSet values
```

That covers all 290 built-in source contracts and the active, registered
Archive, ThreadJob, and declaration-only KubeCtl packages. It supplies:

- command-name suggestions, including catalogued-but-not-runnable commands;
- parameter names and aliases for any known catalog command;
- literal `ValidateSet` values when the static metadata preserves a direct
  value (for example `Get-Verb -Group Common`);
- module names for `Get-Command -Module` and `Get-Module`; and
- command/help-topic names for `Get-Help` and `Get-Command -Name`.

Completion retains the catalog availability in each command/help suggestion.
Discoverability is therefore never presented as an execution guarantee.

`Get-Module` deliberately inventories every valid package version. Completion
does not: it uses the same deterministic active-version winner as `Get-Help`
and `Get-Command` (highest dotted version, then lexical version and package
path). A module completion suggestion therefore cannot be an arbitrary
inventory-version selection.

Every schema-valid extension manifest participates even if authored `help.json`
is absent, corrupt, unreadable, or fails manifest identity validation. The
catalog projects its command contract as generated baseline help; completion
therefore still sees its command, parameter/alias, and direct `ValidateSet`
metadata. The suggestion description identifies this as generated-manifest
provenance. A malformed manifest or provenance remains rejected.

The catalog has an explicit collision policy. It retains every active contract
with the same command name: `Get-Help` renders all candidates and
`Get-Command` lists all candidates. When a complete command name is ambiguous,
completion emits `ambiguous-command` candidates with module descriptions and
withholds parameter/value suggestions. It never picks a parameter shape by
catalog ordering; a future module-qualified command syntax must resolve that.

## What does not transfer

PowerShell completion can execute custom `ArgumentCompleter` script blocks,
call native completers, enumerate providers/filesystem paths, and use live
session state. The AOT service intentionally does none of those things.

- Dynamic `ArgumentCompleter` and `Register-ArgumentCompleter` execution is
  deferred: executing it would violate the no-code-on-discovery boundary.
- Path, provider, command-output, and arbitrary value completion is deferred:
  it needs a designed typed provider/value service rather than guesses in a
  command-specific completer.
- Source `ValidateSet` constructor/property expressions are not evaluated.
  Only direct literal values are suggested. This prevents completion from
  becoming a runtime source-expression evaluator.
- Terminal TAB/line-editing bindings are deferred to a host layer. The
  `--complete` endpoint is the contract that such a host consumes.

## Architecture and reuse review

1. The service accepts narrow `IHelpCatalog` and `IModuleCatalog` dependencies,
   allowing fixture catalogs and avoiding dependency on the executable adapter
   registry.
2. Completion reads `ExtensionPackageCatalog` only through those catalog
   projections. JSON remains source-generated, data-only package input; no
   module import, assembly load, reflection, or script execution was added.
3. `HelpParameter` now carries generated/manifest validation metadata. This
   makes help rendering, completion, and later static validation share the
   same contract rather than copying validation lists into completion.
4. `CompletionInput` is deliberately an incomplete-line lexer, not a second
   executable parser. `UpstreamAstPipelineLowerer` remains the single authority
   for running a command and rejects malformed syntax normally. `ScriptParser`
   is a legacy fixture helper pending removal.
5. No command adapter was added. Completion knows unimplemented and blocked
   commands because the catalog is the discovery plane, while invocation stays
   isolated behind `AotCmdletRegistry`.
6. `IModuleCatalog.FindActive` separates deterministic selection from the
   complete-version inventory API. Shared help, command, and completion lookup
   now agree on active package versions.
7. A collision returns all catalog candidates to help/command queries and an
   explicit ambiguity to completion; parameter completion never calls
   `FirstOrDefault` across multiple contracts.
8. `CompletionWriter` escapes extension-provided control characters before
   producing TSV, preventing suggestion data from injecting extra fields or
   records.

## Verification recorded

```powershell
dotnet run -c Release -- --self-test
dotnet run -c Release -- --complete "Get-Verb -Group "
dotnet run -c Release -- --complete "Get-Command -Module Microsoft.PowerShell.Th"
dotnet run -c Release -- --complete "Get-Help Start-Th"
dotnet run -c Release -- --complete "Start-ThreadJob -Thr"
```

The self-test asserts a built-in command, built-in parameter and alias,
`ValidateSet`, registered module, extension help topic, binary extension
parameter, and declaration-only KubeCtl parameter. It also creates temporary
multi-version, duplicate-command, and control-character extension packages to
assert deterministic active module selection, explicit ambiguity, and safe TSV
escaping. Native-AOT verification is recorded after publishing the updated
binary.

## Next action

When a line-editing host is introduced, bind TAB to `--complete`/the underlying
service. Do not add dynamic completion execution until the extension trust and
execution policy exists.
