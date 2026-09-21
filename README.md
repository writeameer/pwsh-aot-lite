# pwsh-aot-lite

A deliberately focused, Native-AOT-friendly PowerShell-like runner. It proves the replacement execution model, not compatibility with all of PowerShell.

```text
script text → upstream parser facade → AOT execution plan → precompiled C# execution
```

The current plan is deliberately a narrow structural pipeline, lexical-variable,
conditional, and closed-list `foreach` slice, not a claim of general script compatibility. Parse,
binding, scope, and unsupported-feature failures use typed source-aware diagnostics.
See the [AOT Execution Kernel foundation](docs/architecture/aot-execution-kernel.md)
and [Language Compatibility Core](docs/architecture/language-compatibility-core.md).

Supported pipeline:

```powershell
Get-Process | Where-Object CPU -gt 10 | Select-Object Name, Id, CPU
```

The first multi-statement/variable form is also executable:

```powershell
$threshold = 10
$names = 'pwsh', 'dotnet'
Get-Process -Name $names | Where-Object CPU -gt $threshold | Select-Object Name, Id
```

The first conditional form is also executable:

```powershell
$enabled = $true
if ($enabled) {
    Get-Verb -Group Common | Select-Object Verb
}
elseif ($false) {
    Get-Verb -Group Filter
}
else {
    Get-Verb -Group Common
}
```

The first synchronous loop form is also executable. Its collection must be a
closed comma-list, not command output or an arbitrary .NET enumerable:

```powershell
$verbs = 'Add', 'Get'
foreach ($verb in $verbs) {
    Get-Verb -Verb $verb | Select-Object Verb
}
```

Diagnostics are plain by default in redirected output. In an interactive
terminal, choose `--color auto` (default), `--color always`, or `--color never`:

