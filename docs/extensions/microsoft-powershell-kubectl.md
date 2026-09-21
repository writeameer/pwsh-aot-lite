# Microsoft.PowerShell.KubeCtl declaration-only registration proof

## Status

**Registered for `Get-Help` through a declaration-only package; execution is
explicitly blocked.** This is intentionally different from Archive and
ThreadJob: no `Import-Module` occurred during this registration.

## Source observed without import

- Module: `Microsoft.PowerShell.KubeCtl` version `0.0.3`, type `Script`.
- Manifest:
  `/Users/ameerdeen/.local/share/powershell/Modules/Microsoft.PowerShell.KubeCtl/0.0.3/Microsoft.PowerShell.KubeCtl.psd1`.
- Root script: `Microsoft.PowerShell.KubeCtl.psm1`.
- The constrained manifest declares seven exported functions:
  `Get-KubeResource`, `Initialize-ProxyFunction`, `Get-DefaultPSSession`,
  `Set-DefaultPSSession`, `Get-KubeRequireSudo`, `Set-KubeRequireSudo`, and
  `Invoke-KubeCtl`.

`Register-PwshAotModule.ps1 -Mode DeclarationOnly` reads that manifest with
`Import-PowerShellDataFile`, parses the `.psm1` with PowerShell's AST parser,
and reads only static function definitions, parameter declarations, and
comment-based help. It does not execute the module body, exported functions,
PowerShell classes, script-block creation, or `kubectl`.

## What worked

The registrar emitted a valid package:

```text
extensions/Microsoft.PowerShell.KubeCtl/0.0.3/
  extension.json
  help.json
  provenance.json
```

All seven manifest exports were represented. Static AST data correctly gives
`Get-KubeResource` its `name : System.String` and `Force :
System.Management.Automation.SwitchParameter` parameters, and comment help
provides its synopsis and description. `Invoke-KubeCtl`'s seven declared
parameters are likewise captured. This is enough for the Native-AOT binary to
answer `Get-Help Get-KubeResource` without dynamic assembly loading or a
regular PowerShell runtime in the host.

`provenance.json` records `method: declaration-only-static-ast`, confirms that
the module was not imported and no command was invoked, and stores static risk
signals: `kubectl` reference, runtime script-block creation, and generated
format-data side effects. It also preserves the prior explicit full-inspection
failure instead of retrying that import.

## What did not work — by design

The package has `execution.kind: declaration-only` and
`execution.status: blocked-not-imported`. It must not be treated as an
invocation bridge or as a complete command contract. Dynamic proxy commands
created during import are intentionally absent, output types are unknown, and
static AST analysis cannot prove command semantics.

An earlier, separate full-contract registration attempted an isolated import.
Import initialization called `Get-KubeResource`, which called `kubectl
api-resources -o wide`, attempted to generate format data in the installed
module directory, and failed parsing the live output (`length ('-37') must be
a non-negative value`). That failure is why this package is declaration-only;
the safe mode did not repeat it.

## Verification actually run

```powershell
$failure = 'Full contract sidecar attempt (2026-09-21): Get-KubeResource failed while parsing kubectl api-resources output: length (-37) must be non-negative.'
pwsh -NoLogo -NoProfile -NonInteractive -File ./tools/Register-PwshAotModule.ps1 `
  -Name Microsoft.PowerShell.KubeCtl -Mode DeclarationOnly `
  -PreviousImportFailure $failure -ExtensionsRoot ./extensions

$p = './extensions/Microsoft.PowerShell.KubeCtl/0.0.3'
$e = Get-Content "$p/extension.json" -Raw | ConvertFrom-Json -Depth 30
$h = Get-Content "$p/help.json" -Raw | ConvertFrom-Json -Depth 30
$r = Get-Content "$p/provenance.json" -Raw | ConvertFrom-Json -Depth 30
@($e.commands).Count                         # 7
($e.commands | Where-Object Name -eq Get-KubeResource).parameters.Count # 2
$r.registration.moduleWasImportedInIsolatedSidecar # False
$r.registration.import.state                 # previous-attempt-failed; not-retried

# Native AOT help lookup (launched from a non-repository directory).
Push-Location /tmp
& /Users/ameerdeen/progs/pwsh-spikes/pwsh-aot-lite/artifacts/osx-arm64/PwshAotLite `
  -Command 'Get-Help Get-KubeResource'
Pop-Location
```

## Reusable learnings

1. A manifest plus AST can supply safe, useful *declaration help* for a script
   module, but it cannot replace full sidecar inspection.
2. The package must distinguish `blocked-not-imported` from
   `registered-not-invoked`; otherwise help discovery could falsely imply that
   a legacy execution bridge is safe.
3. Static analysis is evidence for policy, not an approval mechanism. External
   commands, runtime source generation, and import-time format updates should
   trigger a user-visible review before any full inspection or execution.
4. A prior import failure belongs in provenance so future tooling can make a
   decision without silently retrying side-effectful initialization.
