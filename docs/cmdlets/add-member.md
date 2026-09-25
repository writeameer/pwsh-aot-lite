# Add-Member boundary note

## Status

Explicitly deferred; no adapter, runtime feature, or migration claim was made.

- Upstream: `src/Microsoft.PowerShell.Commands.Utility/commands/utility/AddMember.cs`
- Generated contract: `GeneratedCmdletPorts.AddMember`

## Retained evidence

`AddMemberCommand.InputObject` is a `PSObject` (lines 36–38).  The source
constructs an ETS member from dynamic parameter choices and mutates object
identity through `_inputObject.Members.Add(member)` (the member construction
and add path, lines 350–484).  Stock PowerShell therefore permits a direct
shape such as:

```powershell
Add-Member -InputObject 'a' -NotePropertyName X -NotePropertyValue y -PassThru |
  Select-Object -ExpandProperty X
```

which outputs `y`.

The AOT target deliberately has immutable, explicit `AotRecord` shapes and no
`PSObject`/ETS adaptation.  Replacing this with a record copy would silently
change object identity, member resolution, downstream mutation, and dynamic
member semantics.  That is not a closed static subset, so it remains an
explicit boundary rather than a fake compatibility adapter.

## Next action

Revisit only with a reviewed, explicitly owned immutable-record transformation
contract; do not add a general ETS or dynamic-member runtime.