```powershell
PwshAotLite --color always -Command "Get-Verb -Group `$missing"
```

`Get-Process` is now a port through a reusable `IAotCmdlet` boundary, rather than a hard-wired pipeline source. It supports the existing cmdlet's core process selection shapes:

```powershell
Get-Process
Get-Process -Name pwsh*
Get-Process -Id 123,456
Get-Process -IncludeUserName | Select-Object Name, Id, UserName
Get-Process -Module -FileVersionInfo | Select-Object ModuleName, FileVersion
Get-Process -Name pwsh* | Where-Object CPU -ge 0 | Select-Object Name, Id
Get-Verb Get* -Group Common | Select-Object Verb, AliasPrefix, Group
Get-TimeZone -ListAvailable | Select-Object Id, StandardName, BaseUtcOffset
Get-TimeZone -Name "Gulf Standard Time" | Select-Object Id, StandardName
Get-Date -UnixTime 0 -AsUTC -Format FileDateTimeUniversal
Get-Date -Date 2024-02-29T12:34:56 -UFormat '+%Y-%m-%dT%H:%M:%S'
Get-FileHash README.md -Algorithm SHA512
Get-FileHash -LiteralPath 'file-with-*.txt' -Algorithm MD5
Get-Help Get-ChildItem
Get-Help Start-ThreadJob
Get-Help Get-KubeResource
Get-Command Get-ChildItem
Get-Command -Module Microsoft.PowerShell.ThreadJob
Get-Command Get-Kube* -CommandType Function
Get-Module
Get-Module Microsoft.PowerShell.ThreadJob | Select-Object Name, Version, Availability, Trust, Provenance
Find-Module Contoso.* | Select-Object Name, Version, Repository, PackageUri, Compatibility, RegistrationMode
PwshAotLite --complete "Get-Verb -Group "
PwshAotLite --complete "Get-Command -Module Microsoft.PowerShell.Th"
PwshAotLite --complete "Get-Help Start-Th"
```

The port preserves explicit static parameter metadata, `Name`/`Id`/pipeline-object selection modes, duplicate removal, name/id sort order, wildcard selection, and non-terminating missing-process errors. It supports `-IncludeUserName`, `-Module`, `-FileVersionInfo`, and their valid module/file-version combination in the macOS AOT target. The current wildcard subset is `*` and `?`.

The repeatable mapping for the next cmdlet is in [PORTING.md](PORTING.md).

## Development prerequisites

This spike intentionally reads a pinned upstream PowerShell source tree at
build time to extract static cmdlet contracts; it does not reference the
PowerShell SDK at runtime. A fresh clone restores the exact source commit into
the ignored `.upstream/` directory:

```text
pwsh-aot-lite/
├── tools/PwshAotPortGenerator/  # checked-in compile-time contract generator
└── .upstream/PowerShell/         # restored, ignored pinned source checkout
```

The project targets .NET 10. Bootstrap and verify a clean clone with:

```powershell
pwsh -NoProfile -File eng/Restore-Upstream.ps1
dotnet build -c Release
dotnet run -c Release -- --self-test
pwsh -NoProfile -File tools/Test-ParserReuseGuard.ps1
pwsh -NoProfile -File tools/Export-PwshParserBaseline.ps1 -Verify
```

To reuse an existing PowerShell checkout instead, pass
`-p:PowerShellSourceRoot=/absolute/path/to/PowerShell/src` to `dotnet`.
The pinned source specification, provenance, and parser-reuse gates remain in
[PARSER-REUSE-GUARD.md](PARSER-REUSE-GUARD.md).

## Generated cmdlet metadata

`tools/PwshAotPortGenerator` is an incremental C# source generator. At build
time it scans every C# source file in the restored upstream PowerShell source,
including declarations
inside platform-specific branches, and generates static metadata for the 290
semantic cmdlet declarations currently discovered. The generated output is
available under `obj/Generated/`.

`GetProcessCmdlet` consumes `GeneratedCmdletPorts.GetProcess`, so its command
name, `Name`/`Id` parameter metadata, and `ProcessName`/`PID` aliases come from
the existing PowerShell source instead of a duplicated hand-written descriptor.

`GetVerbCmdlet` consumes `GeneratedCmdletPorts.GetVerb` and a generated,
100-entry verb catalogue. The generator reads the original `Verbs.cs` constants
and `VerbDescriptionStrings.resx` at build time, so the native executable does
not use reflection or `ResourceManager` to implement `Get-Verb`. Its current
`-Verb` wildcard subset is case-insensitive `*` and `?`; character classes and
escaping remain a documented shared-runtime gap.

The migration design, preserved PowerShell semantics, and non-negotiable port
rules are in [ARCHITECTURE.md](ARCHITECTURE.md).

`GetTimeZoneCmdlet` consumes `GeneratedCmdletPorts.GetTimeZone` and translates
the source's local default, `-ListAvailable`, `-Id`, and standard/daylight-name
lookup paths through an injectable `ITimeZoneCatalog`. It deliberately uses the
shared `*`/`?` wildcard subset for `-Name`; Windows and IANA zone IDs remain
platform data, not a portable hard-coded catalogue.

`GetFileHashCmdlet` consumes `GeneratedCmdletPorts.GetFileHash` and hashes
direct physical filesystem files using the BCL's static SHA1/SHA256/SHA384/
SHA512/MD5 APIs. `-Path` supports terminal filename `*`/`?` matching and
`-LiteralPath` does not interpret wildcard characters. PowerShell providers,
wildcards in directory components, `InputStream`, and generic pipeline binding
are explicitly deferred; this runner never treats their strings as physical
files by accident.

For future implementation agents, the step-by-step operational guide is
[AGENTS.md](AGENTS.md).

Per-cmdlet migration evidence and reusable learnings are indexed in
[docs/README.md](docs/README.md).

## Generic help catalog

`Get-Help` is intentionally independent of the executable cmdlet registry. At
build time the source generator emits a static catalog for all 290 discovered
PowerShell cmdlets, so `Get-Help Get-ChildItem` reports its syntax and clearly
labels it as catalogued-but-unimplemented. At runtime the same command scans
`extensions/<module>/<version>/extension.json`, with `help.json` as an
optional authored overlay. A missing or corrupt help file falls back visibly to
generated manifest-contract help. That means a registered third-party module
can surface help without loading its DLL or requiring an executable AOT adapter:

```powershell
dotnet run -- -Command "Get-Help Start-ThreadJob"
dotnet run -- -Command "Get-Help Get-KubeResource"
dotnet run -- -Command "Get-Help Get-ChildItem"
```

The KubeCtl package proves the safe fallback for a module whose initializer is
not safe to run during registration: it was discovered from a manifest plus
static `.psm1` AST only and therefore reports `declaration-only /
blocked-not-imported`. That is usable help discovery, not an execution bridge.
See [extension registration notes](docs/extensions/README.md) for the package
states and trust boundary.

## Generic command catalog

`Get-Command` is the metadata companion to `Get-Help`: it queries the same
full built-in source catalog and dynamic extension manifests, rather than only
the commands with a native adapter today. Its Native-AOT subset is deliberately
small and explicit:

```powershell
Get-Command [<name>|-Name <name>] [-Module <pattern>] [-CommandType Cmdlet|Function]
```

It emits typed `Name`, `CommandType`, `ModuleName`, `Version`, `Source`,
`Availability`, `Status`, and `Syntax` properties. `Availability` is in the
default view: `native-aot`, `catalogued-only`, `legacy-bridge-pending`, or
`blocked`. A catalogued command is discoverable, not necessarily runnable. A
registered binary module therefore
appears safely before an execution bridge exists:

```powershell
dotnet run -- -Command "Get-Command Start-ThreadJob"
dotnet run -- -Command "Get-Command -Module Microsoft.PowerShell.ThreadJob"
```

Live session-state discovery and the remaining original `Get-Command` modes
are explicitly rejected, never accepted and ignored. See the
[Get-Command port notes](docs/cmdlets/get-command.md).

## Module inventory

`Get-Module` is a metadata inventory over the same static control plane. It
does not perform `$PSModulePath` discovery or import code. It returns the
logical `PowerShell.BuiltIn` source catalog, the explicit
`PwshAotLite.ControlPlane` host contract, and each installed declarative
extension package (including every valid installed version):

```powershell
Get-Module [<name>|-Name <name>]
Get-Module Microsoft.PowerShell.ThreadJob | Select-Object Name, Version, Origin, Availability, Status, Path, Trust, Provenance
```

The `Trust` field deliberately reports `unverified` for registered extensions:
the current proof records provenance but has no package signature verification
or dependency resolver. This keeps the future `Install-Module` trust boundary
visible instead of implying one exists. See [Get-Module port notes](docs/cmdlets/get-module.md).

`Get-Help` and `Get-Command` select one active extension version per module:
the highest valid dotted version, then lexical version and package path as
deterministic tie-breakers. All three commands use one schema/identity-validating
package scanner; malformed manifests/provenance are ignored without loading
extension code, while optional malformed help falls back to generated manifest
contract help.

Set `PWSH_AOT_EXTENSIONS_ROOT` to one explicit extension root for deployment,
or `PWSH_AOT_EXTENSIONS_PATH` to platform-path-separated roots. Discovery is
anchored to the executable as well as the current directory: development
publishes find the repository's sibling `extensions/` directory even when the
binary is launched from another directory. The initial adapter accepts only
positional `Name` or `-Name`; it explicitly rejects the original cmdlet's other
dynamic help modes rather than silently accepting them.

## Repository discovery

`Find-Module` is a separate, read-only repository search plane—not installed
extension discovery and not an installer:

```powershell
Find-Module [<name>|-Name <name>] [-Repository <pattern>]
Find-Module Contoso.* | Select-Object Name, Version, Repository, PackageUri, Compatibility, RegistrationMode
```

It reads a small declarative repository-index v1 JSON document from
`PWSH_AOT_REPOSITORIES_PATH` (a platform-path-separated list of files or
directories). The checked-in `repositories/local-proof.repository.json` gives
the development binary an offline sample. The command has no HTTP client, does
not contact PSGallery, sends no credentials, does not download/install anything,
and never imports a module. It validates bounded untrusted JSON and presents a
package URI only as data for a future explicit installer. The design and
deferred trust/transport work are documented in the [Find-Module notes](docs/cmdlets/find-module.md).

## Metadata completion

Completion is a data-only projection of the same help and module catalogs. Use
the explicit native-host endpoint (or `complete <line>` in the REPL) to inspect
suggestions without terminal-specific TAB handling:

```powershell
./artifacts/osx-arm64/PwshAotLite --complete "Get-Process -In"
./artifacts/osx-arm64/PwshAotLite --complete "Get-Verb -Group "
./artifacts/osx-arm64/PwshAotLite --complete "Get-Command -Module Microsoft.PowerShell.Th"
./artifacts/osx-arm64/PwshAotLite --complete "Get-Help Start-Th"
```

It offers command names, parameter names and aliases, direct static
`ValidateSet` values, module names, and help topics from the full built-in and
registered extension catalogs. It never imports a module, loads an extension,
or invokes a script/argument completer. Dynamic completers, provider/path and
arbitrary value completion, and terminal TAB binding are deferred. See the
[completion design note](docs/control-plane/completion.md).

Supported filters use `CPU`, `Id`, or `WorkingSet` with `-gt`, `-ge`, `-lt`, `-le`, `-eq`, or `-ne`. Supported output columns are `Name`, `Id`, `CPU`, and `WorkingSet`.

Run locally:

```powershell
dotnet run
dotnet run -- --self-test
dotnet run -- -Command "Get-Process | Where-Object CPU -gt 10 | Select-Object Name, CPU"
```

Running with no arguments starts an interactive one-line REPL. Type `help` for
the available grammar and `exit` to leave.

Publish Native AOT:

```powershell
dotnet publish -c Release -r <target-rid> --self-contained true
```

On this Apple Silicon macOS machine, Homebrew supplies the OpenSSL and Brotli
native link dependencies. Publish and run the local executable with:

```powershell
$env:LIBRARY_PATH = '/opt/homebrew/opt/openssl@3/lib:/opt/homebrew/opt/brotli/lib'
dotnet publish -c Release -r osx-arm64 --self-contained true -o artifacts/osx-arm64
./artifacts/osx-arm64/PwshAotLite --self-test
```

Reproduce the Linux ARM64 AOT build and run it entirely in Docker:

```powershell
docker build --build-arg TARGET_RID=linux-arm64 -t pwsh-aot-lite .
docker run --rm pwsh-aot-lite --self-test
docker run --rm pwsh-aot-lite -Command "Get-Process | Where-Object CPU -ge 0 | Select-Object Name, Id, CPU"
```

The checked-in spike was verified by publishing a Linux ARM64 native executable
and running both commands above. `CPU -ge 0` makes the live-container example
deterministic; the self-test includes the exact `CPU -gt 10` filter.

There are no PowerShell SDK dependencies, reflection calls, dynamic assembly loads, expression compilation, or runtime code generation.
