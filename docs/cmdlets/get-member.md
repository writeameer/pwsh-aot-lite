# Get-Member port notes

## Admitted surface

```powershell
<TextRecord-producing AOT command> | Get-Member -Name Length
```

It emits the source-equivalent static `System.String` `Length` property
definition.

- Upstream: `src/Microsoft.PowerShell.Commands.Utility/commands/utility/GetMember.cs`
- Generated contract: `GeneratedCmdletPorts.GetMember`
- Target: `GetMemberCmdlet` and closed `MemberDefinitionRecord`

## Boundary and verification

The source enumerates `PSObject`/ETS members, types, views, filters, static
members, and formatting metadata. This adapter contains only the statically
known String.Length fact and adds no reflection or member discovery.

Managed and fresh osx-arm64 Native AOT self-tests passed. `abc/x,de/x` input
emitted `System.String | Length | Property | int Length {get;}`; `ToString`
fails closed with `AOT6734`. Integrated to `main` at `acd4232`.
