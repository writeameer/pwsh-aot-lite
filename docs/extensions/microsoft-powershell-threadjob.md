# Microsoft.PowerShell.ThreadJob registration proof

## Status

**Registered for catalog/help discovery; execution intentionally unimplemented.**

This is the binary-module proof of the legacy registration path. It verifies
that the registrar handles an assembly-backed `CmdletInfo`, rather than only
script functions, without loading that module into the Native AOT host or
invoking its exported command.

## Source observed

- Module: `Microsoft.PowerShell.ThreadJob` version `2.2.0`.
- Module type: `Binary`.
- Manifest:
  `/opt/homebrew/Cellar/powershell/7.6.6_1/libexec/Modules/Microsoft.PowerShell.ThreadJob/Microsoft.PowerShell.ThreadJob.psd1`.
- Root implementation:
  `Microsoft.PowerShell.ThreadJob.dll`.
- Exported command: compiled cmdlet `Start-ThreadJob`.
- Authored help: present; one description and two examples were normalized
  from `Get-Help -Full`.

## What worked

The explicit registrar
[`tools/Register-PwshAotModule.ps1`](../../tools/Register-PwshAotModule.ps1)
created a clean regular-PowerShell sidecar, imported the binary module there,
and emitted:

```text
extensions/Microsoft.PowerShell.ThreadJob/2.2.0/
  extension.json
  help.json
  provenance.json
```

`extension.json` contains one command contract with `commandType: Cmdlet`,
20 parameter contracts, the `FilePath` and `ScriptBlock` parameter sets, and
output type `Microsoft.PowerShell.ThreadJob.ThreadJob`. `help.json` retains
the authored synopsis, 238-character description, and both examples for
`Start-ThreadJob`. `provenance.json` records `moduleType: Binary`, the DLL
source path, an isolated import, and `exportedCmdletsWereInvoked: false`.

The registration output deliberately reports `legacy-pwsh-sidecar` and
`registered-not-invoked`: catalog and help discovery work, but the AOT runtime
does not yet execute this module.

## What did not work, and the minimal fix

The initial registration failed under `Set-StrictMode` because the inspector
read `$Command.ScriptBlock` directly. `FunctionInfo` exposes that property,
but a compiled `CmdletInfo` does not. The generic contract extractor now reads
the optional property through `Get-ObjectPropertyValue`, which identifies
script functions when present and compiled commands when absent. This changes
no module-specific behavior.

After that correction, the Archive script-module registration was re-run as a
regression check and again produced its expected `1.2.6` package with two
commands. This establishes that one guarded metadata access supports both
module shapes.

## Verification actually run

```powershell
pwsh -NoLogo -NoProfile -File ./tools/Register-PwshAotModule.ps1 `
  -Name Microsoft.PowerShell.ThreadJob -ExtensionsRoot ./extensions

pwsh -NoLogo -NoProfile -File ./tools/Register-PwshAotModule.ps1 `
  -Name Microsoft.PowerShell.Archive -ExtensionsRoot ./extensions

$p = './extensions/Microsoft.PowerShell.ThreadJob/2.2.0'
$e = Get-Content "$p/extension.json" -Raw | ConvertFrom-Json -Depth 30
$h = Get-Content "$p/help.json" -Raw | ConvertFrom-Json -Depth 30
$r = Get-Content "$p/provenance.json" -Raw | ConvertFrom-Json -Depth 30
@($e.commands).Count                         # 1
$e.commands[0].name                           # Start-ThreadJob
$e.commands[0].commandType                    # Cmdlet
@($e.commands[0].parameters).Count            # 20
@($h.commands[0].examples).Count               # 2
$r.registration.moduleWasImportedInIsolatedSidecar # True
$r.registration.exportedCmdletsWereInvoked          # False
```

## Reusable learnings

1. Do not assume `CommandInfo` has a common CLR property surface. Binary and
   script exports must be accessed defensively under strict mode.
2. `ModuleInfo.ExportedCommands` plus `Get-Help -Full` gives a useful uniform
   interface for both shapes: static contract fields come from command
   metadata, while human help comes from the help system.
3. A binary module can be discoverable and fully documented without dynamic
   assembly loading in the AOT host. The only assembly load happens in the
   explicit, disposable registration sidecar.
4. The package needs to retain the module type and source DLL identity so a
   future execution bridge can apply a distinct trust/compatibility policy to
   compiled modules.
5. Command registration is not execution conversion. `Start-ThreadJob`
   depends on the PowerShell job engine and dynamic script blocks; a native
   equivalent needs a separate worker/job design.

## Next action

Run the third proof with `Microsoft.PowerShell.KubeCtl` and compare external
process/module-import behavior with the script and binary baselines.
