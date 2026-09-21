# Install-Module control-plane port and architecture notes

## Status and source

**Complete for the deliberately narrow local-package installation target.**
Like `Find-Module`, this is a PwshAotLite host control-plane contract, not one
of the generated 290 PowerShell-source cmdlets. It does not claim
PowerShellGet/PSResourceGet compatibility.

- AOT adapter: `InstallModuleCmdlet` in `ModuleInstaller.cs`.
- Reusable seam: `IModuleInstaller`; current implementation is
  `LocalPackageModuleInstaller` over `IRepositoryCatalog`.
- Package validator: the existing central `ExtensionPackageCatalog`, used on a
  staged copy before activation.

The command is intentionally small:

```powershell
$env:PWSH_AOT_REPOSITORIES_PATH = '/path/to/repositories'
$env:PWSH_AOT_PACKAGE_ROOTS = '/path/to/approved-package-directories'
$env:PWSH_AOT_EXTENSIONS_ROOT = '/path/to/extensions'

Install-Module Contoso.Tools -Repository LocalProof
```

Only one exact module name and optional exact repository are accepted. The
selected record must be the sole newest version advertised by the repository.

## What transferred

- A distinct discovery/install lifecycle: `Find-Module` advertises package
  metadata; `Install-Module` is the only current command that may write an
  extension root.
- Installed packages become immediately visible through the existing
  `Get-Module`, `Get-Command`, `Get-Help`, and completion catalog paths. Those
  paths remain data-only and still do not execute the installed command.
- Clear typed result fields: `Name`, `Version`, `Repository`, `Status`, `Path`,
  and `ContentSha256`.
- Deterministic existing-version handling: equal verified content is
  `already-installed`; differing content is `InstallConflict` and is never
  overwritten.

## Safety design and failed approaches avoided

The installer consumes only a hostless `file:` package URI. `https:` results discovered
by `Find-Module` are deliberately refused rather than opportunistically
downloaded. A file URI is **not** sufficient authority: its resolved package
directory must also be contained by an explicit, non-symlink
`PWSH_AOT_PACKAGE_ROOTS` entry. Explicit roots are physically canonicalized,
so legitimate macOS aliases such as `/tmp` and `/var` remain usable; links
below the resolved trusted root are rejected, preventing an intermediate child
symlink from escaping. Writes require explicit
`PWSH_AOT_EXTENSIONS_ROOT`; ancestor discovery roots are read conveniences and
are never write targets.

Before copying, the source package is recursively scanned with these v1 limits:

- 32 files maximum, 1 MiB each, 4 MiB total;
- no symbolic links anywhere in the source, configured roots, or target;
- package directories and regular files only—FIFOs, devices, sockets, and
  other non-regular non-directory objects are rejected before hashing or
  copying; and
- only safe relative path segments—no rooted/traversal paths; and
- a deterministic SHA-256 over sorted relative paths and file bytes, which
  must match `packageSha256` in the repository record.

The copy enters
`<resolved-extension-root>/.pwsh-aot-lite-staging/<guid>`. The centralized
extension catalog computes that physical staging root once from the explicit
write root and excludes it from **every** configured discovery-root scan,
including a parent scan. An incomplete package therefore remains
undiscoverable while staging is guaranteed to share the activation filesystem.
The staged
`extension.json`, optional `help.json`, and optional `provenance.json` go
through `ExtensionPackageCatalog.TryLoadPackage`, then the manifest identity
must equal the repository name/version. The staged content is independently
re-scanned and fixed-time compared with the source digest before catalog
validation. Only then does a same-volume directory rename activate it. Any
failure removes staging and leaves no visible extension package; cleanup after
a successful activation is best effort and cannot turn a success into failure.

This deliberately avoids the unsafe shortcuts that would undermine the AOT
boundary: `Import-Module`, DLL loading, script execution, archive extraction,
silent version overwrite, direct copy into an active extension directory, and
network/credential handling.

## What did not transfer

This is not a package manager yet. Deferred by design:

- HTTP/NuGet/PSGallery/PSResourceGet transport, redirects, retries, paging,
  authentication, credential stores, and repository bootstrap;
- signatures/certificates, signed repository identity, transparency data, and
  user trust prompts (a SHA-256 is integrity evidence only after a trusted
  repository provides it);
- dependencies, conflicts, prerelease/range selection, uninstall/rollback,
  multi-process lock protocol, archive formats, and content-type policy;
- PowerShell module import, binary loading, script execution, and a legacy
  sidecar/native-plugin invocation bridge.

`legacy-pwsh-sidecar` in a package manifest remains an advertised future
endpoint; installation does not make it executable.

## Verification

`SelfTest` creates temporary packages and repository records, then verifies:

1. successful staging/activation and immediate catalog visibility;
2. idempotent re-install of byte-identical content;
3. a bad declared hash rejects with `InstallHashMismatch` and no destination;
4. a file URI outside `PWSH_AOT_PACKAGE_ROOTS` rejects with
   `InstallSourceOutsideRoot` and no destination; and
5. an invalid manifest rejects with `InstallPackageInvalid` before activation.
6. a tampering staging seam is invisible to every catalog projection and
   rejects with `InstallStagedHashMismatch` after copy;
7. source/destination intermediate symlink escapes and hosted `file:` URIs
   reject; and
8. bounds, differing-content conflict, and cleanup-failure seams preserve the
   no-overwrite/no-partial-activation result.
9. the activation-volume seam rejects a simulated mount-point mismatch, while
   a real configured platform root alias succeeds.
10. FIFO/special-file, mixed logical/physical root alias child-link, and
    source-changed-after-copy fixtures reject with stable IDs before activation.

```powershell
dotnet run -c Release -- --self-test

$env:LIBRARY_PATH = '/opt/homebrew/opt/openssl@3/lib:/opt/homebrew/opt/brotli/lib'
dotnet publish -c Release -r osx-arm64 --self-contained true -o artifacts/osx-arm64
Push-Location /tmp
& /Users/ameerdeen/progs/pwsh-spikes/pwsh-aot-lite/artifacts/osx-arm64/PwshAotLite --self-test
Pop-Location
```

## Architecture self-review and reusable learnings

1. `IModuleInstaller` owns mutation and depends on read-only
   `IRepositoryCatalog`; neither `Find-Module` nor help/command/module catalog
   code can install as a side effect.
2. `ExtensionPackageCatalog` remains the single authoritative manifest/help/
   provenance validator. The installer validates a staged package through that
   path rather than reproducing a second schema implementation.
3. The explicit roots make the local filesystem trust boundary reviewable and
   testable. The installer does not inherit read-time ancestor discovery.
4. A catalog-excluded in-root staging directory plus source/staged post-copy
   digests closes
   the package-copy integrity gap, preserves same-volume atomic activation,
   and prevents partially copied packages from being discovered.
   Existing unequal content is a conflict, never an overwrite.
5. The content-hash algorithm is surfaced only for fixture/package producers;
   it retains the same bounds and link policy as installation rather than
   becoming an unrestricted file-hashing API.

See `Install-Module` in [`port-variances.json`](../../port-variances.json).
