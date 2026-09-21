# Microsoft.PowerShell.Archive registration proof

## Status

**Registered for catalog/help discovery; execution intentionally unimplemented.**

This is the first pressure test of the legacy-module path. The installed
module is a manifest/script module, not a compiled cmdlet assembly. It is
therefore a clean proof of the discovery contract, but not proof that compiled
cmdlets can execute in the Native AOT process.

## Source observed

- Module: `Microsoft.PowerShell.Archive` version `1.2.6`.
- Manifest:
  `/opt/homebrew/Cellar/powershell/7.6.6_1/libexec/Modules/Microsoft.PowerShell.Archive/Microsoft.PowerShell.Archive.psd1`.
- Exported commands: `Compress-Archive`, `Expand-Archive`.
- Exported command kind: PowerShell functions.
- Authored MAML help was absent. `Get-Help -Full` returned baseline syntax and
  the usual partial-help notice, with no examples.

## What worked

The explicit registrar
[`tools/Register-PwshAotModule.ps1`](../../tools/Register-PwshAotModule.ps1)
started a clean regular-PowerShell sidecar and imported the module there. It
did not invoke either archive command. It produced:

```text
extensions/Microsoft.PowerShell.Archive/1.2.6/
  extension.json
  help.json
  provenance.json
```

The generated `extension.json` records two command contracts:

| Command | Parameter sets | Parameters (including common) |
| --- | ---: | ---: |
| `Compress-Archive` | 6 | 21 |
| `Expand-Archive` | 2 | 19 |

`help.json` retains the baseline multi-syntax synopsis for each command,
including the `-WhatIf` and `-Confirm` forms. `provenance.json` records the
regular `pwsh` executable, its version, the source manifest path, and that the
module was imported in a sidecar while exported commands were not invoked.

## What did not work, and why it matters

There was no authored help content to import. This is expected for this module
and proves that the AOT catalog needs to support useful **contract help**
(syntax, parameters, source, state) even when descriptions/examples are empty.
It must not pretend that baseline `Get-Help` output is equivalent to authored
documentation.

The imported module is script code. Registering it does not convert it to
Native AOT, and the AOT runtime has no sidecar invocation dispatch yet. The
package accordingly advertises `legacy-pwsh-sidecar` plus
`registered-not-invoked`, rather than an executable capability.

## Verification actually run

```powershell
pwsh -NoLogo -NoProfile -File ./tools/Register-PwshAotModule.ps1 -Name Microsoft.PowerShell.Archive

$p = './extensions/Microsoft.PowerShell.Archive/1.2.6'
$e = Get-Content "$p/extension.json" -Raw | ConvertFrom-Json -Depth 30
$h = Get-Content "$p/help.json" -Raw | ConvertFrom-Json -Depth 30
$r = Get-Content "$p/provenance.json" -Raw | ConvertFrom-Json -Depth 30
@($e.commands).Count                 # 2
@($e.commands.name)                  # Compress-Archive, Expand-Archive
@($h.commands).Count                 # 2
$r.registration.moduleWasImportedInIsolatedSidecar # True
$r.registration.exportedCmdletsWereInvoked          # False
```

## Reusable learnings

1. Import-time execution is unavoidable when querying many legacy script
   modules. Treat the sidecar boundary as a deliberate registration/trust
   action, not an AOT-host feature.
2. `ModuleInfo.ExportedCommands` is the correct inventory boundary; inspecting
   `Get-Command` globally would mix in commands from dependencies or sessions.
3. The raw `Get-Help` object has inconsistent/lowercase property shapes,
   particularly when MAML is absent. The registrar reads properties defensively
   and stores a normalized schema.
4. Common parameters are present in the command metadata. A later help renderer
   should recognize and collapse them rather than treating each as bespoke
   command behavior.
5. Contract discovery is independent of execution. That lets future
   `Get-Help Compress-Archive` work as soon as the package is scanned, before a
   legacy bridge or AOT port exists.

## Next action

Repeat the same registration proof with `Microsoft.PowerShell.KubeCtl`, then
compare its third-party-style external-process dependency and any exported
function differences against this baseline.
