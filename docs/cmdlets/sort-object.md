# Sort-Object port notes

## Admitted surface

```powershell
<TextRecord-producing AOT command> | Sort-Object [-Descending] [-CaseSensitive]
```

The adapter preserves source stable current-culture text ordering over closed
`TextRecord` values. It emits the original text values.

- Upstream: `Sort-Object.cs` and `OrderObjectBase.cs`
- Generated contract: `GeneratedCmdletPorts.SortObject`
- Target: `SortObjectCmdlet` using `AotPipelineInputCmdletBase<TextRecord>`

## Boundary and verification

Property expressions, PSObject ordering, `Unique`, `Top`, `Bottom`, `Stable`,
and generic conversion/comparer routes are excluded. No reflection, ETS, or
dynamic object pipeline is added.

Managed and fresh osx-arm64 Native AOT self-tests passed. The `b/A,a/A,b/A`
corpus matched stock ascending and descending order; `-Property` fails closed
with `AOT2002`. Integrated to `main` at `f4b3661`.
