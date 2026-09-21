# Find-Module control-plane port and architecture notes

## Status and source

**Complete for the read-only local repository-index target.** `Find-Module` is
not one of the 290 cmdlets generated from `../../PowerShell/src`: marketplace
behavior is owned by PowerShellGet/PSResourceGet rather than the engine source
catalogue used by this spike. It is therefore an explicit **host control-plane
contract**, not a falsely labelled source-cmdlet port.

- AOT implementation: `FindModuleCmdlet` in `HelpCatalog.cs`.
- Shared seam: `IRepositoryCatalog` and `RepositoryCatalog`.
- Protocol fixture: [`repositories/local-proof.repository.json`](../../repositories/local-proof.repository.json).
- Discovery contract: `PWSH_AOT_REPOSITORIES_PATH`, a platform-path-separated
  list of index files or directories. A directory contributes its direct
  `*.repository.json` and `repository.json` files. The checked-in
  `repositories/` directory is also found from the current/executable ancestor
  path for the development proof. When the environment variable is set, it is
  authoritative: ancestor fixtures are not silently merged into that boundary.

The versioned index has repository identity/URI and module name, version,
description, package URI, compatibility, and registration mode. There is no
credential field, executable path, script, dependency action, or install hook.

## What transferred

- Positional/`-Name` wildcard search and `-Repository` wildcard filtering.
- Typed module discovery rows: `Name`, `Version`, `Description`, `Repository`,
  `RepositoryUri`, `PackageUri`, `Compatibility`, and `RegistrationMode`.
- Multiple versions are retained and presented deterministically (newer dotted
  versions first), because search is inventory—not activation.
- `Get-Help Find-Module` and `Get-Command Find-Module` discover the host
  contract explicitly, with `native-aot` availability. This avoids a hidden
  executable command outside the unified discovery model.

Examples:

```powershell
Find-Module
Find-Module Contoso.*
Find-Module -Repository LocalProof Fabrikam.*
Find-Module Contoso.* | Select-Object Name, Version, Repository, PackageUri, Compatibility, RegistrationMode
```

## What did not transfer

This is intentionally **not** a PowerShell Gallery client and does not yet
attempt a PSGallery OData/NuGet/PSResourceGet protocol. A live repository adds
pagination, package semantics, repository identity, signing, availability,
rate limiting, package hash validation, and credential/authentication policy.
Those need a designed trust boundary, not an opportunistic HTTP call in a
metadata cmdlet.

`Find-Module` never:

- sends credentials or reads a credential store;
- downloads a package, follows a package URI, or installs anything;
- imports a module, runs `pwsh`, loads a DLL, or evaluates a script; or
- accepts PowerShellGet modes such as `-AllowPrerelease` until their metadata
  semantics and policy have an explicit AOT design.

The result's `PackageUri` is only data for a future installer. `Compatibility`
and `RegistrationMode` are publisher/index claims, not a trust verdict and not
evidence that a command can execute.

## Input hardening and reusable design

`RepositoryCatalog` is separate from `ExtensionPackageCatalog` on purpose:
the first searches uninstalled advertised metadata; the latter validates what
is already installed locally. Joining them would make a read-only search look
like installation or let repository data affect command discovery.

The repository reader uses the existing Native-AOT source-generated JSON
context and rejects indexes with:

- an unknown schema/version, malformed JSON, more than 1 MiB of input, or
  nesting beyond 16 levels;
- duplicate `(module name, version)` identities;
- control characters/oversized metadata fields;
- URI schemes other than `https` or `file`, or URI user-info (credentials); and
- unknown registration modes.

Malformed/unreadable indexes are skipped so one user-controlled source cannot
break a valid repository. The query itself has no network client, which is the
critical safety property at this stage.

## Architecture self-review

1. `IRepositoryCatalog` is narrower than module/extension discovery and has
   no dependency on `AotCmdletRegistry`; fixture catalogs can replace it.
2. Installed packages remain exclusively under `ExtensionPackageCatalog`; a
   repository result cannot become a command/help topic or invocation target.
3. The only dynamic input is bounded declarative JSON through a
   source-generated serializer. No reflection, assembly load, module import,
   `pwsh` sidecar, HTTP request, or script execution was added to the read path.
4. The hand-authored descriptor is declared as a host contract and kept out of
   the generated PowerShell-source count, preserving the meaning of the 290
   source contracts.
5. Unknown PowerShellGet/PSResourceGet switches fail in the parser rather than
   being accepted and ignored.

## Verification

Managed fixtures create valid, schema-invalid, malformed, control-character,
and credential-bearing indexes. They assert that only two safe versions remain,
that repository/name filtering emits typed records, that `Get-Help` and
`Get-Command` expose the command, and that `-AllowPrerelease` rejects.

```powershell
dotnet run -c Release -- --self-test
dotnet run -c Release -- -Command 'Find-Module Contoso.* | Select-Object Name, Version, Repository, PackageUri'

$env:LIBRARY_PATH = '/opt/homebrew/opt/openssl@3/lib:/opt/homebrew/opt/brotli/lib'
dotnet publish -c Release -r osx-arm64 --self-contained true -o artifacts/osx-arm64
Push-Location /tmp
& /Users/ameerdeen/progs/pwsh-spikes/pwsh-aot-lite/artifacts/osx-arm64/PwshAotLite -Command 'Find-Module Contoso.*'
Pop-Location
```

## Variances and next action

See `Find-Module` in [`port-variances.json`](../../port-variances.json).

`Install-Module` now supplies the first explicit local-file staging proof, but
`Find-Module` still performs none of those writes. See
[Install-Module notes](install-module.md). Remote transport, repository
identity/signature/dependency policy, and user trust decisions remain a future
installer contract.
