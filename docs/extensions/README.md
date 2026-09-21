# Extension registration notes

This directory records experiments that turn an installed PowerShell module
into a discoverable `pwsh-aot-lite` extension package. These are compatibility
and discovery experiments, not claims of Native AOT execution.

## Package contract, v1

`tools/Register-PwshAotModule.ps1` writes one immutable-by-version package:

```text
extensions/<module-name>/<module-version>/
  extension.json   # command contracts and execution status
  help.json        # optional normalized authored help overlay
  provenance.json  # module identity and sidecar-registration evidence
```

The default registration tool launches a fresh `pwsh -NoProfile -NonInteractive`
sidecar, imports the requested module there, and reads its exported command
metadata and `Get-Help -Full` results. It never invokes an exported command.
The target AOT host is not involved in this step.

`extension.json` is sufficient for discovery. If `help.json` is missing,
unreadable, malformed, or fails its identity checks, the AOT host still exposes
every manifest command through `Get-Help`, `Get-Command`, and completion using
generated contract help (syntax, parameters, validation, outputs, module, and
execution status). `Get-Help` labels that topic's source as generated manifest
help. A malformed manifest or invalid provenance identity remains rejected;
the fallback applies only to optional authored help.

Some script modules are unsafe or impossible to import in a generic inspection
environment. `-Mode DeclarationOnly` is the non-executing fallback: it reads
the constrained `.psd1` manifest and parses the `.psm1` AST, without importing
the module, evaluating its body, or invoking an external dependency. It emits
the same three JSON documents but uses `execution.kind: declaration-only` and
`execution.status: blocked-not-imported`. This supplies lower-confidence
manifest/AST help only; it is not a legacy invocation bridge.

`extension.json` is intentionally explicit that this first format is a
`legacy-pwsh-sidecar` registration with status `registered-not-invoked`.
Discovery and help are proven; invocation needs a separately designed,
versioned bridge and trust policy.

## Experiments

| Module | Result | Notes |
| --- | --- | --- |
| `Microsoft.PowerShell.Archive` | registered; two functions catalogued | [Archive proof](microsoft-powershell-archive.md) |
| `Microsoft.PowerShell.ThreadJob` | registered; one compiled cmdlet catalogued with authored help | [ThreadJob proof](microsoft-powershell-threadjob.md) |
| `Microsoft.PowerShell.KubeCtl` | declaration-only package; seven static exports catalogued, execution blocked | [KubeCtl proof](microsoft-powershell-kubectl.md) |

## Registration command

Run this with the regular installed PowerShell, not the AOT binary:

```powershell
pwsh -NoLogo -NoProfile -File ./tools/Register-PwshAotModule.ps1 -Name Microsoft.PowerShell.Archive
```

The same command works for a binary module; for example:

```powershell
pwsh -NoLogo -NoProfile -File ./tools/Register-PwshAotModule.ps1 -Name Microsoft.PowerShell.ThreadJob
```

For a script module that cannot safely be imported, choose the explicit static
fallback. A known prior full-inspection failure can be retained as provenance
without retrying it:

```powershell
pwsh -NoLogo -NoProfile -File ./tools/Register-PwshAotModule.ps1 `
  -Name Microsoft.PowerShell.KubeCtl -Mode DeclarationOnly `
  -PreviousImportFailure 'prior sidecar inspection failed; not retried'
```

For a specific module manifest, use `-ModulePath`. `-ExtensionsRoot` redirects
only the generated package root, which is useful for isolated tests.

## Security and scope

Importing a PowerShell module executes its import-time code. The fresh sidecar
contains that risk outside the AOT process, but it does not make an untrusted
module safe. Declaration-only parsing avoids that import but has intentionally
incomplete metadata and cannot inspect dynamically generated exports.
Registration should remain an explicit user action and a future installer
should add signature, hash, and provenance policy before automatic use.
Help/contract inspection cannot safely infer the behavior of script code or
native binaries; it only describes the available interface.
