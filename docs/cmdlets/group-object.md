# Group-Object port notes

## Admitted surface

```powershell
<TextRecord-producing AOT command> | Group-Object -NoElement [-CaseSensitive]
```

The adapter emits closed `Count`, `Name` records. It preserves upstream buffer,
current-culture grouping, and sorted group output for static text only.

- Upstream: `src/Microsoft.PowerShell.Commands.Utility/commands/utility/Group-Object.cs` (`GroupObjectCommand`)
- Generated contract: `GeneratedCmdletPorts.GroupObject`
- Target: `GroupObjectCmdlet`, `GroupTextRecord`, `PipelineValueAdapter`

## Boundary and verification

`NoElement` is required: source `GroupInfo` object lists, property expressions,
hash-table mode, `PSObject`, and dynamic comparison are outside the closed
record boundary. The generic pipeline terminal-shape seam now selects the
downstream adapter's declared columns, preventing a source `Value` shape from
misrepresenting the resulting `Count`, `Name` records.

Managed and fresh osx-arm64 Native AOT self-tests passed. Native probes matched
stock grouping (`2|alpha/beta`; `1|a/A`, `2|b/A`); omitted `NoElement` fails
closed with `AOT6732`.
